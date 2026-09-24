using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using Dalamud.Plugin.Services;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Integration;

/// <summary>Une demande de pairage reçue, en attente de décision.</summary>
/// <param name="Ephemeral">La clé d'accord éphémère de l'autre, reçue avec la demande.</param>
/// <param name="PairingMaterial">
/// Le matériau du secret de paire, une fois l'accord fait. Absent tant que la
/// demande n'a pas été acceptée : c'est l'acceptation qui tire notre éphémère.
/// </param>
public sealed record IncomingRequest(
    PeerId Id, byte[] PublicKey, byte[] PairingNonce, string CharacterName, ushort WorldId,
    DateTimeOffset ReceivedAt, byte[] Ephemeral, byte[]? PairingMaterial = null);

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

    /// <summary>Les demandes reçues, gardées par <see cref="_gate"/>.</summary>
    /// <remarks>
    /// Une liste et non une file : l'utilisateur répond dans l'ordre qu'il
    /// veut, et c'est la demande cliquée qu'il faut retirer, pas la plus ancienne.
    /// </remarks>
    private readonly List<IncomingRequest> _incoming = [];

    /// <summary>Les acceptations reçues en réponse à nos propres demandes.</summary>
    private readonly ConcurrentQueue<IncomingRequest> _accepted = new();
    private readonly ConcurrentDictionary<PlayerFingerprint, DateTimeOffset> _detected = new();

    private readonly Dictionary<RendezvousAddress, Session> _sessions = [];
    private readonly HashSet<string> _seen = [];
    private readonly Lock _gate = new();

    /// <summary>Remplacés d'un bloc par le fil de rafraîchissement, lus par les ouvertures.</summary>
    private volatile IReadOnlyList<GroupRecord> _groups = [];

    /// <summary>Par joueur détecté, les groupes dont sa boîte de présence a répondu.</summary>
    private readonly ConcurrentDictionary<PlayerFingerprint, IReadOnlyList<GroupId>> _groupPresence = new();

    /// <summary>
    /// Boîtes qu'on s'autorise sur une connexion avant de la refaire à neuf.
    /// </summary>
    /// <remarks>
    /// Le service ne retire jamais une adresse d'une connexion ouverte, et
    /// chaque fenêtre en ajoute : sans remise à zéro, dix groupes épuisent sa
    /// limite de 64 en une heure et demie, et l'ouverture suivante est refusée.
    /// Un peu sous la limite, pour ne jamais la toucher.
    /// </remarks>
    private const int MailboxBudget = 60;

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

        /// <summary>Adresses tenues par cette connexion, qui ne fait qu'en accumuler.</summary>
        public int OpenedCount { get; set; }

        /// <summary>Les groupes pour lesquels les boîtes ont été ouvertes.</summary>
        public string OpenedGroups { get; set; } = string.Empty;

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

    /// <summary>La panne dite au joueur, dans sa langue.</summary>
    /// <remarks>
    /// Le message d'une <see cref="SocketException"/> est celui de Windows, dans
    /// la langue du système et sans le nom en cause : « Le nom demandé est
    /// valide mais aucune donnée du type requise n'a été trouvée » ne dit à
    /// personne que c'est son DNS qui ne connaît pas le service.
    /// </remarks>
    private static string Describe(Exception e) => e switch
    {
        SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain }
            => "adresse du service introuvable (DNS)",
        SocketException { SocketErrorCode: SocketError.ConnectionRefused }
            => "le service refuse la connexion",
        SocketException { SocketErrorCode: SocketError.TimedOut }
            => "le service ne répond pas",
        _ => e.Message,
    };

    /// <summary>Les empreintes reconnues comme utilisant le plugin, avec leur fraîcheur.</summary>
    public IReadOnlyDictionary<PlayerFingerprint, DateTimeOffset> Detected => _detected;

    /// <summary>Le nombre de demandes en attente, sans copier la liste.</summary>
    public int RequestCount
    {
        get
        {
            lock (_gate)
                return _incoming.Count;
        }
    }

    public IReadOnlyList<IncomingRequest> PeekRequests()
    {
        lock (_gate)
            return [.. _incoming];
    }

    /// <summary>Retire une demande à laquelle l'utilisateur vient de répondre.</summary>
    public void Forget(IncomingRequest request)
    {
        lock (_gate)
            _incoming.Remove(request);
    }

    /// <summary>Oublie toutes les demandes, au changement de personnage.</summary>
    /// <remarks>
    /// Une demande est adressée à la boîte d'un personnage : la montrer au
    /// suivant lui ferait accepter, sous son nom, ce qu'on a proposé à un autre.
    /// </remarks>
    public void ForgetRequests()
    {
        lock (_gate)
            _incoming.Clear();
    }

    /// <summary>Une acceptation qui conclut une demande que nous avons envoyée.</summary>
    public bool TryTakeAcceptance(out IncomingRequest? request)
    {
        var taken = _accepted.TryDequeue(out var value);
        request = value;
        return taken;
    }

    public void SetGroups(IReadOnlyList<GroupRecord> groups) => _groups = groups;

    public IReadOnlyList<GroupId> GroupsOf(PlayerFingerprint member)
        => _groupPresence.TryGetValue(member, out var groups) ? groups : [];

    /// <summary>Ce qui distingue un jeu de groupes d'un autre, pour savoir s'il faut rouvrir.</summary>
    private static string Signature(IReadOnlyList<GroupRecord> groups)
        => string.Join(',', groups.Select(group => group.Id.ToString()).Order(StringComparer.Ordinal));

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

            // Les adresses tournent toutes les trente minutes, et changent aussi
            // quand on rejoint ou quitte un groupe.
            if (session.Client is { } open
                && (session.OpenedWindow != MailboxAddress.IndexAt(_clock.UtcNow)
                    || session.OpenedGroups != Signature(_groups)))
                await ReopenAsync(session, open, fingerprint, ct).ConfigureAwait(false);

            if (session.Client is not null || _clock.UtcNow < session.NextAttempt)
                continue;

            await OpenAsync(session, fingerprint, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Aligne les sessions sur la liste des services activés.</summary>
    private void Reconcile()
    {
        // Les services d'un groupe comptent même s'ils ne sont pas dans nos
        // réglages : c'est là que ses membres nous cherchent.
        var active = _configuration.ActiveRendezvous.Select(entry => entry.Address)
            .Concat(_groups.SelectMany(group => group.Rendezvous))
            .ToHashSet();

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
            var groups = _groups;
            var addresses = Addresses(fingerprint, groups);

            await client.OpenMailboxesAsync(addresses, ct).ConfigureAwait(false);

            var life = new CancellationTokenSource();

            lock (_gate)
            {
                session.Client = client;
                session.Life = life;
                session.OpenedFor = fingerprint;
                session.OpenedWindow = window;
                session.Failure = null;
                session.OpenedCount = addresses.Count;
                session.OpenedGroups = Signature(groups);
            }

            _ = Task.Run(() => ListenAsync(session, client, life.Token), life.Token);
        }
        catch (Exception e)
        {
            lock (_gate)
            {
                session.Failure = Describe(e);

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
        var groups = _groups;
        var addresses = Addresses(fingerprint, groups);

        // Au-delà du budget, on repart d'une connexion neuve, rouverte dans la
        // même ronde : la fermeture ne repousse pas la prochaine tentative.
        if (session.OpenedCount + addresses.Count > MailboxBudget)
        {
            Close(session);
            return;
        }

        try
        {
            await client.OpenMailboxesAsync(addresses, ct).ConfigureAwait(false);

            lock (_gate)
            {
                session.OpenedWindow = window;
                session.OpenedCount += addresses.Count;
                session.OpenedGroups = Signature(groups);
            }
        }
        catch (Exception e)
        {
            // La connexion est peut-être morte sans qu'on l'ait vu : on la
            // ferme, et la même ronde la rouvrira proprement.
            _log.Warning(e, $"Réouverture des boîtes en échec sur {session.At}.");
            Close(session);
        }
    }

    /// <summary>
    /// Les adresses à ouvrir : la boîte personnelle, et une boîte de présence
    /// par groupe, chacune sous la fenêtre courante et la suivante.
    /// </summary>
    private List<byte[]> Addresses(PlayerFingerprint fingerprint, IReadOnlyList<GroupRecord> groups)
        => [.. MailboxAddress.Around(fingerprint, _clock.UtcNow)
            .Concat(groups.SelectMany(group => GroupDerivation.PresenceAround(group.Secret, fingerprint, _clock.UtcNow)))
            .Select(address => address.ToBytes())];

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
                    // Un délai de garde, parce qu'une réponse perdue bloquait
                    // toute la boucle de rafraîchissement : plus de détection,
                    // plus de réouverture de boîte, et rien qui le dise.
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    deadline.CancelAfter(TimeSpan.FromSeconds(10));

                    var present = await session.Client!
                        .QueryPresenceAsync(addresses, deadline.Token).ConfigureAwait(false);

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
                        session.Failure = Describe(e);

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

        await RefreshGroupPresenceAsync(nearby, connected, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Demande, pour chaque joueur détecté, s'il est membre de l'un de nos groupes.
    /// </summary>
    /// <remarks>
    /// Seulement les joueurs détectés : un passant sans le plugin n'a pas de
    /// boîte de groupe, et l'interroger ne ferait que coûter au service. Un
    /// joueur qui a coupé la détection n'est donc pas trouvé par ses groupes,
    /// ce que l'interface devra dire.
    /// </remarks>
    private async Task RefreshGroupPresenceAsync(
        IReadOnlyList<NearbyPlayer> nearby, List<Session> connected, CancellationToken ct)
    {
        var groups = _groups;
        var detected = nearby.Where(player => _detected.ContainsKey(player.Fingerprint)).ToList();

        if (groups.Count == 0 || detected.Count == 0)
        {
            _groupPresence.Clear();
            return;
        }

        var questions = detected
            .SelectMany(player => groups.Select(group => (
                player.Fingerprint,
                Group: group.Id,
                Address: GroupDerivation.PresenceAddress(group.Secret, player.Fingerprint, _clock.UtcNow).ToBytes())))
            .ToList();

        var found = new HashSet<(PlayerFingerprint Member, GroupId Group)>();
        var answered = false;

        foreach (var session in connected)
        {
            foreach (var batch in questions.Chunk(RendezvousWire.MaxQueriedAddresses))
            {
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    deadline.CancelAfter(TimeSpan.FromSeconds(10));

                    var present = await session.Client!
                        .QueryPresenceAsync([.. batch.Select(question => question.Address)], deadline.Token)
                        .ConfigureAwait(false);

                    if (present is null)
                        break;

                    answered = true;

                    for (var i = 0; i < batch.Length && i < present.Length; i++)
                        if (present[i])
                            found.Add((batch[i].Fingerprint, batch[i].Group));
                }
                catch (Exception e)
                {
                    _log.Warning(e, $"Interrogation des groupes en échec sur {session.At}.");
                    break;
                }
            }
        }

        // Même règle que la détection : sans réponse, on ne retire personne.
        if (answered is false)
            return;

        _groupPresence.Clear();

        foreach (var member in found.GroupBy(pair => pair.Member))
            _groupPresence[member.Key] = [.. member.Select(pair => pair.Group)];
    }

    /// <summary>Dépose une demande de pairage dans la boîte d'un joueur.</summary>
    public async Task<string> RequestPairAsync(NearbyPlayer target, NearbyPlayer self, CancellationToken ct)
    {
        if (Connected is false)
            return "aucun service de rendez-vous joignable.";

        if (_identity() is not { } identity)
            return NoCharacter;

        var nonce = RandomNumberGenerator.GetBytes(PairRequestMessage.NonceLength);
        var ephemeral = CryptoPrimitives.GenerateEphemeral();

        var message = new PairRequestMessage(
            IsAccept: false, identity.PublicKey, nonce, CryptoPrimitives.ExportPublicPoint(ephemeral),
            self.Name, self.WorldId);

        var address = MailboxAddress.Of(target.Fingerprint, _clock.UtcNow).ToBytes();
        var (delivered, failure) = await DepositEverywhereAsync(address, message.Encode(), ct).ConfigureAwait(false);

        if (delivered == 0)
        {
            ephemeral.Dispose();
            return $"envoi impossible : {failure}";
        }

        // Gardé en mémoire seulement, jusqu'à la réponse : c'est la moitié
        // privée de l'accord qui donnera le secret de paire. Une nouvelle
        // demande à la même personne remplace l'ancienne, dont la réponse ne
        // pourra plus conclure.
        if (_pendingEphemerals.TryRemove(target.Fingerprint, out var previous))
            previous.Dispose();

        _pendingEphemerals[target.Fingerprint] = ephemeral;
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

    /// <summary>La moitié privée de l'accord de chaque demande en attente.</summary>
    private readonly ConcurrentDictionary<PlayerFingerprint, ECDiffieHellman> _pendingEphemerals = new();

    /// <summary>
    /// Accepte une demande reçue et renvoie notre identité au demandeur.
    /// </summary>
    /// <returns>
    /// Le message pour l'utilisateur, et la demande complétée du matériau de
    /// pairage, que le carnet attend. Absente si rien n'a pu être accordé.
    /// </returns>
    public async Task<(string Message, IncomingRequest? Agreed)> AcceptAsync(
        IncomingRequest request, NearbyPlayer self, CancellationToken ct)
    {
        if (Connected is false)
            return ("aucun service de rendez-vous joignable.", null);

        if (_identity() is not { } identity)
            return (NoCharacter, null);

        using var ephemeral = CryptoPrimitives.GenerateEphemeral();
        var agreed = request with
        {
            PairingMaterial = PairRequestMessage.AgreeOnPairing(ephemeral, request.Ephemeral, request.PairingNonce),
        };

        var reply = new PairRequestMessage(
            IsAccept: true, identity.PublicKey, request.PairingNonce, CryptoPrimitives.ExportPublicPoint(ephemeral),
            self.Name, self.WorldId);

        var theirFingerprint = PlayerFingerprint.Of(request.CharacterName.Trim().ToLowerInvariant(), request.WorldId);

        try
        {
            var (delivered, failure) = await DepositEverywhereAsync(
                MailboxAddress.Of(theirFingerprint, _clock.UtcNow).ToBytes(), reply.Encode(), ct)
                .ConfigureAwait(false);

            // Accordé même si la réponse n'est pas partie, comme avant : le
            // demandeur, lui, ne conclura pas, et il redemandera.
            if (delivered == 0)
                return ($"réponse impossible : {failure}", agreed);

            return ($"{request.CharacterName} accepté.", agreed);
        }
        catch (Exception e)
        {
            _log.Warning(e, "Réponse d'acceptation en échec.");
            return ($"réponse impossible : {e.Message}", agreed);
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

        var request = new IncomingRequest(
            id, message.PublicKey, message.PairingNonce, message.CharacterName, message.WorldId, _clock.UtcNow,
            message.Ephemeral);

        // Une acceptation qui porte le nonce de notre propre demande n'est pas
        // une demande : c'est l'autre qui dit oui à ce que nous avons proposé.
        // La faire accepter une seconde fois à celui qui a invité est absurde,
        // et c'est ce qui se passait.
        var sender = PlayerFingerprint.Of(message.CharacterName.Trim().ToLowerInvariant(), message.WorldId);

        if (message.IsAccept
            && PendingOutgoing.TryGetValue(sender, out var ourNonce)
            && ourNonce.AsSpan().SequenceEqual(message.PairingNonce))
        {
            PendingOutgoing.TryRemove(sender, out _);

            // Le plugin a été rechargé depuis la demande : notre moitié de
            // l'accord est perdue, et le secret ne peut plus être calculé.
            if (_pendingEphemerals.TryRemove(sender, out var ours) is false)
            {
                _log.Warning($"{message.CharacterName} a accepté une demande dont l'accord est perdu : redemander.");
                return;
            }

            using (ours)
                _accepted.Enqueue(request with
                {
                    PairingMaterial = PairRequestMessage.AgreeOnPairing(ours, message.Ephemeral, message.PairingNonce),
                });

            _log.Information($"{message.CharacterName} a accepté notre demande.");
            return;
        }

        lock (_gate)
            _incoming.Add(request);

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
                session.Failure = Describe(e);

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

    public void Dispose()
    {
        CloseAll();

        foreach (var ephemeral in _pendingEphemerals.Values)
            ephemeral.Dispose();

        _pendingEphemerals.Clear();
    }
}
