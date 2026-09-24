# Groupes, incrément 3 : Public et listes de bannissement

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Un joueur active « Public » et voit l'apparence de tout joueur visible qui l'a activé aussi, animations, VFX et sons coupés par défaut ; et un personnage listé par un service actif n'est plus ni pairé, ni admis, ni composé, ni posé à l'écran.

**Architecture:** Public est un `GroupRecord` comme les autres, au secret constant, sans clé ni politique, que `GroupBook` garde même désactivé (« dormant ») pour ne pas perdre les blocages. Les listes de bannissement descendent du service par deux trames paginées (`BanListQuery`, `BanListData`), et le noyau les applique par empreinte de personnage dans `ServiceBanBook` : l'adaptateur calcule la dérivation PBKDF2 à partir du nom, le noyau ne voit que des empreintes et des hachés. Le planificateur, le moteur et l'hôte d'admission consultent ce livre ; l'interface l'affiche.

**Tech Stack:** C# (.NET 10), xUnit, System.Security.Cryptography (SHA-256, PBKDF2), System.Text.Json, ImGui (Dalamud) pour l'interface. Deux dépôts : le plugin (worktree `groupes-noyau`) et le service (`~/Projects/linkpearl-sync/rendezvous`).

**Spec:** `docs/superpowers/specs/2026-09-24-groupes-design.md`, sections « Public », « Listes de bannissement (point 10) », « Interface » et « Livraison en trois incréments ». Incréments 1 et 2 livrés sur la branche `worktree-groupes-noyau` : `docs/superpowers/plans/2026-09-24-groupes-1-noyau.md` et `2026-09-24-groupes-2-prives.md`.

## Global Constraints

- `Linkpearl/Core/` ne référence jamais Dalamud (`ArchitectureTests`).
- Aucun nom de personnage ni chemin local complet dans les journaux par défaut. Un nom peut apparaître dans un message au joueur (`Report`), jamais dans `Log`.
- Le noyau ne reçoit jamais un nom pour le garder : `ServiceBanBook` ne voit que des `PlayerFingerprint` et des hachés dérivés ; la dérivation lui est passée en fonction par l'adaptateur.
- Pas de tiret cadratin (U+2014), nulle part. Commentaires en français, qui disent le pourquoi.
- `record` et `readonly record struct` pour les données ; jamais d'`enum` pour un octet qui traverse le réseau. Un `enum` strictement local est admis (`BanVerdict`).
- Toute donnée venue du réseau est décodée par un `TryRead…`/`TryParse` qui ne lève jamais et rejette le message entier sur une seule règle violée.
- Fichiers copiés dans le dépôt du service, à recopier et à vérifier par `diff` vide : `Core/Transport/Rendezvous/RendezvousWire.cs`, `Linkpearl.Core.Tests/Fixtures/rendezvous-vectors.json`, `Linkpearl.Core.Tests/Rendezvous/RendezvousVectorTests.cs`. `BanList.cs` et `BanListTests.cs` ne changent **pas** dans cet incrément.
- Commits Conventional Commits, sujet en français, **sans** ligne `Co-Authored-By`, `Claude-Session` ni mention « Generated with ».
- Après chaque tâche du plugin : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj` et `dotnet build Linkpearl/Linkpearl.csproj -c Release` (sans warning). Après chaque tâche du service : `dotnet test` à la racine de `~/Projects/linkpearl-sync/rendezvous`.
- Valeurs : secret Public `SHA-256("linkpearl:public:v1")` = `41b3c4bdfd876a04e0c524eca45a469d6baf181d0657e464eeef4f59bfaa5a7a` ; `GroupId` Public = `SHA-256(secret)[0..16]` = `c92263f11cad477c5082acba30895596` ; trames `BanListQuery = 0x15`, `BanListData = 0x16` ; 64 entrées par page, 64 pages au plus ; liste redemandée toutes les heures ; au plus 20 dérivations à la demande par minute.
- Texte d'activation de Public, mot pour mot : « Tout joueur visible qui a aussi activé Public verra votre apparence moddée, et vous la sienne. »

## Décisions prises en écrivant ce plan (rulings)

1. **La liste voyage par pages de 64 entrées.** La spec voulait le JSON de `/api/bans` « borné à la trame de 64 Kio ». Or une liste peut compter 4 096 entrées, et une entrée pèse jusqu'à 860 octets en JSON indenté (un motif de 120 caractères que l'encodeur par défaut échappe chacun en une séquence de six). Tronquer rendrait un banni invisible sans que personne le sache. Chaque page est donc une `BanList` complète (même sel, même coût) portant une tranche des entrées, et le client les fusionne. 64 × 860 octets tiennent sous 64 Kio. Coût si erroné : format des deux trames à revoir, rien n'est publié.
2. **Un service sans ces trames ne bloque rien.** Un service tiers d'avant cet incrément répond « trame inattendue » et ferme la connexion : on n'a alors aucune liste de lui, et un joueur sans liste à vérifier est « libre ». Sinon Public ne composerait jamais personne chez qui garde un service ancien.
3. **Verdict en attente : Public attend, le reste non.** Tant que la dérivation d'un joueur visible n'est pas faite, son verdict est « en attente ». Le planificateur ne compose pas un membre du Public en attente, puisque c'est un inconnu ; il compose un membre d'un groupe privé et le moteur pose l'apparence d'une paire directe, qui sont des gens choisis. Si le verdict tombe « listé » ensuite, la session se ferme et l'apparence se retire au tic suivant.
4. **Seuls les joueurs détectés et les paires du carnet sont vérifiés.** Un passant sans Linkpearl ne recevra jamais rien de nous : dériver pour lui coûterait des centaines de millisecondes de processeur pour rien. Une dérivation PBKDF2 à 600 000 itérations coûte de l'ordre de 0,1 à 0,3 s ; elles s'enchaînent sur une seule tâche du pool.
5. **Les dérivations à la demande sont plafonnées à 20 par minute.** Une demande de pairage ou d'admission arrive d'un inconnu, qui peut en forger sous mille noms. Au-delà du plafond, le verdict reste « en attente » et la demande est ignorée : sous une inondation, une demande honnête peut se perdre, ce que le joueur rattrape en redemandant.
6. **Public est un groupe gardé même désactivé.** `GroupRecord.Dormant` : un Public désactivé n'ouvre aucune boîte, ne compose personne, mais garde ses blocages et ses réglages par membre. Il ne compte pas dans les 10 groupes d'un personnage. Ses services sont ceux de la configuration, recopiés à chaque ronde.
7. **Le réglage des effets de Public est un défaut, pas un plafond.** `GroupMember.Receive` devient nullable : `null` suit `GroupRecord.DefaultReceive` (tout pour un groupe privé, rien pour Public), une valeur posée d'un clic sur un membre l'emporte. Ainsi « réactiver pour tout le Public » libère d'un coup tous ceux qu'on n'a pas réglés un par un, sans écraser un choix fait pour un membre.
8. **Bloquer un membre du Public, c'est le bannir chez soi.** `GroupRecord.Blocked` (clé et empreinte) est une liste locale, jamais transmise, consultée partout où la politique l'est (`GroupRecord.Refuses`). Générique sur tout groupe, exposée par l'interface pour Public seulement.
9. **L'avertissement de Public s'affiche à la première activation seulement** : `Configuration.PublicWarningSeen`. La spec dit « affiche une fois ».

## Review Focus

1. **Un service sans les nouvelles trames ne doit pas empêcher Public de fonctionner.** Test : `ServiceBanBookTests.Sans_liste_tout_le_monde_est_libre` (tâche 3), et `ServiceBanFetcher` garde la liste précédente ou aucune sur erreur (tâche 8, relecture).
2. **Une liste mise à jour ne doit pas refaire les dérivations, et un nouveau banni doit être vu aussitôt.** Test : `ServiceBanBookTests.Une_liste_mise_a_jour_reutilise_les_derivations` (tâche 3).
3. **Désactiver Public ne doit ni ouvrir de boîte, ni composer, ni perdre les blocages ; le réactiver les retrouve.** Tests : `PublicGroupTests.Public_dormant_n_est_pas_dans_All`, `PublicGroupTests.Reactiver_retrouve_les_blocages` (tâche 5), `GroupBookCodecTests.Public_dormant_se_relit_avec_ses_blocages_et_ses_reglages` (tâche 4).
4. **Une liste de plusieurs pages doit se recomposer entière, et des pages sous deux sels doivent être refusées.** Tests : `BanListPagesTests` (tâche 1) et `BanListServiceTests.Une_liste_de_70_entrees_tient_en_deux_pages` (tâche 2).
5. **Une paire directe déjà posée qui devient listée doit perdre son apparence au tic suivant.** Test : `ServiceBanEngineTests.Une_paire_listee_perd_son_apparence` (tâche 6).

---

## Fichiers

| Fichier | Rôle |
|---|---|
| `Linkpearl/Core/Transport/Rendezvous/RendezvousWire.cs` (copié) | `BanListQuery`, `BanListData`, bornes de pagination |
| `Linkpearl/Core/Transport/Rendezvous/RendezvousClient.cs` | `QueryBanListAsync` |
| `Linkpearl/Core/Safety/BanListPages.cs` (nouveau) | Fusion des pages en une liste |
| `Linkpearl/Core/Safety/ServiceBanBook.cs` (nouveau) | Listes par service, dérivations, verdicts |
| `Linkpearl/Core/Groups/PublicGroup.cs` (nouveau) | Secret, identifiant et fabrique du groupe Public |
| `Linkpearl/Core/Groups/GroupRecord.cs` | `Dormant`, `Blocked`, `DefaultReceive`, `IsPublic`, `Refuses`, `ReceiveOf` ; `GroupMember.Receive` nullable |
| `Linkpearl/Core/Groups/GroupBookCodec.cs` | Nouveaux champs, Public relu à part |
| `Linkpearl/Core/Groups/GroupBook.cs` | Activer Public, ses services, bloquer, défaut des effets ; `Refuses` dans `Admit` |
| `Linkpearl/Core/Groups/GroupDialPlanner.cs` | Blocages, bannis des services, Public en attente |
| `Linkpearl/Core/Groups/AdmissionHost.cs` | Filtre des candidats listés |
| `Linkpearl/Core/Sync/SyncEngine.cs` | Rien de posé sur un personnage listé |
| `Linkpearl/Integration/ServiceBanFetcher.cs` (nouveau) | Téléchargement horaire des listes |
| `Linkpearl/Integration/ServiceBanScreening.cs` (nouveau) | Dérivations des joueurs visibles, sur le pool |
| `Linkpearl/Integration/PresenceService.cs` | Demandes de pairage listées ignorées |
| `Linkpearl/Plugin.cs`, `Linkpearl/Configuration.cs` | Câblage, services de Public, avertissement |
| `Linkpearl/Ui/GroupActions.cs`, `Ui/Components/BanChip.cs` (nouveau), `Ui/Pages/GroupsPage.cs`, `Ui/Pages/NearbyPage.cs`, `Ui/Pages/PairsPage.cs` | Carte Public, blocage, motifs de bannissement |
| `docs/protocol.md`, `docs/reprise.md`, `README.md`, `README.fr.md` | Documentation |
| Service : `RendezvousServer.cs`, `BanStore.cs`, `PeerSession.cs`, `Program.cs`, `Tests/ServerHarness.cs`, `Tests/BanListServiceTests.cs` | Trames servies |

---

### Task 1: Les trames de bannissement et la fusion des pages (plugin)

**Files:**
- Modify: `Linkpearl/Core/Transport/Rendezvous/RendezvousWire.cs`
- Modify: `Linkpearl/Core/Transport/Rendezvous/RendezvousClient.cs`
- Create: `Linkpearl/Core/Safety/BanListPages.cs`
- Modify: `Linkpearl.Core.Tests/Fixtures/rendezvous-vectors.json`
- Modify: `Linkpearl.Core.Tests/Rendezvous/RendezvousVectorTests.cs`
- Test: `Linkpearl.Core.Tests/Rendezvous/BanListWireTests.cs`, `Linkpearl.Core.Tests/Safety/BanListPagesTests.cs`

**Interfaces:**
- Consumes: `BanList`, `BanEntry`, `BanParameters` (inchangés).
- Produces:
  - `RendezvousKind.BanListQuery = 0x15`, `RendezvousKind.BanListData = 0x16`
  - `RendezvousWire.BanListPageEntries = 64`, `RendezvousWire.MaxBanListPages = 64`
  - `RendezvousWire.BanListQuery(int page)`, `RendezvousWire.TryReadBanListQuery(ReadOnlySpan<byte>, out int page)`
  - `RendezvousWire.BanListData(int page, int pages, string json)`, `RendezvousWire.TryReadBanListData(ReadOnlySpan<byte>, out int page, out int pages, out string json, out string? rejection)`
  - `static class BanListPages { static bool TryMerge(IReadOnlyList<BanList> pages, out BanList? merged, out string? rejection) }`
  - `RendezvousClient.QueryBanListAsync(CancellationToken) : Task<(BanList? List, string? Failure)>`

- [ ] **Step 1: Écrire les tests qui échouent**

`Linkpearl.Core.Tests/Rendezvous/BanListWireTests.cs` :

```csharp
using Linkpearl.Core.Safety;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Rendezvous;

public sealed class BanListWireTests
{
    [Fact]
    public void Une_demande_de_page_fait_l_aller_retour()
    {
        var frame = RendezvousWire.BanListQuery(3);

        Assert.True(RendezvousWire.TryReadBanListQuery(frame, out var page));
        Assert.Equal(3, page);
    }

    [Theory]
    [InlineData(new byte[] { 0x15, 0x00 })]
    [InlineData(new byte[] { 0x15, 0x00, 0x40 })]          // page 64, hors bornes
    [InlineData(new byte[] { 0x16, 0x00, 0x01 })]          // mauvais type
    [InlineData(new byte[] { 0x15, 0x00, 0x01, 0x00 })]    // un octet de trop
    public void Une_demande_hors_regles_est_refusee(byte[] frame)
        => Assert.False(RendezvousWire.TryReadBanListQuery(frame, out _));

    [Fact]
    public void Une_page_fait_l_aller_retour()
    {
        var frame = RendezvousWire.BanListData(1, 3, "{\"version\":1}");

        Assert.True(RendezvousWire.TryReadBanListData(frame, out var page, out var pages, out var json, out var why), why);
        Assert.Equal((1, 3, "{\"version\":1}"), (page, pages, json));
    }

    [Theory]
    [InlineData(new byte[] { 0x16, 0x00, 0x00, 0x00 })]                  // tronquée
    [InlineData(new byte[] { 0x16, 0x00, 0x02, 0x00, 0x02, 0x7b })]      // page 2 sur 2
    [InlineData(new byte[] { 0x16, 0x00, 0x00, 0x00, 0x00, 0x7b })]      // zéro page
    [InlineData(new byte[] { 0x16, 0x00, 0x00, 0x00, 0x41, 0x7b })]      // 65 pages
    [InlineData(new byte[] { 0x16, 0x00, 0x00, 0x00, 0x01 })]            // sans texte
    public void Une_page_hors_regles_est_refusee(byte[] frame)
        => Assert.False(RendezvousWire.TryReadBanListData(frame, out _, out _, out _, out _));

    [Fact]
    public void Une_page_pleine_au_pire_tient_dans_une_trame()
    {
        // Le pire motif : 120 caractères que l'encodeur JSON par défaut échappe
        // chacun en une séquence de six. C'est ce qui fixe 64 entrées par page.
        var reason = new string('<', BanList.MaxReasonLength);
        var entries = Enumerable.Range(0, RendezvousWire.BanListPageEntries)
            .Select(i => new BanEntry(Enumerable.Repeat((byte)i, 32).ToArray(), reason, long.MaxValue))
            .ToList();

        var json = new BanList(new byte[32], BanParameters.Default, entries).ToJson(long.MaxValue);
        var frame = RendezvousWire.BanListData(0, RendezvousWire.MaxBanListPages, json);

        Assert.True(frame.Length <= RendezvousWire.MaxFrameLength, $"{frame.Length} octets");
    }

    [Fact]
    public void Toutes_les_entrees_possibles_tiennent_dans_les_pages()
        => Assert.True(RendezvousWire.BanListPageEntries * RendezvousWire.MaxBanListPages >= BanList.MaxEntries);
}
```

`Linkpearl.Core.Tests/Safety/BanListPagesTests.cs` :

```csharp
using Linkpearl.Core.Safety;
using Xunit;

namespace Linkpearl.Core.Tests.Safety;

public sealed class BanListPagesTests
{
    private static readonly BanParameters Cheap = new(1000);

    private static BanEntry Entry(byte seed) => new(Enumerable.Repeat(seed, 32).ToArray(), $"motif {seed}", seed);

    [Fact]
    public void Deux_pages_d_une_meme_liste_se_fusionnent()
    {
        var salt = Enumerable.Repeat((byte)7, 32).ToArray();

        Assert.True(BanListPages.TryMerge(
            [new BanList(salt, Cheap, [Entry(1), Entry(2)]), new BanList(salt, Cheap, [Entry(3)])],
            out var merged, out var why), why);

        Assert.Equal([1, 2, 3], merged!.Entries.Select(entry => (int)entry.Hash[0]));
        Assert.Equal(salt, merged.Salt);
    }

    [Fact]
    public void Une_entree_vue_sur_deux_pages_ne_compte_qu_une_fois()
    {
        // La liste peut changer entre deux pages : une entrée glisse d'une page
        // à la suivante et arrive deux fois.
        var salt = new byte[32];

        Assert.True(BanListPages.TryMerge(
            [new BanList(salt, Cheap, [Entry(1)]), new BanList(salt, Cheap, [Entry(1), Entry(2)])],
            out var merged, out _));

        Assert.Equal(2, merged!.Entries.Count);
    }

    [Fact]
    public void Des_pages_sous_deux_sels_sont_refusees()
    {
        Assert.False(BanListPages.TryMerge(
            [new BanList(new byte[32], Cheap, [Entry(1)]), new BanList(Enumerable.Repeat((byte)1, 32).ToArray(), Cheap, [Entry(2)])],
            out _, out var why));

        Assert.Contains("sel", why);
    }

    [Fact]
    public void Des_pages_sous_deux_couts_sont_refusees()
        => Assert.False(BanListPages.TryMerge(
            [new BanList(new byte[32], Cheap, []), new BanList(new byte[32], new BanParameters(2000), [])],
            out _, out _));

    [Fact]
    public void Aucune_page_n_est_pas_une_liste()
        => Assert.False(BanListPages.TryMerge([], out _, out _));
}
```

Dans `RendezvousVectorTests.cs`, ajouter à la fin du tableau `Frames` :

```csharp
        ("bannissement-demande", () => RendezvousWire.BanListQuery(1)),
        ("bannissement-page", () => RendezvousWire.BanListData(0, 2, "{\"version\":1}")),
```

et à la `Theory` des plafonds :

```csharp
    [InlineData("entreesParPageBannissement", RendezvousWire.BanListPageEntries)]
    [InlineData("pagesBannissementMax", RendezvousWire.MaxBanListPages)]
```

- [ ] **Step 2: Lancer les tests, constater l'échec**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter "FullyQualifiedName~BanList|FullyQualifiedName~RendezvousVector"`
Expected: échec de compilation (`BanListQuery`, `BanListPages` inconnus).

- [ ] **Step 3: Écrire les trames**

Dans `RendezvousKind`, après `DirectorySubmit` :

```csharp
    /// <summary>Demande une page de la liste de bannissement du service.</summary>
    public const byte BanListQuery = 0x15;

    /// <summary>Une page : son numéro, le nombre de pages, puis le JSON d'une <c>BanList</c>.</summary>
    public const byte BanListData = 0x16;
```

Dans `RendezvousWire`, avant `Frame` :

```csharp
    /// <summary>
    /// Entrées de la liste de bannissement par page.
    /// </summary>
    /// <remarks>
    /// Une entrée pèse jusqu'à 860 octets en JSON indenté : un motif de 120
    /// caractères que l'encodeur par défaut échappe chacun en six. Soixante-
    /// quatre tiennent sous une trame, et soixante-quatre pages couvrent les
    /// 4 096 entrées qu'une liste peut compter. Tronquer à une trame ferait un
    /// banni que plus personne ne voit, sans que rien le dise.
    /// </remarks>
    public const int BanListPageEntries = 64;

    public const int MaxBanListPages = 64;

    public static byte[] BanListQuery(int page)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(page, MaxBanListPages);

        return [RendezvousKind.BanListQuery, (byte)(page >> 8), (byte)page];
    }

    public static bool TryReadBanListQuery(ReadOnlySpan<byte> frame, out int page)
    {
        page = 0;

        if (frame.Length != 3 || frame[0] != RendezvousKind.BanListQuery)
            return false;

        page = (frame[1] << 8) | frame[2];
        return page < MaxBanListPages;
    }

    public static byte[] BanListData(int page, int pages, string json)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pages, MaxBanListPages);
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(page, pages);

        var text = System.Text.Encoding.UTF8.GetBytes(json);

        if (5 + text.Length > MaxFrameLength)
            throw new ArgumentException($"page de {text.Length} octets, au-delà d'une trame", nameof(json));

        var frame = new byte[5 + text.Length];
        frame[0] = RendezvousKind.BanListData;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(1), (ushort)page);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3), (ushort)pages);
        text.CopyTo(frame.AsSpan(5));
        return frame;
    }

    public static bool TryReadBanListData(
        ReadOnlySpan<byte> frame, out int page, out int pages, out string json, out string? rejection)
    {
        page = 0;
        pages = 0;
        json = string.Empty;

        if (frame.Length < 6 || frame[0] != RendezvousKind.BanListData)
        {
            rejection = "page de liste malformée";
            return false;
        }

        page = BinaryPrimitives.ReadUInt16BigEndian(frame[1..]);
        pages = BinaryPrimitives.ReadUInt16BigEndian(frame[3..]);

        if (pages is < 1 or > MaxBanListPages || page >= pages)
        {
            rejection = $"page {page} sur {pages}, hors bornes (plafond {MaxBanListPages})";
            return false;
        }

        json = System.Text.Encoding.UTF8.GetString(frame[5..]);
        rejection = null;
        return true;
    }
```

- [ ] **Step 4: Écrire la fusion**

`Linkpearl/Core/Safety/BanListPages.cs` :

```csharp
using System.Security.Cryptography;

namespace Linkpearl.Core.Safety;

/// <summary>
/// Recompose une liste de bannissement servie par pages.
/// </summary>
/// <remarks>
/// Dans le plugin seulement : le service découpe, il n'a jamais à recoller.
/// Chaque page est une liste entière sous le même sel ; des pages sous deux
/// sels viennent de deux listes différentes, dont les empreintes ne se
/// comparent pas, et les mêler ferait une liste où la moitié ne bannit
/// personne. La liste peut changer entre deux pages : une entrée qui glisse
/// arrive deux fois, et ne compte qu'une.
/// </remarks>
public static class BanListPages
{
    public static bool TryMerge(IReadOnlyList<BanList> pages, out BanList? merged, out string? rejection)
    {
        merged = null;

        if (pages.Count == 0)
        {
            rejection = "aucune page";
            return false;
        }

        var first = pages[0];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<BanEntry>();

        foreach (var page in pages)
        {
            if (CryptographicOperations.FixedTimeEquals(page.Salt, first.Salt) is false)
            {
                rejection = "pages sous des sels différents";
                return false;
            }

            if (page.Parameters != first.Parameters)
            {
                rejection = "pages sous des coûts de dérivation différents";
                return false;
            }

            foreach (var entry in page.Entries)
                if (seen.Add(Convert.ToHexStringLower(entry.Hash)))
                    entries.Add(entry);
        }

        if (entries.Count > BanList.MaxEntries)
        {
            rejection = $"liste trop longue (plafond {BanList.MaxEntries})";
            return false;
        }

        merged = new BanList(first.Salt, first.Parameters, entries);
        rejection = null;
        return true;
    }
}
```

- [ ] **Step 5: Écrire la demande côté client**

Dans `RendezvousClient`, après `QueryDirectoryAsync`, et ajouter `using Linkpearl.Core.Safety;` en tête :

```csharp
    /// <summary>
    /// Télécharge la liste de bannissement de ce service, toutes pages.
    /// </summary>
    /// <remarks>
    /// Sur une connexion à part, qui n'écoute pas de boîte : la boucle de
    /// présence lit déjà la sienne, et deux lecteurs sur un flux se volent les
    /// trames. Un service d'avant ces trames répond « trame inattendue » :
    /// l'échec est rendu, jamais levé, et l'appelant garde ce qu'il avait.
    /// </remarks>
    public async Task<(BanList? List, string? Failure)> QueryBanListAsync(CancellationToken ct)
    {
        var pages = new List<BanList>();
        var total = 1;

        for (var page = 0; page < total; page++)
        {
            await SendAsync(RendezvousWire.BanListQuery(page), ct).ConfigureAwait(false);

            var frame = await ReadFrameAsync(ct).ConfigureAwait(false);

            if (frame is null)
                return (null, "connexion fermée par le service");

            if (frame[0] == RendezvousKind.Error)
                return (null, System.Text.Encoding.UTF8.GetString(frame.AsSpan(1)));

            if (RendezvousWire.TryReadBanListData(frame, out var index, out var count, out var json, out var why) is false)
                return (null, why);

            if (index != page || (page > 0 && count != total))
                return (null, "pages incohérentes");

            total = count;

            if (BanList.TryParse(json, out var list, out why) is false)
                return (null, why);

            pages.Add(list!);
        }

        return BanListPages.TryMerge(pages, out var merged, out var rejection) ? (merged, null) : (null, rejection);
    }
```

- [ ] **Step 6: Mettre à jour les vecteurs**

Dans `Linkpearl.Core.Tests/Fixtures/rendezvous-vectors.json` : ajouter à `constantes` `"entreesParPageBannissement": 64, "pagesBannissementMax": 64`, et à la fin de `trames` :

```json
    { "nom": "bannissement-demande", "hex": "150001" },
    { "nom": "bannissement-page", "hex": "16000000027b2276657273696f6e223a317d" }
```

Respecter l'indentation et l'ordre des clés du fichier existant (le lire d'abord).

- [ ] **Step 7: Lancer les tests**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: PASS, y compris `RendezvousVectorTests` et `ArchitectureTests`.

- [ ] **Step 8: Commit**

```bash
git add Linkpearl/Core/Transport/Rendezvous/RendezvousWire.cs Linkpearl/Core/Transport/Rendezvous/RendezvousClient.cs \
  Linkpearl/Core/Safety/BanListPages.cs Linkpearl.Core.Tests/Rendezvous/BanListWireTests.cs \
  Linkpearl.Core.Tests/Safety/BanListPagesTests.cs Linkpearl.Core.Tests/Fixtures/rendezvous-vectors.json \
  Linkpearl.Core.Tests/Rendezvous/RendezvousVectorTests.cs
git commit -m "feat(transport): trames paginées de la liste de bannissement"
```

---

### Task 2: Le service sert sa liste (dépôt du service)

Dans `~/Projects/linkpearl-sync/rendezvous`. Le service a déjà `MaxMailboxesPerSession = 64` (commit `fb01a43`, pas encore poussé : il part avec cette tâche). Le fichier `bans.json` non suivi à la racine ne doit **pas** être ajouté.

**Files:**
- Copy: `RendezvousWire.cs`, `rendezvous-vectors.json`, `RendezvousVectorTests.cs` depuis le plugin, vers leurs copies existantes.
- Modify: `Linkpearl.Rendezvous/BanStore.cs`, `Linkpearl.Rendezvous/RendezvousServer.cs`, `Linkpearl.Rendezvous/PeerSession.cs`, `Linkpearl.Rendezvous/Program.cs`
- Modify: `Linkpearl.Rendezvous.Tests/ServerHarness.cs`
- Test: `Linkpearl.Rendezvous.Tests/BanListServiceTests.cs`

**Interfaces:**
- Consumes: les trames de la tâche 1.
- Produces: `BanStore.Page(int index, int size) : (string? Json, int Pages)` ; `RendezvousServer.Bans { get; init; } : BanStore?` ; `PeerSession.BanPagesServed : int` ; `ServerHarness.Bans : BanStore`.

- [ ] **Step 1: Recopier les fichiers partagés et vérifier**

```bash
P=~/Projects/linkpearl-sync/plugin/.claude/worktrees/groupes-noyau
R=~/Projects/linkpearl-sync/rendezvous
for f in RendezvousWire.cs rendezvous-vectors.json RendezvousVectorTests.cs; do
  src=$(find $P -name $f -not -path '*/bin/*' -not -path '*/obj/*')
  dst=$(find $R -name $f -not -path '*/bin/*' -not -path '*/obj/*')
  cp "$src" "$dst" && diff "$src" "$dst" && echo "$f identique"
done
```

Expected: trois lignes « identique ».

- [ ] **Step 2: Écrire les tests qui échouent**

Dans `ServerHarness`, ajouter la propriété et la créer dans `StartAsync` avant le serveur :

```csharp
    public BanStore Bans { get; private set; } = null!;
```

```csharp
        harness.Bans = new BanStore(Path.Combine(harness._dir, "bans.json"));

        harness.Server = new RendezvousServer(
            0, harness.Directory, limits ?? RendezvousLimits.Default, harness.Clock, verbose)
        {
            Log = harness._log,
            Bans = harness.Bans,
        };
```

`Linkpearl.Rendezvous.Tests/BanListServiceTests.cs` :

```csharp
using Linkpearl.Core.Safety;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Rendezvous.Tests;

public sealed class BanListServiceTests
{
    private static async Task<(int Pages, BanList List)> PageAsync(TestClient client, int page)
    {
        await client.SendAsync(RendezvousWire.BanListQuery(page));
        var frame = await client.ReadFrameAsync();

        Assert.NotNull(frame);
        Assert.True(RendezvousWire.TryReadBanListData(frame, out var index, out var pages, out var json, out var why), why);
        Assert.Equal(page, index);
        Assert.True(BanList.TryParse(json, out var list, out why), why);
        return (pages, list!);
    }

    [Fact]
    public async Task Une_liste_vide_tient_en_une_page_et_porte_son_sel()
    {
        await using var harness = await ServerHarness.StartAsync();
        using var client = await harness.ConnectAsync();

        var (pages, list) = await PageAsync(client, 0);

        Assert.Equal(1, pages);
        Assert.Empty(list.Entries);
        Assert.Equal(harness.Bans.Current().Salt, list.Salt);
    }

    [Fact]
    public async Task Une_liste_de_70_entrees_tient_en_deux_pages()
    {
        await using var harness = await ServerHarness.StartAsync();

        // Import plutôt qu'Add : Add dérive en PBKDF2 à 600 000 itérations, et
        // soixante-dix dérivations rendraient le test lent pour rien.
        var current = harness.Bans.Current();
        var entries = Enumerable.Range(0, 70)
            .Select(i => new BanEntry(Enumerable.Repeat((byte)i, 32).ToArray(), $"motif {i}", i))
            .ToList();
        Assert.True(harness.Bans.Import(new BanList(current.Salt, current.Parameters, entries).ToJson(0)).Ok);

        using var client = await harness.ConnectAsync();

        var (pages, first) = await PageAsync(client, 0);
        var (_, second) = await PageAsync(client, 1);

        Assert.Equal(2, pages);
        Assert.Equal(RendezvousWire.BanListPageEntries, first.Entries.Count);
        Assert.Equal(70 - RendezvousWire.BanListPageEntries, second.Entries.Count);
    }

    [Fact]
    public async Task Une_page_hors_de_la_liste_recoit_une_erreur_et_la_connexion_reste()
    {
        await using var harness = await ServerHarness.StartAsync();
        using var client = await harness.ConnectAsync();

        await client.SendAsync(RendezvousWire.BanListQuery(5));
        var frame = await client.ReadFrameAsync();

        Assert.Equal(RendezvousKind.Error, frame![0]);

        // Toujours là : une page de trop n'est pas une faute du client.
        var (pages, _) = await PageAsync(client, 0);
        Assert.Equal(1, pages);
    }

    [Fact]
    public async Task Au_dela_du_plafond_de_pages_la_connexion_est_fermee()
    {
        await using var harness = await ServerHarness.StartAsync();
        using var client = await harness.ConnectAsync();

        for (var i = 0; i < RendezvousWire.MaxBanListPages; i++)
            await PageAsync(client, 0);

        await client.SendAsync(RendezvousWire.BanListQuery(0));

        Assert.True(await client.IsClosedAsync(TimeSpan.FromSeconds(5)));
    }
}
```

Run: `dotnet test --filter FullyQualifiedName~BanListServiceTests` (racine du service)
Expected: échec de compilation (`Bans` inconnu sur `RendezvousServer`).

- [ ] **Step 3: Découper la liste**

Dans `BanStore`, après `Json()` :

```csharp
    /// <summary>
    /// Une page de la liste, et le nombre de pages.
    /// </summary>
    /// <remarks>
    /// Chaque page est une liste entière sous le même sel, pour que le client
    /// la lise avec le même code qu'une liste complète. Hors de la liste, le
    /// JSON est nul et le nombre de pages dit jusqu'où aller.
    /// </remarks>
    public (string? Json, int Pages) Page(int index, int size)
    {
        lock (_gate)
        {
            var list = LoadLocked();
            var pages = Math.Max(1, (list.Entries.Count + size - 1) / size);

            if (index < 0 || index >= pages)
                return (null, pages);

            var slice = list.Entries.Skip(index * size).Take(size).ToList();
            return (new BanList(list.Salt, list.Parameters, slice).ToJson(UpdatedLocked()), pages);
        }
    }
```

- [ ] **Step 4: Servir les pages**

Dans `PeerSession` :

```csharp
    /// <summary>Pages de liste de bannissement servies sur cette connexion.</summary>
    /// <remarks>
    /// Trois octets de demande pour jusqu'à 64 Kio de réponse : sans plafond,
    /// une connexion ferait du service un amplificateur. Une liste entière se
    /// lit en 64 pages au plus.
    /// </remarks>
    public int BanPagesServed { get; set; }
```

Dans `RendezvousServer`, la propriété à côté de `Log` :

```csharp
    /// <summary>La liste que le service publie ; nulle, il répond qu'il n'en a pas.</summary>
    public BanStore? Bans { get; init; }
```

Dans le `switch` de la boucle de trames :

```csharp
                    RendezvousKind.BanListQuery => await HandleBanListQueryAsync(session, frame, ct).ConfigureAwait(false),
```

Et la méthode, après `HandleDirectorySubmit` :

```csharp
    /// <summary>
    /// Sert une page de la liste de bannissement.
    /// </summary>
    /// <remarks>
    /// La même que <c>GET /api/bans</c>, découpée : la console n'écoute qu'en
    /// local, et c'est ici que les clients la trouvent. Une page hors de la
    /// liste n'est pas une faute : la liste a pu raccourcir entre deux pages.
    /// </remarks>
    private async Task<bool> HandleBanListQueryAsync(PeerSession session, byte[] frame, CancellationToken ct)
    {
        if (RendezvousWire.TryReadBanListQuery(frame, out var page) is false)
        {
            await session.SendAsync(RendezvousWire.Error("demande de liste malformée"), ct).ConfigureAwait(false);
            return false;
        }

        if (++session.BanPagesServed > RendezvousWire.MaxBanListPages)
        {
            await session.SendAsync(RendezvousWire.Error("trop de pages demandées"), ct).ConfigureAwait(false);
            return false;
        }

        if (Bans is null)
        {
            await session.SendAsync(RendezvousWire.Error("liste de bannissement indisponible"), ct).ConfigureAwait(false);
            return true;
        }

        var (json, pages) = Bans.Page(page, RendezvousWire.BanListPageEntries);

        await session.SendAsync(
            json is null ? RendezvousWire.Error("page hors de la liste") : RendezvousWire.BanListData(page, pages, json),
            ct).ConfigureAwait(false);

        return true;
    }
```

Note : `handled is false` envoie « trame inattendue » puis ferme ; les deux `return false` ci-dessus ont déjà envoyé leur erreur, comme le fait `HandleAnnounceAsync`.

- [ ] **Step 5: Brancher dans `Program.cs`**

Créer `bans` avant `service` et le passer :

```csharp
var bans = new BanStore(ArgString("--bans", "bans.json"));
var service = new RendezvousServer(port, directory, limits, clock, verbose: args.Contains("--verbose")) { Bans = bans };
```

- [ ] **Step 6: Lancer tous les tests du service**

Run: `dotnet test` (racine du service)
Expected: PASS, dont `RendezvousVectorTests` avec les deux nouvelles trames.

- [ ] **Step 7: Commit et poussée**

```bash
cd ~/Projects/linkpearl-sync/rendezvous
git add Linkpearl.Rendezvous Linkpearl.Rendezvous.Tests
git status --short   # bans.json à la racine ne doit PAS apparaître en ajouté
git commit -m "feat(bans): servir la liste de bannissement aux clients, par pages"
git push origin main
```

Le déploiement sur `rdv.linkpearl.eorzea.events` (`deploy/deploy.sh`) se fait **après accord explicite de l'utilisateur** : c'est le service de production.

---

### Task 3: Le livre des bannissements (noyau)

**Files:**
- Create: `Linkpearl/Core/Safety/ServiceBanBook.cs`
- Test: `Linkpearl.Core.Tests/Safety/ServiceBanBookTests.cs`

**Interfaces:**
- Consumes: `BanList`, `BanParameters`, `PlayerFingerprint`, `RendezvousAddress`, `IClock`.
- Produces:
  - `sealed record ServiceBan(RendezvousAddress Service, string Reason)`
  - `enum BanVerdict { Clear, Pending, Listed }` (local)
  - `readonly record struct ServiceBanStatus(BanVerdict Verdict, ServiceBan? Ban)` avec `static Clear`, `static Pending`
  - `sealed record BanDerivation(string Key, byte[] Salt, BanParameters Parameters)`
  - `interface IServiceBans { ServiceBanStatus Status(PlayerFingerprint player); }`
  - `sealed class ServiceBanBook(IClock clock) : IServiceBans` : `SetList`, `Retain`, `Missing`, `Record`, `Status`, `Screen`, `static KeyOf`, `event Action? Changed`, constantes `MaxRememberedDerivations = 4096`, `MaxOnDemandPerMinute = 20`

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Tests.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Safety;

public sealed class ServiceBanBookTests
{
    private static readonly BanParameters Cheap = new(1000);
    private static readonly RendezvousAddress One = new("rdv.un.ch", 47900);
    private static readonly RendezvousAddress Two = new("rdv.deux.ch", 47900);
    private static readonly byte[] SaltOne = Enumerable.Repeat((byte)1, 32).ToArray();
    private static readonly byte[] SaltTwo = Enumerable.Repeat((byte)2, 32).ToArray();
    private static readonly PlayerFingerprint Mallory = PlayerFingerprint.Of("mallory", 21);
    private static readonly PlayerFingerprint Alice = PlayerFingerprint.Of("alice", 21);

    private readonly MovableClock _clock = new();

    private static BanList Listing(byte[] salt, params (string Name, string Reason)[] banned)
        => new(salt, Cheap, [.. banned.Select(entry => new BanEntry(BanList.Derive(entry.Name, 21, salt, Cheap), entry.Reason, 0))]);

    /// <summary>Ce que l'adaptateur fait, avec le nom qu'il est seul à connaître.</summary>
    private static void DeriveAll(ServiceBanBook book, PlayerFingerprint player, string name)
    {
        foreach (var missing in book.Missing(player))
            book.Record(player, missing.Key, BanList.Derive(name, 21, missing.Salt, missing.Parameters));
    }

    [Fact]
    public void Sans_liste_tout_le_monde_est_libre()
        => Assert.Equal(BanVerdict.Clear, new ServiceBanBook(_clock).Status(Mallory).Verdict);

    [Fact]
    public void Une_liste_vide_ne_demande_aucune_derivation()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, new BanList(SaltOne, Cheap, []));

        Assert.Empty(book.Missing(Mallory));
        Assert.Equal(BanVerdict.Clear, book.Status(Mallory).Verdict);
    }

    [Fact]
    public void Avant_la_derivation_le_verdict_attend()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("mallory", "triche")));

        Assert.Equal(BanVerdict.Pending, book.Status(Alice).Verdict);
    }

    [Fact]
    public void Apres_la_derivation_le_liste_est_vu_avec_son_service_et_son_motif()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("mallory", "triche")));

        DeriveAll(book, Mallory, "mallory");
        DeriveAll(book, Alice, "alice");

        Assert.Equal(new ServiceBanStatus(BanVerdict.Listed, new ServiceBan(One, "triche")), book.Status(Mallory));
        Assert.Equal(BanVerdict.Clear, book.Status(Alice).Verdict);
    }

    [Fact]
    public void Un_seul_service_qui_liste_suffit()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("alice", "autre")));
        book.SetList(Two, Listing(SaltTwo, ("mallory", "triche")));

        DeriveAll(book, Mallory, "mallory");

        Assert.Equal(Two, book.Status(Mallory).Ban!.Service);
    }

    [Fact]
    public void Une_liste_mise_a_jour_reutilise_les_derivations()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("alice", "autre")));
        DeriveAll(book, Mallory, "mallory");

        book.SetList(One, Listing(SaltOne, ("alice", "autre"), ("mallory", "triche")));

        Assert.Empty(book.Missing(Mallory));
        Assert.Equal(BanVerdict.Listed, book.Status(Mallory).Verdict);
    }

    [Fact]
    public void Retirer_un_service_leve_son_bannissement()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("mallory", "triche")));
        DeriveAll(book, Mallory, "mallory");

        book.Retain([Two]);

        Assert.Equal(BanVerdict.Clear, book.Status(Mallory).Verdict);
    }

    [Fact]
    public void Screen_derive_ce_qui_manque_et_rend_le_verdict()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("mallory", "triche")));

        var status = book.Screen(Mallory, (salt, parameters) => BanList.Derive("mallory", 21, salt, parameters));

        Assert.Equal(BanVerdict.Listed, status.Verdict);
    }

    [Fact]
    public void Screen_respecte_son_budget_par_minute()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("mallory", "triche")));

        for (var i = 0; i < ServiceBanBook.MaxOnDemandPerMinute; i++)
        {
            var name = $"forge{i}";
            Assert.NotEqual(BanVerdict.Pending, book.Screen(
                PlayerFingerprint.Of(name, 21), (salt, parameters) => BanList.Derive(name, 21, salt, parameters)).Verdict);
        }

        Assert.Equal(BanVerdict.Pending, book.Screen(Mallory, (salt, parameters) => BanList.Derive("mallory", 21, salt, parameters)).Verdict);

        _clock.Advance(TimeSpan.FromMinutes(1.1));

        Assert.Equal(BanVerdict.Listed, book.Screen(Mallory, (salt, parameters) => BanList.Derive("mallory", 21, salt, parameters)).Verdict);
    }

    [Fact]
    public void Screen_ne_compte_pas_ce_qui_est_deja_derive()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("mallory", "triche")));
        DeriveAll(book, Mallory, "mallory");

        for (var i = 0; i < ServiceBanBook.MaxOnDemandPerMinute * 2; i++)
            Assert.Equal(BanVerdict.Listed, book.Screen(Mallory, (_, _) => throw new InvalidOperationException("déjà dérivé")).Verdict);
    }
}
```

`MovableClock` existe déjà dans les tests (`grep -rn "class MovableClock" Linkpearl.Core.Tests`) ; ajuster le `using` si son espace de noms diffère.

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter FullyQualifiedName~ServiceBanBook`
Expected: échec de compilation.

- [ ] **Step 2: Écrire le livre**

`Linkpearl/Core/Safety/ServiceBanBook.cs` :

```csharp
using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Safety;

/// <summary>Qui a listé ce personnage, et pourquoi.</summary>
/// <remarks>Le motif vient du service : l'interface le normalise avant de l'afficher.</remarks>
public sealed record ServiceBan(RendezvousAddress Service, string Reason);

/// <summary>Le verdict des services sur un personnage. Strictement local.</summary>
public enum BanVerdict
{
    Clear,

    /// <summary>Une liste le concerne, et sa dérivation n'est pas encore faite.</summary>
    Pending,

    Listed,
}

public readonly record struct ServiceBanStatus(BanVerdict Verdict, ServiceBan? Ban)
{
    public static ServiceBanStatus Clear { get; } = new(BanVerdict.Clear, null);

    public static ServiceBanStatus Pending { get; } = new(BanVerdict.Pending, null);
}

/// <summary>Une dérivation à faire pour un personnage : le sel et le coût d'une liste.</summary>
public sealed record BanDerivation(string Key, byte[] Salt, BanParameters Parameters);

/// <summary>Ce que le planificateur, le moteur et l'interface demandent.</summary>
public interface IServiceBans
{
    ServiceBanStatus Status(PlayerFingerprint player);
}

/// <summary>
/// Les listes de bannissement des services actifs, appliquées par empreinte.
/// </summary>
/// <remarks>
/// Le noyau ne voit jamais le nom : c'est l'adaptateur qui dérive, à partir du
/// nom qu'il est seul à tenir, et qui rend le haché. On garde ce haché par
/// personnage et par sel, pas par liste : une liste qui s'allonge sous le même
/// sel ne coûte alors aucune dérivation de plus, et un nouveau banni se voit à
/// la seconde où la liste arrive.
///
/// Une liste vide ne demande rien : c'est le cas de presque tous les services,
/// et dériver pour elle coûterait des centaines de millisecondes par joueur
/// visible pour ne rien trouver.
///
/// Appelé depuis le fil de rafraîchissement, le tic du moteur, les remises de
/// boîte et l'interface : tout passe sous un verrou, et les dérivations, qui
/// sont longues, se font hors de lui.
/// </remarks>
public sealed class ServiceBanBook(IClock clock) : IServiceBans
{
    /// <summary>Au-delà, on oublie toutes les dérivations et on recommence.</summary>
    /// <remarks>
    /// Un lieu bondé compte une centaine de joueurs, fois deux ou trois sels :
    /// 4 096 laisse des heures de passage avant d'oublier. Tout oublier d'un
    /// coup est plus simple qu'un ordre d'ancienneté, et ne coûte que des
    /// dérivations refaites en arrière-plan.
    /// </remarks>
    public const int MaxRememberedDerivations = 4096;

    /// <summary>Dérivations à la demande permises par minute glissante.</summary>
    /// <remarks>
    /// Une demande de pairage ou d'admission vient d'un inconnu, qui peut en
    /// forger sous autant de noms qu'il veut : chacune coûterait une dérivation
    /// PBKDF2 complète. Vingt par minute couvrent largement les demandes
    /// honnêtes ; au-delà, le verdict reste en attente et la demande est ignorée.
    /// </remarks>
    public const int MaxOnDemandPerMinute = 20;

    private readonly Lock _gate = new();
    private readonly Dictionary<RendezvousAddress, BanList> _lists = [];
    private readonly Dictionary<(PlayerFingerprint Player, string Key), byte[]> _derived = [];
    private readonly Queue<DateTimeOffset> _onDemand = new();

    /// <summary>Levé hors du verrou quand une liste ou un verdict a pu changer.</summary>
    public event Action? Changed;

    public static string KeyOf(byte[] salt, BanParameters parameters)
        => $"{Convert.ToHexStringLower(salt)}:{parameters.Iterations}";

    public void SetList(RendezvousAddress service, BanList list)
    {
        lock (_gate)
            _lists[service] = list;

        Changed?.Invoke();
    }

    /// <summary>Oublie les listes des services qui ne sont plus actifs.</summary>
    public void Retain(IReadOnlyCollection<RendezvousAddress> services)
    {
        var removed = false;

        lock (_gate)
        {
            foreach (var service in _lists.Keys.Where(service => services.Contains(service) is false).ToList())
                removed |= _lists.Remove(service);
        }

        if (removed)
            Changed?.Invoke();
    }

    /// <summary>Les dérivations qui manquent pour trancher sur ce personnage.</summary>
    public IReadOnlyList<BanDerivation> Missing(PlayerFingerprint player)
    {
        lock (_gate)
        {
            return [.. _lists.Values
                .Where(list => list.Entries.Count > 0)
                .Select(list => new BanDerivation(KeyOf(list.Salt, list.Parameters), list.Salt, list.Parameters))
                .DistinctBy(derivation => derivation.Key)
                .Where(derivation => _derived.ContainsKey((player, derivation.Key)) is false)];
        }
    }

    public void Record(PlayerFingerprint player, string key, byte[] derived)
    {
        lock (_gate)
        {
            if (_derived.Count >= MaxRememberedDerivations)
                _derived.Clear();

            _derived[(player, key)] = derived;
        }

        Changed?.Invoke();
    }

    public ServiceBanStatus Status(PlayerFingerprint player)
    {
        lock (_gate)
        {
            var pending = false;

            // Dans un ordre fixe : deux services qui listent le même joueur
            // doivent donner le même motif d'une image à l'autre.
            foreach (var (service, list) in _lists.OrderBy(entry => entry.Key.ToString(), StringComparer.Ordinal))
            {
                if (list.Entries.Count == 0)
                    continue;

                if (_derived.TryGetValue((player, KeyOf(list.Salt, list.Parameters)), out var hash) is false)
                {
                    pending = true;
                    continue;
                }

                foreach (var entry in list.Entries)
                {
                    // À temps constant, comme BanList.Contains.
                    if (CryptographicOperations.FixedTimeEquals(entry.Hash, hash))
                        return new ServiceBanStatus(BanVerdict.Listed, new ServiceBan(service, entry.Reason));
                }
            }

            return pending ? ServiceBanStatus.Pending : ServiceBanStatus.Clear;
        }
    }

    /// <summary>
    /// Tranche tout de suite, en dérivant ce qui manque, dans la limite du budget.
    /// </summary>
    /// <remarks>
    /// Pour une demande venue du réseau, qu'on ne peut pas laisser en attente
    /// d'une ronde de détection. La dérivation se fait sur le fil de l'appelant
    /// et hors du verrou : c'est lui qui a choisi d'attendre.
    /// </remarks>
    public ServiceBanStatus Screen(PlayerFingerprint player, Func<byte[], BanParameters, byte[]> derive)
    {
        foreach (var missing in Missing(player))
        {
            lock (_gate)
            {
                var now = clock.UtcNow;

                while (_onDemand.Count > 0 && now - _onDemand.Peek() >= TimeSpan.FromMinutes(1))
                    _onDemand.Dequeue();

                if (_onDemand.Count >= MaxOnDemandPerMinute)
                    return ServiceBanStatus.Pending;

                _onDemand.Enqueue(now);
            }

            Record(player, missing.Key, derive(missing.Salt, missing.Parameters));
        }

        return Status(player);
    }
}
```

Le budget compte les dérivations, pas les joueurs : avec une liste, un joueur coûte une dérivation.

- [ ] **Step 3: Lancer les tests**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter FullyQualifiedName~ServiceBanBook`, puis la suite complète.
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add Linkpearl/Core/Safety/ServiceBanBook.cs Linkpearl.Core.Tests/Safety/ServiceBanBookTests.cs
git commit -m "feat(safety): appliquer les listes des services par empreinte de personnage"
```

---

### Task 4: Le groupe Public, ses données et leur forme sur disque

**Files:**
- Create: `Linkpearl/Core/Groups/PublicGroup.cs`
- Modify: `Linkpearl/Core/Groups/GroupRecord.cs`
- Modify: `Linkpearl/Core/Groups/GroupBookCodec.cs`
- Modify: les appelants de `GroupMember.Receive` (`GroupDialPlanner.cs:106`, `Ui/Pages/GroupsPage.cs:658`, les tests qui le lisent)
- Test: `Linkpearl.Core.Tests/Groups/PublicGroupTests.cs` (créé ici, complété en tâche 5), `Linkpearl.Core.Tests/Groups/GroupBookCodecTests.cs`

**Interfaces:**
- Consumes: `GroupId.Of`, `GroupBan`, `TransientCategories`, `RendezvousAddress`.
- Produces:
  - `static class PublicGroup { const string Name = "Public"; static byte[] Secret { get; } ; static GroupId Id { get; } ; static bool Is(GroupId) ; static GroupRecord Create(IReadOnlyList<RendezvousAddress> services, DateTimeOffset now) }`
  - `GroupRecord.Dormant : bool`, `GroupRecord.Blocked : IReadOnlyList<GroupBan>`, `GroupRecord.DefaultReceive : TransientCategories`, `GroupRecord.IsPublic : bool`, `GroupRecord.Refuses(PeerId?, PlayerFingerprint?) : bool`, `GroupRecord.ReceiveOf(GroupMember?) : TransientCategories`
  - `GroupMember.Receive : TransientCategories?` (null : suit `DefaultReceive`)
  - `PublicGroupTests.Service` (`internal static`), réutilisé par les tâches 5 et 6

- [ ] **Step 1: Écrire les tests qui échouent**

`Linkpearl.Core.Tests/Groups/PublicGroupTests.cs` :

```csharp
using Linkpearl.Core.Groups;
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
```

Dans `GroupBookCodecTests.cs`, ajouter :

```csharp
    [Fact]
    public void Public_dormant_se_relit_avec_ses_blocages_et_ses_reglages()
    {
        var bob = PlayerFingerprint.Of("bob", 21);
        var alice = PlayerFingerprint.Of("alice", 21);
        var group = PublicGroup.Create([], DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)) with
        {
            Dormant = true,
            Blocked = [new GroupBan(null, bob)],
            DefaultReceive = new TransientCategories(true, false, false),
            Members = new Dictionary<PlayerFingerprint, GroupMember>
            {
                [alice] = new() { Fingerprint = alice, DisplayName = "Alice", Receive = TransientCategories.All },
                [bob] = new() { Fingerprint = bob, DisplayName = "Bob" },
            },
        };

        var back = Assert.Single(GroupBookCodec.Decode(GroupBookCodec.Encode([group])));

        Assert.True(back.IsPublic);
        Assert.True(back.Dormant);
        Assert.Empty(back.Rendezvous);    // Public vit sans service enregistré : ils viennent de la configuration
        Assert.Equal(group.Blocked, back.Blocked);
        Assert.Equal(group.DefaultReceive, back.DefaultReceive);
        Assert.Equal(TransientCategories.All, back.Members[alice].Receive);
        Assert.Null(back.Members[bob].Receive);
    }

    [Fact]
    public void Un_faux_Public_est_rejete()
    {
        // L'identifiant du Public avec un autre secret : un fichier altéré qui
        // ferait composer le Public sous des boîtes que personne d'autre n'ouvre.
        var forged = PublicGroup.Create([], DateTimeOffset.UnixEpoch) with { Secret = new byte[32] };

        Assert.Empty(GroupBookCodec.Decode(GroupBookCodec.Encode([forged])));
    }

    [Fact]
    public void Un_groupe_prive_ne_peut_pas_etre_dormant()
    {
        var group = GroupBookTests.Group([.. Enumerable.Range(0, 32).Select(i => (byte)i)], DateTimeOffset.UnixEpoch) with { Dormant = true };

        Assert.False(Assert.Single(GroupBookCodec.Decode(GroupBookCodec.Encode([group]))).Dormant);
    }

    [Fact]
    public void Public_ne_compte_pas_dans_les_dix_groupes()
    {
        var groups = Enumerable.Range(0, GroupBook.MaxGroups)
            .Select(i => GroupBookTests.Group([.. Enumerable.Range(0, 32).Select(b => (byte)(b + i))], DateTimeOffset.UnixEpoch))
            .Append(PublicGroup.Create([], DateTimeOffset.UnixEpoch))
            .ToList();

        Assert.Equal(GroupBook.MaxGroups + 1, GroupBookCodec.Decode(GroupBookCodec.Encode(groups)).Count);
    }
```

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter "FullyQualifiedName~PublicGroup|FullyQualifiedName~GroupBookCodec"`
Expected: échec de compilation.

- [ ] **Step 2: Écrire `PublicGroup`**

```csharp
using System.Security.Cryptography;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>
/// Le groupe que tout le monde peut activer, sans code ni admission.
/// </summary>
/// <remarks>
/// Son secret est une constante : quiconque l'active voit tout joueur visible
/// qui l'a activé aussi, et le service, qui la connaît, peut en apparier tous
/// les membres. Il ne lit pas davantage le contenu, qui passe dans la session
/// SIGMA-I. Pas de clé de groupe, donc pas de politique : la modération est
/// le blocage local et les listes de bannissement des services.
/// </remarks>
public static class PublicGroup
{
    public const string Name = "Public";

    private static readonly byte[] SecretBytes = SHA256.HashData("linkpearl:public:v1"u8);

    /// <summary>Une copie : un appelant qui écrirait dedans ne doit rien changer aux autres.</summary>
    public static byte[] Secret => [.. SecretBytes];

    public static GroupId Id { get; } = GroupId.Of(SecretBytes);

    public static bool Is(GroupId id) => id == Id;

    /// <summary>
    /// Le Public tel qu'on l'active la première fois.
    /// </summary>
    /// <remarks>
    /// Animations, VFX et sons coupés : ce sont des inconnus, et une danse ou
    /// des particules imposées par un passant sont ce qu'on veut le moins voir
    /// arriver sans l'avoir choisi.
    /// </remarks>
    public static GroupRecord Create(IReadOnlyList<RendezvousAddress> services, DateTimeOffset now) => new()
    {
        Id = Id,
        Name = Name,
        Secret = Secret,
        Rendezvous = services,
        JoinedAt = now,
        DefaultReceive = TransientCategories.None,
    };
}
```

- [ ] **Step 3: Étendre `GroupRecord` et `GroupMember`**

Dans `GroupMember`, remplacer `Receive` :

```csharp
    /// <summary>
    /// Les animations, VFX et sons acceptés de ce membre, ou null pour suivre le groupe.
    /// </summary>
    /// <remarks>
    /// Null tant qu'on n'a rien réglé pour lui : il suit alors
    /// <see cref="GroupRecord.DefaultReceive"/>, et « tout réactiver pour le
    /// Public » le libère avec les autres. Un réglage posé d'un clic l'emporte.
    /// </remarks>
    public TransientCategories? Receive { get; init; }
```

Dans `GroupRecord`, après `Policy` :

```csharp
    /// <summary>
    /// Vrai pour un Public désactivé, gardé pour ses blocages et ses réglages.
    /// </summary>
    /// <remarks>
    /// Il n'ouvre aucune boîte et ne compose personne. Le retirer du carnet
    /// ferait perdre, à chaque désactivation, les joueurs qu'on avait bloqués.
    /// Toujours faux pour un groupe privé.
    /// </remarks>
    public bool Dormant { get; init; }

    /// <summary>
    /// Les joueurs qu'on a bloqués dans ce groupe. Local, jamais transmis.
    /// </summary>
    /// <remarks>Le Public n'a pas de politique : c'est sa seule modération avec les listes des services.</remarks>
    public IReadOnlyList<GroupBan> Blocked { get; init; } = [];

    /// <summary>Ce qu'on accepte d'un membre qu'on n'a pas réglé un par un.</summary>
    public TransientCategories DefaultReceive { get; init; } = TransientCategories.All;

    public bool IsPublic => PublicGroup.Is(Id);

    /// <summary>Vrai si la politique bannit ce membre ou si on l'a bloqué.</summary>
    public bool Refuses(PeerId? key, PlayerFingerprint? member)
        => Policy?.IsBanned(key, member) is true || Blocked.Any(ban => ban.Matches(key, member));

    public TransientCategories ReceiveOf(GroupMember? member) => member?.Receive ?? DefaultReceive;
```

- [ ] **Step 4: Adapter le codec**

Dans `GroupBookCodec` :

```csharp
    private sealed record BanDto(string? Peer, string? Fingerprint);

    private sealed record GroupDto(
        string Id, string Name, string Secret, string[] Rendezvous, long JoinedAt, MemberDto[] Members,
        string? OwnerKey = null, string? SigningKey = null, string? Policy = null,
        bool Dormant = false, BanDto[]? Blocked = null, int? DefaultReceive = null);

    /// <summary>Un membre sans réglage propre, qui suit le groupe.</summary>
    private const int ReceiveFollowsGroup = -1;

    /// <summary>Même borne que les bannis d'une politique.</summary>
    private const int MaxBlocked = 256;
```

À l'encodage, `ToBits(member.Receive)` devient `member.Receive is { } receive ? ToBits(receive) : ReceiveFollowsGroup`, et le `GroupDto` reçoit en plus :

```csharp
            group.IsPublic && group.Dormant,
            [.. group.Blocked.Select(ban => new BanDto(
                ban.Peer is { } peer ? Convert.ToHexStringLower(peer.ToBytes()) : null,
                ban.Fingerprint is { } print ? Convert.ToHexStringLower(print.ToBytes()) : null))],
            ToBits(group.DefaultReceive)
```

`Decode` garde au plus `MaxGroups` groupes privés et un Public :

```csharp
    public static IReadOnlyList<GroupRecord> Decode(ReadOnlySpan<byte> json)
    {
        var dtos = JsonSerializer.Deserialize<List<GroupDto?>>(json) ?? [];
        var groups = dtos.Select(Rehydrate).OfType<GroupRecord>().ToList();

        // Le Public ne compte pas dans les dix : il n'ouvre qu'une boîte de
        // présence par fenêtre, et aucune d'admission.
        return [.. groups.Where(group => group.IsPublic is false).Take(GroupBook.MaxGroups),
                .. groups.Where(group => group.IsPublic).Take(1)];
    }
```

Dans `Rehydrate`, calculer `id` avant de juger les services, puis remplacer la règle « un groupe sans service est injoignable » par :

```csharp
            var isPublic = PublicGroup.Is(id);

            // Le Public tire ses services de la configuration : il vit sans en
            // avoir d'enregistré. Un groupe privé sans service est injoignable.
            if (places.Count == 0 && isPublic is false)
                return null;

            // L'identifiant du Public avec un autre secret, ou une clé, ferait
            // composer sous des boîtes que personne n'ouvre, ou prêter une
            // autorité à ce qui n'en a aucune.
            if (isPublic && (secret.AsSpan().SequenceEqual(PublicGroup.Secret) is false
                             || dto.OwnerKey is not null || dto.SigningKey is not null || dto.Policy is not null))
                return null;
```

Les blocages, relus un par un (une entrée abîmée est ignorée, pas le groupe) :

```csharp
            var blocked = (dto.Blocked ?? [])
                .Take(MaxBlocked)
                .Select(RehydrateBan)
                .OfType<GroupBan>()
                .ToList();
```

```csharp
    private static GroupBan? RehydrateBan(BanDto? dto)
    {
        try
        {
            var peer = dto?.Peer is { } peerHex ? PeerId.FromBytes(Convert.FromHexString(peerHex)) : (PeerId?)null;
            var print = dto?.Fingerprint is { } printHex ? PlayerFingerprint.FromBytes(Convert.FromHexString(printHex)) : (PlayerFingerprint?)null;

            return peer is null && print is null ? null : new GroupBan(peer, print);
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            return null;
        }
    }
```

Le `GroupRecord` rendu reçoit :

```csharp
                Dormant = isPublic && dto.Dormant,
                Blocked = blocked,
                DefaultReceive = dto.DefaultReceive is { } bits
                    ? FromBits(bits)
                    : isPublic ? TransientCategories.None : TransientCategories.All,
```

Dans `RehydrateMember` : `Receive = dto.Receive == ReceiveFollowsGroup ? null : FromBits(dto.Receive),`.

Un fichier écrit avant cet incrément porte `Receive = 7` pour chaque membre : il se relit en réglage explicite « tout », ce qui est exactement ce qu'il voulait dire.

- [ ] **Step 5: Corriger les appelants de `Receive`**

- `GroupDialPlanner.Record` : `Receive = group.ReceiveOf(known),`
- `GroupsPage.DrawReceive` : `var receive = group.ReceiveOf(member);`
- Les tests qui comparent `member.Receive` à `TransientCategories.All` sur un membre créé par `Admit` : ils doivent désormais attendre `null`, ou comparer `group.ReceiveOf(member)`. Lancer la suite pour les trouver.

- [ ] **Step 6: Lancer les tests**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj` puis `dotnet build Linkpearl/Linkpearl.csproj -c Release`
Expected: PASS, sans warning.

- [ ] **Step 7: Commit**

```bash
git add Linkpearl/Core/Groups/PublicGroup.cs Linkpearl/Core/Groups/GroupRecord.cs Linkpearl/Core/Groups/GroupBookCodec.cs \
  Linkpearl/Core/Groups/GroupDialPlanner.cs Linkpearl/Ui/Pages/GroupsPage.cs Linkpearl.Core.Tests/Groups
git commit -m "feat(groupes): le groupe Public, ses blocages et ses effets par défaut"
```

---

### Task 5: Le carnet active Public, bloque et règle ses effets

**Files:**
- Modify: `Linkpearl/Core/Groups/GroupBook.cs`
- Test: `Linkpearl.Core.Tests/Groups/PublicGroupTests.cs`

**Interfaces:**
- Consumes: tâche 4.
- Produces, sur `GroupBook` :
  - `All` : groupes privés et Public actif (jamais un Public dormant)
  - `Public : GroupRecord?` (actif ou dormant, pour l'interface)
  - `SetPublic(bool enabled, IReadOnlyList<RendezvousAddress> services)`
  - `SetPublicServices(IReadOnlyList<RendezvousAddress> services)` (ne lève `Changed` que si la liste change)
  - `Block(GroupId, PlayerFingerprint member)` : bloque l'empreinte et, si elle est épinglée, la clé
  - `Unblock(GroupId, GroupBan ban)`
  - `SetDefaultReceive(GroupId, TransientCategories)`
  - `SetReceive(GroupId, PlayerFingerprint, TransientCategories?)` (null : revient au défaut)
  - `Admit` refuse par `GroupRecord.Refuses`, ignore un Public dormant (`UnknownGroup`), et crée un membre avec `Receive = null`
  - `TryAdd` refuse un Public (passer par `SetPublic`) et ne compte que les groupes privés

- [ ] **Step 1: Écrire les tests qui échouent**

Ajouter à `PublicGroupTests` :

```csharp
    private static readonly PlayerFingerprint Bob = PlayerFingerprint.Of("bob", 21);

    /// <summary>Une clé d'identité neuve à chaque appel, point de 65 octets.</summary>
    private static byte[] FreshKey()
    {
        using var identity = Linkpearl.Core.Crypto.CryptoPrimitives.GenerateIdentity();
        return Linkpearl.Core.Crypto.CryptoPrimitives.ExportPublicPoint(identity);
    }

    [Fact]
    public void Public_dormant_n_est_pas_dans_All()
    {
        var book = new GroupBook(_clock);

        book.SetPublic(true, [Service]);
        Assert.Contains(book.All, group => group.IsPublic);

        book.SetPublic(false, [Service]);
        Assert.DoesNotContain(book.All, group => group.IsPublic);
        Assert.True(book.Public!.Dormant);
        Assert.Null(book.Find(PublicGroup.Id));
    }

    [Fact]
    public void Reactiver_retrouve_les_blocages()
    {
        var book = new GroupBook(_clock);
        book.SetPublic(true, [Service]);
        book.Block(PublicGroup.Id, Bob);

        book.SetPublic(false, [Service]);
        book.SetPublic(true, [Service]);

        Assert.True(book.Public!.Refuses(null, Bob));
    }

    [Fact]
    public void Un_Public_dormant_n_admet_personne()
    {
        var book = new GroupBook(_clock);
        book.SetPublic(true, [Service]);
        book.SetPublic(false, [Service]);

        Assert.Equal(GroupAdmission.UnknownGroup, book.Admit(PublicGroup.Id, Bob, FreshKey(), "Bob"));
    }

    [Fact]
    public void Un_bloque_n_est_pas_admis_et_sa_cle_epinglee_est_bloquee_aussi()
    {
        var book = new GroupBook(_clock);
        book.SetPublic(true, [Service]);
        var key = FreshKey();
        Assert.Equal(GroupAdmission.Pinned, book.Admit(PublicGroup.Id, Bob, key, "Bob"));

        book.Block(PublicGroup.Id, Bob);

        Assert.Equal(GroupAdmission.Banned, book.Admit(PublicGroup.Id, Bob, key, "Bob"));
        Assert.Contains(book.Public!.Blocked, ban => ban.Peer == PeerId.Of(key) && ban.Fingerprint == Bob);
    }

    [Fact]
    public void Un_membre_rencontre_suit_le_defaut_jusqu_a_ce_qu_on_le_regle()
    {
        var book = new GroupBook(_clock);
        book.SetPublic(true, [Service]);
        book.Admit(PublicGroup.Id, Bob, FreshKey(), "Bob");

        Assert.Null(book.Public!.Members[Bob].Receive);
        Assert.Equal(TransientCategories.None, book.Public.ReceiveOf(book.Public.Members[Bob]));

        book.SetDefaultReceive(PublicGroup.Id, TransientCategories.All);
        Assert.Equal(TransientCategories.All, book.Public!.ReceiveOf(book.Public.Members[Bob]));

        book.SetReceive(PublicGroup.Id, Bob, TransientCategories.None);
        Assert.Equal(TransientCategories.None, book.Public!.ReceiveOf(book.Public.Members[Bob]));
    }

    [Fact]
    public void Les_services_du_Public_suivent_la_configuration_sans_ecriture_inutile()
    {
        var book = new GroupBook(_clock);
        book.SetPublic(true, [Service]);
        var writes = 0;
        book.Changed += () => writes++;

        book.SetPublicServices([Service]);
        Assert.Equal(0, writes);

        var other = new RendezvousAddress("rdv.autre.ch", 47900);
        book.SetPublicServices([Service, other]);
        Assert.Equal(1, writes);
        Assert.Equal([Service, other], book.Public!.Rendezvous);
    }

    [Fact]
    public void Public_ne_prend_pas_la_place_d_un_groupe_prive()
    {
        var book = new GroupBook(_clock);
        book.SetPublic(true, [Service]);

        for (var i = 0; i < GroupBook.MaxGroups; i++)
            Assert.True(book.TryAdd(GroupBookTests.Group([.. Enumerable.Range(0, 32).Select(b => (byte)(b + i))], _clock.UtcNow), out var why), why);

        Assert.False(book.TryAdd(PublicGroup.Create([Service], _clock.UtcNow), out _));
    }
```

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter FullyQualifiedName~PublicGroupTests`
Expected: échec de compilation.

- [ ] **Step 2: Écrire le carnet**

Dans `GroupBook` :

- une aide commune qui ignore les dormants :

```csharp
    /// <summary>Un groupe vivant : un Public désactivé ne répond à rien.</summary>
    private bool TryLive(GroupId id, out GroupRecord group)
        => _groups.TryGetValue(id, out group!) && group.Dormant is false;
```

- `All` : `return [.. _groups.Values.Where(group => group.Dormant is false)];`
- `Find(id)` : `return TryLive(id, out var group) ? group : null;`
- `CurrentPolicy`, `OfferPolicy`, `Admit`, `UpdateMember` : `TryLive` à la place de `_groups.TryGetValue`.
- `TryAdd` : refuser `group.IsPublic` (« le Public s'active, il ne s'ajoute pas ») ; comparer `_groups.Values.Count(known => known.IsPublic is false)` à `MaxGroups`.
- `Load` : garder au plus `MaxGroups` groupes privés et un Public, comme le codec.
- Dans `Admit`, remplacer `group.Policy?.IsBanned(key, member) is true` par `group.Refuses(key, member)`. Le membre neuf n'a plus de `Receive` posé (il reste null).
- `SetReceive(GroupId id, PlayerFingerprint member, TransientCategories? receive)` : signature élargie, même corps.

Les nouvelles méthodes :

```csharp
    public GroupRecord? Public
    {
        get
        {
            lock (_gate)
                return _groups.GetValueOrDefault(PublicGroup.Id);
        }
    }

    /// <summary>
    /// Active ou désactive le Public.
    /// </summary>
    /// <remarks>
    /// Désactivé, il reste au carnet, dormant : ses blocages et ses réglages
    /// survivent, et on les retrouve en le réactivant.
    /// </remarks>
    public void SetPublic(bool enabled, IReadOnlyList<RendezvousAddress> services)
    {
        lock (_gate)
        {
            _groups[PublicGroup.Id] = _groups.TryGetValue(PublicGroup.Id, out var known)
                ? known with { Dormant = enabled is false, Rendezvous = services }
                : PublicGroup.Create(services, clock.UtcNow) with { Dormant = enabled is false };
        }

        Changed?.Invoke();
    }

    /// <summary>Les services du Public sont ceux de la configuration, recopiés à chaque ronde.</summary>
    public void SetPublicServices(IReadOnlyList<RendezvousAddress> services)
    {
        lock (_gate)
        {
            if (_groups.TryGetValue(PublicGroup.Id, out var known) is false || known.Rendezvous.SequenceEqual(services))
                return;

            _groups[PublicGroup.Id] = known with { Rendezvous = services };
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Bloque un membre, par son personnage et, s'il est épinglé, par sa clé.
    /// </summary>
    /// <remarks>
    /// Les deux : la clé seule laisserait revenir le même joueur sous une clé
    /// neuve, le personnage seul le laisserait revenir sous un autre.
    /// </remarks>
    public void Block(GroupId id, PlayerFingerprint member)
    {
        lock (_gate)
        {
            if (_groups.TryGetValue(id, out var group) is false || group.Blocked.Any(ban => ban.Fingerprint == member))
                return;

            var ban = new GroupBan(group.Members.GetValueOrDefault(member)?.Id, member);
            _groups[id] = group with { Blocked = [.. group.Blocked, ban] };
        }

        Changed?.Invoke();
    }

    public void Unblock(GroupId id, GroupBan ban)
    {
        lock (_gate)
        {
            if (_groups.TryGetValue(id, out var group) is false)
                return;

            _groups[id] = group with { Blocked = [.. group.Blocked.Where(known => known != ban)] };
        }

        Changed?.Invoke();
    }

    public void SetDefaultReceive(GroupId id, TransientCategories receive)
    {
        lock (_gate)
        {
            if (_groups.TryGetValue(id, out var group) is false)
                return;

            _groups[id] = group with { DefaultReceive = receive };
        }

        Changed?.Invoke();
    }
```

`Block`, `Unblock` et `SetDefaultReceive` agissent aussi sur un Public dormant (`_groups.TryGetValue`, pas `TryLive`) : on doit pouvoir le régler avant de l'activer. `GroupBan` est un `record` : l'égalité de valeur suffit à `Unblock`.

- [ ] **Step 3: Lancer les tests**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj` puis le build Release.
Expected: PASS. Les appelants de `SetReceive` côté plugin compilent sans changement (conversion implicite vers le nullable).

- [ ] **Step 4: Commit**

```bash
git add Linkpearl/Core/Groups/GroupBook.cs Linkpearl.Core.Tests/Groups/PublicGroupTests.cs
git commit -m "feat(groupes): activer Public, bloquer un membre, régler ses effets par défaut"
```

---

### Task 6: Le planificateur et le moteur écartent les listés et les bloqués

**Files:**
- Modify: `Linkpearl/Core/Groups/GroupDialPlanner.cs`
- Modify: `Linkpearl/Core/Sync/SyncEngine.cs`
- Test: `Linkpearl.Core.Tests/Groups/GroupDialPlannerTests.cs`, `Linkpearl.Core.Tests/Sync/ServiceBanEngineTests.cs` (nouveau)

**Interfaces:**
- Consumes: `IServiceBans`, `ServiceBanStatus`, `BanVerdict`, `ServiceBan` (tâche 3) ; `GroupRecord.Refuses`, `IsPublic`, `ReceiveOf` (tâche 4) ; `PublicGroupTests.Service`.
- Produces:
  - `GroupDialPlanner.Plan(PlayerFingerprint ours, IReadOnlyList<GroupSighting>, IReadOnlyList<GroupRecord>, IEnumerable<PlayerFingerprint> directlyPaired, IServiceBans? bans = null)`
  - `SyncEngine(…, IServiceBans? bans = null)` (dernier paramètre optionnel du constructeur)

- [ ] **Step 1: Écrire les tests qui échouent**

Dans `GroupDialPlannerTests.cs` (ajouter `using Linkpearl.Core.Safety;` s'il manque) :

```csharp
    private sealed class FixedBans(Dictionary<PlayerFingerprint, BanVerdict> verdicts) : IServiceBans
    {
        public ServiceBanStatus Status(PlayerFingerprint player)
            => verdicts.TryGetValue(player, out var verdict)
                ? new ServiceBanStatus(verdict, verdict is BanVerdict.Listed ? new ServiceBan(PublicGroupTests.Service, "triche") : null)
                : ServiceBanStatus.Clear;
    }

    [Fact]
    public void Un_joueur_liste_par_un_service_n_est_jamais_compose()
    {
        var group = Group(Secret);

        Assert.Empty(new GroupDialPlanner(_clock).Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [group], [],
            new FixedBans(new() { [Bob] = BanVerdict.Listed })));
    }

    [Fact]
    public void Un_joueur_liste_en_cours_de_route_perd_sa_place()
    {
        var group = Group(Secret);
        var planner = new GroupDialPlanner(_clock);
        Assert.Single(planner.Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [group], []));

        Assert.Empty(planner.Plan(Alice, [], [group], [], new FixedBans(new() { [Bob] = BanVerdict.Listed })));
    }

    [Fact]
    public void En_attente_de_verdict_Public_attend_mais_un_groupe_prive_compose()
    {
        var prive = Group(Secret);
        var @public = PublicGroup.Create([PublicGroupTests.Service], _clock.UtcNow);
        var bans = new FixedBans(new() { [Bob] = BanVerdict.Pending });

        Assert.Empty(new GroupDialPlanner(_clock).Plan(Alice, [new GroupSighting(@public.Id, Bob, "Bob")], [@public], [], bans));
        Assert.Single(new GroupDialPlanner(_clock).Plan(Alice, [new GroupSighting(prive.Id, Bob, "Bob")], [prive], [], bans));
    }

    [Fact]
    public void Un_membre_bloque_n_est_pas_compose()
    {
        var @public = PublicGroup.Create([PublicGroupTests.Service], _clock.UtcNow) with { Blocked = [new GroupBan(null, Bob)] };

        Assert.Empty(new GroupDialPlanner(_clock).Plan(Alice, [new GroupSighting(@public.Id, Bob, "Bob")], [@public], []));
    }

    [Fact]
    public void Un_membre_du_Public_arrive_effets_coupes()
    {
        var @public = PublicGroup.Create([PublicGroupTests.Service], _clock.UtcNow);

        var planned = Assert.Single(new GroupDialPlanner(_clock).Plan(Alice, [new GroupSighting(@public.Id, Bob, "Bob")], [@public], []));

        Assert.Equal(TransientCategories.None, planned.Receive);
    }
```

`Linkpearl.Core.Tests/Sync/ServiceBanEngineTests.cs` : deux moteurs réels pairés en direct. Lire `SyncEngineTests.cs` (et `GroupSyncEngineTests.cs` pour la forme) et reprendre sa fabrique de deux carnets pairés, de `MeetingPoint`, de `FixedAppearance` et de `RecordingApplicator` ; la copier si elle est privée. Le moteur de Bob reçoit `bans:`.

```csharp
    private sealed class SwitchableBans : IServiceBans
    {
        public volatile bool Listed;

        public ServiceBanStatus Status(PlayerFingerprint player)
            => Listed
                ? new ServiceBanStatus(BanVerdict.Listed, new ServiceBan(new RendezvousAddress("rdv.exemple.ch", 47900), "triche"))
                : ServiceBanStatus.Clear;
    }

    [Fact]
    public async Task Une_paire_listee_perd_son_apparence()
    {
        var bans = new SwitchableBans();
        await using var world = await PairedWorldAsync(bobBans: bans);

        Assert.True(await SettleAsync(world, () => world.BobApplicator.Applied.Count > 0), "rien n'a été posé");

        bans.Listed = true;

        Assert.True(await SettleAsync(world, () => world.BobApplicator.Removed.Count > 0), "l'apparence est restée");
    }

    [Fact]
    public async Task Rien_n_est_pose_sur_un_liste()
    {
        var bans = new SwitchableBans { Listed = true };
        await using var world = await PairedWorldAsync(bobBans: bans);

        Assert.False(await SettleAsync(world, () => world.BobApplicator.Applied.Count > 0, rounds: 300));
    }
```

Si `RecordingApplicator` ne note pas les retraits, lui ajouter une liste `Removed` alimentée par sa méthode de retrait.

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter "FullyQualifiedName~GroupDialPlanner|FullyQualifiedName~ServiceBanEngine"`
Expected: échec de compilation.

- [ ] **Step 2: Le planificateur**

Ajouter `IServiceBans? bans = null` en dernier paramètre de `Plan`, et en tête de méthode une fonction locale :

```csharp
        // Un listé ne se compose nulle part. Un verdict en attente ne retient
        // que le Public : ses membres sont des inconnus, alors qu'un groupe
        // privé réunit des gens admis. Voir la décision 3 du plan de l'incrément 3.
        bool Excluded(GroupRecord group, PlayerFingerprint member)
        {
            var status = bans?.Status(member) ?? ServiceBanStatus.Clear;

            return group.Refuses(group.Members.GetValueOrDefault(member)?.Id, member)
                   || status.Verdict is BanVerdict.Listed
                   || (status.Verdict is BanVerdict.Pending && group.IsPublic);
        }
```

Remplacer, dans le filtre des `sightings` et dans la boucle de nettoyage de `_recent`, l'expression `group.Policy?.IsBanned(…) is true` par `Excluded(group, sighting.Member)` et `Excluded(group, member)`. Les commentaires existants sur la protection par Id épinglé restent vrais : `Refuses` passe par `Policy.IsBanned`.

- [ ] **Step 3: Le moteur**

Constructeur : paramètre `IServiceBans? bans = null` en dernier, gardé dans `private readonly IServiceBans? _bans;`.

Dans `ReconcileVisibility`, première boucle, juste après le test `ReceiveAppearance` :

```csharp
            // Listé par un service actif : rien de posé, paire directe
            // comprise. Absent d'« announced », il sort du champ pour la suite,
            // et ce qui était posé se retire par le chemin ordinaire.
            if (_bans?.Status(fingerprint).Verdict is BanVerdict.Listed)
                continue;
```

- [ ] **Step 4: Lancer les tests**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj` puis le build Release.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Linkpearl/Core/Groups/GroupDialPlanner.cs Linkpearl/Core/Sync/SyncEngine.cs \
  Linkpearl.Core.Tests/Groups/GroupDialPlannerTests.cs Linkpearl.Core.Tests/Sync
git commit -m "feat(sync): ni session ni apparence pour un personnage listé ou bloqué"
```

---

### Task 7: L'hôte d'admission écarte les candidats listés

**Files:**
- Modify: `Linkpearl/Core/Groups/AdmissionHost.cs`
- Test: `Linkpearl.Core.Tests/Groups/AdmissionTests.cs`

**Interfaces:**
- Consumes: rien de neuf dans le noyau ; le filtre est une fonction.
- Produces: `AdmissionHost(GroupBook book, Func<byte[]?> ourIdentityKey, IClock clock, Func<AdmissionRequest, PlayerFingerprint, bool>? refuses = null)`

- [ ] **Step 1: Écrire les tests qui échouent**

Dans `AdmissionTests.cs`, en reprenant les aides du fichier (le lire d'abord ; les noms ci-dessous sont à remplacer par les siens) :

```csharp
    [Fact]
    public void Un_candidat_liste_ne_recoit_aucune_reponse_en_mode_mot_de_passe()
    {
        var calls = 0;
        var host = HostFor(PasswordGroup(), refuses: (_, _) => { calls++; return true; });

        Assert.Empty(host.OnRequest(Request(), CandidatePrint));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Un_candidat_liste_n_est_pas_propose_aux_moderateurs()
    {
        var host = HostFor(ValidationGroup(), refuses: (_, _) => true);

        host.OnRequest(Request(), CandidatePrint);

        Assert.Empty(host.Pending);
    }

    [Fact]
    public void Le_filtre_n_est_consulte_qu_une_fois_par_demande()
    {
        // Il coûte une dérivation PBKDF2 : un redépôt de la même demande, chaque
        // minute, ne doit pas la refaire.
        var calls = 0;
        var host = HostFor(PasswordGroup(), refuses: (_, _) => { calls++; return false; });
        var request = Request();

        host.OnRequest(request, CandidatePrint);
        _clock.Advance(AdmissionHost.ChallengeResendInterval);
        host.OnRequest(request, CandidatePrint);

        Assert.Equal(1, calls);
    }
```

S'il n'y a pas de paramètre `refuses` dans l'aide qui construit l'hôte, l'ajouter.

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter FullyQualifiedName~AdmissionTests`
Expected: échec de compilation.

- [ ] **Step 2: Écrire le filtre**

Ajouter le paramètre au constructeur primaire. Dans `OnRequest`, **à l'intérieur du verrou, après les plafonds** et seulement pour une demande neuve, donc à deux endroits :

- mode validation : juste avant `_pending[key] = new Waiting(…)`,
- mode mot de passe : juste avant `var ephemeral = CryptoPrimitives.GenerateEphemeral();`,

```csharp
            // Listé par un service actif : aucune réponse, comme un banni du
            // groupe. Après les plafonds et pour une demande neuve seulement :
            // le filtre coûte une dérivation, et une inondation de demandes
            // forgées ne doit pas la payer au-delà de ce que le plafond laisse.
            if (refuses?.Invoke(request, candidate) is true)
            {
                _answered[key] = now;
                return [];
            }
```

`_answered` retient la demande 15 minutes : ses redépôts ne reconsultent pas le filtre. Le filtre est appelé sous le verrou de l'hôte ; celui que l'adaptateur passe ne prend que le verrou de `ServiceBanBook`, qui ne rappelle rien, donc aucun ordre de verrous n'est inversé.

- [ ] **Step 3: Lancer les tests**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add Linkpearl/Core/Groups/AdmissionHost.cs Linkpearl.Core.Tests/Groups/AdmissionTests.cs
git commit -m "feat(groupes): pas d'admission pour un candidat listé par un service"
```

---

### Task 8: L'adaptateur télécharge les listes et vérifie les joueurs visibles

**Files:**
- Create: `Linkpearl/Integration/ServiceBanFetcher.cs`
- Create: `Linkpearl/Integration/ServiceBanScreening.cs`
- Modify: `Linkpearl/Integration/PresenceService.cs`
- Modify: `Linkpearl/Plugin.cs`

**Interfaces:**
- Consumes: `RendezvousClient.QueryBanListAsync` (tâche 1), `ServiceBanBook` (tâche 3), planificateur et moteur (tâche 6), filtre d'admission (tâche 7).
- Produces:
  - `ServiceBanFetcher(Configuration, ServiceBanBook, IPluginLog)` : `Start()`, `RefreshSoon()`, `Dispose()`
  - `ServiceBanScreening(ServiceBanBook, IPluginLog)` : `Schedule(IReadOnlyList<NearbyPlayer> players)`, `Dispose()`
  - `PresenceService.SetServiceBans(ServiceBanBook)` : les demandes de pairage listées sont ignorées
  - `Plugin._serviceBans : ServiceBanBook`, utilisé par la tâche 9

Pas de test automatique : ce sont des adaptateurs Dalamud. La logique testable vit dans les tâches 1 à 7. Vérification par le build Release, puis en jeu (tâche 10).

- [ ] **Step 1: Le téléchargement**

`Linkpearl/Integration/ServiceBanFetcher.cs` :

```csharp
using Dalamud.Plugin.Services;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Integration;

/// <summary>
/// Télécharge la liste de bannissement de chaque service actif, puis toutes les heures.
/// </summary>
/// <remarks>
/// Une connexion neuve par service et par téléchargement, fermée aussitôt :
/// la présence tient déjà la sienne, et y mêler ces trames ferait deux
/// lecteurs sur un flux. Un échec garde la liste précédente : un service qui
/// hoquette ne doit pas lever d'un coup tous ses bannissements. Un service
/// d'avant ces trames n'a jamais de liste, et ne bannit donc personne.
/// </remarks>
public sealed class ServiceBanFetcher(Configuration configuration, ServiceBanBook book, IPluginLog log) : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly CancellationTokenSource _life = new();
    private readonly SemaphoreSlim _wake = new(0, 1);

    public void Start() => _ = Task.Run(() => LoopAsync(_life.Token));

    /// <summary>La liste des services a changé : ne pas attendre l'heure.</summary>
    public void RefreshSoon()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Déjà réveillé : une seconde demande n'ajoute rien.
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (ct.IsCancellationRequested is false)
        {
            var services = configuration.ActiveRendezvous.Select(entry => entry.Address).ToList();
            book.Retain(services);

            foreach (var service in services)
                await FetchAsync(service, ct).ConfigureAwait(false);

            try
            {
                await _wake.WaitAsync(Interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task FetchAsync(RendezvousAddress service, CancellationToken ct)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));

            await using var client = new RendezvousClient();
            await client.ConnectAsync(service.Host, service.Port, deadline.Token).ConfigureAwait(false);

            var (list, failure) = await client.QueryBanListAsync(deadline.Token).ConfigureAwait(false);

            if (list is null)
            {
                log.Debug($"Liste de bannissement de {service} indisponible : {failure}");
                return;
            }

            book.SetList(service, list);
            log.Information($"Liste de bannissement de {service} : {list.Entries.Count} entrée(s).");
        }
        catch (Exception e) when (ct.IsCancellationRequested is false)
        {
            log.Debug($"Liste de bannissement de {service} en échec : {e.Message}");
        }
    }

    public void Dispose()
    {
        _life.Cancel();
        _life.Dispose();
    }
}
```

`RendezvousClient` est `IAsyncDisposable` et `RendezvousAddress` expose `Host` et `Port` (déjà utilisés par `PresenceService.OpenAsync`).

- [ ] **Step 2: Les dérivations en arrière-plan**

`Linkpearl/Integration/ServiceBanScreening.cs` :

```csharp
using Dalamud.Plugin.Services;
using Linkpearl.Core.Safety;

namespace Linkpearl.Integration;

/// <summary>
/// Dérive, sur le pool, l'empreinte de bannissement des joueurs à vérifier.
/// </summary>
/// <remarks>
/// C'est ici, et seulement ici, qu'un nom rencontre une liste : le noyau ne
/// reçoit que le haché. Une dérivation coûte de l'ordre de 0,1 à 0,3 s ; elles
/// s'enchaînent sur une seule tâche, pour ne jamais occuper plus d'un cœur à
/// côté du jeu. Une ronde qui arrive pendant qu'une autre tourne est ignorée :
/// la suivante reprendra ce qui manque encore.
/// </remarks>
public sealed class ServiceBanScreening(ServiceBanBook book, IPluginLog log) : IDisposable
{
    private readonly CancellationTokenSource _life = new();
    private int _running;

    public void Schedule(IReadOnlyList<NearbyPlayer> players)
    {
        var todo = players
            .Select(player => (Player: player, Missing: book.Missing(player.Fingerprint)))
            .Where(entry => entry.Missing.Count > 0)
            .ToList();

        if (todo.Count == 0 || Interlocked.Exchange(ref _running, 1) == 1)
            return;

        var ct = _life.Token;

        _ = Task.Run(() =>
        {
            try
            {
                foreach (var (player, missing) in todo)
                {
                    foreach (var derivation in missing)
                    {
                        ct.ThrowIfCancellationRequested();
                        book.Record(player.Fingerprint, derivation.Key,
                            BanList.Derive(player.Name, player.WorldId, derivation.Salt, derivation.Parameters));
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                log.Warning(e, "Vérification des listes de bannissement en échec.");
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }, ct);
    }

    public void Dispose()
    {
        _life.Cancel();
        _life.Dispose();
    }
}
```

Vérifier le nom réel du champ monde de `NearbyPlayer` (`WorldId` ou `World`) et son espace de noms. La normalisation du nom est faite par `BanList.Derive` (`Trim().ToLowerInvariant()`), qui est celle du service.

- [ ] **Step 3: Les demandes de pairage listées**

Dans `PresenceService` :

```csharp
    /// <summary>Les listes des services, attachées une fois par le plugin.</summary>
    private volatile ServiceBanBook? _bans;

    public void SetServiceBans(ServiceBanBook bans) => _bans = bans;
```

Dans `OnPairMessage`, les acceptations ne changent pas (elles répondent à notre propre demande). Juste avant `lock (_gate) _incoming.Add(request);` :

```csharp
        // Listé par un service actif : la demande est ignorée, comme le veut la
        // spec. Screen dérive ce qui manque, dans son budget ; au-delà, le
        // verdict reste en attente et la demande est ignorée aussi.
        if (_bans?.Screen(sender, (salt, parameters) => BanList.Derive(message.CharacterName, message.WorldId, salt, parameters))
                .Verdict is BanVerdict.Listed or BanVerdict.Pending)
        {
            _log.Information("Demande de pairage ignorée : expéditeur listé par un service, ou vérification impossible pour l'instant.");
            return;
        }
```

Aucun nom au journal.

- [ ] **Step 4: Câbler dans le plugin**

Dans `Plugin` :

- champs `private readonly ServiceBanBook _serviceBans;`, `private readonly ServiceBanFetcher _banFetcher;`, `private readonly ServiceBanScreening _banScreening;`, créés dans le constructeur à côté de `_groups` ; `_presence.SetServiceBans(_serviceBans)` ; `_banFetcher.Start()` à la fin du constructeur.
- l'hôte d'admission :

```csharp
        _admissionHost = new AdmissionHost(
            _groups, () => _pairing.Identity?.PublicKey, clock,
            refuses: (request, print) => _serviceBans.Screen(
                    print, (salt, parameters) => BanList.Derive(request.CharacterName, request.WorldId, salt, parameters))
                .Verdict is not BanVerdict.Clear);
```

- `StartEngineIfReady` : passer `bans: _serviceBans` au `SyncEngine`.
- `RefreshLoopAsync`, après le bloc de `RefreshDetectionAsync` et avant le calcul des `sightings` :

```csharp
                        // Les joueurs à vérifier : ceux qui ont le plugin, et les
                        // paires du carnet, dont l'apparence se pose sans détection.
                        var pinned = _pairing.Book.All
                            .Select(pair => pair.PinnedFingerprint)
                            .OfType<PlayerFingerprint>()
                            .ToHashSet();

                        _banScreening.Schedule([.. _state.Nearby.Where(player =>
                            _presence.Detected.ContainsKey(player.Fingerprint) || pinned.Contains(player.Fingerprint))]);
```

  et l'appel `_groupPlanner.Plan(…)` reçoit `_serviceBans` en dernier argument.
- Toujours dans `RefreshLoopAsync`, suivre la liste des services : garder la dernière liste vue dans un champ `_lastServices` (texte joint des adresses) et appeler `_banFetcher.RefreshSoon()` quand elle change.
- `Dispose` : `_banFetcher.Dispose()` et `_banScreening.Dispose()` avant la présence.

- [ ] **Step 5: Construire**

Run: `dotnet build Linkpearl/Linkpearl.csproj -c Release` puis `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: sans warning, PASS.

- [ ] **Step 6: Commit**

```bash
git add Linkpearl/Integration/ServiceBanFetcher.cs Linkpearl/Integration/ServiceBanScreening.cs \
  Linkpearl/Integration/PresenceService.cs Linkpearl/Plugin.cs
git commit -m "feat(integration): télécharger les listes des services et vérifier les joueurs visibles"
```

---

### Task 9: Public et bannissements dans l'interface

**Files:**
- Modify: `Linkpearl/Configuration.cs`
- Modify: `Linkpearl/Ui/GroupActions.cs`
- Create: `Linkpearl/Ui/Components/BanChip.cs`
- Modify: `Linkpearl/Ui/Pages/GroupsPage.cs`, `Linkpearl/Ui/Pages/NearbyPage.cs`, `Linkpearl/Ui/Pages/PairsPage.cs`, et la fenêtre qui les construit (`MainWindow.cs`)
- Modify: `Linkpearl/Plugin.cs`

**Interfaces:**
- Consumes: `GroupBook.Public`, `SetPublic`, `SetPublicServices`, `Block`, `Unblock`, `SetDefaultReceive`, `SetReceive` (tâche 5) ; `IServiceBans` et `Plugin._serviceBans` (tâches 3 et 8).
- Produces, sur `GroupActions` :
  - `required Action<bool> SetPublic`
  - `required Func<bool> PublicWarningSeen`, `required Action AcknowledgePublicWarning`
  - `required Action<GroupId, PlayerFingerprint> Block`, `required Action<GroupId, GroupBan> Unblock`
  - `required Action<GroupId, TransientCategories> SetDefaultReceive`
  - `SetReceive` devient `Action<GroupId, PlayerFingerprint, TransientCategories?>`

Pas de test automatique (ImGui). Vérification : build Release, puis en jeu (tâche 10).

- [ ] **Step 1: Configuration, actions et services du Public**

`Configuration` :

```csharp
    /// <summary>
    /// Vrai une fois l'avertissement de Public lu.
    /// </summary>
    /// <remarks>La spec veut qu'il s'affiche une fois : la première activation, pas chaque bascule.</remarks>
    public bool PublicWarningSeen { get; set; }
```

`GroupActions` : ajouter les délégués listés ci-dessus, chacun avec un résumé d'une ligne, et élargir `SetReceive`.

`Plugin` : les brancher sur `_groups` (l'enregistrement suit `Changed`, comme pour les autres gestes ; le vérifier) ; `SetPublic` passe `[.. _configuration.ActiveRendezvous.Select(entry => entry.Address)]` ; `PublicWarningSeen` lit la configuration, `AcknowledgePublicWarning` l'écrit puis `Save()`. Dans `RefreshLoopAsync`, avant `_presence.SetGroups(_groups.All)` :

```csharp
                        // Les services du Public sont ceux de la configuration :
                        // ils suivent un service ajouté ou retiré sans rien réactiver.
                        _groups.SetPublicServices([.. _configuration.ActiveRendezvous.Select(entry => entry.Address)]);
```

- [ ] **Step 2: La puce de bannissement**

`Linkpearl/Ui/Components/BanChip.cs` :

```csharp
using Dalamud.Bindings.ImGui;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;

namespace Linkpearl.Ui.Components;

/// <summary>La puce « banni » et son motif, identique sur toutes les pages.</summary>
/// <remarks>
/// Le motif vient d'un service tiers : il passe par <c>Glyphs.Safe</c> comme
/// un nom de pair, et ne s'affiche qu'au survol pour ne pas occuper la ligne.
/// </remarks>
internal static class BanChip
{
    /// <summary>Dessine la puce si le joueur est listé ; rend vrai dans ce cas.</summary>
    public static bool Draw(IServiceBans bans, PlayerFingerprint? player)
    {
        if (player is not { } print || bans.Status(print) is not { Verdict: BanVerdict.Listed, Ban: { } ban })
            return false;

        Chip.Draw("banni par un service", Theme.Danger, Icons.Blocked);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{ban.Service} : {Glyphs.Safe(ban.Reason)}");

        return true;
    }
}
```

Vérifier les noms réels (`Chip.Draw`, `Theme.Danger`, `Glyphs.Safe`, espaces de noms) dans les pages existantes.

Passer `IServiceBans bans` aux constructeurs de `NearbyPage`, `PairsPage` et `GroupsPage`, là où la fenêtre principale les construit, en leur donnant `_serviceBans`.

- `NearbyPage` : avant le bouton « Demander », `if (BanChip.Draw(bans, player.Fingerprint)) return;`. Le bouton n'est pas dessiné : c'est le « bouton désactivé » de la spec, avec le motif au survol.
- `PairsPage` : sur la ligne d'un pair, après son nom, `BanChip.Draw(bans, pair.PinnedFingerprint);`.
- `GroupsPage.DrawMembers` : après le nom d'un membre, `BanChip.Draw(bans, member.Fingerprint);`.

- [ ] **Step 3: La carte Public**

Dans `GroupsPage.Draw`, entre le sous-titre et `DrawEntry()`, appeler `DrawPublic()`. Exclure le Public de la liste des groupes privés : `var all = groups.All.Where(group => group.IsPublic is false).ToList();`.

```csharp
    private const string PublicWarning =
        "Tout joueur visible qui a aussi activé Public verra votre apparence moddée, et vous la sienne.";

    /// <summary>Vrai tant que l'avertissement attend sa réponse, avant la première activation.</summary>
    private bool _publicWarningOpen;

    /// <summary>
    /// L'interrupteur du Public, ses effets, ses joueurs rencontrés et bloqués.
    /// </summary>
    /// <remarks>
    /// En tête de page, avant les groupes privés : c'est le seul réglage de la
    /// page qui expose le joueur à des inconnus, et il doit se voir sans
    /// défiler. L'avertissement s'affiche à la première activation.
    /// </remarks>
    private void DrawPublic()
    {
        var @public = groups.Public;
        var enabled = @public is { Dormant: false };
        var toggled = enabled;

        if (ImGui.Checkbox("Public##public_toggle", ref toggled) && toggled != enabled)
        {
            if (toggled && actions.PublicWarningSeen() is false)
                _publicWarningOpen = true;
            else
                actions.SetPublic(toggled);
        }

        ImGui.SameLine();
        Text.Small(enabled
            ? "Activé : vous voyez les joueurs visibles qui l'ont activé, et ils vous voient."
            : "Désactivé : seuls vos pairs et vos groupes vous voient.");

        if (_publicWarningOpen)
            DrawPublicWarning();

        if (@public is null)
            return;

        var receive = @public.DefaultReceive;
        var animations = receive.Animations;
        var vfx = receive.Vfx;
        var sounds = receive.Sounds;

        Text.Small("Effets des joueurs du Public que vous n'avez pas réglés un par un :");

        var changed = ImGui.Checkbox("Animations##public_anim", ref animations);
        ImGui.SameLine();
        changed |= ImGui.Checkbox("VFX##public_vfx", ref vfx);
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Sons##public_sounds", ref sounds);

        if (changed)
            actions.SetDefaultReceive(PublicGroup.Id, new TransientCategories(animations, vfx, sounds));

        if (enabled && @public.Members.Count > 0 && ImGui.CollapsingHeader($"Joueurs rencontrés ({@public.Members.Count})##public_members"))
            DrawMembers(@public, GroupRole.Member, statuses());

        if (@public.Blocked.Count > 0)
            DrawBlocked(@public);

        ImGui.Dummy(Theme.S(0f, Theme.GapM));
    }

    private void DrawPublicWarning()
    {
        Text.Small(PublicWarning, Theme.Warning);

        if (Btn.Draw("Activer Public", BtnTone.Primary, BtnSize.Small, Icons.World, id: "public_confirm"))
        {
            actions.AcknowledgePublicWarning();
            actions.SetPublic(true);
            _publicWarningOpen = false;
        }

        ImGui.SameLine(0f, Theme.S(Theme.GapS));

        if (Btn.Draw("Annuler", BtnTone.Secondary, BtnSize.Small, Icons.Close, id: "public_cancel"))
            _publicWarningOpen = false;
    }

    private void DrawBlocked(GroupRecord @public)
    {
        Text.Small($"Bloqués ({@public.Blocked.Count})");

        foreach (var (ban, index) in @public.Blocked.Select((ban, index) => (ban, index)))
        {
            var name = ban.Fingerprint is { } print && @public.Members.TryGetValue(print, out var member)
                ? Glyphs.Safe(member.DisplayName)
                : "joueur bloqué";

            ImGui.TextColored(Theme.Text, name);
            ImGui.SameLine();

            if (Btn.Draw("Débloquer", BtnTone.Secondary, BtnSize.Small, Icons.Resume, id: $"unblock_{index}"))
                actions.Unblock(PublicGroup.Id, ban);
        }
    }
```

Dans la ligne d'un membre (`DrawMembers`), quand `group.IsPublic`, ajouter à côté de la pause un bouton « Bloquer » à deux clics, par le même mécanisme `_confirming` que l'exclusion (`DrawExclude` : en reprendre la forme exacte), avec l'infobulle « Bloquer : il ne vous verra plus et vous ne le verrez plus, dans le Public. », qui appelle `actions.Block(group.Id, member.Fingerprint)`.

Dans `DrawReceive`, pour un membre du Public dont `member.Receive` n'est pas nul, ajouter un petit bouton « Suivre le Public » qui appelle `actions.SetReceive(group.Id, member.Fingerprint, null)`.

`Icons.World` (globe) existe déjà : pas de nouvelle icône.

- [ ] **Step 4: Construire et déployer**

Run: `dotnet build Linkpearl/Linkpearl.csproj -c Release` puis `./scripts/deploy-plugin-dev.sh`
Expected: sans warning ; le plugin se recharge en jeu.

- [ ] **Step 5: Commit**

```bash
git add Linkpearl/Configuration.cs Linkpearl/Ui Linkpearl/Plugin.cs
git commit -m "feat(ui): interrupteur Public, blocage et motifs de bannissement"
```

---

### Task 10: Documentation, feuille de route, essais en jeu

**Files:**
- Modify: `docs/protocol.md`, `docs/reprise.md`, `README.md`, `README.fr.md`

- [ ] **Step 1: `docs/protocol.md`**

- Nouvelle section « Groupe Public », après « Groupes privés » : secret et `GroupId` figés (valeurs des contraintes globales), aucune clé ni politique, boîtes de présence comme un groupe privé, services de la configuration, modération locale (blocage par clé et empreinte, jamais transmis).
- Nouvelle section « Listes de bannissement » : trames `0x15` (`15 | page (2 BE)`) et `0x16` (`16 | page (2 BE) | pages (2 BE) | JSON UTF-8 d'une BanList`), 64 entrées par page, 64 pages au plus, 64 pages servies par connexion, fusion sous un même sel et un même coût, erreur « page hors de la liste » sans fermeture, et ce qu'un client fait d'un service qui ne les connaît pas (aucune liste, personne de banni).
- « Modèle de confiance » : dans le Public, le service sait tout de l'appartenance (qui l'a activé, qui se voit) ; une liste de bannissement relève de la réputation, pas de la preuve, et chaque service actif peut en imposer une.
- « Vecteurs figés » : secret et identifiant du Public, trames `bannissement-demande` et `bannissement-page`.

- [ ] **Step 2: `docs/reprise.md`**

- Point 3 de « Ce qui vient ensuite » : incrément 3 livré (date du jour), restent les essais en jeu.
- « Ce qui reste de mémoire » : retirer « Les échanges de fichiers de Penumbra ne partent pas » (livré par `9b0d069` sur `main`, rebasé dans cette branche) ; ajouter « Un service tiers d'avant les trames de bannissement ne bannit personne » et « La vérification d'un joueur visible coûte de 0,1 à 0,3 s de processeur par sel, une fois ».
- « Ce qui attend l'utilisateur en jeu » : Public à deux personnages, et un bannissement vu en jeu (voir l'étape 6).

- [ ] **Step 3: README**

Dans `README.fr.md` et `README.md`, section « Groupes » / « Groups » : un paragraphe Public (désactivé par défaut, ce qu'il expose, effets coupés par défaut, blocage), et une phrase sur les listes de bannissement des services. Le README anglais cite chaque libellé en français suivi de sa traduction : « Public », « Activer Public » (Enable Public), « Bloquer » (Block), « Débloquer » (Unblock), « Suivre le Public » (Follow Public), « banni par un service » (banned by a service). Garder les ancres `#getting-started` et `#premiers-pas`.

- [ ] **Step 4: Vérifier et commiter**

```bash
grep -rnP '\x{2014}' docs README.md README.fr.md Linkpearl | head   # aucun tiret cadratin
dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj
dotnet build Linkpearl/Linkpearl.csproj -c Release
git add docs README.md README.fr.md
git commit -m "docs(groupes): Public et listes de bannissement"
```

- [ ] **Step 5: Feuille de route (après accord de l'utilisateur)**

Sur le projet GitHub `LinkPearl-Sync/projects/3` : créer l'issue « Public group » (label `roadmap`, corps en anglais puis en français) si elle n'existe pas et la placer en In progress ; la carte « Groups » (point 15) reste en In progress jusqu'à la publication ; le point 10 (listes de bannissement) passe en Done et son issue se ferme **à la publication**, pas avant. Ce sont des actions visibles des joueurs : les proposer à l'utilisateur, ne pas les lancer seul.

- [ ] **Step 6: Essais en jeu (avec l'utilisateur)**

Déployer (`./scripts/deploy-plugin-dev.sh`), puis dérouler, à deux personnages sur une machine :

1. Public activé d'un seul côté : rien ne se passe. Des deux côtés : l'apparence arrive, sans animations ni VFX ni sons.
2. Réactiver les effets pour tout le Public : l'idle custom arrive. Couper pour un seul membre : il perd ses effets, les autres non. « Suivre le Public » le ramène au réglage commun.
3. Bloquer l'autre : l'apparence se retire. Désactiver puis réactiver Public : le blocage est toujours là. Débloquer.
4. Service local (le rendez-vous lancé en local, console sur `--admin-allow local`) : bannir un des deux personnages par la console, ajouter puis retirer un service pour forcer le téléchargement, vérifier la puce « banni par un service », l'apparence retirée et la demande de pairage ignorée.
