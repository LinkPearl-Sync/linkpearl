using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Harness;

/// <summary>
/// Refait, contre un service réel, ce que le plugin fait pour se signaler.
/// </summary>
/// <remarks>
/// Deux clients, une boîte ouverte par le premier, une interrogation par le
/// second : c'est tout le chemin de la détection. Le faire ici sépare une
/// panne du service d'une panne du plugin, ce qu'aucune capture d'écran de
/// l'interface ne permet de trancher.
/// </remarks>
public static class PresenceRun
{
    public static async Task<bool> ExecuteAsync(string host, int port, CancellationToken ct)
    {
        var alice = PlayerFingerprint.Of("alice fictive", 73);
        var bob = PlayerFictive("bob fictif");
        var absent = PlayerFictive("personne ici");

        Console.WriteLine($"Service : {host}:{port}");
        Console.WriteLine($"Fenêtre : {MailboxAddress.IndexAt(DateTimeOffset.UtcNow)}");
        Console.WriteLine($"Boîte d'Alice (fenêtre courante) : {MailboxAddress.Of(alice, DateTimeOffset.UtcNow)}");
        Console.WriteLine();

        await using var first = new RendezvousClient();
        await first.ConnectAsync(host, port, ct).ConfigureAwait(false);
        Console.WriteLine("Alice est connectée.");

        var opened = MailboxAddress.Around(alice, DateTimeOffset.UtcNow).Select(a => a.ToBytes()).ToList();
        await first.OpenMailboxesAsync(opened, ct).ConfigureAwait(false);
        Console.WriteLine($"Alice a demandé l'ouverture de {opened.Count} boîte(s).");

        await using var second = new RendezvousClient();
        await second.ConnectAsync(host, port, ct).ConfigureAwait(false);
        Console.WriteLine("Bob est connecté.");

        var asked = new List<byte[]>
        {
            MailboxAddress.Of(alice, DateTimeOffset.UtcNow).ToBytes(),
            MailboxAddress.Of(bob, DateTimeOffset.UtcNow).ToBytes(),
            MailboxAddress.Of(absent, DateTimeOffset.UtcNow).ToBytes(),
        };

        var present = await second.QueryPresenceAsync(asked, ct).ConfigureAwait(false);

        if (present is null)
        {
            Console.WriteLine("ÉCHEC : le service n'a pas répondu à l'interrogation.");
            return false;
        }

        Console.WriteLine();
        Console.WriteLine($"Alice vue par Bob   : {present[0]}   (attendu : True)");
        Console.WriteLine($"Bob vu par Bob      : {present[1]}   (attendu : False, il n'a rien ouvert)");
        Console.WriteLine($"Un inconnu          : {present[2]}   (attendu : False)");

        var ok = present[0] && present[1] is false && present[2] is false;

        Console.WriteLine();
        Console.WriteLine(ok
            ? "Le service sait tenir une boîte et répondre : la détection tient de bout en bout."
            : "Le service ne rend pas ce qu'il devrait : la panne est là, et non dans le plugin.");

        return ok;
    }

    private static PlayerFingerprint PlayerFictive(string name) => PlayerFingerprint.Of(name, 73);
}
