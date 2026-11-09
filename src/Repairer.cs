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
