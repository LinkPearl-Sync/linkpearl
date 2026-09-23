using System.Collections.Concurrent;
using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Transport;

namespace Linkpearl.Core.Sync;

/// <summary>Ce qui sait joindre un pair.</summary>
/// <remarks>
/// Abstrait pour que le moteur se teste sans réseau ni rendez-vous.
/// <see cref="PeerConnector"/> en est la seule implémentation réelle.
/// </remarks>
public interface IPeerDialer
{
    Task<ConnectionAttempt> ConnectAsync(PairRecord pair, CancellationToken ct);
}

/// <summary>Réglages du moteur.</summary>
public sealed record SyncEngineSettings
{
    public static SyncEngineSettings Default { get; } = new();

    /// <summary>Attente avant la première reprise après un échec.</summary>
    public TimeSpan FirstBackoff { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Plafond de l'attente entre deux tentatives.</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Attente quand le pair n'est simplement pas en ligne.
    /// </summary>
    /// <remarks>
    /// Fixe, et non doublée à chaque fois : l'absence n'est pas une panne. Un
    /// ami hors ligne toute la journée serait sinon réessayé une fois par heure
    /// au moment où il se connecte enfin.
    /// </remarks>
    public TimeSpan AbsentBackoff { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Durée au-delà de laquelle une session qui tombe n'est pas un échec.
    /// </summary>
    /// <remarks>
    /// Une session qui a tenu et qui se ferme, c'est un pair qui nous met en
    /// pause, se déconnecte ou recharge son plugin : on se réannonce aussitôt,
    /// pour qu'il nous retrouve dès son retour. Une session qui tombe à peine
    /// établie ressemble à un lien qui clignote, et reprend le délai croissant.
    /// </remarks>
    public TimeSpan StableSession { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Faux pour débrayer le limiteur d'envoi, voir <see cref="RateLimiter.Bypassed"/>.</summary>
    public bool LimitUpload { get; init; } = true;

    /// <summary>
    /// Tronçons servis de front, un par canal.
    /// </summary>
    /// <remarks>
    /// Mesuré par le faux pair sur une apparence réelle de 405 Mo, avec des
    /// tronçons de 4 Mio, des accusés à la milliseconde et un relais qui
    /// retarde les paquets :
    ///
    /// | canaux | 0 ms      | 20 ms     | 60 ms     |
    /// |--------|-----------|-----------|-----------|
    /// | 8      | 57,5 Mo/s | 14,0 Mo/s |           |
    /// | 16     |           | 21,0 Mo/s | 9,6 Mo/s  |
    /// | 32     | 53,8 Mo/s | 33,0 Mo/s | 17,5 Mo/s |
    /// | 48     |           | 37,0 Mo/s | 3,6 Mo/s  |
    ///
    /// Entre deux foyers, c'est la fenêtre fiable de chaque canal qui décide,
    /// et le débit suit le nombre de canaux. Jusqu'à trente-deux, et pas
    /// au-delà : à quarante-huit, un passage à 60 ms s'est effondré.
    ///
    /// Les anciennes mesures, qui plafonnaient vers seize canaux et
    /// s'effondraient à vingt-quatre, venaient d'un /tmp saturé par le banc
    /// lui-même, pas du transport.
    /// </remarks>
    public int DataChannels { get; init; } = 32;

    public int BlockSize { get; init; } = 16 * 1024;

    public RateLimiterSettings Limiter { get; init; } = new();
}

/// <summary>L'état d'un pair, tel que l'interface l'affiche.</summary>
public sealed record PeerStatus(
    PeerId Peer,
    string DisplayName,
    PeerSessionState State,
    PeerView View,
    bool Applied,
    bool FingerprintDisputed,
    string? LastFailure,
    DateTimeOffset? NextAttempt);

/// <summary>
/// Le moteur : il fait vivre une session par pair et décide quoi poser à l'écran.
/// </summary>
/// <remarks>
/// Tout passe par <see cref="TickAsync"/>, appelé à cadence régulière par
/// l'hôte. Rien ici ne dort ni ne boucle de son côté : c'est ce qui rend le
/// moteur observable pas à pas dans un test, avec une horloge que l'on avance à
/// la main.
///
/// Ce qui est long ne se fait jamais dans le tic. Joindre un pair prend une
/// quinzaine de secondes, servir une apparence en prend des centaines, et un
/// redessin passe par le jeu : ces trois-là tournent sur leurs propres tâches,
/// et le tic ne fait qu'en ramasser le résultat. Un pair lent n'empêche donc
/// jamais un autre d'avancer.
/// </remarks>
public sealed class SyncEngine : IAsyncDisposable
{
    private readonly PairBook _book;
    private readonly IPeerDialer _dialer;
    private readonly ILocalAppearance _local;
    private readonly IRemoteApplicator _applicator;
    private readonly IBlobStore _store;
    private readonly PeerId _ourId;
    private readonly ECDsa _identity;
    private readonly IClock _clock;
    private readonly ILogSink _log;
    private readonly SyncEngineSettings _settings;

    /// <summary>Réglable à chaud : relu par chaque session au tic suivant.</summary>
    private volatile bool _uploadLimited;
    private readonly Quotas _quotas;

    private readonly Dictionary<PeerId, Runtime> _runtimes = [];
    private readonly CancellationTokenSource _life = new();

    /// <summary>Les demandes de réapplication, servies au prochain tic.</summary>
    /// <remarks>
    /// Une file plutôt qu'un accès direct : l'interface et le menu du jeu
    /// appellent depuis un autre fil que celui du moteur, et les structures du
    /// moteur ne sont pas faites pour deux fils.
    /// </remarks>
    private readonly ConcurrentQueue<(PeerId? Id, PlayerFingerprint? Fingerprint)> _reapply = new();

    private CharacterManifest? _announcedManifest;
    private PlayerFingerprint? _announcedFingerprint;
    private bool _ticking;

    public SyncEngine(
        PairBook book, IPeerDialer dialer, ILocalAppearance local, IRemoteApplicator applicator,
        IBlobStore store, PeerId ourId, ECDsa identity, IClock clock, ILogSink log,
        SyncEngineSettings? settings = null, Quotas? quotas = null)
    {
        _book = book;
        _dialer = dialer;
        _local = local;
        _applicator = applicator;
        _store = store;
        _ourId = ourId;
        _identity = identity;
        _clock = clock;
        _log = log;
        _settings = settings ?? SyncEngineSettings.Default;
        _uploadLimited = _settings.LimitUpload;
        _quotas = quotas ?? Quotas.Default;
    }

    public IReadOnlyList<PeerStatus> Statuses =>
        _runtimes.Select(entry => new PeerStatus(
            entry.Key,
            entry.Value.Pair.DisplayName,
            entry.Value.Session?.State ?? PeerSessionState.Disconnected,
            entry.Value.Exchange?.View ?? EmptyView,
            entry.Value.AppliedOn is not null,
            entry.Value.Disputed,
            entry.Value.LastFailure,
            entry.Value.Session is null ? entry.Value.NextAttempt : null))
        .ToList();

    private static PeerView EmptyView { get; } = new(null, null, null, 0, 0, false);

    /// <summary>
    /// Un pas du moteur.
    /// </summary>
    /// <param name="visible">Les joueurs actuellement dans notre champ.</param>
    /// <param name="ct">Annulation de ce pas, distincte de la vie du moteur.</param>
    /// <remarks>
    /// Les appels ne se chevauchent pas : un tic qui arrive pendant qu'un autre
    /// tourne est abandonné plutôt que mis en file. Deux passes simultanées
    /// pourraient lancer deux fois la même application, et une cadence en retard
    /// se rattrape toute seule au tic suivant.
    /// </remarks>
    public async Task TickAsync(IReadOnlyList<VisiblePlayer> visible, CancellationToken ct)
    {
        if (_life.IsCancellationRequested || _ticking)
            return;

        _ticking = true;

        try
        {
            await ReconcileBookAsync(ct).ConfigureAwait(false);
            await ServeReapplyAsync(ct).ConfigureAwait(false);
            await AdoptFinishedDialsAsync(ct).ConfigureAwait(false);
            await DropDeadSessionsAsync().ConfigureAwait(false);
            StartDueDials();
            ObserveLinks();
            await AnnounceIfChangedAsync(ct).ConfigureAwait(false);
            ReconcileVisibility(visible);
        }
        finally
        {
            _ticking = false;
        }
    }

    /// <summary>Aligne les runtimes sur le carnet : un pair actif, un runtime.</summary>
    /// <summary>Redemande et repose l'apparence de ce pair.</summary>
    public void Reapply(PeerId id) => _reapply.Enqueue((id, null));

    /// <summary>Embraye ou débraye le limiteur d'envoi, sessions ouvertes comprises.</summary>
    public void SetUploadLimited(bool limited) => _uploadLimited = limited;

    /// <summary>Même chose, pour le personnage visible qui porte cette empreinte.</summary>
    public void Reapply(PlayerFingerprint fingerprint) => _reapply.Enqueue((null, fingerprint));

    private async Task ServeReapplyAsync(CancellationToken ct)
    {
        while (_reapply.TryDequeue(out var request))
        {
            var runtime = _runtimes.Values.FirstOrDefault(candidate =>
                request.Id is { } id ? candidate.Pair.Id == id
                                     : candidate.Exchange?.View.Fingerprint == request.Fingerprint
                                       || candidate.Pair.PinnedFingerprint == request.Fingerprint);

            if (runtime is null)
                continue;

            // Oublier ce qu'on a posé suffit à reposer au prochain passage. Et on
            // redemande le manifeste, pour que ce soit bien le dernier.
            runtime.AppliedManifest = null;
            runtime.AppliedValue = null;
            runtime.AppliedOn = null;

            if (runtime.Exchange is { } exchange && runtime.Session is not null)
            {
                try
                {
                    await exchange.RefreshAsync(ct).ConfigureAwait(false);
                    _log.Info($"{runtime.Pair.DisplayName} : réapplication demandée.");
                }
                catch (Exception e)
                {
                    _log.Warning($"{runtime.Pair.DisplayName} : redemande du manifeste en échec.", e);
                }
            }
        }
    }

    private async Task ReconcileBookAsync(CancellationToken ct)
    {
        var active = _book.Active.ToDictionary(pair => pair.Id);

        foreach (var (id, runtime) in _runtimes.ToList())
        {
            if (active.TryGetValue(id, out var pair))
            {
                runtime.Pair = pair;
                continue;
            }

            // Mis en pause, bloqué ou supprimé : on débranche et on efface ce
            // qu'on avait posé. Laisser l'apparence en place ferait de la mise
            // en pause un bouton sans effet visible.
            _log.Info($"{runtime.Pair.DisplayName} : pair retiré des actifs, session fermée.");
            await TearDownAsync(id, runtime, ct).ConfigureAwait(false);
            _runtimes.Remove(id);
        }

        foreach (var (id, pair) in active)
        {
            if (_runtimes.ContainsKey(id))
                continue;

            // Joignable tout de suite : une reprise de plugin ne doit pas coûter
            // une attente à l'utilisateur.
            _runtimes[id] = new Runtime { Pair = pair, NextAttempt = _clock.UtcNow };
        }
    }

    private async Task AdoptFinishedDialsAsync(CancellationToken ct)
    {
        foreach (var (id, runtime) in _runtimes)
        {
            if (runtime.Dial is not { IsCompleted: true } dial)
                continue;

            runtime.Dial = null;

            DialResult result;

            try
            {
                result = await dial.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                continue;
            }
            catch (Exception e)
            {
                _log.Warning($"{runtime.Pair.DisplayName} : tentative en échec.", e);
                result = new DialResult(null, false, e.Message);
            }

            if (result.Session is { } session)
                await AdoptSessionAsync(id, runtime, session, ct).ConfigureAwait(false);
            else
                Retry(runtime, result.PeerWasAbsent, result.Failure);
        }
    }

    private async Task AdoptSessionAsync(PeerId id, Runtime runtime, PeerSession session, CancellationToken ct)
    {
        var limiter = new RateLimiter(_clock, _settings.Limiter);

        var exchange = new PeerExchange(
            session, _store, _local, limiter, _settings.DataChannels, _settings.BlockSize, _quotas, _log);

        runtime.Session = session;
        runtime.SessionSince = _clock.UtcNow;
        runtime.Exchange = exchange;
        runtime.Limiter = limiter;
        limiter.Bypassed = _uploadLimited is false;
        runtime.Failures = 0;
        runtime.LastFailure = null;
        runtime.Life = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);

        // Deux tâches et non une : le service des blobs d'un pair dure des
        // minutes, et s'il tenait la même boucle que la réception, les trames
        // qu'il nous envoie pendant ce temps s'entasseraient en mémoire dans le
        // processus du jeu. Huit cents mégaoctets par personne, dans les deux
        // sens en même temps.
        runtime.Pump = PumpAsync(runtime, exchange, session, runtime.Life.Token);
        runtime.Serve = exchange.ServeAsync(runtime.Life.Token);

        _book.Seen(id);

        try
        {
            await exchange.HelloAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _log.Warning($"{runtime.Pair.DisplayName} : présence non annoncée.", e);
        }
    }

    private async Task DropDeadSessionsAsync()
    {
        foreach (var (id, runtime) in _runtimes)
        {
            if (runtime.Session is not { State: PeerSessionState.Disconnected })
                continue;

            _log.Info($"{runtime.Pair.DisplayName} : session tombée.");

            var stable = _clock.UtcNow - runtime.SessionSince >= _settings.StableSession;

            await TearDownAsync(id, runtime, CancellationToken.None).ConfigureAwait(false);

            if (stable)
            {
                // Mesuré en jeu : à cinq secondes d'attente ici, une pause suivie
                // d'une reprise coûtait une dizaine de secondes, l'autre n'étant
                // pas encore revenu au rendez-vous quand on l'y cherchait.
                runtime.Failures = 0;
                runtime.LastFailure = null;
                runtime.NextAttempt = _clock.UtcNow;
            }
            else
            {
                Retry(runtime, peerWasAbsent: false, "lien perdu");
            }
        }
    }

    private void StartDueDials()
    {
        foreach (var runtime in _runtimes.Values)
        {
            if (runtime.Session is not null || runtime.Dial is not null)
                continue;

            if (_clock.UtcNow < runtime.NextAttempt)
                continue;

            runtime.DialStartedAt = _clock.UtcNow;
            runtime.Dial = DialAsync(runtime.Pair, _life.Token);
        }
    }

    private async Task<DialResult> DialAsync(PairRecord pair, CancellationToken ct)
    {
        var attempt = await _dialer.ConnectAsync(pair, ct).ConfigureAwait(false);

        if (attempt.Link is null)
            return new DialResult(null, attempt.PeerWasAbsent, attempt.Failure);

        // En cas de refus, la session a déjà refermé le lien : rien à libérer ici.
        var session = await PeerSession
            .EstablishAsync(attempt.Link, pair, _ourId, _identity, _clock, _log, ct)
            .ConfigureAwait(false);

        return session is null
            ? new DialResult(null, false, "session refusée")
            : new DialResult(session, false, null);
    }

    /// <summary>Donne au limiteur ce que le lien observe, pour qu'il puisse céder.</summary>
    /// <remarks>
    /// Sans cette mesure, le limiteur monterait jusqu'à son plafond et n'en
    /// redescendrait jamais : c'est elle, et elle seule, qui ferme la boucle
    /// entre la congestion réelle et le débit que l'on s'autorise.
    /// </remarks>
    private void ObserveLinks()
    {
        foreach (var runtime in _runtimes.Values)
        {
            if (runtime is not { Session: { } session, Limiter: { } limiter })
                continue;

            limiter.Bypassed = _uploadLimited is false;
            limiter.Observe(session.Link.PacketLossPercent, session.Link.RoundTripMs);
        }
    }

    /// <summary>Réannonce notre apparence aux pairs connectés quand elle a changé.</summary>
    /// <remarks>
    /// Rien n'est envoyé tant que rien ne bouge : la session ouverte vaut
    /// présence, et un battement de cœur applicatif serait du trafic pur.
    /// </remarks>
    private async Task AnnounceIfChangedAsync(CancellationToken ct)
    {
        var manifest = await _local.CurrentAsync(ct).ConfigureAwait(false);
        var fingerprint = _local.Fingerprint;

        // Comparaison par référence : le manifeste n'est reconstruit que
        // lorsqu'un changement a été détecté, donc la même instance signifie la
        // même apparence, sans rehacher des centaines de mégaoctets.
        if (ReferenceEquals(manifest, _announcedManifest) && fingerprint == _announcedFingerprint)
            return;

        _announcedManifest = manifest;
        _announcedFingerprint = fingerprint;

        foreach (var runtime in _runtimes.Values)
        {
            if (runtime.Exchange is not { } exchange || runtime.Session is null)
                continue;

            try
            {
                await exchange.HelloAsync(ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log.Warning($"{runtime.Pair.DisplayName} : réannonce en échec.", e);
            }
        }
    }

    /// <summary>Pose ce qui doit l'être, retire ce qui ne doit plus l'être.</summary>
    private void ReconcileVisibility(IReadOnlyList<VisiblePlayer> visible)
    {
        var announced = new Dictionary<PeerId, PlayerFingerprint>();

        foreach (var (id, runtime) in _runtimes)
        {
            if (runtime.Exchange?.View.Fingerprint is not { } fingerprint || fingerprint == default)
                continue;

            if (runtime.Pair.Permissions.HasFlag(PairPermissions.ReceiveAppearance) is false)
                continue;

            if (runtime.Pair.PinnedFingerprint is { } pinned)
            {
                // Le pair annonce un autre personnage que celui auprès duquel on
                // s'est pairé. Ce peut être son alternatif comme ce peut être la
                // revendication du personnage d'un tiers, sur qui ses fichiers
                // s'appliqueraient chez nous. Dans le doute on ne pose rien, et
                // l'interface a de quoi le dire.
                if (pinned != fingerprint)
                {
                    if (runtime.Disputed is false)
                        _log.Warning($"{runtime.Pair.DisplayName} : empreinte de personnage inattendue, rien n'est posé.");

                    runtime.Disputed = true;
                    continue;
                }
            }
            else
            {
                _book.PinFingerprint(id, fingerprint);
                runtime.Pair = _book.Find(id) ?? runtime.Pair;
            }

            runtime.Disputed = false;
            announced[id] = fingerprint;
        }

        var matched = VisibilityMatcher.Match(visible, announced)
            .ToDictionary(match => match.Peer, match => match.Object);

        foreach (var (id, runtime) in _runtimes)
        {
            if (runtime.Busy)
                continue;

            var inSight = matched.TryGetValue(id, out var target);

            if (inSight && runtime.Exchange is { View: { Ready: true, Manifest: { } manifest } })
            {
                var hash = ManifestCodec.HashOf(manifest);

                if (runtime.AppliedOn == target && runtime.AppliedManifest == hash)
                    continue;

                if (_applicator.CanApply(out var why) is false)
                {
                    _log.Debug($"{runtime.Pair.DisplayName} : application différée, {why}");
                    continue;
                }

                // Même objet, mêmes fichiers : seuls les plugins voisins ont
                // quelque chose à reposer, et un redessin ferait clignoter le
                // personnage entier pour un titre qui change.
                if (runtime.AppliedOn == target
                    && runtime.AppliedValue is { } before
                    && ExtrasDiff.OnlyExtrasDiffer(before, manifest))
                {
                    var change = ExtrasDiff.Between(before.ExtrasOrNone, manifest.ExtrasOrNone);
                    runtime.Work = ApplyExtrasAsync(id, runtime, target, manifest, change, hash);
                }
                else
                {
                    runtime.Work = ApplyAsync(id, runtime, target, manifest, hash);
                }
            }
            else if (runtime.AppliedOn is not null && inSight is false)
            {
                runtime.Work = RemoveAsync(id, runtime);
            }
        }
    }

    private async Task ApplyAsync(
        PeerId id, Runtime runtime, GameObjectRef target, CharacterManifest manifest, BlobHash hash)
    {
        try
        {
            var extrasPosed = await _applicator.ApplyAsync(id, target, manifest, _life.Token).ConfigureAwait(false);

            runtime.AppliedOn = target;
            RecordApplied(runtime, manifest, hash, extrasPosed);
            runtime.Session?.MarkApplied();
            _book.Seen(id);

            _log.Info($"{runtime.Pair.DisplayName} : apparence posée.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            runtime.LastFailure = $"application en échec : {e.Message}";
            _log.Warning($"{runtime.Pair.DisplayName} : application en échec.", e);
        }
    }

    /// <summary>
    /// Note ce qui est réellement à l'écran.
    /// </summary>
    /// <remarks>
    /// Des extras qui n'ont pas pu être posés (personnage trop long à charger)
    /// sont notés absents : au tic suivant, le manifeste diffère de ce qui est
    /// posé par ses seuls extras, et ils sont retentés sans redessin. Les croire
    /// posés laissait le pair sans ses proportions jusqu'à sa réapparition.
    /// </remarks>
    private static void RecordApplied(Runtime runtime, CharacterManifest manifest, BlobHash hash, bool extrasPosed)
    {
        var onScreen = extrasPosed ? manifest : manifest with { Extras = null };

        runtime.AppliedValue = onScreen;
        runtime.AppliedManifest = extrasPosed ? hash : ManifestCodec.HashOf(onScreen);
    }

    private async Task ApplyExtrasAsync(
        PeerId id, Runtime runtime, GameObjectRef target, CharacterManifest manifest, ExtrasChange change, BlobHash hash)
    {
        try
        {
            var extrasPosed = await _applicator.ApplyExtrasAsync(id, target, manifest.ExtrasOrNone, change, _life.Token).ConfigureAwait(false);

            RecordApplied(runtime, manifest, hash, extrasPosed);

            _log.Info($"{runtime.Pair.DisplayName} : extras reposés sans redessin.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            runtime.LastFailure = $"extras en échec : {e.Message}";
            _log.Warning($"{runtime.Pair.DisplayName} : extras en échec.", e);
        }
    }

    private async Task RemoveAsync(PeerId id, Runtime runtime)
    {
        try
        {
            await _applicator.RemoveAsync(id, _life.Token).ConfigureAwait(false);

            runtime.AppliedOn = null;
            runtime.AppliedManifest = null;
            runtime.AppliedValue = null;

            // La session reste ouverte et le cache reste plein : le pair va
            // revenir, et tout refaire coûterait un transfert complet.
            runtime.Session?.MarkOutOfSight();

            _log.Info($"{runtime.Pair.DisplayName} : hors du champ, apparence retirée.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            runtime.LastFailure = $"retrait en échec : {e.Message}";
            _log.Warning($"{runtime.Pair.DisplayName} : retrait en échec.", e);
        }
    }

    private async Task PumpAsync(Runtime runtime, PeerExchange exchange, PeerSession session, CancellationToken ct)
    {
        try
        {
            await foreach (var message in session.Messages.ReadAllAsync(ct).ConfigureAwait(false))
                await exchange.HandleAsync(message, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _log.Warning($"{runtime.Pair.DisplayName} : dialogue interrompu.", e);
        }
    }

    /// <summary>Referme tout ce qui concerne un pair, y compris ce qui est à l'écran.</summary>
    private async Task TearDownAsync(PeerId id, Runtime runtime, CancellationToken ct)
    {
        runtime.Life?.Cancel();

        if (runtime.AppliedOn is not null)
        {
            try
            {
                await _applicator.RemoveAsync(id, ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log.Warning($"{runtime.Pair.DisplayName} : retrait en échec à la fermeture.", e);
            }

            runtime.AppliedOn = null;
            runtime.AppliedManifest = null;
            runtime.AppliedValue = null;
        }

        if (runtime.Exchange is { } exchange)
            await exchange.DisposeAsync().ConfigureAwait(false);

        if (runtime.Session is { } session)
            await session.DisposeAsync().ConfigureAwait(false);

        runtime.Life?.Dispose();
        runtime.Life = null;
        runtime.Exchange = null;
        runtime.Session = null;
        runtime.Limiter = null;
        runtime.Pump = null;
        runtime.Serve = null;
    }

    private void Retry(Runtime runtime, bool peerWasAbsent, string? failure)
    {
        runtime.LastFailure = peerWasAbsent ? null : failure;

        if (peerWasAbsent)
        {
            // Depuis le début de la tentative, et non sa fin : l'annonce tient
            // vingt-cinq secondes, et compter depuis sa fin ferait un cycle de
            // cinquante-cinq où l'on n'est présent que vingt-cinq. C'est le
            // recouvrement des annonces des deux côtés qui les apparie.
            var next = runtime.DialStartedAt + _settings.AbsentBackoff;
            runtime.NextAttempt = next > _clock.UtcNow ? next : _clock.UtcNow;
            return;
        }

        // Doublement, pour qu'un rendez-vous en panne ou un pair injoignable ne
        // se traduise pas par une tentative toutes les cinq secondes jusqu'au
        // soir.
        runtime.Failures++;

        var shift = Math.Min(runtime.Failures - 1, 20);
        var ticks = Math.Min(_settings.MaxBackoff.Ticks, _settings.FirstBackoff.Ticks * (1L << shift));

        runtime.NextAttempt = _clock.UtcNow + TimeSpan.FromTicks(ticks);

        if (failure is not null)
            _log.Debug($"{runtime.Pair.DisplayName} : {failure}, reprise dans {TimeSpan.FromTicks(ticks).TotalSeconds:0} s.");
    }

    public async ValueTask DisposeAsync()
    {
        await _life.CancelAsync().ConfigureAwait(false);

        foreach (var (id, runtime) in _runtimes)
            await TearDownAsync(id, runtime, CancellationToken.None).ConfigureAwait(false);

        _runtimes.Clear();
        _life.Dispose();
    }

    private sealed record DialResult(PeerSession? Session, bool PeerWasAbsent, string? Failure);

    /// <summary>Ce que le moteur tient pour un pair, entre deux tics.</summary>
    private sealed class Runtime
    {
        public required PairRecord Pair { get; set; }

        public CancellationTokenSource? Life { get; set; }

        public PeerSession? Session { get; set; }

        public PeerExchange? Exchange { get; set; }

        public RateLimiter? Limiter { get; set; }

        public Task<DialResult>? Dial { get; set; }

        public Task? Pump { get; set; }

        public Task? Serve { get; set; }

        /// <summary>L'application ou le retrait en cours, jamais deux à la fois.</summary>
        public Task? Work { get; set; }

        public int Failures { get; set; }

        public DateTimeOffset NextAttempt { get; set; }

        public DateTimeOffset DialStartedAt { get; set; }

        public DateTimeOffset SessionSince { get; set; }

        public string? LastFailure { get; set; }

        public GameObjectRef? AppliedOn { get; set; }

        public BlobHash? AppliedManifest { get; set; }

        /// <summary>Le manifeste posé, pour savoir ce qu'un nouveau change.</summary>
        public CharacterManifest? AppliedValue { get; set; }

        public bool Disputed { get; set; }

        public bool Busy => Work is { IsCompleted: false };
    }
}
