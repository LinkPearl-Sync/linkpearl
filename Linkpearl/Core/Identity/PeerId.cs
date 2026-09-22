using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Linkpearl.Core.Identity;

/// <summary>
/// Identifiant d'un pair, dérivé de sa clé publique.
/// </summary>
/// <remarks>
/// Seize octets suffisent comme clé de carnet : ce n'est pas une preuve
/// d'identité, seulement un index. L'authenticité vient de la signature du
/// handshake, qui porte sur la clé complète.
/// </remarks>
public readonly record struct PeerId
{
    public const int SizeInBytes = 16;

    private readonly ulong _high;
    private readonly ulong _low;

    private PeerId(ulong high, ulong low) => (_high, _low) = (high, low);

    public static PeerId Of(ReadOnlySpan<byte> publicKey)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(publicKey, digest);

        return new PeerId(
            BinaryPrimitives.ReadUInt64BigEndian(digest),
            BinaryPrimitives.ReadUInt64BigEndian(digest[8..]));
    }

    public string ToHex()
    {
        Span<byte> bytes = stackalloc byte[SizeInBytes];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, _high);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], _low);
        return Convert.ToHexStringLower(bytes);
    }

    public override string ToString() => ToHex();
}
