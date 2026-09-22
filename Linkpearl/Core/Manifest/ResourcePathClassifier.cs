using Linkpearl.Core.Safety;

namespace Linkpearl.Core.Manifest;

/// <summary>Un fichier du disque, et les chemins de jeu qu'il remplace.</summary>
public sealed record LocalFile(string LocalPath, IReadOnlyList<string> GamePaths);

/// <summary>Un chemin de jeu qui en désigne un autre, sans fichier à transférer.</summary>
public sealed record FileSwap(string GamePath, string TargetGamePath);

/// <summary>Le tri des ressources rendues par Penumbra.</summary>
public sealed record ClassifiedResources(
    IReadOnlyList<LocalFile> Files,
    IReadOnlyList<FileSwap> Swaps,
    IReadOnlyList<SkippedFile> Skipped);

/// <summary>
/// Trie ce que Penumbra rend pour un personnage.
/// </summary>
/// <remarks>
/// Penumbra rend « chemin réel -> chemins de jeu », et le chemin réel est de
/// trois natures :
/// <list type="bullet">
/// <item>un fichier du disque, quand un mod redirige la ressource ;</item>
/// <item>un autre chemin de jeu, quand un mod fait un échange de fichier ;</item>
/// <item>le chemin de jeu lui-même, quand la ressource n'est pas moddée.</item>
/// </list>
/// Les confondre ferait chercher un fichier qui n'existe pas, ou gonflerait le
/// manifeste de centaines d'entrées vanilla qu'il n'y a aucune raison de
/// transférer.
/// </remarks>
public static class ResourcePathClassifier
{
    public static ClassifiedResources Classify(
        IEnumerable<(string ActualPath, IReadOnlyCollection<string> GamePaths)> resources,
        Quotas quotas)
    {
        var files = new List<LocalFile>();
        var swaps = new List<FileSwap>();
        var skipped = new List<SkippedFile>();

        foreach (var (actualPath, rawGamePaths) in resources)
        {
            var gamePaths = new List<string>();

            foreach (var raw in rawGamePaths)
            {
                if (GamePathPolicy.TryNormalize(raw, quotas, out var normalized, out var why) is false)
                {
                    skipped.Add(new SkippedFile(raw, why!));
                    continue;
                }

                if (ExtensionAllowList.IsAllowed(normalized, out var refus) is false)
                {
                    skipped.Add(new SkippedFile(raw, refus!));
                    continue;
                }

                gamePaths.Add(normalized);
            }

            if (gamePaths.Count == 0)
                continue;

            gamePaths.Sort(StringComparer.Ordinal);

            if (LooksLikeFileSystemPath(actualPath))
            {
                files.Add(new LocalFile(actualPath, gamePaths));
                continue;
            }

            // Chemin de jeu : échange, ou ressource non moddée.
            if (GamePathPolicy.TryNormalize(actualPath, quotas, out var target, out var targetWhy) is false)
            {
                skipped.Add(new SkippedFile(actualPath, targetWhy!));
                continue;
            }

            foreach (var gamePath in gamePaths)
            {
                if (string.Equals(gamePath, target, StringComparison.Ordinal))
                    continue;   // vanilla : rien à synchroniser

                swaps.Add(new FileSwap(gamePath, target));
            }
        }

        return new ClassifiedResources(files, swaps, skipped);
    }

    /// <summary>
    /// Reconnaît un chemin du système de fichiers plutôt qu'un chemin de jeu.
    /// </summary>
    /// <remarks>
    /// Le test est explicite et non délégué à <c>Path</c>, dont le comportement
    /// diffère entre Windows et Linux : le noyau se teste sous Linux et doit
    /// pourtant raisonner sur des chemins Windows.
    /// </remarks>
    private static bool LooksLikeFileSystemPath(string path)
    {
        if (path.Length == 0)
            return false;

        if (path[0] is '/' or '\\')
            return true;                                    // racine unix, ou UNC

        if (path.Contains('\\'))
            return true;                                    // séparateur Windows

        return path.Length >= 2 && path[1] == ':';          // lettre de lecteur
    }
}
