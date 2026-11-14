using System.Text.RegularExpressions;

namespace TypeMend;

/// <summary>Repairs typos in swear words before the model runs. Same steps as the web page's fixSwearTypos.</summary>
public static partial class SwearWords
{
    private static readonly string[] Repairable = Words.Where(w => w.Length >= MinLength && w.All(c => c is >= 'a' and <= 'z')).ToArray();
    private static readonly HashSet<string> Known = new(Words, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Turns a word one typo away from exactly one swear word of 5+ letters into that swear word ("bastrad" to
    /// "bastard"). Real words ("witch"), swear words themselves, the writer's never-change words and capitalised
    /// words after the first (likely names) stay as typed. Returns the text and the words it changed.
    /// </summary>
    public static (string Text, List<string> Fixed) FixTypos(string text, IReadOnlyCollection<string> keep)
    {
        var fixedWords = new List<string>();
        var keepSet = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
        var first = true;
        var result = Word().Replace(text, m =>
        {
            var typed = m.Value;
            var isFirst = first;
            first = false;
            var lower = typed.ToLowerInvariant();
            if (lower.Length < MinLength - 1 || Known.Contains(lower) || RealNear.Contains(lower) || keepSet.Contains(lower)) return typed;
            var capitalised = char.IsUpper(typed[0]) && typed.Skip(1).Any(char.IsLower);
            if (capitalised && !isFirst) return typed;
            var matches = Repairable.Where(w => OneTypo(lower, w)).ToList();
            // Close to two: a dropped, added or swapped letter beats a changed one ("scheise" is "scheisse", not "scheiss").
            if (matches.Count > 1)
                matches = matches.Where(w => w.Length != lower.Length || string.Concat(w.Order()) == string.Concat(lower.Order())).ToList();
            if (matches.Count != 1) return typed;   // none, or still close to two: leave it to the model
            var match = matches[0];
            var output = typed.All(c => !char.IsLower(c)) && typed.Length > 1 ? match.ToUpperInvariant()
                : char.IsUpper(typed[0]) ? char.ToUpperInvariant(match[0]) + match[1..] : match;
            fixedWords.Add(output);
            return output;
        });
        return (result, fixedWords);
    }

    /// <summary>True when b is a with one letter dropped, added, changed, or two neighbours swapped.</summary>
    public static bool OneTypo(string a, string b)
    {
        if (a == b) return false;
        if (a.Length == b.Length)
        {
            var i = 0;
            while (a[i] == b[i]) i++;
            if (a.AsSpan(i + 1).SequenceEqual(b.AsSpan(i + 1))) return true;
            return i + 1 < a.Length && a[i] == b[i + 1] && a[i + 1] == b[i] && a.AsSpan(i + 2).SequenceEqual(b.AsSpan(i + 2));
        }
        if (Math.Abs(a.Length - b.Length) != 1) return false;
        var (shorter, longer) = a.Length < b.Length ? (a, b) : (b, a);
        var k = 0;
        while (k < shorter.Length && shorter[k] == longer[k]) k++;
        return shorter.AsSpan(k).SequenceEqual(longer.AsSpan(k + 1));
    }

    [GeneratedRegex(@"(?<![\p{L}\p{N}'])[A-Za-z]+(?![\p{L}\p{N}'])")]
    private static partial Regex Word();
}
