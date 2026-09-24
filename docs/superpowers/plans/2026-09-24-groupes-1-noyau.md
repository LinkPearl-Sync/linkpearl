# Groupes, incrément 1 : le noyau

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deux membres d'un même groupe qui se voient en jeu se composent et échangent leur apparence, sans entrée dans le carnet de pairs, à partir d'un groupe fabriqué à la main.

**Architecture:** Un groupe est un secret de 32 octets. Chaque membre ouvre une boîte de présence dérivée du secret et de son empreinte ; la détection existante interroge ces boîtes pour les joueurs détectés ; un planificateur transforme ce qui répond en `PairRecord` éphémères, d'origine « groupe », que le moteur compose comme des paires. Le handshake autorise ces pairs par un `IGroupGate` (épinglage empreinte vers clé, premier vu), et choisit l'initiateur par les empreintes, faute d'identifiant connu d'avance.

**Tech Stack:** C# (.NET 10), xUnit, System.Security.Cryptography (SHA-256, HKDF), System.Text.Json, DPAPI côté plugin.

**Spec:** `docs/superpowers/specs/2026-09-24-groupes-design.md`. Incréments 2 (groupes privés : code, admission, politique, interface) et 3 (Public et bannissements) : plans séparés, écrits après la livraison de celui-ci, sur les interfaces réelles.

## Global Constraints

- `Linkpearl/Core/` ne référence jamais Dalamud (`ArchitectureTests` le vérifie).
- Aucun nom de personnage ni chemin local complet dans les journaux par défaut.
- Pas de tiret cadratin (U+2014), nulle part. Commentaires en français, qui disent le pourquoi.
- `record` et `readonly record struct` pour les données ; jamais d'`enum` pour un octet qui traverse le réseau (un `enum` strictement local est admis, comme `PairTrust`).
- Commits Conventional Commits, sujet en français, **sans** ligne `Co-Authored-By`, `Claude-Session` ni mention « Generated with ».
- `dotnet build Linkpearl/Linkpearl.csproj -c Release` passe sans warning.
- Constantes de la spec : au plus 10 groupes par personnage, 256 membres rencontrés par groupe, 48 sessions de groupe à la fois, départ 5 minutes après la dernière vue, `MaxMailboxesPerSession` du service à 64.
- Étiquettes de dérivation : `"linkpearl:group-presence:v1"`, `"linkpearl:group-pair:v1"`, `"linkpearl:group-runtime:v1"`, entropie DPAPI `"linkpearl:groups:v1"`.
- `GroupBook` est lu par le fil de rafraîchissement, le tic du moteur et les handshakes : il se protège seul par un verrou.

**Écarts assumés avec la spec, pour cet incrément :** `Members` et `Pins` ne font qu'un dictionnaire indexé par empreinte (`GroupMember.Id` est l'épinglage) ; au-delà de 48 sessions, priorité aux membres vus le plus récemment (le jeu ne donne pas la distance dans l'instantané actuel).

## Review Focus

1. **Deux membres qui se voient en même temps doivent tomber d'accord sur qui initie.** Si les deux se croient initiateurs, aucune session ne naît. Test : `PeerSessionGroupTests.Deux_membres_etablissent_une_session_quel_que_soit_l_ordre` (tâche 4).
2. **Un membre retiré du plan après application** doit voir ce qui était posé retiré, pas laissé à l'écran. Test : `GroupSyncEngineTests.Un_membre_retire_du_plan_voit_son_apparence_effacee` (tâche 6).
3. **Un `groups.json` abîmé** ne doit pas empêcher le plugin de charger : entrée abîmée ignorée, document illisible détecté. Tests : `GroupBookCodecTests` (tâche 3).
4. **Un joueur à la fois paire directe et membre d'un groupe commun** ne doit pas ouvrir deux sessions. Test : `GroupDialPlannerTests.Une_paire_directe_n_est_pas_doublee` (tâche 5).
5. **Un avis de retrait (`Unpair`) reçu d'une session de groupe** ne doit ni retirer quoi que ce soit du carnet ni dire « a mis fin au pairage ». Test : `GroupSyncEngineTests.Un_avis_de_retrait_n_arrete_qu_une_paire_du_carnet` (tâche 6).

---

## Fichiers

| Fichier | Rôle |
|---|---|
| `Linkpearl/Core/Groups/GroupId.cs` (nouveau) | Identifiant de groupe, 16 octets, ordonnable |
| `Linkpearl/Core/Groups/GroupDerivation.cs` (nouveau) | Boîte de présence, secret de paire de groupe, identifiant de runtime |
| `Linkpearl/Core/Groups/GroupRecord.cs` (nouveau) | `GroupRecord`, `GroupMember`, `GroupOrigin` |
| `Linkpearl/Core/Groups/GroupBook.cs` (nouveau) | Groupes du personnage, épinglage, réglages par membre, `IGroupGate` |
| `Linkpearl/Core/Groups/GroupBookCodec.cs` (nouveau) | Forme sur disque, testable sous Linux |
| `Linkpearl/Core/Groups/GroupDialPlanner.cs` (nouveau) | Ce qu'on voit devient des `PairRecord` éphémères |
| `Linkpearl/Integration/GroupStore.cs` (nouveau) | DPAPI et écriture atomique autour du codec |
| `Linkpearl/Core/Identity/PairBook.cs` | `PairRecord.Group` |
| `Linkpearl/Core/Sync/PeerSession.cs` | Initiateur par empreintes, autorisation par la porte de groupe |
| `Linkpearl/Core/Sync/SyncEngine.cs` | Pairs de groupe à côté du carnet |
| `Linkpearl/Integration/PresenceService.cs` | Boîtes de présence de groupe, interrogation, budget de boîtes |
| `Linkpearl/Integration/CharacterStorage.cs` | `groups.json` dans `Belongings` |
| `Linkpearl/Plugin.cs` | Câblage, commande d'essai `/lpgroupe` |
| `../rendezvous/Linkpearl.Rendezvous/RendezvousLimits.cs` | `MaxMailboxesPerSession` à 64 |
| `docs/protocol.md`, `docs/reprise.md` | Dérivations et état |

Les tests vont dans `Linkpearl.Core.Tests/Groups/` (nouveau dossier) et `Linkpearl.Core.Tests/Sync/`. `build/Core.Sources.props` inclut déjà `Linkpearl/Core/**/*.cs` : rien à déclarer. Les tests compilent les sources du noyau, donc voient ses membres `internal`.

Vecteurs figés, calculés indépendamment en Python (hashlib, hmac) :

| Donnée | Valeur |
|---|---|
| secret | octets 0x00 à 0x1f |
| instant | 2026-09-22 12:00:00 UTC, fenêtre 994488 |
| empreinte `alice`@21 | `e01e458cfd411c4eb2e0c5d509f02995` |
| empreinte `bob`@21 | `b65c349dc0a65fb594b4117d93729e62` |
| présence d'alice | `31e46f1f1b91` |
| secret de paire alice/bob | `fd0ebba6aa5cd0fa751986531ed267f714904c16f7e47bce559b34d896041672` |
| identifiant de runtime | `31474004ac6d8e2d031c1e3553bd693b` |
| `GroupId.Of(secret)` | `630dcd2966c4336691125448bbb25b4f` |

---

### Task 1: Identifiant de groupe et dérivations

**Files:**
- Create: `Linkpearl/Core/Groups/GroupId.cs`
- Create: `Linkpearl/Core/Groups/GroupDerivation.cs`
- Test: `Linkpearl.Core.Tests/Groups/GroupDerivationTests.cs`

**Interfaces:**
- Consumes: `MailboxAddress.IndexAt`, `MailboxAddress.FromBytes`, `MailboxAddress.SizeInBytes`, `PlayerFingerprint`, `PeerId.FromBytes`, `PairSecret.SizeInBytes`.
- Produces:
  - `readonly record struct GroupId : IComparable<GroupId>` : `SizeInBytes = 16`, `static GroupId Of(ReadOnlySpan<byte> material)`, `static GroupId FromBytes(ReadOnlySpan<byte>)`, `byte[] ToBytes()`, `int CompareTo(GroupId)`, `string ToString()` (hex minuscule).
  - `static class GroupDerivation` : `SecretSize = 32`, `MailboxAddress PresenceAddress(ReadOnlySpan<byte> secret, PlayerFingerprint member, DateTimeOffset now, int windowOffset = 0)`, `IReadOnlyList<MailboxAddress> PresenceAround(ReadOnlySpan<byte> secret, PlayerFingerprint member, DateTimeOffset now)`, `byte[] MemberPairSecret(ReadOnlySpan<byte> groupSecret, PlayerFingerprint one, PlayerFingerprint other)`, `PeerId RuntimeId(ReadOnlySpan<byte> pairSecret)`.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
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
```

- [ ] **Step 2: Vérifier qu'ils échouent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupDerivationTests`
Expected: échec de compilation, `GroupDerivation` et `GroupId` inconnus.

- [ ] **Step 3: Écrire `GroupId`**

```csharp
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Linkpearl.Core.Groups;

/// <summary>
/// Identifiant d'un groupe.
/// </summary>
/// <remarks>
/// Seize octets, comme <see cref="Identity.PeerId"/> : un index, pas une preuve.
/// Ordonnable parce que deux membres qui ont plusieurs groupes en commun doivent
/// choisir le même pour se joindre, sans se concerter : le plus petit.
/// </remarks>
public readonly record struct GroupId : IComparable<GroupId>
{
    public const int SizeInBytes = 16;

    private readonly ulong _high;
    private readonly ulong _low;

    private GroupId(ulong high, ulong low) => (_high, _low) = (high, low);

    /// <summary>Dérive l'identifiant de ce qui fonde le groupe : sa clé de signature, ou son secret.</summary>
    public static GroupId Of(ReadOnlySpan<byte> material)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(material, digest);

        return new GroupId(
            BinaryPrimitives.ReadUInt64BigEndian(digest),
            BinaryPrimitives.ReadUInt64BigEndian(digest[8..]));
    }

    public static GroupId FromBytes(ReadOnlySpan<byte> bytes)
        => bytes.Length != SizeInBytes
            ? throw new ArgumentException($"un identifiant de groupe fait {SizeInBytes} octets", nameof(bytes))
            : new GroupId(
                BinaryPrimitives.ReadUInt64BigEndian(bytes),
                BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]));

    public byte[] ToBytes()
    {
        var bytes = new byte[SizeInBytes];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, _high);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), _low);
        return bytes;
    }

    /// <summary>L'ordre des octets, lus en gros-boutiste.</summary>
    public int CompareTo(GroupId other)
    {
        var high = _high.CompareTo(other._high);
        return high != 0 ? high : _low.CompareTo(other._low);
    }

    public override string ToString() => $"{_high:x16}{_low:x16}";
}
```

- [ ] **Step 4: Écrire `GroupDerivation`**

```csharp
using System.Buffers.Binary;
using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;

namespace Linkpearl.Core.Groups;

/// <summary>
/// Ce qui se dérive du secret d'un groupe.
/// </summary>
/// <remarks>
/// Le service ne voit que des adresses de six octets et des jetons : rien ici ne
/// lui dit de quel groupe il s'agit, ni ne relie une fenêtre à la suivante.
/// </remarks>
public static class GroupDerivation
{
    public const int SecretSize = 32;

    private static ReadOnlySpan<byte> PresenceContext => "linkpearl:group-presence:v1"u8;
    private static ReadOnlySpan<byte> PairInfo => "linkpearl:group-pair:v1"u8;
    private static ReadOnlySpan<byte> RuntimeContext => "linkpearl:group-runtime:v1"u8;

    /// <summary>
    /// La boîte qu'un membre ouvre pour dire « je suis là » à son groupe.
    /// </summary>
    /// <remarks>
    /// Le secret y entre : sans lui, qui voit le personnage ne peut pas savoir
    /// s'il appartient au groupe. C'est ce qui distingue cette boîte de la boîte
    /// personnelle, calculable par n'importe qui.
    /// </remarks>
    public static MailboxAddress PresenceAddress(
        ReadOnlySpan<byte> secret, PlayerFingerprint member, DateTimeOffset now, int windowOffset = 0)
    {
        if (secret.Length != SecretSize)
            throw new ArgumentException($"un secret de groupe fait {SecretSize} octets", nameof(secret));

        var index = MailboxAddress.IndexAt(now) + windowOffset;
        var memberAt = PresenceContext.Length + SecretSize;
        var windowAt = memberAt + PlayerFingerprint.SizeInBytes;

        Span<byte> input = stackalloc byte[windowAt + sizeof(long)];
        PresenceContext.CopyTo(input);
        secret.CopyTo(input[PresenceContext.Length..]);
        member.ToBytes().CopyTo(input[memberAt..]);
        BinaryPrimitives.WriteInt64BigEndian(input[windowAt..], index);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);

        return MailboxAddress.FromBytes(digest[..MailboxAddress.SizeInBytes]);
    }

    /// <summary>La fenêtre courante et la suivante, pour la même raison que <see cref="MailboxAddress.Around"/>.</summary>
    public static IReadOnlyList<MailboxAddress> PresenceAround(
        ReadOnlySpan<byte> secret, PlayerFingerprint member, DateTimeOffset now)
        => [PresenceAddress(secret, member, now), PresenceAddress(secret, member, now, 1)];

    /// <summary>
    /// Le secret de paire de deux membres, calculé par chacun sans rien échanger.
    /// </summary>
    /// <remarks>
    /// Les empreintes entrent triées dans le sel, comme les identifiants dans
    /// <see cref="PairSecret.Derive"/> : les deux côtés doivent s'annoncer sous
    /// les mêmes jetons. Tout membre du groupe peut calculer celui de n'importe
    /// quel couple ; c'est le handshake, et l'épinglage de la clé, qui disent à
    /// qui l'on parle.
    /// </remarks>
    public static byte[] MemberPairSecret(ReadOnlySpan<byte> groupSecret, PlayerFingerprint one, PlayerFingerprint other)
    {
        if (one == other)
            throw new ArgumentException("un membre ne se compose pas avec lui-même", nameof(other));

        var a = one.ToBytes();
        var b = other.ToBytes();
        var (lower, upper) = a.AsSpan().SequenceCompareTo(b) < 0 ? (a, b) : (b, a);

        byte[] salt = [.. lower, .. upper];

        var prk = new byte[32];
        HKDF.Extract(HashAlgorithmName.SHA256, groupSecret, salt, prk);

        var secret = new byte[PairSecret.SizeInBytes];
        HKDF.Expand(HashAlgorithmName.SHA256, prk, secret, PairInfo);
        return secret;
    }

    /// <summary>
    /// L'identifiant sous lequel le moteur range un membre de groupe.
    /// </summary>
    /// <remarks>
    /// Le moteur indexe ses runtimes par <see cref="PeerId"/>, et un membre qu'on
    /// n'a jamais rencontré n'en a pas encore : sa clé n'arrive qu'au handshake.
    /// Celui-ci est stable pour un couple donné, et ne peut pas rencontrer un
    /// vrai <see cref="PeerId"/>, qui est le hash d'une clé publique.
    /// </remarks>
    public static PeerId RuntimeId(ReadOnlySpan<byte> pairSecret)
    {
        Span<byte> input = stackalloc byte[RuntimeContext.Length + pairSecret.Length];
        RuntimeContext.CopyTo(input);
        pairSecret.CopyTo(input[RuntimeContext.Length..]);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);

        return PeerId.FromBytes(digest[..PeerId.SizeInBytes]);
    }
}
```

- [ ] **Step 5: Vérifier que les tests passent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupDerivationTests`
Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add Linkpearl/Core/Groups/GroupId.cs Linkpearl/Core/Groups/GroupDerivation.cs Linkpearl.Core.Tests/Groups/GroupDerivationTests.cs
git commit -m "feat(groupes): identifiant de groupe et dérivations de présence et de paire"
```

---

### Task 2: Les groupes du personnage et la porte du handshake

**Files:**
- Create: `Linkpearl/Core/Groups/GroupRecord.cs`
- Create: `Linkpearl/Core/Groups/GroupBook.cs`
- Modify: `Linkpearl/Core/Identity/PairBook.cs` (ajout de `PairRecord.Group`)
- Test: `Linkpearl.Core.Tests/Groups/GroupBookTests.cs`

**Interfaces:**
- Consumes: `GroupId`, `PlayerFingerprint`, `PeerId`, `TransientCategories`, `RendezvousAddress`, `IClock`, `PairRecord`, `MovableClock` (dans `Linkpearl.Core.Tests/Sync/SyncPiecesTests.cs`).
- Produces:
  - `sealed record GroupOrigin(GroupId Group, PlayerFingerprint Ours, PlayerFingerprint Theirs)`.
  - `PairRecord.Group` : `GroupOrigin?`, null pour une paire directe.
  - `sealed record GroupMember` : `required PlayerFingerprint Fingerprint`, `PeerId? Id`, `required string DisplayName`, `DateTimeOffset? LastSeenAt`, `bool Paused`, `TransientCategories Receive = All`.
  - `sealed record GroupRecord` : `required GroupId Id`, `required string Name`, `required byte[] Secret`, `required IReadOnlyList<RendezvousAddress> Rendezvous`, `required DateTimeOffset JoinedAt`, `IReadOnlyDictionary<PlayerFingerprint, GroupMember> Members` (vide par défaut).
  - `interface IGroupGate { bool Admits(PairRecord pair, byte[] publicKey); }`
  - `enum GroupAdmission { Admitted, Pinned, Disputed, UnknownGroup }` (local, jamais sur le réseau).
  - `sealed class GroupBook(IClock clock) : IGroupGate` : `MaxGroups = 10`, `MaxMembersPerGroup = 256`, `IReadOnlyList<GroupRecord> All`, `GroupRecord? Find(GroupId)`, `bool TryAdd(GroupRecord, out string? refusal)`, `bool Remove(GroupId)`, `void Load(IEnumerable<GroupRecord>)`, `void Clear()`, `GroupAdmission Admit(GroupId, PlayerFingerprint, byte[] publicKey, string displayName)`, `void SetPaused(GroupId, PlayerFingerprint, bool)`, `void SetReceive(GroupId, PlayerFingerprint, TransientCategories)`, `event Action? Changed`.
  - Aide de test `GroupBookTests.Group(byte[] secret, DateTimeOffset joinedAt, string name = "Compagnie")`, `internal static`, réutilisée par les tâches 3, 5 et 6.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Tests.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Groups;

public sealed class GroupBookTests
{
    private static readonly byte[] Secret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    private static readonly PlayerFingerprint Alice = PlayerFingerprint.Of("alice", 21);
    private static readonly byte[] AliceKey = [2, .. Enumerable.Repeat((byte)7, 32)];
    private static readonly byte[] OtherKey = [3, .. Enumerable.Repeat((byte)9, 32)];

    private readonly MovableClock _clock = new();

    internal static GroupRecord Group(byte[] secret, DateTimeOffset joinedAt, string name = "Compagnie")
        => new()
        {
            Id = GroupId.Of(secret),
            Name = name,
            Secret = secret,
            Rendezvous = [new RendezvousAddress("rdv.exemple.ch", 47900)],
            JoinedAt = joinedAt,
        };

    private GroupBook BookWithOneGroup()
    {
        var book = new GroupBook(_clock);
        Assert.True(book.TryAdd(Group(Secret, _clock.UtcNow), out _));
        return book;
    }

    [Fact]
    public void La_premiere_cle_vue_est_epinglee_puis_seule_admise()
    {
        var book = BookWithOneGroup();
        var id = GroupId.Of(Secret);

        Assert.Equal(GroupAdmission.Pinned, book.Admit(id, Alice, AliceKey, "Alice"));
        Assert.Equal(GroupAdmission.Admitted, book.Admit(id, Alice, AliceKey, "Alice"));
        Assert.Equal(GroupAdmission.Disputed, book.Admit(id, Alice, OtherKey, "Alice"));

        Assert.Equal(PeerId.Of(AliceKey), book.Find(id)!.Members[Alice].Id);
    }

    [Fact]
    public void Un_groupe_inconnu_n_admet_personne()
        => Assert.Equal(
            GroupAdmission.UnknownGroup,
            new GroupBook(_clock).Admit(GroupId.Of(Secret), Alice, AliceKey, "Alice"));

    [Fact]
    public void Au_dela_de_dix_groupes_on_refuse()
    {
        var book = new GroupBook(_clock);

        for (var i = 0; i < GroupBook.MaxGroups; i++)
            Assert.True(book.TryAdd(Group([.. Enumerable.Repeat((byte)i, 32)], _clock.UtcNow), out _));

        Assert.False(book.TryAdd(Group([.. Enumerable.Repeat((byte)99, 32)], _clock.UtcNow), out var refusal));
        Assert.NotNull(refusal);
    }

    [Fact]
    public void Un_groupe_deja_present_n_est_pas_ajoute_deux_fois()
    {
        var book = BookWithOneGroup();

        Assert.False(book.TryAdd(Group(Secret, _clock.UtcNow), out _));
        Assert.Single(book.All);
    }

    [Fact]
    public void Au_dela_de_256_membres_le_plus_ancien_non_en_pause_est_oublie()
    {
        var book = BookWithOneGroup();
        var id = GroupId.Of(Secret);
        var first = PlayerFingerprint.Of("membre0", 21);
        var second = PlayerFingerprint.Of("membre1", 21);

        for (var i = 0; i < GroupBook.MaxMembersPerGroup; i++)
        {
            book.Admit(id, PlayerFingerprint.Of($"membre{i}", 21), [2, .. Enumerable.Repeat((byte)i, 32)], $"M{i}");
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        // Le premier est en pause : c'est un réglage que l'utilisateur a posé,
        // on ne le lui retire pas en silence. Le deuxième part à sa place.
        book.SetPaused(id, first, true);
        book.Admit(id, PlayerFingerprint.Of("nouveau", 21), OtherKey, "Nouveau");

        var members = book.Find(id)!.Members;
        Assert.Equal(GroupBook.MaxMembersPerGroup, members.Count);
        Assert.True(members.ContainsKey(first));
        Assert.False(members.ContainsKey(second));
    }

    [Fact]
    public void Epingler_et_regler_levent_Changed()
    {
        var book = BookWithOneGroup();
        var id = GroupId.Of(Secret);
        var changes = 0;
        book.Changed += () => changes++;

        book.Admit(id, Alice, AliceKey, "Alice");
        book.Admit(id, Alice, AliceKey, "Alice");   // rien de neuf à enregistrer
        book.SetPaused(id, Alice, true);
        book.SetReceive(id, Alice, TransientCategories.None);

        Assert.Equal(3, changes);
        Assert.True(book.Find(id)!.Members[Alice].Paused);
        Assert.Equal(TransientCategories.None, book.Find(id)!.Members[Alice].Receive);
    }

    [Fact]
    public void La_porte_n_admet_que_les_pairs_d_origine_groupe()
    {
        var book = BookWithOneGroup();
        var id = GroupId.Of(Secret);
        var bob = PlayerFingerprint.Of("bob", 21);

        var direct = new PairRecord
        {
            Id = PeerId.Of(AliceKey),
            PairSecret = new byte[32],
            DisplayName = "Alice",
            Rendezvous = [new RendezvousAddress("rdv.exemple.ch", 47900)],
            PairedAt = _clock.UtcNow,
        };

        IGroupGate gate = book;

        Assert.False(gate.Admits(direct, AliceKey));
        Assert.True(gate.Admits(direct with { Group = new GroupOrigin(id, bob, Alice) }, AliceKey));
        Assert.False(gate.Admits(direct with { Group = new GroupOrigin(id, bob, Alice) }, OtherKey));
    }
}
```

- [ ] **Step 2: Vérifier qu'ils échouent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupBookTests`
Expected: échec de compilation, `GroupBook` inconnu.

- [ ] **Step 3: Ajouter `PairRecord.Group`**

Dans `Linkpearl/Core/Identity/PairBook.cs`, ajouter `using Linkpearl.Core.Groups;` en tête, et dans `PairRecord`, après `PinnedFingerprint` :

```csharp
    /// <summary>
    /// Le groupe par lequel on joint ce pair, ou null pour une paire du carnet.
    /// </summary>
    /// <remarks>
    /// Un pair de groupe n'est jamais écrit dans le carnet : il naît quand on le
    /// voit, et disparaît cinq minutes après. Son <see cref="Id"/> n'est pas le
    /// hash de sa clé, qu'on ne connaît pas encore, mais un identifiant tiré du
    /// secret du couple (<see cref="GroupDerivation.RuntimeId"/>) : c'est
    /// pourquoi le handshake l'autorise autrement.
    /// </remarks>
    public GroupOrigin? Group { get; init; }
```

- [ ] **Step 4: Écrire `GroupRecord.cs`**

```csharp
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>D'où vient un pair de groupe : quel groupe, et quelles empreintes.</summary>
/// <remarks>
/// Les deux empreintes servent à choisir l'initiateur du handshake : l'identifiant
/// du pair n'est pas connu d'avance, et les deux côtés doivent faire le même choix.
/// </remarks>
public sealed record GroupOrigin(GroupId Group, PlayerFingerprint Ours, PlayerFingerprint Theirs);

/// <summary>Un membre rencontré.</summary>
/// <remarks>
/// Indexé par empreinte et non par clé : c'est le personnage qu'on voit à
/// l'écran, et la clé ne s'apprend qu'au premier handshake, où elle s'épingle.
/// </remarks>
public sealed record GroupMember
{
    public required PlayerFingerprint Fingerprint { get; init; }

    /// <summary>La première clé vue pour ce personnage dans ce groupe. Null avant le premier handshake.</summary>
    public PeerId? Id { get; init; }

    /// <summary>Nom affiché, pour l'interface. Jamais transmis.</summary>
    public required string DisplayName { get; init; }

    public DateTimeOffset? LastSeenAt { get; init; }

    public bool Paused { get; init; }

    /// <summary>Les animations, VFX et sons acceptés de ce membre.</summary>
    public TransientCategories Receive { get; init; } = TransientCategories.All;
}

/// <summary>Un groupe dont le personnage est membre.</summary>
public sealed record GroupRecord
{
    public required GroupId Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Le secret partagé, 32 octets. Tout ce que le service voit en dérive.</summary>
    public required byte[] Secret { get; init; }

    public required IReadOnlyList<RendezvousAddress> Rendezvous { get; init; }

    public required DateTimeOffset JoinedAt { get; init; }

    /// <summary>Les membres rencontrés, et eux seuls : personne ne tient la liste complète.</summary>
    public IReadOnlyDictionary<PlayerFingerprint, GroupMember> Members { get; init; }
        = new Dictionary<PlayerFingerprint, GroupMember>();
}
```

- [ ] **Step 5: Écrire `GroupBook.cs`**

```csharp
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;

namespace Linkpearl.Core.Groups;

/// <summary>Ce que le handshake demande avant d'accepter la clé d'un pair de groupe.</summary>
public interface IGroupGate
{
    bool Admits(PairRecord pair, byte[] publicKey);
}

/// <summary>Le verdict sur la clé d'un membre. Strictement local.</summary>
public enum GroupAdmission
{
    Admitted,

    /// <summary>Admis, et épinglé à l'instant : il faut enregistrer.</summary>
    Pinned,

    /// <summary>Une autre clé a déjà été vue pour ce personnage.</summary>
    Disputed,

    UnknownGroup,
}

/// <summary>
/// Les groupes du personnage connecté.
/// </summary>
/// <remarks>
/// Lu par le fil de rafraîchissement, le tic du moteur et les handshakes, qui
/// tournent chacun sur sa tâche : il se protège seul. Les enregistrements sont
/// immuables, donc ce qui sort d'ici peut être lu sans verrou.
/// </remarks>
public sealed class GroupBook(IClock clock) : IGroupGate
{
    /// <summary>
    /// Chaque groupe coûte quatre boîtes par connexion au changement de fenêtre.
    /// Dix groupes tiennent sous la limite de 64 du service, avec la boîte personnelle.
    /// </summary>
    public const int MaxGroups = 10;

    /// <summary>Plus qu'une compagnie libre, assez peu pour que la liste reste lisible.</summary>
    public const int MaxMembersPerGroup = 256;

    private readonly Lock _gate = new();
    private readonly Dictionary<GroupId, GroupRecord> _groups = [];

    /// <summary>Levé hors du verrou, quand quelque chose qui s'enregistre a changé.</summary>
    public event Action? Changed;

    public IReadOnlyList<GroupRecord> All
    {
        get
        {
            lock (_gate)
                return [.. _groups.Values];
        }
    }

    public GroupRecord? Find(GroupId id)
    {
        lock (_gate)
            return _groups.GetValueOrDefault(id);
    }

    public bool TryAdd(GroupRecord group, out string? refusal)
    {
        lock (_gate)
        {
            if (_groups.ContainsKey(group.Id))
            {
                refusal = "déjà membre de ce groupe";
                return false;
            }

            if (_groups.Count >= MaxGroups)
            {
                refusal = $"au plus {MaxGroups} groupes par personnage";
                return false;
            }

            _groups[group.Id] = group;
        }

        refusal = null;
        Changed?.Invoke();
        return true;
    }

    public bool Remove(GroupId id)
    {
        bool removed;

        lock (_gate)
            removed = _groups.Remove(id);

        if (removed)
            Changed?.Invoke();

        return removed;
    }

    /// <summary>Remplace tout, au chargement. Ne lève pas <see cref="Changed"/> : rien n'est à réécrire.</summary>
    public void Load(IEnumerable<GroupRecord> groups)
    {
        lock (_gate)
        {
            _groups.Clear();

            foreach (var group in groups.Take(MaxGroups))
                _groups[group.Id] = group;
        }
    }

    /// <summary>Oublie tout, au changement de personnage.</summary>
    public void Clear()
    {
        lock (_gate)
            _groups.Clear();
    }

    /// <summary>
    /// Décide si cette clé peut parler pour ce personnage dans ce groupe.
    /// </summary>
    /// <remarks>
    /// Tous les membres connaissent le secret, donc les jetons de n'importe quel
    /// couple : un membre pourrait se présenter à la place d'un autre. La
    /// première clé vue pour un personnage l'emporte, et toute autre est
    /// contestée. Le premier contact reste gagnable par qui arrive avant le
    /// vrai ; c'est un prix énoncé dans la spec.
    /// </remarks>
    public GroupAdmission Admit(GroupId id, PlayerFingerprint member, byte[] publicKey, string displayName)
    {
        var key = PeerId.Of(publicKey);
        GroupAdmission verdict;

        lock (_gate)
        {
            if (_groups.TryGetValue(id, out var group) is false)
                return GroupAdmission.UnknownGroup;

            var members = new Dictionary<PlayerFingerprint, GroupMember>(group.Members);

            if (members.TryGetValue(member, out var known))
            {
                if (known.Id is { } pinned)
                {
                    verdict = pinned == key ? GroupAdmission.Admitted : GroupAdmission.Disputed;
                    members[member] = known with { LastSeenAt = clock.UtcNow };
                }
                else
                {
                    verdict = GroupAdmission.Pinned;
                    members[member] = known with { Id = key, LastSeenAt = clock.UtcNow };
                }
            }
            else
            {
                verdict = GroupAdmission.Pinned;
                members[member] = new GroupMember
                {
                    Fingerprint = member,
                    Id = key,
                    DisplayName = displayName,
                    LastSeenAt = clock.UtcNow,
                };

                if (members.Count > MaxMembersPerGroup)
                    ForgetOldest(members, spare: member);
            }

            _groups[id] = group with { Members = members };
        }

        if (verdict is GroupAdmission.Pinned)
            Changed?.Invoke();

        return verdict;
    }

    public void SetPaused(GroupId id, PlayerFingerprint member, bool paused)
        => UpdateMember(id, member, known => known with { Paused = paused });

    public void SetReceive(GroupId id, PlayerFingerprint member, TransientCategories receive)
        => UpdateMember(id, member, known => known with { Receive = receive });

    bool IGroupGate.Admits(PairRecord pair, byte[] publicKey)
        => pair.Group is { } origin
           && Admit(origin.Group, origin.Theirs, publicKey, pair.DisplayName)
               is GroupAdmission.Admitted or GroupAdmission.Pinned;

    private void UpdateMember(GroupId id, PlayerFingerprint member, Func<GroupMember, GroupMember> change)
    {
        lock (_gate)
        {
            if (_groups.TryGetValue(id, out var group) is false
                || group.Members.TryGetValue(member, out var known) is false)
                return;

            var members = new Dictionary<PlayerFingerprint, GroupMember>(group.Members) { [member] = change(known) };
            _groups[id] = group with { Members = members };
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Oublie le membre vu il y a le plus longtemps.
    /// </summary>
    /// <remarks>
    /// Jamais un membre en pause : c'est un réglage que l'utilisateur a posé, et
    /// le retrouver effacé en silence serait pire que de garder un inconnu.
    /// </remarks>
    private static void ForgetOldest(Dictionary<PlayerFingerprint, GroupMember> members, PlayerFingerprint spare)
    {
        var oldest = members.Values
            .Where(candidate => candidate.Paused is false && candidate.Fingerprint != spare)
            .OrderBy(candidate => candidate.LastSeenAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        if (oldest is not null)
            members.Remove(oldest.Fingerprint);
    }
}
```

- [ ] **Step 6: Vérifier que les tests passent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupBookTests`
Expected: PASS, 7 tests.

- [ ] **Step 7: Toute la suite, puis commit**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: PASS (l'ajout de `PairRecord.Group` ne change aucun test existant).

```bash
git add Linkpearl/Core/Groups/GroupRecord.cs Linkpearl/Core/Groups/GroupBook.cs Linkpearl/Core/Identity/PairBook.cs Linkpearl.Core.Tests/Groups/GroupBookTests.cs
git commit -m "feat(groupes): groupes du personnage, épinglage des membres et porte du handshake"
```

---

### Task 3: Forme sur disque et stockage chiffré

**Files:**
- Create: `Linkpearl/Core/Groups/GroupBookCodec.cs`
- Create: `Linkpearl/Integration/GroupStore.cs`
- Modify: `Linkpearl/Integration/CharacterStorage.cs:19`
- Test: `Linkpearl.Core.Tests/Groups/GroupBookCodecTests.cs`

**Interfaces:**
- Consumes: `GroupRecord`, `GroupMember`, `GroupBook`, `GroupDerivation.SecretSize`, `RendezvousAddress.TryParse`, `CharacterStorage.SetAside`, `GroupBookTests.Group`.
- Produces:
  - `static class GroupBookCodec` : `byte[] Encode(IEnumerable<GroupRecord>)`, `IReadOnlyList<GroupRecord> Decode(ReadOnlySpan<byte> json)` (lève `JsonException` si le document entier est illisible ; ignore une entrée abîmée), `bool IsValid(byte[] json)`.
  - `sealed class GroupStore(string path)` : `void Load(GroupBook)`, `void Save(GroupBook)`, `byte[]? ReadPlain()`, `void WritePlain(byte[])`.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
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
```

- [ ] **Step 2: Vérifier qu'ils échouent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupBookCodecTests`
Expected: échec de compilation, `GroupBookCodec` inconnu.

- [ ] **Step 3: Écrire `GroupBookCodec.cs`**

```csharp
using System.Text.Json;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>
/// La forme sur disque des groupes, sans le chiffrement.
/// </summary>
/// <remarks>
/// Dans le noyau pour se tester sous Linux ; le chiffrement DPAPI reste dans
/// l'adaptateur. Un format explicite plutôt que la sérialisation des types du
/// noyau, pour la même raison que le carnet : ceux-ci vont changer, et des
/// groupes illisibles après une mise à jour seraient perdus. Les champs à venir
/// (politique, clé de signature) s'ajouteront en fin d'enregistrement, nullables.
///
/// Le fichier se relit comme un message venu d'ailleurs, puisqu'il voyagera
/// dans la sauvegarde : une entrée hors des règles est ignorée, pas le
/// fichier entier.
/// </remarks>
public static class GroupBookCodec
{
    private sealed record MemberDto(
        string Fingerprint, string? Id, string DisplayName, long? LastSeenAt, bool Paused, int Receive);

    private sealed record GroupDto(
        string Id, string Name, string Secret, string[] Rendezvous, long JoinedAt, MemberDto[] Members);

    private const int ReceiveAnimations = 1;
    private const int ReceiveVfx = 2;
    private const int ReceiveSounds = 4;

    /// <summary>Même borne que le nom de personnage dans une demande de pairage.</summary>
    private const int MaxNameLength = 64;

    public static byte[] Encode(IEnumerable<GroupRecord> groups)
        => JsonSerializer.SerializeToUtf8Bytes(groups.Select(group => new GroupDto(
            Convert.ToHexStringLower(group.Id.ToBytes()),
            group.Name,
            Convert.ToHexStringLower(group.Secret),
            [.. group.Rendezvous.Select(place => place.ToString())],
            group.JoinedAt.ToUnixTimeSeconds(),
            [.. group.Members.Values.Select(member => new MemberDto(
                Convert.ToHexStringLower(member.Fingerprint.ToBytes()),
                member.Id is { } id ? Convert.ToHexStringLower(id.ToBytes()) : null,
                member.DisplayName,
                member.LastSeenAt?.ToUnixTimeSeconds(),
                member.Paused,
                ToBits(member.Receive)))])).ToList());

    public static IReadOnlyList<GroupRecord> Decode(ReadOnlySpan<byte> json)
    {
        var dtos = JsonSerializer.Deserialize<List<GroupDto?>>(json) ?? [];

        return [.. dtos.Select(Rehydrate).OfType<GroupRecord>().Take(GroupBook.MaxGroups)];
    }

    public static bool IsValid(byte[] json)
    {
        try
        {
            _ = Decode(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static GroupRecord? Rehydrate(GroupDto? dto)
    {
        if (dto is null)
            return null;

        try
        {
            var secret = Convert.FromHexString(dto.Secret);

            if (secret.Length != GroupDerivation.SecretSize || dto.Name.Length is 0 or > MaxNameLength)
                return null;

            var places = (dto.Rendezvous ?? [])
                .Select(text => RendezvousAddress.TryParse(text, out var place, out _) ? place : (RendezvousAddress?)null)
                .OfType<RendezvousAddress>()
                .ToList();

            // Un groupe sans service est injoignable, comme un pair sans service.
            if (places.Count == 0)
                return null;

            var members = (dto.Members ?? [])
                .Select(RehydrateMember)
                .OfType<GroupMember>()
                .Take(GroupBook.MaxMembersPerGroup)
                .GroupBy(member => member.Fingerprint)
                .ToDictionary(same => same.Key, same => same.First());

            return new GroupRecord
            {
                Id = GroupId.FromBytes(Convert.FromHexString(dto.Id)),
                Name = dto.Name,
                Secret = secret,
                Rendezvous = places,
                JoinedAt = DateTimeOffset.FromUnixTimeSeconds(dto.JoinedAt),
                Members = members,
            };
        }
        catch (Exception e) when (e is FormatException or ArgumentException or NullReferenceException)
        {
            return null;   // une entrée abîmée ne doit pas emporter les autres
        }
    }

    private static GroupMember? RehydrateMember(MemberDto? dto)
    {
        if (dto?.DisplayName is not { Length: <= MaxNameLength })
            return null;

        try
        {
            return new GroupMember
            {
                Fingerprint = PlayerFingerprint.FromBytes(Convert.FromHexString(dto.Fingerprint)),
                Id = dto.Id is { } id ? PeerId.FromBytes(Convert.FromHexString(id)) : null,
                DisplayName = dto.DisplayName,
                LastSeenAt = dto.LastSeenAt is { } seen ? DateTimeOffset.FromUnixTimeSeconds(seen) : null,
                Paused = dto.Paused,
                Receive = FromBits(dto.Receive),
            };
        }
        catch (Exception e) when (e is FormatException or ArgumentException or NullReferenceException)
        {
            return null;
        }
    }

    private static int ToBits(TransientCategories receive)
        => (receive.Animations ? ReceiveAnimations : 0)
         | (receive.Vfx ? ReceiveVfx : 0)
         | (receive.Sounds ? ReceiveSounds : 0);

    private static TransientCategories FromBits(int bits)
        => new((bits & ReceiveAnimations) != 0, (bits & ReceiveVfx) != 0, (bits & ReceiveSounds) != 0);
}
```

- [ ] **Step 4: Vérifier que les tests passent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupBookCodecTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Écrire `GroupStore.cs`, calqué sur `PairBookStore`**

```csharp
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
```

Vérifier la signature réelle de `CharacterStorage.SetAside` (utilisée par `PairBookStore.Load`) et l'appeler de la même façon.

- [ ] **Step 6: Déclarer le fichier comme appartenant au personnage**

Dans `Linkpearl/Integration/CharacterStorage.cs:19` :

```csharp
    public static readonly string[] Belongings = ["identity.key", "pairs.json", "invitations.json", "groups.json"];
```

Vérifier dans `Linkpearl.Core.Tests/Identity/CharacterStorageTests.cs` qu'aucun test n'énumère `Belongings` en dur ; s'il le fait, y ajouter `"groups.json"`.

- [ ] **Step 7: Suite, compilation du plugin, commit**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj && dotnet build Linkpearl/Linkpearl.csproj -c Release`
Expected: PASS, compilation sans warning.

```bash
git add Linkpearl/Core/Groups/GroupBookCodec.cs Linkpearl/Integration/GroupStore.cs Linkpearl/Integration/CharacterStorage.cs Linkpearl.Core.Tests/Groups/GroupBookCodecTests.cs
git commit -m "feat(groupes): groupes enregistrés par personnage, chiffrés par DPAPI"
```

---

### Task 4: Le handshake d'un pair de groupe

**Files:**
- Modify: `Linkpearl/Core/Sync/PeerSession.cs` (`EstablishAsync`, `InitiateAsync`, `RespondAsync`, `Authorizes`)
- Test: `Linkpearl.Core.Tests/Sync/PeerSessionGroupTests.cs`

**Interfaces:**
- Consumes: `PairRecord.Group`, `GroupOrigin`, `IGroupGate`, `GroupDerivation.MemberPairSecret`, `GroupDerivation.RuntimeId`, `HeldLink` (dans `PeerSessionTests.cs`), `MovableClock`, `SilentLog`.
- Produces: `PeerSession.EstablishAsync(IPeerLink link, PairRecord pair, PeerId ourId, ECDsa identity, IClock clock, ILogSink log, CancellationToken ct, IGroupGate? groups = null)`. Pour `pair.Group` non nul : initiateur = celui dont l'empreinte hexadécimale est la plus petite en ordinal ; clé admise si et seulement si `groups?.Admits(pair, key)` vaut vrai.
- Produces (test) : `RecordingGate(bool admit) : IGroupGate`, `internal`, réutilisable.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

internal sealed class RecordingGate(bool admit) : IGroupGate
{
    public List<byte[]> Seen { get; } = [];

    public bool Admits(PairRecord pair, byte[] publicKey)
    {
        lock (Seen)
            Seen.Add(publicKey);

        return admit;
    }
}

public sealed class PeerSessionGroupTests : IDisposable
{
    private static readonly byte[] Secret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    private static readonly PlayerFingerprint AlicePrint = PlayerFingerprint.Of("alice", 21);
    private static readonly PlayerFingerprint BobPrint = PlayerFingerprint.Of("bob", 21);

    private readonly MovableClock _clock = new();
    private readonly List<IDisposable> _keys = [];

    public void Dispose()
    {
        foreach (var key in _keys)
            key.Dispose();
    }

    private (System.Security.Cryptography.ECDsa Key, byte[] Public, PeerId Id) Identity()
    {
        var key = CryptoPrimitives.GenerateIdentity();
        _keys.Add(key);
        var point = CryptoPrimitives.ExportPublicPoint(key);
        return (key, point, PeerId.Of(point));
    }

    /// <summary>Ce que le planificateur donnerait à chacun : même secret, même identifiant, origines miroir.</summary>
    private PairRecord Seeing(PlayerFingerprint ours, PlayerFingerprint theirs)
    {
        var secret = GroupDerivation.MemberPairSecret(Secret, ours, theirs);

        return new PairRecord
        {
            Id = GroupDerivation.RuntimeId(secret),
            PairSecret = secret,
            DisplayName = "Membre",
            Rendezvous = [new RendezvousAddress("rdv.exemple.ch", 47900)],
            Trust = PairTrust.Accepted,
            PairedAt = _clock.UtcNow,
            PinnedFingerprint = theirs,
            Group = new GroupOrigin(GroupId.Of(Secret), ours, theirs),
        };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deux_membres_etablissent_une_session_quel_que_soit_l_ordre(bool aliceFirst)
    {
        // Les identifiants de runtime sont les mêmes des deux côtés : les
        // comparer, comme pour une paire, ferait deux initiateurs ou deux
        // répondeurs. Ce sont les empreintes qui décident.
        var alice = Identity();
        var bob = Identity();
        var (aliceLink, bobLink) = HeldLink.Pair();
        var aliceGate = new RecordingGate(admit: true);
        var bobGate = new RecordingGate(admit: true);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        Task<PeerSession?> Alice() => PeerSession.EstablishAsync(
            aliceLink, Seeing(AlicePrint, BobPrint), alice.Id, alice.Key, _clock, new SilentLog(), timeout.Token, aliceGate);

        Task<PeerSession?> Bob() => PeerSession.EstablishAsync(
            bobLink, Seeing(BobPrint, AlicePrint), bob.Id, bob.Key, _clock, new SilentLog(), timeout.Token, bobGate);

        var (first, second) = aliceFirst ? (Alice(), Bob()) : (Bob(), Alice());
        var sessions = await Task.WhenAll(first, second);

        Assert.All(sessions, Assert.NotNull);
        Assert.Equal(bob.Public, Assert.Single(aliceGate.Seen));
        Assert.Equal(alice.Public, Assert.Single(bobGate.Seen));

        foreach (var session in sessions)
            await session!.DisposeAsync();
    }

    [Fact]
    public async Task Une_porte_qui_refuse_empeche_la_session()
    {
        // L'empreinte de bob est la plus petite : bob initie, alice répond et
        // vérifie au dernier message. C'est son refus qu'on observe, sans
        // attendre le délai de garde de quinze secondes de l'autre côté.
        Assert.True(string.CompareOrdinal(BobPrint.ToString(), AlicePrint.ToString()) < 0);

        var alice = Identity();
        var bob = Identity();
        var (aliceLink, bobLink) = HeldLink.Pair();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var aliceSide = PeerSession.EstablishAsync(
            aliceLink, Seeing(AlicePrint, BobPrint), alice.Id, alice.Key, _clock, new SilentLog(), timeout.Token,
            new RecordingGate(admit: false));

        var bobSide = PeerSession.EstablishAsync(
            bobLink, Seeing(BobPrint, AlicePrint), bob.Id, bob.Key, _clock, new SilentLog(), timeout.Token,
            new RecordingGate(admit: true));

        Assert.Null(await aliceSide);

        if (await bobSide is { } opened)
            await opened.DisposeAsync();
    }

    [Fact]
    public async Task Un_pair_de_groupe_sans_porte_est_refuse()
    {
        var alice = Identity();
        var bob = Identity();
        var (aliceLink, bobLink) = HeldLink.Pair();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var aliceSide = PeerSession.EstablishAsync(
            aliceLink, Seeing(AlicePrint, BobPrint), alice.Id, alice.Key, _clock, new SilentLog(), timeout.Token);

        var bobSide = PeerSession.EstablishAsync(
            bobLink, Seeing(BobPrint, AlicePrint), bob.Id, bob.Key, _clock, new SilentLog(), timeout.Token,
            new RecordingGate(admit: true));

        Assert.Null(await aliceSide);

        if (await bobSide is { } opened)
            await opened.DisposeAsync();
    }
}
```

- [ ] **Step 2: Vérifier qu'ils échouent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter PeerSessionGroupTests`
Expected: échec de compilation, `EstablishAsync` n'a pas de paramètre `IGroupGate`.

- [ ] **Step 3: Modifier `PeerSession`**

Ajouter `using Linkpearl.Core.Groups;` en tête. Ajouter un champ :

```csharp
    /// <summary>Ce qui admet la clé d'un pair de groupe. Null quand le moteur n'a pas de groupes.</summary>
    private IGroupGate? _groups;
```

Remplacer la signature et le choix de l'initiateur dans `EstablishAsync` :

```csharp
    public static async Task<PeerSession?> EstablishAsync(
        IPeerLink link, PairRecord pair, PeerId ourId, ECDsa identity, IClock clock, ILogSink log,
        CancellationToken ct, IGroupGate? groups = null)
    {
        var session = new PeerSession(link, pair, log) { State = PeerSessionState.Handshaking, _groups = groups };

        try
        {
            var weInitiate = WeInitiate(pair, ourId);
```

Ajouter la méthode :

```csharp
    /// <summary>
    /// Qui des deux envoie le premier message.
    /// </summary>
    /// <remarks>
    /// Pour un pair de groupe, l'identifiant du pair est tiré du secret du
    /// couple et vaut la même chose des deux côtés : le comparer au nôtre
    /// pourrait donner deux initiateurs. Les deux empreintes, elles, sont
    /// connues des deux et différentes.
    /// </remarks>
    private static bool WeInitiate(PairRecord pair, PeerId ourId)
        => pair.Group is { } origin
            ? string.CompareOrdinal(origin.Ours.ToString(), origin.Theirs.ToString()) < 0
            : string.CompareOrdinal(ourId.ToHex(), pair.Id.ToHex()) < 0;
```

Dans `InitiateAsync` et `RespondAsync`, remplacer `key => Authorizes(pair, key)` par `key => Authorizes(pair, key, _groups)`, et remplacer `Authorizes` :

```csharp
    /// <summary>
    /// La clé reçue doit être celle du pair attendu, et de personne d'autre.
    /// </summary>
    /// <remarks>
    /// Pour une paire, l'empreinte de la clé sert de comparaison : une clé
    /// substituée donne une autre empreinte, donc un refus. Pour un pair de
    /// groupe, on ne connaît pas sa clé d'avance : c'est le groupe qui décide,
    /// par l'épinglage du premier vu. Sans groupe pour trancher, on refuse.
    /// </remarks>
    private static bool Authorizes(PairRecord pair, byte[] publicKey, IGroupGate? groups)
    {
        if (pair.Group is not null)
            return groups?.Admits(pair, publicKey) ?? false;

        if (PeerId.Of(publicKey) != pair.Id)
            return false;

        return pair.PublicKey is not { } known || known.AsSpan().SequenceEqual(publicKey);
    }
```

- [ ] **Step 4: Vérifier que les tests passent, voisins compris**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter "PeerSessionGroupTests|PeerSessionTests|HandshakeTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Linkpearl/Core/Sync/PeerSession.cs Linkpearl.Core.Tests/Sync/PeerSessionGroupTests.cs
git commit -m "feat(sync): handshake d'un pair de groupe, initiateur choisi par les empreintes"
```

---

### Task 5: Le planificateur

**Files:**
- Create: `Linkpearl/Core/Groups/GroupDialPlanner.cs`
- Test: `Linkpearl.Core.Tests/Groups/GroupDialPlannerTests.cs`

**Interfaces:**
- Consumes: `GroupRecord`, `GroupMember`, `GroupOrigin`, `GroupDerivation`, `PairRecord`, `IClock`, `GroupBookTests.Group`.
- Produces:
  - `sealed record GroupSighting(GroupId Group, PlayerFingerprint Member, string DisplayName)`.
  - `sealed class GroupDialPlanner(IClock clock)` : `MaxSessions = 48`, `static readonly TimeSpan Linger` (5 min), `IReadOnlyList<PairRecord> Plan(PlayerFingerprint ours, IReadOnlyList<GroupSighting> sightings, IReadOnlyList<GroupRecord> groups, IEnumerable<PlayerFingerprint> directlyPaired)`. Non réentrant : appelé depuis le seul fil de rafraîchissement.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Tests.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Groups;

public sealed class GroupDialPlannerTests
{
    private static readonly byte[] Secret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    private static readonly byte[] OtherSecret = [.. Enumerable.Range(0, 32).Select(i => (byte)(255 - i))];
    private static readonly PlayerFingerprint Alice = PlayerFingerprint.Of("alice", 21);
    private static readonly PlayerFingerprint Bob = PlayerFingerprint.Of("bob", 21);

    private readonly MovableClock _clock = new();

    private GroupRecord Group(byte[] secret) => GroupBookTests.Group(secret, _clock.UtcNow);

    [Fact]
    public void Un_membre_vu_devient_un_pair_de_groupe_identique_des_deux_cotes()
    {
        var group = Group(Secret);

        var fromAlice = Assert.Single(new GroupDialPlanner(_clock)
            .Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [group], []));

        var fromBob = Assert.Single(new GroupDialPlanner(_clock)
            .Plan(Bob, [new GroupSighting(group.Id, Alice, "Alice")], [group], []));

        Assert.Equal(fromAlice.Id, fromBob.Id);
        Assert.Equal(fromAlice.PairSecret, fromBob.PairSecret);
        Assert.Equal(new GroupOrigin(group.Id, Alice, Bob), fromAlice.Group);
        Assert.Equal(Bob, fromAlice.PinnedFingerprint);
        Assert.Equal("Bob", fromAlice.DisplayName);
        Assert.Equal(PairTrust.Accepted, fromAlice.Trust);
        Assert.Equal(group.Rendezvous, fromAlice.Rendezvous);
    }

    [Fact]
    public void On_ne_se_compose_ni_avec_soi_ni_dans_un_groupe_inconnu()
    {
        var group = Group(Secret);

        Assert.Empty(new GroupDialPlanner(_clock).Plan(
            Alice,
            [new GroupSighting(group.Id, Alice, "Moi"), new GroupSighting(Group(OtherSecret).Id, Bob, "Bob")],
            [group],
            []));
    }

    [Fact]
    public void Deux_groupes_communs_donnent_un_seul_pair_celui_du_plus_petit_groupe()
    {
        var one = Group(Secret);
        var two = Group(OtherSecret);
        var smaller = one.Id.CompareTo(two.Id) < 0 ? one : two;

        var planned = Assert.Single(new GroupDialPlanner(_clock).Plan(
            Alice,
            [new GroupSighting(one.Id, Bob, "Bob"), new GroupSighting(two.Id, Bob, "Bob")],
            [one, two],
            []));

        Assert.Equal(smaller.Id, planned.Group!.Group);
    }

    [Fact]
    public void Une_paire_directe_n_est_pas_doublee()
    {
        var group = Group(Secret);

        Assert.Empty(new GroupDialPlanner(_clock)
            .Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [group], [Bob]));
    }

    [Fact]
    public void Un_membre_en_pause_n_est_pas_compose_et_ses_reglages_suivent()
    {
        var paused = Group(Secret) with
        {
            Members = new Dictionary<PlayerFingerprint, GroupMember>
            {
                [Bob] = new() { Fingerprint = Bob, DisplayName = "Bob", Paused = true },
            },
        };

        Assert.Empty(new GroupDialPlanner(_clock)
            .Plan(Alice, [new GroupSighting(paused.Id, Bob, "Bob")], [paused], []));

        var quiet = paused with
        {
            Members = new Dictionary<PlayerFingerprint, GroupMember>
            {
                [Bob] = new() { Fingerprint = Bob, DisplayName = "Bobby", Receive = TransientCategories.None },
            },
        };

        var planned = Assert.Single(new GroupDialPlanner(_clock)
            .Plan(Alice, [new GroupSighting(quiet.Id, Bob, "Bob")], [quiet], []));

        Assert.Equal(TransientCategories.None, planned.Receive);
        Assert.Equal("Bobby", planned.DisplayName);
    }

    [Fact]
    public void Un_membre_hors_de_vue_reste_cinq_minutes_puis_part()
    {
        var group = Group(Secret);
        var planner = new GroupDialPlanner(_clock);

        Assert.Single(planner.Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [group], []));

        _clock.Advance(GroupDialPlanner.Linger);
        Assert.Single(planner.Plan(Alice, [], [group], []));

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(planner.Plan(Alice, [], [group], []));
    }

    [Fact]
    public void Un_groupe_quitte_emporte_ses_membres_sans_attendre()
    {
        var group = Group(Secret);
        var planner = new GroupDialPlanner(_clock);

        Assert.Single(planner.Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [group], []));
        Assert.Empty(planner.Plan(Alice, [], [], []));
    }

    [Fact]
    public void Au_dela_de_48_membres_les_plus_recemment_vus_passent_d_abord()
    {
        var group = Group(Secret);
        var planner = new GroupDialPlanner(_clock);
        var early = PlayerFingerprint.Of("parti", 21);

        planner.Plan(Alice, [new GroupSighting(group.Id, early, "Parti")], [group], []);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var crowd = Enumerable.Range(0, GroupDialPlanner.MaxSessions)
            .Select(i => new GroupSighting(group.Id, PlayerFingerprint.Of($"foule{i}", 21), $"F{i}"))
            .ToList();

        var planned = planner.Plan(Alice, crowd, [group], []);

        Assert.Equal(GroupDialPlanner.MaxSessions, planned.Count);
        Assert.DoesNotContain(planned, pair => pair.PinnedFingerprint == early);
    }
}
```

- [ ] **Step 2: Vérifier qu'ils échouent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupDialPlannerTests`
Expected: échec de compilation, `GroupDialPlanner` inconnu.

- [ ] **Step 3: Écrire `GroupDialPlanner.cs`**

```csharp
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;

namespace Linkpearl.Core.Groups;

/// <summary>Un joueur visible dont la boîte de présence de groupe répond.</summary>
public sealed record GroupSighting(GroupId Group, PlayerFingerprint Member, string DisplayName);

/// <summary>
/// Transforme ce qu'on voit en pairs à joindre.
/// </summary>
/// <remarks>
/// On ne se connecte qu'aux membres visibles : une compagnie libre de cent
/// personnes ferait sinon cent sessions permanentes par joueur. Un membre qui
/// sort du champ reste cinq minutes, le temps d'un aller-retour dans une autre
/// pièce, sans refaire tout le transfert.
///
/// Appelé depuis le seul fil de rafraîchissement : il n'a pas de verrou.
/// </remarks>
public sealed class GroupDialPlanner(IClock clock)
{
    /// <summary>
    /// Un lieu de RP bondé ne doit pas tenir cent liens, chacun avec trente-deux
    /// canaux et sa fenêtre d'envoi. Quarante-huit couvre largement ce qu'un
    /// joueur voit autour de lui.
    /// </summary>
    public const int MaxSessions = 48;

    public static readonly TimeSpan Linger = TimeSpan.FromMinutes(5);

    private readonly Dictionary<PlayerFingerprint, (GroupId Group, string Name, DateTimeOffset Seen)> _recent = [];

    public IReadOnlyList<PairRecord> Plan(
        PlayerFingerprint ours,
        IReadOnlyList<GroupSighting> sightings,
        IReadOnlyList<GroupRecord> groups,
        IEnumerable<PlayerFingerprint> directlyPaired)
    {
        var byId = groups.ToDictionary(group => group.Id);
        var now = clock.UtcNow;

        // Deux groupes en commun : le plus petit identifiant, choisi pareil des
        // deux côtés sans se concerter, et une seule session.
        foreach (var seen in sightings
                     .Where(sighting => sighting.Member != ours && byId.ContainsKey(sighting.Group))
                     .GroupBy(sighting => sighting.Member))
        {
            var chosen = seen.MinBy(sighting => sighting.Group)!;
            _recent[seen.Key] = (chosen.Group, chosen.DisplayName, now);
        }

        foreach (var (member, entry) in _recent.ToList())
            if (now - entry.Seen > Linger || byId.ContainsKey(entry.Group) is false)
                _recent.Remove(member);

        // Une paire du carnet l'emporte toujours. Le carnet entier, retraits
        // compris : l'autre côté peut encore nous tenir pour une paire.
        var direct = directlyPaired.ToHashSet();

        return [.. _recent
            .Where(entry => direct.Contains(entry.Key) is false)
            .Select(entry => (
                Member: entry.Key,
                entry.Value.Group,
                entry.Value.Name,
                entry.Value.Seen,
                Known: byId[entry.Value.Group].Members.GetValueOrDefault(entry.Key)))
            .Where(candidate => candidate.Known is not { Paused: true })
            .OrderByDescending(candidate => candidate.Seen)
            .ThenBy(candidate => candidate.Member.ToString(), StringComparer.Ordinal)
            .Take(MaxSessions)
            .Select(candidate => Record(
                ours, byId[candidate.Group], candidate.Member, candidate.Known?.DisplayName ?? candidate.Name, candidate.Known))];
    }

    private static PairRecord Record(
        PlayerFingerprint ours, GroupRecord group, PlayerFingerprint theirs, string name, GroupMember? known)
    {
        var secret = GroupDerivation.MemberPairSecret(group.Secret, ours, theirs);

        return new PairRecord
        {
            Id = GroupDerivation.RuntimeId(secret),
            PairSecret = secret,
            DisplayName = name,
            Rendezvous = group.Rendezvous,
            Trust = PairTrust.Accepted,
            PairedAt = group.JoinedAt,
            PinnedFingerprint = theirs,
            Receive = known?.Receive ?? TransientCategories.All,
            Group = new GroupOrigin(group.Id, ours, theirs),
        };
    }
}
```

- [ ] **Step 4: Vérifier que les tests passent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupDialPlannerTests`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add Linkpearl/Core/Groups/GroupDialPlanner.cs Linkpearl.Core.Tests/Groups/GroupDialPlannerTests.cs
git commit -m "feat(groupes): planificateur des membres visibles à joindre"
```

---

### Task 6: Le moteur joint les pairs de groupe

**Files:**
- Modify: `Linkpearl/Core/Sync/SyncEngine.cs` (constructeur, `ReconcileBookAsync`, `DialAsync`, `PumpAsync`, `SetGroupPeers`, `EndsOnUnpair`)
- Test: `Linkpearl.Core.Tests/Sync/GroupSyncEngineTests.cs`

**Interfaces:**
- Consumes: `IGroupGate`, `GroupBook`, `GroupDialPlanner`, `GroupSighting`, `PeerSession.EstablishAsync(..., IGroupGate?)`, les doublures de `SyncEngineTests.cs` (`MeetingPoint`, `MeetingDialer`, `FixedAppearance`, `RecordingApplicator`, `SilentLog`), `MovableClock`, `GroupBookTests.Group`.
- Produces: `SyncEngine(PairBook book, IPeerDialer dialer, ILocalAppearance local, IRemoteApplicator applicator, IBlobStore store, PeerId ourId, ECDsa identity, IClock clock, ILogSink log, SyncEngineSettings? settings = null, Quotas? quotas = null, IGroupGate? groups = null)`, `void SetGroupPeers(IReadOnlyList<PairRecord> peers)` (appelable depuis n'importe quel fil) et `internal static bool EndsOnUnpair(PairRecord pair)`.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using System.Text;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Tests.Groups;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

/// <summary>
/// Deux moteurs réels qui ne se connaissent que par un groupe : carnets vides.
/// </summary>
public sealed class GroupSyncEngineTests : IDisposable
{
    private static readonly byte[] Secret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    private static readonly PlayerFingerprint AlicePrint = PlayerFingerprint.Of("alice", 21);
    private static readonly PlayerFingerprint BobPrint = PlayerFingerprint.Of("bob", 21);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "linkpearl-groupe-" + Guid.NewGuid().ToString("N"));
    private readonly MovableClock _clock = new();
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed record World(
        SyncEngine Alice, SyncEngine Bob, GroupBook BobGroups, RecordingApplicator BobApplicator,
        PeerId AliceId, PeerId RuntimeId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Alice.DisposeAsync();
            await Bob.DisposeAsync();
        }
    }

    private async Task<World> WorldAsync(GroupMember? bobKnowsAlice = null)
    {
        var alice = CryptoPrimitives.GenerateIdentity();
        var bob = CryptoPrimitives.GenerateIdentity();
        _disposables.Add(alice);
        _disposables.Add(bob);

        var aliceId = PeerId.Of(CryptoPrimitives.ExportPublicPoint(alice));
        var bobId = PeerId.Of(CryptoPrimitives.ExportPublicPoint(bob));

        var aliceStore = new FileSystemBlobStore(Path.Combine(_root, "alice"), new CacheSettings(), _clock, _ => long.MaxValue);
        var bobStore = new FileSystemBlobStore(Path.Combine(_root, "bob"), new CacheSettings(), _clock, _ => long.MaxValue);

        var content = Encoding.UTF8.GetBytes("une tenue de compagnie, en tout petit");
        var hash = BlobHash.OfContent(content);

        await using (var writer = await aliceStore.BeginWriteAsync(hash, content.Length, default))
        {
            await writer.WriteAsync(content, default);
            await writer.CommitAsync(default);
        }

        var manifest = new CharacterManifest(
            CharacterManifest.CurrentVersion,
            [new FileReplacement(["chara/equipment/e0001/model/c0101e0001_top.mdl"], hash, content.Length)],
            string.Empty, null);

        var group = GroupBookTests.Group(Secret, _clock.UtcNow);

        var aliceGroups = new GroupBook(_clock);
        aliceGroups.Load([group]);

        var bobGroups = new GroupBook(_clock);
        bobGroups.Load([bobKnowsAlice is null
            ? group
            : group with { Members = new Dictionary<PlayerFingerprint, GroupMember> { [AlicePrint] = bobKnowsAlice } }]);

        var point = new MeetingPoint();
        var bobApplicator = new RecordingApplicator();

        var aliceEngine = new SyncEngine(
            new PairBook(_clock), new MeetingDialer(point), new FixedAppearance(manifest, AlicePrint),
            new RecordingApplicator(), aliceStore, aliceId, alice, _clock, new SilentLog(), groups: aliceGroups);

        var bobEngine = new SyncEngine(
            new PairBook(_clock), new MeetingDialer(point), new FixedAppearance(null, BobPrint),
            bobApplicator, bobStore, bobId, bob, _clock, new SilentLog(), groups: bobGroups);

        aliceEngine.SetGroupPeers(new GroupDialPlanner(_clock)
            .Plan(AlicePrint, [new GroupSighting(group.Id, BobPrint, "Bob")], aliceGroups.All, []));

        bobEngine.SetGroupPeers(new GroupDialPlanner(_clock)
            .Plan(BobPrint, [new GroupSighting(group.Id, AlicePrint, "Alice")], bobGroups.All, []));

        var runtime = GroupDerivation.RuntimeId(GroupDerivation.MemberPairSecret(Secret, AlicePrint, BobPrint));

        return new World(aliceEngine, bobEngine, bobGroups, bobApplicator, aliceId, runtime);
    }

    private static async Task<bool> SettleAsync(World world, Func<bool> done, int rounds = 2000)
    {
        IReadOnlyList<VisiblePlayer> bobSees = [new VisiblePlayer(new GameObjectRef(4, 100), AlicePrint)];

        for (var i = 0; i < rounds; i++)
        {
            await world.Alice.TickAsync([], default);
            await world.Bob.TickAsync(bobSees, default);

            if (done())
                return true;

            await Task.Delay(10);
        }

        return false;
    }

    [Fact]
    public async Task Deux_membres_qui_se_voient_recoivent_l_apparence()
    {
        await using var world = await WorldAsync();

        Assert.True(await SettleAsync(world, () => world.BobApplicator.Applied.Count > 0), "rien n'a été posé");

        var applied = Assert.Single(world.BobApplicator.Applied);
        Assert.Equal(world.RuntimeId, applied.Peer);
        Assert.Equal(world.AliceId, world.BobGroups.Find(GroupId.Of(Secret))!.Members[AlicePrint].Id);
    }

    [Fact]
    public async Task Une_cle_contestee_ne_pose_rien()
    {
        var impostor = new GroupMember { Fingerprint = AlicePrint, Id = PeerId.Of([2, 9, 9, 9]), DisplayName = "Alice" };

        await using var world = await WorldAsync(bobKnowsAlice: impostor);

        Assert.False(await SettleAsync(world, () => world.BobApplicator.Applied.Count > 0, rounds: 300));
    }

    [Fact]
    public async Task Un_membre_retire_du_plan_voit_son_apparence_effacee()
    {
        await using var world = await WorldAsync();

        Assert.True(await SettleAsync(world, () => world.BobApplicator.Applied.Count > 0), "rien n'a été posé");

        world.Bob.SetGroupPeers([]);

        Assert.True(
            await SettleAsync(world, () => world.BobApplicator.Removed.Contains(world.RuntimeId)),
            "l'apparence est restée après le départ du plan");
    }

    [Fact]
    public void Un_avis_de_retrait_n_arrete_qu_une_paire_du_carnet()
    {
        var group = GroupBookTests.Group(Secret, _clock.UtcNow);
        var member = Assert.Single(new GroupDialPlanner(_clock)
            .Plan(BobPrint, [new GroupSighting(group.Id, AlicePrint, "Alice")], [group], []));

        Assert.False(SyncEngine.EndsOnUnpair(member));
        Assert.True(SyncEngine.EndsOnUnpair(member with { Group = null }));
    }
}
```

- [ ] **Step 2: Vérifier qu'ils échouent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupSyncEngineTests`
Expected: échec de compilation, paramètre `groups`, `SetGroupPeers` et `EndsOnUnpair` inconnus.

- [ ] **Step 3: Modifier `SyncEngine`**

Ajouter `using Linkpearl.Core.Groups;`. Ajouter les champs :

```csharp
    private readonly IGroupGate? _groups;

    /// <summary>
    /// Les pairs de groupe voulus, remplacés d'un bloc par le fil de rafraîchissement.
    /// </summary>
    /// <remarks>
    /// Une liste immuable échangée par référence : le tic la lit une fois et
    /// travaille sur sa copie, sans verrou et sans voir une liste à moitié écrite.
    /// </remarks>
    private volatile IReadOnlyList<PairRecord> _groupPeers = [];
```

Constructeur : ajouter le paramètre final `IGroupGate? groups = null` et `_groups = groups;`.

Méthodes :

```csharp
    /// <summary>Les membres de groupe à joindre, tels que le planificateur les voit.</summary>
    public void SetGroupPeers(IReadOnlyList<PairRecord> peers) => _groupPeers = peers;

    /// <summary>
    /// Vrai si un avis de retrait de ce pair doit le retirer.
    /// </summary>
    /// <remarks>
    /// Un pair de groupe n'est pas dans notre carnet : son avis ne peut viser
    /// qu'une paire qu'il croit avoir avec nous, et qui n'existe pas ici. Le
    /// suivre dirait « a mis fin au pairage » à propos d'un groupe.
    /// </remarks>
    internal static bool EndsOnUnpair(PairRecord pair) => pair.Group is null;
```

Dans `ReconcileBookAsync`, remplacer la première ligne :

```csharp
        // Un pair retiré se joint encore, le temps de le lui dire. Les membres
        // de groupe viennent en plus, sans jamais masquer une entrée du carnet.
        var active = _book.Active.Concat(_book.Revoked).ToDictionary(pair => pair.Id);

        foreach (var member in _groupPeers)
            active.TryAdd(member.Id, member);
```

Dans `DialAsync`, passer la porte :

```csharp
        var session = await PeerSession
            .EstablishAsync(attempt.Link, pair, _ourId, _identity, _clock, _log, ct, _groups)
            .ConfigureAwait(false);
```

Dans `PumpAsync`, remplacer le test de l'avis :

```csharp
                if (message.Kind == MessageKind.Unpair)
                {
                    if (EndsOnUnpair(runtime.Pair))
                        runtime.EndedByPeer = true;

                    continue;
                }
```

`ReconcileVisibility` n'a rien à changer : un pair de groupe porte toujours `PinnedFingerprint`, donc il passe par la branche de comparaison et jamais par `_book.PinFingerprint`. `_book.Seen(id)` sur un identifiant absent du carnet ne fait rien.

- [ ] **Step 4: Vérifier que les tests passent, puis toute la suite**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter GroupSyncEngineTests`
Expected: PASS, 4 tests.

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: PASS, `SyncEngineTests` inchangés.

- [ ] **Step 5: Commit**

```bash
git add Linkpearl/Core/Sync/SyncEngine.cs Linkpearl.Core.Tests/Sync/GroupSyncEngineTests.cs
git commit -m "feat(sync): le moteur joint les membres de groupe visibles, hors carnet"
```

---

### Task 7: Le service tient 64 boîtes par connexion

**Files:**
- Modify: `../rendezvous/Linkpearl.Rendezvous/RendezvousLimits.cs:44-45`
- Test: `../rendezvous/Linkpearl.Rendezvous.Tests/RendezvousServerTests.cs`

**Interfaces:**
- Produces: `RendezvousLimits.MaxMailboxesPerSession = 64` par défaut. Aucune trame ni fichier copié ne change : pas de recopie vers le plugin.

- [ ] **Step 1: Écrire le test qui échoue**

Dans `RendezvousServerTests.cs`, à côté du test de la ligne 218 (qui passe la limite à 2 explicitement et reste inchangé) :

```csharp
    [Fact]
    public void Une_connexion_tient_par_defaut_les_boites_de_dix_groupes()
        // Deux personnelles, puis deux de présence et deux d'admission par
        // groupe au changement de fenêtre : 42 pour dix groupes.
        => Assert.True(new RendezvousLimits().MaxMailboxesPerSession >= 42);
```

Run: `cd ../rendezvous && dotnet test --filter Une_connexion_tient_par_defaut_les_boites_de_dix_groupes`
Expected: FAIL (16).

- [ ] **Step 2: Relever la limite**

```csharp
    /// <summary>
    /// Boîtes qu'une même connexion peut tenir ouvertes.
    /// </summary>
    /// <remarks>
    /// Deux personnelles, et deux de présence plus deux d'admission par groupe
    /// au changement de fenêtre : 42 pour les dix groupes qu'un personnage peut
    /// avoir. Le client se reconnecte avant d'atteindre la limite.
    /// </remarks>
    public int MaxMailboxesPerSession { get; init; } = 64;
```

- [ ] **Step 3: Suite du service, commit dans son dépôt**

Run: `cd ../rendezvous && dotnet test`
Expected: PASS.

```bash
cd ../rendezvous
git add Linkpearl.Rendezvous/RendezvousLimits.cs Linkpearl.Rendezvous.Tests/RendezvousServerTests.cs
git commit -m "feat(limites): 64 boîtes par connexion, pour les groupes"
```

- [ ] **Step 4: Déployer, après accord explicite de l'utilisateur**

Le service est public : ne pas déployer sans son « oui ». Puis :

```bash
cd ../rendezvous
LPRDV_HOST=debian@rdv.linkpearl.eorzea.events LPRDV_KEY=~/.ssh/linkpearl_rdv ./deploy/deploy.sh
```

Expected: `/healthz` répond. Une valeur réglée à chaud dans `settings.json` du service prime sur le défaut : vérifier dans la console qu'elle n'y est pas figée à 16.

---

### Task 8: La présence ouvre et interroge les boîtes de groupe

**Files:**
- Modify: `Linkpearl/Integration/PresenceService.cs`

**Interfaces:**
- Consumes: `GroupRecord`, `GroupDerivation.PresenceAround`, `GroupDerivation.PresenceAddress`, `GroupId`.
- Produces:
  - `void SetGroups(IReadOnlyList<GroupRecord> groups)` ;
  - `IReadOnlyList<GroupId> GroupsOf(PlayerFingerprint member)` : les groupes dont ce joueur a répondu à la dernière interrogation ;
  - boîtes de présence ouvertes sur chaque connexion, et une connexion aussi vers chaque service de groupe absent des réglages.

Le service vit dans l'adaptateur et ne se teste pas sous Linux : la vérification est la compilation, puis l'essai en jeu de la tâche 9.

- [ ] **Step 1: Les groupes et le budget**

Ajouter `using Linkpearl.Core.Groups;` et les champs :

```csharp
    /// <summary>Remplacés d'un bloc par le fil de rafraîchissement, lus par les ouvertures.</summary>
    private volatile IReadOnlyList<GroupRecord> _groups = [];

    /// <summary>Par joueur détecté, les groupes dont sa boîte de présence a répondu.</summary>
    private readonly ConcurrentDictionary<PlayerFingerprint, IReadOnlyList<GroupId>> _groupPresence = new();

    /// <summary>
    /// Boîtes qu'on s'autorise sur une connexion avant de la refaire à neuf.
    /// </summary>
    /// <remarks>
    /// Le service ne retire jamais une adresse d'une connexion ouverte, et
    /// chaque fenêtre en ajoute : sans remise à zéro, dix groupes épuisent sa
    /// limite de 64 en une heure et demie, et l'ouverture suivante est refusée.
    /// Un peu sous la limite, pour ne jamais la toucher.
    /// </remarks>
    private const int MailboxBudget = 60;
```

Dans la classe `Session`, ajouter :

```csharp
        /// <summary>Adresses tenues par cette connexion, qui ne fait qu'en accumuler.</summary>
        public int OpenedCount { get; set; }

        /// <summary>Les groupes pour lesquels les boîtes ont été ouvertes.</summary>
        public string OpenedGroups { get; set; } = string.Empty;
```

Ajouter les méthodes :

```csharp
    public void SetGroups(IReadOnlyList<GroupRecord> groups) => _groups = groups;

    public IReadOnlyList<GroupId> GroupsOf(PlayerFingerprint member)
        => _groupPresence.TryGetValue(member, out var groups) ? groups : [];

    /// <summary>Ce qui distingue un jeu de groupes d'un autre, pour savoir s'il faut rouvrir.</summary>
    private static string Signature(IReadOnlyList<GroupRecord> groups)
        => string.Join(',', groups.Select(group => group.Id.ToString()).Order(StringComparer.Ordinal));
```

- [ ] **Step 2: Ouvrir aussi les boîtes de groupe**

Remplacer `Addresses` :

```csharp
    /// <summary>
    /// Les adresses à ouvrir : la boîte personnelle, et une boîte de présence
    /// par groupe, chacune sous la fenêtre courante et la suivante.
    /// </summary>
    private List<byte[]> Addresses(PlayerFingerprint fingerprint, IReadOnlyList<GroupRecord> groups)
        => [.. MailboxAddress.Around(fingerprint, _clock.UtcNow)
            .Concat(groups.SelectMany(group => GroupDerivation.PresenceAround(group.Secret, fingerprint, _clock.UtcNow)))
            .Select(address => address.ToBytes())];
```

Dans `OpenAsync`, remplacer `await client.OpenMailboxesAsync(Addresses(fingerprint), ct)` par :

```csharp
            var groups = _groups;
            var addresses = Addresses(fingerprint, groups);

            await client.OpenMailboxesAsync(addresses, ct).ConfigureAwait(false);
```

et, dans le bloc `lock (_gate)` qui suit, ajouter :

```csharp
                session.OpenedCount = addresses.Count;
                session.OpenedGroups = Signature(groups);
```

Remplacer `ReopenAsync` :

```csharp
    private async Task ReopenAsync(
        Session session, RendezvousClient client, PlayerFingerprint fingerprint, CancellationToken ct)
    {
        var window = MailboxAddress.IndexAt(_clock.UtcNow);
        var groups = _groups;
        var addresses = Addresses(fingerprint, groups);

        // Au-delà du budget, on repart d'une connexion neuve, rouverte dans la
        // même ronde : la fermeture ne repousse pas la prochaine tentative.
        if (session.OpenedCount + addresses.Count > MailboxBudget)
        {
            Close(session);
            return;
        }

        try
        {
            await client.OpenMailboxesAsync(addresses, ct).ConfigureAwait(false);

            lock (_gate)
            {
                session.OpenedWindow = window;
                session.OpenedCount += addresses.Count;
                session.OpenedGroups = Signature(groups);
            }
        }
        catch (Exception e)
        {
            // La connexion est peut-être morte sans qu'on l'ait vu : on la
            // ferme, et la même ronde la rouvrira proprement.
            _log.Warning(e, $"Réouverture des boîtes en échec sur {session.At}.");
            Close(session);
        }
    }
```

Lire `CloseLocked` : s'il repousse `NextAttempt`, la réouverture n'aurait lieu qu'à la ronde suivante ; dans ce cas, remettre `session.NextAttempt = _clock.UtcNow;` juste après l'appel `Close(session)` du dépassement de budget.

Dans `EnsureOpenAsync`, remplacer la condition de réouverture :

```csharp
            // Les adresses tournent toutes les trente minutes, et changent aussi
            // quand on rejoint ou quitte un groupe.
            if (session.Client is { } open
                && (session.OpenedWindow != MailboxAddress.IndexAt(_clock.UtcNow)
                    || session.OpenedGroups != Signature(_groups)))
                await ReopenAsync(session, open, fingerprint, ct).ConfigureAwait(false);
```

- [ ] **Step 3: Joindre aussi les services des groupes**

Dans `Reconcile`, remplacer la première ligne :

```csharp
        // Les services d'un groupe comptent même s'ils ne sont pas dans nos
        // réglages : c'est là que ses membres nous cherchent.
        var active = _configuration.ActiveRendezvous.Select(entry => entry.Address)
            .Concat(_groups.SelectMany(group => group.Rendezvous))
            .ToHashSet();
```

- [ ] **Step 4: Interroger les boîtes de groupe des joueurs détectés**

À la fin de `RefreshDetectionAsync`, après la boucle qui met à jour `_detected`, ajouter `await RefreshGroupPresenceAsync(nearby, connected, ct).ConfigureAwait(false);` et la méthode :

```csharp
    /// <summary>
    /// Demande, pour chaque joueur détecté, s'il est membre de l'un de nos groupes.
    /// </summary>
    /// <remarks>
    /// Seulement les joueurs détectés : un passant sans le plugin n'a pas de
    /// boîte de groupe, et l'interroger ne ferait que coûter au service. Un
    /// joueur qui a coupé la détection n'est donc pas trouvé par ses groupes,
    /// ce que l'interface devra dire.
    /// </remarks>
    private async Task RefreshGroupPresenceAsync(
        IReadOnlyList<NearbyPlayer> nearby, List<Session> connected, CancellationToken ct)
    {
        var groups = _groups;
        var detected = nearby.Where(player => _detected.ContainsKey(player.Fingerprint)).ToList();

        if (groups.Count == 0 || detected.Count == 0)
        {
            _groupPresence.Clear();
            return;
        }

        var questions = detected
            .SelectMany(player => groups.Select(group => (
                player.Fingerprint,
                Group: group.Id,
                Address: GroupDerivation.PresenceAddress(group.Secret, player.Fingerprint, _clock.UtcNow).ToBytes())))
            .ToList();

        var found = new HashSet<(PlayerFingerprint Member, GroupId Group)>();
        var answered = false;

        foreach (var session in connected)
        {
            foreach (var batch in questions.Chunk(RendezvousWire.MaxQueriedAddresses))
            {
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    deadline.CancelAfter(TimeSpan.FromSeconds(10));

                    var present = await session.Client!
                        .QueryPresenceAsync([.. batch.Select(question => question.Address)], deadline.Token)
                        .ConfigureAwait(false);

                    if (present is null)
                        break;

                    answered = true;

                    for (var i = 0; i < batch.Length && i < present.Length; i++)
                        if (present[i])
                            found.Add((batch[i].Fingerprint, batch[i].Group));
                }
                catch (Exception e)
                {
                    _log.Warning(e, $"Interrogation des groupes en échec sur {session.At}.");
                    break;
                }
            }
        }

        // Même règle que la détection : sans réponse, on ne retire personne.
        if (answered is false)
            return;

        _groupPresence.Clear();

        foreach (var member in found.GroupBy(pair => pair.Member))
            _groupPresence[member.Key] = [.. member.Select(pair => pair.Group)];
    }
```

`connected` est la variable locale déjà calculée au début de `RefreshDetectionAsync` ; si elle est déclarée avec `var` sur une expression `ToList()`, son type est bien `List<Session>`.

- [ ] **Step 5: Compiler et commit**

Run: `dotnet build Linkpearl/Linkpearl.csproj -c Release`
Expected: succès, aucun warning.

```bash
git add Linkpearl/Integration/PresenceService.cs
git commit -m "feat(presence): boîtes de présence de groupe, ouvertes et interrogées"
```

---

### Task 9: Câblage et essai en jeu

**Files:**
- Modify: `Linkpearl/Plugin.cs` (champs, constructeur, `TakeCharacter`, `ReleaseCharacter`, `StartEngineIfReady`, `RefreshLoopAsync`, commande, `Dispose`)

**Interfaces:**
- Consumes: `GroupBook`, `GroupStore`, `GroupDialPlanner`, `GroupSighting`, `GroupRecord`, `GroupId`, `PresenceService.SetGroups`, `PresenceService.GroupsOf`, `SyncEngine.SetGroupPeers`, paramètre `groups` du moteur.
- Produces: commande temporaire `/lpgroupe <phrase>` (rejoint un groupe d'essai dont le secret dérive de la phrase) et `/lpgroupe quitter`. **Temporaire** : l'incrément 2 la remplace par la page Groupes et la retire.

- [ ] **Step 1: Les champs**

```csharp
    private readonly GroupBook _groups;
    private readonly GroupDialPlanner _groupPlanner;
    private GroupStore? _groupStore;
```

Dans le constructeur, à côté de la création de `_presence` :

```csharp
        _groups = new GroupBook(clock);
        _groupPlanner = new GroupDialPlanner(clock);

        // Un épinglage arrive d'un handshake, hors du thread du jeu : l'écriture
        // se fait là où il arrive, le stockage se protège seul.
        _groups.Changed += () => _groupStore?.Save(_groups);
```

- [ ] **Step 2: Attacher et détacher avec le personnage**

Dans `TakeCharacter`, après `_pairing.Bind(root);` :

```csharp
        _groupStore = new GroupStore(Path.Combine(root, "groups.json"));
        _groupStore.Load(_groups);
```

Dans `ReleaseCharacter`, après `_pairing.Unbind();` :

```csharp
        _groupStore = null;
        _groups.Clear();
        _presence.SetGroups([]);
```

Dans `StartEngineIfReady`, passer la porte au moteur, en dernier argument nommé :

```csharp
            new PluginLogSink(Log, "moteur"), _engineSettings, groups: _groups);
```

- [ ] **Step 3: Planifier à chaque ronde de rafraîchissement**

Dans `RefreshLoopAsync`, juste avant `await _presence.EnsureOpenAsync(self.Fingerprint, ct)`, ajouter `_presence.SetGroups(_groups.All);`. Après le bloc `if (visible.SetEquals(_lastVisible) is false || stale) { ... }`, ajouter :

```csharp
                        // À chaque ronde et non seulement quand le champ change :
                        // un membre sorti du champ doit finir par partir, et
                        // c'est le passage du temps qui l'y conduit.
                        var sightings = _state.Nearby
                            .SelectMany(player => _presence.GroupsOf(player.Fingerprint)
                                .Select(group => new GroupSighting(group, player.Fingerprint, player.Name)))
                            .ToList();

                        var directly = _pairing.Book.All
                            .Select(pair => pair.PinnedFingerprint)
                            .OfType<PlayerFingerprint>();

                        _engine?.SetGroupPeers(_groupPlanner.Plan(self.Fingerprint, sightings, _groups.All, directly));
```

- [ ] **Step 4: La commande d'essai**

À côté de `Commands.AddHandler(Command, ...)` :

```csharp
        // Temporaire, jusqu'à la page Groupes de l'incrément 2 : de quoi
        // éprouver le noyau à deux personnages, avec un groupe dont le secret
        // dérive d'une phrase convenue.
        Commands.AddHandler(GroupTestCommand, new CommandInfo((_, args) => OnGroupTest(args))
        {
            HelpMessage = "Essai : /lpgroupe <phrase> rejoint un groupe d'essai, /lpgroupe quitter le quitte.",
            ShowInHelp = false,
        });
```

et, parmi les membres de la classe :

```csharp
    private const string GroupTestCommand = "/lpgroupe";

    private GroupId? _testGroup;

    private void OnGroupTest(string args)
    {
        var phrase = args.Trim();

        if (phrase is "quitter")
        {
            if (_testGroup is { } id && _groups.Remove(id))
                Report("groupe d'essai quitté.");

            _testGroup = null;
            return;
        }

        if (phrase.Length == 0 || _configuration.ActiveRendezvous.FirstOrDefault() is not { } service)
        {
            Report("usage : /lpgroupe <phrase>, avec un service de rendez-vous activé.");
            return;
        }

        var secret = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("linkpearl:group-test:v1:" + phrase));
        var group = new GroupRecord
        {
            Id = GroupId.Of(secret),
            Name = "Essai",
            Secret = secret,
            Rendezvous = [service.Address],
            JoinedAt = _clock.UtcNow,
        };

        Report(_groups.TryAdd(group, out var refusal) ? "groupe d'essai rejoint." : $"refusé : {refusal}");
        _testGroup = group.Id;
    }
```

Dans `Dispose`, à côté de `Commands.RemoveHandler(Command);`, ajouter `Commands.RemoveHandler(GroupTestCommand);`. Ajouter les `using` manquants (`Linkpearl.Core.Groups`, `System.Security.Cryptography`). Vérifier le nom réel du champ d'horloge (`_clock`) et de la méthode de message au joueur (`Report`) dans `Plugin.cs`.

- [ ] **Step 5: Compiler, suite, déployer**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj && dotnet build Linkpearl/Linkpearl.csproj -c Release`
Expected: PASS, aucun warning.

Run: `./scripts/deploy-plugin-dev.sh`

- [ ] **Step 6: Essai en jeu, par l'utilisateur**

Deux personnages sur une machine, **non pairés entre eux** (retirer la paire si elle existe), service à 64 boîtes (tâche 7) :
1. Sur chacun : `/lpgroupe lune-rousse`.
2. Se placer côte à côte : dans les 15 à 60 s, l'apparence moddée de l'autre apparaît, sans ligne dans le carnet.
3. Éloigner l'un hors de vue plus de cinq minutes : la session se ferme (journal : « pair retiré des actifs, session fermée »).
4. `/lpgroupe quitter` sur l'un : l'apparence de l'autre disparaît chez lui à la ronde de rafraîchissement suivante.
5. Recharger le plugin : le groupe d'essai est toujours là (`groups.json`).

- [ ] **Step 7: Commit**

```bash
git add Linkpearl/Plugin.cs
git commit -m "feat(groupes): câblage du noyau et commande d'essai /lpgroupe"
```

---

### Task 10: Documentation

**Files:**
- Modify: `docs/protocol.md`
- Modify: `docs/reprise.md`

- [ ] **Step 1: `protocol.md`**

Ajouter une section « Groupes (noyau) » qui documente, avec les étiquettes exactes et les vecteurs figés du tableau en tête de ce plan :
- la boîte de présence `SHA-256("linkpearl:group-presence:v1" || secret (32) || empreinte (16) || fenêtre (8, gros-boutiste))[0..6]`, fenêtres de 30 minutes ;
- le secret de paire de groupe `HKDF-SHA256(ikm = secret du groupe, sel = empreintes triées, info = "linkpearl:group-pair:v1", 32)` ;
- l'identifiant de runtime `SHA-256("linkpearl:group-runtime:v1" || secret de paire)[0..16]`, local, jamais transmis ;
- le choix de l'initiateur par comparaison ordinale des empreintes hexadécimales ;
- dans « Modèle de confiance » : tout membre peut calculer les jetons de tout couple ; parade par l'épinglage de la première clé vue ; le service apprend qu'un joueur tient une boîte de plus, sans savoir laquelle.

- [ ] **Step 2: `reprise.md`**

Dans « Ce qui vient ensuite », passer le point 3 à « en cours : incrément 1 livré (noyau), voir `superpowers/specs/2026-09-24-groupes-design.md` ». Dans « Ce qui reste de mémoire », noter : la commande `/lpgroupe` est temporaire ; un joueur qui a coupé la détection n'est pas trouvé par ses groupes ; un service tiers resté à 16 boîtes refuse l'ouverture dès huit groupes, et plus tôt au fil des fenêtres.

- [ ] **Step 3: Vérifier et commit**

Run: `grep -nP '\x{2014}' docs/protocol.md docs/reprise.md`
Expected: aucune ligne.

```bash
git add docs/protocol.md docs/reprise.md
git commit -m "docs(groupes): dérivations du noyau et état de la reprise"
```
