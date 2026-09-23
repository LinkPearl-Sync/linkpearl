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
    private readonly PairBook _book;
    private readonly RendezvousTicket _tickets;
    private readonly List<PendingInvitation> _pending = [];
    private readonly Configuration _configuration;
    private readonly IPluginLog _log;

    private IdentityKeyPair? _identity;
    private PairBookStore? _bookStore;
    private PendingInvitationStore? _invitationStore;

    public PairingService(Configuration configuration, SystemClock clock, IPluginLog log)
    {
        _configuration = configuration;
        _log = log;
        _book = new PairBook(clock);
        _tickets = new RendezvousTicket(clock);
    }

    /// <summary>Ce qu'on répond tant qu'aucun personnage n'est connecté.</summary>
    private const string NoCharacter =
        "connectez-vous d'abord : l'identité Linkpearl appartient au personnage, pas à l'installation.";

    /// <summary>Vrai quand une identité de personnage est chargée.</summary>
    public bool IsBound => _identity is not null;

    /// <summary>
    /// Reprend l'identité et le carnet d'un personnage.
    /// </summary>
    /// <remarks>
    /// Par personnage et non par installation : deux personnages sur une même
    /// machine sont deux pairs distincts, que personne ne peut relier l'un à
    /// l'autre. C'est cohérent avec le reste, où la boîte aux lettres et la
    /// liste de bannissement portent déjà sur un personnage, et c'est ce qui
    /// permet à deux clients de la même machine de se pairer pour de vrai.
    /// </remarks>
    public void Bind(string root)
    {
        Unbind();

        _identity = IdentityKeyPair.LoadOrCreate(new DpapiIdentityStore(Path.Combine(root, "identity.key")));

        _bookStore = new PairBookStore(Path.Combine(root, "pairs.json"));
        _bookStore.Load(_book);

        _invitationStore = new PendingInvitationStore(Path.Combine(root, "invitations.json"));
        _pending.AddRange(_invitationStore.Load());
    }

    /// <summary>Repose tout à la déconnexion.</summary>
    /// <remarks>
    /// Le carnet est vidé, sans quoi le personnage suivant réécrirait dans son
    /// propre fichier les pairs de celui d'avant.
    /// </remarks>
    public void Unbind()
    {
        _identity?.Dispose();
        _identity = null;
        _bookStore = null;
        _invitationStore = null;

        _book.Clear();
        _pending.Clear();
    }

    /// <summary>Nos propres lieux de rendez-vous, ceux qui sont activés.</summary>
    /// <remarks>
    /// Vide quand l'utilisateur les a tous retirés : les appelants doivent le
    /// dire plutôt que de tomber sur un index hors bornes.
    /// </remarks>
    private IReadOnlyList<RendezvousAddress> Here() =>
        [.. _configuration.ActiveRendezvous.Select(entry => entry.Address)];

    /// <summary>Ce qu'on répond quand l'utilisateur n'a plus aucun service actif.</summary>
    /// <remarks>
    /// Le cas est atteignable en deux clics dans les réglages, et sans message
    /// il se manifesterait par un index hors bornes que personne ne saurait
    /// relier à la case qu'il vient de décocher.
    /// </remarks>
    private const string NoService =
        "aucun service de rendez-vous actif : ajoutez-en un dans les réglages.";

    public PeerId? Id => _identity?.Id;

    public IdentityKeyPair? Identity => _identity;

    /// <summary>Ajoute un pair depuis une demande acceptée dans l'interface.</summary>
    public string AddFromRequest(IncomingRequest request)
    {
        if (_identity is null || _bookStore is null)
            return NoCharacter;

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
        if (_identity is null || _invitationStore is null)
            return (null, NoCharacter);

        var ticket = InvitationTicket.Create();
        var nonce = RandomNumberGenerator.GetBytes(PairingCode.NonceLength);

        var payload = new byte[CryptoPrimitives.CompressedPointLength + PairingCode.NonceLength];
        CryptoPrimitives.Compress(_identity.PublicKey).CopyTo(payload.AsSpan());
        nonce.CopyTo(payload.AsSpan(CryptoPrimitives.CompressedPointLength));

        // Le lieu d'abord : c'est là que le ticket se dépose, et c'est lui que
        // le texte portera pour que l'autre sache où le retirer.
        if (Here() is not [var here, ..])
            return (null, NoService);

        await using var client = new RendezvousClient();
        await client.ConnectAsync(here.Host, here.Port, ct).ConfigureAwait(false);

        var rejection = await client.RegisterInvitationAsync(ticket.ToBytes(), payload, ct).ConfigureAwait(false);

        if (rejection is not null)
            return (null, rejection);

        _pending.Add(new PendingInvitation(ticket.Encode(), nonce, DateTimeOffset.UtcNow));
        _invitationStore.Save(_pending);

        return (InvitationTicketText.Encode(ticket, here), null);
    }

    /// <summary>Retire une invitation et ajoute son auteur au carnet.</summary>
    public async Task<string> RedeemAsync(string text, string displayName, CancellationToken ct)
    {
        if (_identity is null || _bookStore is null)
            return NoCharacter;

        if (InvitationTicketText.TryParse(text, out var ticket, out var at, out var why) is false)
            return $"ticket refusé : {why}";

        // Sans suffixe, le ticket vient d'avant la fédération : on retombe sur
        // notre propre service, qui est ce qu'il désignait implicitement.
        // Sans suffixe, le ticket vient d'avant la fédération : on retombe sur
        // notre premier service, qui est ce qu'il désignait implicitement.
        if (at is null && Here() is not [_, ..])
            return NoService;

        var where = at ?? Here()[0];

        await using var client = new RendezvousClient();
        await client.ConnectAsync(where.Host, where.Port, ct).ConfigureAwait(false);

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

        // Les deux lieux, celui du ticket d'abord puis le nôtre : c'est ce qui
        // fait survivre le pairage à la disparition de l'un des deux.
        var places = new List<RendezvousAddress> { where };

        foreach (var mine in Here())
            if (places.Contains(mine) is false)
                places.Add(mine);

        _book.Add(theirId, theirKey, nonce, _identity.Id, displayName, places);
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
        if (_identity is null || _bookStore is null || _invitationStore is null)
            return NoCharacter;

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

    /// <summary>Met un pair en pause, ou l'en sort.</summary>
    /// <remarks>
    /// En pause, le moteur ferme la session et retire ce qu'il avait posé :
    /// c'est ce qui fait de la pause un bouton dont on voit l'effet.
    /// </remarks>
    public string SetPaused(PeerId id, bool paused)
    {
        if (_bookStore is null)
            return NoCharacter;

        if (_book.Find(id) is not { } record)
            return "pair inconnu.";

        _book.SetPaused(id, paused);
        _bookStore.Save(_book);
        return paused ? $"{record.DisplayName} en pause." : $"{record.DisplayName} repris.";
    }

    /// <summary>Retire un pair du carnet.</summary>
    public string Remove(PeerId id)
    {
        if (_bookStore is null)
            return NoCharacter;

        if (_book.Find(id) is not { } record)
            return "pair inconnu.";

        _book.Remove(id);
        _bookStore.Save(_book);
        return $"{record.DisplayName} retiré du carnet.";
    }

    public string Rename(string from, string to)
    {
        if (_bookStore is null)
            return NoCharacter;

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
        if (_bookStore is null)
            return NoCharacter;

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
        if (_identity is null)
            return new AnnounceReport(0, 0, 0, NoCharacter);

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

    public void Dispose() => Unbind();
}
