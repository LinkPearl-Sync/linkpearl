using System.Buffers.Binary;

namespace Linkpearl.Core.Safety;

/// <summary>
/// Refuse un fichier transitoire mal formé avant qu'il n'atteigne le jeu.
/// </summary>
/// <remarks>
/// Un <c>.pap</c> ou un <c>.avfx</c> est lu par du code natif du client, écrit
/// sans l'hypothèse qu'un pair contrôle le fichier, et les clients comparables
/// ne vérifient rien chez le receveur. Ce contrôle ne lit que des octets bornés,
/// en code managé : il ne prouve pas qu'une animation est jouable, il écarte ce
/// qui n'a même pas la forme d'un fichier du jeu.
///
/// Règles mesurées le 23 septembre 2026 sur les mods de l'utilisateur :
/// 5 109 <c>.pap</c> dont 5 108 passent (le refusé est un fichier Havok brut
/// nommé <c>.pap</c>), 205 <c>.tmb</c> tous marqués <c>TMLB</c>, 1 133
/// <c>.avfx</c> tous marqués <c>XFVA</c>, 576 <c>.scd</c> tous marqués
/// <c>SEDBSSCF</c>. Le type d'un <c>.pap</c> n'est pas contraint : 40 fichiers
/// légitimes (armes, monstres) ne sont pas de type humain.
/// </remarks>
public static class TransientFileCheck
{
    private const int PapHeaderLength = 26;
    private const uint PapVersion = 0x00020001;
    private const int PapInfoEntryLength = 40;
    private const int MaxPapAnimations = 1024;

    private static ReadOnlySpan<byte> PapMagic => "pap "u8;
    private static ReadOnlySpan<byte> HavokTagfile => [0x1E, 0x0D, 0xB0, 0xCA, 0xCE, 0xFA, 0x11, 0xD0];
    private static ReadOnlySpan<byte> HavokPackfile => [0x57, 0xE0, 0xE0, 0x57, 0x10, 0xC0, 0xC0, 0x10];

    public static bool IsWellFormed(string gamePath, Stream content, out string? rejection)
    {
        rejection = null;

        if (TransientCategories.IsTransient(gamePath) is false)
            return true;

        try
        {
            rejection = gamePath[gamePath.LastIndexOf('.')..].ToLowerInvariant() switch
            {
                ".pap" => CheckPap(content),
                ".tmb" => CheckMagic(content, "TMLB"u8),
                ".avfx" => CheckMagic(content, "XFVA"u8),
                ".scd" => CheckMagic(content, "SEDBSSCF"u8),
                ".atex" => content.Length > 0 ? null : "texture d'effet vide",
                _ => null,
            };
        }
        catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException)
        {
            rejection = $"illisible : {e.GetType().Name}";
        }

        return rejection is null;
    }

    private static string? CheckMagic(Stream content, ReadOnlySpan<byte> magic)
    {
        Span<byte> head = stackalloc byte[magic.Length];
        content.Position = 0;

        return content.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length && head.SequenceEqual(magic)
            ? null
            : "marque de fichier absente ou inattendue";
    }

    private static string? CheckPap(Stream content)
    {
        var length = content.Length;

        if (length < PapHeaderLength)
            return "animation : fichier plus court que son en-tête";

        Span<byte> header = stackalloc byte[PapHeaderLength];
        content.Position = 0;

        if (content.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length)
            return "animation : en-tête illisible";

        if (header[..4].SequenceEqual(PapMagic) is false)
            return "animation : marque pap absente";

        if (BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != PapVersion)
            return "animation : version inconnue";

        var animations = BinaryPrimitives.ReadInt16LittleEndian(header[8..]);
        var info = BinaryPrimitives.ReadInt32LittleEndian(header[14..]);
        var havok = BinaryPrimitives.ReadInt32LittleEndian(header[18..]);
        var footer = BinaryPrimitives.ReadInt32LittleEndian(header[22..]);

        if (animations is < 0 or > MaxPapAnimations)
            return "animation : nombre d'animations hors bornes";

        if (info != PapHeaderLength)
            return "animation : section d'informations mal placée";

        // Au moins après les informations, pas forcément juste après : 8 des
        // 5 109 fichiers mesurés ont quelques octets de remplissage.
        if (havok < PapHeaderLength + (PapInfoEntryLength * animations))
            return "animation : section Havok avant la fin des informations";

        if (footer <= havok + 8 || footer > length)
            return "animation : section Havok hors du fichier ou vide";

        Span<byte> havokHead = stackalloc byte[8];
        content.Position = havok;

        if (content.ReadAtLeast(havokHead, havokHead.Length, throwOnEndOfStream: false) != havokHead.Length)
            return "animation : section Havok illisible";

        return havokHead.SequenceEqual(HavokTagfile) || havokHead.SequenceEqual(HavokPackfile)
            ? null
            : "animation : section Havok sans marque de fichier Havok";
    }
}
