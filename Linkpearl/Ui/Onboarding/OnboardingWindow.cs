using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Linkpearl.Ui.Components;
using Linkpearl.Ui.Shell;
using System.Numerics;

namespace Linkpearl.Ui.Onboarding;

/// <summary>
/// La présentation du premier lancement : six écrans, la bannière en tête.
/// </summary>
/// <remarks>
/// Montrée une fois par installation. La fermer par la croix compte comme
/// l'avoir vue : une présentation qui revient à chaque chargement apprend à
/// fermer sans lire. La première fois, sa fermeture ouvre aussi le cache,
/// qui attendait le choix de son dossier.
/// </remarks>
internal sealed class OnboardingWindow : ThemedWindow
{
    /// <summary>Le rapport de la bannière fournie, 1100×550.</summary>
    /// <remarks>
    /// Constant plutôt que lu sur la texture : tant qu'elle se charge, la zone
    /// garde sa taille et la mise en page ne saute pas.
    /// </remarks>
    private const float BannerAspect = 2f;

    private const float WindowWidth  = 580f;
    private const float WindowHeight = 600f;
    private const float StripHeight  = 78f;

    private readonly ISharedImmediateTexture _banner;
    private readonly CacheChooser _cacheChooser;
    private readonly Func<bool> _penumbraReady;
    private readonly Func<bool> _glamourerReady;
    private readonly Action _started;
    private readonly Action _closed;
    private readonly Step[] _steps;

    private int _step;

    private sealed record Step(string Title, string Body, Action? Art);

    public OnboardingWindow(
        ISharedImmediateTexture banner, CacheChooser cacheChooser,
        Func<bool> penumbraReady, Func<bool> glamourerReady, Action started, Action closed)
        : base("Bienvenue dans Linkpearl##onboarding",
               ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking)
    {
        _banner         = banner;
        _cacheChooser   = cacheChooser;
        _penumbraReady  = penumbraReady;
        _glamourerReady = glamourerReady;
        _started        = started;
        _closed         = closed;

        LogicalSizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(WindowWidth, WindowHeight),
            MaximumSize = new Vector2(WindowWidth, WindowHeight),
        };

        _steps =
        [
            new("Bienvenue",
                "Votre apparence moddée, visible par vos amis, directement de joueur à joueur.",
                null),
            new("Comment ça marche",
                "Vos fichiers passent directement de votre jeu à celui de votre ami, chiffrés. "
              + "Le rendez-vous vous aide seulement à vous trouver : il ne stocke rien, et rien "
              + "ne s'échange sans l'accord des deux.",
                OnboardingArt.HowItWorks),
            new("Se pairer, en jeu",
                "Un glyphe à côté du nom signale qui utilise Linkpearl. Clic droit sur le joueur, "
              + "« demander le pairage », et l'autre accepte d'un clic.",
                DrawPairing),
            new("Garder la main",
                "Mettez un pair en pause, ou bloquez les animations, les effets et les sons, pour "
              + "tout le monde ou pour un seul pair. Si une apparence se pose mal, « réappliquer » "
              + "est au clic droit.",
                OnboardingArt.Controls),
            new("Votre cache",
                "Les apparences reçues sont gardées sur le disque pour ne pas les retélécharger. "
              + "Choisissez où, et jusqu'à quelle taille.",
                DrawCache),
            new("Avant de commencer",
                "Linkpearl s'appuie sur Penumbra et Glamourer pour poser les apparences.",
                DrawReady),
        ];
    }

    /// <summary>Ouvre la présentation, toujours sur le premier écran.</summary>
    public void Show()
    {
        _step = 0;
        IsOpen = true;
    }

    public override void PreDraw()
    {
        base.PreDraw();

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
    }

    /// <summary>Toute fermeture compte : la croix, Échap, ou « C'est parti ».</summary>
    public override void OnClose() => _closed();

    protected override void DrawContents()
    {
        _cacheChooser.DrawDialogs();

        var step = _steps[_step];

        DrawBanner(_step == 0);
        ImGui.Dummy(Theme.S(0f, Theme.GapM));

        Text.Title(step.Title);
        ImGui.Dummy(Theme.S(0f, Theme.GapS));
        Text.Wrapped(step.Body, Theme.TextMuted);
        ImGui.Dummy(Theme.S(0f, Theme.GapL));

        step.Art?.Invoke();

        DrawNavigation();
    }

    private void DrawBanner(bool large)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var height = large ? width / BannerAspect : Theme.S(StripHeight);
        var size = new Vector2(height * BannerAspect, height);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (width - size.X) * 0.5f);
        ImGui.Image(_banner.GetWrapOrEmpty().Handle, size);
    }

    private void DrawPairing()
    {
        OnboardingArt.Pairing();
        ImGui.Dummy(Theme.S(0f, Theme.GapS));
        NameplateLegend.Draw();
    }

    private void DrawCache()
    {
        OnboardingArt.QuotaGauge(_cacheChooser.ShownQuotaGiB);
        ImGui.Dummy(Theme.S(0f, Theme.GapS));
        _cacheChooser.Draw();
    }

    private void DrawReady()
    {
        var penumbra = _penumbraReady();
        var glamourer = _glamourerReady();

        Prerequisite("Penumbra", penumbra);
        Prerequisite("Glamourer", glamourer);

        if (penumbra is false || glamourer is false)
        {
            ImGui.Dummy(Theme.S(0f, Theme.GapS));
            Feedback.Alert(Theme.Idle, Icons.Warning,
                "Sans eux, Linkpearl peut se pairer mais ne pourra rien afficher. Installez-les ou "
              + "activez-les depuis /xlplugins.");
        }

        ImGui.Dummy(Theme.S(0f, Theme.GapL));
        Feedback.Alert(Theme.Accent, Icons.Backup,
            "Après votre premier pairage, sauvegardez votre identité depuis les réglages : sans cela, "
          + "une réinstallation obligerait à refaire chaque pairage.");
    }

    private static void Prerequisite(string name, bool ready)
        => Text.WithIcon(ready ? Icons.Check : Icons.Decline,
                         ready ? $"{name} est prêt." : $"{name} est absent ou désactivé.",
                         ready ? Theme.Online : Theme.Danger);

    private void DrawNavigation()
    {
        var bottom = ImGui.GetWindowContentRegionMax().Y - Theme.S(32f);
        ImGui.SetCursorPosY(Math.Max(ImGui.GetCursorPosY(), bottom));

        // Les points de progression.
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var radius = Theme.S(4f);
        var spacing = Theme.S(14f);

        // L'étape courante en perle, les autres en points éteints : la même
        // pastille que la barre d'état, pour que le plugin parle une seule langue.
        for (var i = 0; i < _steps.Length; i++)
        {
            var center = origin + new Vector2(radius + spacing * i, Theme.S(14f));

            if (i == _step)
                Surface.Pearl(dl, center, radius * 1.25f, glow: false);
            else
                dl.AddCircleFilled(center, radius, ImGui.GetColorU32(Theme.Alpha(Theme.TextFaint, 0.5f)));
        }

        var last = _step == _steps.Length - 1;
        var nextLabel = last ? "C'est parti" : "Suivant";
        var nextWidth = Btn.Measure(nextLabel);
        var backWidth = Btn.Measure("Précédent");
        var right = ImGui.GetWindowContentRegionMax().X;

        if (_step > 0)
        {
            ImGui.SetCursorPosX(right - nextWidth - backWidth - Theme.S(Theme.GapS));

            if (Btn.Draw("Précédent", BtnTone.Ghost, id: "onboarding_back"))
                _step--;

            ImGui.SameLine(0f, Theme.S(Theme.GapS));
        }

        ImGui.SetCursorPosX(right - nextWidth);

        if (Btn.Draw(nextLabel, BtnTone.Action, id: "onboarding_next"))
        {
            if (last)
            {
                _started();
                IsOpen = false;
            }
            else
            {
                _step++;
            }
        }
    }
}
