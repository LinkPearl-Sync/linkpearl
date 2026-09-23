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
        var alice = PlayerFictive("alice fictive");
        var bob = PlayerFictive("bob fictif");
        var absent = PlayerFictive("personne ici");

        Console.WriteLine($"Service : {host}:{port}");
        Console.WriteLine($"Fenêtre : {MailboxAddress.IndexAt(DateTimeOffset.UtcNow)}");
        Console.WriteLine($"Boîte d'Alice (fenêtre courante) : {MailboxAddress.Of(alice, DateTimeOffset.UtcNow)}");
        Console.WriteLine();

        await using var first = new RendezvousClient();
        await first.ConnectAsync(host, port, ct).ConfigureAwait(false);

        await using var second = new RendezvousClient();
        await second.ConnectAsync(host, port, ct).ConfigureAwait(false);

        Console.WriteLine("Les deux sont connectés, depuis la même adresse IP.");

        // Les deux ouvrent, comme le font deux clients de jeu sur une même
        // machine : c'est la situation qu'on cherche à reproduire, et non celle
        // d'un seul qui publie pendant que l'autre regarde.
        await first.OpenMailboxesAsync(Around(alice), ct).ConfigureAwait(false);
        await second.OpenMailboxesAsync(Around(bob), ct).ConfigureAwait(false);

        Console.WriteLine("Chacun a ouvert ses deux boîtes.");
        Console.WriteLine();

        var ok = true;

        for (var round = 1; round <= 3; round++)
        {
            var aliceSees = await AsksAsync(first, bob, ct).ConfigureAwait(false);
            var bobSees = await AsksAsync(second, alice, ct).ConfigureAwait(false);
            var ghost = await AsksAsync(first, absent, ct).ConfigureAwait(false);

            Console.WriteLine(
                $"ronde {round} : Alice voit Bob = {Show(aliceSees)}, "
                + $"Bob voit Alice = {Show(bobSees)}, inconnu = {Show(ghost)}");

            ok &= aliceSees is true && bobSees is true && ghost is false;

            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }

        Console.WriteLine();
        Console.WriteLine(ok
            ? "Deux clients d'une même IP se voient sans difficulté : le service n'y est pour rien."
            : "Le service ne rend pas ce qu'il devrait : la panne est là.");

        // Ce que fait le plugin, et que les rondes ci-dessus ne faisaient pas :
        // un écouteur tourne sur la connexion pendant qu'on l'interroge. Sans
        // répartition des trames, l'écouteur avale la réponse et l'interrogation
        // attend pour toujours.
        Console.WriteLine();
        Console.WriteLine("Et maintenant avec un écouteur sur la même connexion, comme le plugin :");

        using var listening = new CancellationTokenSource();
        var listener = Task.Run(() => first.ListenAsync(listening.Token), listening.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);

        using var patience = CancellationTokenSource.CreateLinkedTokenSource(ct);
        patience.CancelAfter(TimeSpan.FromSeconds(10));

        bool? underListener;

        try
        {
            underListener = await AsksAsync(first, bob, patience.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            underListener = null;
        }

        Console.WriteLine($"  Alice voit Bob = {Show(underListener)}   (attendu : oui)");

        await listening.CancelAsync().ConfigureAwait(false);

        try
        {
            await listener.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        if (underListener is not true)
        {
            Console.WriteLine("  L'écouteur vole la réponse : c'est la panne du plugin.");
            return false;
        }

        Console.WriteLine("  La réponse revient à qui l'a demandée.");

        return ok;
    }

    private static string Show(bool? answer) => answer switch
    {
        true => "oui",
        false => "non",
        _ => "PAS DE RÉPONSE",
    };

    private static async Task<bool?> AsksAsync(RendezvousClient client, PlayerFingerprint about, CancellationToken ct)
    {
        var present = await client
            .QueryPresenceAsync([MailboxAddress.Of(about, DateTimeOffset.UtcNow).ToBytes()], ct)
            .ConfigureAwait(false);

        return present is null ? null : present[0];
    }

    private static List<byte[]> Around(PlayerFingerprint fingerprint)
        => [.. MailboxAddress.Around(fingerprint, DateTimeOffset.UtcNow).Select(address => address.ToBytes())];

    private static PlayerFingerprint PlayerFictive(string name) => PlayerFingerprint.Of(name, 73);
}
