namespace Linkpearl.Core.Sync;

/// <summary>Les clés publiques dont le plugin accepte la liste signée.</summary>
/// <remarks>
/// Changer de clé exige une nouvelle version du plugin, et c'est voulu :
/// personne ne peut désigner une autre autorité à distance. La clé privée vit
/// dans /var/lib/lprdv/directory.key sur le service, sauvegardée hors du VPS.
/// </remarks>
public static class ConsensusKeys
{
    public static readonly IReadOnlyList<byte[]> Trusted =
    [
        // Autorité de rdv.linkpearl.eorzea.events, engendrée le 26 septembre 2026.
        Convert.FromHexString(
            "04f3fbb81054e08f574092619a9b96f45ae73c419df531771871bf354148584f00"
            + "69d17361d5128aa32544eb474e84eaed0a444c8c2233efdf5e6ccf62f240cae8"),
    ];
}
