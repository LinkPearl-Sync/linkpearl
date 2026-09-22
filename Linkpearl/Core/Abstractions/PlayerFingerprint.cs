using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Linkpearl.Core.Abstractions;

/// <summary>
/// Empreinte d'un personnage, qui permet d'associer un objet visible à un pair.
/// </summary>
/// <remarks>
/// Le noyau ne manipule jamais un nom de personnage en clair : c'est
/// l'adaptateur Dalamud qui calcule cette empreinte, et cette frontière rend
/// structurellement impossible qu'un nom fuite dans une trame ou un journal.
///
/// Elle n'est révélée qu'à un pair déjà authentifié et déjà accepté, sur le
/// canal chiffré. Aucune publication vers des inconnus.
/// </remarks>
public readonly record struct PlayerFingerprint(ulong High, ulong Low)
{
    public const int SizeInBytes = 16;

    private static ReadOnlySpan<byte> Context => "linkpearl:ident:v1"u8;

    /// <summary>Calcule l'empreinte d'un nom et d'un monde. À n'appeler que dans l'adaptateur.</summary>
    public static PlayerFingerprint Of(string normalizedName, ushort worldId)
    {
        Span<byte> input = stackalloc byte[Context.Length + 256 + sizeof(ushort)];
        Context.CopyTo(input);

        var written = System.Text.Encoding.UTF8.GetBytes(normalizedName, input[Context.Length..]);
        BinaryPrimitives.WriteUInt16BigEndian(input[(Context.Length + written)..], worldId);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input[..(Context.Length + written + sizeof(ushort))], digest);

        return new PlayerFingerprint(
            BinaryPrimitives.ReadUInt64BigEndian(digest),
            BinaryPrimitives.ReadUInt64BigEndian(digest[8..]));
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[SizeInBytes];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, High);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), Low);
        return bytes;
    }

    public static PlayerFingerprint FromBytes(ReadOnlySpan<byte> bytes)
        => bytes.Length != SizeInBytes
            ? throw new ArgumentException($"une empreinte fait {SizeInBytes} octets", nameof(bytes))
            : new PlayerFingerprint(
                BinaryPrimitives.ReadUInt64BigEndian(bytes),
                BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]));

    public override string ToString() => $"{High:x16}{Low:x16}";
}
