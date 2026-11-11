using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace TypeMend;

/// <summary>TypeMend's colours and small drawing helpers, shared by its windows.</summary>
internal static class Theme
{
    public static readonly Vector4 Accent = new(0.55f, 0.78f, 1f, 1f);
    public static readonly Vector4 Muted = new(0.68f, 0.70f, 0.75f, 1f);
    public static readonly Vector4 Unsure = new(1f, 0.75f, 0.25f, 1f);
    public static readonly Vector4 Good = new(0.45f, 0.85f, 0.55f, 1f);
    public static readonly Vector4 Problem = new(1f, 0.45f, 0.45f, 1f);

    public static readonly Vector4 TypedBack = new(1f, 1f, 1f, 0.04f);
    public static readonly Vector4 RepairBack = new(0.35f, 0.6f, 1f, 0.10f);
    public static readonly Vector4 ProblemBack = new(1f, 0.3f, 0.3f, 0.10f);
    public static readonly Vector4 EditBack = new(0f, 0f, 0f, 0.35f);

    private static readonly Vector4 Primary = new(0.20f, 0.45f, 0.75f, 1f);
    private static readonly Vector4 PrimaryHover = new(0.26f, 0.55f, 0.88f, 1f);
    private static readonly Vector4 PrimaryActive = new(0.16f, 0.38f, 0.65f, 1f);

    public static void PushWindowStyle(float scale)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 12) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 7) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6 * scale);
    }

    public static void PopWindowStyle() => ImGui.PopStyleVar(4);

    public static void PushPrimaryButton()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Primary);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, PrimaryHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, PrimaryActive);
    }

    public static void PopPrimaryButton() => ImGui.PopStyleColor(3);

    /// <summary>A small capitals heading above a panel.</summary>
    public static void Label(string text, Vector4 colour) => ImGui.TextColored(colour, text);

    public static void Wrapped(Vector4 colour, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, colour);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    /// <summary>A tinted box that grows with its content.</summary>
    public static void Panel(string id, Vector4 background, Action content)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var padding = new Vector2(10, 8) * scale;
        var draw = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var right = start.X + ImGui.GetContentRegionAvail().X;

        // Content goes on the upper layer; the box is drawn behind it once its height is known.
        draw.ChannelsSplit(2);
        draw.ChannelsSetCurrent(1);
        ImGui.PushID(id);
        ImGui.SetCursorScreenPos(start + padding);
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(right - padding.X - ImGui.GetWindowPos().X);
        ImGui.PushClipRect(start, new Vector2(right, float.MaxValue), true);
        content();
        ImGui.PopClipRect();
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        ImGui.PopID();
        var bottom = ImGui.GetItemRectMax().Y + padding.Y;
        draw.ChannelsSetCurrent(0);
        draw.AddRectFilled(start, new Vector2(right, bottom), ImGui.GetColorU32(background), 6 * scale);
        draw.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(start.X, bottom));
        ImGui.Dummy(new Vector2(right - start.X, 0));
    }

    /// <summary>A grey line of help text that wraps to the window.</summary>
    public static void Hint(string text) => Wrapped(Muted, text);
}
