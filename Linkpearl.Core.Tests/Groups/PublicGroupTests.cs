using Linkpearl.Core.Groups;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Tests.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Groups;

public sealed class PublicGroupTests
{
    internal static readonly RendezvousAddress Service = new("rdv.exemple.ch", 47900);

    private readonly MovableClock _clock = new();

    [Fact]
    public void Le_secret_et_l_identifiant_sont_figes()
    {
        // Tous les clients doivent tomber sur les mêmes boîtes : ces valeurs ne
        // changent jamais, et docs/protocol.md les recopie.
        Assert.Equal("41b3c4bdfd876a04e0c524eca45a469d6baf181d0657e464eeef4f59bfaa5a7a", Convert.ToHexStringLower(PublicGroup.Secret));
        Assert.Equal("c92263f11cad477c5082acba30895596", PublicGroup.Id.ToString());
    }

    [Fact]
    public void Public_bloque_les_effets_par_defaut_et_n_a_ni_cle_ni_politique()
    {
        var group = PublicGroup.Create([Service], _clock.UtcNow);

        Assert.True(group.IsPublic);
        Assert.Equal(TransientCategories.None, group.DefaultReceive);
        Assert.Null(group.OwnerKey);
        Assert.Null(group.Policy);
        Assert.Equal(PublicGroup.Name, group.Name);
    }

    [Fact]
    public void Un_membre_sans_reglage_suit_le_defaut_du_groupe()
    {
        var member = new GroupMember { Fingerprint = PlayerFingerprint.Of("bob", 21), DisplayName = "Bob" };
        var group = PublicGroup.Create([Service], _clock.UtcNow);

        Assert.Equal(TransientCategories.None, group.ReceiveOf(member));
        Assert.Equal(TransientCategories.All, group.ReceiveOf(member with { Receive = TransientCategories.All }));
        Assert.Equal(TransientCategories.All, (group with { DefaultReceive = TransientCategories.All }).ReceiveOf(null));
    }

    [Fact]
    public void Un_bloque_est_refuse_par_cle_ou_par_empreinte()
    {
        var bob = PlayerFingerprint.Of("bob", 21);
        var key = PeerId.FromBytes(new byte[PeerId.SizeInBytes]);
        var group = PublicGroup.Create([Service], _clock.UtcNow) with { Blocked = [new GroupBan(key, bob)] };

        Assert.True(group.Refuses(null, bob));
        Assert.True(group.Refuses(key, null));
        Assert.False(group.Refuses(null, PlayerFingerprint.Of("alice", 21)));
    }
}
