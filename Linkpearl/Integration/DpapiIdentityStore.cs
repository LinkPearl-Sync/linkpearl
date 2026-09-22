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

    public byte[]? Load()
    {
        try
        {
            return File.Exists(path)
                ? ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser)
                : null;
        }
        catch (Exception e) when (e is CryptographicException or IOException)
        {
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
        File.Move(temporary, path, overwrite: true);
    }
}
