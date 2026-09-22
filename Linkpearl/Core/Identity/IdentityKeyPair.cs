using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;

namespace Linkpearl.Core.Identity;

/// <summary>
/// L'identité de cette installation.
/// </summary>
/// <remarks>
/// Générée au premier lancement et conservée telle quelle : la perdre, c'est
/// perdre tous les pairages, puisque c'est elle que les autres ont épinglée. Un
/// export et un import sont donc indispensables, sans quoi réinstaller Windows
/// obligerait à tout refaire.
/// </remarks>
public sealed class IdentityKeyPair : IDisposable
{
    private IdentityKeyPair(ECDsa key)
    {
        Key = key;
        PublicKey = CryptoPrimitives.ExportPublicPoint(key);
        Id = PeerId.Of(PublicKey);
    }

    public ECDsa Key { get; }

    public byte[] PublicKey { get; }

    public PeerId Id { get; }

    /// <summary>Charge l'identité existante, ou en crée une au premier lancement.</summary>
    public static IdentityKeyPair LoadOrCreate(IIdentityStore store)
    {
        var stored = store.Load();

        if (stored is not null)
        {
            try
            {
                var existing = ECDsa.Create();
                existing.ImportPkcs8PrivateKey(stored, out _);
                return new IdentityKeyPair(existing);
            }
            catch (CryptographicException)
            {
                // Identité illisible : on en crée une neuve plutôt que d'empêcher
                // le plugin de démarrer. Les pairages seront à refaire, et
                // l'interface doit le dire.
            }
        }

        var created = CryptoPrimitives.GenerateIdentity();
        store.Save(created.ExportPkcs8PrivateKey());
        return new IdentityKeyPair(created);
    }

    public PairingCode NewInvitation(string rendezvousHost) => PairingCode.Create(Id, rendezvousHost);

    public void Dispose() => Key.Dispose();
}
