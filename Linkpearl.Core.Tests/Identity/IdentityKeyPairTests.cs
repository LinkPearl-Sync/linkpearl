using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Xunit;

namespace Linkpearl.Core.Tests.Identity;

public class IdentityKeyPairTests
{
    private sealed class MemoryStore : IIdentityStore
    {
        private byte[]? _blob;

        public byte[]? Load() => _blob;

        public void Save(byte[] blob) => _blob = blob;

        public void Corrupt() => _blob = [1, 2, 3];
    }

    [Fact]
    public void Une_identite_est_creee_au_premier_lancement()
    {
        var store = new MemoryStore();
        using var identity = IdentityKeyPair.LoadOrCreate(store);

        Assert.NotNull(store.Load());
        Assert.Equal(CryptoPrimitives.PublicPointLength, identity.PublicKey.Length);
    }

    [Fact]
    public void La_meme_identite_est_retrouvee_au_lancement_suivant()
    {
        // La perdre, c'est perdre tous les pairages : c'est elle que les autres
        // ont épinglée.
        var store = new MemoryStore();

        using var premier = IdentityKeyPair.LoadOrCreate(store);
        using var second = IdentityKeyPair.LoadOrCreate(store);

        Assert.Equal(premier.PublicKey, second.PublicKey);
        Assert.Equal(premier.Id, second.Id);
    }

    [Fact]
    public void Une_identite_illisible_n_empeche_pas_le_demarrage()
    {
        var store = new MemoryStore();
        using var premier = IdentityKeyPair.LoadOrCreate(store);
        store.Corrupt();

        using var second = IdentityKeyPair.LoadOrCreate(store);

        Assert.NotEqual(premier.PublicKey, second.PublicKey);
    }

    [Fact]
    public void L_identite_signe_et_sa_cle_publique_verifie()
    {
        var store = new MemoryStore();
        using var identity = IdentityKeyPair.LoadOrCreate(store);

        var signature = CryptoPrimitives.Sign(identity.Key, "message"u8);
        using var verifier = CryptoPrimitives.ImportVerifier(identity.PublicKey);

        Assert.True(CryptoPrimitives.Verify(verifier, "message"u8, signature));
    }

    [Fact]
    public void Une_invitation_porte_l_empreinte_de_cette_identite()
    {
        var store = new MemoryStore();
        using var identity = IdentityKeyPair.LoadOrCreate(store);

        var code = identity.NewInvitation("rdv.exemple.ch");

        Assert.True(PairingCode.TryParse(code.Encode(), out var parsed, out var why), why);
        Assert.Equal(identity.Id, parsed!.Id);
    }
}
