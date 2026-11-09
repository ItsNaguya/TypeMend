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

    /// <summary>Splits text around game placeholders like &lt;t&gt;. Odd positions are placeholders.</summary>
    public static string[] SplitPlaceholders(string body) => Placeholder().Split(body);

    [GeneratedRegex(@"(<[^<>\s]+>)")]
    private static partial Regex Placeholder();
}
