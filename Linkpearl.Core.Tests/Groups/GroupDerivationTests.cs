using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Xunit;

namespace Linkpearl.Core.Tests.Groups;

public sealed class GroupDerivationTests
{
    private static readonly byte[] Secret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    private static readonly PlayerFingerprint Alice = PlayerFingerprint.Of("alice", 21);
    private static readonly PlayerFingerprint Bob = PlayerFingerprint.Of("bob", 21);
    private static readonly DateTimeOffset Noon = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Les_vecteurs_figes_sont_retrouves()
    {
        // Calculés hors de ce code, en Python : ce sont eux qui attraperaient
        // une dérive entre deux implémentations du protocole.
        Assert.Equal("e01e458cfd411c4eb2e0c5d509f02995", Alice.ToString());
        Assert.Equal("31e46f1f1b91", GroupDerivation.PresenceAddress(Secret, Alice, Noon).ToString());

        var pair = GroupDerivation.MemberPairSecret(Secret, Alice, Bob);
        Assert.Equal("fd0ebba6aa5cd0fa751986531ed267f714904c16f7e47bce559b34d896041672", Convert.ToHexStringLower(pair));
        Assert.Equal("31474004ac6d8e2d031c1e3553bd693b", GroupDerivation.RuntimeId(pair).ToHex());
        Assert.Equal("630dcd2966c4336691125448bbb25b4f", GroupId.Of(Secret).ToString());
    }

    [Fact]
    public void Le_secret_de_paire_ne_depend_pas_de_qui_le_calcule()
        => Assert.Equal(
            GroupDerivation.MemberPairSecret(Secret, Alice, Bob),
            GroupDerivation.MemberPairSecret(Secret, Bob, Alice));

    [Fact]
    public void Deux_groupes_donnent_deux_secrets_de_paire()
    {
        var other = Secret.Select(b => (byte)(b ^ 0xff)).ToArray();

        Assert.NotEqual(
            GroupDerivation.MemberPairSecret(Secret, Alice, Bob),
            GroupDerivation.MemberPairSecret(other, Alice, Bob));
    }

    [Fact]
    public void Un_membre_ne_se_compose_pas_avec_lui_meme()
        => Assert.Throws<ArgumentException>(() => GroupDerivation.MemberPairSecret(Secret, Alice, Alice));

    [Fact]
    public void La_boite_de_presence_n_est_pas_la_boite_personnelle()
        => Assert.NotEqual(MailboxAddress.Of(Alice, Noon), GroupDerivation.PresenceAddress(Secret, Alice, Noon));

    [Fact]
    public void La_boite_de_presence_tourne_avec_la_fenetre()
    {
        Assert.Equal(
            GroupDerivation.PresenceAddress(Secret, Alice, Noon),
            GroupDerivation.PresenceAddress(Secret, Alice, Noon.AddMinutes(29)));

        Assert.NotEqual(
            GroupDerivation.PresenceAddress(Secret, Alice, Noon),
            GroupDerivation.PresenceAddress(Secret, Alice, Noon.AddMinutes(30)));

        Assert.Equal(
            [GroupDerivation.PresenceAddress(Secret, Alice, Noon), GroupDerivation.PresenceAddress(Secret, Alice, Noon, 1)],
            GroupDerivation.PresenceAround(Secret, Alice, Noon));
    }

    [Fact]
    public void Un_secret_de_mauvaise_taille_est_refuse()
        => Assert.Throws<ArgumentException>(() => GroupDerivation.PresenceAddress(new byte[31], Alice, Noon));

    [Fact]
    public void Les_identifiants_de_groupe_s_ordonnent_comme_leurs_octets()
    {
        var low = GroupId.FromBytes([.. new byte[15], 1]);
        var high = GroupId.FromBytes([1, .. new byte[15]]);

        Assert.True(low.CompareTo(high) < 0);
        Assert.Equal(low, GroupId.FromBytes(low.ToBytes()));
    }
}
