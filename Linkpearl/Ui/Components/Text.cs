using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System.Numerics;

namespace Linkpearl.Ui.Components;

/// <summary>
/// Typographie.
/// </summary>
/// <remarks>
/// Chaque niveau encapsule sa police et sa couleur, pour que les appelants
/// n'aient jamais à pousser une police à la main.
///
/// Point important : <c>CalcTextSize</c> et les métriques de ligne dépendent de
/// la police courante. Toute mesure doit donc se faire à l'intérieur de la
/// portée de police, ce que ces aides garantissent.
/// </remarks>
internal static class Text
{
    /// <summary>Titre de page.</summary>
    public static void Title(string text, Vector4? color = null)
    {
        using var font = Fonts.PushTitle();
        ImGui.TextColored(color ?? Theme.Text, Glyphs.Safe(text));
    }

    /// <summary>En-tête de section, ou nom d'un pair sur sa carte.</summary>
    public static void H2(string text, Vector4? color = null)
    {
        using var font = Fonts.PushH2();
        ImGui.TextColored(color ?? Theme.Text, Glyphs.Safe(text));
    }

    public static void Body(string text, Vector4? color = null)
        => ImGui.TextColored(color ?? Theme.Text, Glyphs.Safe(text));

    public static void Muted(string text) => ImGui.TextColored(Theme.TextMuted, Glyphs.Safe(text));

    public static void Faint(string text) => ImGui.TextColored(Theme.TextFaint, Glyphs.Safe(text));

    public static void Small(string text, Vector4? color = null)
    {
        using var font = Fonts.PushSmall();
        ImGui.TextColored(color ?? Theme.TextMuted, Glyphs.Safe(text));
    }

    /// <summary>Texte replié sur la largeur disponible.</summary>
    public static void Wrapped(string text, Vector4? color = null)
    {
        // Le point de repli se calcule sur le bord intérieur de la carte et non
        // sur celui de la fenêtre, sinon le texte déborde sous la marge.
        var wrapAt = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - Card.RightInset;

        ImGui.PushTextWrapPos(wrapAt);
        ImGui.TextColored(color ?? Theme.Text, Glyphs.Safe(text));
        ImGui.PopTextWrapPos();
    }

    /// <summary>
    /// Icône suivie d'un libellé sur la même ligne.
    /// </summary>
    /// <remarks>
    /// FontAwesome étant fusionné dans la police de corps, aucune bascule de
    /// police n'est nécessaire.
    /// </remarks>
    public static void WithIcon(FontAwesomeIcon icon, string text,
                                Vector4? iconColor = null, Vector4? textColor = null, bool wrap = false)
    {
        ImGui.TextColored(iconColor ?? Theme.TextMuted, icon.S());
        ImGui.SameLine(0f, Theme.S(Theme.GapS));

        if (wrap)
            Wrapped(text, textColor);
        else
            ImGui.TextColored(textColor ?? Theme.Text, Glyphs.Safe(text));
    }

    /// <summary>Icône seule.</summary>
    public static void Icon(FontAwesomeIcon icon, Vector4? color = null)
        => ImGui.TextColored(color ?? Theme.TextMuted, icon.S());
}
