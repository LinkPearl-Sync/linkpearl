using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace Linkpearl.Ui;

/// <summary>
/// Rendu des surfaces : ombre portée, fond, liseré, bordure, et la nuit du site,
/// son halo et sa perle.
/// </summary>
/// <remarks>
/// Sur fond sombre, une carte qui n'a qu'un fond légèrement différent reste
/// invisible. Ce qui la détache, c'est la combinaison de trois signaux : une
/// ombre diffuse en dessous, une bordure nette, et un liseré clair sur l'arête
/// haute qui simule une lumière zénithale.
///
/// ImGui n'a ni flou ni dégradé radial, et les bindings de Dalamud n'exposent
/// pas d'ellipse. Le halo est un éventail de triangles dont les sommets portent
/// la couleur : la carte graphique interpole d'un sommet à l'autre, ce qui donne
/// un dégradé continu, sans les marches qu'aurait une pile de cercles.
/// </remarks>
internal static class Surface
{
    /// <summary>
    /// Ombre portée diffuse.
    /// </summary>
    /// <remarks>
    /// Empile des contours de plus en plus larges et de plus en plus
    /// transparents : bien moins coûteux qu'un flou réel, et visuellement
    /// suffisant à cette échelle.
    /// </remarks>
    public static void Shadow(ImDrawListPtr dl, Vector2 min, Vector2 max,
                              float rounding, float spread = 6f, float opacity = 1f)
    {
        var steps = Math.Max(1, (int)MathF.Round(Theme.S(spread)));

        for (var i = steps; i >= 1; i--)
        {
            var t     = i / (float)steps;   // 1 au plus large, proche de 0 au plus serré
            var alpha = Theme.Shadow.W * (1f - t) * (1f - t) * 0.5f * opacity;

            if (alpha <= 0.002f)
                continue;

            var grow = new Vector2(i, i);

            dl.AddRect(
                min - grow,
                max + grow + new Vector2(0f, Theme.S(1f)),   // décalage bas : lumière zénithale
                ImGui.GetColorU32(Theme.Alpha(Theme.Shadow, alpha)),
                rounding + i,
                ImDrawFlags.None,
                1f);
        }
    }

    /// <summary>Peint une surface complète : ombre, fond, liseré haut, puis bordure.</summary>
    public static void Panel(ImDrawListPtr dl, Vector2 min, Vector2 max,
                             Vector4 background, Vector4? border = null,
                             float? rounding = null, bool shadow = true,
                             bool highlight = true, float shadowSpread = 6f)
    {
        var r = rounding ?? Theme.S(Theme.RadiusCard);

        if (shadow)
            Shadow(dl, min, max, r, shadowSpread);

        dl.AddRectFilled(min, max, ImGui.GetColorU32(background), r);

        if (highlight)
        {
            // Le liseré s'arrête avant les angles pour ne pas déborder de l'arrondi.
            var inset = r * 0.7f;

            dl.AddLine(
                new Vector2(min.X + inset, min.Y + 0.5f),
                new Vector2(max.X - inset, min.Y + 0.5f),
                ImGui.GetColorU32(Theme.Highlight),
                1f);
        }

        dl.AddRect(min, max, ImGui.GetColorU32(border ?? Theme.Border), r, ImDrawFlags.None, 1f);
    }

    /// <summary>Barre d'accent verticale collée au bord gauche d'une surface.</summary>
    public static void AccentBar(ImDrawListPtr dl, Vector2 min, Vector2 max,
                                 Vector4 color, float width = 3f, float? rounding = null)
    {
        var r = rounding ?? Theme.S(Theme.RadiusCard);

        dl.AddRectFilled(
            min,
            new Vector2(min.X + Theme.S(width), max.Y),
            ImGui.GetColorU32(color),
            r,
            ImDrawFlags.RoundCornersLeft);
    }

    /// <summary>Paliers du halo, du centre au bord : (fraction du rayon, fraction de l'alpha).</summary>
    /// <remarks>
    /// Un seul palier donnerait un cône, dont la pointe se voit au centre. Le
    /// palier du milieu tasse la lumière vers le cœur, comme un halo réel.
    /// </remarks>
    private static readonly (float Radius, float Alpha)[] HaloStops = [(0f, 1f), (0.45f, 0.45f), (1f, 0f)];

    /// <summary>Tache de lumière elliptique, pleine au centre, nulle au bord.</summary>
    public static void Halo(ImDrawListPtr dl, Vector2 center, Vector2 radius, Vector4 color, int segments = 48)
    {
        var uv    = ImGui.GetFontTexUvWhitePixel();
        var quads = segments * (HaloStops.Length - 1);

        // Six sommets par quadrilatère, sans partage : PrimVtx écrit l'index
        // avec le sommet, ce qui évite de tenir soi-même le compte des indices.
        dl.PrimReserve(quads * 6, quads * 6);

        for (var ring = 1; ring < HaloStops.Length; ring++)
        {
            var (innerRadius, innerAlpha) = HaloStops[ring - 1];
            var (outerRadius, outerAlpha) = HaloStops[ring];

            var inner = ImGui.GetColorU32(Theme.Alpha(color, color.W * innerAlpha));
            var outer = ImGui.GetColorU32(Theme.Alpha(color, color.W * outerAlpha));

            for (var i = 0; i < segments; i++)
            {
                var a = OnEllipse(center, radius * innerRadius, i, segments);
                var b = OnEllipse(center, radius * innerRadius, i + 1, segments);
                var c = OnEllipse(center, radius * outerRadius, i + 1, segments);
                var d = OnEllipse(center, radius * outerRadius, i, segments);

                dl.PrimVtx(a, uv, inner);
                dl.PrimVtx(d, uv, outer);
                dl.PrimVtx(c, uv, outer);

                dl.PrimVtx(a, uv, inner);
                dl.PrimVtx(c, uv, outer);
                dl.PrimVtx(b, uv, inner);
            }
        }
    }

    private static Vector2 OnEllipse(Vector2 center, Vector2 radius, int step, int segments)
    {
        var angle = MathF.Tau * step / segments;
        return center + new Vector2(MathF.Cos(angle) * radius.X, MathF.Sin(angle) * radius.Y);
    }

    /// <summary>
    /// Le fond du site : un dégradé vertical de la nuit, et le halo bleu du haut.
    /// </summary>
    /// <remarks>
    /// <c>AddRectFilledMultiColor</c> ne sait pas arrondir : le dégradé couvre
    /// le milieu, et deux bandes unies de la hauteur du rayon, aux couleurs de
    /// ses extrémités, portent les angles. La couture ne se voit pas, les
    /// couleurs se rejoignent exactement.
    /// </remarks>
    public static void NightBackground(ImDrawListPtr dl, Vector2 min, Vector2 max,
                                       float rounding, bool roundTop, float opacity)
    {
        var top    = ImGui.GetColorU32(Theme.Alpha(Theme.NightTop, opacity));
        var bottom = ImGui.GetColorU32(Theme.Alpha(Theme.BgBase, opacity));
        var r      = Math.Min(rounding, (max.Y - min.Y) * 0.5f);

        dl.AddRectFilled(min, new Vector2(max.X, min.Y + r + 1f), top, roundTop ? r : 0f,
                         roundTop ? ImDrawFlags.RoundCornersTop : ImDrawFlags.None);
        dl.AddRectFilledMultiColor(new Vector2(min.X, min.Y + r), new Vector2(max.X, max.Y - r),
                                   top, top, bottom, bottom);
        dl.AddRectFilled(new Vector2(min.X, max.Y - r - 1f), max, bottom, r, ImDrawFlags.RoundCornersBottom);

        // L'ellipse du site, 70 % de la largeur, remontée contre le bord haut et
        // adoucie. Vu en jeu le 26 septembre : centrée à 160 px et pleine, elle
        // tombait juste derrière le texte des pages, qui perdait son contraste.
        // Sa largeur n'atteint pas les angles, et le rognage l'arrête au bord.
        var width = max.X - min.X;

        dl.PushClipRect(min, max, true);
        Halo(dl, new Vector2(min.X + width * 0.5f, min.Y + Theme.S(40f)),
             new Vector2(width * 0.35f, Theme.S(220f)), Theme.Alpha(Theme.NightHalo, opacity * 0.60f));
        dl.PopClipRect();
    }

    /// <summary>
    /// Le halo d'un élément actif, autour de son rectangle arrondi.
    /// </summary>
    /// <remarks>
    /// Même technique que l'ombre : des contours de plus en plus larges et
    /// transparents. À peindre avant l'élément, qui recouvre l'intérieur.
    /// </remarks>
    public static void Glow(ImDrawListPtr dl, Vector2 min, Vector2 max, float rounding,
                            Vector4 color, float spread = 7f)
    {
        var steps = Math.Max(1, (int)MathF.Round(Theme.S(spread)));

        for (var i = 1; i <= steps; i++)
        {
            var t     = i / (float)steps;
            var alpha = color.W * 0.30f * (1f - t) * (1f - t);

            if (alpha <= 0.004f)
                continue;

            var grow = new Vector2(i, i);

            dl.AddRect(min - grow, max + grow, ImGui.GetColorU32(Theme.Alpha(color, alpha)),
                       rounding + i, ImDrawFlags.None, 1.5f);
        }
    }

    /// <summary>
    /// La pastille perle du site : bord lavande, corps bleu, reflet blanc en haut à gauche.
    /// </summary>
    /// <remarks>
    /// Des disques décalés vers la source de lumière plutôt qu'un dégradé
    /// radial : à quelques pixels, un dégradé se lit comme une bille unie,
    /// alors que le reflet net fait la perle.
    /// </remarks>
    public static void Pearl(ImDrawListPtr dl, Vector2 center, float radius, bool glow = true)
    {
        if (glow)
            Halo(dl, center, new Vector2(radius * 2.4f), Theme.Alpha(Theme.PearlGlow, 0.55f), 24);

        var light = new Vector2(-radius, -radius);

        dl.AddCircleFilled(center, radius, ImGui.GetColorU32(Theme.PearlRim));
        dl.AddCircleFilled(center + light * 0.10f, radius * 0.82f, ImGui.GetColorU32(Theme.PearlBody));
        dl.AddCircleFilled(center + light * 0.28f, radius * 0.50f, ImGui.GetColorU32(Theme.PearlLight));
        dl.AddCircleFilled(center + light * 0.36f, radius * 0.22f, ImGui.GetColorU32(Theme.PearlShine));
    }
}
