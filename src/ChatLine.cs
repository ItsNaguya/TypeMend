using System.Text.RegularExpressions;

namespace TypeMend;

/// <summary>Works out which part of a chat line is the writer's own words.</summary>
public static partial class ChatLine
{
    private static readonly HashSet<string> Channels = new(StringComparer.OrdinalIgnoreCase)
    {
        "/s", "/say", "/y", "/yell", "/sh", "/shout", "/p", "/party", "/a", "/alliance",
        "/fc", "/freecompany", "/n", "/novice", "/em", "/emote",
        "/r", "/reply",
        "/l1", "/l2", "/l3", "/l4", "/l5", "/l6", "/l7", "/l8",
        "/linkshell1", "/linkshell2", "/linkshell3", "/linkshell4", "/linkshell5", "/linkshell6", "/linkshell7", "/linkshell8",
        "/cwl1", "/cwl2", "/cwl3", "/cwl4", "/cwl5", "/cwl6", "/cwl7", "/cwl8",
        "/cwlinkshell1", "/cwlinkshell2", "/cwlinkshell3", "/cwlinkshell4", "/cwlinkshell5", "/cwlinkshell6", "/cwlinkshell7", "/cwlinkshell8",
    };

    private static readonly HashSet<string> Tells = new(StringComparer.OrdinalIgnoreCase) { "/t", "/tell" };

    /// <summary>
    /// Splits a line into the part to leave alone (channel, tell target) and the words to repair.
    /// False means the line is a command or has nothing to repair, so it must go out untouched.
    /// </summary>
    public static bool TrySplit(string line, out string prefix, out string body)
    {
        prefix = string.Empty;
        body = line;
        if (string.IsNullOrWhiteSpace(line)) return false;

        if (line[0] == '/')
        {
            var parts = line.Split(' ', StringSplitOptions.None);
            var command = parts[0];
            int kept;
            if (Channels.Contains(command)) kept = 1;
            else if (Tells.Contains(command)) kept = parts.Length > 1 && parts[1].StartsWith('<') ? 2 : 3;
            else return false;

            if (parts.Length <= kept) return false;
            prefix = string.Join(' ', parts.Take(kept)) + " ";
            body = string.Join(' ', parts.Skip(kept));
        }

        return body.Any(char.IsLetter);
    }

    /// <summary>
    /// Roleplay words kept exactly as typed when spelled like this. Measured 2026-11-11 on made-up roleplay lines:
    /// without the list the model changed 5 of 30 correctly typed lines ("crop" to "crap", "aftercare" to "after care").
    /// </summary>
    public static readonly string[] RoleplayWords = { "aftercare", "safeword", "safewords", "crop", "thigh", "thighs", "collar", "collared", "leash", "gag", "gagged", "cuffs", "cuffed", "rope", "ropes", "shibari", "blindfold", "blindfolded", "restraints", "restrained", "kneel", "kneels", "kneeling", "whimper", "whimpers", "squirm", "squirms", "spank", "spanks", "spanking", "Mistress", "Master", "Sir", "Daddy", "Mommy", "pet", "pup", "puppy", "kitten", "brat", "bratty", "subspace", "dom", "domme", "sub", "switch", "ooc", "brb", "afk", "mmph", "mmm", "nngh", "hnng", "mhm", "purr", "purrs", "nuzzle", "nuzzles", "smirk", "smirks" };

    /// <summary>
    /// Short chat words always kept exactly as typed. Measured 2026-11-14: with no context the model turns some of them
    /// into common words ("ok" to "of", "ty" to "they", "np" to "no").
    /// </summary>
    public static readonly string[] ChatWords = { "ok", "okay", "okk", "k", "kk", "yep", "yup", "nope", "ty", "thx", "np", "gn", "gm", "gg", "brb", "afk", "omg", "lol", "lmao", "idk", "ikr", "tbh", "ngl", "imo", "btw", "aint", "oops" };

    /// <summary>Splits text around game placeholders like &lt;t&gt; and roleplay marks (~, ((, )), which stay as typed. Odd positions are the kept parts.</summary>
    public static string[] SplitPlaceholders(string body) => Placeholder().Split(body);

    /// <summary>Reads the writer's never-change list, separated by spaces, commas or lines.</summary>
    public static List<string> ParseKeepWords(string text) =>
        text.Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Matches whole words from the never-change list, in any letter case. Null for an empty list.</summary>
    public static Regex? KeepPattern(IReadOnlyCollection<string> words) => words.Count == 0
        ? null
        : new Regex(@"(?<![\p{L}\p{N}'])(" + string.Join('|', words.Select(Regex.Escape)) + @")(?![\p{L}\p{N}'])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The stretches (four or more of one letter in a row) in a text, in order.</summary>
    public static List<string> Stretches(string text) => Stretch().Matches(text).Select(m => m.Value).ToList();

    /// <summary>Shortens every stretch to three letters, the form the model can read.</summary>
    public static string Collapse(string text) => Stretch().Replace(text, m => new string(m.Value[0], 3));

    /// <summary>
    /// Puts the typed stretches back into repaired text. Null when the model did not keep them one for one,
    /// or changed the letters of a stretched word (it may only move a letter across a space).
    /// </summary>
    public static string? RestoreStretches(string repaired, List<string> stretches, string collapsedTyped)
    {
        var found = Shortened().Matches(repaired);
        if (found.Count != stretches.Count) return null;
        var typed = collapsedTyped.Replace(" ", string.Empty);
        for (var i = 0; i < found.Count; i++)
        {
            if (char.ToLowerInvariant(found[i].Value[0]) != char.ToLowerInvariant(stretches[i][0])) return null;
            int from = found[i].Index, to = from + found[i].Length;
            while (from > 0 && char.IsLetter(repaired[from - 1])) from--;
            while (to < repaired.Length && char.IsLetter(repaired[to])) to++;
            if (!typed.Contains(repaired[from..to], StringComparison.OrdinalIgnoreCase)) return null;
        }
        var next = 0;
        return Shortened().Replace(repaired, _ => stretches[next++]);
    }

    /// <summary>Splits text around whole words that contain a stretch. Odd positions are those words.</summary>
    public static string[] SplitStretched(string text) => StretchedWord().Split(text);

    [GeneratedRegex(@"(<[^<>\s]+>|~+|\(\(+|\)\)+)")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"([a-z])\1{3,}", RegexOptions.IgnoreCase)]
    private static partial Regex Stretch();

    [GeneratedRegex(@"([a-z])\1{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex Shortened();

    [GeneratedRegex(@"(\S*(?:a{4}|b{4}|c{4}|d{4}|e{4}|f{4}|g{4}|h{4}|i{4}|j{4}|k{4}|l{4}|m{4}|n{4}|o{4}|p{4}|q{4}|r{4}|s{4}|t{4}|u{4}|v{4}|w{4}|x{4}|y{4}|z{4})\S*)", RegexOptions.IgnoreCase)]
    private static partial Regex StretchedWord();
}
