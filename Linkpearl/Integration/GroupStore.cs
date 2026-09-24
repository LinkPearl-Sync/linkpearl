using System.Security.Cryptography;
using System.Text.Json;
using Linkpearl.Core.Groups;

namespace Linkpearl.Integration;

/// <summary>
/// Conserve les groupes du personnage, protégés par DPAPI.
/// </summary>
/// <remarks>
/// Protégés parce qu'ils contiennent les secrets de groupe : qui les lirait
/// verrait quels membres sont en ligne, et pourrait se présenter à eux.
/// </remarks>
public sealed class GroupStore(string path)
{
    private static readonly byte[] Entropy = "linkpearl:groups:v1"u8.ToArray();

    /// <summary>Un épinglage peut venir d'un handshake pendant que l'interface enregistre.</summary>
    private readonly Lock _writing = new();

    public void Load(GroupBook book)
    {
        try
        {
            if (ReadPlain() is { } plain)
                book.Load(GroupBookCodec.Decode(plain));
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        {
            // Même règle que le carnet : on démarre sans groupes plutôt que
            // d'empêcher le plugin de se charger, et le fichier est écarté,
            // sans quoi le premier enregistrement l'écraserait.
            CharacterStorage.SetAside(path, "illisible");
        }
    }

    public byte[]? ReadPlain()
        => File.Exists(path)
            ? ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser)
            : null;

    public void WritePlain(byte[] plain)
    {
        lock (_writing)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".part";
            File.WriteAllBytes(temporary, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
            File.Move(temporary, path, overwrite: true);
        }
    }

    public void Save(GroupBook book) => WritePlain(GroupBookCodec.Encode(book.All));
}
