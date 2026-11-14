namespace TypeMend;

/// <summary>
/// The click-to-fix menu's content for one guessed word: the letters that were typed for it and the model's other
/// repairs of that spot. Same steps as the web page's openFix (app.js) and align.js. No Dalamud dependency, so the
/// test harness can run it.
/// </summary>
public static class Suggestions
{
    public sealed record Result(string? Typed, IReadOnlyList<string> Picks);

    /// <param name="typed">What was typed (the chat text without its channel).</param>
    /// <param name="repaired">The repair as it is shown now.</param>
    /// <param name="start">Where the guessed word starts in <paramref name="repaired"/>.</param>
    /// <param name="word">The guessed word.</param>
    /// <param name="known">Real words: suggestions must be made of these, typed words or never-change words.</param>
    /// <param name="keep">The never-change words.</param>
    public static Result For(Repairer model, string typed, string repaired, int start, string word,
        IReadOnlySet<string> known, IEnumerable<string> keep, CancellationToken cancel = default)
    {
        var span = Align.TypedFor(typed, repaired, start, start + word.Length);
        string? typedPart = span is { } s ? typed[s.From..s.To] : null;

        // The model sees the typed words around this spot too; only the part of each alternative that lines up
        // with the typed letters is offered.
        string window = word;
        int s0 = 0, e0 = 0;
        if (span is { } sp)
        {
            int ws = Math.Max(0, sp.From - 25), we = Math.Min(typed.Length, sp.To + 25);
            while (ws > 0 && !char.IsWhiteSpace(typed[ws - 1])) ws--;
            while (we < typed.Length && !char.IsWhiteSpace(typed[we])) we++;
            window = typed[ws..we];
            s0 = sp.From - ws;
            e0 = sp.To - ws;
        }
        if (window.Length > 90 || !Repairer.CanRepair(window)) return new Result(typedPart, Array.Empty<string>());

        var typedWords = Tokens(typed).ToHashSet();
        var keepWords = keep.SelectMany(Tokens).ToHashSet();
        var picks = new List<string>();
        foreach (var found in model.Candidates(window, 6, cancel))
        {
            var c = found;
            if (span != null)
            {
                if (Align.TypedFor(found, window, s0, e0) is not { } r) continue;
                int a0 = r.From, a1 = r.To;
                while (a0 > 0 && !char.IsWhiteSpace(found[a0 - 1])) a0--;
                while (a1 < found.Length && !char.IsWhiteSpace(found[a1])) a1++;
                c = found[a0..a1];
            }
            if (Parts(c) != Parts(word)) continue;   // one word gets one-word suggestions
            if (c.Length == 0 || c == word || c == typedPart || picks.Contains(c)) continue;
            if (!Acceptable(c, typedPart, known, typedWords, keepWords)) continue;
            picks.Add(c);
            if (picks.Count == 3) break;
        }
        return new Result(typedPart, picks);
    }

    private static bool Acceptable(string candidate, string? typedPart, IReadOnlySet<string> known, HashSet<string> typedWords, HashSet<string> keepWords)
    {
        foreach (var w in Tokens(candidate))
        {
            if (SwearWords.Words.Contains(w, StringComparer.OrdinalIgnoreCase))
            {
                // A swear word is offered only when the typed letters are close to it ("nitht" never becomes "bitch").
                if (!Tokens(typedPart ?? string.Empty).Any(x => Align.Near(x, w))) return false;
                continue;
            }
            if (!known.Contains(w) && !typedWords.Contains(w) && !keepWords.Contains(w)) return false;
        }
        return true;
    }

    private static int Parts(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static IEnumerable<string> Tokens(string text)
    {
        var word = new System.Text.StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or '\'') word.Append(ch);
            else if (word.Length > 0) { yield return word.ToString(); word.Clear(); }
        }
        if (word.Length > 0) yield return word.ToString();
    }
}

/// <summary>Lines up typed and repaired letters. Same steps as the web page's align.js.</summary>
public static class Align
{
    /// <summary>
    /// Which part of the typed text became repaired[start..end]. Letters are compared without spaces (the model moves
    /// spaces), in lower case, in a window around the word. Null when nothing lines up.
    /// </summary>
    public static (int From, int To)? TypedFor(string typed, string repaired, int start, int end)
    {
        var T = new List<int>();
        var R = new List<int>();
        for (var i = 0; i < typed.Length; i++) if (!char.IsWhiteSpace(typed[i])) T.Add(i);
        for (var i = 0; i < repaired.Length; i++) if (!char.IsWhiteSpace(repaired[i])) R.Add(i);
        var a = R.FindIndex(i => i >= start);
        if (a < 0 || R[a] >= end || T.Count == 0) return null;
        var b = a;
        while (b + 1 < R.Count && R[b + 1] < end) b++;

        const int W = 60;
        var ratio = (double)T.Count / Math.Max(1, R.Count);
        int r0 = Math.Max(0, a - W), r1 = Math.Min(R.Count, b + W + 1);
        int t0 = Math.Max(0, (int)Math.Floor(r0 * ratio) - 20), t1 = Math.Min(T.Count, (int)Math.Ceiling(r1 * ratio) + 20);
        var rs = R.GetRange(r0, r1 - r0).Select(i => char.ToLowerInvariant(repaired[i])).ToArray();
        var ts = T.GetRange(t0, t1 - t0).Select(i => char.ToLowerInvariant(typed[i])).ToArray();
        int n = rs.Length, m = ts.Length;

        // Edit distance, free to start and end anywhere in the typed window (it is cut wider than the repair window).
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var i = 1; i <= n; i++)
            for (var j = 1; j <= m; j++)
                d[i, j] = Math.Min(d[i - 1, j - 1] + (rs[i - 1] == ts[j - 1] ? 0 : 1), Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1));
        var best = 0;
        for (var k = 1; k <= m; k++) if (d[n, k] < d[n, best]) best = k;

        var hit = new List<int>();
        for (int i = n, j = best; i > 0 && j > 0;)
        {
            if (d[i, j] == d[i - 1, j - 1] + (rs[i - 1] == ts[j - 1] ? 0 : 1))
            {
                if (i - 1 >= a - r0 && i - 1 <= b - r0) hit.Add(j - 1);
                i--; j--;
            }
            else if (d[i, j] == d[i - 1, j] + 1) i--;
            else j--;
        }
        if (hit.Count == 0) return null;
        return (T[t0 + hit.Min()], T[t0 + hit.Max()] + 1);
    }

    /// <summary>True when b is a with at most two letters dropped, added, changed or swapped.</summary>
    public static bool Near(string a, string b)
    {
        int n = a.Length, m = b.Length;
        if (Math.Abs(n - m) > 2) return false;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;
        for (var i = 1; i <= n; i++)
            for (var j = 1; j <= m; j++)
            {
                d[i, j] = Math.Min(d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1), Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1));
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[n, m] <= 2;
    }
}
