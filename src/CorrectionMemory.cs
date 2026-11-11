using System.Text.RegularExpressions;

namespace TypeMend;

/// <summary>One learned correction: the word to write instead, and how often the writer made that fix.</summary>
[Serializable]
public sealed class LearnedWord
{
    public string To { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// Learns from the words the writer fixes in the preview before sending. A correction is applied on its own
/// only after the same fix was made twice, so a one-off rewording is never taken as a rule. Everything stays
/// in the plugin's settings on the writer's PC. No Dalamud dependency, so the test harness can run it.
/// </summary>
public sealed partial class CorrectionMemory
{
    public const int NeededConfirmations = 2;

    private readonly Dictionary<string, LearnedWord> words;
    private readonly object gate = new();

    public CorrectionMemory(Dictionary<string, LearnedWord> words) => this.words = words;

    /// <summary>Replaces words with their learned corrections once those are confirmed.</summary>
    public string Apply(string text)
    {
        lock (gate)
        {
            if (words.Count == 0) return text;
            return Word().Replace(text, m =>
                words.TryGetValue(m.Value.ToLowerInvariant(), out var learned) && learned.Count >= NeededConfirmations
                    ? MatchCase(m.Value, learned.To)
                    : m.Value);
        }
    }

    /// <summary>
    /// Compares the repair that was shown with what the writer sent and learns single-word fixes that look like
    /// spelling fixes, not new wording. A word changed back to what the model wrote unlearns that correction.
    /// Returns true when anything changed.
    /// </summary>
    public bool Learn(string shown, string sent)
    {
        var changed = false;
        foreach (var (from, to) in Fixes(Tokens(shown), Tokens(sent)))
        {
            var key = from.ToLowerInvariant();
            lock (gate)
            {
                if (words.TryGetValue(to.ToLowerInvariant(), out var earlier) && earlier.To.Equals(from, StringComparison.OrdinalIgnoreCase))
                {
                    words.Remove(to.ToLowerInvariant());
                    changed = true;
                    continue;
                }
                if (words.TryGetValue(key, out var learned) && learned.To.Equals(to, StringComparison.OrdinalIgnoreCase))
                    learned.Count++;
                else
                    words[key] = new LearnedWord { To = to, Count = 1 };
                changed = true;
            }
        }
        return changed;
    }

    public List<(string From, string To, int Count)> Entries()
    {
        lock (gate) return words.OrderBy(w => w.Key).Select(w => (w.Key, w.Value.To, w.Value.Count)).ToList();
    }

    public void Forget(string from)
    {
        lock (gate) words.Remove(from);
    }

    public void ForgetAll()
    {
        lock (gate) words.Clear();
    }

    private static List<string> Tokens(string text) => Word().Matches(text).Select(m => m.Value).ToList();

    /// <summary>Pairs of words replaced one for one, found by lining up both texts word by word.</summary>
    private static IEnumerable<(string From, string To)> Fixes(List<string> a, List<string> b)
    {
        var n = a.Count;
        var m = b.Count;
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lcs[i, j] = Same(a[i], b[j]) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        int x = 0, y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && Same(a[x], b[y]))
            {
                x++;
                y++;
                continue;
            }
            int startA = x, startB = y;
            while ((x < n || y < m) && !(x < n && y < m && Same(a[x], b[y])))
            {
                if (y >= m || (x < n && lcs[x + 1, y] >= lcs[x, y + 1])) x++;
                else y++;
            }
            // only stretches with as many words on both sides, and at most three: those are fixes, not rewrites
            if (x - startA == y - startB && x - startA <= 3)
                for (var k = 0; k < x - startA; k++)
                    if (LooksLikeSpellingFix(a[startA + k], b[startB + k]))
                        yield return (a[startA + k], b[startB + k]);
        }
    }

    private static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSpellingFix(string from, string to)
    {
        if (from.Length < 2 || to.Length < 2 || Same(from, to)) return false;
        var allowed = Math.Max(1, Math.Min(3, Math.Max(from.Length, to.Length) / 2));
        return Distance(from.ToLowerInvariant(), to.ToLowerInvariant()) <= allowed;
    }

    /// <summary>Edit distance counting a swap of neighbouring letters as one edit.</summary>
    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    private static string MatchCase(string original, string replacement) =>
        original.Length > 0 && char.IsUpper(original[0]) && replacement.Length > 0
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;

    [GeneratedRegex(@"[A-Za-z']+")]
    private static partial Regex Word();
}
