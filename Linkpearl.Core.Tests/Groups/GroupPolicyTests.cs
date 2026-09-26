using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Groups;

/// <summary>Fabrique des politiques signées pour les tests.</summary>
internal static class PolicyFixture
{
    public static readonly RendezvousAddress Service = new("rdv.exemple.ch", 47900);

    public static byte[] Compressed(ECDsa key) => GroupPolicySigning.CompressedKey(key);

    public static GroupAttestation Attestation(
        ECDsa groupKey, ECDsa ownerMember, IReadOnlyList<ECDsa>? moderators = null,
        ulong version = 1, byte admission = AdmissionMode.Password)
        => GroupPolicySigning.SignAttestation(
            new GroupAttestation(
                GroupId.Of(Compressed(groupKey)), version, admission, Compressed(ownerMember),
                [.. (moderators ?? []).Select(Compressed)], []),
            groupKey);

    public static GroupPolicy Policy(
        ECDsa groupKey, GroupAttestation attestation, ECDsa signer, ulong version = 1,
        string name = "Compagnie", string password = "lune", IReadOnlyList<GroupBan>? bans = null,
        bool dissolved = false)
        => GroupPolicySigning.Sign(
            new GroupPolicy(
                GroupId.Of(Compressed(groupKey)), version, name, [1, 2, 3, 4, 5, 6], [Service], password,
                bans ?? [], dissolved, attestation, [], []),
            signer);
}

public sealed class GroupPolicyTests : IDisposable
{
    private readonly ECDsa _group = CryptoPrimitives.GenerateIdentity();
    private readonly ECDsa _owner = CryptoPrimitives.GenerateIdentity();
    private readonly ECDsa _moderator = CryptoPrimitives.GenerateIdentity();
    private readonly ECDsa _stranger = CryptoPrimitives.GenerateIdentity();

    public void Dispose()
    {
        _group.Dispose();
        _owner.Dispose();
        _moderator.Dispose();
        _stranger.Dispose();
    }

    private GroupId Id => GroupId.Of(PolicyFixture.Compressed(_group));

    private byte[] GroupKey => PolicyFixture.Compressed(_group);

    private GroupAttestation Attested(ulong version = 1, byte admission = AdmissionMode.Password)
        => PolicyFixture.Attestation(_group, _owner, [_moderator], version, admission);

    private bool Accepts(GroupPolicy policy, out string? why)
        => GroupPolicyRules.TryAccept(GroupPolicyCodec.Encode(policy), Id, GroupKey, out _, out why);

    private static PeerId PeerOf(ECDsa key) => PeerId.Of(CryptoPrimitives.ExportPublicPoint(key));

    [Fact]
    public void Une_politique_fait_l_aller_retour_a_l_octet_pres()
    {
        var policy = PolicyFixture.Policy(_group, Attested(), _group,
            bans: [new GroupBan(PeerOf(_stranger), PlayerFingerprint.Of("mallory", 21)), new GroupBan(null, PlayerFingerprint.Of("eve", 21))]);

        var encoded = GroupPolicyCodec.Encode(policy);

        Assert.True(GroupPolicyCodec.TryDecode(encoded, out var decoded, out var why), why);
        Assert.Equal(encoded, GroupPolicyCodec.Encode(decoded!));
        Assert.Equal("Compagnie", decoded!.Name);
        Assert.Equal(2, decoded.Bans.Count);
        Assert.Single(decoded.Attestation.Moderators);
    }

    [Fact]
    public void Le_proprietaire_et_un_moderateur_signent_valablement()
    {
        Assert.True(Accepts(PolicyFixture.Policy(_group, Attested(), _group), out var why1), why1);
        Assert.True(Accepts(PolicyFixture.Policy(_group, Attested(), _moderator, version: 2), out var why2), why2);
    }

    [Fact]
    public void Un_inconnu_ne_signe_pas()
        => Assert.False(Accepts(PolicyFixture.Policy(_group, Attested(), _stranger), out _));

    [Fact]
    public void Une_attestation_forgee_est_refusee()
    {
        // Mallory s'inscrit lui-même comme modérateur et signe l'attestation
        // de sa propre clé : c'est exactement ce que l'attestation empêche.
        var forged = GroupPolicySigning.SignAttestation(
            new GroupAttestation(Id, 9, AdmissionMode.Password, PolicyFixture.Compressed(_owner),
                [PolicyFixture.Compressed(_stranger)], []),
            _stranger);

        Assert.False(Accepts(PolicyFixture.Policy(_group, forged, _stranger), out _));
    }

    [Fact]
    public void Un_octet_modifie_invalide_la_signature()
    {
        var encoded = GroupPolicyCodec.Encode(PolicyFixture.Policy(_group, Attested(), _group));
        encoded[20] ^= 0x01;   // dans la version

        Assert.False(GroupPolicyRules.TryAccept(encoded, Id, GroupKey, out _, out _));
    }

    [Fact]
    public void Un_moderateur_ne_dissout_pas_et_ne_bannit_ni_moderateur_ni_proprietaire()
    {
        Assert.False(Accepts(PolicyFixture.Policy(_group, Attested(), _moderator, dissolved: true), out _));
        Assert.False(Accepts(PolicyFixture.Policy(_group, Attested(), _moderator, bans: [new GroupBan(PeerOf(_moderator), null)]), out _));
        Assert.False(Accepts(PolicyFixture.Policy(_group, Attested(), _moderator, bans: [new GroupBan(PeerOf(_owner), null)]), out _));
    }

    [Fact]
    public void Personne_ne_bannit_par_cle_le_proprietaire_ni_un_moderateur()
    {
        Assert.False(Accepts(PolicyFixture.Policy(_group, Attested(), _group, bans: [new GroupBan(PeerOf(_owner), null)]), out _));
        Assert.False(Accepts(PolicyFixture.Policy(_group, Attested(), _group, bans: [new GroupBan(PeerOf(_moderator), null)]), out _));
        Assert.True(Accepts(PolicyFixture.Policy(_group, Attested(), _group, bans: [new GroupBan(PeerOf(_stranger), null)]), out var why), why);
    }

    [Fact]
    public void Le_mode_mot_de_passe_exige_un_mot_de_passe()
    {
        Assert.False(Accepts(PolicyFixture.Policy(_group, Attested(), _group, password: ""), out _));
        Assert.True(Accepts(PolicyFixture.Policy(_group, Attested(admission: AdmissionMode.Validation), _group, password: ""), out var why), why);
    }

    [Fact]
    public void Une_politique_d_un_autre_groupe_est_refusee()
    {
        using var other = CryptoPrimitives.GenerateIdentity();
        var foreign = PolicyFixture.Policy(other, PolicyFixture.Attestation(other, _owner), other);

        Assert.False(GroupPolicyRules.TryAccept(GroupPolicyCodec.Encode(foreign), Id, GroupKey, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Un nom de groupe bien trop long pour tenir")]
    [InlineData("Ligne\nbrisée")]
    public void Un_nom_hors_regles_est_refuse(string name)
        => Assert.False(GroupPolicyCodec.IsValidName(name));

    [Theory]
    [InlineData("Les Mogs")]
    [InlineData("Mogs!")]
    [InlineData("Mogs@Ravana")]
    [InlineData("Mogs.Club")]
    public void Un_nouveau_nom_n_a_ni_espace_ni_caractere_special(string name)
        => Assert.False(GroupPolicyCodec.IsCreatableName(name));

    [Theory]
    [InlineData("LesMogs")]
    [InlineData("Łódź")]
    [InlineData("Club-des-Mogs")]
    [InlineData("mogs_42")]
    public void Un_nouveau_nom_garde_lettres_accents_chiffres_et_tirets(string name)
        => Assert.True(GroupPolicyCodec.IsCreatableName(name));

    [Fact]
    public void Un_ancien_nom_avec_espace_reste_accepte_a_la_reception()
    {
        // Resserrer la réception ferait rejeter par chaque membre la politique
        // des groupes nés avant la règle, qui cesseraient de fonctionner.
        Assert.True(GroupPolicyCodec.IsValidName("Les Mogs"));
    }

    [Fact]
    public void Les_bornes_sont_appliquees_au_decodage()
    {
        var tooManyBans = Enumerable.Range(0, GroupPolicyCodec.MaxBans + 1)
            .Select(i => new GroupBan(null, PlayerFingerprint.Of($"b{i}", 21))).ToList();
        var encoded = GroupPolicyCodec.Encode(PolicyFixture.Policy(_group, Attested(), _group, bans: tooManyBans));

        Assert.False(GroupPolicyCodec.TryDecode(encoded, out _, out _));

        var valid = GroupPolicyCodec.Encode(PolicyFixture.Policy(_group, Attested(), _group));
        Assert.False(GroupPolicyCodec.TryDecode([.. valid, 0x00], out _, out _));                    // octet en trop
        Assert.False(GroupPolicyCodec.TryDecode(valid.AsSpan(0, valid.Length - 1), out _, out _));   // tronquée
        Assert.False(GroupPolicyCodec.TryDecode([], out _, out _));
    }

    [Fact]
    public void L_ordre_suit_l_attestation_puis_la_version_puis_la_signature()
    {
        var v1 = PolicyFixture.Policy(_group, Attested(), _group, version: 5);
        var v2 = PolicyFixture.Policy(_group, Attested(), _group, version: 6);
        var newerAttestation = PolicyFixture.Policy(_group, Attested(version: 2), _group, version: 1);

        Assert.True(GroupPolicyRules.IsNewer(v1, null));
        Assert.True(GroupPolicyRules.IsNewer(v2, v1));
        Assert.False(GroupPolicyRules.IsNewer(v1, v2));
        Assert.True(GroupPolicyRules.IsNewer(newerAttestation, v2));
        Assert.False(GroupPolicyRules.IsNewer(v2, newerAttestation));

        var a = PolicyFixture.Policy(_group, Attested(), _group, version: 7, name: "Alpha");
        var b = PolicyFixture.Policy(_group, Attested(), _moderator, version: 7, name: "Beta");
        Assert.NotEqual(GroupPolicyRules.IsNewer(a, b), GroupPolicyRules.IsNewer(b, a));
        Assert.False(GroupPolicyRules.IsNewer(a, a));
    }

    [Fact]
    public void Une_dissolution_l_emporte_toujours_sur_une_version_plus_haute()
    {
        var dissolved = PolicyFixture.Policy(_group, Attested(), _group, version: 1, dissolved: true);
        var higherNotDissolved = PolicyFixture.Policy(_group, Attested(), _group, version: 99, dissolved: false);

        Assert.False(GroupPolicyRules.IsNewer(higherNotDissolved, dissolved));
        Assert.True(GroupPolicyRules.IsNewer(dissolved, higherNotDissolved));
    }

    [Fact]
    public void Un_moderateur_reste_protege_meme_banni_par_empreinte_seule()
    {
        var fingerprint = PlayerFingerprint.Of("alt-du-moderateur", 21);
        var policy = PolicyFixture.Policy(_group, Attested(), _group, bans: [new GroupBan(null, fingerprint)]);

        Assert.True(Accepts(policy, out var why), why);
        Assert.False(policy.IsBanned(PeerOf(_moderator), fingerprint));
        Assert.True(policy.IsBanned(null, fingerprint));
    }

    [Fact]
    public void Deux_signatures_du_meme_contenu_ne_departagent_pas()
    {
        var unsigned = new GroupPolicy(
            Id, 3, "Compagnie", [1, 2, 3, 4, 5, 6], [PolicyFixture.Service], "lune", [], false, Attested(), [], []);

        var a = GroupPolicySigning.Sign(unsigned, _group);
        var b = GroupPolicySigning.Sign(unsigned, _group);

        Assert.NotEqual(a.Signature, b.Signature);
        Assert.False(GroupPolicyRules.IsNewer(a, b));
        Assert.False(GroupPolicyRules.IsNewer(b, a));
    }

    [Fact]
    public void Un_encodage_non_canonique_est_refuse()
    {
        var canonicalPolicy = new GroupPolicy(
            Id, 1, "Compagnie", [1, 2, 3, 4, 5, 6], [new RendezvousAddress("rdv.exemple.ch", 47900)], "lune",
            [], false, Attested(), [], []);
        var encoded = GroupPolicyCodec.Encode(GroupPolicySigning.Sign(canonicalPolicy, _group));

        // Repère l'adresse encodée : format (1) + groupe (16) + version (8) + nom (1 + 9, « Compagnie »)
        // + code (6) + n (1). Le service canonique tient dans « rdv.exemple.ch », sans port explicite.
        var addressLengthOffset = 1 + GroupId.SizeInBytes + 8 + (1 + 9) + 6 + 1;
        var addressLength = encoded[addressLengthOffset];
        var addressText = System.Text.Encoding.UTF8.GetString(encoded, addressLengthOffset + 1, addressLength);
        Assert.Equal("rdv.exemple.ch", addressText);

        var explicitPort = System.Text.Encoding.UTF8.GetBytes("rdv.exemple.ch:47900");
        var mutated = new byte[encoded.Length + (explicitPort.Length - addressLength)];

        encoded.AsSpan(0, addressLengthOffset).CopyTo(mutated);
        mutated[addressLengthOffset] = checked((byte)explicitPort.Length);
        explicitPort.CopyTo(mutated.AsSpan(addressLengthOffset + 1));
        encoded.AsSpan(addressLengthOffset + 1 + addressLength).CopyTo(mutated.AsSpan(addressLengthOffset + 1 + explicitPort.Length));

        Assert.False(GroupPolicyCodec.TryDecode(mutated, out _, out var why));
        Assert.Equal("politique non canonique", why);
    }

    [Fact]
    public void Une_cle_de_groupe_qui_ne_donne_pas_l_identifiant_attendu_est_refusee()
    {
        using var other = CryptoPrimitives.GenerateIdentity();
        var encoded = GroupPolicyCodec.Encode(PolicyFixture.Policy(_group, Attested(), _group));

        Assert.False(GroupPolicyRules.TryAccept(encoded, Id, PolicyFixture.Compressed(other), out _, out var why));
        Assert.Equal("clé de groupe qui ne donne pas cet identifiant", why);
    }

    [Fact]
    public void Un_moderateur_retire_par_with_n_est_plus_protege()
    {
        var moderatorPeer = PeerOf(_moderator);
        var policy = PolicyFixture.Policy(_group, Attested(), _group);

        // Remplit ce qui aurait été un cache, si GroupPolicy en tenait un.
        Assert.False(policy.IsBanned(moderatorPeer, null));
        Assert.True(policy.IsProtected(moderatorPeer));

        var withoutModerator = policy with { Attestation = policy.Attestation with { Moderators = [] } };

        Assert.False(withoutModerator.IsProtected(moderatorPeer));
    }

    [Fact]
    public void Un_moderateur_ne_ressuscite_pas_un_groupe_dissous()
    {
        var dissolved = PolicyFixture.Policy(_group, Attested(), _group, version: 10, dissolved: true);
        var revived = PolicyFixture.Policy(_group, Attested(), _moderator, version: 15, dissolved: false);

        Assert.False(GroupPolicyRules.IsNewer(revived, dissolved));
    }
}
