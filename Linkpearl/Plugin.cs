using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Linkpearl.Integration;
using Linkpearl.Ui;

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
    [PluginService] internal static IPluginLog              Log             { get; private set; } = null!;

    private readonly SelfLoop _selfLoop;
    private readonly PairingService _pairing;
    private readonly PresenceService _presence;
    private readonly DalamudObjectSource _objectSource;
    private readonly PluginState _state = new();
    private readonly WindowSystem _windows = new("Linkpearl");
    private readonly MainWindow _window;
    private readonly Configuration _configuration;
    private readonly CancellationTokenSource _shutdown = new();

    public Plugin()
    {
        var penumbra  = new PenumbraIpc(PluginInterface);
        var glamourer = new GlamourerIpc(PluginInterface);
        _selfLoop = new SelfLoop(penumbra, glamourer, Framework, Log);

        _configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Linkpearl");

        var clock = new SystemClock();
        _pairing = new PairingService(root, _configuration, clock, Log);
        _presence = new PresenceService(_configuration, _pairing.Identity, clock, Log);
        _objectSource = new DalamudObjectSource(Objects, ClientState, Framework);

        _window = new MainWindow(
            _pairing, _presence, _state, _configuration,
            player => RunSafely(() => RequestPairAsync(player)),
            request => RunSafely(() => AcceptAsync(request)),
            Decline);

        _windows.AddWindow(_window);
        PluginInterface.UiBuilder.Draw += _windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Open;
        PluginInterface.UiBuilder.OpenConfigUi += Open;

        _ = Task.Run(() => RefreshLoopAsync(_shutdown.Token), _shutdown.Token);

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
            case "id":            Report($"votre identifiant : {_pairing.Id}"); break;
            case "":              Open();                                      break;
            case "diag":          RunSafely(DiagnoseAsync);                    break;
            case "unlock":        RunSafely(UnlockAsync);                      break;
            case "announce":      RunSafely(AnnounceAsync);                    break;
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
                Report("rdv <hôte> | id | announce | capture | apply | revert | diag | unlock");
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

                        _state.Nearby = await _objectSource.SnapshotAsync(ct).ConfigureAwait(false);
                        await _presence.RefreshDetectionAsync(_state.Nearby, ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    _state.Nearby = [];
                }
            }
            catch (Exception e) when (ct.IsCancellationRequested is false)
            {
                Log.Warning(e, "Rafraîchissement en échec.");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
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
        _selfLoop.Dispose();
        _presence.Dispose();
        _pairing.Dispose();
        _shutdown.Dispose();
    }
}
