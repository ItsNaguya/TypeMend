using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace TypeMend;

/// <summary>Lets the writer list names and game words the repair must leave alone.</summary>
public sealed class SettingsWindow
{
    private readonly Configuration configuration;
    private readonly Action save;
    private string words;
    private bool isOpen;

    public SettingsWindow(Configuration configuration, Action save)
    {
        this.configuration = configuration;
        this.save = save;
        words = configuration.NeverChange;
    }

    public void Open() => isOpen = true;

    public void Draw()
    {
        if (!isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(440, 0), ImGuiCond.Appearing);
        if (ImGui.Begin("TypeMend settings###typemend-settings", ref isOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.PushTextWrapPos(420);
            ImGui.TextUnformatted("Words that should never change");
            ImGui.TextDisabled("Names and game words, one per line. They stay exactly as you typed them when spelled like this.");
            ImGui.PopTextWrapPos();
            ImGui.InputTextMultiline("##never-change", ref words, 4000, new Vector2(420, 160));
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                configuration.NeverChange = words;
                save();
            }
        }
        ImGui.End();
    }
}
