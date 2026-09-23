namespace Linkpearl.Core.Cache;

/// <summary>
/// Ce que le gardien du cache lit et écrit dans la configuration du plugin.
/// </summary>
/// <remarks>
/// Une interface du noyau plutôt que la configuration elle-même, qui dépend de
/// Dalamud : c'est ce qui permet de tester le gardien sous Linux.
/// </remarks>
public interface ICacheConfiguration
{
    /// <summary>Dossier du cache. Vide pour le défaut sous LOCALAPPDATA.</summary>
    string CacheDirectory { get; set; }

    long CacheQuotaBytes { get; set; }

    /// <summary>La présentation a été fermée une fois : le cache peut exister.</summary>
    bool OnboardingSeen { get; set; }

    /// <summary>Le cache a déjà créé son dossier une fois : sa disparition est un incident.</summary>
    bool CacheEstablished { get; set; }

    /// <summary>L'ancien dossier après un changement, à proposer à la suppression. Vide sinon.</summary>
    string PreviousCacheDirectory { get; set; }

    void Save();
}
