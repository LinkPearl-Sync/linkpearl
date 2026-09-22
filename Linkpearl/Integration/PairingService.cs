using System.Security.Cryptography;
using Dalamud.Plugin.Services;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Integration;

/// <summary>Ce qu'une annonce au rendez-vous a donné.</summary>
public sealed record AnnounceReport(int Pairs, int Announced, int Matched, string? Failure);

/// <summary>
/// Identité, carnet de pairs, et annonce au rendez-vous.
/// </summary>
/// <remarks>
/// Ne connecte encore rien : cette étape sert à éprouver en jeu que l'identité
/// persiste, que les codes d'invitation circulent, et que le plugin sait
/// s'annoncer. La connexion entre pairs vient après.
/// </remarks>
public sealed class PairingService : IDisposable
{
    private readonly IdentityKeyPair _identity;
    private readonly PairBook _book;
    private readonly PairBookStore _bookStore;
    private readonly RendezvousTicket _tickets;
    private readonly PendingInvitationStore _invitationStore;
    private readonly List<PendingInvitation> _pending;
    private readonly Configuration _configuration;
    private readonly IPluginLog _log;

    public PairingService(string root, Configuration configuration, SystemClock clock, IPluginLog log)
    {
        _configuration = configuration;
        _log = log;

        _identity = IdentityKeyPair.LoadOrCreate(new DpapiIdentityStore(Path.Combine(root, "identity.key")));
        _book = new PairBook(clock);
        _bookStore = new PairBookStore(Path.Combine(root, "pairs.json"));
        _bookStore.Load(_book);
        _tickets = new RendezvousTicket(clock);
        _invitationStore = new PendingInvitationStore(Path.Combine(root, "invitations.json"));
        _pending = _invitationStore.Load();
    }

    /// <summary>Notre propre lieu de rendez-vous, en une liste d'un élément.</summary>
    /// <remarks>
    /// Provisoire : la fédération donnera ici le lieu d'où vient l'invitation
    /// suivi du nôtre, de sorte qu'un pairage survive à la perte de l'un d'eux.
    /// </remarks>
    private IReadOnlyList<RendezvousAddress> Here() =>
        [new RendezvousAddress(_configuration.RendezvousHost, _configuration.RendezvousPort)];

    public PeerId Id => _identity.Id;

    public IdentityKeyPair Identity => _identity;

    /// <summary>Ajoute un pair depuis une demande acceptée dans l'interface.</summary>
    public string AddFromRequest(IncomingRequest request)
    {
        if (_book.Find(request.Id) is { } existing)
            return $"déjà appairé avec {existing.DisplayName}.";

        _book.Add(request.Id, request.PublicKey, request.PairingNonce, _identity.Id,
                  request.CharacterName, Here());
        _bookStore.Save(_book);

        return $"{request.CharacterName} ajouté à vos pairs.";
    }

    public PairBook Book => _book;

    public int PendingInvitations => _pending.Count;

    /// <summary>
    /// Dépose une invitation au rendez-vous et rend le ticket de douze caractères.
    /// </summary>
    /// <remarks>
    /// La charge déposée contient notre clé publique compressée et l'aléa de
    /// pairage. <b>Le serveur peut la lire et la remplacer</b> : c'est la
    /// contrepartie d'un ticket court, et c'est la comparaison des six mots qui
    /// la rattrape.
    /// </remarks>
    public async Task<(string? Ticket, string? Rejection)> CreateInvitationAsync(CancellationToken ct)
    {
        var ticket = InvitationTicket.Create();
        var nonce = RandomNumberGenerator.GetBytes(PairingCode.NonceLength);

        var payload = new byte[CryptoPrimitives.CompressedPointLength + PairingCode.NonceLength];
        CryptoPrimitives.Compress(_identity.PublicKey).CopyTo(payload.AsSpan());
        nonce.CopyTo(payload.AsSpan(CryptoPrimitives.CompressedPointLength));

        await using var client = new RendezvousClient();
        await client.ConnectAsync(_configuration.RendezvousHost, _configuration.RendezvousPort, ct).ConfigureAwait(false);

        var rejection = await client.RegisterInvitationAsync(ticket.ToBytes(), payload, ct).ConfigureAwait(false);

        if (rejection is not null)
            return (null, rejection);

        _pending.Add(new PendingInvitation(ticket.Encode(), nonce, DateTimeOffset.UtcNow));
        _invitationStore.Save(_pending);

        return (ticket.Encode(), null);
    }

    /// <summary>Retire une invitation et ajoute son auteur au carnet.</summary>
    public async Task<string> RedeemAsync(string text, string displayName, CancellationToken ct)
    {
        if (InvitationTicket.TryParse(text, out var ticket, out var why) is false)
            return $"ticket refusé : {why}";

        await using var client = new RendezvousClient();
        await client.ConnectAsync(_configuration.RendezvousHost, _configuration.RendezvousPort, ct).ConfigureAwait(false);

        var (payload, rejection) = await client.RedeemInvitationAsync(ticket.ToBytes(), ct).ConfigureAwait(false);

        if (payload is null)
            return $"retrait impossible : {rejection}";

        if (payload.Length != CryptoPrimitives.CompressedPointLength + PairingCode.NonceLength)
            return "invitation malformée.";

        byte[] theirKey;
        try
        {
            theirKey = CryptoPrimitives.Decompress(payload.AsSpan(0, CryptoPrimitives.CompressedPointLength));
        }
        catch (CryptographicException e)
        {
            return $"clé publique invalide dans l'invitation : {e.Message}";
        }

        var nonce = payload.AsSpan(CryptoPrimitives.CompressedPointLength).ToArray();
        var theirId = PeerId.Of(theirKey);

        if (theirId == _identity.Id)
            return "cette invitation est la vôtre.";

        if (_book.Find(theirId) is { } existing)
            return $"déjà dans le carnet sous le nom « {existing.DisplayName} ».";

        _book.Add(theirId, theirKey, nonce, _identity.Id, displayName, Here());
        _bookStore.Save(_book);

        // On dépose notre propre identité dans la case de réponse : sans elle,
        // celui qui a invité ne peut pas dériver le même secret de paire, ne
        // sachant pas d'avance qui viendrait.
        var reply = new byte[CryptoPrimitives.CompressedPointLength];
        CryptoPrimitives.Compress(_identity.PublicKey).CopyTo(reply.AsSpan());

        var replyRejection = await client.RegisterInvitationAsync(
            InvitationTicket.ReplySlot(nonce), reply, ct).ConfigureAwait(false);

        return replyRejection is null
            ? $"« {displayName} » ajouté. Comparez vos six mots avant de lui faire confiance."
            : $"« {displayName} » ajouté, mais la réponse n'a pas pu être déposée : {replyRejection}";
    }

    /// <summary>Relève les réponses à nos invitations en attente.</summary>
    public async Task<string> CollectRepliesAsync(CancellationToken ct)
    {
        if (_pending.Count == 0)
            return "aucune invitation en attente.";

        var added = new List<string>();
        var remaining = new List<PendingInvitation>();

        foreach (var invitation in _pending)
        {
            // Une invitation dépassée n'aboutira plus : le rendez-vous l'a déjà
            // oubliée au bout de vingt-quatre heures.
            if (DateTimeOffset.UtcNow - invitation.CreatedAt > TimeSpan.FromHours(24))
                continue;

            try
            {
                await using var client = new RendezvousClient();
                await client.ConnectAsync(_configuration.RendezvousHost, _configuration.RendezvousPort, ct)
                            .ConfigureAwait(false);

                var (payload, _) = await client.RedeemInvitationAsync(
                    InvitationTicket.ReplySlot(invitation.Nonce), ct).ConfigureAwait(false);

                if (payload is null || payload.Length != CryptoPrimitives.CompressedPointLength)
                {
                    remaining.Add(invitation);
                    continue;
                }

                var theirKey = CryptoPrimitives.Decompress(payload);
                var theirId = PeerId.Of(theirKey);

                if (_book.Find(theirId) is null)
                {
                    _book.Add(theirId, theirKey, invitation.Nonce, _identity.Id,
                              $"pair-{theirId.ToHex()[..6]}", Here());
                    added.Add(theirId.ToHex()[..6]);
                }
            }
            catch (Exception e)
            {
                _log.Warning(e, "Relève d'une réponse en échec.");
                remaining.Add(invitation);
            }
        }

        _pending.Clear();
        _pending.AddRange(remaining);
        _invitationStore.Save(_pending);
        _bookStore.Save(_book);

        return added.Count == 0
            ? $"aucune réponse. {remaining.Count} invitation(s) encore en attente."
            : $"{added.Count} pair(s) ajouté(s) : {string.Join(", ", added)}. Renommez-les avec « rename ».";
    }

    public string Rename(string from, string to)
    {
        var record = _book.All.FirstOrDefault(
            p => string.Equals(p.DisplayName, from, StringComparison.OrdinalIgnoreCase));

        if (record is null)
            return $"aucun pair nommé « {from} ».";

        _book.Load(_book.All.Select(p => p.Id == record.Id ? p with { DisplayName = to } : p).ToList());
        _bookStore.Save(_book);
        return $"« {from} » renommé en « {to} ».";
    }

    public string Remove(string displayName)
    {
        var record = _book.All.FirstOrDefault(
            p => string.Equals(p.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));

        if (record is null)
            return $"aucun pair nommé « {displayName} ».";

        _book.Remove(record.Id);
        _bookStore.Save(_book);
        return $"« {displayName} » retiré.";
    }

    /// <summary>
    /// S'annonce au rendez-vous pour chaque pair actif.
    /// </summary>
    /// <remarks>
    /// Une connexion par pair : les jetons d'une paire ne doivent pas arriver
    /// par la même session que ceux d'une autre, sans quoi le serveur pourrait
    /// relier entre elles les paires d'un même utilisateur, ce que les jetons
    /// tournants cherchent précisément à éviter.
    /// </remarks>
    public async Task<AnnounceReport> AnnounceAsync(CancellationToken ct)
    {
        var active = _book.Active.ToList();

        if (active.Count == 0)
            return new AnnounceReport(0, 0, 0, "aucun pair actif dans le carnet");

        var announced = 0;
        var matched = 0;
        string? failure = null;

        foreach (var pair in active)
        {
            try
            {
                await using var client = new RendezvousClient();
                var place = pair.Rendezvous[0];
                await client.ConnectAsync(place.Host, place.Port, ct).ConfigureAwait(false);

                var tickets = _tickets.Announce(pair.PairSecret);

                // Pas encore de candidats à offrir : cette étape ne vérifie que
                // l'annonce elle-même.
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));

                announced++;

                var partner = await client.AnnounceAndWaitAsync(new Announcement(tickets, []), deadline.Token)
                                          .ConfigureAwait(false);

                if (partner is not null)
                    matched++;
            }
            catch (OperationCanceledException)
            {
                // Délai écoulé : le pair n'est pas là, ce qui est le cas normal.
            }
            catch (Exception e)
            {
                failure ??= $"{pair.DisplayName} : {e.Message}";
                _log.Warning(e, $"Annonce en échec pour {pair.DisplayName}.");
            }
        }

        return new AnnounceReport(active.Count, announced, matched, failure);
    }

    public void Dispose() => _identity.Dispose();
}
