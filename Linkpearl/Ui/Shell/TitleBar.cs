using Dalamud.Game.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System.Numerics;

namespace Linkpearl.Ui.Shell;

/// <summary>
/// Barre de titre maison, en remplacement de celle de Dalamud.
/// </summary>
/// <remarks>
/// Elle assume le déplacement de la fenêtre, que <c>NoTitleBar</c> supprime. Le
/// redimensionnement, lui, reste natif : la fenêtre ne pose pas <c>NoResize</c>,
/// ImGui gère donc les bords et la poignée.
/// </remarks>
internal static class TitleBar
{
    /// <summary>Dessine la barre. Rend vrai si la fermeture est demandée.</summary>
    public static bool Draw(float width)
    {
        var height = Theme.S(Theme.TitleBarHeight);
        var origin = ImGui.GetCursorScreenPos();
        var end    = new Vector2(origin.X + width, origin.Y + height);
        var dl     = ImGui.GetWindowDrawList();

        // Bandeau dans le ton foncé de l'accent : la nacre claire porterait mal
        // du texte clair, son contraste tombant sous le seuil de lisibilité.
        // Le rayon est celui de la fenêtre, sinon le fond déborde aux angles.
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Theme.AccentActive),
            Theme.S(Theme.RadiusWindow), ImDrawFlags.RoundCornersTop);

        // Reflet vers la gauche : un aplat uni sur toute la largeur est plus plat
        // qu'un bandeau qui capte la lumière d'un côté. C'est le seul endroit où
        // la nacre se comporte en nacre.
        var glow  = ImGui.GetColorU32(Theme.Alpha(Theme.Accent, 0.38f));
        var clear = ImGui.GetColorU32(Theme.Alpha(Theme.Accent, 0f));
        dl.AddRectFilledMultiColor(origin, end, glow, clear, clear, glow);

        // Bouton carré calé sur la hauteur de la barre, et non sur la hauteur
        // d'un cadre ImGui : celle-ci dépend de la police et du padding, si bien
        // que le fond ne tomberait pas au centre du bandeau.
        var margin = MathF.Round(Theme.S(Theme.GapS));
        var side   = MathF.Round(height - margin * 2f);
        var closed = false;

        // ── Zone de déplacement ───────────────────────────────────────────────
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##titledrag", new Vector2(Math.Max(1f, width - side - margin), height));

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);

        // ── Marque ────────────────────────────────────────────────────────────
        // Le symbole HQ du jeu, comme dans la barre de statut : le même glyphe
        // partout, pour que le plugin se reconnaisse d'un coup d'œil.
        using (Fonts.PushH2())
        {
            var glyph = SeIconChar.HighQuality.ToIconString();
            const string text = "Linkpearl";

            var glyphSize = ImGui.CalcTextSize(glyph);
            var textSize  = ImGui.CalcTextSize(text);
            var x = origin.X + Theme.S(Theme.PadWindowX);

            dl.AddText(new Vector2(x, origin.Y + (height - glyphSize.Y) * 0.5f),
                ImGui.GetColorU32(Theme.Text), glyph);

            x += glyphSize.X + Theme.S(Theme.GapS);

            dl.AddText(new Vector2(x, origin.Y + (height - textSize.Y) * 0.5f),
                ImGui.GetColorU32(Theme.Text), text);
        }

        // ── Fermeture ─────────────────────────────────────────────────────────
        var position = new Vector2(MathF.Round(end.X - margin - side), MathF.Round(origin.Y + margin));

        if (IconButton(dl, position, side, Icons.Close, "shell_close"))
            closed = true;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height));
        return closed;
    }

    /// <summary>
    /// Bouton d'icône dessiné à la main.
    /// </summary>
    /// <remarks>
    /// <c>ImGui.Button</c> dimensionne son fond à partir du cadre courant, donc
    /// de la police et du padding : sur un bandeau de hauteur fixe, il ne tombe
    /// pas au centre. Ici le carré est posé aux coordonnées voulues, et le
    /// glyphe centré sur son encombrement réel.
    /// </remarks>
    private static bool IconButton(ImDrawListPtr dl, Vector2 position, float side,
                                   FontAwesomeIcon icon, string id)
    {
        ImGui.SetCursorScreenPos(position);

        var clicked = ImGui.InvisibleButton($"##{id}", new Vector2(side, side));
        var hovered = ImGui.IsItemHovered();

        if (hovered)
        {
            dl.AddRectFilled(position, position + new Vector2(side, side),
                ImGui.GetColorU32(Theme.Alpha(Theme.Text, 0.16f)),
                Theme.S(Theme.RadiusFrame));

            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var glyph = icon.S();
        var size  = ImGui.CalcTextSize(glyph);

        dl.AddText(
            new Vector2(MathF.Round(position.X + (side - size.X) * 0.5f),
                        MathF.Round(position.Y + (side - size.Y) * 0.5f)),
            ImGui.GetColorU32(Theme.Text),
            glyph);

        return clicked;
    }
}
