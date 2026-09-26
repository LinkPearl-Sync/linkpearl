using System.Security.Cryptography;
using System.Text;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Sync;

/// <summary>
/// Les deux services du cercle ouvert où une paire se retrouve.
/// </summary>
/// <remarks>
/// Un hachage de rendez-vous : chaque service reçoit un score tiré du secret de
/// paire, et les deux meilleurs l'emportent. Quand la liste change, seules les
/// paires dont un service retenu est entré ou sorti changent de place.
///
/// Aucune rotation dans le temps. Chez Tor, les descripteurs tournent parce
/// qu'un attaquant peut calculer leur place et s'y poster. Ici la place dépend
/// du secret de paire, qu'il ignore : il ne capture que des paires prises au
/// hasard, et faire tourner n'exposerait chacune qu'à plus d'opérateurs.
/// </remarks>
public static class ServicePlacement
{
    public const int Chosen = 2;

    private static ReadOnlySpan<byte> PlaceInfo => "linkpearl:place:v1"u8;

    public static byte[] Score(ReadOnlySpan<byte> pairSecret, string canonicalAddress)
    {
        var address = Encoding.UTF8.GetBytes(canonicalAddress);
        var message = new byte[PlaceInfo.Length + pairSecret.Length + address.Length];

        PlaceInfo.CopyTo(message);
        pairSecret.CopyTo(message.AsSpan(PlaceInfo.Length));
        address.CopyTo(message.AsSpan(PlaceInfo.Length + pairSecret.Length));

        return SHA256.HashData(message);
    }

    public static IReadOnlyList<RendezvousAddress> Choose(ReadOnlySpan<byte> pairSecret, IReadOnlyList<ConsensusEntry> entries)
    {
        var scored = new List<(RendezvousAddress At, byte[] Family, byte[] Score)>(entries.Count);

        foreach (var entry in entries)
        {
            if (RendezvousAddress.TryParse(entry.Address, out var at, out _) is false)
                continue;

            scored.Add((at, entry.Family, Score(pairSecret, ServiceConsensus.Canonical(at))));
        }

        // Décroissant, octet par octet : les deux côtés d'une paire doivent
        // obtenir exactement le même ordre.
        scored.Sort((one, other) => other.Score.AsSpan().SequenceCompareTo(one.Score));

        var chosen = new List<RendezvousAddress>(Chosen);
        var families = new List<byte[]>(Chosen);

        foreach (var (at, family, _) in scored)
        {
            // Deux services d'un même sous-réseau tombent ensemble : les
            // retenir tous deux ne donnerait qu'une redondance de façade.
            if (families.Any(taken => taken.AsSpan().SequenceEqual(family)))
                continue;

            chosen.Add(at);
            families.Add(family);

            if (chosen.Count == Chosen)
                break;
        }

        return chosen;
    }
}
