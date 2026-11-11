using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace TypeMend;

/// <summary>TypeMend's main window: on/off, the writer's own lists, and the corrections it learned from them.</summary>
public sealed class SettingsWindow
{
    public enum Tab { General, NeverChange, Learned }

    private readonly Configuration configuration;
    private readonly CorrectionMemory memory;
    private readonly Action save;
    private readonly Func<string> modelState;
    private string words;
    private string filter = string.Empty;
    private bool isOpen, confirmForgetAll;
    private Tab? select;

    public SettingsWindow(Configuration configuration, CorrectionMemory memory, Action save, Func<string> modelState)
    {
        this.configuration = configuration;
        this.memory = memory;
        this.save = save;
        this.modelState = modelState;
        words = configuration.NeverChange;
    }

    public void Open() => Open(Tab.General);

    public void Open(Tab tab)
    {
        isOpen = true;
        select = tab;
    }

    public void Draw()
    {
        if (!isOpen) return;

        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSize(new Vector2(520, 480) * scale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(420, 340) * scale, new Vector2(1400, 1400) * scale);
        Theme.PushWindowStyle(scale);
        if (ImGui.Begin("TypeMend###typemend-settings", ref isOpen, ImGuiWindowFlags.NoCollapse))
        {
            if (ImGui.BeginTabBar("##tabs"))
            {
                if (TabItem("General###general", Tab.General))
                {
                    DrawGeneral(scale);
                    ImGui.EndTabItem();
                }
                if (TabItem("Never change###never", Tab.NeverChange))
                {
                    DrawNeverChange();
                    ImGui.EndTabItem();
                }
                var count = memory.Entries().Count;
                if (TabItem(count > 0 ? $"Learned ({count})###learned" : "Learned###learned", Tab.Learned))
                {
                    DrawLearned(scale);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            select = null;
        }
        ImGui.End();
        Theme.PopWindowStyle();
        if (!isOpen) CommitWords();
    }

    private bool TabItem(string label, Tab tab) =>
        ImGui.BeginTabItem(label, select == tab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None);

    private void DrawGeneral(float scale)
    {
        ImGui.Spacing();
        if (!configuration.SeenWelcome)
        {
            Theme.Panel("##welcome", Theme.RepairBack, () =>
            {
                ImGui.TextColored(Theme.Accent, "Welcome to TypeMend");
                Theme.Wrapped(ImGui.GetStyle().Colors[(int)ImGuiCol.Text],
                    "Type in chat as you always do. Before a message goes out, a small window shows what you typed and the repair. " +
                    "You choose what is sent. It all runs on this PC; nothing leaves it.");
                if (ImGui.SmallButton("Got it"))
                {
                    configuration.SeenWelcome = true;
                    save();
                }
            });
            ImGui.Spacing();
        }

        // On/off with a clear state line.
        var enabled = configuration.Enabled;
        if (ImGui.Checkbox("Repair my chat messages", ref enabled))
        {
            configuration.Enabled = enabled;
            save();
        }
        ImGui.SameLine();
        ImGui.TextColored(enabled ? Theme.Good : Theme.Muted, enabled ? "On" : "Off: messages go out as typed");
        var state = modelState();
        ImGui.TextColored(state.StartsWith("Ready") ? Theme.Muted : state.StartsWith("Loading") ? Theme.Unsure : Theme.Problem, "Repair model: " + state);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var roleplay = configuration.KeepRoleplayWords;
        if (ImGui.Checkbox("Keep common roleplay words as typed", ref roleplay))
        {
            configuration.KeepRoleplayWords = roleplay;
            save();
        }
        Indented("Words like collar, safeword, aftercare, kitten or brat stay exactly as you wrote them. Marks like ~ and (( )) always do.");

        var tips = configuration.ShowTips;
        if (ImGui.Checkbox("Show tips in the preview window", ref tips))
        {
            configuration.ShowTips = tips;
            save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(Theme.Accent, "How it works");
        Step("1", "Type and press Enter as usual. Commands, macros and emotes are never touched.");
        Step("2", "Check the repair. Amber, underlined words are guesses.");
        Step("3", "Press Enter to send the repair, or pick \"Send as typed\". Esc sends nothing.");
        Step("4", "Fix a wrong word in the box first and TypeMend learns it after the same fix twice.");

        ImGui.Spacing();
        ImGui.TextColored(Theme.Accent, "Chat commands");
        Command("/typemend", "open this window");
        Command("/typemend on  |  off", "turn repairs on or off");
        Command("/typemend words", "open your never-change list");
    }

    private void DrawNeverChange()
    {
        ImGui.Spacing();
        Theme.Hint("Names, places and game words that must stay exactly as you typed them, one per line (or separated by commas). Letter case does not matter for finding them.");
        ImGui.Spacing();
        ImGui.InputTextMultiline("##never-change", ref words, 4000, new Vector2(-1, -ImGui.GetFrameHeightWithSpacing() - 4));
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitWords();
        var count = ChatLine.ParseKeepWords(words).Count;
        ImGui.TextColored(Theme.Muted, count == 1 ? "1 word. Saved when you click outside the box." : $"{count} words. Saved when you click outside the box.");
    }

    private void DrawLearned(float scale)
    {
        ImGui.Spacing();
        Theme.Hint($"When you fix a word in the preview, TypeMend remembers it. After the same fix {CorrectionMemory.NeededConfirmations} times it makes that fix for you. Changing it back makes it forget. Stays on this PC.");
        ImGui.Spacing();

        var entries = memory.Entries();
        if (entries.Count == 0)
        {
            Theme.Panel("##none", Theme.TypedBack, () => ImGui.TextColored(Theme.Muted, "Nothing learned yet. Fix a word in the preview window to teach TypeMend."));
            return;
        }

        ImGui.SetNextItemWidth(220 * scale);
        ImGui.InputTextWithHint("##filter", "Search", ref filter, 100);
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize("Forget everything").X - ImGui.GetStyle().FramePadding.X * 2);
        if (confirmForgetAll)
        {
            ImGui.NewLine();
            ImGui.TextColored(Theme.Problem, $"Forget all {entries.Count} learned fixes?");
            ImGui.SameLine();
            if (ImGui.SmallButton("Yes, forget them"))
            {
                memory.ForgetAll();
                save();
                confirmForgetAll = false;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("No")) confirmForgetAll = false;
        }
        else if (ImGui.Button("Forget everything")) confirmForgetAll = true;

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##learned", 4, flags, new Vector2(-1, -1))) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("TypeMend wrote", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("You fixed it to", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 90 * scale);
        ImGui.TableSetupColumn("##forget", ImGuiTableColumnFlags.WidthFixed, 60 * scale);
        ImGui.TableHeadersRow();

        foreach (var (from, to, count) in entries)
        {
            if (filter.Length > 0 && !from.Contains(filter, StringComparison.OrdinalIgnoreCase) && !to.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Theme.Muted, from);
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(to);
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (count >= CorrectionMemory.NeededConfirmations) ImGui.TextColored(Theme.Good, "in use");
            else ImGui.TextColored(Theme.Unsure, $"seen {count} of {CorrectionMemory.NeededConfirmations}");
            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"Forget##{from}"))
            {
                memory.Forget(from);
                save();
            }
        }
        ImGui.EndTable();
    }

    private static void Indented(string text)
    {
        ImGui.Indent(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X);
        Theme.Hint(text);
        ImGui.Unindent(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X);
    }

    private static void Step(string number, string text)
    {
        ImGui.TextColored(Theme.Accent, number);
        ImGui.SameLine();
        ImGui.TextWrapped(text);
    }

    private static void Command(string command, string what)
    {
        ImGui.TextUnformatted(command);
        ImGui.SameLine(190 * ImGuiHelpers.GlobalScale);
        ImGui.TextColored(Theme.Muted, what);
    }

    private void CommitWords()
    {
        if (words == configuration.NeverChange) return;
        configuration.NeverChange = words;
        save();
    }
}
