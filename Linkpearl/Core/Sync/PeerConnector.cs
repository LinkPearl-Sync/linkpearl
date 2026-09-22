using System.Net;
using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Sync;

/// <summary>Où joindre le service de rendez-vous.</summary>
public sealed record RendezvousEndpoint(string Host, int Port);

/// <summary>Ce qu'une tentative de connexion a donné.</summary>
public sealed record ConnectionAttempt(IPeerLink? Link, bool PeerWasAbsent, string? Failure);

/// <summary>
/// Enchaîne ce qu'il faut pour joindre un pair : réflexion, annonce, perçage.
/// </summary>
/// <remarks>
/// Le rendez-vous n'intervient que pour échanger des adresses, et il ne les lit
/// même pas : le bloc de candidats est scellé sous une clé dérivée du secret de
/// paire. Il ne peut donc pas apprendre les adresses locales d'un réseau
/// domestique, seulement les adresses publiques qu'il voit de toute façon.
/// </remarks>
public sealed class PeerConnector(
    PeerLinkFactory links, RendezvousEndpoint rendezvous, IClock clock, ILogSink log) : IPeerDialer
{
    private static ReadOnlySpan<byte> CandidateKeyInfo => "linkpearl:candidates:v1"u8;
    private static ReadOnlySpan<byte> TokenInfo => "linkpearl:token:v1"u8;

    /// <summary>
    /// Le jeton qu'un pair doit présenter pour ouvrir une session avec nous.
    /// </summary>
    /// <remarks>
    /// Dérivé du secret de paire, donc inconnu de quiconque n'est pas ce pair.
    /// Il n'authentifie rien par lui-même, le handshake s'en charge : il évite
    /// seulement qu'un inconnu qui a trouvé notre adresse puisse ouvrir des
    /// sessions à volonté.
    /// </remarks>
    public static string TokenFor(ReadOnlySpan<byte> pairSecret)
    {
        var token = new byte[16];
        HKDF.Expand(HashAlgorithmName.SHA256, pairSecret, token, TokenInfo);
        return Convert.ToHexStringLower(token);
    }

    public async Task<ConnectionAttempt> ConnectAsync(PairRecord pair, CancellationToken ct)
    {
        if (pair.Rendezvous.Count == 0)
            return new ConnectionAttempt(null, false, "aucun lieu de rendez-vous enregistré pour ce pair");

        var candidates = await GatherCandidatesAsync(ct).ConfigureAwait(false);

        if (candidates.Count == 0)
            return new ConnectionAttempt(null, false, "aucune adresse à offrir");

        var key = CandidateKey(pair.PairSecret);
        var sealedCandidates = CryptoPrimitives.Seal(
            key, new byte[CryptoPrimitives.NonceLength], CandidateSet.Encode(candidates), CandidateKeyInfo);

        byte[]? theirs;

        try
        {
            await using var client = new RendezvousClient();
            // Le premier lieu seulement, pour l'instant : la tâche qui suit
            // remplace cet appel par une annonce parallèle sur tous.
            var place = pair.Rendezvous[0];
            await client.ConnectAsync(place.Host, place.Port, ct).ConfigureAwait(false);

            var tickets = new RendezvousTicket(clock).Announce(pair.PairSecret);

            // Le pair n'est peut-être pas en ligne : on n'attend pas longtemps,
            // et son absence n'est pas une erreur.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));

            theirs = await client.AnnounceAndWaitAsync(
                new Announcement(tickets, sealedCandidates), deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new ConnectionAttempt(null, true, null);
        }
        catch (Exception e)
        {
            return new ConnectionAttempt(null, false, $"rendez-vous injoignable : {e.Message}");
        }

        if (theirs is null)
            return new ConnectionAttempt(null, true, null);

        if (CryptoPrimitives.TryOpen(
                key, new byte[CryptoPrimitives.NonceLength], theirs, CandidateKeyInfo, out var plain) is false)
            return new ConnectionAttempt(null, false, "bloc de candidats illisible : secret de paire différent ?");

        if (CandidateSet.TryDecode(plain, out var theirCandidates, out var why) is false)
            return new ConnectionAttempt(null, false, $"candidats refusés : {why}");

        var ordered = CandidateSet.InPriorityOrder(theirCandidates);
        log.Info($"{pair.DisplayName} : {ordered.Count} adresse(s) à essayer.");

        var token = TokenFor(pair.PairSecret);
        links.Allow(token, pair.DisplayName);

        var link = await links.ConnectAsync(ordered, token, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);

        return link is null
            ? new ConnectionAttempt(null, false, "perçage sans réponse : NAT symétrique des deux côtés ?")
            : new ConnectionAttempt(link, false, null);
    }

    /// <summary>Nos adresses : celle que le rendez-vous voit, et nos adresses locales.</summary>
    private async Task<IReadOnlyList<IPEndPoint>> GatherCandidatesAsync(CancellationToken ct)
    {
        var candidates = new List<IPEndPoint>();

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(rendezvous.Host, ct).ConfigureAwait(false);

            foreach (var address in addresses.Take(2))
            {
                var reflected = await links.ReflectAsync(
                    new IPEndPoint(address, rendezvous.Port), TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);

                if (reflected is not null && candidates.Contains(reflected) is false)
                    candidates.Add(reflected);
            }
        }
        catch (Exception e)
        {
            log.Warning("Réflexion d'adresse en échec.", e);
        }

        // Les adresses locales servent quand les deux joueurs sont sous le même
        // toit, cas où l'adresse publique ne fonctionne pas toujours.
        candidates.AddRange(links.LocalCandidates());

        return candidates;
    }

    private static byte[] CandidateKey(ReadOnlySpan<byte> pairSecret)
    {
        var key = new byte[CryptoPrimitives.KeyLength];
        HKDF.Expand(HashAlgorithmName.SHA256, pairSecret, key, CandidateKeyInfo);
        return key;
    }
}
