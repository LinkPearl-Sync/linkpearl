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
        Draw(text, color ?? Theme.Text);
    }

    /// <summary>En-tête de section, ou nom d'un pair sur sa carte.</summary>
    public static void H2(string text, Vector4? color = null)
    {
        using var font = Fonts.PushH2();
        Draw(text, color ?? Theme.Text);
    }

    public static void Body(string text, Vector4? color = null) => Draw(text, color ?? Theme.Text);

    public static void Muted(string text) => Draw(text, Theme.TextMuted);

    public static void Faint(string text) => Draw(text, Theme.TextFaint);

    public static void Small(string text, Vector4? color = null)
    {
        using var font = Fonts.PushSmall();
        Draw(text, color ?? Theme.TextMuted);
    }

    /// <summary>Texte replié sur la largeur disponible.</summary>
    /// <remarks>Toutes les aides replient désormais : ce nom reste pour les appelants qui le disent.</remarks>
    public static void Wrapped(string text, Vector4? color = null) => Draw(text, color ?? Theme.Text);

    /// <summary>
    /// Replie tout le texte dessiné dans la portée sur une largeur fixe.
    /// </summary>
    /// <remarks>
    /// Pour les infobulles : leur fenêtre prend la taille de son contenu, donc
    /// la largeur disponible mesurée dedans est celle de l'image précédente, et
    /// replier dessus fait rétrécir l'infobulle d'image en image.
    /// </remarks>
    public static FixedWrapScope WrapAt(float width)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        _fixedWrapDepth++;
        return new FixedWrapScope();
    }

    internal ref struct FixedWrapScope
    {
        public void Dispose()
        {
            _fixedWrapDepth--;
            ImGui.PopTextWrapPos();
        }
    }

    private static int _fixedWrapDepth;

    /// <summary>En deçà, replier empile les mots un par ligne : mieux vaut rogner.</summary>
    private static float MinWrapWidth => Theme.S(60f);

    /// <summary>
    /// Dessine un texte replié au bord intérieur de la zone courante.
    /// </summary>
    /// <remarks>
    /// Sans repli, un texte plus large que la fenêtre est simplement rogné :
    /// réduire la fenêtre coupait les phrases en plein mot. Le point de repli se
    /// calcule sur le bord intérieur de la carte et non sur celui de la fenêtre,
    /// sinon le texte déborde sous la marge.
    /// </remarks>
    private static void Draw(string text, Vector4 color)
    {
        var safe = Glyphs.Safe(text);

        if (_fixedWrapDepth > 0)
        {
            ImGui.TextColored(color, safe);
            return;
        }

        var width = ImGui.GetContentRegionAvail().X - Card.RightInset;

        // Ne replier que ce qui déborde vraiment. Un texte qui tient est dessiné
        // tel quel : replié à sa propre largeur, il se coupait parfois sur un
        // arrondi, et une fenêtre ajustée à son contenu, comme le menu des effets
        // d'un pair, rétrécissait alors d'image en image. Et jamais sur une
        // largeur minuscule : derrière un SameLine au bord d'une carte, ImGui
        // replierait sur un pixel, un mot par ligne.
        if (width < MinWrapWidth || ImGui.CalcTextSize(safe).X <= width)
        {
            ImGui.TextColored(color, safe);
            return;
        }

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(color, safe);
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
                                Vector4? iconColor = null, Vector4? textColor = null)
    {
        ImGui.TextColored(iconColor ?? Theme.TextMuted, icon.S());
        ImGui.SameLine(0f, Theme.S(Theme.GapS));

        Draw(text, textColor ?? Theme.Text);
    }

    /// <summary>Icône seule.</summary>
    public static void Icon(FontAwesomeIcon icon, Vector4? color = null)
        => ImGui.TextColored(color ?? Theme.TextMuted, icon.S());
}
