using System.Buffers;
using System.Buffers.Binary;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Protocol;

namespace Linkpearl.Core.Transfer;

/// <summary>Une trame prête à partir, avant chiffrement.</summary>
public sealed record OutgoingFrame(byte Kind, byte[] Payload);

/// <summary>
/// Découpe un blob du cache en trames.
/// </summary>
/// <remarks>
/// La production est paresseuse, et c'est le point : l'appelant tire les trames
/// au rythme que lui laissent le limiteur de débit et la contre-pression du
/// transport. Produire d'un coup les trames de huit cents mégaoctets
/// allouerait huit cents mégaoctets dans le processus du jeu, ce qui est
/// exactement le défaut de la file non bornée de LiteNetLib.
/// </remarks>
public sealed class BlobSender(IBlobStore store, int blockSize)
{
    public async IAsyncEnumerable<OutgoingFrame> FramesFor(
        BlobHash hash,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (store.TryGetSize(hash, out var size) is false)
            throw new FileNotFoundException($"blob absent du cache : {hash}");

        var start = new byte[BlobHash.SizeInBytes + sizeof(long)];
        hash.TryWriteTo(start);
        BinaryPrimitives.WriteInt64BigEndian(start.AsSpan(BlobHash.SizeInBytes), size);
        yield return new OutgoingFrame(MessageKind.BlobStart, start);

        await using var stream = await store.OpenReadAsync(hash, ct).ConfigureAwait(false);

        var buffer = ArrayPool<byte>.Shared.Rent(blockSize);
        try
        {
            var index = 0u;

            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, blockSize), ct).ConfigureAwait(false);
                if (read == 0)
                    break;

                var chunk = new byte[sizeof(uint) + read];
                BinaryPrimitives.WriteUInt32BigEndian(chunk, index++);
                buffer.AsSpan(0, read).CopyTo(chunk.AsSpan(sizeof(uint)));

                yield return new OutgoingFrame(MessageKind.BlobChunk, chunk);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var end = new byte[BlobHash.SizeInBytes];
        hash.TryWriteTo(end);
        yield return new OutgoingFrame(MessageKind.BlobEnd, end);
    }
}
