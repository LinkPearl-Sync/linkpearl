using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using System.Numerics;

namespace Linkpearl.Ui;

/// <summary>
/// Le logo du plugin, celui du site : deux mogs et leur perle.
/// </summary>
/// <remarks>
/// Statique comme <see cref="Fonts"/> : la barre de titre et les pages vides
/// sont des aides statiques, et leur passer la texture de main en main
/// traverserait toutes les pages.
///
/// Tant que la texture n'est pas chargée, rien n'est dessiné et
/// <see cref="Draw"/> rend faux : l'appelant garde alors sa place ou son icône.
/// </remarks>
internal static class Brand
{
    private static ISharedImmediateTexture? _logo;

    public static void Initialize(ISharedImmediateTexture logo) => _logo = logo;

    public static void Dispose() => _logo = null;

    /// <summary>Dessine le logo dans le carré donné. Rend faux s'il n'est pas encore chargé.</summary>
    /// <param name="glow">Un halo derrière, pour les grandes tailles.</param>
    public static bool Draw(ImDrawListPtr dl, Vector2 min, float side, bool glow = false)
    {
        if (_logo?.GetWrapOrDefault() is not { } wrap)
            return false;

        if (glow)
            Surface.Halo(dl, min + new Vector2(side * 0.5f), new Vector2(side * 0.8f),
                         Theme.Alpha(Theme.Accent, 0.35f));

        dl.AddImage(wrap.Handle, min, min + new Vector2(side, side));
        return true;
    }
}
