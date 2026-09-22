using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace Linkpearl.Ui.Components;

internal enum BtnTone
{
    /// <summary>Action principale de l'écran. Une seule par vue.</summary>
    Primary,

    /// <summary>Action courante, surface neutre surélevée.</summary>
    Secondary,

    /// <summary>Action discrète : pas de fond, sauf au survol.</summary>
    Ghost,

    /// <summary>Action destructrice.</summary>
    Danger,

    /// <summary>Action de confirmation.</summary>
    Success,
}

internal enum BtnSize
{
    Small,
    Medium,

    /// <summary>Occupe toute la largeur disponible.</summary>
    Block,
}

/// <summary>
/// Boutons.
/// </summary>
/// <remarks>
/// La couleur du libellé est déduite de la luminance du fond, ce qui garantit
/// la lisibilité même sur l'accent nacré, qui est clair, et permet de changer
/// la palette sans repasser sur chaque bouton.
/// </remarks>
internal static class Btn
{
    public static bool Draw(string label,
                            BtnTone tone = BtnTone.Secondary,
                            BtnSize size = BtnSize.Medium,
                            FontAwesomeIcon? icon = null,
                            bool disabled = false,
                            string? tooltip = null,
                            string? id = null)
    {
        var (normal, hovered, active) = Palette(tone);
        var caption = Compose(label, icon);

        using var color = ImRaii.PushColor(ImGuiCol.Button, normal)
                                .Push(ImGuiCol.ButtonHovered, hovered)
                                .Push(ImGuiCol.ButtonActive,  active)
                                .Push(ImGuiCol.Text,          Theme.TextOn(normal));

        // Le contour des champs de saisie ne doit pas déborder sur les boutons,
        // qui se distinguent déjà par leur fond.
        using var flat = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 0f);

        bool clicked;

        using (ImRaii.Disabled(disabled))
            clicked = ImGui.Button($"{caption}##{id ?? label}", Dimensions(size, caption));

        // Hors de la portée désactivée : un widget désactivé ne remonte pas le survol.
        if (tooltip != null)
            Feedback.TooltipOnHover(tooltip);

        return clicked && disabled is false;
    }

    /// <summary>Bouton réduit à une icône, carré.</summary>
    public static bool Icon(FontAwesomeIcon icon, string id,
                            BtnTone tone = BtnTone.Ghost,
                            string? tooltip = null,
                            bool disabled = false)
    {
        var (normal, hovered, active) = Palette(tone);
        var side = ImGui.GetFrameHeight();

        using var color = ImRaii.PushColor(ImGuiCol.Button, normal)
                                .Push(ImGuiCol.ButtonHovered, hovered)
                                .Push(ImGuiCol.ButtonActive,  active)
                                .Push(ImGuiCol.Text,          Theme.TextOn(normal));

        using var flat = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 0f);

        bool clicked;

        using (ImRaii.Disabled(disabled))
            clicked = ImGui.Button($"{icon.S()}##{id}", new Vector2(side, side));

        if (tooltip != null)
            Feedback.TooltipOnHover(tooltip);

        return clicked && disabled is false;
    }

    /// <summary>Largeur qu'occuperait le bouton, pour aligner à droite.</summary>
    public static float Measure(string label, BtnSize size = BtnSize.Medium, FontAwesomeIcon? icon = null)
    {
        var caption = Compose(label, icon);
        var width   = Dimensions(size, caption).X;

        return width > 0f ? width : Width(caption);
    }

    private static string Compose(string label, FontAwesomeIcon? icon)
        => icon is { } value ? $"{value.S()}  {label}" : label;

    /// <summary>
    /// Encombrement d'un bouton.
    /// </summary>
    /// <remarks>
    /// La petite taille donne une largeur régulière aux boutons courts, qui
    /// s'alignent ainsi les uns sous les autres, mais c'est un plancher et non
    /// une largeur imposée : un libellé plus long l'emporte. Fixée pour de bon,
    /// elle rognerait le texte, et un libellé tronqué se lit comme un bug.
    /// </remarks>
    private static Vector2 Dimensions(BtnSize size, string caption) => size switch
    {
        BtnSize.Block => new Vector2(-1f, 0f),
        BtnSize.Small => new Vector2(Math.Max(Theme.S(88f), Width(caption)), 0f),
        _             => Vector2.Zero,   // largeur ajustée au contenu
    };

    /// <summary>Largeur du libellé, marges du cadre comprises.</summary>
    private static float Width(string caption)
        => ImGui.CalcTextSize(caption).X + ImGui.GetStyle().FramePadding.X * 2f;

    private static (Vector4 Normal, Vector4 Hovered, Vector4 Active) Palette(BtnTone tone) => tone switch
    {
        BtnTone.Primary => (Theme.Accent, Theme.AccentHover, Theme.AccentActive),
        BtnTone.Danger  => (Theme.Danger, Theme.DangerHover, Theme.Mix(Theme.Danger, Theme.BgBase, 0.3f)),
        BtnTone.Success => (Theme.Online, Theme.Mix(Theme.Online, Theme.Text, 0.2f),
                            Theme.Mix(Theme.Online, Theme.BgBase, 0.3f)),
        BtnTone.Ghost   => (Vector4.Zero, Theme.Mix(Theme.BgRaised, Theme.Accent, 0.18f), Theme.BgHover),
        _               => (Theme.BgRaised, Theme.Mix(Theme.BgHover, Theme.Accent, 0.15f), Theme.BgSurface),
    };
}
