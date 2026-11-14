using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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
    public static readonly Vector4 Changed = new(0.62f, 0.86f, 1f, 1f);

    public static readonly Vector4 TypedBack = new(1f, 1f, 1f, 0.04f);
    public static readonly Vector4 RepairBack = new(0.35f, 0.6f, 1f, 0.10f);
    public static readonly Vector4 ProblemBack = new(1f, 0.3f, 0.3f, 0.10f);
    public static readonly Vector4 GoodBack = new(0.3f, 0.85f, 0.45f, 0.10f);
    public static readonly Vector4 UnsureBack = new(1f, 0.75f, 0.25f, 0.12f);
    public static readonly Vector4 EditBack = new(0f, 0f, 0f, 0.35f);
    public static readonly Vector4 CardBack = new(1f, 1f, 1f, 0.05f);

    private static readonly Vector4 Primary = new(0.20f, 0.45f, 0.75f, 1f);
    private static readonly Vector4 PrimaryHover = new(0.26f, 0.55f, 0.88f, 1f);
    private static readonly Vector4 PrimaryActive = new(0.16f, 0.38f, 0.65f, 1f);

    public static void PushWindowStyle(float scale)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 12) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 7) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 5 * scale);
    }

    public static void PopWindowStyle() => ImGui.PopStyleVar(7);

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

    /// <summary>A FontAwesome icon as text.</summary>
    public static void Icon(FontAwesomeIcon icon, Vector4 colour)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(colour, icon.ToIconString());
        ImGui.PopFont();
    }

    public static Vector2 IconSize(FontAwesomeIcon icon)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        var size = ImGui.CalcTextSize(icon.ToIconString());
        ImGui.PopFont();
        return size;
    }

    /// <summary>An icon and a heading on one line.</summary>
    public static void Heading(FontAwesomeIcon icon, string text, Vector4? colour = null)
    {
        ImGui.AlignTextToFramePadding();
        Icon(icon, colour ?? Accent);
        ImGui.SameLine();
        ImGui.TextColored(colour ?? Accent, text);
    }

    /// <summary>A button with an icon in front of its text; the label after ## keeps it unique.</summary>
    public static bool Button(FontAwesomeIcon icon, string text, Vector2 size, bool primary = false)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var shown = text.Split("##")[0];
        var iconSize = IconSize(icon);
        var gap = 6 * scale;
        var textSize = ImGui.CalcTextSize(shown);
        var padding = ImGui.GetStyle().FramePadding;
        if (size.X <= 0) size.X = iconSize.X + gap + textSize.X + padding.X * 2;
        if (size.Y <= 0) size.Y = ImGui.GetFrameHeight();

        if (primary) PushPrimaryButton();
        var start = ImGui.GetCursorScreenPos();
        var clicked = ImGui.Button("##" + text, size);
        if (primary) PopPrimaryButton();

        var colour = ImGui.GetColorU32(ImGuiCol.Text);   // follows BeginDisabled's fade
        var draw = ImGui.GetWindowDrawList();
        var x = start.X + (size.X - iconSize.X - gap - textSize.X) / 2;
        ImGui.PushFont(UiBuilder.IconFont);
        draw.AddText(new Vector2(x, start.Y + (size.Y - iconSize.Y) / 2), colour, icon.ToIconString());
        ImGui.PopFont();
        draw.AddText(new Vector2(x + iconSize.X + gap, start.Y + (size.Y - textSize.Y) / 2), colour, shown);
        return clicked;
    }

    /// <summary>A small icon-only button.</summary>
    public static bool IconButton(string id, FontAwesomeIcon icon, string tooltip)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        var clicked = ImGui.Button(icon.ToIconString() + "##" + id);
        ImGui.PopFont();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    /// <summary>A rounded pill with an icon and a short text, like a status badge.</summary>
    public static void Chip(FontAwesomeIcon icon, string text, Vector4 colour, Vector4 background)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var pad = new Vector2(8, 3) * scale;
        var gap = icon == FontAwesomeIcon.None ? 0 : 5 * scale;
        var iconSize = icon == FontAwesomeIcon.None ? Vector2.Zero : IconSize(icon);
        var textSize = ImGui.CalcTextSize(text);
        var height = Math.Max(iconSize.Y, textSize.Y) + pad.Y * 2;
        var size = new Vector2(pad.X * 2 + iconSize.X + gap + textSize.X, height);
        var start = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + size, ImGui.GetColorU32(background), height / 2);
        var x = start.X + pad.X;
        if (icon != FontAwesomeIcon.None)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            draw.AddText(new Vector2(x, start.Y + (height - iconSize.Y) / 2), ImGui.GetColorU32(colour), icon.ToIconString());
            ImGui.PopFont();
        }
        draw.AddText(new Vector2(x + iconSize.X + gap, start.Y + (height - textSize.Y) / 2), ImGui.GetColorU32(colour), text);
        ImGui.Dummy(size);
    }

    public static float ChipWidth(FontAwesomeIcon icon, string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var iconWidth = icon == FontAwesomeIcon.None ? 0 : IconSize(icon).X + 5 * scale;
        return 16 * scale + iconWidth + ImGui.CalcTextSize(text).X;
    }

    /// <summary>A thin bar that fills up; used for the chat line's byte limit.</summary>
    public static void Bar(float fraction, Vector4 colour, float height)
    {
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + new Vector2(width, height), ImGui.GetColorU32(new Vector4(1, 1, 1, 0.08f)), height / 2);
        var filled = Math.Clamp(fraction, 0, 1) * width;
        if (filled > 0) draw.AddRectFilled(start, start + new Vector2(filled, height), ImGui.GetColorU32(colour), height / 2);
        ImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>A tinted box that grows with its content.</summary>
    public static void Panel(string id, Vector4 background, Action content, Vector4? border = null)
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
        if (border is { } b) draw.AddRect(start, new Vector2(right, bottom), ImGui.GetColorU32(b), 6 * scale, ImDrawFlags.None, 1 * scale);
        draw.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(start.X, bottom));
        ImGui.Dummy(new Vector2(right - start.X, 0));
    }

    /// <summary>A grey line of help text that wraps to the window.</summary>
    public static void Hint(string text) => Wrapped(Muted, text);

    /// <summary>Moves the cursor so an item of this width ends at the right edge of the content region.</summary>
    public static void AlignRight(float width)
    {
        var x = ImGui.GetWindowContentRegionMax().X - width;
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), x));
    }
}
