using System.Diagnostics;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;

namespace TypeMend;

/// <summary>Shows what was typed next to the repair. Nothing is sent until the writer chooses.</summary>
public sealed class PreviewWindow
{
    public const int ChatByteLimit = 500;

    private static readonly Vector4 Unsure = new(1f, 0.75f, 0.2f, 1f);
    private static readonly Vector4 Problem = new(1f, 0.4f, 0.4f, 1f);
    private static readonly TimeSpan EnterGuard = TimeSpan.FromMilliseconds(350);

    private readonly Action<string, bool, string, ChatChannel> send;

    private string typed = string.Empty, prefix = string.Empty, edited = string.Empty, error = string.Empty, refused = string.Empty;
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

    public void Open(string line, string linePrefix, bool history, ChatChannel heldChannel)
    {
        typed = line;
        prefix = linePrefix;
        saveToHistory = history;
        channel = heldChannel;
        edited = error = refused = string.Empty;
        unsure = new();
        ready = sending = false;
        IsOpen = true;
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

        ImGui.SetNextWindowSize(new Vector2(560, 0), ImGuiCond.Appearing);
        var stayOpen = true;
        if (ImGui.Begin("TypeMend###typemend-preview", ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.PushTextWrapPos(540);
            ImGui.TextDisabled("You typed");
            ImGui.TextUnformatted(typed);
            ImGui.Spacing();

            if (!ready) ImGui.TextUnformatted("Repairing...");
            else if (error.Length > 0) ImGui.TextColored(Problem, error);
            else DrawRepair();
            ImGui.PopTextWrapPos();

            ImGui.Spacing();
            DrawButtons();
        }
        ImGui.End();
        if (!stayOpen) IsOpen = false;
    }

    private void DrawRepair()
    {
        ImGui.TextDisabled("Will send (highlighted words are guesses)");
        var first = true;
        foreach (var word in edited.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var width = ImGui.CalcTextSize(word).X;
            if (!first && ImGui.GetItemRectMax().X + 8 + width < ImGui.GetWindowPos().X + 540) ImGui.SameLine();
            if (unsure.Contains(word)) ImGui.TextColored(Unsure, word);
            else ImGui.TextUnformatted(word);
            first = false;
        }

        ImGui.Spacing();
        if (focus)
        {
            ImGui.SetKeyboardFocusHere();
            focus = false;
        }
        ImGui.SetNextItemWidth(530);
        if (ImGui.InputText("##edit", ref edited, 800, ImGuiInputTextFlags.EnterReturnsTrue) && shown.Elapsed > EnterGuard)
            TrySend(prefix + edited, true);
    }

    private void DrawButtons()
    {
        var bytes = Encoding.UTF8.GetByteCount(prefix + edited);
        var tooLong = bytes > ChatByteLimit;

        ImGui.BeginDisabled(!ready || error.Length > 0 || tooLong);
        if (ImGui.Button("Send repair (Enter)")) TrySend(prefix + edited, true);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Send as typed")) TrySend(typed, false);
        ImGui.SameLine();
        if (ImGui.Button("Cancel (Esc)") || ImGui.IsKeyPressed(ImGuiKey.Escape)) IsOpen = false;

        if (tooLong) ImGui.TextColored(Problem, $"Too long for chat: {bytes} of {ChatByteLimit} bytes.");
        if (refused.Length > 0) ImGui.TextColored(Problem, refused);
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
