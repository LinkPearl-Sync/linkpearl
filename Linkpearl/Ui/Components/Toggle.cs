using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace Linkpearl.Ui.Components;

/// <summary>
/// Interrupteur : le réglage oui/non du plugin.
/// </summary>
/// <remarks>
/// Libellé à gauche, interrupteur calé au bord droit : d'un réglage à l'autre,
/// les interrupteurs s'alignent en colonne et se comparent d'un coup d'œil, ce
/// que des cases collées à des libellés de longueurs diverses ne font pas.
/// Allumé, le curseur est une perle dans le halo, comme les pastilles du site.
///
/// Toute la rangée est cliquable : viser une case de quinze pixels n'a jamais
/// été le but de personne.
/// </remarks>
internal static class Toggle
{
    /// <summary>La rangée complète. Rend vrai si l'état vient de changer.</summary>
    /// <param name="hint">Explication en infobulle, sur une icône ⓘ après le libellé.</param>
    /// <param name="hintContent">Comme <paramref name="hint"/>, mais dessinée librement.</param>
    public static bool Draw(string label, ref bool value, string id, string? hint = null, Action? hintContent = null)
    {
        using var scope = ImRaii.PushId(id);

        var start  = ImGui.GetCursorScreenPos();
        var width  = Math.Max(1f, ImGui.GetContentRegionAvail().X - Card.RightInset);
        var height = ImGui.GetFrameHeight();

        var clicked = ImGui.InvisibleButton("##row", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var dl    = ImGui.GetWindowDrawList();
        var track = TrackSize();
        var mid   = start.Y + height * 0.5f;

        var trackMin = new Vector2(start.X + width - track.X, mid - track.Y * 0.5f);

        // Le libellé s'arrête avant la piste : dans une fenêtre étroite, il
        // passerait dessous et l'interrupteur ne se lirait plus.
        var text     = Glyphs.Safe(label);
        var textSize = ImGui.CalcTextSize(text);
        var textEnd  = trackMin.X - Theme.S(Theme.GapL);

        dl.PushClipRect(start, new Vector2(textEnd, start.Y + height), true);
        dl.AddText(new Vector2(start.X, mid - textSize.Y * 0.5f), ImGui.GetColorU32(Theme.Text), text);
        dl.PopClipRect();

        if (hint is not null || hintContent is not null)
            DrawHint(dl, new Vector2(Math.Min(start.X + textSize.X, textEnd) + Theme.S(Theme.GapS), mid),
                     hint, hintContent);

        DrawTrack(dl, trackMin, value, hovered);

        if (clicked)
            value = !value;

        return clicked;
    }

    /// <summary>L'interrupteur seul, en ligne, pour une rangée qui porte déjà ses propres éléments.</summary>
    public static bool Switch(ref bool value, string id, string? tooltip = null)
    {
        var track   = TrackSize();
        var height  = ImGui.GetFrameHeight();
        var start   = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##switch_{id}", new Vector2(track.X, height));
        var hovered = ImGui.IsItemHovered();

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (tooltip is not null)
            Feedback.TooltipOnHover(tooltip);

        DrawTrack(ImGui.GetWindowDrawList(), new Vector2(start.X, start.Y + (height - track.Y) * 0.5f), value, hovered);

        if (clicked)
            value = !value;

        return clicked;
    }

    private static Vector2 TrackSize() => Theme.S(34f, 19f);

    private static void DrawTrack(ImDrawListPtr dl, Vector2 min, bool on, bool hovered)
    {
        var size   = TrackSize();
        var max    = min + size;
        var radius = size.Y * 0.5f;
        var knob   = radius - Theme.S(3f);

        if (on)
        {
            Surface.Glow(dl, min, max, radius, Theme.Accent, spread: 4f);
            dl.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Alpha(Theme.Accent, hovered ? 0.45f : 0.35f)), radius);
            dl.AddRect(min, max, ImGui.GetColorU32(Theme.Accent), radius, ImDrawFlags.None, 1f);
            Surface.Pearl(dl, new Vector2(max.X - radius, min.Y + radius), knob, glow: false);
            return;
        }

        dl.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Alpha(Theme.Text, hovered ? 0.12f : 0.08f)), radius);
        dl.AddRect(min, max, ImGui.GetColorU32(Theme.BorderLight), radius, ImDrawFlags.None, 1f);
        dl.AddCircleFilled(new Vector2(min.X + radius, min.Y + radius), knob, ImGui.GetColorU32(Theme.TextFaint));
    }

    /// <summary>L'icône ⓘ, dessinée à la main : la rangée entière est déjà un bouton.</summary>
    private static void DrawHint(ImDrawListPtr dl, Vector2 at, string? hint, Action? hintContent)
    {
        var glyph = Icons.Info.S();
        var size  = ImGui.CalcTextSize(glyph);
        var min   = new Vector2(at.X, at.Y - size.Y * 0.5f);

        dl.AddText(min, ImGui.GetColorU32(Theme.TextFaint), glyph);

        if (ImGui.IsMouseHoveringRect(min, min + size) is false)
            return;

        if (hintContent is not null)
            Feedback.Tooltip(hintContent);
        else if (hint is not null)
            Feedback.Tooltip(hint);
    }
}
