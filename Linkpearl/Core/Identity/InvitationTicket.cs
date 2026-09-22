using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Linkpearl.Core.Identity;

/// <summary>
/// Un ticket d'invitation : douze caractères, à usage unique.
/// </summary>
/// <remarks>
/// <b>Ce ticket ne porte aucune identité.</b> Douze caractères de base32 font
/// soixante bits, là où une empreinte de clé en demande cent vingt-huit pour
/// être infalsifiable. Ce n'est donc qu'un numéro que le service de rendez-vous
/// échange contre une clé publique.
///
/// La conséquence est à énoncer clairement : <b>au premier contact, c'est le
/// rendez-vous qui dit à qui l'on parle, et il peut mentir.</b> C'est le
/// compromis retenu par ZRTP et par la vérification d'appareil de Matrix. Il se
/// rattrape en comparant de vive voix les six mots que le handshake produit des
/// deux côtés : s'ils correspondent, personne ne s'est intercalé, et la clé est
/// épinglée définitivement. Une fois épinglée, le rendez-vous n'a plus aucun
/// pouvoir.
///
/// Quarante-huit bits d'aléa pour un jeton à usage unique, valable vingt-quatre
/// heures et derrière un plafond d'essais : deviner en demanderait des milliers
/// de milliards.
/// </remarks>
public readonly record struct InvitationTicket
{
    public const int Length = 12;
    public const int SizeInBytes = 6;

    private const int SecretBits = 48;
    private const int ChecksumBits = 12;

    private readonly ulong _value;   // 48 bits d'aléa, poids faibles

    private InvitationTicket(ulong value) => _value = value;

    public static InvitationTicket Create()
    {
        Span<byte> random = stackalloc byte[8];
        RandomNumberGenerator.Fill(random);

        return new InvitationTicket(BinaryPrimitives.ReadUInt64BigEndian(random) & ((1UL << SecretBits) - 1));
    }

    /// <summary>Les six octets sous lesquels le rendez-vous indexe le ticket.</summary>
    public byte[] ToBytes()
    {
        Span<byte> full = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(full, _value);
        return full[2..].ToArray();
    }

    public static InvitationTicket FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SizeInBytes)
            throw new ArgumentException($"un ticket fait {SizeInBytes} octets", nameof(bytes));

        Span<byte> full = stackalloc byte[8];
        bytes.CopyTo(full[2..]);
        return new InvitationTicket(BinaryPrimitives.ReadUInt64BigEndian(full));
    }

    public string Encode()
    {
        // Douze caractères : quarante-huit bits d'aléa, puis douze bits de
        // contrôle. La somme distingue « vous avez mal recopié » de « ce ticket
        // n'existe plus », deux messages d'erreur très différents à lire.
        return Base32Crockford.EncodeBits((_value << ChecksumBits) | Checksum(_value), Length);
    }

    public static bool TryParse(string? text, out InvitationTicket ticket, out string? rejection)
    {
        ticket = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            rejection = "ticket vide";
            return false;
        }

        var cleaned = text.Trim().Replace("-", "").Replace(" ", "");

        if (cleaned.Length != Length)
        {
            rejection = $"un ticket fait {Length} caractères, celui-ci en a {cleaned.Length}";
            return false;
        }

        if (Base32Crockford.TryDecodeBits(cleaned, Length, out var combined) is false)
        {
            rejection = "ticket contenant un caractère qui n'existe pas dans l'alphabet";
            return false;
        }

        var value = combined >> ChecksumBits;

        if ((combined & ((1UL << ChecksumBits) - 1)) != Checksum(value))
        {
            rejection = "ticket mal recopié : la somme de contrôle ne correspond pas";
            return false;
        }

        ticket = new InvitationTicket(value);
        rejection = null;
        return true;
    }

    private static ulong Checksum(ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return Crc32C.Of(bytes) & ((1UL << ChecksumBits) - 1);
    }

    /// <summary>
    /// La case où celui qui retire l'invitation dépose sa propre identité.
    /// </summary>
    /// <remarks>
    /// Celui qui invite ne sait pas d'avance qui viendra, donc il ne peut pas
    /// dériver le secret de paire au moment de l'invitation. Il lui faut la
    /// clé de l'autre, qui la dépose ici. Dérivée de l'aléa et non du ticket
    /// lui-même, pour que connaître le ticket ne suffise pas à deviner la case
    /// de réponse.
    /// </remarks>
    public static byte[] ReplySlot(ReadOnlySpan<byte> pairingNonce)
    {
        Span<byte> digest = stackalloc byte[32];
        Span<byte> input = stackalloc byte[pairingNonce.Length + 5];
        pairingNonce.CopyTo(input);
        "reply"u8.CopyTo(input[pairingNonce.Length..]);

        SHA256.HashData(input, digest);
        return digest[..SizeInBytes].ToArray();
    }

    public override string ToString() => Encode();
}
