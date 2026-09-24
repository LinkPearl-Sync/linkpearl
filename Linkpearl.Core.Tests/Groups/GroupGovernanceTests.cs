using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Tests.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Groups;

public sealed class GroupGovernanceTests : IDisposable
{
    private readonly ECDsa _owner = CryptoPrimitives.GenerateIdentity();
    private readonly ECDsa _moderator = CryptoPrimitives.GenerateIdentity();
    private readonly MovableClock _clock = new();

    public void Dispose()
    {
        _owner.Dispose();
        _moderator.Dispose();
    }

    private byte[] OwnerPoint => CryptoPrimitives.ExportPublicPoint(_owner);
    private byte[] ModeratorPoint => CryptoPrimitives.ExportPublicPoint(_moderator);

    private (GroupBook Book, GroupId Id) Created(string password = "lune")
    {
        var created = GroupGovernance.Create("Compagnie", password, PolicyFixture.Service, OwnerPoint, _clock.UtcNow);
        var book = new GroupBook(_clock);
        book.Load([created.Record]);
        return (book, created.Record.Id);
    }

    private static void Apply(GroupBook book, GroupId id, byte[] encoded)
        => Assert.Equal(PolicyOffer.Adopted, book.OfferPolicy(id, encoded));

    [Fact]
    public void Un_groupe_cree_est_complet_et_valide()
    {
        var created = GroupGovernance.Create("Compagnie", "lune", PolicyFixture.Service, OwnerPoint, _clock.UtcNow);
        var record = created.Record;

        Assert.Equal(GroupId.Of(record.OwnerKey!), record.Id);
        Assert.Equal(32, record.Secret.Length);
        Assert.NotNull(record.SigningKey);
        Assert.Equal(AdmissionMode.Password, record.Policy!.Attestation.Admission);
        Assert.Equal(created.Code.ToBytes(), record.Policy.Code);
        Assert.Equal(created.Code, GroupGovernance.CodeOf(record));
        Assert.True(GroupPolicyRules.TryAccept(GroupPolicyCodec.Encode(record.Policy), record.Id, record.OwnerKey!, out _, out var why), why);
        Assert.Equal(GroupRole.Owner, GroupGovernance.RoleOf(record, OwnerPoint));

        var open = GroupGovernance.Create("Cercle", "", PolicyFixture.Service, OwnerPoint, _clock.UtcNow);
        Assert.Equal(AdmissionMode.Validation, open.Record.Policy!.Attestation.Admission);
    }

    [Fact]
    public void Le_proprietaire_nomme_un_moderateur_qui_peut_changer_le_mot_de_passe()
    {
        var (book, id) = Created();

        Apply(book, id, GroupGovernance.SetModerators(book.Find(id)!, [ModeratorPoint]));

        var asMember = book.Find(id)! with { SigningKey = null };
        Assert.Equal(GroupRole.Moderator, GroupGovernance.RoleOf(asMember, ModeratorPoint));
        Assert.Equal(2UL, asMember.Policy!.Attestation.Version);

        Apply(book, id, GroupGovernance.SetPassword(asMember, "soleil", _moderator));
        Assert.Equal("soleil", book.Find(id)!.Policy!.Password);

        Apply(book, id, GroupGovernance.NewCode(book.Find(id)! with { SigningKey = null }, _moderator));
        Assert.NotEqual(asMember.Policy.Code, book.Find(id)!.Policy!.Code);
    }

    [Fact]
    public void Un_moderateur_ne_dissout_pas_et_un_inconnu_ne_signe_rien()
    {
        var (book, id) = Created();
        Apply(book, id, GroupGovernance.SetModerators(book.Find(id)!, [ModeratorPoint]));
        var asMember = book.Find(id)! with { SigningKey = null };

        Assert.Throws<InvalidOperationException>(() => GroupGovernance.Dissolve(asMember));
        Assert.Throws<InvalidOperationException>(() => GroupGovernance.SetModerators(asMember, []));

        using var stranger = CryptoPrimitives.GenerateIdentity();
        Assert.Throws<InvalidOperationException>(() => GroupGovernance.Rename(asMember, "Pirate", stranger));
    }

    [Fact]
    public void Exclure_puis_lever_le_bannissement()
    {
        var (book, id) = Created();
        var ban = new GroupBan(PeerId.Of(ModeratorPoint), PlayerFingerprint.Of("mallory", 21));

        Apply(book, id, GroupGovernance.Ban(book.Find(id)!, ban, null));
        Assert.True(book.Find(id)!.Policy!.IsBanned(null, PlayerFingerprint.Of("mallory", 21)));

        Apply(book, id, GroupGovernance.Unban(book.Find(id)!, ban, null));
        Assert.Empty(book.Find(id)!.Policy!.Bans);
    }

    [Fact]
    public void Dissoudre_et_changer_le_mode()
    {
        var (book, id) = Created();

        Apply(book, id, GroupGovernance.SetAdmission(book.Find(id)!, AdmissionMode.Validation));
        Assert.Equal(AdmissionMode.Validation, book.Find(id)!.Policy!.Attestation.Admission);

        Apply(book, id, GroupGovernance.Dissolve(book.Find(id)!));
        Assert.True(book.Find(id)!.Policy!.Dissolved);
    }

    [Fact]
    public void Passer_en_mode_mot_de_passe_sans_mot_de_passe_est_refuse()
    {
        var (book, id) = Created(password: "");

        Assert.Throws<InvalidOperationException>(() => GroupGovernance.SetAdmission(book.Find(id)!, AdmissionMode.Password));
    }

    [Fact]
    public void Une_modification_du_proprietaire_hausse_la_version_d_attestation()
    {
        var (book, id) = Created();
        Assert.Equal(1UL, book.Find(id)!.Policy!.Attestation.Version);

        Apply(book, id, GroupGovernance.Rename(book.Find(id)!, "Nouveau nom", null));

        Assert.Equal(2UL, book.Find(id)!.Policy!.Attestation.Version);
        Assert.Equal("Nouveau nom", book.Find(id)!.Policy!.Name);
    }

    [Fact]
    public void Le_proprietaire_l_emporte_sur_un_moderateur_qui_a_pousse_la_version_du_corps()
    {
        var (book, id) = Created();
        Apply(book, id, GroupGovernance.SetModerators(book.Find(id)!, [ModeratorPoint]));

        var current = book.Find(id)!.Policy!;

        // Mallory, modérateur légitime, publie une politique dont le corps
        // porte la version la plus haute possible, sous l'attestation
        // courante : c'est tout ce qu'un modérateur peut forger, puisqu'il
        // n'a jamais la clé du groupe pour re-signer l'attestation.
        var forged = GroupPolicySigning.Sign(current with { Version = ulong.MaxValue }, _moderator);
        Apply(book, id, GroupPolicyCodec.Encode(forged));
        Assert.Equal(ulong.MaxValue, book.Find(id)!.Policy!.Version);

        // Le propriétaire renomme malgré tout : sa modification hausse la
        // version d'attestation, qui l'emporte avant même de comparer la
        // version du corps.
        Apply(book, id, GroupGovernance.Rename(book.Find(id)!, "Toujours propriétaire", null));
        Assert.Equal("Toujours propriétaire", book.Find(id)!.Policy!.Name);
    }

    [Fact]
    public void Un_moderateur_dont_la_version_est_epuisee_recoit_l_exception_attendue()
    {
        var (book, id) = Created();
        Apply(book, id, GroupGovernance.SetModerators(book.Find(id)!, [ModeratorPoint]));

        var exhausted = book.Find(id)!.Policy! with { Version = ulong.MaxValue };
        var group = book.Find(id)! with { SigningKey = null, Policy = GroupPolicySigning.Sign(exhausted, _owner) };

        var exception = Assert.Throws<InvalidOperationException>(
            () => GroupGovernance.Rename(group, "Peu importe", _moderator));
        Assert.Equal("version de politique épuisée : le propriétaire doit intervenir", exception.Message);
    }

    [Fact]
    public void Le_proprietaire_bannit_un_moderateur_qui_disparait_de_l_attestation()
    {
        var (book, id) = Created();
        Apply(book, id, GroupGovernance.SetModerators(book.Find(id)!, [ModeratorPoint]));

        var fingerprint = PlayerFingerprint.Of("mallory-le-moderateur", 21);
        var ban = new GroupBan(PeerId.Of(ModeratorPoint), fingerprint);

        Apply(book, id, GroupGovernance.Ban(book.Find(id)!, ban, null));

        var policy = book.Find(id)!.Policy!;
        Assert.DoesNotContain(policy.Attestation.Moderators, key => key.AsSpan().SequenceEqual(CryptoPrimitives.Compress(ModeratorPoint)));
        Assert.Contains(policy.Bans, existing => existing == ban);
        Assert.True(policy.IsBanned(PeerId.Of(ModeratorPoint), fingerprint));
    }

    [Fact]
    public void Promouvoir_un_pair_banni_par_cle_leve_son_bannissement()
    {
        var (book, id) = Created();

        var ban = new GroupBan(PeerId.Of(ModeratorPoint), null);
        Apply(book, id, GroupGovernance.Ban(book.Find(id)!, ban, null));
        Assert.Single(book.Find(id)!.Policy!.Bans);

        Apply(book, id, GroupGovernance.SetModerators(book.Find(id)!, [ModeratorPoint]));

        Assert.Empty(book.Find(id)!.Policy!.Bans);
        Assert.True(book.Find(id)!.Policy!.IsModerator(CryptoPrimitives.Compress(ModeratorPoint)));
    }
}
