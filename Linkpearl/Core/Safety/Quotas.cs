namespace Linkpearl.Core.Safety;

/// <summary>
/// Plafonds appliqués à toute donnée venant d'un pair.
/// </summary>
/// <remarks>
/// Un dépassement coupe la session plutôt que de tronquer : une donnée tronquée
/// donne un personnage incohérent et masque une tentative d'abus.
/// </remarks>
public sealed record Quotas
{
    public static Quotas Default { get; } = new();

    /// <summary>Nombre de remplacements de fichiers dans un manifeste.</summary>
    public int MaxReplacements { get; init; } = 2_000;

    /// <summary>Nombre total de chemins de jeu, plusieurs pouvant viser un même contenu.</summary>
    public int MaxGamePaths { get; init; } = 8_000;

    /// <summary>Longueur d'un chemin de jeu, en caractères.</summary>
    public int MaxGamePathLength { get; init; } = 256;

    /// <summary>Profondeur d'un chemin de jeu, en segments.</summary>
    public int MaxGamePathDepth { get; init; } = 16;

    /// <summary>Taille d'un blob transféré.</summary>
    public long MaxBlobBytes { get; init; } = 128L * 1024 * 1024;

    /// <summary>Taille d'un manifeste tel qu'il arrive sur le réseau.</summary>
    public int MaxManifestCompressedBytes { get; init; } = 1024 * 1024;

    /// <summary>
    /// Taille d'un manifeste une fois détendu.
    /// </summary>
    /// <remarks>
    /// Un pair peut envoyer un mégaoctet qui se détend en plusieurs gigaoctets.
    /// La lecture s'arrête ici plutôt que de remplir la mémoire du processus du jeu.
    /// </remarks>
    public int MaxManifestDecompressedBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Chaîne de manipulations méta de Penumbra, opaque pour nous.</summary>
    public int MaxMetaManipulationChars { get; init; } = 512 * 1024;

    /// <summary>Chaîne d'état de Glamourer, opaque pour nous.</summary>
    public int MaxGlamourerStateChars { get; init; } = 64 * 1024;

    /// <summary>Profil Customize+, JSON des os : de 1 à 15 Kio relevés, marge large.</summary>
    public int MaxCustomizePlusChars { get; init; } = 64 * 1024;

    /// <summary>Configuration SimpleHeels nettoyée : moins de 1 Kio relevé.</summary>
    public int MaxHeelsChars { get; init; } = 16 * 1024;

    /// <summary>Titre Honorific : moins de 250 octets relevés.</summary>
    public int MaxHonorificChars { get; init; } = 4 * 1024;

    /// <summary>Honorific refuse lui-même d'afficher plus de 32 caractères.</summary>
    public int MaxHonorificTitleLength { get; init; } = 32;

    /// <summary>Moodles en base64 : de 0,5 à 3 Kio relevés.</summary>
    public int MaxMoodlesChars { get; init; } = 32 * 1024;

    /// <summary>PetNicknames en base64 : de 0,5 à 3 Kio relevés.</summary>
    public int MaxPetNicknamesChars { get; init; } = 16 * 1024;

    /// <summary>Profondeur maximale des extras JSON.</summary>
    public int MaxExtrasJsonDepth { get; init; } = 8;
}
