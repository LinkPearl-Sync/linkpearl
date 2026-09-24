using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport;
using Linkpearl.Core.Transport.Rendezvous;
using Linkpearl.Integration;
using Linkpearl.Integration.Extras;
using Linkpearl.Ui;
using Linkpearl.Ui.Onboarding;
using Linkpearl.Ui.Pages;
using System.Reflection;

namespace Linkpearl;

/// <summary>
/// Point d'entrée du plugin.
/// </summary>
/// <remarks>
/// Le plugin n'est qu'une coquille autour de <c>Core</c> : il fournit les
/// adaptateurs Dalamud et l'interface, et ne porte aucune logique. Tout ce qui
/// décide se teste sous Linux, sans le jeu.
/// </remarks>
public sealed class Plugin : IDalamudPlugin
{
    /// <summary>
    /// Ouvre la fenêtre, rien d'autre : tout le reste passe par l'interface.
    /// </summary>
    /// <remarks>
    /// Le jeu intercepte « /linkpearl » avant Dalamud : c'est une commande de
    /// chat native. Toute commande choisie ici doit être vérifiée en jeu.
    /// </remarks>
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
    [PluginService] internal static IGameGui                GameGui         { get; private set; } = null!;
    [PluginService] internal static INamePlateGui           NamePlates      { get; private set; } = null!;
    [PluginService] internal static IPluginLog              Log             { get; private set; } = null!;
    [PluginService] internal static INotificationManager    Notifications   { get; private set; } = null!;
    [PluginService] internal static ITextureProvider        Textures        { get; private set; } = null!;

    private readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;
    private readonly SelfLoop _selfLoop;

    /// <summary>
    /// Décide quand le cache existe. L'apparence locale et l'applicateur
    /// reçoivent son magasin commutable, le moteur n'existe que cache ouvert.
    /// </summary>
    private readonly CacheKeeper _cacheKeeper;

    /// <summary>Compte les tics, pour sonder le dossier du cache toutes les cinq secondes.</summary>
    private int _syncTicks;

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
    /// <summary>
    /// Assignée plus loin dans le constructeur, après <see cref="_window"/> :
    /// initialisée à <c>null!</c> parce qu'une fermeture capturée plus haut
    /// (le bouchon de <see cref="MainWindow"/> qui la rouvre) la référence
    /// avant cette affectation, sans jamais l'appeler avant elle.
    /// </summary>
    private readonly OnboardingWindow _onboarding = null!;

    /// <summary>L'état des dépendances, relevé par IPC hors du dessin.</summary>
    private volatile bool _penumbraReady;
    private volatile bool _glamourerReady;
    private long _prerequisitesDueAt;

    private readonly StatusBarEntry _statusBar;
    private readonly TransferOverlay _overlay;
    private readonly NameplateGlyphs _nameplates;
    private readonly ExtrasIpc _extras;
    private readonly TransientCapture _transients;
    private long _statusBarDueAt;
    private long _nameplatesDueAt;
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
        // Tout au début, avant GetPluginConfig() : Dalamud range configuration
        // et identités sous l'InternalName, donc encore sous l'ancien nom tant
        // que ceci n'a pas couru une première fois après le renommage.
        AdoptLegacyConfiguration();

        var penumbra  = _penumbra  = new PenumbraIpc(PluginInterface);
        var glamourer = _glamourer = new GlamourerIpc(PluginInterface);
        _selfLoop = new SelfLoop(penumbra, glamourer, Log);

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
            Receive = new TransientCategories(
                _configuration.ReceiveAnimations, _configuration.ReceiveVfx, _configuration.ReceiveSounds),
            Limiter = new RateLimiterSettings
            {
                CeilingBytesPerSecond = _configuration.UploadCeilingBytesPerSecond,
            },
        };

        _cacheKeeper = new CacheKeeper(
            _configuration,
            Path.Combine(_legacyRoot, "cache"),
            clock,
            path => new DriveInfo(Path.GetPathRoot(path) ?? "/").AvailableFreeSpace,
            new PluginLogSink(Log, "cache"));

        _links = new PeerLinkFactory(engineSettings.DataChannels + 1, new PluginLogSink(Log, "transport"));
        _extras = new ExtrasIpc(PluginInterface, Objects, Log);
        _transients = new TransientCapture(penumbra, Framework, Objects, clock, Log);
        _appearance = new LocalAppearance(
            penumbra, glamourer, Framework, Objects, _extras, _transients, MoodlesKey, _cacheKeeper.Store, Log);

        // Une animation moddée jouée pour la première fois : elle rejoint
        // l'apparence annoncée, par le même anti-rebond.
        _transients.Discovered += _appearanceChanged.Signal;

        // Tout changement de mod affectant le personnage produit un redessin, et
        // Glamourer signale chaque changement d'état. Les deux sont levés
        // depuis le thread du jeu : on ne fait que signaler, la reconstruction
        // part de la boucle de synchronisation.
        penumbra.SettingsChanged += _appearanceChanged.Signal;

        penumbra.Redrawn += index =>
        {
            if (index == 0)
                _appearanceChanged.Signal();
        };

        glamourer.Changed += address =>
        {
            if (address == Objects.LocalPlayer?.Address)
                _appearanceChanged.Signal();
        };

        // Les plugins voisins signalent eux-mêmes nos changements : titre,
        // statuts, proportions. La rafale passe par le même anti-rebond.
        _extras.Changed += _appearanceChanged.Signal;

        // Honorific, Moodles ou PetNicknames qui redémarrent ont oublié ce
        // que nous avions posé chez eux : on repose chaque pair affiché.
        _extras.Ready += () =>
        {
            foreach (var status in _engine?.Statuses ?? [])
            {
                if (status.Applied)
                    _engine?.Reapply(status.Peer);
            }
        };

        _applicator = new RemoteApplicator(
            penumbra, glamourer, _extras, Framework, Objects, ClientState, Condition, _cacheKeeper.Store, Quotas.Default, root, Log);

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
            Accept,
            Decline,
            (id, paused) => _pairing.SetPaused(id, paused),
            id => _engine?.Reapply(id),
            id => _pairing.Remove(id),
            SetUploadLimited,
            () => GlobalReceive,
            SetGlobalReceive,
            SetPairReceive,
            _backupState,
            (path, password) => RunSafely(() => BackupAsync(path, password)),
            (path, password) => RunSafely(() => RestoreAsync(path, password)),
            _cacheKeeper,
            () => _onboarding.Show());

        // Clic droit sur un personnage appairé : réappliquer, comme le font
        // les autres outils de synchronisation. C'est le geste que les joueurs
        // connaissent déjà.
        ContextMenu.OnMenuOpened += OnMenuOpened;

        _windows.AddWindow(_window);
        _windows.AddWindow(new RequestToasts(
            _presence, _state, () => _window.ShowsRequests, _window.OpenRequests, Accept, Decline));

        _onboarding = new OnboardingWindow(
            Textures.GetFromManifestResource(Assembly.GetExecutingAssembly(), "Images.banner.png"),
            new CacheChooser(_cacheKeeper),
            () => _penumbraReady,
            () => _glamourerReady,
            started: _window.OpenNearby,
            closed: OnOnboardingClosed);
        _windows.AddWindow(_onboarding);
        Framework.Update += UpdatePrerequisites;

        _statusBar = new StatusBarEntry(DtrBar, Open);
        Framework.Update += UpdateStatusBar;

        _overlay = new TransferOverlay(GameGui, Objects);
        Framework.Update += UpdateOverlay;
        PluginInterface.UiBuilder.Draw += _overlay.Draw;

        _nameplates = new NameplateGlyphs(NamePlates);
        Framework.Update += UpdateNameplates;

        PluginInterface.UiBuilder.Draw += _windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Open;
        PluginInterface.UiBuilder.OpenConfigUi += Open;

        _ = Task.Run(() => RefreshLoopAsync(_shutdown.Token), _shutdown.Token);
        _ = Task.Run(() => SyncLoopAsync(_shutdown.Token), _shutdown.Token);

        Commands.AddHandler(Command, new CommandInfo((_, _) => Open())
        {
            HelpMessage = "Ouvre la fenêtre de Linkpearl.",
        });

        Log.Information($"Chargé. Penumbra : {Describe(penumbra.TryGetVersion())}, Glamourer : {Describe(glamourer.TryGetVersion())}.");

        // Rattrapage d'une session précédente : un rechargement ou un plantage a
        // pu laisser une collection affectée au personnage, que plus rien en
        // mémoire ne sait retirer.
        if (_selfLoop.HasLeftovers())
        {
            Framework.RunOnFrameworkThread(_selfLoop.Revert);
            Log.Information("Une collection d'une session précédente a été retirée.");
        }

        Framework.RunOnFrameworkThread(() =>
        {
            var left = _applicator.CleanLeftovers();

            if (left > 0)
                Log.Information($"{left} collection(s) de pair d'une session précédente ont été retirées.");
        });

        if (_configuration.OnboardingSeen is false)
            _onboarding.Show();

        // En dernier : l'ouverture peut lever Lost, qui ouvre la fenêtre, et la
        // fenêtre doit exister. La mesure de l'ancien cache suit l'ouverture,
        // voir OnCacheOpened.
        _cacheKeeper.Opened += OnCacheOpened;
        _cacheKeeper.Lost += OnCacheLost;
        _cacheKeeper.Start();
    }

    /// <summary>
    /// Ajoute au menu d'un personnage ce que Linkpearl peut faire avec lui :
    /// réappliquer s'il est pairé, le pairage s'il peut l'être.
    /// </summary>
    /// <remarks>
    /// Rien pour les autres : proposer l'entrée à tout le monde inviterait à
    /// cliquer pour rien, et dirait à qui regarde par-dessus l'épaule que le
    /// plugin est là.
    /// </remarks>
    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.Target is not MenuTargetDefault { TargetObject: IPlayerCharacter player })
            return;

        var name = player.Name.TextValue;

        if (string.IsNullOrWhiteSpace(name))
            return;

        var world = (ushort)player.HomeWorld.RowId;
        var fingerprint = PlayerFingerprint.Of(DalamudObjectSource.Normalize(name), world);

        // L'empreinte annoncée compte autant que l'épinglée : un pair ajouté
        // avant que l'épinglage se fasse à l'acceptation n'a que la première.
        var paired = _pairing.Book.Listed.Any(pair => pair.PinnedFingerprint == fingerprint)
                  || (_engine?.Statuses ?? []).Any(status => status.View.Fingerprint == fingerprint);

        if (paired)
        {
            args.AddMenuItem(new MenuItem
            {
                Name = "Linkpearl : réappliquer",
                PrefixChar = 'L',
                PrefixColor = 541,
                OnClicked = _ => _engine?.Reapply(fingerprint),
            });

            return;
        }

        if (PairingMenuItem(name, world, fingerprint) is { } item)
            args.AddMenuItem(item);
    }

    /// <summary>
    /// L'entrée de pairage, pour un joueur que la page « Autour de vous »
    /// listerait comme « à pairer ».
    /// </summary>
    /// <remarks>
    /// Si c'est lui qui a demandé, l'entrée accepte : lui renvoyer une demande
    /// croiserait la sienne au lieu de conclure.
    /// </remarks>
    private MenuItem? PairingMenuItem(string name, ushort world, PlayerFingerprint fingerprint)
    {
        var incoming = _presence.RequestCount == 0
            ? null
            : _presence.PeekRequests().FirstOrDefault(request =>
                  request.WorldId == world
               && string.Equals(request.CharacterName, name, StringComparison.OrdinalIgnoreCase));

        if (incoming is not null)
        {
            return new MenuItem
            {
                Name = "Linkpearl : accepter le pairage",
                PrefixChar = 'L',
                PrefixColor = 541,
                OnClicked = _ => Accept(incoming),
            };
        }

        // Seulement un joueur qui se signale : une demande déposée dans la
        // boîte de quelqu'un qui n'utilise pas le plugin n'arriverait jamais.
        if (_presence.Detected.ContainsKey(fingerprint) is false)
            return null;

        // L'instantané fournit le NearbyPlayer qu'attend la demande ; un joueur
        // tout juste arrivé n'y est pas encore, et l'entrée attend le suivant.
        if (_state.Nearby.FirstOrDefault(nearby => nearby.Fingerprint == fingerprint) is not { } target)
            return null;

        var sent = _presence.PendingOutgoing.ContainsKey(fingerprint);

        return new MenuItem
        {
            Name = sent ? "Linkpearl : renvoyer la demande de pairage" : "Linkpearl : demander le pairage",
            PrefixChar = 'L',
            PrefixColor = 541,
            OnClicked = _ => RunSafely(() => RequestPairAsync(target)),
        };
    }

    /// <summary>
    /// Reprend la configuration et les identités d'avant le renommage en
    /// LinkpearlSync, si elles sont manifestement à nous.
    /// </summary>
    /// <remarks>
    /// pluginConfigs/Linkpearl.json et pluginConfigs/Linkpearl/ sont aussi les
    /// emplacements du plugin officiel Linkpearl (NotNite) : LegacyConfigAdoption
    /// vérifie que c'est bien à nous avant de reprendre quoi que ce soit, et ne
    /// remplace ni n'efface jamais rien. Une reprise ratée ne doit pas empêcher
    /// le plugin de charger, d'où le journal qui ne garde que le type
    /// d'exception : IOException embarque souvent le chemin complet, qui porte
    /// le nom du compte Windows.
    /// </remarks>
    private void AdoptLegacyConfiguration()
    {
        try
        {
            var newConfigFile = PluginInterface.ConfigFile.FullName;
            var legacyConfigFile = Path.Combine(
                Path.GetDirectoryName(newConfigFile) ?? "", "Linkpearl.json");

            if (LegacyConfigAdoption.TryAdoptConfigFile(legacyConfigFile, newConfigFile))
                Log.Information("configuration d'avant le renommage reprise.");

            var newConfigDir = Path.TrimEndingDirectorySeparator(PluginInterface.ConfigDirectory.FullName);
            var legacyConfigDir = Path.Combine(Path.GetDirectoryName(newConfigDir) ?? "", "Linkpearl");

            if (LegacyConfigAdoption.TryAdoptDirectory(legacyConfigDir, newConfigDir))
                Log.Information("identités d'avant le renommage reprises.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"reprise d'avant le renommage impossible ({e.GetType().Name}).");
        }
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
        var root = CharacterStorage.Prepare(_root, contentId, _legacyRoot, message => Log.Information(message));

        _pairing.Bind(root);
        _transients.Attach(root);
        StartEngineIfReady();
    }

    /// <summary>
    /// Construit le moteur si tout ce qu'il lui faut est là : un personnage et
    /// un cache ouvert.
    /// </summary>
    /// <remarks>
    /// Appelé à la connexion et à l'ouverture du cache, dans n'importe quel
    /// ordre : le second appel est celui qui construit. Sur le thread du jeu.
    /// </remarks>
    private void StartEngineIfReady()
    {
        if (_engine is not null || _character is 0 || _pairing.Id is null
            || _cacheKeeper.State is not CacheGateState.Open)
            return;

        _engine = new SyncEngine(
            _pairing.Book,
            new PeerConnector(
                _links,
                new RendezvousEndpoint(_configuration.RendezvousHost, _configuration.RendezvousPort),
                _clock,
                new PluginLogSink(Log, "moteur")),
            _appearance, _applicator, _cacheKeeper.Store, _pairing.Id!.Value, _pairing.Identity!.Key, _clock,
            new PluginLogSink(Log, "moteur"), _engineSettings);

        // Le moteur a déjà retiré l'entrée du carnet : il reste à l'écrire, et
        // à dire pourquoi une ligne vient de disparaître de la liste.
        _engine.PairEnded += pair =>
        {
            _pairing.Save();
            Report($"{pair.DisplayName} a mis fin au pairage.");
        };
        _engine.RevocationDelivered += _ => _pairing.Save();

        var pairs = _pairing.Book.Listed.Count;

        Log.Information(pairs is 0
            ? $"identité de ce personnage : {_pairing.Id}. Aucun pair pour l'instant."
            : $"identité de ce personnage : {_pairing.Id}. {pairs} pair(s) au carnet.");
    }

    private void ReleaseCharacter()
    {
        // Le moteur d'abord : il partage le même PairBook, et la boucle de
        // synchronisation doit voir _engine à null avant que le carnet ne
        // soit vidé par Unbind.
        StopEngine();
        _pairing.Unbind();
        _presence.ForgetRequests();
        _transients.Attach(null);
    }

    /// <summary>
    /// Arrête le moteur, qui retire des pairs ce qu'il leur a posé.
    /// </summary>
    /// <remarks>
    /// Hors du thread du jeu : le retrait passe par RunOnFrameworkThread, et
    /// l'attendre depuis ce thread-là se bloquerait sur soi-même.
    /// </remarks>
    private void StopEngine()
    {
        var engine = _engine;
        _engine = null;

        if (engine is null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await engine.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Warning(e, "Arrêt du moteur en échec.");
            }
        });
    }

    /// <summary>Le cache est ouvert : le moteur peut naître, et notre apparence y entrer.</summary>
    /// <remarks>
    /// Peut être levé depuis le pool de threads (un dossier rechoisi) : le
    /// moteur se construit sur le thread du jeu. L'apparence est recapturée
    /// parce que ses fichiers ne sont pas dans un cache neuf.
    /// </remarks>
    private void OnCacheOpened()
    {
        _appearanceChanged.Signal();
        Framework.RunOnFrameworkThread(StartEngineIfReady);

        // L'ancien cache se mesure en parcourant tout l'arbre : hors du
        // chargement. Après l'ouverture et non au constructeur : Choose
        // pendant la présentation peut fixer PreviousCacheDirectory, et la
        // mesure doit porter sur ce que l'ouverture vient de retenir.
        RunSafely(() => Task.Run(_cacheKeeper.MeasurePrevious));
    }

    /// <summary>
    /// Le dossier du cache a disparu : tout s'arrête, et on le dit.
    /// </summary>
    /// <remarks>
    /// Arrêter le moteur rend chaque pair à son apparence par défaut : ses mods
    /// temporaires pointent sur des fichiers qui n'existent plus.
    /// </remarks>
    private void OnCacheLost()
    {
        Framework.RunOnFrameworkThread(() =>
        {
            StopEngine();
            _window.IsOpen = true;

            Notifications.AddNotification(new Notification
            {
                Title = "Linkpearl",
                Content = "Le dossier du cache est introuvable. La synchronisation est arrêtée "
                        + "jusqu'au choix d'un autre dossier.",
                Type = NotificationType.Error,
            });
        });
    }

    /// <summary>
    /// La présentation se ferme. La première fois, le cache peut enfin naître,
    /// dans le dossier choisi.
    /// </summary>
    /// <remarks>
    /// Sur le pool de threads : ouvrir le cache relit son index, voire tout
    /// son arbre.
    /// </remarks>
    private void OnOnboardingClosed()
    {
        if (_configuration.OnboardingSeen)
            return;

        RunSafely(() => Task.Run(_cacheKeeper.FinishOnboarding));
    }

    /// <summary>
    /// Relève Penumbra et Glamourer toutes les deux secondes, seulement tant
    /// que la présentation est ouverte.
    /// </summary>
    /// <remarks>
    /// Le dessin ne doit rien interroger : un appel IPC à chaque image, pour
    /// une réponse qui ne change qu'au chargement d'un plugin, serait du gâchis.
    /// </remarks>
    private void UpdatePrerequisites(IFramework framework)
    {
        if (_onboarding.IsOpen is false)
            return;

        var now = Environment.TickCount64;

        if (now < _prerequisitesDueAt)
            return;

        _prerequisitesDueAt = now + 2000;
        _penumbraReady = _penumbra.TryGetVersion() is not null;
        _glamourerReady = _glamourer.TryGetVersion() is not null;
    }

    private static string Describe((int Major, int Minor)? version)
        => version is { } v ? $"{v.Major}.{v.Minor}" : "absent";

    private void Open() => _window.IsOpen = true;

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
        _statusBar.Update(
            _state.Nearby, _pairing.Book.Listed, _presence.RequestCount, _cacheKeeper.State is CacheGateState.Missing);

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
        if (_configuration.BackupReminded || _pairing.Book.Listed.Count == 0)
            return;

        _configuration.BackupReminded = true;
        _configuration.Save();

        Report("pensez à sauvegarder votre identité (Réglages, « Sauvegarde de l'identité ») : "
             + "sans elle, une réinstallation de Windows oblige à refaire chaque pairage.");
    }

    private string CharactersRoot => Path.Combine(_root, "characters");

    /// <summary>
    /// La clé qui renomme nos GUID Moodles, propre au personnage connecté.
    /// </summary>
    /// <remarks>
    /// Dérivée de l'identité du personnage : deux personnages d'une même
    /// personne donnent des GUID qu'on ne peut pas relier.
    /// </remarks>
    private byte[]? MoodlesKey()
        => _pairing.Identity is { } identity
            ? MoodlesSanitizer.KeyFor(identity.Key.ExportParameters(includePrivateParameters: true).D!)
            : null;

    /// <summary>
    /// Place les badges de transfert, à chaque image : le personnage bouge, la
    /// caméra aussi, et un badge en retard d'une seconde flotterait à côté.
    /// </summary>
    private void UpdateOverlay(IFramework framework)
        => _overlay.Update(
            _configuration.ShowTransferBadges, _engine?.Statuses ?? [], _state.Nearby, _pairing.Book);

    /// <summary>
    /// Recalcule les glyphes des plaques de nom, quatre fois par seconde.
    /// </summary>
    /// <remarks>
    /// Pas à chaque image comme les badges : la plaque suit le personnage
    /// d'elle-même, et ce qu'elle affiche ne change qu'au rythme de l'instantané
    /// des joueurs visibles et des états du moteur.
    /// </remarks>
    private void UpdateNameplates(IFramework framework)
    {
        var now = Environment.TickCount64;

        if (now < _nameplatesDueAt)
            return;

        _nameplatesDueAt = now + 250;
        _nameplates.Update(
            _configuration.ShowNameplateGlyphs, _state.Nearby, _pairing.Book.Listed, _engine?.Statuses ?? [],
            [.. _presence.Detected.Keys],
            _presence.RequestCount == 0 ? [] : _presence.PeekRequests());
    }

    private TransientCategories GlobalReceive => _engineSettings.Receive;

    private void SetGlobalReceive(TransientCategories receive)
    {
        _configuration.ReceiveAnimations = receive.Animations;
        _configuration.ReceiveVfx = receive.Vfx;
        _configuration.ReceiveSounds = receive.Sounds;
        _configuration.Save();

        // Le moteur du personnage suivant naîtra avec le même réglage.
        _engineSettings = _engineSettings with { Receive = receive };
        _engine?.SetGlobalReceive(receive);
    }

    private void SetPairReceive(PeerId id, TransientCategories receive)
    {
        // Le moteur voit le changement au tic suivant et redemande le
        // manifeste : rien d'autre à faire ici.
        if (_pairing.SetReceive(id, receive) is { Length: > 0 } failure)
            Log.Warning($"Réglage de réception refusé : {failure}");
    }

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
                // Rien ne touche au cache tant qu'il n'est pas ouvert : la capture
                // de notre apparence y écrit. Le signal reste en attente, et la
                // capture part dès l'ouverture.
                if (_cacheKeeper.State is CacheGateState.Open)
                {
                    _appearance.Follow(_state.Self?.Fingerprint);

                    // L'anti-rebond n'est consommé qu'une fois le calme revenu, ou
                    // au plafond : c'est ce qui évite de rehacher pendant qu'on
                    // essaie dix tenues d'affilée.
                    if (_appearanceChanged.TryConsume())
                        _appearance.Rebuild();
                }
                else
                {
                    // Le cache est fermé (présentation en attente, ou dossier
                    // perdu) : on oublie ce qu'on suivait. Sans cela, un
                    // personnage inchangé à la réouverture verrait Follow(fp)
                    // rester muet, et le moteur neuf annoncerait un manifeste
                    // dont les blobs ne sont plus dans le nouveau cache.
                    _appearance.Forget();
                }

                // Le dossier du cache peut être supprimé pendant qu'on joue.
                if (++_syncTicks % 5 == 0)
                    _cacheKeeper.Check();

                var visible = _state.Nearby
                    .Select(player => new VisiblePlayer(player.Object, player.Fingerprint))
                    .ToList();

                // Sur le chemin de la perte, StopEngine ne fait que mettre
                // l'arrêt en file sur le thread du jeu : sans cette condition,
                // ce même tic pourrait encore appeler TickAsync pendant que
                // DisposeAsync du moteur tourne.
                if (_engine is { } engine && _cacheKeeper.State is CacheGateState.Open)
                    await engine.TickAsync(visible, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (ct.IsCancellationRequested is false)
            {
                Log.Warning(e, "Tic du moteur en échec.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
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

    private void Accept(IncomingRequest request)
    {
        // Retirée avant tout await : la réponse part en tâche de fond, et un
        // second clic à l'image suivante accepterait sinon deux fois.
        _presence.Forget(request);
        RunSafely(() => AcceptAsync(request));
    }

    private async Task AcceptAsync(IncomingRequest request)
    {
        if (_state.Self is not { } self)
        {
            Report("personnage introuvable.");
            return;
        }

        var (message, agreed) = await _presence.AcceptAsync(request, self, _shutdown.Token).ConfigureAwait(false);

        if (agreed is not null)
            Report(_pairing.AddFromRequest(agreed));

        Report(message);
    }

    private void Decline(IncomingRequest request) => _presence.Forget(request);

    /// <summary>
    /// Exécute une tâche déclenchée depuis l'interface, sans jamais laisser une
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
                Log.Error(e, "Action Linkpearl en échec.");
                Report($"échec : {e.Message}");
            }
        }, _shutdown.Token);

    /// <summary>
    /// Dit au joueur ce qui le concerne, dans son chat local : rien de ce que
    /// fait ce plugin ne doit partir vers le serveur.
    /// </summary>
    /// <remarks>
    /// Seulement ce qu'il ne verrait pas autrement : une réponse arrivée pendant
    /// que la fenêtre était fermée, une action qui a échoué. Le diagnostic va
    /// au journal de Dalamud.
    /// </remarks>
    private static void Report(string message) => Chat.Print($"[Linkpearl] {message}");

    public void Dispose()
    {
        // Le déchargement de Dalamud est coopératif : une tâche encore vivante
        // ici ferait fuir l'AssemblyLoadContext, et le rechargement suivant en
        // créerait un second.
        _shutdown.Cancel();
        _cacheKeeper.Opened -= OnCacheOpened;
        _cacheKeeper.Lost -= OnCacheLost;

        PluginInterface.UiBuilder.Draw -= _windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Open;
        PluginInterface.UiBuilder.OpenConfigUi -= Open;
        _windows.RemoveAllWindows();

        Commands.RemoveHandler(Command);
        ContextMenu.OnMenuOpened -= OnMenuOpened;
        Framework.Update -= PollLinks;
        Framework.Update -= FollowCharacter;
        Framework.Update -= UpdateStatusBar;
        Framework.Update -= UpdateOverlay;
        Framework.Update -= UpdateNameplates;
        Framework.Update -= UpdatePrerequisites;
        PluginInterface.UiBuilder.Draw -= _overlay.Draw;
        _nameplates.Dispose();
        _statusBar.Dispose();

        // Le moteur d'abord : il retire des pairs ce qu'il leur a posé, et cela
        // passe par des appels au jeu que la suite ne pourrait plus faire.
        _engine?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        _applicator.Dispose();
        _appearance.Dispose();
        _transients.Dispose();
        _extras.Dispose();
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
