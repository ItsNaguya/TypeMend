using Dalamud.Configuration;

namespace TypeMend;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;

    /// <summary>The writer's own names and game words that must stay as typed, separated by spaces, commas or lines.</summary>
    public string NeverChange { get; set; } = string.Empty;

    /// <summary>Corrections learned from the writer's own fixes in the preview, keyed by the word the model wrote.</summary>
    public Dictionary<string, LearnedWord> Corrections { get; set; } = new();
}
