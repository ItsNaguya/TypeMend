using System.Diagnostics;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace TypeMend;

/// <summary>Shows what was typed next to the repair. Nothing is sent until the writer chooses.</summary>
public sealed class PreviewWindow
{
    public const int ChatByteLimit = 500;
    private const float Width = 600;

    private static readonly TimeSpan EnterGuard = TimeSpan.FromMilliseconds(350);

    private readonly Action<string, bool, string, ChatChannel> send;

    private string typed = string.Empty, body = string.Empty, prefix = string.Empty, edited = string.Empty, error = string.Empty, refused = string.Empty;
    private HashSet<string> unsure = new();
    private bool saveToHistory, ready, focus, sending;
    private ChatChannel channel;
    private string repairShown = string.Empty;
    private string? sentRepair;
    private readonly Stopwatch shown = new();

    public PreviewWindow(Action<string, bool, string, ChatChannel> send) => this.send = send;

    public bool IsOpen { get; private set; }

    /// <summary>Called after a repair went out: the repair as shown, then the text actually sent.</summary>
    public Action<string, string>? RepairSent { get; init; }

    /// <summary>Shows a short "how this works" line under the buttons until the writer hides it.</summary>
    public bool ShowTips { get; set; } = true;

    public Action? HideTips { get; init; }

    public void Open(string line, string linePrefix, bool history, ChatChannel heldChannel)
    {
        typed = line;
        prefix = linePrefix;
        body = line[linePrefix.Length..];
        saveToHistory = history;
        channel = heldChannel;
        edited = error = refused = string.Empty;
        unsure = new();
        ready = sending = false;
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

        var scale = ImGuiHelpers.GlobalScale;
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.GetCenter() + new Vector2(0, viewport.Size.Y * 0.15f), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(Width * scale, 0), ImGuiCond.Always);
        var stayOpen = true;
        Theme.PushWindowStyle(scale);
        if (ImGui.Begin("TypeMend###typemend-preview", ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize))
        {
            Theme.Label("YOU TYPED", Theme.Muted);
            Theme.Panel("##typed", Theme.TypedBack, () => Theme.Wrapped(Theme.Muted, typed));

            if (!ready) DrawWorking();
            else if (error.Length > 0) Theme.Panel("##failed", Theme.ProblemBack, () => Theme.Wrapped(Theme.Problem, error));
            else DrawRepair();

            ImGui.Spacing();
            DrawButtons(scale);
        }
        ImGui.End();
        Theme.PopWindowStyle();
        if (!stayOpen) IsOpen = false;
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
        var guesses = edited.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(unsure.Contains);
        if (guesses > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.Unsure, guesses == 1 ? "  1 guess, please check it" : $"  {guesses} guesses, please check them");
        }
        else if (edited == repairShown && edited.Trim() == body.Trim())
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.Good, "  nothing needed fixing");
        }

        Theme.Panel("##repair", Theme.RepairBack, () => DrawWords(edited));

        if (focus)
        {
            ImGui.SetKeyboardFocusHere();
            focus = false;
        }
        ImGui.SetNextItemWidth(-1);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.EditBack);
        var enter = ImGui.InputTextWithHint("##edit", "Fix any word here before sending", ref edited, 800, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.PopStyleColor();
        if (enter && shown.Elapsed > EnterGuard) TrySend(prefix + edited, true);
    }

    /// <summary>Lays the words out with wrapping, guesses in amber and underlined.</summary>
    private void DrawWords(string text)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X - 10 * ImGuiHelpers.GlobalScale;
        var space = ImGui.CalcTextSize(" ").X;
        var first = true;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var size = ImGui.CalcTextSize(word);
            if (!first && ImGui.GetItemRectMax().X + space + size.X < right) ImGui.SameLine(0, space);
            if (unsure.Contains(word))
            {
                ImGui.TextColored(Theme.Unsure, word);
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, max.Y), max, ImGui.GetColorU32(Theme.Unsure), 1.5f);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("TypeMend is not sure about this word.\nFix it in the box below if it is wrong.");
            }
            else ImGui.TextUnformatted(word);
            first = false;
        }
        if (first) ImGui.TextColored(Theme.Muted, "(empty)");
    }

    private void DrawButtons(float scale)
    {
        var bytes = Encoding.UTF8.GetByteCount(prefix + edited);
        var tooLong = bytes > ChatByteLimit;
        var height = 30 * scale;

        ImGui.BeginDisabled(!ready || error.Length > 0 || tooLong || sending);
        Theme.PushPrimaryButton();
        if (ImGui.Button("Send repair  [Enter]", new Vector2(180 * scale, height))) TrySend(prefix + edited, true);
        Theme.PopPrimaryButton();
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(sending);
        if (ImGui.Button("Send as typed", new Vector2(125 * scale, height))) TrySend(typed, false);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Sends exactly what you typed, without any repair.");

        ImGui.SameLine();
        if (ImGui.Button("Cancel  [Esc]", new Vector2(110 * scale, height)) || ImGui.IsKeyPressed(ImGuiKey.Escape)) IsOpen = false;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Sends nothing. Your text stays shown above until you close this.");

        var counter = $"{bytes} / {ChatByteLimit}";
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(counter).X);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(tooLong ? Theme.Problem : Theme.Muted, counter);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A chat line holds 500 bytes.\nMost letters count 1; accents and symbols count 2 or 3.");

        if (tooLong) Theme.Wrapped(Theme.Problem, "Too long for one chat line. Shorten it in the box, or send it in two parts.");
        if (refused.Length > 0) Theme.Wrapped(Theme.Problem, refused);
        if (ShowTips && ready && error.Length == 0)
        {
            ImGui.Separator();
            Theme.Wrapped(Theme.Muted, "Tip: amber words are guesses. Fix a word in the box and TypeMend learns it after the same fix twice. /typemend opens the settings.");
            if (ImGui.SmallButton("Got it, hide tips")) HideTips?.Invoke();
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
