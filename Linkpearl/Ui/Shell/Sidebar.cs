using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace Linkpearl.Ui.Shell;

/// <summary>
/// Barre de navigation latérale : pictogramme et libellé par entrée, l'entrée
/// active signalée par un halo et un liseré.
/// </summary>
/// <remarks>
/// Les pictogrammes sont centrés sur une gouttière de largeur fixe, pour que
/// tous les libellés démarrent à la même abscisse quelle que soit la largeur du
/// glyphe.
/// </remarks>
internal static class Sidebar
{
    /// <summary>Abscisse du centre des pictogrammes.</summary>
    private const float IconGutter = 22f;

    /// <summary>Dessine la barre. Rend l'identifiant cliqué, ou null.</summary>
    public static string? Draw(IReadOnlyList<ShellPage> pages, string activeId, float height)
    {
        var width = Theme.S(Theme.SidebarWidth);

        // Angles vifs : c'est une colonne du cadre, pas une carte. Arrondie au
        // rayon des cartes, elle découpait des coins de nuit contre le contenu
        // et contre la barre d'état.
        using var background = ImRaii.PushColor(ImGuiCol.ChildBg, Theme.BgSidebar);
        using var rounding   = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 0f);
        using var child = ImRaii.Child("##sidebar", new Vector2(width, height), false,
                                       ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        if (!child)
            return null;

        // Séparation avec le contenu : la teinte seule ne suffit pas à marquer
        // la limite quand la page affiche une carte claire juste à côté.
        var dl  = ImGui.GetWindowDrawList();
        var top = ImGui.GetWindowPos();

        dl.AddLine(new Vector2(top.X + width - 0.5f, top.Y),
                   new Vector2(top.X + width - 0.5f, top.Y + height),
                   ImGui.GetColorU32(Theme.BorderSoft), 1f);

        string? clicked = null;
        var item = Theme.S(Theme.SidebarItem);

        ImGui.Dummy(new Vector2(0f, Theme.S(Theme.GapM)));

        foreach (var page in pages)
        {
            if (page.Pinned)
                continue;

            if (DrawItem(page, activeId, width, item))
                clicked = page.Id;
        }

        var pinned = pages.Where(page => page.Pinned).ToList();

        if (pinned.Count == 0)
            return clicked;

        // Entrées ancrées en bas de colonne.
        var needed = pinned.Count * item + Theme.S(Theme.GapM);
        var free   = height - ImGui.GetCursorPosY() - needed;

        if (free > 0f)
            ImGui.Dummy(new Vector2(0f, free));

        foreach (var page in pinned)
            if (DrawItem(page, activeId, width, item))
                clicked = page.Id;

        return clicked;
    }

    private static bool DrawItem(ShellPage page, string activeId, float width, float item)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl     = ImGui.GetWindowDrawList();

        ImGui.InvisibleButton($"##nav_{page.Id}", new Vector2(width, item));

        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();
        var active  = page.Id == activeId;

        if (active || hovered)
        {
            var inset = Theme.S(Theme.GapM);
            var min   = origin + new Vector2(inset, Theme.S(2f));
            var max   = origin + new Vector2(width - inset, item - Theme.S(2f));
            var r     = Theme.S(Theme.RadiusCard);

            // L'entrée active dans le halo, bordée comme le bouton de langue
            // choisi sur le site ; le survol, à peine éclairé.
            if (active)
                Surface.Glow(dl, min, max, r, Theme.Accent, spread: 5f);

            dl.AddRectFilled(min, max, ImGui.GetColorU32(active
                ? Theme.Alpha(Theme.Accent, 0.18f)
                : Theme.Alpha(Theme.Text, 0.06f)), r);

            if (active)
                dl.AddRect(min, max, ImGui.GetColorU32(Theme.Alpha(Theme.Accent, 0.80f)), r, ImDrawFlags.None, 1f);
        }

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var tint = ImGui.GetColorU32(active || hovered ? Theme.Text : Theme.TextMuted);

        // Icône alignée sur une gouttière fixe, pour que les libellés démarrent
        // tous à la même abscisse quelle que soit la largeur du pictogramme.
        // FontAwesome étant fusionné dans la police de corps, ni la mesure ni le
        // tracé ne demandent de bascule.
        var glyph     = page.Icon.S();
        var glyphSize = ImGui.CalcTextSize(glyph);
        var gutter    = Theme.S(IconGutter);

        dl.AddText(origin + new Vector2(gutter - glyphSize.X * 0.5f, (item - glyphSize.Y) * 0.5f), tint, glyph);

        var label     = page.Label();
        var labelSize = ImGui.CalcTextSize(label);

        dl.AddText(new Vector2(origin.X + Theme.S(IconGutter + 18f),
                               origin.Y + (item - labelSize.Y) * 0.5f), tint, label);

        var count = page.Badge?.Invoke() ?? 0;

        if (count > 0)
            DrawBadge(dl, origin, width, item, count);

        return clicked;
    }

    /// <summary>Pastille de compteur, alignée à droite de l'entrée.</summary>
    private static void DrawBadge(ImDrawListPtr dl, Vector2 origin, float width, float item, int count)
    {
        var text = count > 99 ? "99+" : count.ToString();

        using var font = Fonts.PushSmall();

        var size   = ImGui.CalcTextSize(text);
        var radius = Math.Max(Theme.S(8f), size.X * 0.7f);
        var center = origin + new Vector2(width - Theme.S(20f), item * 0.5f);

        dl.AddCircleFilled(center, radius, ImGui.GetColorU32(Theme.Accent));
        dl.AddText(center - size * 0.5f, ImGui.GetColorU32(Theme.TextOn(Theme.Accent)), text);
    }
}
