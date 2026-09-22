using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System.Numerics;

namespace Linkpearl.Ui.Components;

/// <summary>
/// Puce : une étiquette courte sur fond teinté.
/// </summary>
/// <remarks>
/// Dessinée à la main plutôt qu'avec un bouton désactivé : il faut un fond
/// arrondi en gélule, une couleur par état, et surtout qu'elle ne réagisse pas
/// au survol. Un bouton grisé invite au clic, une puce non.
/// </remarks>
internal static class Chip
{
    public static void Draw(string label, Vector4 tint, FontAwesomeIcon? icon = null)
    {
        using var font = Fonts.PushSmall();

        var text = icon is { } value ? $"{value.S()}  {Glyphs.Safe(label)}" : Glyphs.Safe(label);
        var size = ImGui.CalcTextSize(text);

        var padX = Theme.S(Theme.GapM);
        var padY = Theme.S(Theme.GapXs);

        var origin = ImGui.GetCursorScreenPos();
        var max    = new Vector2(origin.X + size.X + padX * 2f, origin.Y + size.Y + padY * 2f);
        var dl     = ImGui.GetWindowDrawList();

        // Le rayon vaut la moitié de la hauteur : la gélule est parfaite quelle
        // que soit l'échelle, là où un rayon fixe s'aplatit en grandissant.
        var radius = (max.Y - origin.Y) * 0.5f;

        dl.AddRectFilled(origin, max, ImGui.GetColorU32(Theme.Alpha(tint, 0.16f)), radius);
        dl.AddRect(origin, max, ImGui.GetColorU32(Theme.Alpha(tint, 0.40f)), radius, ImDrawFlags.None, 1f);
        dl.AddText(origin + new Vector2(padX, padY), ImGui.GetColorU32(tint), text);

        ImGui.Dummy(new Vector2(max.X - origin.X, max.Y - origin.Y));
    }
}
