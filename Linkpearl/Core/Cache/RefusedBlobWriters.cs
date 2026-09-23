namespace Linkpearl.Core.Cache;

/// <summary>Écriture refusée d'avance, pour que l'appelant n'ait qu'un chemin à gérer.</summary>
/// <remarks>
/// Partagée entre le magasin sur disque et le magasin commutable : un refus
/// d'espace, de quota ou de cache absent se lit de la même façon au commit.
/// </remarks>
internal sealed class RefusedBlobWriter(string reason) : IBlobWriter
{
    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask<BlobCommitResult> CommitAsync(CancellationToken ct)
        => ValueTask.FromResult(BlobCommitResult.Refused(reason));

    public void Abort() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Assemblage refusé d'avance, pendant de <see cref="RefusedBlobWriter"/>.</summary>
internal sealed class RefusedBlobAssembly(string reason) : IBlobAssembly
{
    public ValueTask WriteAtAsync(long offset, ReadOnlyMemory<byte> data, CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask<BlobCommitResult> CommitAsync(CancellationToken ct)
        => ValueTask.FromResult(BlobCommitResult.Refused(reason));

    public void Abort() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
