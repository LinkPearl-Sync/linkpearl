using System.Text;
using Linkpearl.Core.Cache;
using Xunit;

namespace Linkpearl.Core.Tests.Cache;

public sealed class SwitchableBlobStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "linkpearl-commutable-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeClock : Linkpearl.Core.Abstractions.IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    }

    private FileSystemBlobStore Disk() => new(_root, new CacheSettings(), new FakeClock(), _ => long.MaxValue);

    private static async Task<BlobCommitResult> PutAsync(IBlobStore store, byte[] content)
    {
        await using var writer = await store.BeginWriteAsync(BlobHash.OfContent(content), content.Length, default);
        await writer.WriteAsync(content, default);
        return await writer.CommitAsync(default);
    }

    [Fact]
    public async Task Sans_cache_attache_tout_est_refuse_et_rien_n_est_ecrit()
    {
        var store = new SwitchableBlobStore();
        var content = Encoding.UTF8.GetBytes("rien ne doit arriver sur le disque");

        Assert.False(store.IsAvailable);
        Assert.False((await PutAsync(store, content)).Accepted);
        Assert.False(store.TryGetSize(BlobHash.OfContent(content), out _));
        Assert.True(store.IsReadOnly);
        Assert.False(store.NeedsEviction);
        Assert.Equal(0, store.TotalBytes);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Un_cache_attache_recoit_les_ecritures()
    {
        var store = new SwitchableBlobStore();
        var disk = Disk();
        store.Attach(disk);

        var content = Encoding.UTF8.GetBytes("écrit à travers le commutateur");

        Assert.True((await PutAsync(store, content)).Accepted);
        Assert.True(disk.TryGetSize(BlobHash.OfContent(content), out _));
        Assert.Equal(disk.PathFor(BlobHash.OfContent(content)), store.PathFor(BlobHash.OfContent(content)));
    }

    [Fact]
    public async Task Un_cache_detache_ne_recoit_plus_rien()
    {
        var store = new SwitchableBlobStore();
        var disk = Disk();
        store.Attach(disk);

        Assert.Same(disk, store.Detach());
        Assert.Null(store.Detach());

        var content = Encoding.UTF8.GetBytes("après le détachement");

        Assert.False((await PutAsync(store, content)).Accepted);
        Assert.False(disk.TryGetSize(BlobHash.OfContent(content), out _));
    }

    [Fact]
    public async Task Sans_cache_une_lecture_echoue_comme_un_blob_absent()
    {
        var store = new SwitchableBlobStore();

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => store.OpenReadAsync(BlobHash.OfContent([1, 2, 3]), default));
    }
}
