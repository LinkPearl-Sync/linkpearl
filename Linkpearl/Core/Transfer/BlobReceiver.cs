using System.Buffers.Binary;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Protocol;
using Linkpearl.Core.Safety;

namespace Linkpearl.Core.Transfer;

/// <summary>Ce qu'une trame reçue a produit.</summary>
public sealed record ReceiveOutcome(
    bool Accepted, bool BlobCompleted = false, bool AlreadyPresent = false,
    BlobHash? Hash = null, string? Rejection = null)
{
    public static ReceiveOutcome Ok { get; } = new(true);

    public static ReceiveOutcome Refused(string reason) => new(false, Rejection: reason);
}

/// <summary>
/// Reçoit les blobs d'un pair.
/// </summary>
/// <remarks>
/// L'état est tenu <em>par canal</em>, et c'est une contrainte du transport et
/// non un choix : LiteNetLib ne garantit l'ordre qu'à l'intérieur d'un canal.
/// Un blob dont l'annonce, les blocs et la clôture voyageraient sur des canaux
/// différents verrait sa clôture arriver avant son dernier bloc. Un blob
/// entier tient donc sur un seul canal, et le parallélisme vient de plusieurs
/// blobs en vol sur des canaux différents.
///
/// C'est aussi l'endroit où atterrit tout ce qu'un pair envoie, donc l'endroit
/// où l'on dit non :
///
/// <list type="number">
/// <item>seul ce que notre plan a demandé est accepté : un pair ne décide pas de
/// ce que l'on stocke ;</item>
/// <item>un transfert à la fois par canal, et un nombre total plafonné, sinon un
/// pair ouvre mille écritures et épuise la mémoire et les descripteurs ;</item>
/// <item>tout ce qui dépasse la taille annoncée est refusé, sinon annoncer un
/// octet et en envoyer un gigaoctet remplirait le disque.</item>
/// </list>
/// </remarks>
public sealed class BlobReceiver(
    IBlobStore store, Quotas quotas, IReadOnlySet<BlobHash> requested, int maxConcurrent = 64)
    : IAsyncDisposable
{
    private sealed class Incoming
    {
        public required IBlobWriter Writer { get; init; }
        public required BlobHash Hash { get; init; }
        public required long Announced { get; init; }
        public long Received { get; set; }
    }

    private readonly Dictionary<byte, Incoming> _byChannel = [];

    public int ActiveTransfers => _byChannel.Count;

    public async ValueTask<ReceiveOutcome> HandleAsync(
        byte channel, byte kind, ReadOnlyMemory<byte> payload, CancellationToken ct)
        => kind switch
        {
            MessageKind.BlobStart => await StartAsync(channel, payload, ct).ConfigureAwait(false),
            MessageKind.BlobChunk => await ChunkAsync(channel, payload, ct).ConfigureAwait(false),
            MessageKind.BlobEnd => await EndAsync(channel, payload, ct).ConfigureAwait(false),
            _ => ReceiveOutcome.Refused($"type de message inattendu dans un transfert ({kind:X2})"),
        };

    private async ValueTask<ReceiveOutcome> StartAsync(byte channel, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (payload.Length != BlobHash.SizeInBytes + sizeof(long))
            return ReceiveOutcome.Refused("annonce de blob malformée");

        if (_byChannel.ContainsKey(channel))
            return ReceiveOutcome.Refused($"un transfert est déjà en cours sur le canal {channel}");

        if (_byChannel.Count >= maxConcurrent)
            return ReceiveOutcome.Refused($"trop de transferts simultanés (plafond {maxConcurrent})");

        var hash = BlobHash.FromBytes(payload.Span[..BlobHash.SizeInBytes]);
        var size = BinaryPrimitives.ReadInt64BigEndian(payload.Span[BlobHash.SizeInBytes..]);

        if (requested.Contains(hash) is false)
            return ReceiveOutcome.Refused($"blob non demandé : {hash}");

        if (size < 0 || size > quotas.MaxBlobBytes)
            return ReceiveOutcome.Refused($"taille hors plafond ({size}, plafond {quotas.MaxBlobBytes})");

        // Déjà en cache : il n'y a rien à écrire, et le dire évite au pair de
        // nous envoyer des mégaoctets pour rien.
        if (store.TryGetSize(hash, out _))
            return new ReceiveOutcome(true, AlreadyPresent: true, Hash: hash);

        _byChannel[channel] = new Incoming
        {
            Writer = await store.BeginWriteAsync(hash, size, ct).ConfigureAwait(false),
            Hash = hash,
            Announced = size,
        };

        return ReceiveOutcome.Ok;
    }

    private async ValueTask<ReceiveOutcome> ChunkAsync(byte channel, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (payload.Length < sizeof(uint))
            return ReceiveOutcome.Refused("bloc malformé");

        if (_byChannel.TryGetValue(channel, out var incoming) is false)
            return ReceiveOutcome.Refused($"bloc reçu sans annonce préalable sur le canal {channel}");

        var data = payload[sizeof(uint)..];

        if (incoming.Received + data.Length > incoming.Announced)
        {
            await DiscardAsync(channel).ConfigureAwait(false);
            return ReceiveOutcome.Refused(
                $"envoi au-delà de la taille annoncée ({incoming.Received + data.Length} pour {incoming.Announced})");
        }

        await incoming.Writer.WriteAsync(data, ct).ConfigureAwait(false);
        incoming.Received += data.Length;

        return ReceiveOutcome.Ok;
    }

    private async ValueTask<ReceiveOutcome> EndAsync(byte channel, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (payload.Length != BlobHash.SizeInBytes)
            return ReceiveOutcome.Refused("clôture de blob malformée");

        if (_byChannel.TryGetValue(channel, out var incoming) is false)
            return ReceiveOutcome.Refused($"clôture reçue sans annonce préalable sur le canal {channel}");

        var hash = BlobHash.FromBytes(payload.Span);

        if (hash != incoming.Hash)
        {
            await DiscardAsync(channel).ConfigureAwait(false);
            return ReceiveOutcome.Refused("clôture portant une autre empreinte que l'annonce");
        }

        _byChannel.Remove(channel);

        // C'est le cache qui vérifie l'empreinte, pendant l'écriture : un blob
        // n'est publié que s'il correspond à ce qui était annoncé.
        var result = await incoming.Writer.CommitAsync(ct).ConfigureAwait(false);
        await incoming.Writer.DisposeAsync().ConfigureAwait(false);

        return result.Accepted
            ? new ReceiveOutcome(true, BlobCompleted: true, Hash: hash)
            : ReceiveOutcome.Refused(result.Rejection!);
    }

    private async ValueTask DiscardAsync(byte channel)
    {
        if (_byChannel.Remove(channel, out var incoming) is false)
            return;

        incoming.Writer.Abort();
        await incoming.Writer.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var channel in _byChannel.Keys.ToList())
            await DiscardAsync(channel).ConfigureAwait(false);
    }
}
