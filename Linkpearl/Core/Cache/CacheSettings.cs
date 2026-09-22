namespace Linkpearl.Core.Cache;

/// <summary>Réglages du cache, exposés à l'utilisateur.</summary>
/// <remarks>
/// Le répertoire et le quota sont des réglages attendus : le cache pèse des
/// dizaines de gigaoctets et beaucoup de gens le mettront sur un disque
/// secondaire.
/// </remarks>
public sealed record CacheSettings
{
    /// <summary>Taille maximale du cache.</summary>
    public long QuotaBytes { get; init; } = 20L * 1024 * 1024 * 1024;

    /// <summary>
    /// Part du quota visée après une éviction.
    /// </summary>
    /// <remarks>
    /// Évincer jusqu'au quota exact ferait repartir une éviction au blob
    /// suivant. On descend plus bas pour ne le faire que rarement.
    /// </remarks>
    public double LowWatermark { get; init; } = 0.85;

    /// <summary>
    /// Espace libre sous lequel le cache passe en lecture seule.
    /// </summary>
    /// <remarks>
    /// Mieux vaut cesser de synchroniser que remplir le disque de
    /// l'utilisateur, qui ne saurait pas d'où vient le problème.
    /// </remarks>
    public long MinimumFreeBytes { get; init; } = 2L * 1024 * 1024 * 1024;
}

/// <summary>Résultat d'une écriture de blob.</summary>
public sealed record BlobCommitResult(bool Accepted, string? Rejection)
{
    public static BlobCommitResult Ok { get; } = new(true, null);

    public static BlobCommitResult Refused(string reason) => new(false, reason);
}
