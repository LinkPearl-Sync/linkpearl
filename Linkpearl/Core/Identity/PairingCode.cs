using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Linkpearl.Core.Identity;

/// <summary>
/// Une invitation : à qui l'on parle, avec quel aléa, et où le trouver.
/// </summary>
/// <remarks>
/// Le code porte l'<b>empreinte</b> de la clé publique, pas la clé elle-même.
/// Seize octets suffisent : substituer une identité demanderait de trouver une
/// autre clé ayant la même empreinte, soit deux puissance cent vingt-huit
/// essais. La clé complète arrive au handshake et s'y vérifie contre cette
/// empreinte. Le service de rendez-vous ne peut donc toujours pas usurper quoi
/// que ce soit, et le code passe de cent vingt caractères à moins de soixante-dix.
///
/// Le serveur est dans le code parce que sans lui, deux pairs configurés sur
/// deux rendez-vous différents ne se trouveraient jamais et que personne ne
/// comprendrait pourquoi.
///
/// L'aléa ne sert pas à l'authentification mais à dériver le secret de pairage,
/// dont sont tirés les jetons tournants du rendez-vous.
/// </remarks>
public sealed record PairingCode(PeerId Id, byte[] PairingNonce, string RendezvousHost)
{
    public const byte Version = 1;

    /// <summary>
    /// Quatre-vingt-seize bits d'aléa.
    /// </summary>
    /// <remarks>
    /// Il faut assez d'entropie pour qu'un observateur connaissant les deux
    /// empreintes ne puisse pas retrouver le secret de paire par recherche
    /// exhaustive, et donc suivre les présences sur le rendez-vous.
    /// </remarks>
    public const int NonceLength = 12;

    private const string Prefix = "LP";
    private const int ChecksumLength = 2;
    private const int BodyLength = PeerId.SizeInBytes + NonceLength + ChecksumLength;

    public static PairingCode Create(PeerId id, string rendezvousHost)
        => new(id, RandomNumberGenerator.GetBytes(NonceLength), rendezvousHost);

    public string Encode()
    {
        var body = new byte[BodyLength];
        Id.ToBytes().CopyTo(body.AsSpan());
        PairingNonce.CopyTo(body.AsSpan(PeerId.SizeInBytes));

        // Somme de contrôle sur deux octets : elle ne sert qu'à détecter une
        // faute de recopie avant toute opération réseau, pas une altération
        // volontaire. L'authenticité vient de la signature du handshake.
        var payload = PeerId.SizeInBytes + NonceLength;
        BinaryPrimitives.WriteUInt16BigEndian(
            body.AsSpan(payload), (ushort)(Crc32C.Of(body.AsSpan(0, payload)) & 0xFFFF));

        return $"{Prefix}{Version}-{Base32Crockford.Encode(body)}@{RendezvousHost}";
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

        var payloadLength = PeerId.SizeInBytes + NonceLength;
        var expected = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(payloadLength));

        if ((ushort)(Crc32C.Of(body.AsSpan(0, payloadLength)) & 0xFFFF) != expected)
        {
            rejection = "somme de contrôle incorrecte : le code a probablement été mal recopié";
            return false;
        }

        code = new PairingCode(
            PeerId.FromBytes(body.AsSpan(0, PeerId.SizeInBytes)),
            body.AsSpan(PeerId.SizeInBytes, NonceLength).ToArray(),
            host);

        rejection = null;
        return true;
    }
}
