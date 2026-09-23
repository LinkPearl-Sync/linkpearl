using System.Buffers.Binary;
using Linkpearl.Core.Cache;

namespace Linkpearl.Core.Transfer;

/// <summary>
/// Le découpage d'un blob en tronçons, et l'annonce qui en ouvre un.
/// </summary>
/// <remarks>
/// Un tronçon voyage entier sur un seul canal, parce que LiteNetLib ne garantit
/// l'ordre qu'à l'intérieur d'un canal. Mais un blob n'a plus à y tenir entier.
/// Mesuré au faux pair, à 20 ms de latence et seize canaux : les 364 premiers
/// mégaoctets d'une apparence réelle arrivaient en 18 s, puis un seul blob de
/// 85 Mo en prenait 20 à lui seul, sur un canal plafonné à 1,9 Mo/s par sa
/// fenêtre fiable.
///
/// Les tronçons sont alignés sur <see cref="SegmentSize"/> et ont une longueur
/// imposée : le receveur n'a rien à négocier, et deux tronçons ne peuvent pas
/// se chevaucher. Un pair qui en annonce un autrement est refusé.
///
/// Annonce : empreinte (32) | taille du blob (8) | position (8) | longueur (8).
/// </remarks>
public static class BlobSegments
{
    /// <summary>
    /// Quatre mébioctets : assez petit pour qu'un blob de 85 Mo occupe vingt
    /// canaux, assez grand pour que l'annonce et la clôture restent négligeables
    /// devant les deux cent cinquante blocs qu'elles encadrent.
    /// </summary>
    public const int SegmentSize = 4 * 1024 * 1024;

    public const int StartLength = BlobHash.SizeInBytes + (3 * sizeof(long));

    /// <summary>La longueur de l'ancienne annonce, sans position ni longueur.</summary>
    public const int LegacyStartLength = BlobHash.SizeInBytes + sizeof(long);

    /// <summary>Les tronçons d'un blob, dans l'ordre. Un blob vide en a un, vide.</summary>
    public static IReadOnlyList<(long Offset, long Length)> Of(long size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        if (size == 0)
            return [(0, 0)];

        var segments = new List<(long, long)>((int)((size + SegmentSize - 1) / SegmentSize));

        for (long offset = 0; offset < size; offset += SegmentSize)
            segments.Add((offset, Math.Min(SegmentSize, size - offset)));

        return segments;
    }

    /// <summary>Le nombre de tronçons d'un blob.</summary>
    public static int CountFor(long size) => size == 0 ? 1 : (int)((size + SegmentSize - 1) / SegmentSize);

    /// <summary>Vrai si ce tronçon est exactement l'un de ceux que <see cref="Of"/> produirait.</summary>
    public static bool IsValid(long size, long offset, long length)
        => size >= 0
        && offset >= 0
        && offset % SegmentSize == 0
        && (size == 0 ? offset == 0 && length == 0 : offset < size && length == Math.Min(SegmentSize, size - offset));

    public static void WriteStart(Span<byte> destination, BlobHash hash, long size, long offset, long length)
    {
        hash.TryWriteTo(destination);
        BinaryPrimitives.WriteInt64BigEndian(destination[BlobHash.SizeInBytes..], size);
        BinaryPrimitives.WriteInt64BigEndian(destination[(BlobHash.SizeInBytes + 8)..], offset);
        BinaryPrimitives.WriteInt64BigEndian(destination[(BlobHash.SizeInBytes + 16)..], length);
    }

    public static (BlobHash Hash, long Size, long Offset, long Length) ReadStart(ReadOnlySpan<byte> payload)
        => (BlobHash.FromBytes(payload[..BlobHash.SizeInBytes]),
            BinaryPrimitives.ReadInt64BigEndian(payload[BlobHash.SizeInBytes..]),
            BinaryPrimitives.ReadInt64BigEndian(payload[(BlobHash.SizeInBytes + 8)..]),
            BinaryPrimitives.ReadInt64BigEndian(payload[(BlobHash.SizeInBytes + 16)..]));
}
