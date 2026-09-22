using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Linkpearl.Core.Cache;

/// <summary>
/// Empreinte SHA-256 d'un contenu, qui sert aussi de nom de fichier dans le cache.
/// </summary>
/// <remarks>
/// SHA-256 et non SHA-1 comme le faisait Mare : dans un cache adressé par
/// contenu alimenté par un tiers, les collisions à préfixe choisi de SHA-1 sont
/// un vecteur réel.
///
/// Les 32 octets tiennent dans quatre entiers gros-boutiens plutôt que dans un
/// tableau, ce qui donne l'égalité et le code de hachage de structure sans
/// allocation, et rend impossible de comparer deux références au lieu de deux
/// valeurs.
/// </remarks>
public readonly record struct BlobHash
{
    public const int SizeInBytes = 32;
    public const int HexLength = SizeInBytes * 2;

    private readonly ulong _a;
    private readonly ulong _b;
    private readonly ulong _c;
    private readonly ulong _d;

    private BlobHash(ulong a, ulong b, ulong c, ulong d) => (_a, _b, _c, _d) = (a, b, c, d);

    public static BlobHash FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SizeInBytes)
            throw new ArgumentException($"un hash fait {SizeInBytes} octets, reçu {bytes.Length}", nameof(bytes));

        return new BlobHash(
            BinaryPrimitives.ReadUInt64BigEndian(bytes),
            BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt64BigEndian(bytes[16..]),
            BinaryPrimitives.ReadUInt64BigEndian(bytes[24..]));
    }

    public static BlobHash OfContent(ReadOnlySpan<byte> content)
    {
        Span<byte> digest = stackalloc byte[SizeInBytes];
        SHA256.HashData(content, digest);
        return FromBytes(digest);
    }

    /// <summary>Hache un flux sans le charger en mémoire.</summary>
    public static async ValueTask<BlobHash> OfStreamAsync(Stream stream, CancellationToken ct)
        => FromBytes(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));

    public bool TryWriteTo(Span<byte> destination)
    {
        if (destination.Length < SizeInBytes)
            return false;

        BinaryPrimitives.WriteUInt64BigEndian(destination, _a);
        BinaryPrimitives.WriteUInt64BigEndian(destination[8..], _b);
        BinaryPrimitives.WriteUInt64BigEndian(destination[16..], _c);
        BinaryPrimitives.WriteUInt64BigEndian(destination[24..], _d);
        return true;
    }

    public string ToHex()
    {
        Span<byte> bytes = stackalloc byte[SizeInBytes];
        TryWriteTo(bytes);
        return Convert.ToHexStringLower(bytes);
    }

    public static bool TryParseHex(string? hex, out BlobHash hash)
    {
        hash = default;

        if (hex is null || hex.Length != HexLength)
            return false;

        foreach (var c in hex)
        {
            if (IsHexDigit(c) is false)
                return false;
        }

        // Convert.FromHexString lève sur une entrée invalide, et cette entrée
        // vient du réseau : on valide avant plutôt que de rattraper après.
        hash = FromBytes(Convert.FromHexString(hex));
        return true;
    }

    private static bool IsHexDigit(char c)
        => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    /// <summary>Premier niveau de répertoire du cache, 256 valeurs.</summary>
    public string CacheLevel1 => ((byte)(_a >> 56)).ToString("x2");

    /// <summary>Second niveau de répertoire du cache, 256 valeurs.</summary>
    public string CacheLevel2 => ((byte)(_a >> 48)).ToString("x2");

    public override string ToString() => ToHex();
}
