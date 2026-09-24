using Linkpearl.Core.Cache;
using Linkpearl.Core.Safety;

namespace Linkpearl.Core.Manifest;

/// <summary>
/// Construit le manifeste de notre propre personnage.
/// </summary>
public static class ManifestBuilder
{
    /// <summary>
    /// Regroupe les fichiers résolus par contenu et rend un manifeste canonique.
    /// </summary>
    /// <remarks>
    /// Un fichier qui ne passe pas les contrôles est écarté et rapporté, pas
    /// fatal. C'est l'inverse de <see cref="ManifestValidator"/>, qui rejette un
    /// manifeste reçu en entier : à la construction, un seul mod tordu ne doit
    /// pas priver l'utilisateur de toute synchronisation, alors qu'à la
    /// réception un rejet partiel donnerait un personnage incohérent et
    /// masquerait une tentative.
    /// </remarks>
    public static ManifestBuildResult Build(
        IEnumerable<ResolvedFile> files,
        string metaManipulations,
        string? glamourerState,
        Quotas quotas,
        IEnumerable<FileSwap>? swaps = null)
    {
        var grouped = new Dictionary<BlobHash, (long Size, SortedSet<string> Paths)>();
        var skipped = new List<SkippedFile>();

        foreach (var file in files)
        {
            if (GamePathPolicy.TryNormalize(file.GamePath, quotas, out var path, out var why) is false)
            {
                skipped.Add(new SkippedFile(file.GamePath, why!));
                continue;
            }

            if (ExtensionAllowList.IsAllowed(path, out var refus) is false)
            {
                skipped.Add(new SkippedFile(file.GamePath, refus!));
                continue;
            }

            if (grouped.TryGetValue(file.Hash, out var entry) is false)
            {
                entry = (file.Size, new SortedSet<string>(StringComparer.Ordinal));
                grouped[file.Hash] = entry;
            }

            entry.Paths.Add(path);
        }

        // Tri par empreinte, puis chemins triés à l'intérieur de chaque entrée :
        // sans cet ordre total, deux recalculs du même contenu donneraient deux
        // hashs de manifeste différents.
        var replacements = grouped
            .OrderBy(pair => pair.Key.ToHex(), StringComparer.Ordinal)
            .Select(pair => new FileReplacement(pair.Value.Paths.ToArray(), pair.Key, pair.Value.Size))
            .ToArray();

        var claimed = replacements.SelectMany(r => r.GamePaths).ToHashSet(StringComparer.Ordinal);
        var kept = new SortedDictionary<string, FileSwap>(StringComparer.Ordinal);

        foreach (var swap in swaps ?? [])
        {
            if (FileSwapPolicy.TryNormalize(swap, quotas, out var normalized, out var why) is false)
            {
                skipped.Add(new SkippedFile(swap.GamePath, why!));
                continue;
            }

            // Un chemin déjà servi par un fichier, ou déjà échangé : le receveur
            // refuserait le manifeste entier pour cette contradiction.
            if (claimed.Contains(normalized!.GamePath) || kept.ContainsKey(normalized.GamePath))
            {
                skipped.Add(new SkippedFile(swap.GamePath, "chemin déjà remplacé"));
                continue;
            }

            kept[normalized.GamePath] = normalized;
        }

        return new ManifestBuildResult(
            new CharacterManifest(CharacterManifest.CurrentVersion, replacements, metaManipulations, glamourerState,
                Swaps: kept.Count > 0 ? kept.Values.ToArray() : null),
            skipped);
    }
}
