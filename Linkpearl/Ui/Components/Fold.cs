using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace Linkpearl.Ui.Components;

/// <summary>
/// En-tête repliable : chevron, titre, compteur.
/// </summary>
/// <remarks>
/// Remplace <c>CollapsingHeader</c>, dont le bandeau gris plein cadre jurait
/// avec la nuit et les cartes translucides. Ici, rien qu'un survol à peine
/// éclairé : l'en-tête range une liste, il n'est pas une surface.
///
/// L'état est gardé par identifiant, pour la session : une liste repliée le
/// reste d'une image à l'autre, et d'une ouverture de fenêtre à l'autre.
/// </remarks>
internal static class Fold
{
    private static readonly Dictionary<uint, bool> Open = [];

    /// <summary>Dessine l'en-tête. Rend vrai si le contenu doit être dessiné dessous.</summary>
    public static bool Draw(string title, int count, string id, bool defaultOpen = true)
    {
        var key = ImGui.GetID(id);

        if (Open.TryGetValue(key, out var open) is false)
        {
            open = defaultOpen;
            Open[key] = open;
        }

        var start  = ImGui.GetCursorScreenPos();
        var width  = Math.Max(1f, ImGui.GetContentRegionAvail().X - Card.RightInset);
        var height = ImGui.GetFrameHeight();

        if (ImGui.InvisibleButton($"##fold_{id}", new Vector2(width, height)))
        {
            open = !open;
            Open[key] = open;
        }

        var hovered = ImGui.IsItemHovered();
        var dl      = ImGui.GetWindowDrawList();

        if (hovered)
        {
            dl.AddRectFilled(start, start + new Vector2(width, height),
                ImGui.GetColorU32(Theme.Alpha(Theme.Text, 0.06f)), Theme.S(Theme.RadiusFrame));

            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var chevron = (open ? Icons.Expanded : Icons.Collapsed).S();
        var label   = Glyphs.Safe(title);
        var counter = $"{count}";

        var mid = start.Y + height * 0.5f;
        var x   = start.X + Theme.S(Theme.GapS);

        var chevronSize = ImGui.CalcTextSize(chevron);
        dl.AddText(new Vector2(x, mid - chevronSize.Y * 0.5f), ImGui.GetColorU32(Theme.TextFaint), chevron);

        // Le titre démarre à une abscisse fixe : les chevrons ouvert et fermé
        // n'ont pas la même largeur, et le titre sautait à chaque clic.
        x += ImGui.CalcTextSize(Icons.Expanded.S()).X + Theme.S(Theme.GapM);

        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(x, mid - labelSize.Y * 0.5f), ImGui.GetColorU32(Theme.Text), label);

        x += labelSize.X + Theme.S(Theme.GapS);

        using (Fonts.PushSmall())
        {
            var counterSize = ImGui.CalcTextSize(counter);
            dl.AddText(new Vector2(x, mid - counterSize.Y * 0.5f), ImGui.GetColorU32(Theme.TextFaint), counter);
        }

        return open;
    }
}
