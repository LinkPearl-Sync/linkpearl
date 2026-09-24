using Linkpearl.Core.Cache;

namespace Linkpearl.Core.Manifest;

/// <summary>
/// Un fichier résolu par Penumbra, avant regroupement.
/// </summary>
public readonly record struct ResolvedFile(string GamePath, BlobHash Hash, long Size);

/// <summary>
/// Un contenu, et tous les chemins de jeu qu'il remplace.
/// </summary>
/// <remarks>
/// Le regroupement se fait par hash et non par chemin : une texture référencée
/// par six chemins de jeu est un blob à transférer, pas six.
/// </remarks>
public sealed record FileReplacement(IReadOnlyList<string> GamePaths, BlobHash Hash, long Size);

/// <summary>
/// L'apparence d'un personnage, décrite sans son contenu.
/// </summary>
/// <remarks>
/// Le manifeste ne porte aucun horodatage, et c'est délibéré : son hash est son
/// identité. Une date de construction le ferait changer à chaque recalcul même
/// quand rien n'a bougé, et le diff qui évite de retransférer ne servirait plus
/// à rien. La fraîcheur relève du numéro de séquence porté par le protocole.
///
/// <c>MetaManipulations</c> et <c>GlamourerState</c> sont opaques : ce sont des
/// chaînes produites par Penumbra et Glamourer, que l'on transporte sans les
/// comprendre. On ne peut donc que borner leur taille et vérifier qu'elles sont
/// du base64, le reste de la confiance étant délégué à ces deux plugins.
/// </remarks>
public sealed record CharacterManifest(
    ushort Version,
    IReadOnlyList<FileReplacement> Replacements,
    string MetaManipulations,
    string? GlamourerState,
    CharacterExtras? Extras = null,
    IReadOnlyList<FileSwap>? Swaps = null)
{
    /// <summary>
    /// 2 depuis les intégrations : un manifeste peut porter des extras. Un
    /// manifeste 1 reste lisible, sans extras.
    /// </summary>
    public const ushort CurrentVersion = 2;

    public CharacterExtras ExtrasOrNone => Extras ?? CharacterExtras.None;

    /// <summary>
    /// Les échanges : un chemin de jeu qui en joue un autre, sans fichier à
    /// transférer. Forme de bien des mods d'animation, qui font jouer l'idle
    /// d'une autre race.
    /// </summary>
    /// <remarks>
    /// Ajoutés sans changer de version : un lecteur plus ancien ignore la clé,
    /// et ne pose alors que les fichiers, ce qu'il faisait déjà.
    /// </remarks>
    public IReadOnlyList<FileSwap> SwapsOrNone => Swaps ?? [];
}

/// <summary>
/// Ce que les plugins voisins montrent du personnage, au format natif de chacun.
/// </summary>
/// <remarks>
/// Null veut dire « pas de ce plugin, ou rien à montrer ». Les chaînes sont
/// opaques pour le noyau, qui n'en vérifie que la forme et la taille : ce sont
/// les plugins qui leur donnent un sens. Moodles et PetNicknames arrivent déjà
/// nettoyés de tout identifiant.
/// </remarks>
public sealed record CharacterExtras(
    string? CustomizePlus,
    string? Heels,
    string? Honorific,
    string? Moodles,
    string? PetNicknames)
{
    public static CharacterExtras None { get; } = new(null, null, null, null, null);

    public bool IsEmpty => this == None;
}

/// <summary>Un fichier écarté à la construction, avec la raison.</summary>
public sealed record SkippedFile(string GamePath, string Reason);

/// <summary>Le manifeste construit, et ce qui n'a pas pu y entrer.</summary>
public sealed record ManifestBuildResult(CharacterManifest Manifest, IReadOnlyList<SkippedFile> Skipped);
