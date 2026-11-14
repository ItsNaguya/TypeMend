using Dalamud.Configuration;

namespace TypeMend;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;

    /// <summary>The writer's own names and game words that must stay as typed, separated by spaces, commas or lines.</summary>
    public string NeverChange { get; set; } = string.Empty;

    /// <summary>Also keep the built-in roleplay words (collar, safeword, aftercare, ...) when typed exactly.</summary>
    public bool KeepRoleplayWords { get; set; } = true;

    /// <summary>Keep the built-in English and German swear words when typed exactly, and repair their typos.</summary>
    public bool KeepSwearWords { get; set; } = true;

    /// <summary>A line the repair leaves unchanged, with no guesses, goes out without the preview window.</summary>
    public bool SendCleanRightAway { get; set; }

    /// <summary>Shows the short how-to line under the preview buttons.</summary>
    public bool ShowTips { get; set; } = true;

    /// <summary>The welcome note was read; the main window no longer opens by itself on load.</summary>
    public bool SeenWelcome { get; set; }

    /// <summary>Corrections learned from the writer's own fixes in the preview, keyed by the word the model wrote.</summary>
    public Dictionary<string, LearnedWord> Corrections { get; set; } = new();
}
