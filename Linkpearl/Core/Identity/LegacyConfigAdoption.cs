using System.Text.Json;

namespace Linkpearl.Core.Identity;

/// <summary>
/// Reprend ce qui vivait sous l'ancien InternalName, avant le renommage en
/// LinkpearlSync.
/// </summary>
/// <remarks>
/// <c>pluginConfigs/Linkpearl.json</c> et <c>pluginConfigs/Linkpearl/</c> sont
/// aussi les emplacements du plugin officiel de même ancien nom : la prudence
/// vient d'abord de là, pas seulement de la migration en elle-même. On ne
/// reprend que ce qui est manifestement à nous, on ne remplace jamais rien
/// qui existe déjà côté nouveau nom, et on ne touche jamais à l'ancien
/// dossier ni à l'ancien fichier : une reprise ratée ne doit rien perdre.
/// </remarks>
public static class LegacyConfigAdoption
{
    /// <summary>Les clés de premier niveau propres à la configuration de Linkpearl.</summary>
    /// <remarks>
    /// Le plugin officiel de même ancien nom range aussi ses réglages sous
    /// <c>Linkpearl.json</c> : un JSON qui n'en porte aucune (ou une seule,
    /// par coïncidence) n'est pas le nôtre.
    /// </remarks>
    private static readonly string[] OwnConfigurationKeys = ["RendezvousHost", "CacheQuotaBytes", "Discoverable"];

    /// <summary>
    /// Reprend le dossier d'identités d'avant le renommage, s'il est bien à nous.
    /// </summary>
    /// <returns>Vrai si la reprise a eu lieu.</returns>
    public static bool TryAdoptDirectory(string legacyDir, string newDir)
    {
        if (Directory.Exists(newDir) && Directory.EnumerateFileSystemEntries(newDir).Any())
            return false;

        // Le sous-dossier characters/ est ce qui distingue notre dossier de
        // celui, au même ancien nom, du plugin officiel : lui n'y range rien
        // de tel.
        if (Directory.Exists(Path.Combine(legacyDir, "characters")) is false)
            return false;

        CopyDirectory(legacyDir, newDir);
        return true;
    }

    /// <summary>
    /// Reprend le fichier de configuration d'avant le renommage, s'il est bien à nous.
    /// </summary>
    /// <returns>Vrai si la reprise a eu lieu.</returns>
    public static bool TryAdoptConfigFile(string legacyFile, string newFile)
    {
        if (File.Exists(newFile) || File.Exists(legacyFile) is false)
            return false;

        if (LooksLikeOurConfiguration(legacyFile) is false)
            return false;

        File.Copy(legacyFile, newFile);
        return true;
    }

    private static bool LooksLikeOurConfiguration(string legacyFile)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(legacyFile));

            if (document.RootElement.ValueKind is not JsonValueKind.Object)
                return false;

            return OwnConfigurationKeys.Count(key => document.RootElement.TryGetProperty(key, out _)) >= 2;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Un JSON illisible, tronqué, ou un fichier devenu inaccessible
            // entre le File.Exists et la lecture : aucune reprise plutôt
            // qu'un plugin qui refuse de charger.
            return false;
        }
    }

    /// <summary>Copie récursive sans jamais écraser une entrée déjà là.</summary>
    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var target = Path.Combine(destinationDir, Path.GetFileName(file));

            if (File.Exists(target) is false)
                File.Copy(file, target);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDir))
            CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
    }
}
