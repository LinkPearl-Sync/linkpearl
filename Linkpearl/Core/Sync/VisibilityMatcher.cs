using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;

namespace Linkpearl.Core.Sync;

/// <summary>Un pair reconnu devant nous.</summary>
public sealed record MatchedPeer(PeerId Peer, GameObjectRef Object);

/// <summary>
/// Associe un objet visible du jeu au pair qui lui correspond.
/// </summary>
/// <remarks>
/// L'empreinte n'est révélée qu'à un pair déjà authentifié et déjà accepté, sur
/// le canal chiffré. On ne publie donc jamais d'empreinte vers des inconnus,
/// contrairement à un schéma où chacun annoncerait la sienne au serveur.
///
/// Si deux pairs annoncent la même empreinte, aucun des deux n'est retenu : l'un
/// des deux ment, et appliquer au hasard reviendrait à faire porter à quelqu'un
/// l'apparence choisie par un tiers.
/// </remarks>
public static class VisibilityMatcher
{
    public static IReadOnlyList<MatchedPeer> Match(
        IReadOnlyList<VisiblePlayer> visible,
        IReadOnlyDictionary<PeerId, PlayerFingerprint> announced)
    {
        var claimants = new Dictionary<PlayerFingerprint, List<PeerId>>();

        foreach (var (peer, fingerprint) in announced)
        {
            if (claimants.TryGetValue(fingerprint, out var list) is false)
                claimants[fingerprint] = list = [];

            list.Add(peer);
        }

        var matches = new List<MatchedPeer>();

        foreach (var player in visible)
        {
            if (claimants.TryGetValue(player.Fingerprint, out var list) is false)
                continue;

            if (list.Count != 1)
                continue;   // revendication contestée : on n'applique rien

            matches.Add(new MatchedPeer(list[0], player.Object));
        }

        return matches;
    }
}
