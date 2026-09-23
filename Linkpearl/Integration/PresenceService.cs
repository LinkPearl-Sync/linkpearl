using System.Collections.Concurrent;
using System.Security.Cryptography;
using Dalamud.Plugin.Services;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Integration;

/// <summary>Une demande de pairage reçue, en attente de décision.</summary>
public sealed record IncomingRequest(
    PeerId Id, byte[] PublicKey, byte[] PairingNonce, string CharacterName, ushort WorldId,
    DateTimeOffset ReceivedAt);

/// <summary>
/// Présence au rendez-vous, détection des joueurs alentour, demandes de pairage.
/// </summary>
/// <remarks>
/// Une seule connexion, tenue ouverte. C'est elle qui vaut présence : la fermer
/// vaut déclaration d'absence, sans battement de cœur à gérer. C'est aussi elle
/// qui reçoit les demandes, poussées par le serveur plutôt que sondées.
///
/// Ce que le rendez-vous apprend, et c'est assumé : quels noms de personnage
/// sont en ligne. Une adresse de boîte dérive du nom, donc un inconnu ne peut
/// pas reconnaître quelqu'un sans que le serveur le puisse aussi. Voir
/// docs/pairage.md.
/// </remarks>
public sealed class PresenceService : IDisposable
{
    private readonly Configuration _configuration;
    private readonly Func<IdentityKeyPair?> _identity;
    private readonly IPluginLog _log;
    private readonly IClock _clock;

    private readonly ConcurrentQueue<IncomingRequest> _incoming = new();
    private readonly ConcurrentDictionary<PlayerFingerprint, DateTimeOffset> _detected = new();

    private readonly Dictionary<RendezvousAddress, Session> _sessions = [];
    private readonly HashSet<string> _seen = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// Ce que nous tenons ouvert auprès d'un service.
    /// </summary>
    /// <remarks>
    /// Une par service activé. La connexion elle-même vaut présence : la fermer
    /// déclare l'absence, sans battement de cœur à gérer. Chaque session a son
    /// propre compte à rebours de reprise, pour qu'un service en panne ne
    /// retarde pas les autres.
    /// </remarks>
    private sealed class Session
    {
        public required RendezvousAddress At { get; init; }

        public RendezvousClient? Client { get; set; }

        public CancellationTokenSource? Life { get; set; }

        public PlayerFingerprint? OpenedFor { get; set; }

        /// <summary>La fenêtre sous laquelle les boîtes ont été ouvertes.</summary>
        public long OpenedWindow { get; set; }

        public string? Failure { get; set; }

        public DateTimeOffset NextAttempt { get; set; }
    }

    /// <remarks>
    /// L'identité est demandée à chaque usage et non prise une fois : elle
    /// appartient au personnage connecté, donc elle apparaît à la connexion,
    /// change au changement de personnage, et n'existe pas à l'écran-titre.
    /// </remarks>
    public PresenceService(Configuration configuration, Func<IdentityKeyPair?> identity, IClock clock, IPluginLog log)
    {
        _configuration = configuration;
        _identity = identity;
        _clock = clock;
        _log = log;
    }

    /// <summary>Ce qu'on répond tant qu'aucun personnage n'est connecté.</summary>
    private const string NoCharacter =
        "connectez-vous d'abord : l'identité Linkpearl appartient au personnage, pas à l'installation.";

    /// <summary>Vrai dès qu'un seul service répond.</summary>
    /// <remarks>
    /// Un seul suffit à être vu et à voir : exiger que tous répondent ferait
    /// dépendre l'affichage du plus mal en point.
    /// </remarks>
    public bool Connected => ConnectedCount > 0;

    public int ConnectedCount
    {
        get
        {
            lock (_gate)
                return _sessions.Values.Count(session => session.Client is not null);
        }
    }

    public int ConfiguredCount => _configuration.ActiveRendezvous.Count;

    /// <summary>La panne du premier service qui en signale une, s'il y en a.</summary>
    public string? LastFailure
    {
        get
        {
            lock (_gate)
                return _sessions.Values.FirstOrDefault(session => session.Failure is not null)?.Failure;
        }
    }

    /// <summary>Les empreintes reconnues comme utilisant le plugin, avec leur fraîcheur.</summary>
    public IReadOnlyDictionary<PlayerFingerprint, DateTimeOffset> Detected => _detected;

    public bool TryTakeRequest(out IncomingRequest? request)
    {
        var taken = _incoming.TryDequeue(out var value);
        request = value;
        return taken;
    }

    public IReadOnlyList<IncomingRequest> PeekRequests() => _incoming.ToArray();

    /// <summary>
    /// Ouvre nos boîtes, ou les rouvre si le personnage a changé.
    /// </summary>
    /// <remarks>
    /// Le changement de personnage compte : les boîtes dérivent du nom, donc
    /// celles de l'ancien doivent se fermer et celles du nouveau s'ouvrir.
    /// </remarks>
    public async Task EnsureOpenAsync(PlayerFingerprint fingerprint, CancellationToken ct)
    {
        if (_configuration.Discoverable is false)
        {
            CloseAll();
            return;
        }

        Reconcile();

        foreach (var session in Snapshot())
        {
            // Le changement de personnage compte : les boîtes dérivent du nom,
            // donc celles de l'ancien doivent se fermer et celles du nouveau
            // s'ouvrir.
            if (session.Client is not null && session.OpenedFor != fingerprint)
                Close(session);

            // Les adresses tournent toutes les trente minutes. Une boîte
            // ouverte une fois pour toutes cesse d'être trouvable dès que la
            // fenêtre suivante commence, et plus rien ne le dit : la détection
            // s'arrête en silence sur une connexion qui a l'air en bonne santé.
            if (session.Client is { } open && session.OpenedWindow != MailboxAddress.IndexAt(_clock.UtcNow))
                await ReopenAsync(session, open, fingerprint, ct).ConfigureAwait(false);

            if (session.Client is not null || _clock.UtcNow < session.NextAttempt)
                continue;

            await OpenAsync(session, fingerprint, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Aligne les sessions sur la liste des services activés.</summary>
    private void Reconcile()
    {
        var active = _configuration.ActiveRendezvous.Select(entry => entry.Address).ToHashSet();

        lock (_gate)
        {
            foreach (var (at, session) in _sessions.ToList())
            {
                if (active.Contains(at))
                    continue;

                // Retiré des réglages : on ferme, sinon l'utilisateur resterait
                // annoncé sur un service qu'il croit avoir quitté.
                CloseLocked(session);
                _sessions.Remove(at);
            }

            foreach (var at in active)
                if (_sessions.ContainsKey(at) is false)
                    _sessions[at] = new Session { At = at, NextAttempt = _clock.UtcNow };
        }
    }

    private async Task OpenAsync(Session session, PlayerFingerprint fingerprint, CancellationToken ct)
    {
        try
        {
            var client = new RendezvousClient();
            await client.ConnectAsync(session.At.Host, session.At.Port, ct).ConfigureAwait(false);

            client.Delivered += OnDelivered;

            var window = MailboxAddress.IndexAt(_clock.UtcNow);

            await client.OpenMailboxesAsync(Addresses(fingerprint), ct).ConfigureAwait(false);

            var life = new CancellationTokenSource();

            lock (_gate)
            {
                session.Client = client;
                session.Life = life;
                session.OpenedFor = fingerprint;
                session.OpenedWindow = window;
                session.Failure = null;
            }

            _ = Task.Run(() => ListenAsync(session, client, life.Token), life.Token);
        }
        catch (Exception e)
        {
            lock (_gate)
            {
                session.Failure = e.Message;

                // Trente secondes avant de réessayer : un service éteint ne doit
                // pas être sollicité à chaque ronde de détection.
                session.NextAttempt = _clock.UtcNow + TimeSpan.FromSeconds(30);
            }

            _log.Warning(e, $"Ouverture des boîtes en échec sur {session.At}.");
        }
    }

    /// <summary>
    /// Rouvre les boîtes sous la fenêtre courante, sur la connexion en place.
    /// </summary>
    /// <remarks>
    /// Sans reconnexion : le service garde les anciennes adresses liées à cette
    /// session jusqu'à sa fermeture, et en ajouter n'en retire aucune. Celui
    /// qui nous cherchait sous l'ancienne nous trouve donc encore, et celui qui
    /// arrive nous trouve sous la nouvelle.
    /// </remarks>
    private async Task ReopenAsync(
        Session session, RendezvousClient client, PlayerFingerprint fingerprint, CancellationToken ct)
    {
        var window = MailboxAddress.IndexAt(_clock.UtcNow);

        try
        {
            await client.OpenMailboxesAsync(Addresses(fingerprint), ct).ConfigureAwait(false);

            lock (_gate)
                session.OpenedWindow = window;
        }
        catch (Exception e)
        {
            // La connexion est peut-être morte sans qu'on l'ait vu : on la
            // ferme, et la même ronde la rouvrira proprement.
            _log.Warning(e, $"Réouverture des boîtes en échec sur {session.At}.");
            Close(session);
        }
    }

    /// <summary>Les adresses à ouvrir : la fenêtre courante et la suivante.</summary>
    private List<byte[]> Addresses(PlayerFingerprint fingerprint)
        => [.. MailboxAddress.Around(fingerprint, _clock.UtcNow).Select(address => address.ToBytes())];

    /// <summary>
    /// Demande à tous les services lesquels de ces joueurs utilisent le plugin.
    /// </summary>
    /// <remarks>
    /// Un joueur est détecté dès qu'un seul service reconnaît sa boîte, et
    /// l'union se fait sur ceux qui répondent. Exiger l'accord de tous rendrait
    /// la détection dépendante du plus mal en point ; et le silence d'un service
    /// n'est pas une réponse négative, donc il ne retire personne.
    /// </remarks>
    public async Task RefreshDetectionAsync(IReadOnlyList<NearbyPlayer> nearby, CancellationToken ct)
    {
        if (nearby.Count == 0)
            return;

        var connected = Snapshot().Where(session => session.Client is not null).ToList();

        if (connected.Count == 0)
            return;

        var seenSomewhere = new HashSet<PlayerFingerprint>();
        var answered = false;

        foreach (var session in connected)
        {
            // Le serveur plafonne les interrogations : on découpe plutôt que de
            // se faire refuser.
            foreach (var batch in nearby.Chunk(RendezvousWire.MaxQueriedAddresses))
            {
                var addresses = batch
                    .Select(player => MailboxAddress.Of(player.Fingerprint, _clock.UtcNow).ToBytes())
                    .ToList();

                try
                {
                    var present = await session.Client!.QueryPresenceAsync(addresses, ct).ConfigureAwait(false);

                    if (present is null)
                        break;

                    answered = true;

                    for (var i = 0; i < batch.Length && i < present.Length; i++)
                        if (present[i])
                            seenSomewhere.Add(batch[i].Fingerprint);
                }
                catch (Exception e)
                {
                    lock (_gate)
                        session.Failure = e.Message;

                    _log.Warning(e, $"Interrogation de présence en échec sur {session.At}.");
                    break;
                }
            }
        }

        // Rien de retiré tant que personne n'a répondu : perdre tout le monde
        // parce que le réseau a hoqueté ferait clignoter la liste.
        if (answered is false)
            return;

        foreach (var player in nearby)
        {
            if (seenSomewhere.Contains(player.Fingerprint))
                _detected[player.Fingerprint] = _clock.UtcNow;
            else
                _detected.TryRemove(player.Fingerprint, out _);
        }
    }

    /// <summary>Dépose une demande de pairage dans la boîte d'un joueur.</summary>
    public async Task<string> RequestPairAsync(NearbyPlayer target, NearbyPlayer self, CancellationToken ct)
    {
        if (Connected is false)
            return "aucun service de rendez-vous joignable.";

        if (_identity() is not { } identity)
            return NoCharacter;

        var nonce = RandomNumberGenerator.GetBytes(PairRequestMessage.NonceLength);

        var message = new PairRequestMessage(
            IsAccept: false, identity.PublicKey, nonce, self.Name, self.WorldId);

        var address = MailboxAddress.Of(target.Fingerprint, _clock.UtcNow).ToBytes();
        var (delivered, failure) = await DepositEverywhereAsync(address, message.Encode(), ct).ConfigureAwait(false);

        if (delivered == 0)
            return $"envoi impossible : {failure}";

        PendingOutgoing[target.Fingerprint] = nonce;
        return $"demande envoyée à {target.Name}.";
    }

    /// <summary>
    /// Dépose sur tous nos services à la fois.
    /// </summary>
    /// <remarks>
    /// Nous ignorons lequel la cible utilise : sa boîte vit chez le service
    /// qu'elle a choisi, pas chez nous. Déposer partout est donc la seule façon
    /// de l'atteindre, et le destinataire dédoublonne à la réception.
    /// </remarks>
    private async Task<(int Delivered, string? Failure)> DepositEverywhereAsync(
        byte[] address, byte[] payload, CancellationToken ct)
    {
        var delivered = 0;
        string? failure = null;

        foreach (var session in Snapshot())
        {
            if (session.Client is null)
                continue;

            try
            {
                await session.Client.DepositAsync(address, payload, ct).ConfigureAwait(false);
                delivered++;
            }
            catch (Exception e)
            {
                failure = e.Message;
                _log.Warning(e, $"Dépôt en échec sur {session.At}.");
            }
        }

        return (delivered, failure);
    }

    /// <summary>Les aléas des demandes que nous avons envoyées, en attente de réponse.</summary>
    public ConcurrentDictionary<PlayerFingerprint, byte[]> PendingOutgoing { get; } = new();

    /// <summary>Accepte une demande reçue et renvoie notre identité au demandeur.</summary>
    public async Task<string> AcceptAsync(IncomingRequest request, NearbyPlayer self, CancellationToken ct)
    {
        if (Connected is false)
            return "aucun service de rendez-vous joignable.";

        if (_identity() is not { } identity)
            return NoCharacter;

        var reply = new PairRequestMessage(
            IsAccept: true, identity.PublicKey, request.PairingNonce, self.Name, self.WorldId);

        var theirFingerprint = PlayerFingerprint.Of(request.CharacterName.Trim().ToLowerInvariant(), request.WorldId);

        try
        {
            var (delivered, failure) = await DepositEverywhereAsync(
                MailboxAddress.Of(theirFingerprint, _clock.UtcNow).ToBytes(), reply.Encode(), ct)
                .ConfigureAwait(false);

            if (delivered == 0)
                return $"réponse impossible : {failure}";

            return $"{request.CharacterName} accepté.";
        }
        catch (Exception e)
        {
            _log.Warning(e, "Réponse d'acceptation en échec.");
            return $"réponse impossible : {e.Message}";
        }
    }

    private void OnDelivered(byte[] payload)
    {
        if (PairRequestMessage.TryDecode(payload, out var message, out var why) is false)
        {
            _log.Warning($"Demande illisible reçue : {why}");
            return;
        }

        var id = PeerId.Of(message!.PublicKey);

        if (_identity() is not { } identity)
            return;   // déconnecté entre-temps : plus personne à qui remettre ceci

        if (id == identity.Id)
            return;   // notre propre écho, sans intérêt

        // Un expéditeur qui dépose sur plusieurs services ne doit produire
        // qu'une seule invite : la même demande nous arrive alors par autant de
        // chemins que nous partageons de services avec lui.
        var key = $"{id.ToHex()}:{Convert.ToHexStringLower(message.PairingNonce)}";

        lock (_gate)
            if (_seen.Add(key) is false)
                return;

        _incoming.Enqueue(new IncomingRequest(
            id, message.PublicKey, message.PairingNonce, message.CharacterName, message.WorldId, _clock.UtcNow));

        _log.Information($"Demande de pairage reçue de {message.CharacterName}.");
    }

    private async Task ListenAsync(Session session, RendezvousClient client, CancellationToken ct)
    {
        try
        {
            await client.ListenAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (ct.IsCancellationRequested is false)
        {
            lock (_gate)
                session.Failure = e.Message;

            _log.Warning(e, $"Écoute interrompue sur {session.At}.");
        }
        finally
        {
            if (ct.IsCancellationRequested is false)
                Close(session);
        }
    }

    private List<Session> Snapshot()
    {
        lock (_gate)
            return [.. _sessions.Values];
    }

    private void Close(Session session)
    {
        lock (_gate)
            CloseLocked(session);
    }

    private void CloseLocked(Session session)
    {
        session.Life?.Cancel();
        session.Life?.Dispose();
        session.Life = null;

        if (session.Client is not null)
        {
            session.Client.Delivered -= OnDelivered;
            _ = session.Client.DisposeAsync();
            session.Client = null;
        }

        session.OpenedFor = null;
    }

    private void CloseAll()
    {
        lock (_gate)
        {
            foreach (var session in _sessions.Values)
                CloseLocked(session);

            _sessions.Clear();
        }

        // Plus personne n'est joignable : garder les détections ferait croire
        // que des joueurs utilisent le plugin alors que plus rien ne le dit.
        _detected.Clear();
    }

    public void Dispose() => CloseAll();
}
