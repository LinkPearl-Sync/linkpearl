using System.Security.Cryptography;
using System.Buffers.Binary;

namespace Linkpearl.Core.Identity;

/// <summary>
/// Le nom du dossier où vivent les affaires d'un personnage.
/// </summary>
/// <remarks>
/// Une empreinte, et non l'identifiant lui-même : un identifiant de contenu
/// désigne un personnage chez l'éditeur du jeu, et il n'a rien à faire en clair
/// dans une arborescence qu'une capture d'écran ou un journal partagé exposent.
/// On n'a jamais besoin de remonter du dossier à l'identifiant, seulement de
/// retomber sur le même dossier pour le même personnage.
///
/// Seize caractères hexadécimaux, soit soixante-quatre bits : une collision
/// demanderait des milliards de personnages sur une même machine.
/// </remarks>
public static class CharacterFolder
{
    private static ReadOnlySpan<byte> Info => "linkpearl:character-folder:v1"u8;

    public const int Length = 16;

    public static string Name(ulong contentId)
    {
        // Zéro veut dire « personne n'est connecté ». Y répondre par un dossier
        // ferait écrire l'identité de personne dans un dossier partagé, que le
        // premier personnage à se connecter adopterait ensuite.
        ArgumentOutOfRangeException.ThrowIfZero(contentId);

        Span<byte> material = stackalloc byte[Info.Length + sizeof(ulong)];
        Info.CopyTo(material);
        BinaryPrimitives.WriteUInt64LittleEndian(material[Info.Length..], contentId);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(material, digest);

        return Convert.ToHexStringLower(digest[..(Length / 2)]);
    }
}
