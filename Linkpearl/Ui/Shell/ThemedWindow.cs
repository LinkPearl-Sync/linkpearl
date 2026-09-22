using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace Linkpearl.Ui.Shell;

/// <summary>
/// Classe de base des fenêtres du plugin.
/// </summary>
/// <remarks>
/// Le thème est appliqué avant que Dalamud n'appelle <c>ImGui.Begin</c>, et
/// dépilé après <c>ImGui.End</c>. Pousser le style depuis <c>Draw()</c> serait
/// sans effet sur le fond, les arrondis et la bordure de la fenêtre :
/// <c>Begin</c> a déjà eu lieu.
///
/// Le style n'est jamais écrit dans <c>ImGui.GetStyle()</c>, qui est partagé
/// avec Dalamud et tous les autres plugins.
/// </remarks>
public abstract class ThemedWindow : Window
{
    private readonly ThemeStack _stack = new();

    /// <summary>
    /// Portée de la police de corps, tenue pour toute la durée du rendu.
    /// </summary>
    /// <remarks>
    /// En champ parce que <c>PreDraw</c> et <c>PostDraw</c> sont deux appels
    /// distincts. Vaut une portée vide tant que l'atlas n'est pas construit.
    /// </remarks>
    private IDisposable? _fontScope;

    /// <summary>
    /// Vrai pour une fenêtre à chrome maison : la marge intérieure tombe à zéro
    /// pour que le shell peigne bord à bord.
    /// </summary>
    protected virtual bool Chromeless => false;

    /// <summary>
    /// Contraintes de taille en pixels logiques, à cent pour cent d'échelle.
    /// </summary>
    /// <remarks>
    /// Remises à l'échelle à chaque image, ce que ne fait pas
    /// <see cref="Window.SizeConstraints"/> : une contrainte figée au
    /// constructeur tronque le contenu dès que l'utilisateur passe à 150 %.
    /// </remarks>
    protected WindowSizeConstraints? LogicalSizeConstraints { get; set; }

    /// <summary>
    /// Opacité du fond de fenêtre.
    /// </summary>
    /// <remarks>
    /// Légèrement translucide, avec le flou natif de Dalamud derrière : le décor
    /// du jeu transparaît sans nuire à la lisibilité du texte.
    /// </remarks>
    protected virtual float BackgroundOpacity => 0.94f;

    protected ThemedWindow(string name, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
        : base(name, flags | ImGuiWindowFlags.NoCollapse)
    {
        AllowBackgroundBlur = true;

        // Boutons de titre redessinés plutôt que natifs. ImGui pose le repli et
        // la croix l'un contre l'autre sans espacement réglable ; Dalamud, lui,
        // espace correctement les boutons qu'on lui fournit. On retire donc le
        // repli, dont personne ne se sert en jeu, et on redessine la fermeture.
        ShowCloseButton = false;

        TitleBarButtons.Add(new TitleBarButton
        {
            Icon       = FontAwesomeIcon.Times,
            IconOffset = new Vector2(1.5f, 1f),
            Click      = _ => OnCloseButton(),
        });
    }

    /// <summary>
    /// Fermeture demandée depuis la barre de titre. Les fenêtres à chrome maison
    /// la neutralisent, la leur ayant sa propre croix.
    /// </summary>
    protected virtual void OnCloseButton() => IsOpen = false;

    public override void PreDraw()
    {
        // Avant les métriques : les contraintes et le calcul de disposition se
        // font avec la police effectivement utilisée pour le rendu.
        _fontScope = Fonts.PushBody();

        if (LogicalSizeConstraints is { } logical)
        {
            var scale = ImGuiHelpers.GlobalScale;

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = logical.MinimumSize * scale,
                MaximumSize = logical.MaximumSize * scale,
            };
        }

        _stack
            // ─── Arrondis ─────────────────────────────────────────────────────
            .Var(ImGuiStyleVar.WindowRounding,    Theme.S(Theme.RadiusWindow))
            .Var(ImGuiStyleVar.ChildRounding,     Theme.S(Theme.RadiusCard))
            .Var(ImGuiStyleVar.FrameRounding,     Theme.S(Theme.RadiusFrame))
            .Var(ImGuiStyleVar.PopupRounding,     Theme.S(Theme.RadiusCard))
            .Var(ImGuiStyleVar.ScrollbarRounding, Theme.S(8f))
            .Var(ImGuiStyleVar.GrabRounding,      Theme.S(Theme.RadiusFrame))
            .Var(ImGuiStyleVar.TabRounding,       Theme.S(Theme.RadiusFrame))

            // ─── Bordures ─────────────────────────────────────────────────────
            .Var(ImGuiStyleVar.WindowBorderSize, 1f)
            .Var(ImGuiStyleVar.ChildBorderSize,  0f)
            // Bordure sur les champs : sans elle, une saisie enfoncée sur fond
            // sombre n'a aucun contour et se confond avec le panneau.
            .Var(ImGuiStyleVar.FrameBorderSize,  1f)
            .Var(ImGuiStyleVar.PopupBorderSize,  1f)

            // ─── Espacements ──────────────────────────────────────────────────
            .Var(ImGuiStyleVar.WindowPadding, Chromeless
                                                  ? Vector2.Zero
                                                  : Theme.S(Theme.PadWindowX, Theme.PadWindowY))
            .Var(ImGuiStyleVar.FramePadding,     Theme.S(11f, 6f))
            .Var(ImGuiStyleVar.ItemSpacing,      Theme.S(Theme.GapM, 5f))
            .Var(ImGuiStyleVar.ItemInnerSpacing, Theme.S(Theme.GapL, 4f))
            .Var(ImGuiStyleVar.CellPadding,      Theme.S(Theme.GapM, Theme.GapS))
            .Var(ImGuiStyleVar.IndentSpacing,    Theme.S(18f))
            .Var(ImGuiStyleVar.ScrollbarSize,    Theme.S(10f))
            .Var(ImGuiStyleVar.GrabMinSize,      Theme.S(14f))

            // ─── Alignements ──────────────────────────────────────────────────
            .Var(ImGuiStyleVar.WindowTitleAlign,    new Vector2(0f,   0.5f))
            .Var(ImGuiStyleVar.ButtonTextAlign,     new Vector2(0.5f, 0.5f))
            .Var(ImGuiStyleVar.SelectableTextAlign, new Vector2(0f,   0.5f))

            // ─── Fonds et bordures ────────────────────────────────────────────
            // Seul le fond de fenêtre est translucide : les cartes restent
            // opaques, sans quoi le décor du jeu remonterait derrière le texte.
            .Color(ImGuiCol.WindowBg,     Theme.Alpha(Theme.BgBase, BackgroundOpacity))
            .Color(ImGuiCol.ChildBg,      Vector4.Zero)
            .Color(ImGuiCol.PopupBg,      Theme.BgSurface)
            .Color(ImGuiCol.Border,       Theme.BorderSoft)
            .Color(ImGuiCol.BorderShadow, Vector4.Zero)

            // ─── Texte ────────────────────────────────────────────────────────
            .Color(ImGuiCol.Text,           Theme.Text)
            .Color(ImGuiCol.TextDisabled,   Theme.TextMuted)
            .Color(ImGuiCol.TextSelectedBg, Theme.Alpha(Theme.Accent, 0.40f))

            // ─── Champs de saisie ─────────────────────────────────────────────
            .Color(ImGuiCol.FrameBg,        Theme.BgSunken)
            .Color(ImGuiCol.FrameBgHovered, Theme.BgSurface)
            .Color(ImGuiCol.FrameBgActive,  Theme.BgRaised)

            // ─── Barre de titre native, masquée sur le shell ───────────────────
            .Color(ImGuiCol.TitleBg,          Theme.BgSidebar)
            .Color(ImGuiCol.TitleBgActive,    Theme.BgSidebar)
            .Color(ImGuiCol.TitleBgCollapsed, Theme.BgSidebar)
            .Color(ImGuiCol.MenuBarBg,        Theme.BgSidebar)

            // ─── Ascenseurs ───────────────────────────────────────────────────
            .Color(ImGuiCol.ScrollbarBg,          Vector4.Zero)
            .Color(ImGuiCol.ScrollbarGrab,        Theme.Alpha(Theme.BorderLight, 0.60f))
            .Color(ImGuiCol.ScrollbarGrabHovered, Theme.BorderLight)
            .Color(ImGuiCol.ScrollbarGrabActive,  Theme.TextMuted)

            // ─── Contrôles ────────────────────────────────────────────────────
            .Color(ImGuiCol.CheckMark,        Theme.Accent)
            .Color(ImGuiCol.SliderGrab,       Theme.Accent)
            .Color(ImGuiCol.SliderGrabActive, Theme.AccentHover)
            .Color(ImGuiCol.Button,           Theme.BgRaised)
            .Color(ImGuiCol.ButtonHovered,    Theme.BgHover)
            .Color(ImGuiCol.ButtonActive,     Theme.BgSurface)
            .Color(ImGuiCol.Header,           Theme.BgSurface)
            .Color(ImGuiCol.HeaderHovered,    Theme.BgRaised)
            .Color(ImGuiCol.HeaderActive,     Theme.BgHover)

            // ─── Séparateurs et poignées ──────────────────────────────────────
            .Color(ImGuiCol.Separator,         Theme.BorderSoft)
            .Color(ImGuiCol.SeparatorHovered,  Theme.BorderLight)
            .Color(ImGuiCol.SeparatorActive,   Theme.Accent)
            .Color(ImGuiCol.ResizeGrip,        Theme.Alpha(Theme.BorderLight, 0.35f))
            .Color(ImGuiCol.ResizeGripHovered, Theme.Alpha(Theme.Accent, 0.70f))
            .Color(ImGuiCol.ResizeGripActive,  Theme.Accent)

            // ─── Onglets ──────────────────────────────────────────────────────
            .Color(ImGuiCol.Tab,        Theme.BgSunken)
            .Color(ImGuiCol.TabHovered, Theme.BgRaised)
            .Color(ImGuiCol.TabActive,  Theme.BgSurface)

            // ─── Tables ───────────────────────────────────────────────────────
            .Color(ImGuiCol.TableHeaderBg,     Theme.BgSurface)
            .Color(ImGuiCol.TableBorderStrong, Theme.Border)
            .Color(ImGuiCol.TableBorderLight,  Theme.Alpha(Theme.Border, 0.50f))
            .Color(ImGuiCol.TableRowBg,        Vector4.Zero)
            .Color(ImGuiCol.TableRowBgAlt,     Theme.Alpha(Theme.BgSurface, 0.5f))

            .Color(ImGuiCol.DragDropTarget, Theme.Accent);
    }

    public override void PostDraw()
    {
        // Ordre inverse de l'empilement.
        _stack.PopAll();
        _fontScope?.Dispose();
        _fontScope = null;
    }
}
