using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport;
using Linkpearl.Core.Transport.Rendezvous;
using Linkpearl.Integration;
using Linkpearl.Ui;
using Linkpearl.Ui.Pages;

namespace Linkpearl;

/// <summary>
/// Point d'entrée du plugin.
/// </summary>
/// <remarks>
/// Le plugin n'est qu'une coquille autour de <c>Core</c> : il fournit les
/// adaptateurs Dalamud et l'interface, et ne porte aucune logique. Tout ce qui
/// décide se teste sous Linux, sans le jeu.
///
/// À ce stade (jalon 1) il n'y a ni réseau, ni pair, ni interface : seulement la
/// boucle locale qui lève les inconnues côté jeu.
/// </remarks>
public sealed class Plugin : IDalamudPlugin
{
    /// <summary>
    /// Le jeu intercepte « /linkpearl » avant Dalamud : c'est une commande de
    /// chat native. Toute commande choisie ici doit être vérifiée en jeu.
    /// </summary>
    private const string Command = "/lpearl";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager         Commands        { get; private set; } = null!;
    [PluginService] internal static IFramework              Framework       { get; private set; } = null!;
    [PluginService] internal static IChatGui                Chat            { get; private set; } = null!;
    [PluginService] internal static IObjectTable            Objects         { get; private set; } = null!;
    [PluginService] internal static IClientState            ClientState     { get; private set; } = null!;
    [PluginService] internal static IPlayerState            PlayerState     { get; private set; } = null!;
    [PluginService] internal static IContextMenu            ContextMenu     { get; private set; } = null!;
    [PluginService] internal static ICondition              Condition       { get; private set; } = null!;
    [PluginService] internal static IDtrBar                 DtrBar          { get; private set; } = null!;
    [PluginService] internal static IPluginLog              Log             { get; private set; } = null!;

    private readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;
    private readonly SelfLoop _selfLoop;
    private readonly FileSystemBlobStore _cache;
    private readonly PeerLinkFactory _links;
    private readonly LocalAppearance _appearance;
    private readonly RemoteApplicator _applicator;
    /// <summary>
    /// Le moteur n'existe qu'une fois un personnage connecté.
    /// </summary>
    /// <remarks>
    /// Il porte la clé d'identité, qui appartient au personnage : la construire
    /// à l'écran-titre reviendrait à en inventer une pour personne, que le
    /// premier connecté hériterait.
    /// </remarks>
    private SyncEngine? _engine;

    private ulong _character;

    /// <summary>Vrai pendant qu'une restauration réécrit les dossiers des personnages.</summary>
    /// <remarks>
    /// Le suivi du personnage se tait pendant ce temps : recharger le carnet au
    /// milieu, ou pire le réécrire, mélangerait l'ancien et le restauré.
    /// </remarks>
    private volatile bool _restoring;

    private readonly BackupState _backupState = new();
    private readonly string _root;
    private readonly string _legacyRoot;
    private readonly PairingService _pairing;
    private readonly PresenceService _presence;
    private readonly DalamudObjectSource _objectSource;
    private readonly PluginState _state = new();
    private readonly DiscoveryState _discovery = new();

    /// <summary>
    /// Regroupe les signaux de changement d'apparence en une reconstruction.
    /// </summary>
    /// <remarks>
    /// Un changement de tenue produit une dizaine de redessins en quelques
    /// centaines de millisecondes. Le plafond existe pour que quelqu'un qui
    /// bricole son apparence dix minutes finisse quand même par être annoncé.
    /// </remarks>
    private readonly Debouncer _appearanceChanged =
        new(new SystemClock(), TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(5));
    private readonly WindowSystem _windows = new("Linkpearl");
    private readonly MainWindow _window;
    private readonly StatusBarEntry _statusBar;
    private long _statusBarDueAt;
    private readonly Configuration _configuration;
    private readonly SystemClock _clock = new();
    private SyncEngineSettings _engineSettings = new();
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>Les empreintes visibles à la ronde précédente.</summary>
    /// <remarks>
    /// La détection n'interroge les services que lorsque cet ensemble change :
    /// redemander toutes les quinze secondes pour les mêmes personnes est du
    /// trafic pur, multiplié par le nombre de services.
    /// </remarks>
    private HashSet<Linkpearl.Core.Abstractions.PlayerFingerprint> _lastVisible = [];

    /// <summary>Quand la dernière interrogation a eu lieu.</summary>
    private DateTimeOffset _lastDetection = DateTimeOffset.MinValue;

    public Plugin()
    {
        var penumbra  = _penumbra  = new PenumbraIpc(PluginInterface);
        var glamourer = _glamourer = new GlamourerIpc(PluginInterface);
        _selfLoop = new SelfLoop(penumbra, glamourer, Framework, Log);

        _configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Un réglage d'avant la fédération devient une liste d'une entrée. Rien
        // n'est demandé à l'utilisateur, et rien n'est écrasé s'il a déjà choisi.
        _configuration.MigrateIfNeeded();

        // Le dossier que Dalamud attribue au plugin, et non un chemin de notre
        // invention : c'est là qu'une sauvegarde, une désinstallation ou un
        // utilisateur curieux iront chercher ce qui appartient à Linkpearl.
        var root = _root = PluginInterface.ConfigDirectory.FullName;
        Directory.CreateDirectory(root);

        // Le cache reste ailleurs : plusieurs gigaoctets qui se régénèrent n'ont
        // rien à faire dans un profil itinérant. C'est aussi l'emplacement
        // d'avant, donc les caches déjà constitués restent utilisables.
        _legacyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Linkpearl");

        // Le témoin des collections posées suit le plugin dans son dossier.
        // Il est repris de l'ancien emplacement, sans quoi les collections
        // laissées par la session d'avant ne seraient plus retirables.
        AdoptLeftoverMarker();

        var clock = _clock;
        _pairing = new PairingService(_configuration, clock, Log);
        _presence = new PresenceService(_configuration, () => _pairing.Identity, clock, Log);
        _objectSource = new DalamudObjectSource(Objects, ClientState, Framework);

        // Le moteur et ce qu'il lui faut. Une seule socket pour tous les pairs :
        // c'est son adresse publique que le rendez-vous rend, donc elle seule
        // qui aura percé le NAT.
        var engineSettings = new SyncEngineSettings
        {
            LimitUpload = _configuration.LimitUpload,
            Limiter = new RateLimiterSettings
            {
                CeilingBytesPerSecond = _configuration.UploadCeilingBytesPerSecond,
            },
        };

        _cache = new FileSystemBlobStore(
            _configuration.CacheDirectory is "" ? Path.Combine(_legacyRoot, "cache") : _configuration.CacheDirectory,
            new CacheSettings { QuotaBytes = _configuration.CacheQuotaBytes },
            clock,
            path => new DriveInfo(Path.GetPathRoot(path) ?? "/").AvailableFreeSpace);

        _links = new PeerLinkFactory(engineSettings.DataChannels + 1, new PluginLogSink(Log, "transport"));
        _appearance = new LocalAppearance(penumbra, glamourer, Framework, _cache, Log);

        // Tout changement de mod affectant le personnage produit un redessin, et
        // Glamourer signale la fin d'une application d'état. Les deux sont levés
        // depuis le thread du jeu : on ne fait que signaler, la reconstruction
        // part de la boucle de synchronisation.
        penumbra.SettingsChanged += _appearanceChanged.Signal;

        penumbra.Redrawn += index =>
        {
            if (index == 0)
                _appearanceChanged.Signal();
        };

        glamourer.Finalized += address =>
        {
            if (address == Objects.LocalPlayer?.Address)
                _appearanceChanged.Signal();
        };

        _applicator = new RemoteApplicator(
            penumbra, glamourer, Framework, Objects, ClientState, Condition, _cache, Quotas.Default, root, Log);

        _engineSettings = engineSettings;

        // Le personnage est inconnu au chargement : le plugin démarre à
        // l'écran-titre. Tout ce qui porte l'identité attend donc la connexion.
        Framework.Update += FollowCharacter;

        // PollEvents depuis le thread du jeu, jamais avec UnsyncedEvents : les
        // trames reçues remontent ainsi sur le fil qui a le droit de toucher au
        // jeu, et la cadence est celle d'une image.
        Framework.Update += PollLinks;

        // Les polices avant la fenêtre : l'atlas est construit en tâche de
        // fond, et la fenêtre retombe sur celle de Dalamud tant qu'il ne l'est
        // pas, sans jamais rester vide.
        Fonts.Build(PluginInterface);

        _window = new MainWindow(
            _pairing, _presence, _state, _configuration,
            () => _engine?.Statuses ?? [],
            _discovery,
            at => RunSafely(() => DiscoverAsync(at)),
            player => RunSafely(() => RequestPairAsync(player)),
            request => RunSafely(() => AcceptAsync(request)),
            Decline,
            (id, paused) => Report(_pairing.SetPaused(id, paused)),
            id => { _engine?.Reapply(id); Report("réapplication demandée."); },
            id => Report(_pairing.Remove(id)),
            SetUploadLimited,
            _backupState,
            (path, password) => RunSafely(() => BackupAsync(path, password)),
            (path, password) => RunSafely(() => RestoreAsync(path, password)));

        // Clic droit sur un personnage appairé : réappliquer, comme le font
        // les autres outils de synchronisation. C'est le geste que les joueurs
        // connaissent déjà.
        ContextMenu.OnMenuOpened += OnMenuOpened;

        _windows.AddWindow(_window);

        _statusBar = new StatusBarEntry(DtrBar, Open);
        Framework.Update += UpdateStatusBar;

        PluginInterface.UiBuilder.Draw += _windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Open;
        PluginInterface.UiBuilder.OpenConfigUi += Open;

        _ = Task.Run(() => RefreshLoopAsync(_shutdown.Token), _shutdown.Token);
        _ = Task.Run(() => SyncLoopAsync(_shutdown.Token), _shutdown.Token);

        Commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "capture | capture force | apply | revert",
        });

        Report($"chargé. Penumbra : {Describe(penumbra.TryGetVersion())}, Glamourer : {Describe(glamourer.TryGetVersion())}.");

        // Rattrapage d'une session précédente : un rechargement ou un plantage a
        // pu laisser une collection affectée au personnage, que plus rien en
        // mémoire ne sait retirer.
        if (_selfLoop.HasLeftovers())
        {
            Framework.RunOnFrameworkThread(_selfLoop.Revert);
            Report("une collection d'une session précédente a été retirée.");
        }

        Framework.RunOnFrameworkThread(() =>
        {
            var left = _applicator.CleanLeftovers();

            if (left > 0)
                Report($"{left} collection(s) de pair d'une session précédente ont été retirées.");
        });
    }

    /// <summary>Ajoute « réappliquer » au menu d'un personnage appairé.</summary>
    /// <remarks>
    /// Seulement pour un personnage du carnet : proposer l'entrée à tout le
    /// monde inviterait à cliquer pour rien, et dirait à qui regarde par-dessus
    /// l'épaule que le plugin est là.
    /// </remarks>
    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.Target is not MenuTargetDefault { TargetObject: IPlayerCharacter player })
            return;

        var name = player.Name.TextValue;

        if (string.IsNullOrWhiteSpace(name))
            return;

        var fingerprint = PlayerFingerprint.Of(
            DalamudObjectSource.Normalize(name), (ushort)player.HomeWorld.RowId);

        if (_pairing.Book.All.Any(pair => pair.PinnedFingerprint == fingerprint) is false)
            return;

        args.AddMenuItem(new MenuItem
        {
            Name = "Linkpearl : réappliquer",
            PrefixChar = 'L',
            PrefixColor = 541,
            OnClicked = _ =>
            {
                _engine?.Reapply(fingerprint);
                Report($"réapplication demandée pour {name}.");
            },
        });
    }

    /// <summary>Reprend le témoin des collections de l'emplacement précédent.</summary>
    private void AdoptLeftoverMarker()
    {
        const string marker = "remote-collections.txt";

        var from = Path.Combine(_legacyRoot, marker);
        var to = Path.Combine(_root, marker);

        try
        {
            if (File.Exists(from) && File.Exists(to) is false)
                File.Move(from, to);
        }
        catch (IOException e)
        {
            Log.Warning(e, "Reprise du témoin de collections impossible.");
        }
    }

    /// <summary>
    /// Suit le personnage connecté, et attache l'identité qui lui appartient.
    /// </summary>
    /// <remarks>
    /// Sur le changement de l'identifiant plutôt que sur les événements de
    /// connexion : le même test couvre l'arrivée à l'écran de sélection, le
    /// départ, et le passage d'un personnage à un autre sans déconnexion, qui
    /// ne lève pas les mêmes événements.
    /// </remarks>
    private void FollowCharacter(IFramework framework)
    {
        // L'identifiant de contenu du personnage connecté, que Dalamud expose
        // ici depuis qu'il a séparé l'état du joueur de celui du client.
        if (_restoring)
            return;

        var current = ClientState.IsLoggedIn ? PlayerState.ContentId : 0;

        if (current == _character)
            return;

        _character = current;

        try
        {
            ReleaseCharacter();

            if (current is not 0)
                TakeCharacter(current);
        }
        catch (Exception e)
        {
            // Une exception ici remonterait dans la boucle du jeu.
            Log.Error(e, "Changement de personnage en échec.");
            Report("l'identité de ce personnage n'a pas pu être chargée, voir le journal.");
        }
    }

    private void TakeCharacter(ulong contentId)
    {
        var root = CharacterStorage.Prepare(_root, contentId, _legacyRoot, Report);

        _pairing.Bind(root);

        _engine = new SyncEngine(
            _pairing.Book,
            new PeerConnector(
                _links,
                new RendezvousEndpoint(_configuration.RendezvousHost, _configuration.RendezvousPort),
                _clock,
                new PluginLogSink(Log, "moteur")),
            _appearance, _applicator, _cache, _pairing.Id!.Value, _pairing.Identity!.Key, _clock,
            new PluginLogSink(Log, "moteur"), _engineSettings);

        var pairs = _pairing.Book.All.Count;

        Report(pairs is 0
            ? $"identité de ce personnage : {_pairing.Id}. Aucun pair pour l'instant."
            : $"identité de ce personnage : {_pairing.Id}. {pairs} pair(s) au carnet.");
    }

    private void ReleaseCharacter()
    {
        var engine = _engine;

        _engine = null;
        _pairing.Unbind();

        if (engine is null)
            return;

        // Hors du thread du jeu : le moteur retire des pairs ce qu'il leur a
        // posé, ce qui passe par RunOnFrameworkThread, et l'attendre depuis ce
        // thread-là se bloquerait sur soi-même.
        _ = Task.Run(async () =>
        {
            try
            {
                await engine.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Warning(e, "Arrêt du moteur en échec au changement de personnage.");
            }
        });
    }

    private static string Describe((int Major, int Minor)? version)
        => version is { } v ? $"{v.Major}.{v.Minor}" : "absent";

    private void OnCommand(string _, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "capture":       RunSafely(() => CaptureAsync(force: false)); break;
            case "capture force": RunSafely(() => CaptureAsync(force: true));  break;
            case "invite":        RunSafely(InviteAsync);                      break;
            case "check":         RunSafely(CheckAsync);                       break;
            case "pairs":         ShowPairs();                                 break;
            case "id":
                Report(_pairing.Id is { } id
                    ? $"votre identifiant sur ce personnage : {id}"
                    : "connectez-vous d'abord : l'identité appartient au personnage.");
                break;
            case "":              Open();                                      break;
            case "diag":          RunSafely(DiagnoseAsync);                    break;
            case "unlock":        RunSafely(UnlockAsync);                      break;
            case "announce":      RunSafely(AnnounceAsync);                    break;
            case "sync":          ShowSync();                                  break;
            case "presence":      ShowPresence();                              break;
            case "rebuild":       _appearance.Rebuild(); Report("apparence en cours de reconstruction."); break;
            case "apply":     RunSafely(ApplyAsync);                        break;
            case "revert":    _selfLoop.Revert(); Report("personnage rendu à son état normal."); break;
            default:
                if (arguments.StartsWith("rdv ", StringComparison.OrdinalIgnoreCase))
                {
                    SetRendezvous(arguments[4..].Trim());
                    break;
                }

                if (arguments.StartsWith("pair ", StringComparison.OrdinalIgnoreCase))
                {
                    AddPair(arguments[5..].Trim());
                    break;
                }

                if (arguments.StartsWith("unpair ", StringComparison.OrdinalIgnoreCase))
                {
                    Report(_pairing.Remove(arguments[7..].Trim()));
                    break;
                }

                if (arguments.StartsWith("rename ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = arguments[7..].Trim().Split(' ', 2);
                    Report(parts.Length == 2
                        ? _pairing.Rename(parts[0], parts[1])
                        : "usage : /lpearl rename <ancien> <nouveau>");
                    break;
                }

                Report("invite | pair <nom> | check | pairs | rename <a> <b> | unpair <nom>");
                Report("rdv <hôte> | id | announce | sync | presence | rebuild | capture | apply | revert | diag | unlock");
                Report("capture | capture force | apply | revert");
                break;
        }
    }

    private void Open() => _window.IsOpen = true;

    /// <summary>Dit ce que le plugin a laissé sur le personnage, s'il a laissé quelque chose.</summary>
    private async Task DiagnoseAsync()
    {
        var report = await Framework.RunOnFrameworkThread(_selfLoop.Diagnose).ConfigureAwait(false);
        Report(report);
        Log.Information($"Diagnostic Linkpearl : {report}");
    }

    /// <summary>Retire toute mainmise sur Glamourer, sans toucher à l'apparence.</summary>
    private async Task UnlockAsync()
    {
        var released = await Framework.RunOnFrameworkThread(_selfLoop.UnlockGlamourer).ConfigureAwait(false);

        Report(released switch
        {
            < 0 => "Glamourer n'a pas répondu.",
            0 => "aucun verrou de notre part : Linkpearl ne tenait rien.",
            _ => $"{released} verrou(x) relâché(s).",
        });
    }

    /// <summary>
    /// Tient à jour ce que l'interface affiche.
    /// </summary>
    /// <remarks>
    /// En tâche de fond, à intervalle lâche : la détection interroge le
    /// rendez-vous, et le faire à chaque image en ferait un service de sondage.
    /// </remarks>
    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        while (ct.IsCancellationRequested is false)
        {
            try
            {
                if (_objectSource.IsLoggedIn)
                {
                    var self = await _objectSource.LocalAsync(ct).ConfigureAwait(false);
                    _state.Self = self;

                    if (self is not null)
                    {
                        await _presence.EnsureOpenAsync(self.Fingerprint, ct).ConfigureAwait(false);

                        // L'autre a dit oui à notre demande : le pair entre au
                        // carnet sans rien redemander à celui qui a invité.
                        while (_presence.TryTakeAcceptance(out var accepted) && accepted is not null)
                            Report($"{accepted.CharacterName} a accepté : {_pairing.AddFromRequest(accepted)}");

                        _state.Nearby = await _objectSource.SnapshotAsync(ct).ConfigureAwait(false);

                        // Mesuré : 345 Mo par mois et par joueur à trois
                        // secondes, contre 20 à quinze et seulement quand le
                        // champ change. La fédération multiplie encore ce coût
                        // par le nombre de services, ce qui fait de cette
                        // cadence une nécessité et non un confort.
                        var visible = _state.Nearby.Select(player => player.Fingerprint).ToHashSet();

                        // Le changement de champ n'est pas un critère suffisant.
                        // Un « non » n'est vrai qu'à l'instant où il est donné :
                        // celui qui vient d'arriver n'a pas encore ouvert sa
                        // boîte, et sans ce rattrapage sa réponse négative tient
                        // pour toujours, alors que les deux sont là, connectés,
                        // et que rien ne bouge plus. C'est exactement ainsi que
                        // deux personnages côte à côte ne se sont jamais vus.
                        var stale = _clock.UtcNow - _lastDetection > TimeSpan.FromSeconds(60);

                        if (visible.SetEquals(_lastVisible) is false || stale)
                        {
                            _lastVisible = visible;
                            _lastDetection = _clock.UtcNow;

                            await _presence.RefreshDetectionAsync(_state.Nearby, ct).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    _state.Nearby = [];
                    _lastVisible.Clear();
                }
            }
            catch (Exception e) when (ct.IsCancellationRequested is false)
            {
                Log.Warning(e, "Rafraîchissement en échec.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Demande à un service la liste de ceux qu'il connaît.
    /// </summary>
    /// <remarks>
    /// Le résultat n'est qu'une proposition : rien n'entre dans la liste de
    /// l'utilisateur sans qu'il coche une case. C'est ce qui empêche un annuaire
    /// de devenir une autorité.
    /// </remarks>
    private async Task DiscoverAsync(RendezvousAddress at)
    {
        _discovery.Reset();
        _discovery.From = at;
        _discovery.Running = true;

        try
        {
            await using var client = new RendezvousClient();
            await client.ConnectAsync(at.Host, at.Port, _shutdown.Token).ConfigureAwait(false);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));

            var offered = await client.QueryDirectoryAsync(deadline.Token).ConfigureAwait(false);

            _discovery.Offered = offered ?? [];

            if (offered is null)
                _discovery.Failure = "ce service ne publie pas d'annuaire.";
        }
        catch (Exception e)
        {
            _discovery.Failure = $"interrogation impossible : {e.Message}";
        }
        finally
        {
            _discovery.Running = false;
        }
    }

    private void PollLinks(IFramework framework) => _links.Poll();

    /// <summary>
    /// Tient à jour le compte de la barre de statut.
    /// </summary>
    /// <remarks>
    /// Une fois par seconde : l'instantané des joueurs proches ne change pas
    /// plus vite, et la barre est un nœud du jeu qu'on ne touche que d'ici.
    /// </remarks>
    private void UpdateStatusBar(IFramework framework)
    {
        var now = Environment.TickCount64;

        if (now < _statusBarDueAt)
            return;

        _statusBarDueAt = now + 1000;
        _statusBar.Update(_state.Nearby, _pairing.Book.All);

        RemindBackup();
    }

    /// <summary>
    /// Rappelle, une fois, qu'une sauvegarde existe.
    /// </summary>
    /// <remarks>
    /// Au premier pair et pas avant : c'est à ce moment qu'il y a quelque chose
    /// à perdre, et une réinstallation de Windows obligerait sinon à refaire
    /// chaque pairage.
    /// </remarks>
    private void RemindBackup()
    {
        if (_configuration.BackupReminded || _pairing.Book.All.Count == 0)
            return;

        _configuration.BackupReminded = true;
        _configuration.Save();

        Report("pensez à sauvegarder votre identité (Réglages, « Sauvegarde de l'identité ») : "
             + "sans elle, une réinstallation de Windows oblige à refaire chaque pairage.");
    }

    private string CharactersRoot => Path.Combine(_root, "characters");

    private void SetUploadLimited(bool limited)
    {
        _configuration.LimitUpload = limited;
        _configuration.Save();

        // Le moteur du personnage suivant naîtra avec le même réglage.
        _engineSettings = _engineSettings with { LimitUpload = limited };
        _engine?.SetUploadLimited(limited);
    }

    private async Task BackupAsync(string destination, string? password)
    {
        _backupState.Running = true;

        try
        {
            var (entries, unreadable) = await Task.Run(() => IdentityBackupService.Collect(CharactersRoot))
                .ConfigureAwait(false);

            if (entries.Count == 0)
            {
                Tell("aucun personnage à sauvegarder : connectez-vous d'abord avec chacun.", failed: true);
                return;
            }

            // PBKDF2 prend une demi-seconde : hors du thread du jeu.
            var content = await Task.Run(() => IdentityBackup.Write(entries, password)).ConfigureAwait(false);
            IdentityBackupService.WriteFile(destination, content);

            _configuration.BackupReminded = true;
            _configuration.Save();

            var saved = entries.Count == 1 ? "1 personnage sauvegardé" : $"{entries.Count} personnages sauvegardés";
            var skipped = unreadable > 0 ? $", {unreadable} illisible(s) laissé(s) de côté" : "";

            // Le nom du fichier seulement : un chemin complet porte le nom du
            // compte Windows.
            Tell($"{saved}{skipped}, dans {Path.GetFileName(destination)}.", failed: unreadable > 0);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Tell($"écriture impossible : {e.Message}", failed: true);
        }
        finally
        {
            _backupState.Running = false;
        }
    }

    private async Task RestoreAsync(string source, string? password)
    {
        _backupState.Running = true;

        try
        {
            var file = new FileInfo(source);

            if (file.Exists is false || file.Length > 16 * 1024 * 1024)
            {
                Tell("ce fichier n'est pas une sauvegarde Linkpearl.", failed: true);
                return;
            }

            var content = await File.ReadAllBytesAsync(source).ConfigureAwait(false);
            var read = await Task.Run(() => IdentityBackup.Read(content, password)).ConfigureAwait(false);

            if (read.Failure is { } failure)
            {
                _backupState.AwaitingPassword = read.NeedsPassword ? source : null;

                // La première demande de mot de passe n'est pas un échec : le
                // fichier est simplement protégé.
                Tell(password is null && read.NeedsPassword ? null : failure, failed: true);
                return;
            }

            // Lâcher le personnage avant d'écrire : un carnet encore chargé
            // réécrirait l'ancien par-dessus le restauré.
            await Framework.RunOnFrameworkThread(() =>
            {
                _restoring = true;
                ReleaseCharacter();
            }).ConfigureAwait(false);

            (bool Restored, string Message) outcome;

            try
            {
                outcome = await Task.Run(() => IdentityBackupService.Restore(CharactersRoot, read.Entries))
                    .ConfigureAwait(false);
            }
            finally
            {
                // Zéro force le suivi à reprendre le personnage connecté à
                // l'image suivante, avec l'identité qu'on vient de poser.
                await Framework.RunOnFrameworkThread(() =>
                {
                    _character = 0;
                    _restoring = false;
                }).ConfigureAwait(false);
            }

            _backupState.AwaitingPassword = null;

            if (outcome.Restored)
            {
                _configuration.BackupReminded = true;
                _configuration.Save();
            }

            Tell(outcome.Message, failed: outcome.Restored is false);
            Report(outcome.Message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Tell($"lecture impossible : {e.Message}", failed: true);
        }
        finally
        {
            _backupState.Running = false;
        }
    }

    private void Tell(string? message, bool failed)
    {
        _backupState.Message = message;
        _backupState.Failed = failed;
    }

    /// <summary>
    /// Fait avancer le moteur.
    /// </summary>
    /// <remarks>
    /// Une seconde, et non une image : le tic ne transfère rien lui-même, les
    /// sessions vivent sur leurs propres tâches. Il décide seulement de joindre,
    /// de poser et de retirer, et une seconde de retard sur ces trois-là ne se
    /// voit pas. Le faire à chaque image ferait tourner pour rien une boucle qui
    /// parcourt tous les pairs.
    ///
    /// Il ne prend pas d'instantané de l'ObjectTable : il réutilise celui que la
    /// boucle d'interface a déjà pris, car chaque instantané part sur le thread
    /// du jeu.
    /// </remarks>
    private async Task SyncLoopAsync(CancellationToken ct)
    {
        while (ct.IsCancellationRequested is false)
        {
            try
            {
                _appearance.Follow(_state.Self?.Fingerprint);

                // L'anti-rebond n'est consommé qu'une fois le calme revenu, ou
                // au plafond : c'est ce qui évite de rehacher pendant qu'on
                // essaie dix tenues d'affilée.
                if (_appearanceChanged.TryConsume())
                    _appearance.Rebuild();

                var visible = _state.Nearby
                    .Select(player => new VisiblePlayer(player.Object, player.Fingerprint))
                    .ToList();

                if (_engine is { } engine)
                    await engine.TickAsync(visible, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (ct.IsCancellationRequested is false)
            {
                Log.Warning(e, "Tic du moteur en échec.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Pourquoi on voit, ou ne voit pas, les joueurs d'à côté.
    /// </summary>
    /// <remarks>
    /// Chaque ligne oppose ce que nous ouvrons à ce que nous demandons : c'est
    /// la seule façon de distinguer une place où personne n'utilise le plugin
    /// d'une boîte ouverte sous une adresse que les autres ne calculent pas.
    /// </remarks>
    private void ShowPresence()
    {
        foreach (var line in _presence.Describe(_state.Self?.Fingerprint))
            Report(line);

        var nearby = _state.Nearby;

        if (nearby.Count == 0)
        {
            Report("aucun joueur visible autour de vous.");
            return;
        }

        Report($"{nearby.Count} joueur(s) visible(s), et la boîte que nous leur calculons :");

        foreach (var player in nearby.Take(8))
        {
            var box = MailboxAddress.Of(player.Fingerprint, new SystemClock().UtcNow);
            var seen = _presence.Detected.ContainsKey(player.Fingerprint);

            Report($"  {player.Display} : {box}{(seen ? " (détecté)" : "")}");
        }
    }

    /// <summary>Ce que le moteur fait, pair par pair.</summary>
    private void ShowSync()
    {
        Report($"apparence annoncée : {_appearance.Description}{(_appearance.Building ? " (en construction)" : "")}");

        var statuses = _engine?.Statuses ?? [];

        if (statuses.Count == 0)
        {
            Report("aucun pair actif.");
            return;
        }

        foreach (var status in statuses)
        {
            var view = status.View;

            var avancement = status.Applied ? "posée"
                           : view.Ready ? "prête"
                           : view.MissingBytes > 0
                                ? $"{view.ReceivedBytes / 1024 / 1024} / {view.MissingBytes / 1024 / 1024} Mo"
                                : "rien à recevoir";

            Report($"{status.DisplayName} : {status.State}, {avancement}"
                 + $"{(status.FingerprintDisputed ? ", empreinte inattendue" : "")}"
                 + $"{(status.LastFailure is { } failure ? $", {failure}" : "")}");
        }
    }

    private async Task RequestPairAsync(NearbyPlayer target)
    {
        if (_state.Self is not { } self)
        {
            Report("personnage introuvable.");
            return;
        }

        Report(await _presence.RequestPairAsync(target, self, _shutdown.Token).ConfigureAwait(false));
    }

    private async Task AcceptAsync(IncomingRequest request)
    {
        if (_state.Self is not { } self)
        {
            Report("personnage introuvable.");
            return;
        }

        _presence.TryTakeRequest(out _);

        var message = await _presence.AcceptAsync(request, self, _shutdown.Token).ConfigureAwait(false);
        Report(_pairing.AddFromRequest(request));
        Report(message);
    }

    private void Decline(IncomingRequest request)
    {
        _presence.TryTakeRequest(out _);
        Report($"{request.CharacterName} refusé.");
    }

    private void SetRendezvous(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            Report($"rendez-vous actuel : {(_configuration.RendezvousHost is "" ? "aucun" : _configuration.RendezvousHost)}");
            return;
        }

        _configuration.RendezvousHost = host;
        _configuration.Save();
        Report($"rendez-vous réglé sur {host}. C'est lui qui figurera dans vos invitations.");
    }

    /// <summary>Dépose une invitation et met le ticket dans le presse-papiers.</summary>
    private async Task InviteAsync()
    {
        var (ticket, rejection) = await _pairing.CreateInvitationAsync(_shutdown.Token).ConfigureAwait(false);

        if (ticket is null)
        {
            Report($"invitation impossible : {rejection}");
            return;
        }

        Report($"ticket d'invitation : {ticket}");

        if (TryCopyToClipboard(ticket))
            Report("copié dans le presse-papiers. Valable 24 heures, pour une seule personne.");

        Report("quand la personne l'aura utilisé, faites /lpearl check.");
        Log.Information($"Ticket d'invitation Linkpearl : {ticket}");
    }

    /// <summary>Relève les réponses aux invitations déposées.</summary>
    private async Task CheckAsync()
        => Report(await _pairing.CollectRepliesAsync(_shutdown.Token).ConfigureAwait(false));

    /// <summary>
    /// Met un texte dans le presse-papiers de Windows.
    /// </summary>
    /// <remarks>
    /// Passe par le thread du framework : le presse-papiers d'ImGui appartient
    /// au contexte de rendu, et y toucher depuis un autre fil planterait le jeu.
    /// </remarks>
    private static bool TryCopyToClipboard(string text)
    {
        try
        {
            Framework.RunOnFrameworkThread(() => ImGui.SetClipboardText(text)).Wait(TimeSpan.FromSeconds(2));
            return true;
        }
        catch (Exception e)
        {
            Log.Warning(e, "Copie dans le presse-papiers impossible.");
            return false;
        }
    }

    /// <summary>
    /// Ajoute un pair en retirant son ticket, pris dans le presse-papiers.
    /// </summary>
    private void AddPair(string arguments)
    {
        var name = arguments.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Report("usage : copiez le ticket reçu, puis /lpearl pair <nom>");
            return;
        }

        var space = name.IndexOf(' ');

        if (space > 0)
        {
            var given = name[(space + 1)..].Trim();
            var who = name[..space].Trim();
            RunSafely(async () => Report(await _pairing.RedeemAsync(given, who, _shutdown.Token).ConfigureAwait(false)));
            return;
        }

        if (TryReadClipboard(out var ticket) is false || string.IsNullOrWhiteSpace(ticket))
        {
            Report("presse-papiers vide. Copiez d'abord le ticket qu'on vous a envoyé.");
            return;
        }

        RunSafely(async () =>
            Report(await _pairing.RedeemAsync(ticket.Trim(), name, _shutdown.Token).ConfigureAwait(false)));
    }

    /// <summary>Lit le presse-papiers, depuis le thread du framework.</summary>
    private static bool TryReadClipboard(out string text)
    {
        var read = string.Empty;

        try
        {
            Framework.RunOnFrameworkThread(() => read = ImGui.GetClipboardText()).Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception e)
        {
            Log.Warning(e, "Lecture du presse-papiers impossible.");
            text = string.Empty;
            return false;
        }

        text = read;
        return true;
    }

    private void ShowPairs()
    {
        var pairs = _pairing.Book.All.ToList();

        if (pairs.Count == 0)
        {
            Report("carnet vide. Demandez un ticket à quelqu'un, puis /lpearl pair <nom>.");
            return;
        }

        foreach (var pair in pairs)
        {
            Report($"{pair.DisplayName} : {pair.Trust}"
                 + $"{(pair.KeyVerified ? ", vérifié" : ", NON VÉRIFIÉ")}"
                 + $", {pair.Id.ToHex()[..8]}");
        }

        Report($"{_pairing.PendingInvitations} invitation(s) en attente de réponse.");
    }

    private async Task AnnounceAsync()
    {
        var report = await _pairing.AnnounceAsync(_shutdown.Token).ConfigureAwait(false);

        Report($"{report.Pairs} pairs actifs, {report.Announced} annonces envoyées, {report.Matched} appariés.");

        if (report.Failure is not null)
            Report($"échec : {report.Failure}");
    }

    private async Task CaptureAsync(bool force)
    {
        var report = await _selfLoop.CaptureAsync(force, _shutdown.Token).ConfigureAwait(false);

        Report($"capture : {report.FileCount} fichiers, {Megabytes(report.TotalBytes)}, "
             + $"{report.GamePathCount} chemins de jeu.");
        Report($"manifeste : {report.ManifestBytes / 1024} Ko bruts, "
             + $"{report.CompressedManifestBytes / 1024} Ko compressés.");
        Report($"durées : hachage {report.HashMilliseconds} ms, copie vers le cache {report.CopyMilliseconds} ms.");
        Report($"échanges de fichier non synchronisés en v1 : {report.SwapCount}. "
             + $"Ressources écartées : {report.SkippedCount}.");

        foreach (var skipped in report.Skipped.Take(5))
            Log.Information($"écarté : {skipped.GamePath} ({skipped.Reason})");
    }

    private async Task ApplyAsync()
    {
        var applied = await _selfLoop.ApplyAsync(_shutdown.Token).ConfigureAwait(false);
        Report($"appliqué {applied} chemins de jeu depuis le cache.");
    }

    /// <summary>
    /// Exécute une tâche déclenchée par une commande, sans jamais laisser une
    /// exception se perdre dans un Task oublié.
    /// </summary>
    private void RunSafely(Func<Task> work)
        => _ = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Commande Linkpearl en échec.");
                Report($"échec : {e.Message}");
            }
        }, _shutdown.Token);

    private static string Megabytes(long bytes) => $"{bytes / 1024.0 / 1024.0:F1} Mo";

    /// <summary>
    /// Écrit dans le journal local du joueur, jamais dans le chat du jeu : rien
    /// de ce que fait ce plugin ne doit partir vers le serveur.
    /// </summary>
    private static void Report(string message) => Chat.Print($"[Linkpearl] {message}");

    public void Dispose()
    {
        // Le déchargement de Dalamud est coopératif : une tâche encore vivante
        // ici ferait fuir l'AssemblyLoadContext, et le rechargement suivant en
        // créerait un second.
        _shutdown.Cancel();

        PluginInterface.UiBuilder.Draw -= _windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Open;
        PluginInterface.UiBuilder.OpenConfigUi -= Open;
        _windows.RemoveAllWindows();

        Commands.RemoveHandler(Command);
        ContextMenu.OnMenuOpened -= OnMenuOpened;
        Framework.Update -= PollLinks;
        Framework.Update -= FollowCharacter;
        Framework.Update -= UpdateStatusBar;
        _statusBar.Dispose();

        // Le moteur d'abord : il retire des pairs ce qu'il leur a posé, et cela
        // passe par des appels au jeu que la suite ne pourrait plus faire.
        _engine?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        _applicator.Dispose();
        _appearance.Dispose();
        _penumbra.Dispose();
        _glamourer.Dispose();
        _links.Dispose();
        Fonts.Dispose();

        _selfLoop.Dispose();
        _presence.Dispose();
        _pairing.Dispose();
        _shutdown.Dispose();
    }
}
