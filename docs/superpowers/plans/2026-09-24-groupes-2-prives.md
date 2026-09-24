# Groupes, incrément 2 : les groupes privés

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Un joueur crée un groupe, en donne le code par /tell, un autre le rejoint (mot de passe ou validation par un modérateur), et le propriétaire et ses modérateurs gouvernent le groupe par une politique signée, le tout depuis une page « Groupes » de l'interface.

**Architecture:** Tout ce qui décide vit dans `Core/Groups` et se teste sous Linux : la politique signée (`GroupPolicy`, son codec canonique et ses règles), les dépôts d'admission (`AdmissionCodec`, `AdmissionSealing`), la gouvernance (`GroupGovernance`), l'hôte et le candidat d'admission (`AdmissionHost`, `AdmissionCandidate`). Le moteur propage la politique de session en session (`MessageKind.GroupPolicy`). L'adaptateur (`PresenceService`, `Plugin`) ne fait que brancher ces pièces sur les boîtes aux lettres, et l'interface sur `GroupBook`.

**Tech Stack:** C# (.NET 10), xUnit, System.Security.Cryptography (ECDSA P-256, ECDH, HKDF, AES-GCM), ImGui (Dalamud) pour l'interface.

**Spec:** `docs/superpowers/specs/2026-09-24-groupes-design.md`. Incrément 1 livré : `docs/superpowers/plans/2026-09-24-groupes-1-noyau.md` (branche `worktree-groupes-noyau`, PR #20). L'incrément 3 (Public et bannissements du service) a son propre plan.

## Global Constraints

- `Linkpearl/Core/` ne référence jamais Dalamud (`ArchitectureTests`).
- Aucun nom de personnage ni chemin local complet dans les journaux par défaut. Un nom peut apparaître dans un message au joueur (`Report`), jamais dans `Log`.
- Pas de tiret cadratin (U+2014), nulle part. Commentaires en français, qui disent le pourquoi.
- `record` et `readonly record struct` pour les données ; jamais d'`enum` pour un octet qui traverse le réseau (constantes `byte` validées, comme `MessageKind`). Un `enum` strictement local est admis.
- Toute donnée venue du réseau est décodée par un `TryDecode` qui ne lève jamais et rejette le message entier sur une seule règle violée.
- Commits Conventional Commits, sujet en français, **sans** ligne `Co-Authored-By`, `Claude-Session` ni mention « Generated with ».
- `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj` et `dotnet build Linkpearl/Linkpearl.csproj -c Release` (sans warning) passent après chaque tâche.
- Bornes : dépôt de boîte 512 octets (`RendezvousWire.MaxDepositLength`), nom de groupe 1 à 32 caractères, mot de passe 64 octets UTF-8, 4 services, 256 bannis, 16 modérateurs, 5 échecs de mot de passe par fenêtre de 30 minutes et par candidat, redépôt chaque minute pendant 10 minutes.
- Étiquettes : `"linkpearl:group-join:v1"`, `"linkpearl:group-admit:v1:proof"`, `"linkpearl:group-admit:v1:welcome"`, `"linkpearl:group-attest:v1"`, `"linkpearl:group-policy:v1"`.

## Décisions prises en écrivant ce plan (rulings)

1. **Attestation des modérateurs signée par le groupe, embarquée dans chaque politique.** La spec disait « un modérateur de la version précédente connue ». Or un membre qui vient d'entrer n'en connaît aucune, et pourrait accepter une politique forgée par quelqu'un qui s'inscrit lui-même comme modérateur. Chaque politique porte donc l'attestation (propriétaire, modérateurs, mode d'admission), signée par la clé du groupe, et se vérifie seule. L'ordre est (version d'attestation, version, SHA-256 de la signature le plus petit) : un modérateur retiré ne peut pas reprendre la main avec une ancienne attestation. Coût si erroné : format de politique à revoir avant diffusion, rien n'est encore publié.
2. **L'attestation nomme la clé d'identité du propriétaire membre**, distincte de la clé du groupe. Elle sert à interdire qu'on bannisse le propriétaire et à afficher son rôle. La clé du groupe est tirée à la création, ne sert jamais d'identité, et `GroupId = SHA-256(clé du groupe compressée)[0..16]`.
3. **Chaque membre en ligne défie**, et le candidat retient le premier défi. La spec voulait qu'un membre s'abstienne s'il voyait passer un autre défi, mais les défis vont dans la boîte personnelle du candidat, que les membres ne voient pas. Le délai aléatoire de 0 à 2 s ne sert donc à rien et disparaît.
4. **Pas de `MessageKind.GroupCard` (0x11).** Le nom d'un membre vient déjà du jeu (`GroupSighting.DisplayName`) et son rôle se déduit de la politique. Ce message ne porterait rien de neuf.
5. **Le propriétaire ne « quitte » pas : il dissout.** Un groupe dissous reste dans sa liste jusqu'à ce qu'il le retire, pour que la politique dissoute atteigne les membres qu'il croisera encore. Un membre qui la reçoit quitte le groupe et le dit.
6. **Le code contient le service.** `InvitationTicketText` (`XXXXXXXXXXXX@service`) ; affiché par groupes de 4 caractères pour la lecture, accepté avec ou sans tirets.

## Review Focus

1. **Un candidat qui redépose sa demande chaque minute ne doit pas déclencher un second défi, ni réapparaître dans les demandes après un refus.** Tests : `AdmissionTests.Une_demande_redeposee_ne_redefie_pas`, `AdmissionTests.Une_demande_refusee_ne_revient_pas` (tâche 5).
2. **Une politique plus ancienne reçue d'un pair ne doit jamais écraser la nôtre, et la nôtre doit lui être renvoyée.** Tests : `GroupPolicySyncTests` (tâche 6).
3. **Un modérateur ne doit pas pouvoir dissoudre, changer les modérateurs ni bannir un modérateur ou le propriétaire.** Tests : `GroupPolicyTests` et `GroupGovernanceTests` (tâches 1 et 4).
4. **Une sauvegarde v1 doit toujours se relire** après le passage à la v2. Test : `IdentityBackupTests.Une_sauvegarde_v1_se_relit` (tâche 7).
5. **Un dépôt vers une boîte fermée ne doit plus faire échouer l'interrogation de présence en cours.** Correctif dans `RendezvousClient.ListenAsync` (tâche 8), vérifié par un test si l'infrastructure de test du client le permet, sinon par relecture.

---

## Fichiers

| Fichier | Rôle |
|---|---|
| `Linkpearl/Core/Groups/GroupPolicy.cs` (nouveau) | `AdmissionMode`, `GroupBan`, `GroupAttestation`, `GroupPolicy` |
| `Linkpearl/Core/Groups/GroupPolicyCodec.cs` (nouveau) | Encodage canonique, décodage borné, noms valides |
| `Linkpearl/Core/Groups/GroupPolicyRules.cs` (nouveau) | Vérification des signatures et des règles, ordre entre versions, signature |
| `Linkpearl/Core/Groups/AdmissionMessages.cs` (nouveau) | Dépôts `0x05` à `0x09`, `AdmissionCodec`, `AdmissionSealing`, `GroupGrant` |
| `Linkpearl/Core/Groups/GroupGovernance.cs` (nouveau) | Création, rôles, modifications signées |
| `Linkpearl/Core/Groups/AdmissionHost.cs` (nouveau) | Côté membre : défis, preuves, validations |
| `Linkpearl/Core/Groups/AdmissionCandidate.cs` (nouveau) | Côté candidat : demande, redépôts, preuve, bienvenue |
| `Linkpearl/Core/Groups/GroupDerivation.cs` | `AdmissionAddress`, `AdmissionAround` |
| `Linkpearl/Core/Groups/GroupRecord.cs` | `OwnerKey`, `SigningKey`, `Policy` ; `GroupMember.PublicKey` |
| `Linkpearl/Core/Groups/GroupBook.cs` | Bannis refusés, `IGroupPolicies`, `OfferPolicy`, `PolicyAdopted` |
| `Linkpearl/Core/Groups/GroupBookCodec.cs` | Nouveaux champs en fin d'enregistrement |
| `Linkpearl/Core/Groups/GroupDialPlanner.cs` | Bannis exclus |
| `Linkpearl/Core/Protocol/MessageKind.cs` | `GroupPolicy = 0x10` |
| `Linkpearl/Core/Sync/SyncEngine.cs` | Propagation de la politique, `PeerStatus.Group` |
| `Linkpearl/Core/Identity/IdentityBackup.cs` | Version 2 avec les groupes |
| `Linkpearl/Integration/IdentityBackupService.cs` | Collecte et restauration de `groups.json` |
| `Linkpearl/Core/Transport/Rendezvous/RendezvousClient.cs` | « destinataire absent » n'interrompt plus la présence |
| `Linkpearl/Integration/PresenceService.cs` | Aiguillage des dépôts, boîtes d'admission, redépôts |
| `Linkpearl/Plugin.cs` | Actions de groupe, politique adoptée, retrait de `/lpgroupe` |
| `Linkpearl/Ui/GroupActions.cs` (nouveau) | Délégués entre l'interface et le plugin |
| `Linkpearl/Ui/Pages/GroupsPage.cs` (nouveau) | La page Groupes |
| `Linkpearl/Ui/Pages/RequestsPage.cs`, `RequestToasts.cs`, `MainWindow.cs`, `Icons.cs`, `NameplateGlyphs.cs`, `NameplateLegend.cs`, `Core/Sync/NameplateMark.cs`, `Integration/StatusBarEntry.cs` | Demandes d'admission, symbole de membre de groupe |
| `docs/protocol.md`, `docs/pairage.md`, `docs/reprise.md`, `README.md`, `README.fr.md` | Documentation |

Tests : `Linkpearl.Core.Tests/Groups/` et `Linkpearl.Core.Tests/Sync/`. Les tests compilent les sources du noyau et voient ses membres `internal`.

Vecteurs figés, calculés en Python (hashlib, hmac) :

| Donnée | Valeur |
|---|---|
| `AdmissionAddress(code = 01 02 03 04 05 06, 2026-09-22 12:00 UTC)` (fenêtre 994488) | `e5153db1e51e` |
| `AdmissionSealing.DeriveKey(partagé = 0x00..0x1f, aléa = 0x64..0x6f, "proof")` | `8341723f2280566dcb34344d5eded1c5f25eb0287a702e68c529c3067fa1b482` |
| idem, `"welcome"` | `657e3195b920ee89df8d42bf96d75de2a3ab0f79a807a4e4305d1db51250c0ac` |

---

### Task 1: La politique signée

**Files:**
- Create: `Linkpearl/Core/Groups/GroupPolicy.cs`
- Create: `Linkpearl/Core/Groups/GroupPolicyCodec.cs`
- Create: `Linkpearl/Core/Groups/GroupPolicyRules.cs`
- Test: `Linkpearl.Core.Tests/Groups/GroupPolicyTests.cs`

**Interfaces:**
- Consumes: `GroupId`, `PeerId`, `PlayerFingerprint`, `RendezvousAddress`, `InvitationTicket.SizeInBytes`, `CryptoPrimitives` (`Sign`, `Verify`, `ImportVerifier`, `Compress`, `Decompress`, `ExportPublicPoint`).
- Produces:
  - `static class AdmissionMode { const byte Password = 0x01; const byte Validation = 0x02; static bool IsKnown(byte) }`
  - `sealed record GroupBan(PeerId? Peer, PlayerFingerprint? Fingerprint)` avec `bool Matches(PeerId?, PlayerFingerprint?)`
  - `sealed record GroupAttestation(GroupId Group, ulong Version, byte Admission, byte[] Owner, IReadOnlyList<byte[]> Moderators, byte[] Signature)` (clés compressées de 33 octets)
  - `sealed record GroupPolicy(GroupId Group, ulong Version, string Name, byte[] Code, IReadOnlyList<RendezvousAddress> Rendezvous, string Password, IReadOnlyList<GroupBan> Bans, bool Dissolved, GroupAttestation Attestation, byte[] Signer, byte[] Signature)` avec `bool IsBanned(PeerId?, PlayerFingerprint?)` et `bool IsModerator(ReadOnlySpan<byte> compressedKey)`
  - `static class GroupPolicyCodec` : constantes de bornes, `Encode`, `EncodeAttestation`, `SignedPortion`, `AttestationSignedPortion`, `TryDecode`, `IsValidName`
  - `static class GroupPolicyRules` : `TryAccept(ReadOnlySpan<byte> encoded, GroupId expected, ReadOnlySpan<byte> groupKey, out GroupPolicy?, out string?)`, `IsNewer(GroupPolicy candidate, GroupPolicy? current)`
  - `static class GroupPolicySigning` : `SignAttestation(GroupAttestation, ECDsa groupKey)`, `Sign(GroupPolicy, ECDsa signer)`, `CompressedKey(ECDsa)`
  - Aide de test `internal static class PolicyFixture` (dans le fichier de test), réutilisée par les tâches 3 à 6.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
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
    public void Personne_ne_bannit_le_proprietaire_mais_il_peut_bannir_un_moderateur()
    {
        Assert.False(Accepts(PolicyFixture.Policy(_group, Attested(), _group, bans: [new GroupBan(PeerOf(_owner), null)]), out _));
        Assert.True(Accepts(PolicyFixture.Policy(_group, Attested(), _group, bans: [new GroupBan(PeerOf(_moderator), null)]), out var why), why);
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
}
```

- [ ] **Step 2: Vérifier qu'ils échouent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupPolicyTests`
Expected: échec de compilation, `GroupPolicy` et consorts inconnus.

- [ ] **Step 3: Écrire `GroupPolicy.cs`**

```csharp
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>
/// Comment un candidat entre dans un groupe.
/// </summary>
/// <remarks>
/// Des constantes et non une énumération : l'octet voyage dans la politique, et
/// tout ce qui vient du réseau se valide explicitement.
/// </remarks>
public static class AdmissionMode
{
    /// <summary>N'importe quel membre en ligne admet qui connaît le mot de passe.</summary>
    public const byte Password = 0x01;

    /// <summary>Seuls le propriétaire et les modérateurs admettent, un par un.</summary>
    public const byte Validation = 0x02;

    public static bool IsKnown(byte mode) => mode is Password or Validation;
}

/// <summary>Un bannissement : une clé, un personnage, ou les deux.</summary>
/// <remarks>
/// Les deux, d'ordinaire : bannir la seule clé laisserait revenir le même
/// personnage sous une identité neuve, bannir le seul personnage laisserait la
/// même personne revenir sous un autre.
/// </remarks>
public sealed record GroupBan(PeerId? Peer, PlayerFingerprint? Fingerprint)
{
    public bool Matches(PeerId? peer, PlayerFingerprint? fingerprint)
        => (Peer is { } banned && peer == banned) || (Fingerprint is { } character && fingerprint == character);
}

/// <summary>
/// Ce que seul le propriétaire décide, signé par la clé du groupe.
/// </summary>
/// <remarks>
/// Embarquée dans chaque politique pour qu'un membre qui vient d'entrer puisse
/// vérifier un modérateur sans rien connaître de l'historique : sans elle,
/// quiconque pourrait publier une politique où il se nomme lui-même.
/// </remarks>
/// <param name="Owner">La clé d'identité du propriétaire en tant que membre, compressée.</param>
/// <param name="Moderators">Les clés d'identité des modérateurs, compressées.</param>
public sealed record GroupAttestation(
    GroupId Group, ulong Version, byte Admission, byte[] Owner, IReadOnlyList<byte[]> Moderators, byte[] Signature);

/// <summary>Ce que le groupe dit de lui-même, signé par le groupe ou par un modérateur.</summary>
/// <param name="Code">Le ticket courant, 6 octets.</param>
/// <param name="Signer">La clé compressée du signataire : celle du groupe, ou celle d'un modérateur.</param>
public sealed record GroupPolicy(
    GroupId Group, ulong Version, string Name, byte[] Code, IReadOnlyList<RendezvousAddress> Rendezvous,
    string Password, IReadOnlyList<GroupBan> Bans, bool Dissolved, GroupAttestation Attestation,
    byte[] Signer, byte[] Signature)
{
    public bool IsBanned(PeerId? peer, PlayerFingerprint? fingerprint)
        => Bans.Any(ban => ban.Matches(peer, fingerprint));

    public bool IsModerator(ReadOnlySpan<byte> compressedKey)
    {
        foreach (var moderator in Attestation.Moderators)
            if (moderator.AsSpan().SequenceEqual(compressedKey))
                return true;

        return false;
    }
}
```

- [ ] **Step 4: Écrire `GroupPolicyCodec.cs`**

```csharp
using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>
/// L'encodage canonique d'une politique, celui qui est signé.
/// </summary>
/// <remarks>
/// Binaire et non JSON : deux membres doivent produire les mêmes octets pour
/// la même politique, sans quoi aucune signature ne se vérifierait. Entiers en
/// gros-boutiste, textes précédés d'un octet de longueur.
///
/// Disposition :
/// <code>
/// attestation : format (1) | groupe (16) | version (8) | admission (1) | propriétaire (33)
///               | n (1) | n × modérateur (33) | signature (64)
/// politique   : format (1) | groupe (16) | version (8) | nom (1+) | code (6)
///               | n (1) | n × service (1+) | mot de passe (1+) | n (2) | n × banni
///               | dissous (1) | taille (2) | attestation | signataire (33) | signature (64)
/// banni       : drapeaux (1, 1 = clé, 2 = personnage) | clé (16)? | personnage (16)?
/// </code>
/// </remarks>
public static class GroupPolicyCodec
{
    public const byte Format = 0x01;
    public const int MaxNameChars = 32;
    public const int MaxNameBytes = 128;
    public const int MaxRendezvous = 4;
    public const int MaxRendezvousLength = 255;
    public const int MaxPasswordBytes = 64;
    public const int MaxBans = 256;
    public const int MaxModerators = 16;

    /// <summary>Largement au-dessus du pire cas (environ 11 Kio), assez bas pour qu'un pair ne nous fasse rien allouer d'absurde.</summary>
    public const int MaxEncodedLength = 16 * 1024;

    private const byte BanPeer = 0x01;
    private const byte BanFingerprint = 0x02;
    private const int KeyLength = CryptoPrimitives.CompressedPointLength;
    private const int SignatureLength = CryptoPrimitives.SignatureLength;
    private const int CodeLength = InvitationTicket.SizeInBytes;

    private static ReadOnlySpan<byte> AttestationLabel => "linkpearl:group-attest:v1"u8;
    private static ReadOnlySpan<byte> PolicyLabel => "linkpearl:group-policy:v1"u8;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Un nom de groupe : 1 à 32 caractères, sans caractère de contrôle, pas seulement des espaces.</summary>
    public static bool IsValidName(string? name)
        => name is { Length: > 0 and <= MaxNameChars }
           && string.IsNullOrWhiteSpace(name) is false
           && name.Any(char.IsControl) is false
           && Encoding.UTF8.GetByteCount(name) <= MaxNameBytes;

    public static byte[] Encode(GroupPolicy policy)
    {
        var writer = new ArrayBufferWriter<byte>();
        WritePolicyBody(writer, policy);
        writer.Write(policy.Signature);
        return writer.WrittenSpan.ToArray();
    }

    public static byte[] EncodeAttestation(GroupAttestation attestation)
    {
        var writer = new ArrayBufferWriter<byte>();
        WriteAttestationBody(writer, attestation);
        writer.Write(attestation.Signature);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>Ce que signe le signataire de la politique : l'étiquette, puis tout sauf sa signature.</summary>
    public static byte[] SignedPortion(GroupPolicy policy)
    {
        var writer = new ArrayBufferWriter<byte>();
        writer.Write(PolicyLabel);
        WritePolicyBody(writer, policy);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>Ce que signe la clé du groupe dans l'attestation.</summary>
    public static byte[] AttestationSignedPortion(GroupAttestation attestation)
    {
        var writer = new ArrayBufferWriter<byte>();
        writer.Write(AttestationLabel);
        WriteAttestationBody(writer, attestation);
        return writer.WrittenSpan.ToArray();
    }

    public static bool TryDecode(ReadOnlySpan<byte> encoded, out GroupPolicy? policy, out string? rejection)
    {
        policy = null;

        if (encoded.Length > MaxEncodedLength)
            return Refuse("politique démesurée", out rejection);

        var reader = new Reader(encoded);

        if (reader.TryByte(out var format) is false || format != Format)
            return Refuse("format de politique inconnu", out rejection);

        if (reader.TryTake(GroupId.SizeInBytes, out var group) is false || reader.TryU64(out var version) is false)
            return Refuse("politique tronquée", out rejection);

        if (reader.TryText(MaxNameBytes, out var name) is false || IsValidName(name) is false)
            return Refuse("nom de groupe hors règles", out rejection);

        if (reader.TryTake(CodeLength, out var code) is false)
            return Refuse("politique tronquée", out rejection);

        if (reader.TryByte(out var placeCount) is false || placeCount is 0 or > MaxRendezvous)
            return Refuse("nombre de services hors bornes", out rejection);

        var places = new List<RendezvousAddress>(placeCount);

        for (var i = 0; i < placeCount; i++)
        {
            if (reader.TryText(MaxRendezvousLength, out var text) is false
                || RendezvousAddress.TryParse(text, out var place, out _) is false)
                return Refuse("service illisible", out rejection);

            places.Add(place);
        }

        if (reader.TryText(MaxPasswordBytes, out var password) is false || password.Any(char.IsControl))
            return Refuse("mot de passe hors règles", out rejection);

        if (reader.TryU16(out var banCount) is false || banCount > MaxBans)
            return Refuse("trop de bannis", out rejection);

        var bans = new List<GroupBan>(banCount);

        for (var i = 0; i < banCount; i++)
        {
            if (reader.TryByte(out var flags) is false || flags is 0 or > (BanPeer | BanFingerprint))
                return Refuse("bannissement mal formé", out rejection);

            PeerId? peer = null;
            PlayerFingerprint? fingerprint = null;

            if ((flags & BanPeer) != 0)
            {
                if (reader.TryTake(PeerId.SizeInBytes, out var bytes) is false)
                    return Refuse("politique tronquée", out rejection);

                peer = PeerId.FromBytes(bytes);
            }

            if ((flags & BanFingerprint) != 0)
            {
                if (reader.TryTake(PlayerFingerprint.SizeInBytes, out var bytes) is false)
                    return Refuse("politique tronquée", out rejection);

                fingerprint = PlayerFingerprint.FromBytes(bytes);
            }

            bans.Add(new GroupBan(peer, fingerprint));
        }

        if (reader.TryByte(out var dissolved) is false || dissolved > 1)
            return Refuse("drapeau de dissolution mal formé", out rejection);

        if (reader.TryU16(out var attestationLength) is false || reader.TryTake(attestationLength, out var attestationBytes) is false)
            return Refuse("politique tronquée", out rejection);

        if (TryDecodeAttestation(attestationBytes, out var attestation, out rejection) is false)
            return false;

        if (reader.TryTake(KeyLength, out var signer) is false || reader.TryTake(SignatureLength, out var signature) is false)
            return Refuse("politique tronquée", out rejection);

        if (reader.Ended is false)
            return Refuse("octets en trop après la politique", out rejection);

        policy = new GroupPolicy(
            GroupId.FromBytes(group), version, name, code.ToArray(), places, password, bans, dissolved == 1,
            attestation!, signer.ToArray(), signature.ToArray());

        rejection = null;
        return true;
    }

    private static bool TryDecodeAttestation(ReadOnlySpan<byte> encoded, out GroupAttestation? attestation, out string? rejection)
    {
        attestation = null;
        var reader = new Reader(encoded);

        if (reader.TryByte(out var format) is false || format != Format)
            return Refuse("format d'attestation inconnu", out rejection);

        if (reader.TryTake(GroupId.SizeInBytes, out var group) is false
            || reader.TryU64(out var version) is false
            || reader.TryByte(out var admission) is false)
            return Refuse("attestation tronquée", out rejection);

        if (AdmissionMode.IsKnown(admission) is false)
            return Refuse("mode d'admission inconnu", out rejection);

        if (reader.TryTake(KeyLength, out var owner) is false)
            return Refuse("attestation tronquée", out rejection);

        if (reader.TryByte(out var count) is false || count > MaxModerators)
            return Refuse("trop de modérateurs", out rejection);

        var moderators = new List<byte[]>(count);

        for (var i = 0; i < count; i++)
        {
            if (reader.TryTake(KeyLength, out var key) is false)
                return Refuse("attestation tronquée", out rejection);

            moderators.Add(key.ToArray());
        }

        if (reader.TryTake(SignatureLength, out var signature) is false)
            return Refuse("attestation tronquée", out rejection);

        if (reader.Ended is false)
            return Refuse("octets en trop après l'attestation", out rejection);

        attestation = new GroupAttestation(
            GroupId.FromBytes(group), version, admission, owner.ToArray(), moderators, signature.ToArray());
        rejection = null;
        return true;
    }

    private static void WriteAttestationBody(ArrayBufferWriter<byte> writer, GroupAttestation attestation)
    {
        Byte(writer, Format);
        writer.Write(attestation.Group.ToBytes());
        U64(writer, attestation.Version);
        Byte(writer, attestation.Admission);
        writer.Write(attestation.Owner);
        Byte(writer, checked((byte)attestation.Moderators.Count));

        foreach (var moderator in attestation.Moderators)
            writer.Write(moderator);
    }

    private static void WritePolicyBody(ArrayBufferWriter<byte> writer, GroupPolicy policy)
    {
        Byte(writer, Format);
        writer.Write(policy.Group.ToBytes());
        U64(writer, policy.Version);
        Text(writer, policy.Name);
        writer.Write(policy.Code);
        Byte(writer, checked((byte)policy.Rendezvous.Count));

        foreach (var place in policy.Rendezvous)
            Text(writer, place.ToString());

        Text(writer, policy.Password);
        U16(writer, checked((ushort)policy.Bans.Count));

        foreach (var ban in policy.Bans)
        {
            Byte(writer, (byte)((ban.Peer is null ? 0 : BanPeer) | (ban.Fingerprint is null ? 0 : BanFingerprint)));

            if (ban.Peer is { } peer)
                writer.Write(peer.ToBytes());

            if (ban.Fingerprint is { } fingerprint)
                writer.Write(fingerprint.ToBytes());
        }

        Byte(writer, policy.Dissolved ? (byte)1 : (byte)0);

        var attestation = EncodeAttestation(policy.Attestation);
        U16(writer, checked((ushort)attestation.Length));
        writer.Write(attestation);
        writer.Write(policy.Signer);
    }

    private static void Byte(ArrayBufferWriter<byte> writer, byte value) => writer.Write([value]);

    private static void U16(ArrayBufferWriter<byte> writer, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void U64(ArrayBufferWriter<byte> writer, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void Text(ArrayBufferWriter<byte> writer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Byte(writer, checked((byte)bytes.Length));
        writer.Write(bytes);
    }

    private static bool Refuse(string why, out string? rejection)
    {
        rejection = why;
        return false;
    }

    /// <summary>Lecture bornée : chaque méthode rend faux plutôt que de lever sur une trame courte.</summary>
    private ref struct Reader
    {
        private ReadOnlySpan<byte> _rest;

        public Reader(ReadOnlySpan<byte> data) => _rest = data;

        public readonly bool Ended => _rest.IsEmpty;

        public bool TryTake(int count, out ReadOnlySpan<byte> bytes)
        {
            if (count < 0 || _rest.Length < count)
            {
                bytes = default;
                return false;
            }

            bytes = _rest[..count];
            _rest = _rest[count..];
            return true;
        }

        public bool TryByte(out byte value)
        {
            value = 0;

            if (TryTake(1, out var bytes) is false)
                return false;

            value = bytes[0];
            return true;
        }

        public bool TryU16(out ushort value)
        {
            value = 0;

            if (TryTake(2, out var bytes) is false)
                return false;

            value = BinaryPrimitives.ReadUInt16BigEndian(bytes);
            return true;
        }

        public bool TryU64(out ulong value)
        {
            value = 0;

            if (TryTake(8, out var bytes) is false)
                return false;

            value = BinaryPrimitives.ReadUInt64BigEndian(bytes);
            return true;
        }

        public bool TryText(int maxBytes, out string text)
        {
            text = "";

            if (TryByte(out var length) is false || length > maxBytes || TryTake(length, out var bytes) is false)
                return false;

            try
            {
                text = StrictUtf8.GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }
    }
}
```

- [ ] **Step 5: Écrire `GroupPolicyRules.cs`**

```csharp
using System.Security.Cryptography;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;

namespace Linkpearl.Core.Groups;

/// <summary>
/// Ce qui fait d'une politique décodée une politique acceptable.
/// </summary>
/// <remarks>
/// Tout se vérifie à partir de la politique elle-même et de la clé du groupe :
/// un membre qui vient d'entrer n'a aucun historique sur lequel s'appuyer.
/// </remarks>
public static class GroupPolicyRules
{
    public static bool TryAccept(
        ReadOnlySpan<byte> encoded, GroupId expected, ReadOnlySpan<byte> groupKey,
        out GroupPolicy? policy, out string? rejection)
    {
        policy = null;

        if (GroupPolicyCodec.TryDecode(encoded, out var decoded, out rejection) is false)
            return false;

        var candidate = decoded!;

        if (candidate.Group != expected || candidate.Attestation.Group != expected)
        {
            rejection = "politique d'un autre groupe";
            return false;
        }

        if (candidate.Attestation.Admission == AdmissionMode.Password && candidate.Password.Length == 0)
        {
            rejection = "mode mot de passe sans mot de passe";
            return false;
        }

        try
        {
            using (var group = CryptoPrimitives.ImportVerifier(CryptoPrimitives.Decompress(groupKey)))
            {
                if (CryptoPrimitives.Verify(group, GroupPolicyCodec.AttestationSignedPortion(candidate.Attestation),
                        candidate.Attestation.Signature) is false)
                {
                    rejection = "attestation que le groupe n'a pas signée";
                    return false;
                }
            }

            var byOwner = candidate.Signer.AsSpan().SequenceEqual(groupKey);

            if (byOwner is false && candidate.IsModerator(candidate.Signer) is false)
            {
                rejection = "signataire ni propriétaire ni modérateur";
                return false;
            }

            using (var signer = CryptoPrimitives.ImportVerifier(CryptoPrimitives.Decompress(candidate.Signer)))
            {
                if (CryptoPrimitives.Verify(signer, GroupPolicyCodec.SignedPortion(candidate), candidate.Signature) is false)
                {
                    rejection = "signature de politique invalide";
                    return false;
                }
            }

            var owner = PeerId.Of(CryptoPrimitives.Decompress(candidate.Attestation.Owner));

            if (candidate.Bans.Any(ban => ban.Peer == owner))
            {
                rejection = "politique qui bannit le propriétaire";
                return false;
            }

            if (byOwner is false)
            {
                // Ce qu'un modérateur ne peut pas faire, même par une politique
                // bien signée : dissoudre, ou écarter ses pairs.
                if (candidate.Dissolved)
                {
                    rejection = "seul le propriétaire dissout";
                    return false;
                }

                var moderators = candidate.Attestation.Moderators
                    .Select(key => PeerId.Of(CryptoPrimitives.Decompress(key)))
                    .ToHashSet();

                if (candidate.Bans.Any(ban => ban.Peer is { } peer && moderators.Contains(peer)))
                {
                    rejection = "un modérateur ne bannit pas un modérateur";
                    return false;
                }
            }
        }
        catch (CryptographicException e)
        {
            rejection = $"clé de politique invalide : {e.Message}";
            return false;
        }

        policy = candidate;
        rejection = null;
        return true;
    }

    /// <summary>
    /// Vrai si la candidate doit remplacer la politique courante.
    /// </summary>
    /// <remarks>
    /// L'attestation d'abord : un modérateur retiré garde l'ancienne attestation
    /// qui le nomme, et pourrait sinon publier une version plus haute sous elle.
    /// À version égale, la plus petite empreinte de signature l'emporte, pour que
    /// deux membres qui voient les deux mêmes politiques choisissent la même.
    /// </remarks>
    public static bool IsNewer(GroupPolicy candidate, GroupPolicy? current)
    {
        if (current is null)
            return true;

        if (candidate.Attestation.Version != current.Attestation.Version)
            return candidate.Attestation.Version > current.Attestation.Version;

        if (candidate.Version != current.Version)
            return candidate.Version > current.Version;

        return SHA256.HashData(candidate.Signature).AsSpan().SequenceCompareTo(SHA256.HashData(current.Signature)) < 0;
    }
}

/// <summary>Signer une attestation ou une politique.</summary>
public static class GroupPolicySigning
{
    public static byte[] CompressedKey(ECDsa key) => CryptoPrimitives.Compress(CryptoPrimitives.ExportPublicPoint(key));

    public static GroupAttestation SignAttestation(GroupAttestation unsigned, ECDsa groupKey)
        => unsigned with { Signature = CryptoPrimitives.Sign(groupKey, GroupPolicyCodec.AttestationSignedPortion(unsigned)) };

    public static GroupPolicy Sign(GroupPolicy unsigned, ECDsa signer)
    {
        var withSigner = unsigned with { Signer = CompressedKey(signer), Signature = [] };
        return withSigner with { Signature = CryptoPrimitives.Sign(signer, GroupPolicyCodec.SignedPortion(withSigner)) };
    }
}
```

- [ ] **Step 6: Vérifier que les tests passent, puis la suite complète**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupPolicyTests`
Expected: PASS.

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Linkpearl/Core/Groups/GroupPolicy.cs Linkpearl/Core/Groups/GroupPolicyCodec.cs Linkpearl/Core/Groups/GroupPolicyRules.cs Linkpearl.Core.Tests/Groups/GroupPolicyTests.cs
git commit -m "feat(groupes): politique signée, attestation des modérateurs et ordre des versions"
```

---

### Task 2: Les dépôts d'admission

**Files:**
- Create: `Linkpearl/Core/Groups/AdmissionMessages.cs`
- Modify: `Linkpearl/Core/Groups/GroupDerivation.cs` (`AdmissionAddress`, `AdmissionAround`)
- Test: `Linkpearl.Core.Tests/Groups/AdmissionMessagesTests.cs`

**Interfaces:**
- Consumes: `CryptoPrimitives`, `GroupId`, `GroupPolicyCodec.IsValidName`, `GroupPolicyCodec.MaxPasswordBytes`, `GroupPolicyCodec.MaxNameBytes`, `GroupPolicySigning.CompressedKey`, `InvitationTicket.SizeInBytes`, `MailboxAddress`, `RendezvousWire.MaxDepositLength`.
- Produces:
  - `static class AdmissionKind { Request = 0x05, Challenge = 0x06, Proof = 0x07, Welcome = 0x08, Refusal = 0x09; static bool IsAdmission(byte) }`
  - `static class RefusalReason { WrongPassword = 0x01, TooManyAttempts = 0x02, Declined = 0x03; static bool IsKnown(byte) }`
  - `abstract record AdmissionMessage` et ses cinq dérivés : `AdmissionRequest(byte[] Code, byte[] PublicKey, byte[] Ephemeral, byte[] Nonce, ushort WorldId, string CharacterName)`, `AdmissionChallenge(byte[] Nonce, byte[] MemberEphemeral)`, `AdmissionProof(byte[] Code, byte[] Nonce, byte[] MemberEphemeral, byte[] SealedPassword)`, `AdmissionWelcome(byte[] Nonce, byte[] MemberEphemeral, byte[] SealedGrant)`, `AdmissionRefusal(byte[] Nonce, byte Reason)`. Les clés et éphémères y sont des points **non compressés de 65 octets**, comme dans `PairRequestMessage`.
  - `static class AdmissionCodec` : `NonceLength = 12`, `MaxNameBytes = 64`, `byte[] Encode(AdmissionMessage)`, `bool TryDecode(ReadOnlySpan<byte>, out AdmissionMessage?, out string?)`.
  - `sealed record GroupGrant(GroupId Group, byte[] Secret, byte[] OwnerKey, string Name)` et `static class GroupGrantCodec { MinSealedLength; MaxSealedLength; byte[] Encode(GroupGrant); bool TryDecode(ReadOnlySpan<byte>, out GroupGrant?, out string?) }`.
  - `static class AdmissionSealing` : `const string Proof = "proof"`, `const string Welcome = "welcome"`, `byte[] DeriveKey(ReadOnlySpan<byte> shared, ReadOnlySpan<byte> nonce, string purpose)`, `byte[] Key(ECDiffieHellman ours, ReadOnlySpan<byte> theirEphemeral, ReadOnlySpan<byte> nonce, string purpose)`, `byte[] Associated(byte kind, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> memberEphemeral)`, `byte[] Seal(byte[] key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associated)`, `bool TryOpen(byte[] key, ReadOnlySpan<byte> sealed, ReadOnlySpan<byte> associated, out byte[] plaintext)`.
  - `GroupDerivation.AdmissionAddress(ReadOnlySpan<byte> code, DateTimeOffset now, int windowOffset = 0)` et `AdmissionAround(ReadOnlySpan<byte> code, DateTimeOffset now)`.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using System.Security.Cryptography;
using System.Text;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Groups;

public sealed class AdmissionMessagesTests
{
    private static readonly byte[] Code = [1, 2, 3, 4, 5, 6];
    private static readonly byte[] Nonce = [.. Enumerable.Range(100, 12).Select(i => (byte)i)];
    private static readonly DateTimeOffset Noon = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static byte[] Point()
    {
        using var key = CryptoPrimitives.GenerateEphemeral();
        return CryptoPrimitives.ExportPublicPoint(key);
    }

    private static T RoundTrip<T>(T message) where T : AdmissionMessage
    {
        var encoded = AdmissionCodec.Encode(message);
        Assert.True(encoded.Length <= RendezvousWire.MaxDepositLength);
        Assert.True(AdmissionCodec.TryDecode(encoded, out var decoded, out var why), why);
        return Assert.IsType<T>(decoded);
    }

    [Fact]
    public void Les_vecteurs_figes_sont_retrouves()
    {
        Assert.Equal("e5153db1e51e", GroupDerivation.AdmissionAddress(Code, Noon).ToString());

        byte[] shared = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
        Assert.Equal("8341723f2280566dcb34344d5eded1c5f25eb0287a702e68c529c3067fa1b482",
            Convert.ToHexStringLower(AdmissionSealing.DeriveKey(shared, Nonce, AdmissionSealing.Proof)));
        Assert.Equal("657e3195b920ee89df8d42bf96d75de2a3ab0f79a807a4e4305d1db51250c0ac",
            Convert.ToHexStringLower(AdmissionSealing.DeriveKey(shared, Nonce, AdmissionSealing.Welcome)));
    }

    [Fact]
    public void Chaque_depot_fait_l_aller_retour()
    {
        var request = RoundTrip(new AdmissionRequest(Code, Point(), Point(), Nonce, 21, "Jhalen Tavari"));
        Assert.Equal("Jhalen Tavari", request.CharacterName);
        Assert.Equal(21, request.WorldId);
        Assert.Equal(Code, request.Code);

        var challenge = RoundTrip(new AdmissionChallenge(Nonce, Point()));
        Assert.Equal(Nonce, challenge.Nonce);

        var proof = RoundTrip(new AdmissionProof(Code, Nonce, Point(), new byte[16 + GroupPolicyCodec.MaxPasswordBytes]));
        Assert.Equal(16 + GroupPolicyCodec.MaxPasswordBytes, proof.SealedPassword.Length);

        var welcome = RoundTrip(new AdmissionWelcome(Nonce, Point(), new byte[GroupGrantCodec.MaxSealedLength]));
        Assert.Equal(GroupGrantCodec.MaxSealedLength, welcome.SealedGrant.Length);

        var refusal = RoundTrip(new AdmissionRefusal(Nonce, RefusalReason.Declined));
        Assert.Equal(RefusalReason.Declined, refusal.Reason);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x05, 1, 2 })]
    [InlineData(new byte[] { 0x03 })]
    [InlineData(new byte[] { 0x7F, 0, 0, 0 })]
    public void Un_depot_mal_forme_est_refuse(byte[] payload)
        => Assert.False(AdmissionCodec.TryDecode(payload, out _, out _));

    [Fact]
    public void Un_nom_hostile_ou_un_motif_inconnu_est_refuse()
    {
        var hostile = AdmissionCodec.Encode(new AdmissionRequest(Code, Point(), Point(), Nonce, 21, "Jhalen\nTavari"));
        Assert.False(AdmissionCodec.TryDecode(hostile, out _, out _));

        var unknown = AdmissionCodec.Encode(new AdmissionRefusal(Nonce, RefusalReason.Declined));
        unknown[^1] = 0x7F;
        Assert.False(AdmissionCodec.TryDecode(unknown, out _, out _));

        var tail = AdmissionCodec.Encode(new AdmissionChallenge(Nonce, Point()));
        Assert.False(AdmissionCodec.TryDecode([.. tail, 0x00], out _, out _));
    }

    [Fact]
    public void Le_scellement_ne_s_ouvre_qu_avec_la_bonne_paire_d_ephemeres()
    {
        using var candidate = CryptoPrimitives.GenerateEphemeral();
        using var member = CryptoPrimitives.GenerateEphemeral();
        using var intruder = CryptoPrimitives.GenerateEphemeral();

        var memberPoint = CryptoPrimitives.ExportPublicPoint(member);
        var associated = AdmissionSealing.Associated(AdmissionKind.Proof, Nonce, memberPoint);

        var sealedPassword = AdmissionSealing.Seal(
            AdmissionSealing.Key(candidate, memberPoint, Nonce, AdmissionSealing.Proof), "lune"u8, associated);

        var opened = AdmissionSealing.TryOpen(
            AdmissionSealing.Key(member, CryptoPrimitives.ExportPublicPoint(candidate), Nonce, AdmissionSealing.Proof),
            sealedPassword, associated, out var plain);

        Assert.True(opened);
        Assert.Equal("lune", Encoding.UTF8.GetString(plain));

        Assert.False(AdmissionSealing.TryOpen(
            AdmissionSealing.Key(intruder, CryptoPrimitives.ExportPublicPoint(candidate), Nonce, AdmissionSealing.Proof),
            sealedPassword, associated, out _));

        // Même accord, autre usage : une preuve ne s'ouvre pas comme une bienvenue.
        Assert.False(AdmissionSealing.TryOpen(
            AdmissionSealing.Key(member, CryptoPrimitives.ExportPublicPoint(candidate), Nonce, AdmissionSealing.Welcome),
            sealedPassword, associated, out _));
    }

    [Fact]
    public void Un_octroi_fait_l_aller_retour_et_verifie_son_groupe()
    {
        using var groupKey = CryptoPrimitives.GenerateIdentity();
        var ownerKey = GroupPolicySigning.CompressedKey(groupKey);
        var grant = new GroupGrant(GroupId.Of(ownerKey), RandomNumberGenerator.GetBytes(32), ownerKey, "Compagnie");

        Assert.True(GroupGrantCodec.TryDecode(GroupGrantCodec.Encode(grant), out var back, out var why), why);
        Assert.Equal(grant.Group, back!.Group);
        Assert.Equal(grant.Secret, back.Secret);
        Assert.Equal("Compagnie", back.Name);

        // Un identifiant qui ne dérive pas de la clé du groupe : un membre
        // malveillant qui voudrait faire entrer le candidat ailleurs.
        var lying = grant with { Group = GroupId.FromBytes(new byte[16]) };
        Assert.False(GroupGrantCodec.TryDecode(GroupGrantCodec.Encode(lying), out _, out _));
    }

    [Fact]
    public void La_boite_d_admission_tourne_avec_la_fenetre_et_depend_du_code()
    {
        Assert.Equal(
            [GroupDerivation.AdmissionAddress(Code, Noon), GroupDerivation.AdmissionAddress(Code, Noon, 1)],
            GroupDerivation.AdmissionAround(Code, Noon));

        Assert.NotEqual(GroupDerivation.AdmissionAddress(Code, Noon), GroupDerivation.AdmissionAddress([9, 9, 9, 9, 9, 9], Noon));
    }
}
```

- [ ] **Step 2: Vérifier qu'ils échouent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter AdmissionMessagesTests`
Expected: échec de compilation.

- [ ] **Step 3: Ajouter la boîte d'admission à `GroupDerivation.cs`**

Ajouter l'étiquette à côté des autres :

```csharp
    private static ReadOnlySpan<byte> AdmissionContext => "linkpearl:group-join:v1"u8;
```

et les méthodes (ajouter `using Linkpearl.Core.Identity;` si ce n'est pas déjà fait, pour `InvitationTicket`) :

```csharp
    /// <summary>
    /// La boîte où un candidat dépose sa demande, et que chaque membre ouvre.
    /// </summary>
    /// <remarks>
    /// Elle dérive du code et non du secret : le candidat ne connaît que le code.
    /// Changer le code dans la politique déplace la boîte, et l'ancien code ne
    /// mène plus nulle part.
    /// </remarks>
    public static MailboxAddress AdmissionAddress(ReadOnlySpan<byte> code, DateTimeOffset now, int windowOffset = 0)
    {
        if (code.Length != InvitationTicket.SizeInBytes)
            throw new ArgumentException($"un code fait {InvitationTicket.SizeInBytes} octets", nameof(code));

        var index = MailboxAddress.IndexAt(now) + windowOffset;
        var windowAt = AdmissionContext.Length + code.Length;

        Span<byte> input = stackalloc byte[windowAt + sizeof(long)];
        AdmissionContext.CopyTo(input);
        code.CopyTo(input[AdmissionContext.Length..]);
        BinaryPrimitives.WriteInt64BigEndian(input[windowAt..], index);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);

        return MailboxAddress.FromBytes(digest[..MailboxAddress.SizeInBytes]);
    }

    public static IReadOnlyList<MailboxAddress> AdmissionAround(ReadOnlySpan<byte> code, DateTimeOffset now)
        => [AdmissionAddress(code, now), AdmissionAddress(code, now, 1)];
```

- [ ] **Step 4: Écrire `AdmissionMessages.cs`**

```csharp
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;

namespace Linkpearl.Core.Groups;

/// <summary>
/// Les types de dépôt de l'admission, à la suite de ceux du pairage (0x03, 0x04).
/// </summary>
public static class AdmissionKind
{
    public const byte Request = 0x05;
    public const byte Challenge = 0x06;
    public const byte Proof = 0x07;
    public const byte Welcome = 0x08;
    public const byte Refusal = 0x09;

    public static bool IsAdmission(byte kind) => kind is >= Request and <= Refusal;
}

/// <summary>Pourquoi un membre refuse un candidat.</summary>
public static class RefusalReason
{
    public const byte WrongPassword = 0x01;
    public const byte TooManyAttempts = 0x02;
    public const byte Declined = 0x03;

    public static bool IsKnown(byte reason) => reason is WrongPassword or TooManyAttempts or Declined;
}

public abstract record AdmissionMessage;

/// <summary>Candidat vers la boîte d'admission : « je veux entrer ».</summary>
/// <param name="PublicKey">Clé d'identité du candidat, point de 65 octets.</param>
/// <param name="Ephemeral">Éphémère du candidat, point de 65 octets.</param>
public sealed record AdmissionRequest(
    byte[] Code, byte[] PublicKey, byte[] Ephemeral, byte[] Nonce, ushort WorldId, string CharacterName) : AdmissionMessage;

/// <summary>Membre vers la boîte du candidat : « prouve que tu connais le mot de passe ».</summary>
public sealed record AdmissionChallenge(byte[] Nonce, byte[] MemberEphemeral) : AdmissionMessage;

/// <summary>Candidat vers la boîte d'admission : le mot de passe, scellé pour le membre qui a défié.</summary>
public sealed record AdmissionProof(byte[] Code, byte[] Nonce, byte[] MemberEphemeral, byte[] SealedPassword) : AdmissionMessage;

/// <summary>Membre vers la boîte du candidat : l'octroi du groupe, scellé.</summary>
public sealed record AdmissionWelcome(byte[] Nonce, byte[] MemberEphemeral, byte[] SealedGrant) : AdmissionMessage;

/// <summary>Membre vers la boîte du candidat : non, avec le motif.</summary>
/// <remarks>Non authentifié : un tiers peut le forger, ce qui ne coûte qu'un nouvel essai au candidat.</remarks>
public sealed record AdmissionRefusal(byte[] Nonce, byte Reason) : AdmissionMessage;

/// <summary>
/// Les dépôts de l'admission, chacun sous les 512 octets d'une boîte.
/// </summary>
/// <remarks>
/// Disposition, après l'octet de type :
/// <code>
/// demande   : code (6) | clé (33) | éphémère (33) | aléa (12) | monde (2) | nom (reste, 1 à 64)
/// défi      : aléa (12) | éphémère du membre (33)
/// preuve    : code (6) | aléa (12) | éphémère du membre (33) | mot de passe scellé (reste)
/// bienvenue : aléa (12) | éphémère du membre (33) | octroi scellé (reste)
/// refus     : aléa (12) | motif (1)
/// </code>
/// Points compressés sur le fil, non compressés en mémoire, comme la demande de pairage.
/// </remarks>
public static class AdmissionCodec
{
    public const int NonceLength = 12;
    public const int MaxNameBytes = 64;

    private const int CodeLength = InvitationTicket.SizeInBytes;
    private const int PointLength = CryptoPrimitives.CompressedPointLength;
    private const int TagLength = CryptoPrimitives.TagLength;

    public static byte[] Encode(AdmissionMessage message) => message switch
    {
        AdmissionRequest request => EncodeRequest(request),
        AdmissionChallenge challenge =>
            [AdmissionKind.Challenge, .. challenge.Nonce, .. CryptoPrimitives.Compress(challenge.MemberEphemeral)],
        AdmissionProof proof =>
            [AdmissionKind.Proof, .. proof.Code, .. proof.Nonce, .. CryptoPrimitives.Compress(proof.MemberEphemeral), .. proof.SealedPassword],
        AdmissionWelcome welcome =>
            [AdmissionKind.Welcome, .. welcome.Nonce, .. CryptoPrimitives.Compress(welcome.MemberEphemeral), .. welcome.SealedGrant],
        AdmissionRefusal refusal => [AdmissionKind.Refusal, .. refusal.Nonce, refusal.Reason],
        _ => throw new ArgumentException("dépôt d'admission inconnu", nameof(message)),
    };

    private static byte[] EncodeRequest(AdmissionRequest request)
    {
        var name = Encoding.UTF8.GetBytes(request.CharacterName);

        if (name.Length > MaxNameBytes)
            throw new ArgumentException("nom de personnage trop long", nameof(request));

        var world = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(world, request.WorldId);

        return [
            AdmissionKind.Request, .. request.Code, .. CryptoPrimitives.Compress(request.PublicKey),
            .. CryptoPrimitives.Compress(request.Ephemeral), .. request.Nonce, .. world, .. name,
        ];
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out AdmissionMessage? message, out string? rejection)
    {
        message = null;

        if (payload.IsEmpty || AdmissionKind.IsAdmission(payload[0]) is false)
            return Refuse("dépôt qui n'est pas une admission", out rejection);

        var body = payload[1..];

        try
        {
            switch (payload[0])
            {
                case AdmissionKind.Request:
                {
                    const int header = CodeLength + PointLength + PointLength + NonceLength + 2;

                    if (body.Length <= header || body.Length > header + MaxNameBytes)
                        return Refuse("demande d'admission hors bornes", out rejection);

                    var name = Encoding.UTF8.GetString(body[header..]);

                    if (name.Any(char.IsControl) || string.IsNullOrWhiteSpace(name))
                        return Refuse("nom de personnage hors règles", out rejection);

                    message = new AdmissionRequest(
                        body[..CodeLength].ToArray(),
                        CryptoPrimitives.Decompress(body.Slice(CodeLength, PointLength)),
                        CryptoPrimitives.Decompress(body.Slice(CodeLength + PointLength, PointLength)),
                        body.Slice(CodeLength + (2 * PointLength), NonceLength).ToArray(),
                        BinaryPrimitives.ReadUInt16BigEndian(body.Slice(header - 2, 2)),
                        name);
                    break;
                }

                case AdmissionKind.Challenge:
                    if (body.Length != NonceLength + PointLength)
                        return Refuse("défi hors bornes", out rejection);

                    message = new AdmissionChallenge(body[..NonceLength].ToArray(), CryptoPrimitives.Decompress(body[NonceLength..]));
                    break;

                case AdmissionKind.Proof:
                {
                    const int header = CodeLength + NonceLength + PointLength;
                    var sealedLength = body.Length - header;

                    if (sealedLength < TagLength || sealedLength > TagLength + GroupPolicyCodec.MaxPasswordBytes)
                        return Refuse("preuve hors bornes", out rejection);

                    message = new AdmissionProof(
                        body[..CodeLength].ToArray(),
                        body.Slice(CodeLength, NonceLength).ToArray(),
                        CryptoPrimitives.Decompress(body.Slice(CodeLength + NonceLength, PointLength)),
                        body[header..].ToArray());
                    break;
                }

                case AdmissionKind.Welcome:
                {
                    const int header = NonceLength + PointLength;
                    var sealedLength = body.Length - header;

                    if (sealedLength < GroupGrantCodec.MinSealedLength || sealedLength > GroupGrantCodec.MaxSealedLength)
                        return Refuse("bienvenue hors bornes", out rejection);

                    message = new AdmissionWelcome(
                        body[..NonceLength].ToArray(),
                        CryptoPrimitives.Decompress(body.Slice(NonceLength, PointLength)),
                        body[header..].ToArray());
                    break;
                }

                case AdmissionKind.Refusal:
                    if (body.Length != NonceLength + 1 || RefusalReason.IsKnown(body[NonceLength]) is false)
                        return Refuse("refus hors bornes", out rejection);

                    message = new AdmissionRefusal(body[..NonceLength].ToArray(), body[NonceLength]);
                    break;
            }
        }
        catch (CryptographicException e)
        {
            return Refuse($"clé invalide : {e.Message}", out rejection);
        }

        rejection = null;
        return message is not null;
    }

    private static bool Refuse(string why, out string? rejection)
    {
        rejection = why;
        return false;
    }
}

/// <summary>Ce qu'un membre remet au candidat admis : de quoi être du groupe.</summary>
/// <param name="OwnerKey">La clé compressée du groupe, dont dérive son identifiant.</param>
public sealed record GroupGrant(GroupId Group, byte[] Secret, byte[] OwnerKey, string Name);

public static class GroupGrantCodec
{
    private const int FixedLength = GroupId.SizeInBytes + GroupDerivation.SecretSize + CryptoPrimitives.CompressedPointLength + 1;

    public const int MinSealedLength = FixedLength + 1 + CryptoPrimitives.TagLength;
    public const int MaxSealedLength = FixedLength + GroupPolicyCodec.MaxNameBytes + CryptoPrimitives.TagLength;

    public static byte[] Encode(GroupGrant grant)
    {
        var name = Encoding.UTF8.GetBytes(grant.Name);
        return [.. grant.Group.ToBytes(), .. grant.Secret, .. grant.OwnerKey, checked((byte)name.Length), .. name];
    }

    public static bool TryDecode(ReadOnlySpan<byte> plain, out GroupGrant? grant, out string? rejection)
    {
        grant = null;

        if (plain.Length < FixedLength || plain.Length != FixedLength + plain[FixedLength - 1])
        {
            rejection = "octroi mal formé";
            return false;
        }

        var group = GroupId.FromBytes(plain[..GroupId.SizeInBytes]);
        var secret = plain.Slice(GroupId.SizeInBytes, GroupDerivation.SecretSize).ToArray();
        var ownerKey = plain.Slice(GroupId.SizeInBytes + GroupDerivation.SecretSize, CryptoPrimitives.CompressedPointLength).ToArray();
        string name;

        try
        {
            name = new UTF8Encoding(false, true).GetString(plain[FixedLength..]);
        }
        catch (DecoderFallbackException)
        {
            rejection = "nom de groupe illisible";
            return false;
        }

        // L'identifiant doit dériver de la clé : sinon un membre malveillant
        // ferait croire au candidat qu'il entre dans un groupe alors qu'il entre
        // dans un autre.
        if (GroupId.Of(ownerKey) != group || GroupPolicyCodec.IsValidName(name) is false)
        {
            rejection = "octroi incohérent";
            return false;
        }

        grant = new GroupGrant(group, secret, ownerKey, name);
        rejection = null;
        return true;
    }
}

/// <summary>
/// Le scellement de l'admission : la preuve et l'octroi.
/// </summary>
/// <remarks>
/// La clé vient de l'accord des éphémères suivi de l'aléa, comme le pairage,
/// dérivée par usage : une preuve ne s'ouvre jamais comme une bienvenue. Chaque
/// clé ne sert qu'une fois, d'où l'aléa AES-GCM fixe à zéro. Les données
/// associées lient le scellé à son en-tête.
/// </remarks>
public static class AdmissionSealing
{
    public const string Proof = "proof";
    public const string Welcome = "welcome";

    private static readonly byte[] ZeroNonce = new byte[CryptoPrimitives.NonceLength];

    public static byte[] DeriveKey(ReadOnlySpan<byte> shared, ReadOnlySpan<byte> nonce, string purpose)
    {
        byte[] material = [.. shared, .. nonce];

        try
        {
            var key = new byte[CryptoPrimitives.KeyLength];
            HKDF.DeriveKey(HashAlgorithmName.SHA256, material, key, [], Encoding.ASCII.GetBytes("linkpearl:group-admit:v1:" + purpose));
            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    public static byte[] Key(ECDiffieHellman ours, ReadOnlySpan<byte> theirEphemeral, ReadOnlySpan<byte> nonce, string purpose)
    {
        var shared = CryptoPrimitives.Agree(ours, theirEphemeral);

        try
        {
            return DeriveKey(shared, nonce, purpose);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
        }
    }

    public static byte[] Associated(byte kind, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> memberEphemeral)
        => [kind, .. nonce, .. CryptoPrimitives.Compress(memberEphemeral)];

    public static byte[] Seal(byte[] key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associated)
        => CryptoPrimitives.Seal(key, ZeroNonce, plaintext, associated);

    public static bool TryOpen(byte[] key, ReadOnlySpan<byte> sealedData, ReadOnlySpan<byte> associated, out byte[] plaintext)
        => CryptoPrimitives.TryOpen(key, ZeroNonce, sealedData, associated, out plaintext);
}
```

- [ ] **Step 5: Vérifier que les tests passent, puis la suite complète**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter AdmissionMessagesTests`
Expected: PASS.

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Linkpearl/Core/Groups/AdmissionMessages.cs Linkpearl/Core/Groups/GroupDerivation.cs Linkpearl.Core.Tests/Groups/AdmissionMessagesTests.cs
git commit -m "feat(groupes): dépôts d'admission, boîte d'admission et scellement"
```

---

### Task 3: Le groupe porte sa politique

**Files:**
- Modify: `Linkpearl/Core/Groups/GroupRecord.cs`
- Modify: `Linkpearl/Core/Groups/GroupBook.cs`
- Modify: `Linkpearl/Core/Groups/GroupBookCodec.cs`
- Modify: `Linkpearl/Core/Groups/GroupDialPlanner.cs`
- Test: `Linkpearl.Core.Tests/Groups/GroupBookPolicyTests.cs`

**Interfaces:**
- Consumes: tâche 1 (`GroupPolicy`, `GroupPolicyCodec`, `GroupPolicyRules`), `PolicyFixture`, `GroupBookTests.Group`.
- Produces:
  - `GroupRecord` : `byte[]? OwnerKey` (clé compressée du groupe, nulle pour un groupe d'essai et le Public), `byte[]? SigningKey` (PKCS#8, chez le seul propriétaire), `GroupPolicy? Policy` (vérifiée). `GroupMember` : `byte[]? PublicKey` (point de 65 octets, épinglé).
  - `enum GroupAdmission` gagne `Banned`.
  - `enum PolicyOffer { Adopted, Same, Stale, Invalid, UnknownGroup, NotPrivate }` (local).
  - `interface IGroupPolicies { byte[]? CurrentPolicy(GroupId group); PolicyOffer OfferPolicy(GroupId group, ReadOnlySpan<byte> encoded); }`, implémentée par `GroupBook`.
  - `GroupBook.PolicyAdopted` : `event Action<GroupId>?`, levé hors du verrou après `Changed`.
  - `GroupBookCodec` : champs ajoutés en fin, nullables ; un `OwnerKey` dont ne dérive pas l'`Id` rejette l'entrée ; une politique qui ne passe pas `TryAccept` est oubliée (le groupe est gardé).
  - `GroupDialPlanner` : un membre banni par la politique n'est ni retenu ni gardé.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Tests.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Groups;

public sealed class GroupBookPolicyTests : IDisposable
{
    private readonly ECDsa _group = CryptoPrimitives.GenerateIdentity();
    private readonly ECDsa _owner = CryptoPrimitives.GenerateIdentity();
    private readonly ECDsa _moderator = CryptoPrimitives.GenerateIdentity();
    private readonly MovableClock _clock = new();

    public void Dispose()
    {
        _group.Dispose();
        _owner.Dispose();
        _moderator.Dispose();
    }

    private static readonly PlayerFingerprint Alice = PlayerFingerprint.Of("alice", 21);
    private static readonly PlayerFingerprint Mallory = PlayerFingerprint.Of("mallory", 21);

    private GroupAttestation Attested => PolicyFixture.Attestation(_group, _owner, [_moderator]);

    private GroupPolicy PolicyV(ulong version, IReadOnlyList<GroupBan>? bans = null, string name = "Compagnie")
        => PolicyFixture.Policy(_group, Attested, _group, version: version, bans: bans, name: name);

    private GroupRecord Private(GroupPolicy? policy)
    {
        var ownerKey = PolicyFixture.Compressed(_group);
        return GroupBookTests.Group(RandomNumberGenerator.GetBytes(32), _clock.UtcNow) with
        {
            Id = GroupId.Of(ownerKey),
            OwnerKey = ownerKey,
            Policy = policy,
        };
    }

    [Fact]
    public void Une_politique_plus_recente_est_adoptee_et_renomme_le_groupe()
    {
        var book = new GroupBook(_clock);
        var group = Private(PolicyV(1));
        book.Load([group]);

        var adopted = new List<GroupId>();
        book.PolicyAdopted += adopted.Add;

        Assert.Equal(PolicyOffer.Adopted, book.OfferPolicy(group.Id, GroupPolicyCodec.Encode(PolicyV(2, name: "Nouvelle"))));
        Assert.Equal("Nouvelle", book.Find(group.Id)!.Name);
        Assert.Equal(2UL, book.Find(group.Id)!.Policy!.Version);
        Assert.Equal([group.Id], adopted);

        Assert.Equal(PolicyOffer.Same, book.OfferPolicy(group.Id, GroupPolicyCodec.Encode(book.Find(group.Id)!.Policy!)));
        Assert.Equal(PolicyOffer.Stale, book.OfferPolicy(group.Id, GroupPolicyCodec.Encode(PolicyV(1))));
        Assert.Equal(PolicyOffer.Invalid, book.OfferPolicy(group.Id, [1, 2, 3]));
        Assert.Equal(PolicyOffer.UnknownGroup, book.OfferPolicy(GroupId.FromBytes(new byte[16]), GroupPolicyCodec.Encode(PolicyV(3))));
    }

    [Fact]
    public void Un_groupe_d_essai_n_accepte_aucune_politique()
    {
        var book = new GroupBook(_clock);
        var test = GroupBookTests.Group(RandomNumberGenerator.GetBytes(32), _clock.UtcNow);
        book.Load([test]);

        Assert.Equal(PolicyOffer.NotPrivate, book.OfferPolicy(test.Id, GroupPolicyCodec.Encode(PolicyV(1))));
    }

    [Fact]
    public void Un_membre_banni_n_est_pas_admis_et_rien_n_est_epingle()
    {
        using var mallory = CryptoPrimitives.GenerateIdentity();
        var malloryKey = CryptoPrimitives.ExportPublicPoint(mallory);
        var book = new GroupBook(_clock);
        var group = Private(PolicyV(1, bans: [new GroupBan(PeerId.Of(malloryKey), null)]));
        book.Load([group]);

        Assert.Equal(GroupAdmission.Banned, book.Admit(group.Id, Mallory, malloryKey, "Mallory"));
        Assert.Empty(book.Find(group.Id)!.Members);

        var byCharacter = Private(PolicyV(2, bans: [new GroupBan(null, Alice)]));
        book.Load([byCharacter]);
        Assert.Equal(GroupAdmission.Banned, book.Admit(byCharacter.Id, Alice, CryptoPrimitives.ExportPublicPoint(_moderator), "Alice"));
    }

    [Fact]
    public void La_cle_complete_est_gardee_a_l_epinglage()
    {
        var book = new GroupBook(_clock);
        var group = Private(PolicyV(1));
        book.Load([group]);
        var key = CryptoPrimitives.ExportPublicPoint(_moderator);

        Assert.Equal(GroupAdmission.Pinned, book.Admit(group.Id, Alice, key, "Alice"));
        Assert.Equal(key, book.Find(group.Id)!.Members[Alice].PublicKey);
    }

    [Fact]
    public void Politique_cles_et_membres_survivent_au_codec()
    {
        var group = Private(PolicyV(4)) with
        {
            SigningKey = _group.ExportPkcs8PrivateKey(),
            Members = new Dictionary<PlayerFingerprint, GroupMember>
            {
                [Alice] = new() { Fingerprint = Alice, DisplayName = "Alice", PublicKey = CryptoPrimitives.ExportPublicPoint(_owner) },
            },
        };

        var back = Assert.Single(GroupBookCodec.Decode(GroupBookCodec.Encode([group])));

        Assert.Equal(group.OwnerKey, back.OwnerKey);
        Assert.Equal(group.SigningKey, back.SigningKey);
        Assert.Equal(4UL, back.Policy!.Version);
        Assert.Equal(group.Members[Alice].PublicKey, back.Members[Alice].PublicKey);
    }

    [Fact]
    public void Une_politique_alteree_sur_disque_est_oubliee_pas_le_groupe()
    {
        var group = Private(PolicyV(1)) with { Policy = PolicyV(1) with { Name = "Falsifié" } };   // signature devenue fausse

        var back = Assert.Single(GroupBookCodec.Decode(GroupBookCodec.Encode([group])));

        Assert.Null(back.Policy);
        Assert.Equal(group.Id, back.Id);
    }

    [Fact]
    public void Une_cle_de_groupe_qui_ne_donne_pas_l_identifiant_rejette_l_entree()
    {
        var group = Private(PolicyV(1)) with { Id = GroupId.FromBytes(new byte[16]) };

        Assert.Empty(GroupBookCodec.Decode(GroupBookCodec.Encode([group])));
    }

    [Fact]
    public void Le_planificateur_ne_retient_pas_un_banni()
    {
        var group = Private(PolicyV(1, bans: [new GroupBan(null, Mallory)]));
        var planner = new GroupDialPlanner(_clock);

        var planned = planner.Plan(
            Alice,
            [new GroupSighting(group.Id, Mallory, "Mallory"), new GroupSighting(group.Id, PlayerFingerprint.Of("bob", 21), "Bob")],
            [group], []);

        Assert.Equal(PlayerFingerprint.Of("bob", 21), Assert.Single(planned).PinnedFingerprint);
    }
}
```

- [ ] **Step 2: Vérifier qu'ils échouent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupBookPolicyTests`
Expected: échec de compilation.

- [ ] **Step 3: Étendre `GroupRecord.cs`**

Dans `GroupMember`, après `Receive` :

```csharp
    /// <summary>
    /// La clé d'identité complète épinglée, point de 65 octets.
    /// </summary>
    /// <remarks>
    /// L'identifiant seul ne suffit pas pour nommer un modérateur : la politique
    /// porte des clés, pas des empreintes de clés.
    /// </remarks>
    public byte[]? PublicKey { get; init; }
```

Dans `GroupRecord`, après `Members` :

```csharp
    /// <summary>
    /// La clé publique compressée du groupe, dont dérive son identifiant.
    /// </summary>
    /// <remarks>
    /// Nulle pour un groupe fabriqué à partir d'un secret seul (essai, Public) :
    /// sans elle, aucune politique ne peut se vérifier, donc aucune ne s'applique.
    /// </remarks>
    public byte[]? OwnerKey { get; init; }

    /// <summary>La clé privée du groupe, PKCS#8, chez le seul propriétaire.</summary>
    public byte[]? SigningKey { get; init; }

    /// <summary>La politique vérifiée la plus récente. Nulle tant qu'aucun membre ne l'a transmise.</summary>
    public GroupPolicy? Policy { get; init; }
```

- [ ] **Step 4: Étendre `GroupBook.cs`**

Ajouter, avant `GroupBook` :

```csharp
/// <summary>Le sort d'une politique proposée. Strictement local.</summary>
public enum PolicyOffer
{
    Adopted,

    /// <summary>C'est déjà la nôtre.</summary>
    Same,

    /// <summary>La nôtre est plus récente : c'est à nous de la renvoyer.</summary>
    Stale,

    Invalid,
    UnknownGroup,

    /// <summary>Un groupe sans clé : essai ou Public.</summary>
    NotPrivate,
}

/// <summary>Ce que le moteur demande pour propager les politiques.</summary>
public interface IGroupPolicies
{
    byte[]? CurrentPolicy(GroupId group);

    PolicyOffer OfferPolicy(GroupId group, ReadOnlySpan<byte> encoded);
}
```

Dans l'énumération `GroupAdmission`, ajouter avant `UnknownGroup` :

```csharp
    /// <summary>La politique bannit cette clé ou ce personnage.</summary>
    Banned,
```

Déclarer `GroupBook(IClock clock) : IGroupGate, IGroupPolicies`, et ajouter :

```csharp
    /// <summary>Levé hors du verrou, après <see cref="Changed"/>, quand une politique est adoptée.</summary>
    public event Action<GroupId>? PolicyAdopted;

    public byte[]? CurrentPolicy(GroupId id)
    {
        lock (_gate)
            return _groups.GetValueOrDefault(id)?.Policy is { } policy ? GroupPolicyCodec.Encode(policy) : null;
    }

    /// <summary>
    /// Adopte une politique si elle est valide et plus récente que la nôtre.
    /// </summary>
    /// <remarks>
    /// Le nom et les services du groupe suivent la politique : c'est elle qui
    /// fait foi, pas ce qu'on avait reçu à l'entrée.
    /// </remarks>
    public PolicyOffer OfferPolicy(GroupId id, ReadOnlySpan<byte> encoded)
    {
        lock (_gate)
        {
            if (_groups.TryGetValue(id, out var group) is false)
                return PolicyOffer.UnknownGroup;

            if (group.OwnerKey is not { } ownerKey)
                return PolicyOffer.NotPrivate;

            if (GroupPolicyRules.TryAccept(encoded, id, ownerKey, out var candidate, out _) is false)
                return PolicyOffer.Invalid;

            if (group.Policy is { } current && GroupPolicyRules.IsNewer(candidate!, current) is false)
                return current.Signature.AsSpan().SequenceEqual(candidate!.Signature) ? PolicyOffer.Same : PolicyOffer.Stale;

            _groups[id] = group with { Policy = candidate, Name = candidate!.Name, Rendezvous = candidate.Rendezvous };
        }

        Changed?.Invoke();
        PolicyAdopted?.Invoke(id);
        return PolicyOffer.Adopted;
    }
```

Dans `Admit`, juste après avoir trouvé le groupe sous le verrou, et avant toute autre chose :

```csharp
            // Un banni ne laisse aucune trace : ni épinglage, ni dernière vue.
            if (group.Policy?.IsBanned(key, member) is true)
                return GroupAdmission.Banned;
```

Toujours dans `Admit`, garder la clé complète :
- dans les branches qui épinglent (nouveau membre, ou membre connu sans `Id`) : `PublicKey = publicKey` ;
- dans la branche « déjà épinglé » (retour anticipé depuis le correctif de l'incrément 1) : **ne rien écrire sur `Disputed`** ; sur `Admitted`, si le membre n'a pas encore de `PublicKey`, la compléter et lever `Changed` (c'est un changement réel à enregistrer). Garder le retour anticipé pour le cas où il n'y a rien à écrire.

- [ ] **Step 5: Étendre `GroupBookCodec.cs`**

- `MemberDto` gagne en fin `string? PublicKey = null` ; `GroupDto` gagne en fin `string? OwnerKey = null, string? SigningKey = null, string? Policy = null`.
- À l'encodage : hex minuscules, `Policy` = `GroupPolicyCodec.Encode(record.Policy)`.
- Au décodage :
  - `OwnerKey` présent : exactement 33 octets, et `GroupId.Of(ownerKey) == Id`, sinon l'entrée est rejetée (rendre `null`).
  - `SigningKey` présent : au plus 1024 octets, et seulement si `OwnerKey` est présent, sinon l'entrée est rejetée.
  - `Policy` présent : `GroupPolicyRules.TryAccept(bytes, id, ownerKey, ...)` ; en cas d'échec, `Policy = null` sans rejeter le groupe. Si la politique est acceptée, le `Name` et les `Rendezvous` de l'enregistrement restent ceux stockés (ils ont été alignés à l'adoption).
  - `PublicKey` de membre présent : exactement 65 octets, sinon le membre est ignoré.

- [ ] **Step 6: Exclure les bannis dans `GroupDialPlanner.cs`**

Dans la boucle sur les `sightings`, ne retenir que les apparitions dont le groupe est connu et dont la politique ne bannit pas l'empreinte :

```csharp
                     .Where(sighting => sighting.Member != ours
                                        && byId.TryGetValue(sighting.Group, out var group)
                                        && group.Policy?.IsBanned(null, sighting.Member) is not true)
```

Et dans la boucle d'oubli de `_recent`, retirer aussi un membre que la politique courante bannit (`byId[entry.Group].Policy?.IsBanned(null, member) is true`, une fois vérifié que le groupe est encore là), pour que sa session se ferme au tic suivant. Documenter en commentaire.

- [ ] **Step 7: Vérifier que les tests passent, puis la suite complète**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter "GroupBookPolicyTests|GroupBookTests|GroupBookCodecTests|GroupDialPlannerTests"`
Expected: PASS.

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj && dotnet build Linkpearl/Linkpearl.csproj -c Release`
Expected: PASS, aucun warning.

- [ ] **Step 8: Commit**

```bash
git add Linkpearl/Core/Groups/ Linkpearl.Core.Tests/Groups/GroupBookPolicyTests.cs
git commit -m "feat(groupes): le groupe porte sa politique, les bannis sont refusés"
```

---

### Task 4: La gouvernance

**Files:**
- Create: `Linkpearl/Core/Groups/GroupGovernance.cs`
- Test: `Linkpearl.Core.Tests/Groups/GroupGovernanceTests.cs`

**Interfaces:**
- Consumes: tâches 1 et 3, `InvitationTicket`, `RendezvousAddress`, `CryptoPrimitives`.
- Produces:
  - `enum GroupRole { Member, Moderator, Owner }` (local).
  - `sealed record CreatedGroup(GroupRecord Record, InvitationTicket Code)`.
  - `static class GroupGovernance` :
    - `CreatedGroup Create(string name, string password, RendezvousAddress service, byte[] ownerIdentityKey, DateTimeOffset now)` (`ownerIdentityKey` : point de 65 octets) ;
    - `GroupRole RoleOf(GroupRecord group, byte[]? ourIdentityKey)` ;
    - `InvitationTicket? CodeOf(GroupRecord group)` ;
    - modifications, qui rendent la politique **encodée** et validée, à passer à `GroupBook.OfferPolicy` : `Rename(group, name, ECDsa? moderator)`, `NewCode(group, moderator)`, `SetPassword(group, password, moderator)`, `Ban(group, GroupBan ban, moderator)`, `Unban(group, GroupBan ban, moderator)`, `Dissolve(group)`, `SetAdmission(group, byte mode)`, `SetModerators(group, IReadOnlyList<byte[]> identityKeys)`.
    - Convention : `moderator == null` signifie « signé par la clé du groupe », ce qui exige `SigningKey`. Une modification refusée par les règles lève `InvalidOperationException` avec le motif.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
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
}
```

- [ ] **Step 2: Vérifier qu'ils échouent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupGovernanceTests`
Expected: échec de compilation.

- [ ] **Step 3: Écrire `GroupGovernance.cs`**

```csharp
using System.Security.Cryptography;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>Ce qu'on peut faire dans un groupe. Strictement local.</summary>
public enum GroupRole
{
    Member,
    Moderator,
    Owner,
}

public sealed record CreatedGroup(GroupRecord Record, InvitationTicket Code);

/// <summary>
/// Créer un groupe et en modifier la politique.
/// </summary>
/// <remarks>
/// Chaque modification rend la politique encodée, déjà passée par les règles
/// que les autres membres appliqueront : ce qui sort d'ici est ce qu'ils
/// accepteront. L'appelant la donne ensuite à <see cref="GroupBook.OfferPolicy"/>,
/// et le moteur la propage.
/// </remarks>
public static class GroupGovernance
{
    public static CreatedGroup Create(
        string name, string password, RendezvousAddress service, byte[] ownerIdentityKey, DateTimeOffset now)
    {
        if (GroupPolicyCodec.IsValidName(name) is false)
            throw new ArgumentException("nom de groupe invalide : 1 à 32 caractères", nameof(name));

        // Une clé propre au groupe, jamais une identité : un propriétaire peut
        // ainsi créer plusieurs groupes, chacun avec son identifiant.
        using var groupKey = CryptoPrimitives.GenerateIdentity();
        var ownerKey = GroupPolicySigning.CompressedKey(groupKey);
        var id = GroupId.Of(ownerKey);
        var code = InvitationTicket.Create();

        var attestation = GroupPolicySigning.SignAttestation(
            new GroupAttestation(
                id, 1, password.Length > 0 ? AdmissionMode.Password : AdmissionMode.Validation,
                CryptoPrimitives.Compress(ownerIdentityKey), [], []),
            groupKey);

        var policy = GroupPolicySigning.Sign(
            new GroupPolicy(id, 1, name, code.ToBytes(), [service], password, [], false, attestation, [], []),
            groupKey);

        Validated(policy, id, ownerKey);

        var record = new GroupRecord
        {
            Id = id,
            Name = name,
            Secret = RandomNumberGenerator.GetBytes(GroupDerivation.SecretSize),
            Rendezvous = [service],
            JoinedAt = now,
            OwnerKey = ownerKey,
            SigningKey = groupKey.ExportPkcs8PrivateKey(),
            Policy = policy,
        };

        return new CreatedGroup(record, code);
    }

    /// <summary>Notre rôle dans ce groupe.</summary>
    /// <param name="ourIdentityKey">Notre clé d'identité, point de 65 octets.</param>
    public static GroupRole RoleOf(GroupRecord group, byte[]? ourIdentityKey)
    {
        if (group.SigningKey is not null)
            return GroupRole.Owner;

        if (ourIdentityKey is null || group.Policy is not { } policy)
            return GroupRole.Member;

        return policy.IsModerator(CryptoPrimitives.Compress(ourIdentityKey)) ? GroupRole.Moderator : GroupRole.Member;
    }

    public static InvitationTicket? CodeOf(GroupRecord group)
        => group.Policy is { } policy ? InvitationTicket.FromBytes(policy.Code) : null;

    public static byte[] Rename(GroupRecord group, string name, ECDsa? moderator)
        => Edit(group, moderator, policy => policy with { Name = name });

    public static byte[] NewCode(GroupRecord group, ECDsa? moderator)
        => Edit(group, moderator, policy => policy with { Code = InvitationTicket.Create().ToBytes() });

    public static byte[] SetPassword(GroupRecord group, string password, ECDsa? moderator)
        => Edit(group, moderator, policy => policy with { Password = password });

    public static byte[] Ban(GroupRecord group, GroupBan ban, ECDsa? moderator)
        => Edit(group, moderator, policy => policy with { Bans = [.. policy.Bans.Where(existing => existing != ban), ban] });

    public static byte[] Unban(GroupRecord group, GroupBan ban, ECDsa? moderator)
        => Edit(group, moderator, policy => policy with { Bans = [.. policy.Bans.Where(existing => existing != ban)] });

    public static byte[] Dissolve(GroupRecord group)
        => Edit(group, null, policy => policy with { Dissolved = true });

    public static byte[] SetAdmission(GroupRecord group, byte mode)
        => Attest(group, attestation => attestation with { Admission = mode });

    /// <param name="identityKeys">Les clés d'identité des modérateurs, points de 65 octets.</param>
    public static byte[] SetModerators(GroupRecord group, IReadOnlyList<byte[]> identityKeys)
        => Attest(group, attestation => attestation with { Moderators = [.. identityKeys.Select(key => CryptoPrimitives.Compress(key))] });

    private static byte[] Edit(GroupRecord group, ECDsa? moderator, Func<GroupPolicy, GroupPolicy> change)
    {
        var current = Current(group);
        using var owner = moderator is null ? ImportGroupKey(group) : null;
        var next = GroupPolicySigning.Sign(change(current) with { Version = current.Version + 1 }, moderator ?? owner!);
        return Validated(next, group.Id, group.OwnerKey!);
    }

    private static byte[] Attest(GroupRecord group, Func<GroupAttestation, GroupAttestation> change)
    {
        var current = Current(group);
        using var owner = ImportGroupKey(group);

        var attestation = GroupPolicySigning.SignAttestation(
            change(current.Attestation) with { Version = current.Attestation.Version + 1 }, owner);

        var next = GroupPolicySigning.Sign(current with { Version = current.Version + 1, Attestation = attestation }, owner);
        return Validated(next, group.Id, group.OwnerKey!);
    }

    private static GroupPolicy Current(GroupRecord group)
        => group.Policy ?? throw new InvalidOperationException("ce groupe n'a pas encore reçu sa politique");

    private static ECDsa ImportGroupKey(GroupRecord group)
    {
        var signing = group.SigningKey ?? throw new InvalidOperationException("seul le propriétaire peut faire cela");
        var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(signing, out _);
        return key;
    }

    private static byte[] Validated(GroupPolicy policy, GroupId id, byte[] groupKey)
    {
        var encoded = GroupPolicyCodec.Encode(policy);

        if (GroupPolicyRules.TryAccept(encoded, id, groupKey, out _, out var why) is false)
            throw new InvalidOperationException(why);

        return encoded;
    }
}
```

- [ ] **Step 4: Vérifier que les tests passent, puis la suite complète**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupGovernanceTests`
Expected: PASS.

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Linkpearl/Core/Groups/GroupGovernance.cs Linkpearl.Core.Tests/Groups/GroupGovernanceTests.cs
git commit -m "feat(groupes): créer un groupe et en gouverner la politique"
```

---

### Task 5: L'hôte et le candidat d'admission

**Files:**
- Create: `Linkpearl/Core/Groups/AdmissionHost.cs`
- Create: `Linkpearl/Core/Groups/AdmissionCandidate.cs`
- Test: `Linkpearl.Core.Tests/Groups/AdmissionTests.cs`

**Interfaces:**
- Consumes: tâches 1 à 4, `MailboxAddress.IndexAt`, `IClock`.
- Produces:
  - `sealed record AdmissionOutbound(string CharacterName, ushort WorldId, IReadOnlyList<RendezvousAddress> Via, byte[] Payload)` : un dépôt à faire dans la boîte personnelle du candidat, sur ces services.
  - `sealed record PendingValidation(GroupId Group, string GroupName, byte[] Nonce, string CharacterName, ushort WorldId, DateTimeOffset LastSeen)`.
  - `sealed class AdmissionHost(GroupBook book, Func<byte[]?> ourIdentityKey, IClock clock) : IDisposable` : `MaxFailures = 5`, `ChallengeLifetime = 10 min`, `ValidationSilence = 3 min`, `IReadOnlyList<PendingValidation> Pending`, `IReadOnlyList<byte[]> AdmissionCodes`, `IReadOnlyList<AdmissionOutbound> OnRequest(AdmissionRequest, PlayerFingerprint candidate)`, `IReadOnlyList<AdmissionOutbound> OnProof(AdmissionProof)`, `AdmissionOutbound? Approve(byte[] nonce)`, `AdmissionOutbound? Decline(byte[] nonce)`. Appelable depuis plusieurs fils.
  - `enum CandidacyState { Idle, Waiting, NeedsPassword, Proving, Joined, Refused, Expired }` (local).
  - `sealed class AdmissionCandidate(IClock clock) : IDisposable` : `RedepositInterval = 1 min`, `Lifetime = 10 min`, `State`, `RefusalReason`, `Code`, `Service`, `LastJoinedName`, `byte[] Start(InvitationTicket code, RendezvousAddress service, string password, byte[] identityKey, string characterName, ushort worldId)`, `byte[]? DueRedeposit()`, `byte[]? OnChallenge(AdmissionChallenge)`, `bool OnWelcome(AdmissionWelcome)`, `void OnRefusal(AdmissionRefusal)`, `void Cancel()`, `GroupRecord? TakeJoined(DateTimeOffset now)`. Appelable depuis plusieurs fils.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Tests.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Groups;

public sealed class AdmissionTests : IDisposable
{
    private readonly ECDsa _owner = CryptoPrimitives.GenerateIdentity();
    private readonly ECDsa _candidateIdentity = CryptoPrimitives.GenerateIdentity();
    private readonly MovableClock _clock = new();
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        _owner.Dispose();
        _candidateIdentity.Dispose();

        foreach (var disposable in _disposables)
            disposable.Dispose();
    }

    private static readonly PlayerFingerprint Candidate = PlayerFingerprint.Of("jhalen tavari", 21);

    private byte[] OwnerPoint => CryptoPrimitives.ExportPublicPoint(_owner);
    private byte[] CandidatePoint => CryptoPrimitives.ExportPublicPoint(_candidateIdentity);

    /// <summary>Un membre qui tient le groupe, et son hôte.</summary>
    private (AdmissionHost Host, GroupBook Book, CreatedGroup Created) Member(string password = "lune", bool asOwner = false)
    {
        var created = GroupGovernance.Create("Compagnie", password, PolicyFixture.Service, OwnerPoint, _clock.UtcNow);
        var book = new GroupBook(_clock);
        book.Load([asOwner ? created.Record : created.Record with { SigningKey = null }]);

        using var stranger = CryptoPrimitives.GenerateIdentity();
        var ours = asOwner ? OwnerPoint : CryptoPrimitives.ExportPublicPoint(stranger);
        var host = new AdmissionHost(book, () => ours, _clock);
        _disposables.Add(host);
        return (host, book, created);
    }

    private AdmissionCandidate NewCandidate()
    {
        var candidate = new AdmissionCandidate(_clock);
        _disposables.Add(candidate);
        return candidate;
    }

    private static T Decode<T>(byte[] payload) where T : AdmissionMessage
    {
        Assert.True(AdmissionCodec.TryDecode(payload, out var message, out var why), why);
        return Assert.IsType<T>(message);
    }

    private byte[] Start(AdmissionCandidate candidate, CreatedGroup created, string password)
        => candidate.Start(created.Code, PolicyFixture.Service, password, CandidatePoint, "Jhalen Tavari", 21);

    [Fact]
    public void Le_bon_mot_de_passe_fait_entrer()
    {
        var (host, _, created) = Member();
        var candidate = NewCandidate();

        var request = Decode<AdmissionRequest>(Start(candidate, created, "lune"));
        var challenge = Assert.Single(host.OnRequest(request, Candidate));
        Assert.Equal("Jhalen Tavari", challenge.CharacterName);
        Assert.Equal([PolicyFixture.Service], challenge.Via);

        var proof = candidate.OnChallenge(Decode<AdmissionChallenge>(challenge.Payload));
        Assert.Equal(CandidacyState.Proving, candidate.State);

        var welcome = Assert.Single(host.OnProof(Decode<AdmissionProof>(proof!)));
        Assert.True(candidate.OnWelcome(Decode<AdmissionWelcome>(welcome.Payload)));
        Assert.Equal(CandidacyState.Joined, candidate.State);

        var record = candidate.TakeJoined(_clock.UtcNow);
        Assert.Equal(created.Record.Id, record!.Id);
        Assert.Equal(created.Record.Secret, record.Secret);
        Assert.Equal(created.Record.OwnerKey, record.OwnerKey);
        Assert.Equal([PolicyFixture.Service], record.Rendezvous);
        Assert.Null(record.Policy);
        Assert.Equal(CandidacyState.Idle, candidate.State);
        Assert.Equal("Compagnie", candidate.LastJoinedName);
    }

    [Fact]
    public void Un_mauvais_mot_de_passe_est_refuse_puis_le_sixieme_essai_est_bloque()
    {
        var (host, _, created) = Member();

        for (var attempt = 1; attempt <= AdmissionHost.MaxFailures + 1; attempt++)
        {
            var candidate = NewCandidate();
            var request = Decode<AdmissionRequest>(Start(candidate, created, "soleil"));
            var challenge = Assert.Single(host.OnRequest(request, Candidate));
            var proof = candidate.OnChallenge(Decode<AdmissionChallenge>(challenge.Payload));
            var refusal = Decode<AdmissionRefusal>(Assert.Single(host.OnProof(Decode<AdmissionProof>(proof!))).Payload);

            candidate.OnRefusal(refusal);
            Assert.Equal(CandidacyState.Refused, candidate.State);
            Assert.Equal(
                attempt <= AdmissionHost.MaxFailures ? RefusalReason.WrongPassword : RefusalReason.TooManyAttempts,
                refusal.Reason);
        }
    }

    [Fact]
    public void Un_candidat_banni_ne_recoit_aucune_reponse()
    {
        var created = GroupGovernance.Create("Compagnie", "lune", PolicyFixture.Service, OwnerPoint, _clock.UtcNow);
        var book = new GroupBook(_clock);
        book.Load([created.Record]);
        Assert.Equal(PolicyOffer.Adopted, book.OfferPolicy(created.Record.Id,
            GroupGovernance.Ban(created.Record, new GroupBan(null, Candidate), null)));

        using var host = new AdmissionHost(book, () => OwnerPoint, _clock);
        var request = Decode<AdmissionRequest>(Start(NewCandidate(), created, "lune"));

        Assert.Empty(host.OnRequest(request, Candidate));
    }

    [Fact]
    public void Une_demande_redeposee_ne_redefie_pas()
    {
        var (host, _, created) = Member();
        var candidate = NewCandidate();
        var request = Decode<AdmissionRequest>(Start(candidate, created, "lune"));

        Assert.Single(host.OnRequest(request, Candidate));
        Assert.Empty(host.OnRequest(request, Candidate));
    }

    [Fact]
    public void En_validation_la_demande_attend_un_moderateur()
    {
        var (host, _, created) = Member(password: "", asOwner: true);
        var candidate = NewCandidate();
        var request = Decode<AdmissionRequest>(Start(candidate, created, ""));

        Assert.Empty(host.OnRequest(request, Candidate));
        var pending = Assert.Single(host.Pending);
        Assert.Equal("Jhalen Tavari", pending.CharacterName);
        Assert.Equal("Compagnie", pending.GroupName);

        var welcome = host.Approve(pending.Nonce);
        Assert.True(candidate.OnWelcome(Decode<AdmissionWelcome>(welcome!.Payload)));
        Assert.Empty(host.Pending);
    }

    [Fact]
    public void En_validation_un_simple_membre_ne_voit_rien()
    {
        var (host, _, created) = Member(password: "");
        var request = Decode<AdmissionRequest>(Start(NewCandidate(), created, ""));

        Assert.Empty(host.OnRequest(request, Candidate));
        Assert.Empty(host.Pending);
        Assert.Empty(host.AdmissionCodes);
    }

    [Fact]
    public void Une_demande_refusee_ne_revient_pas()
    {
        var (host, _, created) = Member(password: "", asOwner: true);
        var candidate = NewCandidate();
        var request = Decode<AdmissionRequest>(Start(candidate, created, ""));

        host.OnRequest(request, Candidate);
        var refusal = host.Decline(Assert.Single(host.Pending).Nonce);
        candidate.OnRefusal(Decode<AdmissionRefusal>(refusal!.Payload));

        Assert.Equal(CandidacyState.Refused, candidate.State);
        Assert.Equal(RefusalReason.Declined, candidate.RefusalReason);

        // Le candidat redépose une minute plus tard, avant d'avoir lu le refus.
        _clock.Advance(TimeSpan.FromMinutes(1));
        host.OnRequest(request, Candidate);
        Assert.Empty(host.Pending);
    }

    [Fact]
    public void Une_demande_qui_n_est_plus_redeposee_disparait()
    {
        var (host, _, created) = Member(password: "", asOwner: true);
        host.OnRequest(Decode<AdmissionRequest>(Start(NewCandidate(), created, "")), Candidate);
        Assert.Single(host.Pending);

        _clock.Advance(AdmissionHost.ValidationSilence + TimeSpan.FromSeconds(1));
        Assert.Empty(host.Pending);
    }

    [Fact]
    public void Le_candidat_redepose_chaque_minute_puis_abandonne()
    {
        var (_, _, created) = Member();
        var candidate = NewCandidate();
        Start(candidate, created, "lune");

        Assert.Null(candidate.DueRedeposit());
        _clock.Advance(AdmissionCandidate.RedepositInterval);
        Assert.NotNull(candidate.DueRedeposit());
        Assert.Null(candidate.DueRedeposit());

        _clock.Advance(AdmissionCandidate.Lifetime);
        Assert.Null(candidate.DueRedeposit());
        Assert.Equal(CandidacyState.Expired, candidate.State);
    }

    [Fact]
    public void Sans_mot_de_passe_un_defi_demande_d_en_saisir_un()
    {
        var (host, _, created) = Member();
        var candidate = NewCandidate();
        var challenge = Assert.Single(host.OnRequest(Decode<AdmissionRequest>(Start(candidate, created, "")), Candidate));

        Assert.Null(candidate.OnChallenge(Decode<AdmissionChallenge>(challenge.Payload)));
        Assert.Equal(CandidacyState.NeedsPassword, candidate.State);
    }

    [Fact]
    public void Un_defi_d_une_autre_demande_est_ignore()
    {
        var (host, _, created) = Member();
        var first = NewCandidate();
        var other = NewCandidate();
        Start(first, created, "lune");
        var otherChallenge = Assert.Single(host.OnRequest(Decode<AdmissionRequest>(Start(other, created, "lune")), Candidate));

        Assert.Null(first.OnChallenge(Decode<AdmissionChallenge>(otherChallenge.Payload)));
        Assert.Equal(CandidacyState.Waiting, first.State);
    }

    [Fact]
    public void Un_groupe_dissous_n_admet_plus()
    {
        var created = GroupGovernance.Create("Compagnie", "lune", PolicyFixture.Service, OwnerPoint, _clock.UtcNow);
        var book = new GroupBook(_clock);
        book.Load([created.Record]);
        Assert.Equal(PolicyOffer.Adopted, book.OfferPolicy(created.Record.Id, GroupGovernance.Dissolve(created.Record)));

        using var host = new AdmissionHost(book, () => OwnerPoint, _clock);

        Assert.Empty(host.OnRequest(Decode<AdmissionRequest>(Start(NewCandidate(), created, "lune")), Candidate));
        Assert.Empty(host.AdmissionCodes);
    }
}
```

- [ ] **Step 2: Vérifier qu'ils échouent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter AdmissionTests`
Expected: échec de compilation.

- [ ] **Step 3: Écrire `AdmissionHost.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>Un dépôt à faire dans la boîte personnelle du candidat, sur ces services.</summary>
/// <remarks>Le candidat est désigné par nom et monde : c'est l'adaptateur qui en tire l'adresse.</remarks>
public sealed record AdmissionOutbound(string CharacterName, ushort WorldId, IReadOnlyList<RendezvousAddress> Via, byte[] Payload);

/// <summary>Une demande qui attend qu'un modérateur tranche.</summary>
public sealed record PendingValidation(
    GroupId Group, string GroupName, byte[] Nonce, string CharacterName, ushort WorldId, DateTimeOffset LastSeen);

/// <summary>
/// Le côté membre de l'admission.
/// </summary>
/// <remarks>
/// Appelé depuis le fil qui lit les boîtes et depuis l'interface : tout passe
/// sous un verrou. Le verrou du carnet de groupes est pris après le nôtre,
/// jamais l'inverse.
/// </remarks>
public sealed class AdmissionHost(GroupBook book, Func<byte[]?> ourIdentityKey, IClock clock) : IDisposable
{
    /// <summary>Au-delà, deviner le mot de passe au hasard ne vaut plus rien.</summary>
    public const int MaxFailures = 5;

    /// <summary>Le temps pour un candidat de répondre à un défi.</summary>
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Une demande en validation disparaît si elle n'est plus redéposée.
    /// </summary>
    /// <remarks>Le candidat redépose chaque minute : trois minutes de silence veulent dire qu'il a abandonné.</remarks>
    public static readonly TimeSpan ValidationSilence = TimeSpan.FromMinutes(3);

    /// <summary>Combien de temps on se souvient d'avoir déjà répondu à une demande.</summary>
    private static readonly TimeSpan AnswerMemory = TimeSpan.FromMinutes(15);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Challenge> _challenges = [];
    private readonly Dictionary<string, Waiting> _pending = [];
    private readonly Dictionary<string, DateTimeOffset> _answered = [];
    private readonly Dictionary<PeerId, (int Count, long Window)> _failures = [];

    private sealed record Challenge(GroupId Group, AdmissionRequest Request, ECDiffieHellman Ephemeral, DateTimeOffset Created);

    private sealed record Waiting(GroupId Group, AdmissionRequest Request, DateTimeOffset LastSeen);

    public IReadOnlyList<PendingValidation> Pending
    {
        get
        {
            lock (_gate)
            {
                Prune();

                return [.. _pending.Values
                    .OrderBy(waiting => waiting.LastSeen)
                    .Select(waiting => new PendingValidation(
                        waiting.Group, book.Find(waiting.Group)?.Name ?? "?", waiting.Request.Nonce,
                        waiting.Request.CharacterName, waiting.Request.WorldId, waiting.LastSeen))];
            }
        }
    }

    /// <summary>Les codes dont on ouvre la boîte d'admission : ceux des groupes qu'on peut admettre.</summary>
    /// <remarks>
    /// En mode validation, un simple membre n'y peut rien : il n'ouvre pas la
    /// boîte, et ne fait pas payer au service des dépôts qu'il jetterait.
    /// </remarks>
    public IReadOnlyList<byte[]> AdmissionCodes
        => [.. book.All.Where(CanAdmit).Select(group => group.Policy!.Code)];

    public IReadOnlyList<AdmissionOutbound> OnRequest(AdmissionRequest request, PlayerFingerprint candidate)
    {
        var group = Admitting(request.Code);

        if (group?.Policy is not { } policy)
            return [];

        var ours = ourIdentityKey();

        if (ours is not null && ours.AsSpan().SequenceEqual(request.PublicKey))
            return [];

        if (policy.IsBanned(PeerId.Of(request.PublicKey), candidate))
            return [];

        var key = Convert.ToHexStringLower(request.Nonce);

        lock (_gate)
        {
            Prune();

            if (_answered.ContainsKey(key))
                return [];

            if (policy.Attestation.Admission == AdmissionMode.Validation)
            {
                _pending[key] = new Waiting(group.Id, request, clock.UtcNow);
                return [];
            }

            if (_challenges.ContainsKey(key))
                return [];

            var ephemeral = CryptoPrimitives.GenerateEphemeral();
            _challenges[key] = new Challenge(group.Id, request, ephemeral, clock.UtcNow);

            var challenge = new AdmissionChallenge(request.Nonce, CryptoPrimitives.ExportPublicPoint(ephemeral));
            return [new AdmissionOutbound(request.CharacterName, request.WorldId, group.Rendezvous, AdmissionCodec.Encode(challenge))];
        }
    }

    public IReadOnlyList<AdmissionOutbound> OnProof(AdmissionProof proof)
    {
        var key = Convert.ToHexStringLower(proof.Nonce);
        Challenge? challenge;

        lock (_gate)
        {
            Prune();

            // Plusieurs membres défient le même candidat : la preuve désigne
            // celui qu'il a retenu par son éphémère, les autres l'ignorent.
            if (_challenges.TryGetValue(key, out challenge) is false
                || CryptoPrimitives.ExportPublicPoint(challenge.Ephemeral).AsSpan().SequenceEqual(proof.MemberEphemeral) is false)
                return [];

            _challenges.Remove(key);
            _answered[key] = clock.UtcNow;
        }

        using (challenge.Ephemeral)
        {
            var group = book.Find(challenge.Group);

            if (group?.Policy is not { Dissolved: false } policy || policy.Code.AsSpan().SequenceEqual(proof.Code) is false)
                return [];

            var request = challenge.Request;
            var candidate = PeerId.Of(request.PublicKey);
            var window = MailboxAddress.IndexAt(clock.UtcNow);

            lock (_gate)
            {
                if (_failures.TryGetValue(candidate, out var failures) && failures.Window == window && failures.Count >= MaxFailures)
                    return [Refuse(request, group, RefusalReason.TooManyAttempts)];
            }

            var sealingKey = AdmissionSealing.Key(challenge.Ephemeral, request.Ephemeral, request.Nonce, AdmissionSealing.Proof);
            var associated = AdmissionSealing.Associated(AdmissionKind.Proof, request.Nonce, proof.MemberEphemeral);

            if (AdmissionSealing.TryOpen(sealingKey, proof.SealedPassword, associated, out var password) is false)
                return [];

            if (CryptographicOperations.FixedTimeEquals(password, Encoding.UTF8.GetBytes(policy.Password)) is false)
            {
                lock (_gate)
                {
                    var previous = _failures.TryGetValue(candidate, out var f) && f.Window == window ? f.Count : 0;
                    _failures[candidate] = (previous + 1, window);
                }

                return [Refuse(request, group, RefusalReason.WrongPassword)];
            }

            return [Welcome(request, group, challenge.Ephemeral)];
        }
    }

    public AdmissionOutbound? Approve(byte[] nonce)
    {
        if (TakePending(nonce) is not { } waiting || book.Find(waiting.Group) is not { Policy.Dissolved: false } group)
            return null;

        using var ephemeral = CryptoPrimitives.GenerateEphemeral();
        return Welcome(waiting.Request, group, ephemeral);
    }

    public AdmissionOutbound? Decline(byte[] nonce)
    {
        if (TakePending(nonce) is not { } waiting || book.Find(waiting.Group) is not { } group)
            return null;

        return Refuse(waiting.Request, group, RefusalReason.Declined);
    }

    private Waiting? TakePending(byte[] nonce)
    {
        var key = Convert.ToHexStringLower(nonce);

        lock (_gate)
        {
            if (_pending.Remove(key, out var waiting) is false)
                return null;

            _answered[key] = clock.UtcNow;
            return waiting;
        }
    }

    private GroupRecord? Admitting(byte[] code)
        => book.All.FirstOrDefault(group => CanAdmit(group) && group.Policy!.Code.AsSpan().SequenceEqual(code));

    private bool CanAdmit(GroupRecord group)
        => group is { OwnerKey: not null, Policy: { Dissolved: false } policy }
           && (policy.Attestation.Admission == AdmissionMode.Password
               || GroupGovernance.RoleOf(group, ourIdentityKey()) is not GroupRole.Member);

    private static AdmissionOutbound Welcome(AdmissionRequest request, GroupRecord group, ECDiffieHellman ephemeral)
    {
        var memberPoint = CryptoPrimitives.ExportPublicPoint(ephemeral);
        var key = AdmissionSealing.Key(ephemeral, request.Ephemeral, request.Nonce, AdmissionSealing.Welcome);
        var grant = GroupGrantCodec.Encode(new GroupGrant(group.Id, group.Secret, group.OwnerKey!, group.Name));
        var sealedGrant = AdmissionSealing.Seal(key, grant, AdmissionSealing.Associated(AdmissionKind.Welcome, request.Nonce, memberPoint));

        return new AdmissionOutbound(
            request.CharacterName, request.WorldId, group.Rendezvous,
            AdmissionCodec.Encode(new AdmissionWelcome(request.Nonce, memberPoint, sealedGrant)));
    }

    private static AdmissionOutbound Refuse(AdmissionRequest request, GroupRecord group, byte reason)
        => new(request.CharacterName, request.WorldId, group.Rendezvous, AdmissionCodec.Encode(new AdmissionRefusal(request.Nonce, reason)));

    private void Prune()
    {
        var now = clock.UtcNow;

        foreach (var (key, challenge) in _challenges.ToList())
        {
            if (now - challenge.Created <= ChallengeLifetime)
                continue;

            challenge.Ephemeral.Dispose();
            _challenges.Remove(key);
        }

        foreach (var (key, waiting) in _pending.ToList())
            if (now - waiting.LastSeen > ValidationSilence)
                _pending.Remove(key);

        foreach (var (key, at) in _answered.ToList())
            if (now - at > AnswerMemory)
                _answered.Remove(key);

        var window = MailboxAddress.IndexAt(now);

        foreach (var (candidate, failures) in _failures.ToList())
            if (failures.Window != window)
                _failures.Remove(candidate);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var challenge in _challenges.Values)
                challenge.Ephemeral.Dispose();

            _challenges.Clear();
        }
    }
}
```

- [ ] **Step 4: Écrire `AdmissionCandidate.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>Où en est une candidature. Strictement local.</summary>
public enum CandidacyState
{
    Idle,

    /// <summary>Demande déposée, en attente d'un défi, d'un modérateur ou d'une réponse.</summary>
    Waiting,

    /// <summary>Un membre a défié, mais nous n'avons pas de mot de passe à lui donner.</summary>
    NeedsPassword,

    Proving,
    Joined,
    Refused,

    /// <summary>Dix minutes sans réponse : personne d'autorisé n'était en ligne.</summary>
    Expired,
}

/// <summary>
/// Le côté candidat de l'admission. Une seule candidature à la fois.
/// </summary>
/// <remarks>
/// Appelé depuis le fil qui lit les boîtes, la boucle de rafraîchissement et
/// l'interface : tout passe sous un verrou.
/// </remarks>
public sealed class AdmissionCandidate(IClock clock) : IDisposable
{
    /// <summary>Les boîtes ne gardent rien : un modérateur qui se connecte doit pouvoir voir la demande.</summary>
    public static readonly TimeSpan RedepositInterval = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private ECDiffieHellman? _ephemeral;
    private AdmissionRequest? _request;
    private GroupGrant? _grant;
    private string _password = "";
    private CandidacyState _state = CandidacyState.Idle;
    private DateTimeOffset _started;
    private DateTimeOffset _lastSent;

    public CandidacyState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public byte RefusalReason { get; private set; }

    public InvitationTicket? Code { get; private set; }

    public RendezvousAddress? Service { get; private set; }

    /// <summary>Le nom du dernier groupe rejoint, pour que l'interface puisse le dire.</summary>
    public string? LastJoinedName { get; private set; }

    /// <summary>Commence une candidature et rend la demande à déposer.</summary>
    /// <param name="identityKey">Notre clé d'identité, point de 65 octets.</param>
    public byte[] Start(
        InvitationTicket code, RendezvousAddress service, string password, byte[] identityKey,
        string characterName, ushort worldId)
    {
        lock (_gate)
        {
            Reset();

            _ephemeral = CryptoPrimitives.GenerateEphemeral();
            _request = new AdmissionRequest(
                code.ToBytes(), identityKey, CryptoPrimitives.ExportPublicPoint(_ephemeral),
                RandomNumberGenerator.GetBytes(AdmissionCodec.NonceLength), worldId, characterName);

            _password = password;
            Code = code;
            Service = service;
            LastJoinedName = null;
            _state = CandidacyState.Waiting;
            _started = _lastSent = clock.UtcNow;

            return AdmissionCodec.Encode(_request);
        }
    }

    /// <summary>La demande à redéposer, si c'est le moment.</summary>
    public byte[]? DueRedeposit()
    {
        lock (_gate)
        {
            if (_state is not CandidacyState.Waiting || _request is null)
                return null;

            var now = clock.UtcNow;

            if (now - _started >= Lifetime)
            {
                _state = CandidacyState.Expired;
                DropEphemeral();
                return null;
            }

            if (now - _lastSent < RedepositInterval)
                return null;

            _lastSent = now;
            return AdmissionCodec.Encode(_request);
        }
    }

    /// <summary>Répond au premier défi reçu par la preuve à déposer.</summary>
    public byte[]? OnChallenge(AdmissionChallenge challenge)
    {
        lock (_gate)
        {
            if (_state is not CandidacyState.Waiting || _request is null || _ephemeral is null
                || challenge.Nonce.AsSpan().SequenceEqual(_request.Nonce) is false)
                return null;

            if (_password.Length == 0)
            {
                _state = CandidacyState.NeedsPassword;
                return null;
            }

            var key = AdmissionSealing.Key(_ephemeral, challenge.MemberEphemeral, _request.Nonce, AdmissionSealing.Proof);
            var sealedPassword = AdmissionSealing.Seal(
                key, Encoding.UTF8.GetBytes(_password),
                AdmissionSealing.Associated(AdmissionKind.Proof, _request.Nonce, challenge.MemberEphemeral));

            _state = CandidacyState.Proving;
            return AdmissionCodec.Encode(new AdmissionProof(_request.Code, _request.Nonce, challenge.MemberEphemeral, sealedPassword));
        }
    }

    public bool OnWelcome(AdmissionWelcome welcome)
    {
        lock (_gate)
        {
            if (_state is not (CandidacyState.Waiting or CandidacyState.Proving) || _request is null || _ephemeral is null
                || welcome.Nonce.AsSpan().SequenceEqual(_request.Nonce) is false)
                return false;

            var key = AdmissionSealing.Key(_ephemeral, welcome.MemberEphemeral, _request.Nonce, AdmissionSealing.Welcome);

            if (AdmissionSealing.TryOpen(key, welcome.SealedGrant,
                    AdmissionSealing.Associated(AdmissionKind.Welcome, _request.Nonce, welcome.MemberEphemeral), out var plain) is false
                || GroupGrantCodec.TryDecode(plain, out var grant, out _) is false)
                return false;

            _grant = grant;
            _state = CandidacyState.Joined;
            DropEphemeral();
            return true;
        }
    }

    public void OnRefusal(AdmissionRefusal refusal)
    {
        lock (_gate)
        {
            if (_state is not (CandidacyState.Waiting or CandidacyState.Proving) || _request is null
                || refusal.Nonce.AsSpan().SequenceEqual(_request.Nonce) is false)
                return;

            _state = CandidacyState.Refused;
            RefusalReason = refusal.Reason;
            DropEphemeral();
        }
    }

    /// <summary>Le groupe rejoint, une seule fois ; la candidature revient au repos.</summary>
    /// <remarks>Sans politique : elle arrive par la première session avec un membre.</remarks>
    public GroupRecord? TakeJoined(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_state is not CandidacyState.Joined || _grant is not { } grant || Service is not { } service)
                return null;

            Reset();
            LastJoinedName = grant.Name;

            return new GroupRecord
            {
                Id = grant.Group,
                Name = grant.Name,
                Secret = grant.Secret,
                OwnerKey = grant.OwnerKey,
                Rendezvous = [service],
                JoinedAt = now,
            };
        }
    }

    public void Cancel()
    {
        lock (_gate)
            Reset();
    }

    private void Reset()
    {
        DropEphemeral();
        _request = null;
        _grant = null;
        _password = "";
        _state = CandidacyState.Idle;
        RefusalReason = 0;
    }

    private void DropEphemeral()
    {
        _ephemeral?.Dispose();
        _ephemeral = null;
    }

    public void Dispose()
    {
        lock (_gate)
            DropEphemeral();
    }
}
```

- [ ] **Step 5: Vérifier que les tests passent, puis la suite complète**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter AdmissionTests`
Expected: PASS.

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Linkpearl/Core/Groups/AdmissionHost.cs Linkpearl/Core/Groups/AdmissionCandidate.cs Linkpearl.Core.Tests/Groups/AdmissionTests.cs
git commit -m "feat(groupes): admission par mot de passe ou par validation"
```

---

### Task 6: Le moteur propage la politique

**Files:**
- Modify: `Linkpearl/Core/Protocol/MessageKind.cs`
- Modify: `Linkpearl/Core/Sync/SyncEngine.cs`
- Test: `Linkpearl.Core.Tests/Sync/GroupPolicySyncTests.cs`

**Interfaces:**
- Consumes: `IGroupPolicies`, `PolicyOffer` (tâche 3), `GroupGovernance` (tâche 4), les doublures et le montage de `GroupSyncEngineTests.cs`.
- Produces:
  - `MessageKind.GroupPolicy = 0x10` : charge `GroupId (16) || politique encodée`.
  - `SyncEngine(..., IGroupGate? groups = null, IGroupPolicies? policies = null)`.
  - `PeerStatus` gagne en dernier paramètre `GroupId? Group = null`.
  - `internal static bool CarriesGroupMessages(PairRecord pair) => pair.Group is not null`.

Comportement :
- À l'ouverture d'une session de groupe, et à chaque tic : si la politique courante de ce groupe diffère de la dernière envoyée sur cette session, l'envoyer.
- `PumpAsync` : un `MessageKind.GroupPolicy` venu d'une session de groupe est rangé dans une file du runtime (`ConcurrentQueue<byte[]>`), jamais traité sur le fil de la pompe ; venu d'une paire directe, il est ignoré.
- Une nouvelle étape du tic, `ExchangePoliciesAsync`, placée juste après `AdoptFinishedDialsAsync`, vide la file de chaque runtime de groupe :
  - rejette une charge de 16 octets ou moins, ou dont l'identifiant n'est pas celui du groupe de la session ;
  - `OfferPolicy` ; sur `Stale`, oublie la dernière envoyée pour forcer le renvoi de la nôtre ;
  - puis envoie la courante si elle diffère de la dernière envoyée.
- `TearDownAsync` oublie la dernière envoyée et vide la file.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `Linkpearl.Core.Tests/Sync/GroupPolicySyncTests.cs` en reprenant **à l'identique** le montage de `GroupSyncEngineTests.cs` (répertoire temporaire, `MovableClock`, stores, blob, manifeste, `MeetingPoint`, `SettleAsync` qui avance l'horloge de 50 ms par tour, `IDisposable`), avec trois différences :
- le groupe est privé, créé par `GroupGovernance.Create("Compagnie", "lune", new RendezvousAddress("rdv.exemple.ch", 47900), <point d'Alice>, now)` ;
- chaque moteur reçoit `groups: xxxGroups, policies: xxxGroups` ;
- les carnets de groupes d'Alice et de Bob tiennent des versions différentes de la politique.

`WorldAsync(bool aliceRenames = false, bool bobIsAhead = false)` : Alice crée le groupe (`created`) et charge `created.Record` ; Bob charge `created.Record with { SigningKey = null }`. On calcule `v2 = GroupGovernance.Rename(created.Record, "Nouvelle", null)` ; si `aliceRenames`, c'est Alice qui l'adopte (`OfferPolicy`) ; si `bobIsAhead`, c'est Bob. Les deux moteurs reçoivent leur plan par `GroupDialPlanner.Plan` comme dans `GroupSyncEngineTests`. Le record `World` expose `AliceGroups`, `BobGroups` et `Group` (l'identifiant).

Tests :

```csharp
    [Fact]
    public async Task La_plus_recente_gagne_des_deux_cotes()
    {
        // Alice est propriétaire et renomme ; Bob n'a que la version 1.
        await using var world = await WorldAsync(aliceRenames: true);

        Assert.True(await SettleAsync(world, () => world.BobGroups.Find(world.Group)!.Policy!.Version == 2),
            "Bob n'a jamais reçu la version 2");
        Assert.Equal("Nouvelle", world.BobGroups.Find(world.Group)!.Name);
        Assert.Equal(2UL, world.AliceGroups.Find(world.Group)!.Policy!.Version);
    }

    [Fact]
    public async Task Une_politique_plus_ancienne_recue_ne_remplace_pas_la_notre()
    {
        // Bob a la version 2, Alice seulement la 1 : Alice doit monter, Bob ne pas redescendre.
        await using var world = await WorldAsync(bobIsAhead: true);

        Assert.True(await SettleAsync(world, () => world.AliceGroups.Find(world.Group)!.Policy!.Version == 2),
            "Alice n'a jamais reçu la version 2");
        Assert.Equal(2UL, world.BobGroups.Find(world.Group)!.Policy!.Version);
    }

    [Fact]
    public void Une_paire_directe_ne_porte_pas_de_message_de_groupe()
    {
        var group = GroupBookTests.Group([.. Enumerable.Range(0, 32).Select(i => (byte)i)], _clock.UtcNow);
        var member = Assert.Single(new GroupDialPlanner(_clock)
            .Plan(BobPrint, [new GroupSighting(group.Id, AlicePrint, "Alice")], [group], []));

        Assert.True(SyncEngine.CarriesGroupMessages(member));
        Assert.False(SyncEngine.CarriesGroupMessages(member with { Group = null }));
    }
```

- [ ] **Step 2: Vérifier qu'ils échouent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupPolicySyncTests`
Expected: échec de compilation (paramètre `policies`, `CarriesGroupMessages`).

- [ ] **Step 3: Ajouter le message**

Dans `MessageKind.cs`, après `Unpair` :

```csharp
    /// <summary>
    /// La politique signée d'un groupe : identifiant (16) puis politique encodée.
    /// </summary>
    /// <remarks>
    /// Envoyée à l'ouverture de chaque session de groupe et à chaque nouvelle
    /// version. Le receveur garde la plus récente et renvoie la sienne si elle
    /// l'est davantage. Un client qui ne le connaît pas l'ignore.
    /// </remarks>
    public const byte GroupPolicy = 0x10;
```

- [ ] **Step 4: Brancher le moteur**

Dans `SyncEngine.cs` :
- champ `private readonly IGroupPolicies? _policies;`, paramètre de constructeur `IGroupPolicies? policies = null` après `groups`.
- `Runtime` gagne `public ConcurrentQueue<byte[]> InboundPolicies { get; } = new();` et `public byte[]? SentPolicy { get; set; }`.
- `CarriesGroupMessages` comme ci-dessus, documenté (même raisonnement qu'`EndsOnUnpair`).
- `PumpAsync`, avant l'appel à `exchange.HandleAsync` :

```csharp
                // Ramassé au tic suivant, comme l'avis de retrait : c'est le tic
                // qui touche au carnet de groupes, pas ce fil-ci.
                if (message.Kind == MessageKind.GroupPolicy)
                {
                    if (CarriesGroupMessages(runtime.Pair))
                        runtime.InboundPolicies.Enqueue(message.Payload);

                    continue;
                }
```

- nouvelle étape, appelée dans `TickAsync` juste après `AdoptFinishedDialsAsync` :

```csharp
    /// <summary>
    /// Échange les politiques de groupe sur chaque session de groupe.
    /// </summary>
    /// <remarks>
    /// Chaque côté envoie la sienne à l'ouverture, garde la plus récente des
    /// deux, et renvoie la sienne s'il reçoit plus ancien : deux membres qui se
    /// croisent repartent avec la même.
    /// </remarks>
    private async Task ExchangePoliciesAsync(CancellationToken ct)
    {
        if (_policies is null)
            return;

        foreach (var runtime in _runtimes.Values)
        {
            if (runtime is not { Pair.Group: { } origin, Session: { } session })
                continue;

            while (runtime.InboundPolicies.TryDequeue(out var payload))
            {
                if (payload.Length <= GroupId.SizeInBytes
                    || GroupId.FromBytes(payload.AsSpan(0, GroupId.SizeInBytes)) != origin.Group)
                    continue;

                if (_policies.OfferPolicy(origin.Group, payload.AsSpan(GroupId.SizeInBytes)) is PolicyOffer.Stale)
                    runtime.SentPolicy = null;
            }

            if (_policies.CurrentPolicy(origin.Group) is not { } current
                || runtime.SentPolicy is { } sent && sent.AsSpan().SequenceEqual(current))
                continue;

            try
            {
                byte[] message = [.. origin.Group.ToBytes(), .. current];
                await session.SendAsync(ChannelPlan.ControlChannel, MessageKind.GroupPolicy, message, ct).ConfigureAwait(false);
                runtime.SentPolicy = current;
            }
            catch (Exception e)
            {
                _log.Warning($"{runtime.Pair.DisplayName} : politique de groupe non envoyée.", e);
            }
        }
    }
```

- `TearDownAsync` : `runtime.SentPolicy = null;` et vider `InboundPolicies`.
- `Statuses` : passer `entry.Value.Pair.Group?.Group` en dernier argument de `PeerStatus`.
- `PeerStatus` : ajouter `GroupId? Group = null` en dernier paramètre.

- [ ] **Step 5: Vérifier que les tests passent, puis la suite complète et la compilation**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter "GroupPolicySyncTests|GroupSyncEngineTests|SyncEngineTests"`
Expected: PASS.

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj && dotnet build Linkpearl/Linkpearl.csproj -c Release`
Expected: PASS, aucun warning.

- [ ] **Step 6: Commit**

```bash
git add Linkpearl/Core/Protocol/MessageKind.cs Linkpearl/Core/Sync/SyncEngine.cs Linkpearl.Core.Tests/Sync/GroupPolicySyncTests.cs
git commit -m "feat(sync): la politique de groupe se propage de session en session"
```

---

### Task 7: La sauvegarde porte les groupes

**Files:**
- Modify: `Linkpearl/Core/Identity/IdentityBackup.cs`
- Modify: `Linkpearl/Integration/IdentityBackupService.cs`
- Test: `Linkpearl.Core.Tests/Identity/IdentityBackupTests.cs`

**Interfaces:**
- Produces:
  - `BackupEntry(string Folder, byte[] Identity, byte[] Pairs, byte[]? Groups = null)`.
  - `IdentityBackup` écrit la version 2 : chaque entrée se termine par `taille groupes (4, LE) | groupes` (taille 0 quand `Groups` est nul, et `Groups` relu à `null` dans ce cas). La lecture accepte 1 (pas de champ, `Groups = null`) et 2 ; une version supérieure reste refusée avec le message existant. Borne `MaxGroupsLength = 4 Mio`, vérifiée dans `Validate`.
  - `IdentityBackupService.Collect` lit `groups.json` en clair (`new GroupStore(path).ReadPlain()`, `null` si absent). `Restore` vérifie `GroupBookCodec.IsValid(entry.Groups)` avant toute écriture (refus : « sauvegarde refusée : une liste de groupes est illisible. »), puis, après la mise de côté des fichiers existants, écrit `groups.json` par `GroupStore.WritePlain` quand `Groups` n'est pas nul.

- [ ] **Step 1: Écrire les tests qui échouent**

Dans `IdentityBackupTests.cs`, ajouter :

```csharp
    [Fact]
    public void Les_groupes_font_l_aller_retour()
    {
        var withGroups = Alice with { Groups = """[{"Id":"00"}]"""u8.ToArray() };

        var read = IdentityBackup.Read(IdentityBackup.Write([withGroups, Bob], password: null), password: null);

        Assert.Null(read.Failure);
        Assert.Equal(withGroups.Groups, read.Entries[0].Groups);
        Assert.Null(read.Entries[1].Groups);
    }

    [Fact]
    public void Une_sauvegarde_v1_se_relit()
    {
        var read = IdentityBackup.Read(VersionOne(Alice), password: null);

        Assert.Null(read.Failure);
        var entry = Assert.Single(read.Entries);
        Assert.Equal(Alice.Identity, entry.Identity);
        Assert.Equal(Alice.Pairs, entry.Pairs);
        Assert.Null(entry.Groups);
    }
```

`VersionOne(BackupEntry)` fabrique à la main un fichier de version 1 en mode clair (mode 0) : **lire d'abord `IdentityBackup.cs`** pour reproduire exactement la disposition de la charge v1 (le commentaire du fichier dit « nombre (2) | { dossier (16) | taille clé (2) | clé | taille carnet (4) | carnet }* », en little-endian ; vérifier comment le dossier de 16 caractères hexadécimaux est écrit : 16 octets ASCII ou 8 octets binaires) et celle de l'en-tête et du condensé (« LPBK » | version | mode | charge | SHA-256(charge)). Adapter aussi aux vraies formes des champs `Alice` et `Bob` du fichier de test.

- [ ] **Step 2: Vérifier qu'ils échouent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter IdentityBackupTests`
Expected: échec de compilation (`Groups`).

- [ ] **Step 3: Implémenter la version 2**

Dans `IdentityBackup.cs` : `Version = 2`, une constante `private const byte FirstVersion = 1;`, accepter `file[4] is FirstVersion or Version`, passer la version lue au décodage de la charge, lire le champ des groupes seulement en version 2, écrire toujours la version 2, mettre à jour le commentaire de disposition et ajouter la borne. Garder le refus de tout octet résiduel.

Dans `IdentityBackupService.cs` : collecte et restauration comme décrit dans les interfaces. `GroupBookCodec.IsValid` existe depuis l'incrément 1.

- [ ] **Step 4: Vérifier, suite complète, compilation**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj && dotnet build Linkpearl/Linkpearl.csproj -c Release`
Expected: PASS, aucun warning.

- [ ] **Step 5: Commit**

```bash
git add Linkpearl/Core/Identity/IdentityBackup.cs Linkpearl/Integration/IdentityBackupService.cs Linkpearl.Core.Tests/Identity/IdentityBackupTests.cs
git commit -m "feat(identite): la sauvegarde en un fichier porte aussi les groupes"
```

---

### Task 8: Les boîtes d'admission dans la présence

**Files:**
- Modify: `Linkpearl/Core/Transport/Rendezvous/RendezvousClient.cs`
- Modify: `Linkpearl/Integration/PresenceService.cs`

**Interfaces:**
- Consumes: `AdmissionHost`, `AdmissionCandidate`, `AdmissionCodec`, `AdmissionKind`, `GroupDerivation.AdmissionAround`/`AdmissionAddress`, `InvitationTicket`.
- Produces (sur `PresenceService`) :
  - `void Attach(AdmissionHost host, AdmissionCandidate candidate)` : appelé une fois par le plugin.
  - `Task<string> JoinGroupAsync(InvitationTicket code, RendezvousAddress service, string password, NearbyPlayer self, CancellationToken ct)` : rend un message pour le joueur.
  - `Task<string> AnswerAsync(AdmissionOutbound outbound, CancellationToken ct)` : dépose une réponse de l'hôte (utilisé par l'interface pour Accepter et Refuser).

Ce fichier vit dans l'adaptateur et ne se teste pas sous Linux : la vérification est la compilation, la relecture et l'essai en jeu.

- [ ] **Step 1: « destinataire absent » n'interrompt plus la présence**

Dans `RendezvousClient.ListenAsync`, sur une trame `Error` : lire le texte (le motif existe déjà dans `RegisterInvitationAsync` : `[RendezvousKind.Error, .. var reason]`). Si le texte vaut exactement `"destinataire absent"`, ignorer la trame (c'est la réponse du service à un dépôt vers une boîte fermée, que personne n'attend) ; sinon, comportement actuel (libérer l'interrogation en cours avec `null`). Commenter le pourquoi : les redépôts d'admission rendent ce cas fréquent, et il faisait passer une interrogation de présence pour « sans réponse ». Si `Linkpearl.Core.Tests/Rendezvous/` contient déjà une infrastructure pour tester `RendezvousClient` contre un faux serveur, ajouter un test ; sinon, le dire dans le rapport.

- [ ] **Step 2: Aiguiller les dépôts**

`OnDelivered` lit `payload[0]` avant tout :
- `0x01` à `0x04` : chemin actuel de `PairRequestMessage`, inchangé ;
- `AdmissionKind.IsAdmission(payload[0])` : `AdmissionCodec.TryDecode`, puis :
  - `AdmissionRequest` → `host.OnRequest(request, PlayerFingerprint.Of(DalamudObjectSource.Normalize(request.CharacterName), request.WorldId))`, et chaque `AdmissionOutbound` rendu est déposé (étape 4) ;
  - `AdmissionProof` → `host.OnProof(proof)`, idem ;
  - `AdmissionChallenge` → `candidate.OnChallenge(challenge)` ; si une preuve est rendue, la déposer dans `GroupDerivation.AdmissionAddress(candidate.Code.ToBytes(), now)` sur `candidate.Service` ;
  - `AdmissionWelcome` → `candidate.OnWelcome(welcome)` ;
  - `AdmissionRefusal` → `candidate.OnRefusal(refusal)`.
- Tout le reste : journaliser au niveau Debug que le dépôt est inconnu, sans nom.
- `OnDelivered` tourne sur le fil d'écoute : les dépôts de réponse partent par `Task.Run`, jamais en bloquant ce fil. Journaliser un échec avec `_log.Warning(e, "...")` sans nom de personnage.
- Le dédoublonnage : une même demande arrive par autant de services que nous en partageons avec le candidat. L'hôte dédoublonne déjà par aléa ; ne rien ajouter.

- [ ] **Step 3: Ouvrir les boîtes d'admission**

- `Addresses(fingerprint, groups)` ajoute `GroupDerivation.AdmissionAround(code, now)` pour chaque code de `host.AdmissionCodes`.
- La signature d'ouverture suit aussi les codes : remplacer l'ensemble `OpenedGroups` (`IReadOnlySet<GroupId>`) par un ensemble de clés texte, `"g:<GroupId>"` pour chaque groupe et `"c:<code hex>"` pour chaque code admis. Garder la règle actuelle : un ensemble qui **rétrécit** ferme la connexion (seule façon de retirer une boîte), un ensemble qui grandit ou une nouvelle fenêtre rouvre. Adapter `Signature` en conséquence.
- `Reconcile` ajoute `candidate.Service` aux services actifs tant que la candidature est `Waiting`, `NeedsPassword` ou `Proving`.

- [ ] **Step 4: Déposer vers un candidat, rejoindre, redéposer**

- Méthode privée `DepositOnAsync(IReadOnlyList<RendezvousAddress> via, byte[] address, byte[] payload, CancellationToken ct)` : comme `DepositEverywhereAsync`, mais seulement sur les sessions dont `At` est dans `via`. Refuser (et journaliser) une charge de plus de `RendezvousWire.MaxDepositLength` octets avant tout envoi : le service fermerait la connexion.
- `AnswerAsync(outbound)` : adresse `MailboxAddress.Of(PlayerFingerprint.Of(Normalize(outbound.CharacterName), outbound.WorldId), now)`, dépôt sur `outbound.Via`.
- `JoinGroupAsync` :
  - refuse si `Discoverable` est faux : « Activez la détection dans les réglages : c'est par votre boîte que le groupe vous répond. » ;
  - refuse si aucune identité ;
  - `candidate.Start(code, service, password, identity.PublicKey, self.Name, self.WorldId)` ;
  - `EnsureOpenAsync(self.Fingerprint, ct)` pour ouvrir au besoin une session vers `service` (que `Reconcile` ajoute désormais) ;
  - dépose la demande dans `GroupDerivation.AdmissionAddress(code.ToBytes(), now)` sur `service` ; si aucun dépôt n'est parti, le dire ;
  - rend « Demande envoyée. Un membre du groupe doit être en ligne pour vous répondre. ».
- Dans `EnsureOpenAsync`, après l'ouverture des sessions : `if (candidate.DueRedeposit() is { } again)` déposer de nouveau dans la boîte d'admission du code, sur `candidate.Service`.

- [ ] **Step 5: Compiler et commit**

Run: `dotnet build Linkpearl/Linkpearl.csproj -c Release && dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: succès, aucun warning ; suite inchangée ou augmentée.

```bash
git add Linkpearl/Core/Transport/Rendezvous/RendezvousClient.cs Linkpearl/Integration/PresenceService.cs
git commit -m "feat(presence): boîtes d'admission, réponses aux candidats et redépôts"
```

---

### Task 9: Le plugin branche les groupes privés

**Files:**
- Create: `Linkpearl/Ui/GroupActions.cs`
- Modify: `Linkpearl/Plugin.cs`
- Modify: `Linkpearl/Core/Sync/NameplateMark.cs`, `Linkpearl/Ui/NameplateGlyphs.cs`, `Linkpearl/Ui/NameplateLegend.cs`, `Linkpearl/Integration/StatusBarEntry.cs`
- Test: `Linkpearl.Core.Tests/Sync/NameplateMarkTests.cs`

**Interfaces:**
- Produces:
  - `sealed class GroupActions` (dans `Linkpearl/Ui`, instanciée par le plugin), avec des propriétés `required` que le plugin remplit et que la page appelle :
    - `Action<string, string> Create` (nom, mot de passe) ;
    - `Action<string, string> Join` (texte du code, mot de passe) ; `Action CancelJoin` ;
    - `Action<GroupId> Leave` ; `Action<GroupId> Forget` (retirer de la liste un groupe dissous) ;
    - `Action<GroupId, Func<GroupRecord, ECDsa?, byte[]>> Edit` : applique une modification de gouvernance, en passant `null` pour signer comme propriétaire et notre clé d'identité pour signer comme modérateur ;
    - `Action<PendingValidation> Approve`, `Action<PendingValidation> Decline` ;
    - `Action<GroupId, PlayerFingerprint, bool> SetPaused`, `Action<GroupId, PlayerFingerprint, TransientCategories> SetReceive` ;
    - `Func<byte[]?> OurIdentityKey`.
  - `NameplateMark.GroupMember` : un membre de groupe connecté ou dont l'apparence est posée.

- [ ] **Step 1: Test du symbole de plaque de nom**

Dans `NameplateMarkTests.cs`, ajouter un test : un `PeerStatus` avec `Group` non nul, état `Connected`, et `View.Fingerprint` égal à une empreinte visible, donne `NameplateMark.GroupMember` pour cette empreinte ; un pair du carnet sur une autre empreinte garde `Online`. Lire d'abord le fichier de test pour reprendre la façon dont il construit `PeerStatus` et `PeerView`.

- [ ] **Step 2: Le symbole**

- `NameplateMark` gagne `GroupMember` ; `NameplateMarks.Build` pose `GroupMember` pour chaque statut de groupe (`status.Group is not null`) connecté ou appliqué, sur `status.View.Fingerprint`, **avant** la couche du carnet (un pair du carnet l'emporte).
- `NameplateGlyphs.ColorOf(GroupMember)` : `Theme.Hex(0xB48CF2)`.
- `NameplateLegend` : une ligne « membre d'un de vos groupes ».

- [ ] **Step 3: Le plugin**

Dans `Plugin.cs` :
- **Retirer `/lpgroupe`**, `OnGroupTest`, `_testGroup`, la constante et le retrait de la commande dans `Dispose`. La commande était annoncée temporaire jusqu'à cet incrément.
- Créer `_admissionHost = new AdmissionHost(_groups, () => _pairing.Identity?.PublicKey, clock)` et `_candidate = new AdmissionCandidate(clock)`, les passer à `_presence.Attach(...)`, les libérer dans `Dispose`.
- Passer `policies: _groups` au moteur.
- `_groups.PolicyAdopted += OnPolicyAdopted`. Le gestionnaire, levé hors du fil du jeu :
  - lit le groupe ; si la politique nous bannit (`PeerId` de notre identité, ou `_state.Self?.Fingerprint`) et que nous ne sommes pas propriétaire : `_groups.Remove(id)` et `Report($"Vous avez été exclu du groupe {nom}.")` ;
  - si elle est dissoute et que nous ne sommes pas propriétaire : `_groups.Remove(id)` et `Report($"Le groupe {nom} a été dissous.")`.
- Dans `RefreshLoopAsync`, à chaque ronde : `if (_candidate.TakeJoined(_clock.UtcNow) is { } joined)`, l'ajouter par `_groups.TryAdd` et le dire (« Vous avez rejoint {nom}. », ou le motif du refus de `TryAdd`).
- `GroupActions` :
  - `Create` : exige une identité et un premier service actif ; `GroupGovernance.Create(nom, mot de passe, service, identity.PublicKey, now)` ; `TryAdd` ; `Report` avec le code formaté (`InvitationTicketText.Encode(code, service)`) ; toute `ArgumentException` devient un `Report` lisible.
  - `Join` : retirer tirets et espaces du texte, puis `InvitationTicketText.TryParse(texte, out ticket, out at, out why)` ; service = `at ?? premier service actif` ; `RunSafely(() => _presence.JoinGroupAsync(...))` et `Report` du message.
  - `CancelJoin` : `_candidate.Cancel()`.
  - `Leave` : pour un propriétaire, publier `GroupGovernance.Dissolve` par `OfferPolicy` (le groupe reste listé, dissous) ; pour les autres, `_groups.Remove(id)`.
  - `Forget` : `_groups.Remove(id)` (n'est proposé que pour un groupe dissous).
  - `Edit` : trouve le groupe, calcule le rôle ; propriétaire → signataire `null` ; modérateur → `_pairing.Identity.Key` ; membre → `Report` « Seuls le propriétaire et les modérateurs peuvent faire cela. ». Appelle la fonction, passe le résultat à `_groups.OfferPolicy`, et transforme une `InvalidOperationException` en `Report($"Refusé : {e.Message}")`.
  - `Approve` / `Decline` : `_admissionHost.Approve(nonce)` / `Decline(nonce)`, puis `RunSafely(() => _presence.AnswerAsync(outbound, token))`.
  - `SetPaused` / `SetReceive` : `_groups.SetPaused` / `SetReceive`.
- Barre de statut : le compte des demandes en attente devient `_presence.RequestCount + _admissionHost.Pending.Count`.

- [ ] **Step 4: Vérifier et commit**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj && dotnet build Linkpearl/Linkpearl.csproj -c Release`
Expected: PASS, aucun warning.

```bash
git add Linkpearl/Ui/GroupActions.cs Linkpearl/Plugin.cs Linkpearl/Core/Sync/NameplateMark.cs Linkpearl/Ui/NameplateGlyphs.cs Linkpearl/Ui/NameplateLegend.cs Linkpearl/Integration/StatusBarEntry.cs Linkpearl.Core.Tests/Sync/NameplateMarkTests.cs
git commit -m "feat(groupes): le plugin crée, rejoint et gouverne les groupes privés"
```

---

### Task 10: La page Groupes

**Files:**
- Create: `Linkpearl/Ui/Pages/GroupsPage.cs`
- Modify: `Linkpearl/Ui/MainWindow.cs`, `Linkpearl/Ui/Pages/RequestsPage.cs`, `Linkpearl/Ui/RequestToasts.cs`, `Linkpearl/Ui/Icons.cs`, `Linkpearl/Plugin.cs` (construction de la fenêtre)

**Interfaces:**
- Consumes: `GroupBook`, `AdmissionCandidate`, `AdmissionHost.Pending`, `GroupActions`, `Func<IReadOnlyList<PeerStatus>>`, `GroupGovernance.RoleOf`/`CodeOf`, `InvitationTicketText.Encode`.
- Produces: `GroupsPage(GroupBook groups, AdmissionCandidate candidate, Func<IReadOnlyList<PeerStatus>> statuses, GroupActions actions)` avec `Draw()`.

Style : reprendre exactement les composants et motifs existants (`Text.Title`, `Text.Small`, `Card.Begin`, `Btn.Draw`, `Btn.Icon`, `Chip.Draw`, `Feedback.Hint`, `Feedback.EmptyState`, `CollapsingHeader` coloré comme dans `PairsPage`, confirmation en deux clics comme `_confirming` dans `PairsPage`, champ mot de passe comme `BackupCard`, `Glyphs.Safe` sur tout nom affiché). **Lire `PairsPage.cs`, `RequestsPage.cs`, `BackupCard.cs` et `SettingsPage.cs` en entier avant d'écrire.** Tous les libellés en français.

- [ ] **Step 1: L'icône**

`Icons.Groups` : choisir un membre existant de `FontAwesomeIcon` qui évoque un groupe et n'est pas déjà utilisé (vérifier dans l'énumération de Dalamud, par exemple `PeopleGroup` s'il existe, sinon `UsersCog` ou `LayerGroup`), et **l'ajouter à `Icons.All`**, sans quoi il s'affiche en carré vide.

- [ ] **Step 2: La page**

Structure, de haut en bas :
1. `Text.Title("Groupes")`, puis une phrase : « Un groupe synchronise tous ses membres entre eux, sans les pairer un à un. »
2. **Carte « Rejoindre »** : champ code (`XXXX-XXXX-XXXX@service`), champ mot de passe (drapeau `Password`, facultatif), bouton « Rejoindre » ; sous la carte, l'état de la candidature :
   - `Waiting` : « Demande envoyée. En attente d'un membre en ligne, ou d'un modérateur si le groupe valide chaque entrée. » + bouton « Annuler » ;
   - `NeedsPassword` : « Ce groupe demande un mot de passe. Saisissez-le et relancez. » (Idle) ;
   - `Proving` : « Mot de passe envoyé… » ;
   - `Refused` : selon `RefusalReason` : « Mot de passe incorrect. », « Trop d'essais : réessayez dans une demi-heure. », « Un modérateur a refusé votre demande. » (Danger) ;
   - `Expired` : « Personne n'a répondu en dix minutes. Un membre doit être en ligne. » (Idle) ;
   - `Idle` avec `LastJoinedName` : « Vous avez rejoint {nom}. » (Online).
3. **Carte « Créer un groupe »** : champ nom (32 caractères), champ mot de passe facultatif, `Feedback.Hint` : « Sans mot de passe, chaque entrée devra être validée par vous ou un modérateur. », bouton « Créer ».
4. **Aucun groupe** : `Feedback.EmptyState(Icons.Groups, "Aucun groupe", "Créez-en un ou rejoignez-en un avec son code.")`.
5. **Une section repliable par groupe**, titre `"{nom} ({membres rencontrés})"`, ouverte par défaut, avec :
   - une ligne de puces : le rôle (« propriétaire », « modérateur », « membre »), « dissous » s'il l'est, « en attente de sa politique » si `Policy` est nulle ;
   - pour le propriétaire et les modérateurs : le code formaté par groupes de quatre avec le service, et un bouton de copie (`ImGui.SetClipboardText`) ;
   - la table des membres rencontrés : pastille d'état, nom, puce d'état (même logique que `PairsPage.DrawState`, en rapprochant le membre du statut par `status.Group == group.Id && status.View.Fingerprint == member.Fingerprint`), pause, menu des effets (copie adaptée de `PairsPage.DrawReceive`), et pour le propriétaire et les modérateurs un bouton « Exclure » en deux clics, qui bannit `member.Id` et `member.Fingerprint` ; pour le propriétaire seulement, un bouton qui nomme ou retire un modérateur (désactivé sans `member.PublicKey`, avec l'infobulle « Il faut l'avoir croisé une fois pour connaître sa clé. ») ;
   - une sous-section « Gestion » repliée par défaut, pour le propriétaire et les modérateurs : changer le mot de passe (champ + bouton), « Nouveau code » (l'ancien ne mène plus nulle part, le dire dans l'infobulle), la liste des bannis avec « Lever » ; pour le propriétaire seulement : le mode d'admission (deux boutons radio « Mot de passe » / « Validation par un modérateur »), et « Dissoudre » en deux clics ;
   - en bas : « Quitter le groupe » en deux clics pour un membre ou un modérateur ; pour un groupe dissous, « Retirer de la liste ».
6. Les actions passent toutes par `GroupActions` ; la page ne touche jamais `GroupBook` en écriture directement.

- [ ] **Step 3: Les demandes d'admission**

- `RequestsPage` reçoit `Func<IReadOnlyList<PendingValidation>> admissions`, `Action<PendingValidation> approve`, `Action<PendingValidation> decline`. `Count` additionne les deux sortes. Les demandes d'admission s'affichent après celles de pairage, une carte chacune : titre = nom du personnage, sous-titre « veut rejoindre {groupe} », la puce « visible autour de vous » / « pas visible d'ici » (même rapprochement par nom et monde que les demandes de pairage), boutons « Accepter » et « Refuser ».
- `RequestToasts` : afficher aussi ces demandes (en-tête « Demande d'entrée dans un groupe »), en comptant dans `MaxShown`.

- [ ] **Step 4: La fenêtre**

- `MainWindow` reçoit la `GroupsPage` construite par le plugin (ou ses dépendances), et l'enregistre comme `ShellPage { Id = "groups", Icon = Icons.Groups, Label = () => "Groupes", Draw = groups.Draw }`, placée après « Demandes » et avant « Réglages ».
- `Plugin.cs` : construire `GroupActions`, la page et les nouveaux arguments de `RequestsPage` / `RequestToasts`.

- [ ] **Step 5: Compiler, déployer, commit**

Run: `dotnet build Linkpearl/Linkpearl.csproj -c Release && dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: succès, aucun warning.

Run: `./scripts/deploy-plugin-dev.sh`
Expected: « Déployé ». Ne rien tester en jeu soi-même.

```bash
git add Linkpearl/Ui/ Linkpearl/Plugin.cs
git commit -m "feat(ui): page Groupes et demandes d'entrée dans un groupe"
```

---

### Task 11: Documentation

**Files:**
- Modify: `docs/protocol.md`, `docs/pairage.md`, `docs/reprise.md`, `README.md`, `README.fr.md`

- [ ] **Step 1: `protocol.md`**

Une section « Groupes privés » qui documente, avec les étiquettes exactes et les vecteurs figés du tableau en tête de ce plan :
- la boîte d'admission, les cinq dépôts (disposition octet par octet), le scellement (`HKDF(ECDH ‖ aléa)`, étiquettes par usage, aléa AES-GCM nul, données associées), l'octroi et sa vérification (`GroupId` = SHA-256 de la clé du groupe) ;
- l'encodage canonique de l'attestation et de la politique, les règles d'acceptation, l'ordre entre versions ;
- `MessageKind.GroupPolicy` et l'échange à l'ouverture de session ;
- dans « Modèle de confiance » : l'admission se fait en confiance au premier contact, un service qui s'intercale apprend le mot de passe et le secret ; le mot de passe est connu de tous les membres ; un banni garde le secret et voit la présence des membres.

- [ ] **Step 2: `pairage.md`, `reprise.md`, README**

- `pairage.md`, « Ce qui garde les codes » : les codes sont devenus ceux des groupes, une porte et non une clé ; renvoyer à la spec des groupes.
- `reprise.md` : point 3 de la feuille de route, incrément 2 livré ; retirer la mention de `/lpgroupe` ; noter ce qui reste (Public et bannissements du service à l'incrément 3, essais en jeu).
- `README.md` et `README.fr.md`, en parallèle : une section « Groupes » / « Groups » (créer, partager le code par /tell, rejoindre, mot de passe ou validation, exclure, dissoudre). Dans le README anglais, citer chaque libellé tel qu'il apparaît en jeu suivi de sa traduction. Garder les titres `#getting-started` et `#premiers-pas`.

- [ ] **Step 3: Vérifier et commit**

Run: `grep -nP '\x{2014}' docs/protocol.md docs/pairage.md docs/reprise.md README.md README.fr.md`
Expected: aucune ligne.

```bash
git add docs/protocol.md docs/pairage.md docs/reprise.md README.md README.fr.md
git commit -m "docs(groupes): groupes privés, admission et politique"
```
