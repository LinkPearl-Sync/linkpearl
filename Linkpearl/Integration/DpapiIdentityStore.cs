using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;

namespace Linkpearl.Integration;

/// <summary>
/// Range la clé privée d'identité, protégée par DPAPI.
/// </summary>
/// <remarks>
/// Ici et non dans Core/ : DPAPI est propre à Windows, et le noyau doit rester
/// compilable et testable sous Linux. La protection est liée au compte
/// utilisateur, donc un autre compte de la même machine ne peut pas relire le
/// fichier.
///
/// Ce n'est pas une protection contre quelqu'un qui aurait déjà la main sur la
/// session : dans ce cas il a aussi le jeu, les mods, et tout le reste.
/// </remarks>
public sealed class DpapiIdentityStore(string path) : IIdentityStore
{
    private static readonly byte[] Entropy = "linkpearl:identity:v1"u8.ToArray();

    /// <remarks>
    /// Une erreur d'entrée-sortie remonte au lieu de rendre null : null veut
    /// dire « pas d'identité », et l'appelant en créerait une neuve. Un fichier
    /// verrouillé un instant ne doit pas coûter tous les pairages.
    /// </remarks>
    public byte[]? Load()
    {
        if (File.Exists(path) is false)
            return null;

        try
        {
            return ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            // Protégée par un autre compte Windows, ou par ce compte avant une
            // réinstallation du système. Écartée plutôt qu'écrasée.
            CharacterStorage.SetAside(path, "illisible");
            return null;
        }
    }

    public void Save(byte[] blob)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Écriture puis déplacement : une coupure de courant ne doit pas laisser
        // une identité tronquée, qui reviendrait à perdre tous les pairages.
        var temporary = path + ".part";
        File.WriteAllBytes(temporary, ProtectedData.Protect(blob, Entropy, DataProtectionScope.CurrentUser));

        // Une identité ne s'écrit par-dessus une autre que par accident : la
        // création suit une lecture qui a échoué, la restauration écarte déjà
        // la sienne. L'ancienne reste donc à côté.
        CharacterStorage.SetAside(path, "remplacee");
        File.Move(temporary, path);
    }
}
