using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace TypeMend;

/// <summary>The writer's own lists: words the repair must leave alone, and the corrections it learned from them.</summary>
public sealed class SettingsWindow
{
    private readonly Configuration configuration;
    private readonly CorrectionMemory memory;
    private readonly Action save;
    private string words;
    private bool isOpen;

    public SettingsWindow(Configuration configuration, CorrectionMemory memory, Action save)
    {
        this.configuration = configuration;
        this.memory = memory;
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
            ImGui.InputTextMultiline("##never-change", ref words, 4000, new Vector2(420, 120));
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                configuration.NeverChange = words;
                save();
            }
            var roleplay = configuration.KeepRoleplayWords;
            if (ImGui.Checkbox("Also keep common roleplay words (collar, safeword, aftercare, ...)", ref roleplay))
            {
                configuration.KeepRoleplayWords = roleplay;
                save();
            }

            ImGui.Spacing();
            ImGui.Separator();
            DrawLearned();
        }
        ImGui.End();
    }

    private void DrawLearned()
    {
        ImGui.PushTextWrapPos(420);
        ImGui.TextUnformatted("Learned from your fixes");
        ImGui.TextDisabled($"When you fix a word in the preview, TypeMend remembers it. After the same fix {CorrectionMemory.NeededConfirmations} times it makes it for you. Stays on this PC.");
        ImGui.PopTextWrapPos();

        var entries = memory.Entries();
        if (entries.Count == 0)
        {
            ImGui.TextDisabled("Nothing learned yet.");
            return;
        }

        if (ImGui.BeginChild("##learned", new Vector2(420, Math.Min(200, 26 * entries.Count + 8)), true))
        {
            foreach (var (from, to, count) in entries)
            {
                if (ImGui.SmallButton($"Forget##{from}"))
                {
                    memory.Forget(from);
                    save();
                }
                ImGui.SameLine();
                var state = count >= CorrectionMemory.NeededConfirmations ? "in use" : $"seen {count} of {CorrectionMemory.NeededConfirmations}";
                ImGui.TextUnformatted($"{from}  ->  {to}   ({state})");
            }
        }
        ImGui.EndChild();

        if (ImGui.Button("Forget everything learned"))
        {
            memory.ForgetAll();
            save();
        }
    }
}
