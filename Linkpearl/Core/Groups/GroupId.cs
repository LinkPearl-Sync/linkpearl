using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Linkpearl.Core.Groups;

/// <summary>
/// Identifiant d'un groupe.
/// </summary>
/// <remarks>
/// Seize octets, comme <see cref="Identity.PeerId"/> : un index, pas une preuve.
/// Ordonnable parce que deux membres qui ont plusieurs groupes en commun doivent
/// choisir le même pour se joindre, sans se concerter : le plus petit.
/// </remarks>
public readonly record struct GroupId : IComparable<GroupId>
{
    public const int SizeInBytes = 16;

    private readonly ulong _high;
    private readonly ulong _low;

    private GroupId(ulong high, ulong low) => (_high, _low) = (high, low);

    /// <summary>Dérive l'identifiant de ce qui fonde le groupe : sa clé de signature, ou son secret.</summary>
    public static GroupId Of(ReadOnlySpan<byte> material)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(material, digest);

        return new GroupId(
            BinaryPrimitives.ReadUInt64BigEndian(digest),
            BinaryPrimitives.ReadUInt64BigEndian(digest[8..]));
    }

    public static GroupId FromBytes(ReadOnlySpan<byte> bytes)
        => bytes.Length != SizeInBytes
            ? throw new ArgumentException($"un identifiant de groupe fait {SizeInBytes} octets", nameof(bytes))
            : new GroupId(
                BinaryPrimitives.ReadUInt64BigEndian(bytes),
                BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]));

    public byte[] ToBytes()
    {
        var bytes = new byte[SizeInBytes];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, _high);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), _low);
        return bytes;
    }

    /// <summary>L'ordre des octets, lus en gros-boutiste.</summary>
    public int CompareTo(GroupId other)
    {
        var high = _high.CompareTo(other._high);
        return high != 0 ? high : _low.CompareTo(other._low);
    }

    public override string ToString() => $"{_high:x16}{_low:x16}";
}
