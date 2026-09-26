using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;
using Linkpearl.Core.Safety;

namespace Linkpearl.Ui.Shell;

/// <summary>
/// Coque de la fenêtre principale : barre de titre maison, navigation latérale,
/// zone de contenu et barre d'état.
/// </summary>
/// <remarks>
/// La fenêtre hôte doit être déclarée sans chrome (<c>NoTitleBar</c>) et sans
/// marge intérieure, le shell peignant lui-même bord à bord.
///
///     ┌────────────────────────────────────────┐
///     │ Linkpearl                           ✕  │  barre de titre, déplaçable
///     ├──────────┬─────────────────────────────┤
///     │ ico      │                             │
///     │ ico      │  contenu de la page         │
///     │ ico      │                             │
///     │ ⚙        │                             │
///     ├──────────┴─────────────────────────────┤
///     │ ● Ysolde@Ravana        2 / 3 visibles  │
///     └────────────────────────────────────────┘
/// </remarks>
internal sealed class AppShell
{
    private readonly List<ShellPage> _pages;

    private string _activeId;

    public AppShell(IEnumerable<ShellPage> pages, string initialId)
    {
        _pages    = [.. pages];
        _activeId = initialId;
    }

    public string ActiveId => _activeId;

    /// <summary>Ce qu'on accepte de tous : animations, VFX, sons. Null pour ne pas montrer les bascules.</summary>
    public Func<TransientCategories>? Receive { get; init; }

    public Action<TransientCategories>? SetReceive { get; init; }

    public void Navigate(string pageId)
    {
        if (_pages.Exists(page => page.Id == pageId))
            _activeId = pageId;
    }

    /// <summary>
    /// Dessine la fenêtre entière.
    /// </summary>
    /// <param name="closeRequested">Vrai si la croix a été cliquée.</param>
    /// <param name="status">Ce que la barre d'état doit montrer.</param>
    /// <param name="fullScreen">
    /// Écran qui court-circuite la navigation, tout en gardant la barre de
    /// titre. Sert quand rien d'autre n'a de sens, par exemple hors du jeu.
    /// </param>
    public void Draw(out bool closeRequested, ShellStatus status, Action? fullScreen = null)
    {
        var available = ImGui.GetContentRegionAvail();

        closeRequested = TitleBar.Draw(available.X, Receive?.Invoke(), SetReceive);

        // L'interligne d'ImGui qui suivrait le corps est rendu au corps : sans
        // cela, une bande de nuit sépare la barre latérale de la barre d'état.
        var spacing    = ImGui.GetStyle().ItemSpacing.Y;
        var bodyHeight = available.Y
                       - Theme.S(Theme.TitleBarHeight)
                       - Theme.S(Theme.StatusBarHeight)
                       + spacing;

        if (fullScreen != null)
        {
            DrawContent("##shellfull", available.X, bodyHeight, fullScreen);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - spacing);
            StatusBar.Draw(status);
            return;
        }

        if (Sidebar.Draw(_pages, _activeId, bodyHeight) is { } clicked)
            _activeId = clicked;

        ImGui.SameLine(0f, 0f);

        DrawContent("##shellcontent",
                    available.X - Theme.S(Theme.SidebarWidth),
                    bodyHeight,
                    Active().Draw);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - spacing);
        StatusBar.Draw(status);
    }

    /// <summary>
    /// Zone de contenu, marges comprises.
    /// </summary>
    /// <remarks>
    /// Les marges sont obtenues en rétrécissant l'enfant plutôt qu'en poussant
    /// <c>WindowPadding</c> : ainsi la largeur disponible mesurée à l'intérieur
    /// est déjà la bonne, et ce qui demande toute la largeur ne déborde pas sous
    /// la marge droite.
    /// </remarks>
    private static void DrawContent(string id, float width, float height, Action body)
    {
        var padX = Theme.S(Theme.PadWindowX);
        var padY = Theme.S(Theme.PadWindowY);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padX);

        using var child = ImRaii.Child(id, new Vector2(width - padX * 2f, height), false);

        if (!child)
            return;

        ImGui.Dummy(new Vector2(0f, padY));
        body();
    }

    private ShellPage Active()
    {
        var page = _pages.Find(entry => entry.Id == _activeId);

        if (page != null)
            return page;

        _activeId = _pages[0].Id;
        return _pages[0];
    }
}
