using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TypeMend;

/// <summary>Result of one repair: the text, and the words the model was unsure about.</summary>
public sealed record Repair(string Text, IReadOnlyList<string> UnsureWords);

/// <summary>
/// Runs the character-level model on the CPU. Has no Dalamud dependency, so the same file
/// is exercised outside the game by the test harness.
/// </summary>
public sealed class Repairer : IDisposable
{
    private const long Bos = 1, Eos = 2;
    private const int FirstChar = 32, LastChar = 126, CharOffset = 29, VocabSize = 98;
    private const int MaxPiece = 300;

    /// <summary>A word is shown as unsure when its least certain letter is below this.</summary>
    public const double UnsureBelow = 0.9;

    private static int nativeRegistered;
    private readonly InferenceSession encoder, decoder;

    public Repairer(string pluginDirectory, int threads)
    {
        RegisterNative(pluginDirectory);
        using var options = new SessionOptions
        {
            IntraOpNumThreads = threads,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        encoder = new InferenceSession(Path.Combine(pluginDirectory, "model", "enc.q.onnx"), options);
        decoder = new InferenceSession(Path.Combine(pluginDirectory, "model", "dec.q.onnx"), options);
    }

    /// <summary>Loads onnxruntime.dll by full path, so a different copy elsewhere on the PC is never picked up.</summary>
    private static void RegisterNative(string directory)
    {
        if (Interlocked.Exchange(ref nativeRegistered, 1) != 0) return;
        NativeLibrary.SetDllImportResolver(typeof(InferenceSession).Assembly, (name, _, _) =>
            name.StartsWith("onnxruntime", StringComparison.OrdinalIgnoreCase)
                ? NativeLibrary.Load(Path.Combine(directory, "onnxruntime.dll"))
                : IntPtr.Zero);
    }

    /// <summary>True when every character is one the model understands.</summary>
    public static bool CanRepair(string text)
    {
        foreach (var c in text)
            if (c < FirstChar || c > LastChar) return false;
        return true;
    }

    public Repair Run(string text, CancellationToken cancel = default)
    {
        var output = new StringBuilder();
        var unsure = new List<string>();
        foreach (var piece in Split(text))
        {
            if (output.Length > 0) output.Append(' ');
            var (fixedPiece, confidence) = Decode(piece, cancel);
            output.Append(fixedPiece);
            CollectUnsure(fixedPiece, confidence, unsure);
        }
        return new Repair(output.ToString(), unsure);
    }

    /// <summary>
    /// Repairs a text while keeping words stretched on purpose exactly as long as they were typed. The whole
    /// sentence is repaired with shortened stretches; if those cannot be matched back, the stretched words stay as typed.
    /// </summary>
    public Repair RunKeepingStretches(string text, CancellationToken cancel = default)
    {
        var stretches = ChatLine.Stretches(text);
        if (stretches.Count == 0) return Run(text, cancel);

        var collapsed = ChatLine.Collapse(text);
        var first = Run(collapsed, cancel);
        var restored = ChatLine.RestoreStretches(first.Text, stretches, collapsed);
        if (restored != null)
        {
            var marked = first.UnsureWords.ToHashSet();
            var words = restored.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new Repair(restored, words.Where(w => marked.Contains(w) || marked.Contains(ChatLine.Collapse(w))).ToList());
        }

        return Around(ChatLine.SplitStretched(text), Run, cancel);
    }

    /// <summary>
    /// Repairs a text and leaves the writer's never-change words alone. The whole sentence is repaired first, for its
    /// context; if the model altered one of those words, the text is repaired around them instead.
    /// </summary>
    public Repair RunKeeping(string text, IReadOnlyCollection<string> keep, CancellationToken cancel = default)
    {
        var pattern = ChatLine.KeepPattern(keep);
        var typed = pattern?.Matches(text).Select(m => m.Value).ToList();
        if (pattern == null || typed == null || typed.Count == 0) return RunKeepingStretches(text, cancel);

        var first = RunKeepingStretches(text, cancel);
        if (pattern.Matches(first.Text).Select(m => m.Value).SequenceEqual(typed))
            return new Repair(first.Text, first.UnsureWords.Where(w => !typed.Contains(w)).ToList());
        return Around(pattern.Split(text), RunKeepingStretches, cancel);
    }

    /// <summary>Repairs the even positions of a split text; odd positions stay exactly as they are.</summary>
    private static Repair Around(string[] parts, Func<string, CancellationToken, Repair> inner, CancellationToken cancel)
    {
        var output = new StringBuilder();
        var unsure = new List<string>();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1 || string.IsNullOrWhiteSpace(parts[i]))
            {
                output.Append(parts[i]);
                continue;
            }
            var repair = inner(parts[i], cancel);
            if (parts[i].StartsWith(' ')) output.Append(' ');
            output.Append(repair.Text);
            if (parts[i].EndsWith(' ')) output.Append(' ');
            unsure.AddRange(repair.UnsureWords);
        }
        return new Repair(output.ToString(), unsure);
    }

    /// <summary>Cuts long messages at spaces; the model is trained on up to about 370 letters.</summary>
    private static IEnumerable<string> Split(string text)
    {
        var rest = text.Trim();
        while (rest.Length > MaxPiece)
        {
            var cut = rest.LastIndexOf(' ', MaxPiece);
            if (cut < 20) cut = MaxPiece;
            yield return rest[..cut];
            rest = rest[cut..].TrimStart();
        }
        if (rest.Length > 0) yield return rest;
    }

    private (string Text, List<double> Confidence) Decode(string piece, CancellationToken cancel)
    {
        var input = new long[piece.Length];
        for (var i = 0; i < piece.Length; i++) input[i] = piece[i] - CharOffset;

        using var encoded = encoder.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("x", new DenseTensor<long>(input, new[] { 1, input.Length })),
        });
        var memory = encoded.First().AsTensor<float>();

        var ids = new List<long> { Bos };
        var confidence = new List<double>();
        var text = new StringBuilder();
        var limit = piece.Length + 6 + (piece.Length / 4);
        var finished = false;
        for (var step = 0; step < limit; step++)
        {
            cancel.ThrowIfCancellationRequested();
            using var result = decoder.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("y", new DenseTensor<long>(ids.ToArray(), new[] { 1, ids.Count })),
                NamedOnnxValue.CreateFromTensor("m", memory),
            });
            var scores = result.First().AsEnumerable<float>().ToArray();
            var (best, probability) = Top(scores);
            if (best == Eos)
            {
                finished = true;
                break;
            }
            ids.Add(best);
            confidence.Add(probability);
            text.Append((char)(best + CharOffset));
        }

        // A model that never stops is repeating itself: keep the writer's own text, marked as unsure.
        if (!finished) return (piece, Enumerable.Repeat(0.0, piece.Length).ToList());
        return (text.ToString(), confidence);
    }

    private static (int Index, double Probability) Top(float[] scores)
    {
        var best = 0;
        for (var i = 1; i < VocabSize; i++)
            if (scores[i] > scores[best]) best = i;
        double sum = 0;
        for (var i = 0; i < VocabSize; i++) sum += Math.Exp(scores[i] - scores[best]);
        return (best, 1 / sum);
    }

    private static void CollectUnsure(string text, List<double> confidence, List<string> unsure)
    {
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != ' ') continue;
            if (i > start)
            {
                var lowest = double.MaxValue;
                for (var k = start; k < i; k++) lowest = Math.Min(lowest, confidence[k]);
                if (lowest < UnsureBelow) unsure.Add(text[start..i]);
            }
            start = i + 1;
        }
    }

    public void Dispose()
    {
        encoder.Dispose();
        decoder.Dispose();
    }
}
