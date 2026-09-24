using System.Text;
using System.Text.Json;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;
using Xunit;

namespace Linkpearl.Core.Tests.Groups;

public sealed class GroupBookCodecTests
{
    private static readonly byte[] Secret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    private static readonly DateTimeOffset Noon = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly PlayerFingerprint Alice = PlayerFingerprint.Of("alice", 21);

    [Fact]
    public void Un_groupe_survit_a_l_aller_retour()
    {
        var member = new GroupMember
        {
            Fingerprint = Alice,
            Id = PeerId.Of([2, 1, 2, 3]),
            DisplayName = "Alice",
            LastSeenAt = Noon,
            Paused = true,
            Receive = new TransientCategories(true, false, true),
        };

        var group = GroupBookTests.Group(Secret, Noon) with
        {
            Members = new Dictionary<PlayerFingerprint, GroupMember> { [Alice] = member },
        };

        var back = Assert.Single(GroupBookCodec.Decode(GroupBookCodec.Encode([group])));

        Assert.Equal(group.Id, back.Id);
        Assert.Equal(group.Name, back.Name);
        Assert.Equal(group.Secret, back.Secret);
        Assert.Equal(group.Rendezvous, back.Rendezvous);
        Assert.Equal(group.JoinedAt, back.JoinedAt);
        Assert.Equal(member, back.Members[Alice]);
    }

    [Fact]
    public void Une_entree_abimee_n_emporte_pas_les_autres()
    {
        var text = Encoding.UTF8.GetString(GroupBookCodec.Encode([GroupBookTests.Group(Secret, Noon)]));

        // Un second groupe au secret trop court, glissé devant le bon.
        var broken = text.Insert(1, """{"Id":"00","Name":"x","Secret":"0011","Rendezvous":["rdv.exemple.ch"],"JoinedAt":0,"Members":[]},""");

        var decoded = GroupBookCodec.Decode(Encoding.UTF8.GetBytes(broken));

        Assert.Equal(GroupId.Of(Secret), Assert.Single(decoded).Id);
    }

    [Fact]
    public void Un_groupe_sans_service_est_ignore()
        => Assert.Empty(GroupBookCodec.Decode(GroupBookCodec.Encode([GroupBookTests.Group(Secret, Noon) with { Rendezvous = [] }])));

    [Fact]
    public void L_identifiant_est_relu_tel_quel_et_non_recalcule()
    {
        // L'identifiant d'un groupe privé viendra de sa clé de signature, pas
        // du secret : le codec ne doit pas le recalculer.
        var group = GroupBookTests.Group(Secret, Noon) with { Id = GroupId.FromBytes(new byte[16]) };

        Assert.Equal(group.Id, Assert.Single(GroupBookCodec.Decode(GroupBookCodec.Encode([group]))).Id);
    }

    [Fact]
    public void Un_document_illisible_leve_une_exception_json()
        => Assert.ThrowsAny<JsonException>(() => GroupBookCodec.Decode("pas du json"u8));

    [Fact]
    public void IsValid_distingue_un_document_d_un_dechet()
    {
        Assert.True(GroupBookCodec.IsValid(GroupBookCodec.Encode([])));
        Assert.False(GroupBookCodec.IsValid("pas du json"u8.ToArray()));
    }
}
