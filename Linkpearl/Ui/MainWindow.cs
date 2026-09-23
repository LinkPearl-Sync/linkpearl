using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Linkpearl.Integration;
using Linkpearl.Ui.Pages;
using Linkpearl.Ui.Shell;
using System.Numerics;

namespace Linkpearl.Ui;

/// <summary>
/// La fenêtre principale : une coque à chrome maison, quatre pages.
/// </summary>
/// <remarks>
/// <b>Aucune clé n'y est montrée.</b> L'utilisateur voit des noms de
/// personnage, qui sont l'identité qui l'intéresse.
///
/// Le dessin ne fait que lire un état préparé ailleurs. Rien ici n'interroge le
/// réseau ni l'ObjectTable.
/// </remarks>
public sealed class MainWindow : ThemedWindow
{
    private readonly PairingService _pairing;
    private readonly PresenceService _presence;
    private readonly PluginState _state;
    private readonly Func<IReadOnlyList<PeerStatus>> _statuses;
    private readonly RequestsPage _requests;
    private readonly BackupCard _backup;
    private readonly AppShell _shell;

    public MainWindow(
        PairingService pairing, PresenceService presence, PluginState state, Configuration configuration,
        Func<IReadOnlyList<PeerStatus>> statuses, DiscoveryState discovery,
        Action<RendezvousAddress> discover,
        Action<NearbyPlayer> requestPair, Action<IncomingRequest> accept, Action<IncomingRequest> decline,
        Action<PeerId, bool> setPaused, Action<PeerId> reapply, Action<PeerId> unpair,
        Action<bool> setUploadLimited,
        Func<TransientCategories> globalReceive, Action<TransientCategories> setGlobalReceive,
        Action<PeerId, TransientCategories> setPairReceive,
        BackupState backupState, Action<string, string?> backup, Action<string, string?> restore)
        : base("Linkpearl",
               ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _pairing  = pairing;
        _presence = presence;
        _state    = state;
        _statuses = statuses;

        var nearby = new NearbyPage(state, presence, pairing, requestPair);
        var pairs  = new PairsPage(pairing, statuses, setPaused, reapply, unpair, setPairReceive);
        _backup = new BackupCard(backupState, backup, restore);
        var settings = new SettingsPage(configuration, discovery, discover, _backup, setUploadLimited);

        _requests = new RequestsPage(state, presence, accept, decline);

        _shell = new AppShell(
        [
            new ShellPage
            {
                Id = "nearby", Icon = Icons.Nearby, Label = () => "Autour", Draw = nearby.Draw,
            },
            new ShellPage
            {
                Id = "pairs", Icon = Icons.Pairs, Label = () => "Pairs", Draw = pairs.Draw,
                Badge = () => pairs.Count,
            },
            new ShellPage
            {
                Id = "requests", Icon = Icons.Requests, Label = () => "Demandes", Draw = _requests.Draw,
                Badge = () => _requests.Count,
            },
            new ShellPage
            {
                Id = "settings", Icon = Icons.Settings, Label = () => "Réglages", Draw = settings.Draw,
                Pinned = true,
            },
        ], "nearby")
        {
            Receive = globalReceive,
            SetReceive = setGlobalReceive,
        };

        LogicalSizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520f, 340f),
            MaximumSize = new Vector2(1100f, 1400f),
        };
    }

    /// <summary>Vrai quand la page des demandes est à l'écran.</summary>
    public bool ShowsRequests => IsOpen && _shell.ActiveId == "requests";

    /// <summary>Ouvre la fenêtre sur la page des demandes.</summary>
    public void OpenRequests()
    {
        IsOpen = true;
        _shell.Navigate("requests");
    }

    /// <summary>Le shell peint bord à bord, sans marge de fenêtre.</summary>
    protected override bool Chromeless => true;

    /// <summary>La croix est celle du shell, pas celle de Dalamud.</summary>
    protected override void OnCloseButton()
    {
    }

    public override void Draw()
    {
        // Une demande en attente passe devant : c'est la seule chose qui ne peut
        // pas attendre que l'utilisateur pense à aller voir.
        if (_requests.Count > 0 && _shell.ActiveId == "nearby")
            _shell.Navigate("requests");

        _shell.Draw(out var closeRequested, Status());
        _backup.DrawDialogs();

        if (closeRequested)
            IsOpen = false;
    }

    private ShellStatus Status()
    {
        var statuses = _statuses();

        return new ShellStatus(
            Connected: _presence.Connected,
            Failure: _presence.LastFailure,
            Character: _state.Self?.Display ?? "aucun personnage",
            Pairs: _pairing.Book.Active.Count(),
            Applied: statuses.Count(status => status.Applied));
    }
}
