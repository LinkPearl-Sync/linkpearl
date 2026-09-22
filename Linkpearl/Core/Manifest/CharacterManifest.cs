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
    string? GlamourerState)
{
    public const ushort CurrentVersion = 1;
}

/// <summary>Un fichier écarté à la construction, avec la raison.</summary>
public sealed record SkippedFile(string GamePath, string Reason);

/// <summary>Le manifeste construit, et ce qui n'a pas pu y entrer.</summary>
public sealed record ManifestBuildResult(CharacterManifest Manifest, IReadOnlyList<SkippedFile> Skipped);
