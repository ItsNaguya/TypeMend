using System.Diagnostics;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace TypeMend;

/// <summary>Shows the repair next to what was typed. Nothing is sent until the writer chooses.</summary>
public sealed class PreviewWindow
{
    public const int ChatByteLimit = 500;
    private const float Width = 600;

    private static readonly TimeSpan EnterGuard = TimeSpan.FromMilliseconds(350);

    /// <summary>Quick repairs land before the window would show, so it does not flash up for a moment.</summary>
    private static readonly TimeSpan ShowAfter = TimeSpan.FromMilliseconds(150);

    private readonly Action<string, bool, string, ChatChannel> send;

    private string typed = string.Empty, body = string.Empty, prefix = string.Empty, edited = string.Empty, error = string.Empty, refused = string.Empty;
    private HashSet<string> unsure = new();
    private HashSet<string> typedWords = new(StringComparer.OrdinalIgnoreCase);
    private bool saveToHistory, ready, focus, sending, autoSending;
    private volatile bool autoSend;
    private ChatChannel channel;
    private string repairShown = string.Empty;
    private string? sentRepair;
    private readonly Stopwatch shown = new();

    public PreviewWindow(Action<string, bool, string, ChatChannel> send) => this.send = send;

    public bool IsOpen { get; private set; }

    /// <summary>Called after a repair went out: the repair as shown, then the text actually sent.</summary>
    public Action<string, string>? RepairSent { get; init; }

    /// <summary>Shows a short "how this works" note under the buttons until the writer hides it.</summary>
    public bool ShowTips { get; set; } = true;

    public Action? HideTips { get; init; }

    /// <summary>When the repair changes nothing and has no guesses, the line goes out without waiting for a click.</summary>
    public bool SendCleanRightAway { get; set; }

    /// <summary>Works out the click-to-fix menu for a word: typed text, repair shown, word start, word.</summary>
    public Func<string, string, int, string, CancellationToken, Suggestions.Result>? Suggest { get; init; }

    private const string FixPopup = "##typemend-fix";
    private string fixWord = string.Empty;
    private string? fixTyped;
    private int fixStart = -1;
    private Task<Suggestions.Result>? fixTask;
    private CancellationTokenSource? fixCancel;

    public void Open(string line, string linePrefix, bool history, ChatChannel heldChannel)
    {
        typed = line;
        prefix = linePrefix;
        body = line[linePrefix.Length..];
        typedWords = new(body.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        saveToHistory = history;
        channel = heldChannel;
        edited = error = refused = string.Empty;
        unsure = new();
        ready = sending = autoSend = autoSending = false;
        StopFix();
        IsOpen = true;
        shown.Restart();
    }

    public void SendDone()
    {
        sending = false;
        IsOpen = false;
        if (sentRepair != null && sentRepair != repairShown) RepairSent?.Invoke(repairShown, sentRepair);
        sentRepair = null;
    }

    public void SendRefused(string message)
    {
        refused = message;
        sending = false;
    }

    public void SetResult(string repairedBody, IEnumerable<string> unsureWords)
    {
        edited = repairShown = repairedBody;
        unsure = new(unsureWords);
        autoSend = SendCleanRightAway && unsure.Count == 0 && repairedBody.Trim() == body.Trim();
        ready = focus = true;
        shown.Restart();
    }

    public void SetFailed(string message)
    {
        error = message;
        ready = true;
        shown.Restart();
    }

    public void Draw()
    {
        if (!IsOpen) return;
        if (autoSend)
        {
            autoSend = false;
            autoSending = true;
            TrySend(typed, false);
        }
        // A line that needed nothing goes out without showing the window, unless the game refused it.
        if (autoSending && refused.Length == 0) return;
        if (!ready && shown.Elapsed < ShowAfter) return;

        var scale = ImGuiHelpers.GlobalScale;
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.GetCenter() + new Vector2(0, viewport.Size.Y * 0.15f), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(Width * scale, 0), ImGuiCond.Always);
        var stayOpen = true;
        Theme.PushWindowStyle(scale);
        if (ImGui.Begin("TypeMend###typemend-preview", ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize))
        {
            DrawHeader();

            if (!ready) DrawWorking();
            else if (error.Length > 0) Theme.Panel("##failed", Theme.ProblemBack, () => Theme.Wrapped(Theme.Problem, error));
            else DrawRepair();

            ImGui.Spacing();
            Theme.Label("YOU TYPED", Theme.Muted);
            Theme.Panel("##typed", Theme.TypedBack, () => Theme.Wrapped(Theme.Muted, body.Length > 0 ? body : typed));

            if (ready && error.Length == 0) DrawEditor(scale);

            ImGui.Spacing();
            DrawButtons(scale);
        }
        ImGui.End();
        Theme.PopWindowStyle();
        if (!stayOpen) IsOpen = false;
    }

    /// <summary>Where the line goes on the left, how the repair went on the right.</summary>
    private void DrawHeader()
    {
        var where = prefix.Trim();
        Theme.Chip(FontAwesomeIcon.CommentDots, where.Length > 0 ? where : "current channel", Theme.Muted, Theme.CardBack);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(where.Length > 0
                ? "This line goes to the channel its command names."
                : "This line goes to the chat channel that was active when you pressed Enter.");

        var (icon, text, colour, back) = Status();
        Theme.AlignRight(Theme.ChipWidth(icon, text));
        Theme.Chip(icon, text, colour, back);
    }

    private (FontAwesomeIcon Icon, string Text, Vector4 Colour, Vector4 Back) Status()
    {
        if (sending) return (FontAwesomeIcon.PaperPlane, "Sending...", Theme.Accent, Theme.RepairBack);
        if (!ready) return (FontAwesomeIcon.HourglassHalf, "Repairing...", Theme.Muted, Theme.CardBack);
        if (error.Length > 0) return (FontAwesomeIcon.ExclamationTriangle, "Repair failed", Theme.Problem, Theme.ProblemBack);
        var words = edited.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var guesses = words.Count(unsure.Contains);
        if (guesses > 0) return (FontAwesomeIcon.QuestionCircle, guesses == 1 ? "1 guess to check" : $"{guesses} guesses to check", Theme.Unsure, Theme.UnsureBack);
        var changed = words.Count(w => !typedWords.Contains(w));
        if (changed > 0) return (FontAwesomeIcon.Magic, changed == 1 ? "1 word fixed" : $"{changed} words fixed", Theme.Accent, Theme.RepairBack);
        return (FontAwesomeIcon.CheckCircle, "Nothing to fix", Theme.Good, Theme.GoodBack);
    }

    private void DrawWorking()
    {
        Theme.Label("WILL SEND", Theme.Accent);
        var dots = new string('.', 1 + (int)(shown.Elapsed.TotalSeconds * 3 % 3));
        Theme.Panel("##working", Theme.RepairBack, () => ImGui.TextColored(Theme.Muted, "Repairing" + dots));
    }

    private void DrawRepair()
    {
        Theme.Label("WILL SEND", Theme.Accent);
        var hasGuesses = edited.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(unsure.Contains);
        Theme.Panel("##repair", Theme.RepairBack, () => DrawWords(edited), hasGuesses ? Theme.Unsure with { W = 0.35f } : null);
        if (hasGuesses) Theme.Wrapped(Theme.Muted, "Amber words are guesses. Click one to pick another word or keep what you typed.");
    }

    /// <summary>The edit box, with a thin bar for the chat line's byte limit under it.</summary>
    private void DrawEditor(float scale)
    {
        ImGui.Spacing();
        if (focus)
        {
            ImGui.SetKeyboardFocusHere();
            focus = false;
        }
        ImGui.SetNextItemWidth(-1);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.EditBack);
        var enter = ImGui.InputTextWithHint("##edit", "Edit before sending (Enter sends)", ref edited, 800, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.PopStyleColor();
        if (enter && shown.Elapsed > EnterGuard) TrySend(prefix + edited, true);

        var bytes = Encoding.UTF8.GetByteCount(prefix + edited);
        var fraction = bytes / (float)ChatByteLimit;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 4 * scale);
        var colour = fraction > 1 ? Theme.Problem : fraction > 0.85f ? Theme.Unsure : Theme.Accent with { W = 0.6f };
        Theme.Bar(fraction, colour, 3 * scale);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{bytes} of {ChatByteLimit} bytes.\nA chat line holds 500 bytes. Most letters count 1; accents and symbols count 2 or 3.");
        if (fraction > 0.85f)
        {
            var counter = $"{bytes} / {ChatByteLimit}";
            ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(counter).X);
            ImGui.TextColored(colour, counter);
        }
    }

    /// <summary>
    /// Lays the words out with wrapping. Guesses are amber and underlined, fixed words light blue; a click on either
    /// opens the fix menu.
    /// </summary>
    private void DrawWords(string text)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X - 10 * ImGuiHelpers.GlobalScale;
        var space = ImGui.CalcTextSize(" ").X;
        var first = true;
        var at = 0;
        while (at < text.Length)
        {
            if (text[at] == ' ') { at++; continue; }
            var end = text.IndexOf(' ', at);
            if (end < 0) end = text.Length;
            var word = text[at..end];
            var size = ImGui.CalcTextSize(word);
            if (!first && ImGui.GetItemRectMax().X + space + size.X < right) ImGui.SameLine(0, space);
            var guess = unsure.Contains(word);
            var changed = !guess && !typedWords.Contains(word);
            if (guess || changed)
            {
                var colour = guess ? Theme.Unsure : Theme.Changed;
                ImGui.TextColored(colour, word);
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var line = guess ? colour : colour with { W = 0.35f };
                ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, max.Y), max, ImGui.GetColorU32(line), guess ? 1.5f : 1f);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    var was = Align.TypedFor(body, text, at, end) is { } span ? body[span.From..span.To] : null;
                    var from = was != null && was != word ? $"You typed \"{was}\".\n" : string.Empty;
                    ImGui.SetTooltip(guess
                        ? $"TypeMend is not sure about this word.\n{from}Click it for other suggestions."
                        : $"{from}Click it for other suggestions or to undo.");
                }
                if (ImGui.IsItemClicked())
                {
                    StartFix(text, at, word);
                    ImGui.OpenPopup(FixPopup);
                }
            }
            else ImGui.TextUnformatted(word);
            first = false;
            at = end;
        }
        if (first) ImGui.TextColored(Theme.Muted, "(empty)");
        DrawFixMenu();
    }

    private void StartFix(string text, int start, string word)
    {
        StopFix();
        fixWord = word;
        fixStart = start;
        fixTyped = Align.TypedFor(body, text, start, start + word.Length) is { } span ? body[span.From..span.To] : null;
        if (Suggest == null) return;
        var cancel = fixCancel = new CancellationTokenSource();
        var typedBody = body;
        fixTask = Task.Run(() => Suggest(typedBody, text, start, word, cancel.Token), cancel.Token);
    }

    private void StopFix()
    {
        fixCancel?.Cancel();
        fixCancel = null;
        fixTask = null;
        fixStart = -1;
    }

    /// <summary>Other suggestions from the model, the letters as typed, or keep the word: one click each.</summary>
    private void DrawFixMenu()
    {
        if (!ImGui.BeginPopup(FixPopup)) return;
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 5) * scale);
        var width = new Vector2(170 * scale, 0);

        Theme.Heading(FontAwesomeIcon.Magic, "Other suggestions", Theme.Muted);
        if (fixTask == null) ImGui.TextColored(Theme.Muted, "None");
        else if (!fixTask.IsCompleted) ImGui.TextColored(Theme.Muted, "Looking" + new string('.', 1 + (int)(shown.Elapsed.TotalSeconds * 3 % 3)));
        else if (fixTask.IsCompletedSuccessfully && fixTask.Result.Picks.Count > 0)
        {
            foreach (var pick in fixTask.Result.Picks)
                if (ImGui.Selectable(pick + "##pick", false, ImGuiSelectableFlags.None, width)) Replace(pick);
        }
        else ImGui.TextColored(Theme.Muted, "No other suggestions");

        if (fixTyped != null && fixTyped != fixWord)
        {
            ImGui.Spacing();
            Theme.Heading(FontAwesomeIcon.Undo, "As you typed it", Theme.Muted);
            if (ImGui.Selectable(fixTyped + "##typed", false, ImGuiSelectableFlags.None, width)) Replace(fixTyped);
        }
        ImGui.Separator();
        Theme.Icon(FontAwesomeIcon.Check, Theme.Good);
        ImGui.SameLine();
        if (ImGui.Selectable($"Keep \"{fixWord}\"##keep")) Replace(fixWord);
        ImGui.PopStyleVar();
        ImGui.EndPopup();
    }

    /// <summary>Puts the chosen text in place of the word. The word stops being marked as a guess.</summary>
    private void Replace(string text)
    {
        if (fixStart >= 0 && fixStart + fixWord.Length <= edited.Length && string.CompareOrdinal(edited, fixStart, fixWord, 0, fixWord.Length) == 0)
            edited = edited[..fixStart] + text + edited[(fixStart + fixWord.Length)..];
        unsure.Remove(fixWord);
        // A fixed word that was kept stays as it is; treat it as the writer's own from now on.
        typedWords.Add(text);
        StopFix();
        ImGui.CloseCurrentPopup();
    }

    private void DrawButtons(float scale)
    {
        var tooLong = Encoding.UTF8.GetByteCount(prefix + edited) > ChatByteLimit;
        var height = 32 * scale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        float typedWidth = 130 * scale, cancelWidth = 105 * scale;
        var sendWidth = Math.Max(150 * scale, ImGui.GetContentRegionAvail().X - typedWidth - cancelWidth - spacing * 2);

        ImGui.BeginDisabled(!ready || error.Length > 0 || tooLong || sending);
        if (Theme.Button(FontAwesomeIcon.PaperPlane, "Send repair##send", new Vector2(sendWidth, height), primary: true)) TrySend(prefix + edited, true);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(tooLong ? "Too long for one chat line." : "Sends the text in the edit box. Shortcut: Enter.");

        ImGui.SameLine();
        ImGui.BeginDisabled(sending);
        if (Theme.Button(FontAwesomeIcon.Keyboard, "As typed##astyped", new Vector2(typedWidth, height))) TrySend(typed, false);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Sends exactly what you typed, without any repair.");

        ImGui.SameLine();
        if (Theme.Button(FontAwesomeIcon.Times, "Cancel##cancel", new Vector2(cancelWidth, height)) || ImGui.IsKeyPressed(ImGuiKey.Escape)) IsOpen = false;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Sends nothing. Shortcut: Esc.");

        if (tooLong) Theme.Wrapped(Theme.Problem, "Too long for one chat line. Shorten it in the box, or send it in two parts.");
        if (refused.Length > 0) Theme.Panel("##refused", Theme.ProblemBack, () => Theme.Wrapped(Theme.Problem, refused));
        if (ShowTips && ready && error.Length == 0)
        {
            ImGui.Spacing();
            Theme.Panel("##tip", Theme.CardBack, () =>
            {
                Theme.Icon(FontAwesomeIcon.Lightbulb, Theme.Unsure);
                ImGui.SameLine();
                Theme.Wrapped(Theme.Muted, "Blue words were fixed, amber words are guesses: click either for other choices. Fix the same word twice and TypeMend learns it. /typemend opens the settings.");
                if (ImGui.SmallButton("Got it, hide tips")) HideTips?.Invoke();
            });
        }
    }

    private void TrySend(string line, bool isRepair)
    {
        if (sending || Encoding.UTF8.GetByteCount(line) > ChatByteLimit) return;
        sending = true;
        refused = string.Empty;
        sentRepair = isRepair ? edited : null;
        send(line, saveToHistory, prefix, channel);
    }
}
