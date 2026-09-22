using System.Buffers.Binary;
using System.Security.Cryptography;
using Linkpearl.Core.Crypto;

namespace Linkpearl.Core.Identity;

/// <summary>
/// Une invitation : la clé publique d'un pair, un aléa, et où le trouver.
/// </summary>
/// <remarks>
/// <b>Le code porte la clé publique complète.</b> C'est la propriété centrale du
/// système : le service de rendez-vous ne peut pas substituer une identité,
/// puisqu'il n'en fournit aucune. Au pire il refuse son service, ce qui produit
/// un échec de connexion et jamais une usurpation.
///
/// Le serveur est dans le code parce que sans lui, deux pairs configurés sur
/// deux rendez-vous différents ne se trouveraient jamais et que personne ne
/// comprendrait pourquoi.
///
/// L'aléa ne sert pas à l'authentification mais à dériver le secret de pairage,
/// dont sont tirés les tickets tournants du rendez-vous.
/// </remarks>
public sealed record PairingCode(byte[] PublicKey, byte[] PairingNonce, string RendezvousHost)
{
    public const byte Version = 1;
    public const int NonceLength = 16;

    private const string Prefix = "LP";
    private const int GroupSize = 6;
    private const int FlagsLength = 1;
    private const int ChecksumLength = 4;

    private static int BodyLength =>
        1 + CryptoPrimitives.CompressedPointLength + NonceLength + FlagsLength + ChecksumLength;

    public PeerId Id => PeerId.Of(PublicKey);

    public static PairingCode Create(byte[] publicKey, string rendezvousHost)
        => new(publicKey, RandomNumberGenerator.GetBytes(NonceLength), rendezvousHost);

    public string Encode()
    {
        var body = new byte[BodyLength];
        var offset = 0;

        body[offset++] = Version;
        CryptoPrimitives.Compress(PublicKey).CopyTo(body.AsSpan(offset));
        offset += CryptoPrimitives.CompressedPointLength;
        PairingNonce.CopyTo(body.AsSpan(offset));
        offset += NonceLength;
        body[offset++] = 0;   // drapeaux, réservés

        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(offset), Crc32C.Of(body.AsSpan(0, offset)));

        var encoded = Base32Crockford.Encode(body);
        var grouped = string.Join('-', Enumerable
            .Range(0, (encoded.Length + GroupSize - 1) / GroupSize)
            .Select(i => encoded.Substring(i * GroupSize, Math.Min(GroupSize, encoded.Length - (i * GroupSize)))));

        return $"{Prefix}{Version}-{grouped}@{RendezvousHost}";
    }

    public static bool TryParse(string? text, out PairingCode? code, out string? rejection)
    {
        code = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            rejection = "code vide";
            return false;
        }

        var trimmed = text.Trim();
        var at = trimmed.LastIndexOf('@');

        if (at < 0 || at == trimmed.Length - 1)
        {
            rejection = "code sans serveur de rendez-vous : il doit se terminer par « @hôte »";
            return false;
        }

        var host = trimmed[(at + 1)..];
        var head = trimmed[..at];

        if (head.Length < Prefix.Length + 2 || head.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) is false)
        {
            rejection = $"code ne commençant pas par « {Prefix} »";
            return false;
        }

        if (char.IsDigit(head[Prefix.Length]) is false || head[Prefix.Length] - '0' != Version)
        {
            rejection = $"version de code inconnue : ce plugin lit la version {Version}";
            return false;
        }

        if (Base32Crockford.TryDecode(head[(Prefix.Length + 1)..], out var body) is false)
        {
            rejection = "code contenant un caractère qui n'existe pas dans l'alphabet";
            return false;
        }

        if (body.Length < BodyLength)
        {
            rejection = $"code trop court ({body.Length} octets décodés, attendu {BodyLength})";
            return false;
        }

        // La base32 peut produire un octet de bourrage en fin : on ne lit que ce
        // que le format annonce.
        body = body[..BodyLength];

        var payloadLength = BodyLength - ChecksumLength;
        var expected = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(payloadLength));

        if (Crc32C.Of(body.AsSpan(0, payloadLength)) != expected)
        {
            rejection = "somme de contrôle incorrecte : le code a probablement été mal recopié";
            return false;
        }

        if (body[0] != Version)
        {
            rejection = $"version de code inconnue : ce plugin lit la version {Version}";
            return false;
        }

        byte[] publicKey;
        try
        {
            publicKey = CryptoPrimitives.Decompress(body.AsSpan(1, CryptoPrimitives.CompressedPointLength));
        }
        catch (CryptographicException e)
        {
            rejection = $"clé publique invalide : {e.Message}";
            return false;
        }

        var nonce = body.AsSpan(1 + CryptoPrimitives.CompressedPointLength, NonceLength).ToArray();

        code = new PairingCode(publicKey, nonce, host);
        rejection = null;
        return true;
    }
}
