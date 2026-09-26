namespace Linkpearl.Core.Sync;

/// <summary>Les clés publiques dont le plugin accepte la liste signée.</summary>
/// <remarks>
/// Vide tant que l'autorité n'a pas démarré en production : sans clé, aucune
/// liste n'est acceptée et tout passe par l'ancrage, comme avant le cercle
/// ouvert. Changer de clé exige une nouvelle version du plugin, et c'est voulu :
/// personne ne peut désigner une autre autorité à distance.
/// </remarks>
public static class ConsensusKeys
{
    public static readonly IReadOnlyList<byte[]> Trusted = [];
}
