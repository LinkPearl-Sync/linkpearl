using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;

namespace Linkpearl.Core.Sync;

/// <summary>Ce que dit le glyphe posé à droite du nom d'un joueur.</summary>
/// <remarks>
/// Local seulement : rien de ceci ne traverse le réseau, d'où l'énumération.
/// </remarks>
public enum NameplateMark
{
    /// <summary>Pas de glyphe : joueur sans Linkpearl, ou pair bloqué.</summary>
    None,

    /// <summary>Utilise Linkpearl et se laisse trouver, pas encore pairé.</summary>
    Available,

    /// <summary>Nous a envoyé une demande de pairage qui attend notre réponse.</summary>
    Requesting,

    /// <summary>Pairé, lien établi.</summary>
    Online,

    /// <summary>Pairé, mais pas de lien : hors ligne, en pause, ou connexion en cours.</summary>
    Offline,

    /// <summary>Pairé, et quelque chose a échoué : la fenêtre en dit plus.</summary>
    Trouble,
}

/// <summary>
/// Calcule le glyphe de chaque joueur, à partir de ce que le carnet, le moteur
/// et la présence savent déjà.
/// </summary>
/// <remarks>
/// Même lecture que la puce d'état du carnet, pour que la plaque de nom et la
/// fenêtre ne se contredisent jamais.
/// </remarks>
public static class NameplateMarks
{
    public static IReadOnlyDictionary<PlayerFingerprint, NameplateMark> Build(
        IReadOnlyList<PairRecord> pairs,
        IReadOnlyList<PeerStatus> statuses,
        IReadOnlyCollection<PlayerFingerprint> detected,
        IReadOnlyCollection<PlayerFingerprint> requesting)
    {
        var marks = new Dictionary<PlayerFingerprint, NameplateMark>();

        foreach (var print in detected)
            marks[print] = NameplateMark.Available;

        // Après la détection : une demande attend une action de notre part, ce
        // qu'aucun autre état non pairé ne fait.
        foreach (var print in requesting)
            marks[print] = NameplateMark.Requesting;

        var byPeer = statuses.ToDictionary(status => status.Peer);

        // En dernier : ce que le carnet sait l'emporte sur ce que la présence devine.
        foreach (var pair in pairs)
        {
            if (pair.Trust is not (PairTrust.Accepted or PairTrust.Blocked))
                continue;

            var status = byPeer.GetValueOrDefault(pair.Id);

            // L'empreinte annoncée par le pair, ou à défaut celle épinglée à la
            // première rencontre : avant le premier échange, seule la seconde existe.
            if ((status?.View.Fingerprint ?? pair.PinnedFingerprint) is not { } print)
                continue;

            marks[print] = Of(pair, status);
        }

        return marks;
    }

    private static NameplateMark Of(PairRecord pair, PeerStatus? status)
    {
        if (pair.Trust is PairTrust.Blocked)
            return NameplateMark.None;

        if (pair.Paused)
            return NameplateMark.Offline;

        // Le moteur n'en garde une que si le pair était là pour la provoquer :
        // un pair simplement absent ne vire pas au rouge.
        if (status is { FingerprintDisputed: true } or { LastFailure: not null })
            return NameplateMark.Trouble;

        return status?.State is PeerSessionState.Connected or PeerSessionState.Applied
            ? NameplateMark.Online
            : NameplateMark.Offline;
    }
}
