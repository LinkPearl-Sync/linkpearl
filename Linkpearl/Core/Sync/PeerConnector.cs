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

/// <summary>Ce qui sait s'annoncer auprès d'un service de rendez-vous.</summary>
/// <remarks>
/// Abstrait pour que le choix du lieu se teste sans réseau. L'implémentation
/// réelle ouvre un <see cref="RendezvousClient"/> et attend l'appariement.
/// </remarks>
public interface IRendezvousDialer
{
    Task<byte[]?> AnnounceAsync(RendezvousAddress at, Announcement announcement, CancellationToken ct);
}

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

    /// <summary>
    /// S'annonce sur tous les lieux à la fois, et rend le premier appariement.
    /// </summary>
    /// <remarks>
    /// En parallèle et non l'un après l'autre : en séquence, deux personnes
    /// pourtant en ligne se manquent dès qu'elles essaient les services dans un
    /// ordre différent, l'une attendant sur le premier pendant que l'autre
    /// attend sur le second.
    ///
    /// Le lieu rendu est celui qui a apparié : c'est par lui que passera le
    /// relais si le direct échoue, puisque c'est le seul que les deux ont
    /// atteint.
    /// </remarks>
    public static async Task<(RendezvousAddress At, byte[] Theirs)?> AnnounceEverywhereAsync(
        IRendezvousDialer dialer, IReadOnlyList<RendezvousAddress> places,
        Announcement announcement, TimeSpan budget, CancellationToken ct)
    {
        if (places.Count == 0)
            return null;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(budget);

        var attempts = places.Select(async place =>
        {
            try
            {
                var theirs = await dialer.AnnounceAsync(place, announcement, deadline.Token).ConfigureAwait(false);

                return theirs is null ? null : ((RendezvousAddress At, byte[] Theirs)?)(place, theirs);
            }
            catch (Exception)
            {
                // Un service injoignable n'est pas une erreur : c'est
                // précisément ce à quoi sert d'en avoir plusieurs. Rattrapé ici
                // pour qu'aucune tâche ne se termine en faute, dont l'exception
                // resterait non observée après qu'une autre a gagné.
                return null;
            }
        }).ToList();

        while (attempts.Count > 0)
        {
            var finished = await Task.WhenAny(attempts).ConfigureAwait(false);
            attempts.Remove(finished);

            if (await finished.ConfigureAwait(false) is { } match)
            {
                // Le premier gagne : les autres n'ont plus lieu d'être, et leur
                // annulation ferme leurs connexions.
                await deadline.CancelAsync().ConfigureAwait(false);
                return match;
            }
        }

        return null;
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

        var announcement = new Announcement(
            new RendezvousTicket(clock).Announce(pair.PairSecret), sealedCandidates);

        // Le pair n'est peut-être pas en ligne : on n'attend pas longtemps, et
        // son absence n'est pas une erreur.
        var dialer = new LiveDialer();

        var match = await AnnounceEverywhereAsync(
            dialer, pair.Rendezvous, announcement, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

        if (match is null)
        {
            // Deux silences très différents. Si un service nous a reçus, le pair
            // n'était simplement pas là et il reviendra. Si aucun ne nous a
            // reçus, la réparation est d'ajouter un service ou de se repairer,
            // et dire « hors ligne » enverrait attendre pour rien.
            return dialer.Reached > 0
                ? new ConnectionAttempt(null, true, null)
                : new ConnectionAttempt(null, false, "aucun lieu de rendez-vous commun joignable");
        }

        if (CryptoPrimitives.TryOpen(
                key, new byte[CryptoPrimitives.NonceLength], match.Value.Theirs, CandidateKeyInfo, out var plain) is false)
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

    /// <summary>L'annonceur réel : une connexion par lieu, le temps d'attendre.</summary>
    /// <remarks>
    /// Il compte les services atteints, ce dont l'appelant a besoin pour
    /// distinguer un pair absent d'un réseau sans lieu commun. Une instance par
    /// tentative, donc le compteur n'a pas à se remettre à zéro.
    /// </remarks>
    private sealed class LiveDialer : IRendezvousDialer
    {
        private int _reached;

        public int Reached => Volatile.Read(ref _reached);

        public async Task<byte[]?> AnnounceAsync(
            RendezvousAddress at, Announcement announcement, CancellationToken ct)
        {
            await using var client = new RendezvousClient();
            await client.ConnectAsync(at.Host, at.Port, ct).ConfigureAwait(false);

            Interlocked.Increment(ref _reached);

            return await client.AnnounceAndWaitAsync(announcement, ct).ConfigureAwait(false);
        }
    }

    private static byte[] CandidateKey(ReadOnlySpan<byte> pairSecret)
    {
        var key = new byte[CryptoPrimitives.KeyLength];
        HKDF.Expand(HashAlgorithmName.SHA256, pairSecret, key, CandidateKeyInfo);
        return key;
    }
}
