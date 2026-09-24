using System.Net;
using System.Net.Sockets;
using System.Text;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Transport;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Harness;

/// <summary>
/// Éprouve le service de rendez-vous : appariement, échange de blocs scellés,
/// réflexion d'adresse et relais.
/// </summary>
/// <remarks>
/// Le secret de paire est ici une constante. Sa dérivation est déjà couverte
/// par les tests unitaires ; ce que l'on veut éprouver ici, c'est le serveur.
/// </remarks>
public static class RendezvousRun
{
    private static readonly byte[] PairSecretForTest =
        Enumerable.Range(0, 32).Select(i => (byte)(0x40 + i)).ToArray();

    public static async Task<bool> ExecuteAsync(string host, int port, string role, CancellationToken ct)
    {
        var tickets = new RendezvousTicket(new RealClock()).Announce(PairSecretForTest);
        var ok = true;

        Console.WriteLine($"Rôle « {role} », rendez-vous {host}:{port}.");
        Console.WriteLine($"Jetons annoncés : {Convert.ToHexStringLower(tickets[0])[..12]}… et "
                        + $"{Convert.ToHexStringLower(tickets[1])[..12]}…");

        // Réflexion d'adresse, sur la socket qui servirait au trou de NAT.
        using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        udp.Bind(new IPEndPoint(IPAddress.Any, 0));

        var server = new IPEndPoint((await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false))
            .First(a => a.AddressFamily is AddressFamily.InterNetwork), port);

        var observed = await RendezvousClient.ReflectAsync(udp, server, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);

        if (observed is null)
        {
            Console.WriteLine("ÉCHEC : pas de réflexion d'adresse.");
            ok = false;
        }
        else
        {
            Console.WriteLine($"Vu par le serveur comme {observed}, port local {((IPEndPoint)udp.LocalEndPoint!).Port}.");
        }

        // Le bloc de candidats est scellé : le serveur le transporte sans le lire.
        var candidateKey = new byte[32];
        PairSecretForTest.CopyTo(candidateKey, 0);
        var candidates = Encoding.UTF8.GetBytes($"candidats de {role} : {observed}");
        var sealedCandidates = CryptoPrimitives.Seal(candidateKey, new byte[12], candidates, "rv"u8);

        await using var client = new RendezvousClient();
        await client.ConnectAsync(host, port, ct).ConfigureAwait(false);

        Console.WriteLine("Annonce envoyée, attente d'un pair...");

        var partner = await client.AnnounceAndWaitAsync(new Announcement(tickets, sealedCandidates), ct)
                                  .ConfigureAwait(false);

        if (partner is null)
        {
            Console.WriteLine("ÉCHEC : aucun pair ne s'est présenté.");
            return false;
        }

        if (CryptoPrimitives.TryOpen(candidateKey, new byte[12], partner, "rv"u8, out var opened) is false)
        {
            Console.WriteLine("ÉCHEC : bloc de candidats illisible.");
            ok = false;
        }
        else
        {
            Console.WriteLine($"Apparié. Bloc du pair : « {Encoding.UTF8.GetString(opened)} »");
        }

        // Relais : le serveur met les deux sessions bout à bout, et le lien
        // relayé du plugin s'en sert tel quel. Un message d'un mégaoctet dans
        // chaque sens éprouve la fragmentation sous la limite de trame du
        // service, que le premier manifeste venu dépasse.
        var relay = new RendezvousClient();
        await relay.ConnectAsync(host, port, ct).ConfigureAwait(false);

        Console.WriteLine("Ouverture du relais...");

        // Le jeton que le plugin dérive, distinct de ceux de l'annonce.
        var relayTicket = Linkpearl.Core.Sync.PeerConnector.RelayTicketFor(PairSecretForTest, sealedCandidates, partner);

        if (await relay.OpenRelayAsync(relayTicket, ct).ConfigureAwait(false) is false)
        {
            Console.WriteLine("ÉCHEC : relais non ouvert.");
            await relay.DisposeAsync().ConfigureAwait(false);
            return false;
        }

        Console.WriteLine("Relais ouvert.");

        await using var link = new RelayPeerLink(new RendezvousRelayPipe(relay), new DnsEndPoint(host, port));

        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        link.Received += (_, payload) => received.TrySetResult(payload);

        var big = new byte[1024 * 1024];
        new Random(role.GetHashCode()).NextBytes(big);
        Encoding.UTF8.GetBytes($"bonjour de {role}").CopyTo(big, 0);

        var started = System.Diagnostics.Stopwatch.StartNew();
        await link.SendAsync(5, big, ct).ConfigureAwait(false);

        var theirs = await received.Task.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

        if (theirs.Length != big.Length)
        {
            Console.WriteLine($"ÉCHEC : reçu {theirs.Length} octets par le relais au lieu de {big.Length}.");
            ok = false;
        }
        else
        {
            Console.WriteLine($"Reçu par le relais : « {Encoding.UTF8.GetString(theirs, 0, 11)}… », "
                            + $"{theirs.Length} octets en {started.ElapsedMilliseconds} ms.");
        }

        // Laisser passer au moins une sonde avant de lire la latence.
        await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
        Console.WriteLine($"Aller-retour mesuré de bout en bout : {link.RoundTripMs} ms.");

        Console.WriteLine();
        Console.WriteLine(ok ? "TOUT EST PASSÉ." : "AU MOINS UNE ÉTAPE A ÉCHOUÉ.");
        return ok;
    }
}
