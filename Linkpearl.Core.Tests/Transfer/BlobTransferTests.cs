using System.Security.Cryptography;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Protocol;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Transfer;
using Xunit;

namespace Linkpearl.Core.Tests.Transfer;

/// <summary>
/// Le récepteur est l'endroit où atterrit tout ce qu'un pair envoie. Ces tests
/// portent surtout sur ce qu'il refuse.
/// </summary>
public sealed class BlobTransferTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "linkpearl-xfer-" + Guid.NewGuid().ToString("N"));

    private sealed class Clock : Linkpearl.Core.Abstractions.IClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    }

    private FileSystemBlobStore NewStore()
        => new(Path.Combine(_root, Guid.NewGuid().ToString("N")), new CacheSettings(), new Clock(), _ => long.MaxValue);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static byte[] Content(int size)
    {
        var content = new byte[size];
        RandomNumberGenerator.Fill(content);
        return content;
    }

    private async Task<FileSystemBlobStore> StoreWith(byte[] content)
    {
        var store = NewStore();
        var hash = BlobHash.OfContent(content);

        await using var writer = await store.BeginWriteAsync(hash, content.Length, default);
        await writer.WriteAsync(content, default);
        await writer.CommitAsync(default);

        return store;
    }

    [Fact]
    public async Task Un_blob_traverse_l_emetteur_et_le_recepteur_a_l_identique()
    {
        var content = Content(200_000);
        var hash = BlobHash.OfContent(content);

        var source = await StoreWith(content);
        var destination = NewStore();

        var sender = new BlobSender(source, blockSize: 16 * 1024);
        await using var receiver = new BlobReceiver(destination, Quotas.Default, new HashSet<BlobHash> { hash });

        await foreach (var frame in sender.FramesFor(hash, default))
        {
            var outcome = await receiver.HandleAsync(1, frame.Kind, frame.Payload, default);
            Assert.True(outcome.Accepted, outcome.Rejection);
        }

        Assert.True(destination.TryGetSize(hash, out var size));
        Assert.Equal(content.Length, size);

        await using var stream = await destination.OpenReadAsync(hash, default);
        Assert.Equal(hash, await BlobHash.OfStreamAsync(stream, default));
    }

    [Fact]
    public async Task Un_blob_vide_traverse_aussi()
    {
        var content = Array.Empty<byte>();
        var hash = BlobHash.OfContent(content);

        var source = await StoreWith(content);
        var destination = NewStore();

        var sender = new BlobSender(source, 16 * 1024);
        await using var receiver = new BlobReceiver(destination, Quotas.Default, new HashSet<BlobHash> { hash });

        await foreach (var frame in sender.FramesFor(hash, default))
            Assert.True((await receiver.HandleAsync(1, frame.Kind, frame.Payload, default)).Accepted);

        Assert.True(destination.TryGetSize(hash, out _));
    }

    [Fact]
    public async Task Un_blob_qu_on_n_a_pas_demande_est_refuse()
    {
        // Un pair ne décide pas de ce qu'on stocke : seul ce que le plan a
        // demandé est accepté.
        var content = Content(1000);
        var hash = BlobHash.OfContent(content);

        var source = await StoreWith(content);
        var destination = NewStore();

        var sender = new BlobSender(source, 16 * 1024);
        await using var receiver = new BlobReceiver(destination, Quotas.Default, new HashSet<BlobHash>());

        var first = await sender.FramesFor(hash, default).FirstAsync();
        var outcome = await receiver.HandleAsync(1, first.Kind, first.Payload, default);

        Assert.False(outcome.Accepted);
        Assert.Contains("demandé", outcome.Rejection!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_bloc_sans_annonce_prealable_est_refuse()
    {
        var destination = NewStore();
        await using var receiver = new BlobReceiver(destination, Quotas.Default, new HashSet<BlobHash>());

        var outcome = await receiver.HandleAsync(1, MessageKind.BlobChunk, new byte[100], default);

        Assert.False(outcome.Accepted);
        Assert.NotNull(outcome.Rejection);
    }

    [Fact]
    public async Task Un_blob_plus_gros_que_le_plafond_est_refuse_des_l_annonce()
    {
        var hash = BlobHash.OfContent("x"u8);
        var destination = NewStore();
        var quotas = Quotas.Default with { MaxBlobBytes = 1000 };

        await using var receiver = new BlobReceiver(destination, quotas, new HashSet<BlobHash> { hash });

        var payload = new byte[BlobHash.SizeInBytes + 8];
        hash.TryWriteTo(payload);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(BlobHash.SizeInBytes), 5000);

        var outcome = await receiver.HandleAsync(1, MessageKind.BlobStart, payload, default);

        Assert.False(outcome.Accepted);
        Assert.Contains("plafond", outcome.Rejection!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_envoi_qui_depasse_la_taille_annoncee_est_coupe()
    {
        // Sans ce contrôle, un pair pourrait annoncer un octet et en envoyer
        // autant qu'il veut, jusqu'à remplir le disque.
        var content = Content(50_000);
        var hash = BlobHash.OfContent(content);

        var source = await StoreWith(content);
        var destination = NewStore();

        var sender = new BlobSender(source, 16 * 1024);
        await using var receiver = new BlobReceiver(destination, Quotas.Default, new HashSet<BlobHash> { hash });

        var frames = new List<OutgoingFrame>();
        await foreach (var frame in sender.FramesFor(hash, default))
            frames.Add(frame);

        // On rejoue un bloc de données : le total dépasse alors l'annonce.
        var chunk = frames.First(f => f.Kind == MessageKind.BlobChunk);
        await receiver.HandleAsync(1, frames[0].Kind, frames[0].Payload, default);

        ReceiveOutcome? refus = null;
        for (var i = 0; i < 10 && refus is null; i++)
        {
            var outcome = await receiver.HandleAsync(1, chunk.Kind, chunk.Payload, default);
            if (outcome.Accepted is false)
                refus = outcome;
        }

        Assert.NotNull(refus);
        Assert.Contains("annoncée", refus!.Rejection!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_contenu_qui_ne_correspond_pas_a_l_empreinte_annoncee_est_refuse_a_la_cloture()
    {
        var annonce = BlobHash.OfContent("ce qui est annoncé"u8);
        var envoye = "ce qui est envoyé"u8.ToArray();

        var destination = NewStore();
        await using var receiver = new BlobReceiver(destination, Quotas.Default, new HashSet<BlobHash> { annonce });

        var start = new byte[BlobHash.SizeInBytes + 8];
        annonce.TryWriteTo(start);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(start.AsSpan(BlobHash.SizeInBytes), envoye.Length);

        Assert.True((await receiver.HandleAsync(1, MessageKind.BlobStart, start, default)).Accepted);

        var chunk = new byte[4 + envoye.Length];
        envoye.CopyTo(chunk.AsSpan(4));
        Assert.True((await receiver.HandleAsync(1, MessageKind.BlobChunk, chunk, default)).Accepted);

        var end = new byte[BlobHash.SizeInBytes];
        annonce.TryWriteTo(end);
        var outcome = await receiver.HandleAsync(1, MessageKind.BlobEnd, end, default);

        Assert.False(outcome.Accepted);
        Assert.False(destination.TryGetSize(annonce, out _));
    }

    [Fact]
    public async Task Une_annonce_pendant_un_transfert_en_cours_est_refusee()
    {
        // Un transfert entrant à la fois par pair : sinon un pair ouvre mille
        // écritures et fait exploser la mémoire et les descripteurs.
        var content = Content(50_000);
        var hash = BlobHash.OfContent(content);

        var source = await StoreWith(content);
        var destination = NewStore();

        var sender = new BlobSender(source, 16 * 1024);
        await using var receiver = new BlobReceiver(destination, Quotas.Default, new HashSet<BlobHash> { hash });

        var start = await sender.FramesFor(hash, default).FirstAsync();

        Assert.True((await receiver.HandleAsync(1, start.Kind, start.Payload, default)).Accepted);
        var outcome = await receiver.HandleAsync(1, start.Kind, start.Payload, default);

        Assert.False(outcome.Accepted);
        Assert.Contains("cours", outcome.Rejection!, StringComparison.OrdinalIgnoreCase);

        // Mais sur un autre canal, un second blob est parfaitement légitime :
        // c'est de là que vient tout le parallélisme du transfert.
        Assert.True((await receiver.HandleAsync(2, start.Kind, start.Payload, default)).Accepted);
    }

    [Theory]
    [InlineData(MessageKind.BlobStart, 10)]
    [InlineData(MessageKind.BlobChunk, 2)]
    [InlineData(MessageKind.BlobEnd, 5)]
    public async Task Un_message_tronque_est_refuse_sans_lever(byte kind, int length)
    {
        var destination = NewStore();
        await using var receiver = new BlobReceiver(destination, Quotas.Default, new HashSet<BlobHash>());

        var outcome = await receiver.HandleAsync(1, kind, new byte[length], default);

        Assert.False(outcome.Accepted);
        Assert.NotNull(outcome.Rejection);
    }

    [Fact]
    public async Task Un_type_de_message_inconnu_est_refuse()
    {
        var destination = NewStore();
        await using var receiver = new BlobReceiver(destination, Quotas.Default, new HashSet<BlobHash>());

        Assert.False((await receiver.HandleAsync(1, 0xFE, new byte[10], default)).Accepted);
    }

    [Fact]
    public async Task Un_blob_deja_en_cache_n_est_pas_reecrit()
    {
        var content = Content(5_000);
        var hash = BlobHash.OfContent(content);

        var source = await StoreWith(content);
        var destination = await StoreWith(content);

        var sender = new BlobSender(source, 16 * 1024);
        await using var receiver = new BlobReceiver(destination, Quotas.Default, new HashSet<BlobHash> { hash });

        var start = await sender.FramesFor(hash, default).FirstAsync();
        var outcome = await receiver.HandleAsync(1, start.Kind, start.Payload, default);

        Assert.True(outcome.Accepted);
        Assert.True(outcome.AlreadyPresent);
    }
}

internal static class AsyncEnumerableExtensions
{
    public static async Task<T> FirstAsync<T>(this IAsyncEnumerable<T> source)
    {
        await foreach (var item in source)
            return item;

        throw new InvalidOperationException("séquence vide");
    }
}
