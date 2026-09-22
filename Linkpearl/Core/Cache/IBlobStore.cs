namespace Linkpearl.Core.Cache;

/// <summary>Cache adressé par contenu.</summary>
public interface IBlobStore
{
    bool TryGetSize(BlobHash hash, out long size);

    Task<Stream> OpenReadAsync(BlobHash hash, CancellationToken ct);

    Task<IBlobWriter> BeginWriteAsync(BlobHash expected, long expectedSize, CancellationToken ct);

    /// <summary>Chemin du blob, tel qu'on le donne à Penumbra.</summary>
    string PathFor(BlobHash hash);

    long TotalBytes { get; }

    int Count { get; }

    bool IsReadOnly { get; }

    /// <summary>Rafraîchit la récence d'un blob qu'on vient de servir.</summary>
    void Touch(BlobHash hash);

    Task EvictToAsync(long targetBytes, IReadOnlySet<BlobHash> pinned, CancellationToken ct);
}

/// <summary>
/// Écriture d'un blob en cours.
/// </summary>
/// <remarks>
/// L'écriture se fait dans un fichier temporaire et n'est publiée qu'une fois
/// le hash vérifié : un blob visible est toujours un blob complet et conforme.
/// </remarks>
public interface IBlobWriter : IAsyncDisposable
{
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

    ValueTask<BlobCommitResult> CommitAsync(CancellationToken ct);

    void Abort();
}
