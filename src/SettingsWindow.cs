using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;

namespace TypeMend;

/// <summary>TypeMend's main window: on/off, options, the writer's own words, what it learned, and help.</summary>
public sealed class SettingsWindow
{
    public enum Tab { General, NeverChange, Learned, Help }

    private readonly Configuration configuration;
    private readonly CorrectionMemory memory;
    private readonly Action save;
    private readonly Func<string> modelState;
    private string words;
    private string newWord = string.Empty;
    private string filter = string.Empty;
    private bool isOpen, confirmForgetAll, editAsText;
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
        ImGui.SetNextWindowSize(new Vector2(560, 540) * scale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(440, 380) * scale, new Vector2(1400, 1400) * scale);
        Theme.PushWindowStyle(scale);
        if (ImGui.Begin("TypeMend###typemend-settings", ref isOpen, ImGuiWindowFlags.NoCollapse))
        {
            DrawStatusCard(scale);
            if (!configuration.SeenWelcome) DrawWelcome();
            ImGui.Spacing();

            if (ImGui.BeginTabBar("##tabs"))
            {
                if (TabItem("Options###general", Tab.General))
                {
                    Scroll("##options", () => DrawOptions());
                    ImGui.EndTabItem();
                }
                var keepCount = ChatLine.ParseKeepWords(words).Count;
                if (TabItem(keepCount > 0 ? $"Never change ({keepCount})###never" : "Never change###never", Tab.NeverChange))
                {
                    DrawNeverChange(scale);
                    ImGui.EndTabItem();
                }
                var count = memory.Entries().Count;
                if (TabItem(count > 0 ? $"Learned ({count})###learned" : "Learned###learned", Tab.Learned))
                {
                    DrawLearned(scale);
                    ImGui.EndTabItem();
                }
                if (TabItem("Help###help", Tab.Help))
                {
                    Scroll("##help", DrawHelp);
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

    private static void Scroll(string id, Action content)
    {
        ImGui.BeginChild(id, new Vector2(-1, -1), false);
        ImGui.Spacing();
        content();
        ImGui.EndChild();
    }

    private const string LanguageNote =
        "TypeMend is made for English. It can help with other languages too, with limits: German swear words are known, " +
        "but other words may be \"repaired\" into English ones, so check the preview. Lines with letters like ä, ö, ü or ß go out as typed. " +
        "Add words you use often under Never change.";

    /// <summary>The big on/off switch with the state in words, and whether the model is ready.</summary>
    private void DrawStatusCard(float scale)
    {
        var enabled = configuration.Enabled;
        var state = modelState();
        var ready = state.StartsWith("Ready");
        var loading = state.StartsWith("Loading");

        Theme.Panel("##status", enabled ? Theme.RepairBack : Theme.CardBack, () =>
        {
            ImGui.SetWindowFontScale(1.25f);
            Theme.Icon(enabled ? FontAwesomeIcon.Magic : FontAwesomeIcon.PowerOff, enabled ? Theme.Accent : Theme.Muted);
            ImGui.SameLine();
            ImGui.TextUnformatted(enabled ? "TypeMend is on" : "TypeMend is off");
            ImGui.SetWindowFontScale(1f);

            var toggleWidth = ImGui.GetFrameHeight() * 1.55f;
            ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - toggleWidth - 12 * scale);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3 * scale);
            if (ImGuiComponents.ToggleButton("##enabled", ref enabled))
            {
                configuration.Enabled = enabled;
                save();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(enabled ? "Turn repairs off: messages go out as typed." : "Turn repairs on.");

            Theme.Hint(enabled
                ? "Your chat messages are checked before they go out. You choose what is sent."
                : "Messages go out exactly as you type them.");

            var dot = ready ? Theme.Good : loading ? Theme.Unsure : Theme.Problem;
            Theme.Icon(FontAwesomeIcon.Microchip, dot);
            ImGui.SameLine();
            ImGui.TextColored(Theme.Muted, "Repair model: " + state);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Runs on this PC's processor. Nothing you type leaves your PC.");
        });
    }

    private void DrawWelcome()
    {
        ImGui.Spacing();
        Theme.Panel("##welcome", Theme.CardBack, () =>
        {
            Theme.Heading(FontAwesomeIcon.InfoCircle, "Welcome to TypeMend");
            Theme.Wrapped(ImGui.GetStyle().Colors[(int)ImGuiCol.Text],
                "Type in chat as you always do. Before a message goes out, a small window shows the repair next to what you typed. " +
                "You choose what is sent. It all runs on this PC; nothing leaves it.");
            Theme.Wrapped(Theme.Muted, LanguageNote);
            if (Theme.Button(FontAwesomeIcon.Check, "Got it##welcome", Vector2.Zero, primary: true))
            {
                configuration.SeenWelcome = true;
                save();
            }
        }, Theme.Accent with { W = 0.3f });
    }

    private void DrawOptions()
    {
        Theme.Heading(FontAwesomeIcon.Magic, "Repairs");
        Option("Keep common roleplay words as typed",
            "Words like collar, safeword, aftercare, kitten or brat stay exactly as you wrote them. Marks like ~ and (( )) always do.",
            configuration.KeepRoleplayWords, v => configuration.KeepRoleplayWords = v);
        Option("Keep swear words and fix their typos",
            "English and German swear words stay as you wrote them, and a typo in one (\"bastrad\") becomes the swear word you meant. Its fixes show as guesses.",
            configuration.KeepSwearWords, v => configuration.KeepSwearWords = v);

        ImGui.Spacing();
        Theme.Heading(FontAwesomeIcon.Bolt, "Preview window");
        Option("Send right away when nothing needs fixing",
            "A message the repair leaves exactly as typed, with no guesses, goes out without the preview window. Off: every message waits for you.",
            configuration.SendCleanRightAway, v => configuration.SendCleanRightAway = v);
        Option("Show tips in the preview window",
            "A short note under the buttons explains the colours and the shortcuts.",
            configuration.ShowTips, v => configuration.ShowTips = v);

        ImGui.Spacing();
        Theme.Heading(FontAwesomeIcon.Language, "Languages");
        Theme.Hint(LanguageNote);
    }

    /// <summary>A checkbox with a title and a grey line under it that says what it does.</summary>
    private void Option(string title, string hint, bool value, Action<bool> set)
    {
        Theme.Panel("##" + title, Theme.CardBack, () =>
        {
            if (ImGui.Checkbox(title, ref value))
            {
                set(value);
                save();
            }
            var indent = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
            ImGui.Indent(indent);
            Theme.Hint(hint);
            ImGui.Unindent(indent);
        });
    }

    private void DrawNeverChange(float scale)
    {
        ImGui.Spacing();
        Theme.Hint("Names, places and game words that must stay exactly as you typed them. Letter case does not matter for finding them.");
        ImGui.Spacing();

        var list = ChatLine.ParseKeepWords(words);
        if (editAsText)
        {
            ImGui.InputTextMultiline("##never-change", ref words, 4000, new Vector2(-1, -ImGui.GetFrameHeightWithSpacing() * 1.4f));
            if (ImGui.IsItemDeactivatedAfterEdit()) CommitWords();
            if (Theme.Button(FontAwesomeIcon.Check, "Done##text", Vector2.Zero, primary: true))
            {
                CommitWords();
                editAsText = false;
            }
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Theme.Muted, "One word per line, or separated by spaces or commas.");
            return;
        }

        // Add one word at a time; a pasted list with spaces or commas is split into words.
        ImGui.SetNextItemWidth(Math.Min(260 * scale, ImGui.GetContentRegionAvail().X - 200 * scale));
        var added = ImGui.InputTextWithHint("##new-word", "Add a name or word", ref newWord, 200, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        ImGui.BeginDisabled(newWord.Trim().Length == 0);
        added |= Theme.Button(FontAwesomeIcon.Plus, "Add##add", Vector2.Zero, primary: true);
        ImGui.EndDisabled();
        if (added && newWord.Trim().Length > 0)
        {
            var fresh = ChatLine.ParseKeepWords(newWord).Where(w => !list.Contains(w, StringComparer.OrdinalIgnoreCase)).ToList();
            if (fresh.Count > 0)
            {
                list.AddRange(fresh);
                words = string.Join('\n', list);
                CommitWords();
            }
            newWord = string.Empty;
            ImGui.SetKeyboardFocusHere(-1);
        }
        Theme.AlignRight(ImGui.CalcTextSize("Edit as text").X + Theme.IconSize(FontAwesomeIcon.Pen).X + 6 * scale + ImGui.GetStyle().FramePadding.X * 2);
        if (Theme.Button(FontAwesomeIcon.Pen, "Edit as text##edit", Vector2.Zero)) editAsText = true;

        ImGui.Spacing();
        ImGui.BeginChild("##chips", new Vector2(-1, -1), false);
        if (list.Count == 0)
        {
            Theme.Panel("##empty", Theme.TypedBack, () => ImGui.TextColored(Theme.Muted, "No words yet. Add your character's name, friends' names, places or game terms."));
        }
        else
        {
            string? remove = null;
            var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            var space = 6 * scale;
            var first = true;
            foreach (var word in list)
            {
                var width = ImGui.CalcTextSize(word).X + Theme.IconSize(FontAwesomeIcon.Times).X + 30 * scale;
                if (!first && ImGui.GetItemRectMax().X + space + width < right) ImGui.SameLine(0, space);
                if (WordChip(word, width)) remove = word;
                first = false;
            }
            if (remove != null)
            {
                words = string.Join('\n', list.Where(w => !string.Equals(w, remove, StringComparison.OrdinalIgnoreCase)));
                CommitWords();
            }
        }
        ImGui.EndChild();
    }

    /// <summary>One never-change word as a pill; the x removes it. True when it was clicked to remove.</summary>
    private static bool WordChip(string word, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = ImGui.GetFrameHeight();
        var start = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##chip" + word, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + new Vector2(width, height), ImGui.GetColorU32(hovered ? Theme.ProblemBack : Theme.RepairBack), height / 2);
        var textSize = ImGui.CalcTextSize(word);
        draw.AddText(start + new Vector2(10 * scale, (height - textSize.Y) / 2), ImGui.GetColorU32(ImGuiCol.Text), word);
        ImGui.PushFont(UiBuilder.IconFont);
        var icon = FontAwesomeIcon.Times.ToIconString();
        var iconSize = ImGui.CalcTextSize(icon);
        draw.AddText(new Vector2(start.X + width - iconSize.X - 10 * scale, start.Y + (height - iconSize.Y) / 2),
            ImGui.GetColorU32(hovered ? Theme.Problem : Theme.Muted), icon);
        ImGui.PopFont();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip($"Remove \"{word}\"");
        }
        return clicked;
    }

    private void DrawLearned(float scale)
    {
        ImGui.Spacing();
        Theme.Hint($"When you fix a word in the preview, TypeMend remembers it. After the same fix {CorrectionMemory.NeededConfirmations} times it makes that fix for you. Changing it back makes it forget. Stays on this PC.");
        ImGui.Spacing();

        var entries = memory.Entries();
        if (entries.Count == 0)
        {
            Theme.Panel("##none", Theme.TypedBack, () =>
            {
                Theme.Icon(FontAwesomeIcon.Brain, Theme.Muted);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Muted, "Nothing learned yet. Fix a word in the preview window to teach TypeMend.");
            });
            return;
        }

        ImGui.SetNextItemWidth(220 * scale);
        ImGui.InputTextWithHint("##filter", "Search", ref filter, 100);
        if (confirmForgetAll)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Theme.Problem, $"Forget all {entries.Count}?");
            ImGui.SameLine();
            if (ImGui.Button("Yes, forget them"))
            {
                memory.ForgetAll();
                save();
                confirmForgetAll = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("No")) confirmForgetAll = false;
        }
        else
        {
            Theme.AlignRight(ImGui.CalcTextSize("Forget everything").X + Theme.IconSize(FontAwesomeIcon.Trash).X + 6 * scale + ImGui.GetStyle().FramePadding.X * 2);
            if (Theme.Button(FontAwesomeIcon.Trash, "Forget everything##all", Vector2.Zero)) confirmForgetAll = true;
        }

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##learned", 4, flags, new Vector2(-1, -1))) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("TypeMend wrote", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("You fixed it to", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 110 * scale);
        ImGui.TableSetupColumn("##forget", ImGuiTableColumnFlags.WidthFixed, 34 * scale);
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
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2 * scale);
            if (count >= CorrectionMemory.NeededConfirmations) Theme.Chip(FontAwesomeIcon.Check, "in use", Theme.Good, Theme.GoodBack);
            else Theme.Chip(FontAwesomeIcon.None, $"seen {count} of {CorrectionMemory.NeededConfirmations}", Theme.Unsure, Theme.UnsureBack);
            ImGui.TableNextColumn();
            if (Theme.IconButton("forget" + from, FontAwesomeIcon.Trash, $"Forget \"{from}\" -> \"{to}\""))
            {
                memory.Forget(from);
                save();
            }
        }
        ImGui.EndTable();
    }

    private static void DrawHelp()
    {
        Theme.Heading(FontAwesomeIcon.ListUl, "How it works");
        Step(1, "Type and press Enter as usual. Commands, macros and emotes are never touched.");
        Step(2, "Check the repair. Light blue words were fixed; amber, underlined words are guesses.");
        Step(3, "Click a coloured word for other suggestions, the word as you typed it, or to keep it.");
        Step(4, "Press Enter to send the repair, pick \"As typed\" to send your own text, or Esc to send nothing.");
        Step(5, "Fix the same word twice and TypeMend makes that fix for you from then on.");

        ImGui.Spacing();
        Theme.Heading(FontAwesomeIcon.Terminal, "Chat commands");
        Command("/typemend", "open this window");
        Command("/typemend on  |  off", "turn repairs on or off");
        Command("/typemend words", "open your never-change list");

        ImGui.Spacing();
        Theme.Heading(FontAwesomeIcon.Language, "Languages");
        Theme.Hint(LanguageNote);

        ImGui.Spacing();
        Theme.Heading(FontAwesomeIcon.ShieldAlt, "Privacy");
        Theme.Hint("Everything runs on this PC. Your messages, your words and what TypeMend learned never leave it.");
    }

    /// <summary>A numbered circle and a line of text.</summary>
    private static void Step(int number, string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var radius = 9 * scale;
        var start = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();
        var centre = start + new Vector2(radius, lineHeight / 2);
        var draw = ImGui.GetWindowDrawList();
        draw.AddCircleFilled(centre, radius, ImGui.GetColorU32(Theme.RepairBack with { W = 0.35f }));
        var label = number.ToString();
        var size = ImGui.CalcTextSize(label);
        draw.AddText(centre - size / 2, ImGui.GetColorU32(Theme.Accent), label);
        ImGui.Dummy(new Vector2(radius * 2, lineHeight));
        ImGui.SameLine(0, 8 * scale);
        ImGui.TextWrapped(text);
    }

    private static void Command(string command, string what)
    {
        ImGui.TextColored(Theme.Accent, command);
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
