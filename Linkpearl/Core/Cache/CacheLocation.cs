namespace Linkpearl.Core.Cache;

/// <summary>Un dossier prêt à recevoir le cache, ou la raison du refus.</summary>
public sealed record CachePreparation(string? Root, string? Error);

/// <summary>
/// Où vit le cache, et comment on s'en débarrasse.
/// </summary>
/// <remarks>
/// Linkpearl n'écrit jamais directement dans le dossier choisi : il y crée
/// <see cref="FolderName"/>. Choisir <c>D:\Jeux</c> ne mélange donc rien aux
/// fichiers de l'utilisateur, et supprimer le cache ne touche qu'à ce qui est
/// à nous.
/// </remarks>
public static class CacheLocation
{
    public const string FolderName = "LinkpearlCache";

    private const string Probe = ".linkpearl-probe";

    /// <summary>Le dossier effectif : le réglage, ou le défaut s'il est vide.</summary>
    public static string Resolve(string configured, string defaultRoot)
        => configured is "" ? defaultRoot : configured;

    /// <summary>Le dossier du cache pour un dossier choisi.</summary>
    /// <remarks>
    /// Rechoisir <see cref="FolderName"/> lui-même, par exemple après l'avoir
    /// recréé, ne doit pas donner <c>LinkpearlCache/LinkpearlCache</c>.
    /// </remarks>
    public static string ForChosen(string chosen)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(chosen));

        return string.Equals(Path.GetFileName(full), FolderName, StringComparison.OrdinalIgnoreCase)
            ? full
            : Path.Combine(full, FolderName);
    }

    /// <summary>
    /// Crée le sous-dossier et vérifie qu'on peut y écrire.
    /// </summary>
    /// <remarks>
    /// Un fichier témoin écrit puis effacé : un disque en lecture seule ou un
    /// dossier protégé se découvrent ici, et non au premier transfert.
    /// </remarks>
    public static CachePreparation Prepare(string chosen)
    {
        if (string.IsNullOrWhiteSpace(chosen) || Path.IsPathFullyQualified(chosen) is false)
            return new(null, "choisissez un dossier complet, lecteur compris.");

        try
        {
            var root = ForChosen(chosen);
            Directory.CreateDirectory(root);

            var probe = Path.Combine(root, Probe);
            File.WriteAllBytes(probe, [1]);
            File.Delete(probe);

            return new(root, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                     or NotSupportedException or ArgumentException)
        {
            return new(null, $"ce dossier n'est pas utilisable : {e.Message}");
        }
    }

    /// <summary>La place qu'occupe un dossier, zéro s'il n'existe pas.</summary>
    /// <remarks>Parcourt tout l'arbre : à appeler depuis le pool de threads.</remarks>
    public static long Measure(string root)
    {
        if (Directory.Exists(root) is false)
            return 0;

        return new DirectoryInfo(root)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(file => file.Length);
    }

    /// <summary>
    /// Efface un ancien cache, fichier par fichier.
    /// </summary>
    /// <remarks>
    /// Jamais de suppression récursive d'un dossier : seulement les blobs dont
    /// le nom est un hash, les écritures interrompues et l'index. Un fichier
    /// étranger y survit toujours, et le dossier qui le contient avec lui.
    /// Parcourt tout l'arbre : à appeler depuis le pool de threads.
    /// </remarks>
    /// <returns>Le nombre de fichiers effacés.</returns>
    public static int DeleteOwned(string root)
    {
        if (Directory.Exists(root) is false)
            return 0;

        var deleted = 0;
        var blobs = Path.Combine(root, "blobs");
        var incoming = Path.Combine(root, "incoming");

        if (Directory.Exists(blobs))
        {
            foreach (var file in Directory.EnumerateFiles(blobs, "*", SearchOption.AllDirectories).ToList())
            {
                if (BlobHash.TryParseHex(Path.GetFileName(file), out _) && TryDelete(file))
                    deleted++;
            }
        }

        if (Directory.Exists(incoming))
        {
            foreach (var file in Directory.EnumerateFiles(incoming, "*.part").ToList())
            {
                if (TryDelete(file))
                    deleted++;
            }
        }

        foreach (var name in new[] { "cache.index", "cache.index.part" })
        {
            var file = Path.Combine(root, name);

            if (File.Exists(file) && TryDelete(file))
                deleted++;
        }

        if (Directory.Exists(blobs))
            RemoveEmptyTree(blobs);

        RemoveIfEmpty(incoming);
        RemoveIfEmpty(root);

        return deleted;
    }

    private static bool TryDelete(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Un blob en cours de lecture : il restera, et le dossier avec lui.
            return false;
        }
    }

    /// <summary>Les dossiers sous <c>blobs</c> sont les deux niveaux de hash : à nous.</summary>
    private static void RemoveEmptyTree(string directory)
    {
        foreach (var child in Directory.EnumerateDirectories(directory).ToList())
            RemoveEmptyTree(child);

        RemoveIfEmpty(directory);
    }

    private static void RemoveIfEmpty(string directory)
    {
        if (Directory.Exists(directory) is false || Directory.EnumerateFileSystemEntries(directory).Any())
            return;

        try
        {
            Directory.Delete(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Vide mais verrouillé : il restera, sans rien coûter.
        }
    }
}
