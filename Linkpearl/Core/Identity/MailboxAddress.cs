using System.Buffers.Binary;
using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;

namespace Linkpearl.Core.Identity;

/// <summary>
/// L'adresse de la boîte aux lettres d'un personnage.
/// </summary>
/// <remarks>
/// Elle dérive du nom et du monde, donc quiconque voit le personnage peut la
/// calculer. C'est délibéré : c'est ce qui permet à un inconnu de reconnaître
/// quelqu'un qui utilise le plugin, et de lui adresser une demande.
///
/// <b>Le prix est que l'opérateur du rendez-vous peut savoir qui est en
/// ligne</b>, l'espace des noms de FFXIV étant énumérable. Être découvrable par
/// un inconnu implique de l'être par le serveur ; les deux ne se séparent pas.
/// La fenêtre tournante empêche seulement de relier deux périodes entre elles.
///
/// Le noyau ne voit jamais le nom en clair : l'adaptateur Dalamud lui passe
/// l'empreinte déjà calculée.
/// </remarks>
public readonly record struct MailboxAddress
{
    public const int SizeInBytes = 6;

    /// <summary>Durée d'une fenêtre. Assez courte pour tourner, assez longue pour se croiser.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    private static ReadOnlySpan<byte> Context => "linkpearl:mbox:v1"u8;

    private readonly ulong _value;

    private MailboxAddress(ulong value) => _value = value;

    /// <summary>La fenêtre en cours à cet instant.</summary>
    /// <remarks>
    /// Publique parce que celui qui tient une boîte ouverte doit savoir quand
    /// son adresse a cessé d'être celle que les autres calculent.
    /// </remarks>
    public static long IndexAt(DateTimeOffset now) => now.ToUnixTimeSeconds() / (long)Window.TotalSeconds;

    public static MailboxAddress Of(PlayerFingerprint fingerprint, DateTimeOffset now, int windowOffset = 0)
    {
        var index = IndexAt(now) + windowOffset;

        Span<byte> input = stackalloc byte[Context.Length + PlayerFingerprint.SizeInBytes + sizeof(long)];
        Context.CopyTo(input);
        fingerprint.ToBytes().CopyTo(input[Context.Length..]);
        BinaryPrimitives.WriteInt64BigEndian(input[(Context.Length + PlayerFingerprint.SizeInBytes)..], index);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);

        Span<byte> padded = stackalloc byte[8];
        digest[..SizeInBytes].CopyTo(padded[2..]);

        return new MailboxAddress(BinaryPrimitives.ReadUInt64BigEndian(padded));
    }

    /// <summary>
    /// Les adresses à surveiller : la fenêtre courante et la suivante.
    /// </summary>
    /// <remarks>
    /// Sans la suivante, deux joueurs de part et d'autre d'une bascule ne se
    /// verraient pas, et l'un des deux attendrait sans comprendre.
    /// </remarks>
    public static IReadOnlyList<MailboxAddress> Around(PlayerFingerprint fingerprint, DateTimeOffset now)
        => [Of(fingerprint, now), Of(fingerprint, now, 1)];

    public byte[] ToBytes()
    {
        Span<byte> full = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(full, _value);
        return full[2..].ToArray();
    }

    public static MailboxAddress FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SizeInBytes)
            throw new ArgumentException($"une adresse fait {SizeInBytes} octets", nameof(bytes));

        Span<byte> full = stackalloc byte[8];
        bytes.CopyTo(full[2..]);
        return new MailboxAddress(BinaryPrimitives.ReadUInt64BigEndian(full));
    }

    public override string ToString() => Convert.ToHexStringLower(ToBytes());
}
