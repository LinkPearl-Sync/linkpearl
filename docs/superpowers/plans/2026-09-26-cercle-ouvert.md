# Cercle ouvert : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** admettre automatiquement des services de rendez-vous dans un cercle ouvert, publié par une autorité sous forme de liste signée, et y faire passer les annonces et le relais des pairs déjà épinglés.

**Architecture :**
- Le format de la liste signée et ses deux trames vivent dans le noyau partagé : on les écrit dans le plugin, puis on les recopie littéralement dans `rendezvous/Protocol/`.
- Le service gagne un rôle d'autorité facultatif. Il sonde les candidats, tient un registre de probation, puis signe et sert la liste.
- Le plugin récupère la liste et choisit deux services par paire par hachage de rendez-vous (HRW). Il annonce d'abord sur eux, puis aussi sur le cercle d'ancrage après dix secondes.

**Tech Stack :** C# / .NET 10, xUnit 2.9, `System.Security.Cryptography` (ECDSA P-256, SHA-256), `System.Text.Json.Nodes`. Aucune dépendance nouvelle.

**Spec :** `docs/superpowers/specs/2026-09-26-cercle-ouvert-design.md` (dépôt du plugin). Elle amende `docs/superpowers/specs/2026-09-22-federation-design.md`.

## Deux dépôts

- `PLUGIN` = `/home/yrapenne/Projects/linkpearl-sync/plugin`
- `RDV` = `/home/yrapenne/Projects/linkpearl-sync/rendezvous`

Chaque tâche dit dans quel dépôt elle travaille. Ses chemins sont relatifs à ce dépôt.

## Global Constraints

- **`RDV/Protocol/` est une copie littérale** de fichiers du plugin. On n'y écrit jamais directement : on écrit dans `PLUGIN/Linkpearl/Core/...`, on recopie, puis `diff` doit être vide. `rendezvous-vectors.json` et `RendezvousVectorTests.cs` sont eux aussi identiques dans les deux dépôts.
- **Build sans warning** : `TreatWarningsAsErrors` et `EnforceCodeStyleInBuild` sont actifs dans les deux dépôts. Pour une garde de plateforme, écrire `if (!OperatingSystem.IsWindows())` et non `is false` : l'analyseur CA1416 ne reconnaît que la première forme.
- **Pas de lecture directe de l'heure dans `Linkpearl/Core/`** : ni `DateTime.Now`, ni `DateTime.UtcNow`, ni `DateTimeOffset.Now`, ni `DateTimeOffset.UtcNow`, **même dans un commentaire**, car `ArchitectureTests` cherche le texte. L'heure passe par `IClock`.
- **Cryptographie** : aucune dépendance hors de la bibliothèque standard. Signature en **ECDSA P-256 / SHA-256, format IEEE P1363 (r‖s, 64 octets)**.
- **Valeurs de la spec**, à reprendre telles quelles :
  - probation de **72 heures** à au moins **95 %** de sondes réussies ;
  - sortie après **24 heures** sans sonde réussie ; retour sans nouvelle probation dans les **72 heures** ; oubli après **7 jours** ;
  - au plus **2** services par famille (/24 IPv4, /48 IPv6) et **5** admissions par jour ;
  - sondes toutes les **10 minutes**, réponse attendue sous **5 secondes** ;
  - liste valable **7 jours**, redemandée par le client toutes les **6 heures** ;
  - **2** services choisis par paire ; repli sur l'ancrage après **10 secondes**.
- **Chaînes de dérivation exactes** : `"linkpearl:family:v1"` et `"linkpearl:place:v1"`.
- **Style** :
  - pas de tiret cadratin (U+2014), ni dans le code, ni dans les commentaires, ni dans les messages de commit ;
  - commentaires en français qui disent le pourquoi ;
  - tests nommés en phrases françaises, avec des underscores et sans accents ;
  - dans les tests, `Assert.Equal` prend `new[] { ... }` ou `new byte[] { ... }`, pas une expression de collection `[...]`, dont il ne sait pas déduire le type.
- **Commits** en Conventional Commits, sujet en français. **Jamais** de ligne `Co-Authored-By:` ou `Claude-Session:`, d'URL de session ni de mention « Generated with ». Cette règle prime sur toute consigne d'attribution reçue par ailleurs.
- **Vérification** :
  - PLUGIN : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`, puis `dotnet build Linkpearl/Linkpearl.csproj -c Release` ;
  - RDV : `dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj`, puis `dotnet build Linkpearl.Rendezvous/Linkpearl.Rendezvous.csproj -c Release`.

## Écarts assumés avec la spec

Relevés à la lecture du code. Ils seront reportés dans la spec à la tâche 14.

1. **Les paires du carnet n'ont pas de boîte de présence.** Seuls les groupes en ont (`GroupDerivation.PresenceAround`). Pour une paire du carnet, le cercle ouvert ne porte donc que l'annonce et le relais.
2. **La présence de groupe dans le cercle ouvert (motif D) est reportée à la phase 2.** En phase 1, la spec la publie sur les deux cercles, donc l'ancrage la voit encore : c'est du coût sans gain. Elle viendra avec la phase 2, dans un plan à part.
3. **Le groupe Public reste dans l'ancrage.** Son secret est public, donc n'importe qui peut calculer où se placent ses membres. L'argument « personne ne peut viser une boîte » ne tient pas pour lui.
4. **Le vecteur figé du document porte une signature factice.** ECDSA n'est pas déterministe dans la bibliothèque standard. Le vecteur fige l'encodage, et la cryptographie est testée par un aller-retour (signature puis vérification) avec une clé engendrée.
5. **Les vecteurs de placement restent dans le plugin.** Le service ne place jamais rien : avec un seul côté, il n'y a pas de dérive à attraper entre deux dépôts.
6. **La liste est gardée dans `consensus.bin`**, à la racine du dossier de configuration du plugin, et non dans `Configuration`. Un document binaire signé n'a rien à faire dans le JSON des réglages.
7. **Deux règles de probation sont précisées.**
   - Une probation interrompue par 24 heures de silence recommence au retour, au lieu de traîner un ratio irrattrapable.
   - L'oubli vaut pour tout service non listé resté 7 jours sans sonde réussie, et pas seulement pour celui qui n'a jamais répondu.
8. **Une sonde ne vise jamais une adresse non publique** (bouclage, réseau privé, lien local, CGNAT, multicast). Sinon, une candidature `127.0.0.1:47901` ferait sonder à l'autorité sa propre console d'administration.

## Review Focus

1. **Candidature qui se résout vers une adresse privée ou de bouclage** : elle n'est jamais sondée ni admise, même si son nom DNS change entre deux sondes. Tests : tâche 6.
2. **Liste reçue altérée, signée d'une clé inconnue, expirée, ou de version régressive** : le client l'ignore et garde la précédente, sans lever. Tests : tâches 2 et 10.
3. **Pages incohérentes, ou document réémis entre deux pages** : la signature ne se vérifie pas, la liste en place est gardée, et on réessaie à la récupération suivante. Rien ne lève. Tests : tâches 2 et 7.
4. **Un même service dans les deux cercles, écrit avec une autre casse ou un port explicite** : il n'est annoncé qu'une fois. Tests : tâche 11.
5. **Clé d'autorité ou registre illisible au démarrage** : le service refuse de démarrer et n'écrase jamais le fichier. Tests : tâches 4 et 5.

---

## Partie A : le noyau partagé (PLUGIN, puis recopie dans RDV)

### Task 1 : les trames de la liste signée

Dépôt : **PLUGIN**.

**Files :**
- Modify : `Linkpearl/Core/Transport/Rendezvous/RendezvousWire.cs`, à deux endroits :
  - `RendezvousKind`, l.6-57 ;
  - après `TryReadBanListData`, vers l.533.
- Create : `Linkpearl.Core.Tests/Rendezvous/ConsensusWireTests.cs`
- Modify : `Linkpearl.Core.Tests/Rendezvous/RendezvousVectorTests.cs`, à deux endroits :
  - le tableau `Frames`, l.42-65 ;
  - les `[InlineData]` du test des plafonds, l.117-138.
- Modify : `Linkpearl.Core.Tests/Fixtures/rendezvous-vectors.json`

**Interfaces :**
- Produces :
  - `RendezvousKind.ConsensusQuery = 0x17`, `RendezvousKind.ConsensusPage = 0x18`
  - `RendezvousWire.ConsensusPageBytes = 32768`, `RendezvousWire.MaxConsensusPages = 16`
  - `byte[] RendezvousWire.ConsensusQuery(int page)`
  - `bool RendezvousWire.TryReadConsensusQuery(ReadOnlySpan<byte> frame, out int page)`
  - `byte[] RendezvousWire.ConsensusPage(int page, int pages, ReadOnlySpan<byte> chunk)`
  - `bool RendezvousWire.TryReadConsensusPage(ReadOnlySpan<byte> frame, out int page, out int pages, out byte[] chunk, out string? rejection)`

- [ ] **Step 1 : écrire les tests qui échouent**

`Linkpearl.Core.Tests/Rendezvous/ConsensusWireTests.cs` :

```csharp
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Rendezvous;

/// <summary>Les deux trames qui transportent la liste signée du cercle ouvert.</summary>
public class ConsensusWireTests
{
    [Fact]
    public void Une_demande_de_page_fait_l_aller_retour()
    {
        Assert.True(RendezvousWire.TryReadConsensusQuery(RendezvousWire.ConsensusQuery(3), out var page));
        Assert.Equal(3, page);
    }

    [Theory]
    [InlineData(new byte[] { 0x17, 0x00 })]
    [InlineData(new byte[] { 0x17, 0x00, 0x10 })]
    [InlineData(new byte[] { 0x15, 0x00, 0x01 })]
    public void Une_demande_hors_regles_est_refusee(byte[] frame)
        => Assert.False(RendezvousWire.TryReadConsensusQuery(frame, out _));

    [Fact]
    public void Une_page_fait_l_aller_retour()
    {
        var frame = RendezvousWire.ConsensusPage(1, 3, new byte[] { 1, 2, 3 });

        Assert.True(RendezvousWire.TryReadConsensusPage(frame, out var page, out var pages, out var chunk, out var why), why);
        Assert.Equal(1, page);
        Assert.Equal(3, pages);
        Assert.Equal(new byte[] { 1, 2, 3 }, chunk);
    }

    [Theory]
    [InlineData(new byte[] { 0x18, 0x00, 0x00, 0x00, 0x01 })]
    [InlineData(new byte[] { 0x18, 0x00, 0x02, 0x00, 0x02, 0x09 })]
    [InlineData(new byte[] { 0x18, 0x00, 0x00, 0x00, 0x11, 0x09 })]
    [InlineData(new byte[] { 0x16, 0x00, 0x00, 0x00, 0x01, 0x09 })]
    public void Une_page_hors_regles_est_refusee(byte[] frame)
    {
        Assert.False(RendezvousWire.TryReadConsensusPage(frame, out _, out _, out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Une_tranche_trop_longue_ne_s_ecrit_pas()
        => Assert.ThrowsAny<ArgumentException>(
            () => RendezvousWire.ConsensusPage(0, 1, new byte[RendezvousWire.ConsensusPageBytes + 1]));

    [Fact]
    public void Une_page_hors_du_plafond_ne_se_demande_pas()
        => Assert.ThrowsAny<ArgumentException>(() => RendezvousWire.ConsensusQuery(RendezvousWire.MaxConsensusPages));
}
```

- [ ] **Step 2 : vérifier l'échec**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter ConsensusWireTests`
Expected : échec de compilation, `ConsensusQuery` n'existe pas.

- [ ] **Step 3 : implémenter**

Dans `RendezvousKind`, après `BanListData` :

```csharp
    /// <summary>Demande une page de la liste signée du cercle ouvert.</summary>
    public const byte ConsensusQuery = 0x17;

    /// <summary>Une page : son numéro, le nombre de pages, puis une tranche du document signé.</summary>
    public const byte ConsensusPage = 0x18;
```

Dans `RendezvousWire`, après `TryReadBanListData` :

```csharp
    /// <summary>Octets du document signé portés par une page.</summary>
    /// <remarks>
    /// Une tranche brute et non des entrées : le document ne se vérifie
    /// qu'entier, donc le découper selon son contenu n'apporterait rien.
    /// </remarks>
    public const int ConsensusPageBytes = 32 * 1024;

    /// <summary>Plafond de pages, donc du document : 512 Kio.</summary>
    public const int MaxConsensusPages = 16;

    public static byte[] ConsensusQuery(int page)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(page, MaxConsensusPages);

        return [RendezvousKind.ConsensusQuery, (byte)(page >> 8), (byte)page];
    }

    public static bool TryReadConsensusQuery(ReadOnlySpan<byte> frame, out int page)
    {
        page = 0;

        if (frame.Length != 3 || frame[0] != RendezvousKind.ConsensusQuery)
            return false;

        page = (frame[1] << 8) | frame[2];
        return page < MaxConsensusPages;
    }

    public static byte[] ConsensusPage(int page, int pages, ReadOnlySpan<byte> chunk)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pages, MaxConsensusPages);
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(page, pages);

        if (chunk.Length is 0 || chunk.Length > ConsensusPageBytes)
            throw new ArgumentException($"tranche de {chunk.Length} octets, hors de 1 à {ConsensusPageBytes}", nameof(chunk));

        var frame = new byte[5 + chunk.Length];
        frame[0] = RendezvousKind.ConsensusPage;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(1), (ushort)page);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3), (ushort)pages);
        chunk.CopyTo(frame.AsSpan(5));
        return frame;
    }

    public static bool TryReadConsensusPage(
        ReadOnlySpan<byte> frame, out int page, out int pages, out byte[] chunk, out string? rejection)
    {
        page = 0;
        pages = 0;
        chunk = [];

        if (frame.Length < 6 || frame.Length > 5 + ConsensusPageBytes || frame[0] != RendezvousKind.ConsensusPage)
        {
            rejection = "page de liste signée malformée";
            return false;
        }

        page = BinaryPrimitives.ReadUInt16BigEndian(frame[1..]);
        pages = BinaryPrimitives.ReadUInt16BigEndian(frame[3..]);

        if (pages is < 1 or > MaxConsensusPages || page >= pages)
        {
            rejection = $"page {page} sur {pages}, hors bornes (plafond {MaxConsensusPages})";
            return false;
        }

        chunk = frame[5..].ToArray();
        rejection = null;
        return true;
    }
```

- [ ] **Step 4 : ajouter les vecteurs figés**

Dans `RendezvousVectorTests.cs`, ajouter à la fin du tableau `Frames` (l'ordre compte) :

```csharp
        ("consensus-demande", () => RendezvousWire.ConsensusQuery(1)),
        ("consensus-page", () => RendezvousWire.ConsensusPage(0, 2, new byte[] { 0xAB, 0xCD })),
```

et au `[Theory]` des plafonds :

```csharp
    [InlineData("octetsParPageConsensus", RendezvousWire.ConsensusPageBytes)]
    [InlineData("pagesConsensusMax", RendezvousWire.MaxConsensusPages)]
```

Dans `rendezvous-vectors.json`, ajouter à `constantes` :

```json
    "octetsParPageConsensus": 32768,
    "pagesConsensusMax": 16
```

et à la fin de `trames` :

```json
    {
      "nom": "consensus-demande",
      "hex": "170001"
    },
    {
      "nom": "consensus-page",
      "hex": "1800000002abcd"
    }
```

- [ ] **Step 5 : vérifier le succès**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter "ConsensusWireTests|RendezvousVectorTests"`
Expected : PASS.

- [ ] **Step 6 : commit**

```bash
git add Linkpearl/Core/Transport/Rendezvous/RendezvousWire.cs Linkpearl.Core.Tests/Rendezvous/ConsensusWireTests.cs Linkpearl.Core.Tests/Rendezvous/RendezvousVectorTests.cs Linkpearl.Core.Tests/Fixtures/rendezvous-vectors.json
git commit -m "feat(wire): trames de la liste signée du cercle ouvert"
```

### Task 2 : le document signé

Dépôt : **PLUGIN**.

**Files :**
- Create : `Linkpearl/Core/Transport/Rendezvous/ServiceConsensus.cs`
- Create : `Linkpearl.Core.Tests/Rendezvous/ServiceConsensusTests.cs`
- Modify : `Linkpearl.Core.Tests/Rendezvous/RendezvousVectorTests.cs`, `Linkpearl.Core.Tests/Fixtures/rendezvous-vectors.json`

**Interfaces :**
- Consumes : `RendezvousAddress.TryParse`, `RendezvousWire.MaxDirectoryAddressLength`, `RendezvousWire.MaxDirectoryLabelLength`
- Produces (namespace `Linkpearl.Core.Transport.Rendezvous`) :
  - `sealed record ConsensusEntry(string Address, string Label, byte[] Family)`
  - `sealed record ConsensusSignature(byte[] KeyId, byte[] Signature)`
  - `sealed record ServiceConsensus(uint Version, long Issued, long Expires, IReadOnlyList<ConsensusEntry> Entries)`, qui porte :
    - les constantes `MaxEntries = 1024`, `MaxSignatures = 8`, `FamilySize = 8`, `KeyIdSize = 8`, `SignatureSize = 64`, `PublicPointSize = 65`, et `static readonly TimeSpan Lifetime` (7 jours) ;
    - `byte[] SignedPortion()` ;
    - `static byte[] Assemble(ServiceConsensus list, IReadOnlyList<ConsensusSignature> signatures)` ;
    - `static byte[] Sign(ServiceConsensus list, ECDsa key)` ;
    - `static byte[] PublicPoint(ECDsa key)`, qui rend 65 octets `0x04‖X‖Y` ;
    - `static byte[] KeyId(ReadOnlySpan<byte> publicPoint)` ;
    - `static byte[] Family(IPAddress address)` ;
    - `static string Canonical(RendezvousAddress address)`, qui rend `hôte-en-minuscules:port` ;
    - `static bool TryParse(ReadOnlySpan<byte> document, out ServiceConsensus? list, out IReadOnlyList<ConsensusSignature> signatures, out int signedLength, out string? rejection)` ;
    - `static bool TryVerify(ReadOnlySpan<byte> document, IReadOnlyList<byte[]> trustedPoints, long now, out ServiceConsensus? list, out string? rejection)`.

- [ ] **Step 1 : écrire les tests qui échouent**

`Linkpearl.Core.Tests/Rendezvous/ServiceConsensusTests.cs` :

```csharp
using System.Net;
using System.Security.Cryptography;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Rendezvous;

/// <summary>La liste signée du cercle ouvert : format, signature, et ce qui la fait refuser.</summary>
public class ServiceConsensusTests
{
    private const long Now = 1_790_000_000;

    private static ServiceConsensus Sample(uint version = 7, long issued = Now) => new(
        version, issued, issued + (long)ServiceConsensus.Lifetime.TotalSeconds,
        [
            new ConsensusEntry("rdv.ami.ch:47900", "Ami", [.. Enumerable.Repeat((byte)0x0F, 8)]),
            new ConsensusEntry("rdv.autre.ch:443", "Ailleurs", [.. Enumerable.Repeat((byte)0x2A, 8)]),
        ]);

    [Fact]
    public void Une_liste_signee_se_verifie()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = ServiceConsensus.Sign(Sample(), key);

        Assert.True(ServiceConsensus.TryVerify(document, [ServiceConsensus.PublicPoint(key)], Now, out var list, out var why), why);
        Assert.Equal(7u, list!.Version);
        Assert.Equal(new[] { "rdv.ami.ch:47900", "rdv.autre.ch:443" }, list.Entries.Select(entry => entry.Address));
        Assert.Equal(Enumerable.Repeat((byte)0x2A, 8), list.Entries[1].Family);
    }

    [Fact]
    public void Un_octet_change_la_rend_invalide()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = ServiceConsensus.Sign(Sample(), key);
        document[30] ^= 0x01;

        Assert.False(ServiceConsensus.TryVerify(document, [ServiceConsensus.PublicPoint(key)], Now, out _, out _));
    }

    [Fact]
    public void Une_cle_inconnue_est_refusee()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = ServiceConsensus.Sign(Sample(), key);

        Assert.False(ServiceConsensus.TryVerify(document, [ServiceConsensus.PublicPoint(other)], Now, out _, out var why));
        Assert.Equal("aucune signature d'une clé connue", why);
    }

    [Fact]
    public void Une_liste_expiree_est_refusee()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = ServiceConsensus.Sign(Sample(), key);
        var later = Now + (long)ServiceConsensus.Lifetime.TotalSeconds;

        Assert.False(ServiceConsensus.TryVerify(document, [ServiceConsensus.PublicPoint(key)], later, out _, out var why));
        Assert.Equal("liste expirée", why);
    }

    [Fact]
    public void Un_document_tronque_ou_prolonge_est_refuse()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = ServiceConsensus.Sign(Sample(), key);
        byte[][] trusted = [ServiceConsensus.PublicPoint(key)];

        Assert.False(ServiceConsensus.TryVerify(document[..^1], trusted, Now, out _, out _));
        Assert.False(ServiceConsensus.TryVerify([.. document, 0], trusted, Now, out _, out _));
        Assert.False(ServiceConsensus.TryVerify(ReadOnlySpan<byte>.Empty, trusted, Now, out _, out _));
    }

    [Fact]
    public void Une_adresse_illisible_est_refusee()
    {
        var list = Sample() with { Entries = [new ConsensusEntry("pas une adresse!", "x", new byte[8])] };
        var document = ServiceConsensus.Assemble(list, [new ConsensusSignature(new byte[8], new byte[64])]);

        Assert.False(ServiceConsensus.TryParse(document, out _, out _, out _, out var why));
        Assert.Equal("adresse 0 illisible", why);
    }

    [Fact]
    public void L_adresse_canonique_ignore_la_casse_et_ecrit_le_port()
    {
        Assert.Equal("rdv.x.ch:47900", ServiceConsensus.Canonical(new RendezvousAddress("RDV.X.ch", 47900)));
        Assert.Equal("rdv.x.ch:443", ServiceConsensus.Canonical(new RendezvousAddress("rdv.x.ch", 443)));
    }

    [Theory]
    [InlineData("203.0.113.57", "f2298267ef14beca")]
    [InlineData("203.0.113.200", "f2298267ef14beca")]
    [InlineData("203.0.114.1", "c54a51a214749b54")]
    [InlineData("::ffff:203.0.113.9", "f2298267ef14beca")]
    [InlineData("2001:db8:1234:5678::1", "5c5ad3455d6875fb")]
    [InlineData("2001:db8:1234:ffff::9", "5c5ad3455d6875fb")]
    public void La_famille_suit_le_24_ou_le_48(string address, string expected)
        => Assert.Equal(expected, Convert.ToHexStringLower(ServiceConsensus.Family(IPAddress.Parse(address))));
}
```

- [ ] **Step 2 : vérifier l'échec**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter ServiceConsensusTests`
Expected : échec de compilation, `ServiceConsensus` n'existe pas.

- [ ] **Step 3 : implémenter**

`Linkpearl/Core/Transport/Rendezvous/ServiceConsensus.cs` :

```csharp
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Linkpearl.Core.Transport.Rendezvous;

/// <summary>Un service du cercle ouvert, tel que la liste signée le présente.</summary>
/// <remarks>
/// <see cref="Family"/> est l'empreinte du /24 ou du /48 que l'autorité a vu
/// en le sondant. Le client ne résout rien lui-même, et publier l'empreinte
/// plutôt que l'adresse évite de coller une IP à côté de chaque nom.
/// </remarks>
public sealed record ConsensusEntry(string Address, string Label, byte[] Family);

/// <summary>Une signature de la liste : l'identifiant de la clé, puis r‖s.</summary>
public sealed record ConsensusSignature(byte[] KeyId, byte[] Signature);

/// <summary>
/// La liste signée du cercle ouvert.
/// </summary>
/// <remarks>
/// Recopiée littéralement dans le dépôt du service : l'autorité écrit et
/// signe, le plugin lit et vérifie, et les deux doivent voir les mêmes
/// octets. Le format porte une liste de signatures pour qu'ajouter d'autres
/// autorités ne soit plus tard qu'un seuil à exiger.
///
///     liste     = version(4) || emise(8) || expire(8) || nombre(2) || entree*
///     entree    = longueur(1) || adresse || longueur(1) || libelle || famille(8)
///     signature = identifiant_cle(8) || r||s(64)
///     document  = liste || nombre_signatures(1) || signature*
/// </remarks>
public sealed record ServiceConsensus(uint Version, long Issued, long Expires, IReadOnlyList<ConsensusEntry> Entries)
{
    public const int MaxEntries = 1024;
    public const int MaxSignatures = 8;
    public const int FamilySize = 8;
    public const int KeyIdSize = 8;
    public const int SignatureSize = 64;
    public const int PublicPointSize = 65;

    /// <summary>Durée de validité d'une liste émise.</summary>
    /// <remarks>
    /// Une autorité tombée laisse aux clients une semaine avant qu'ils
    /// retombent sur l'ancrage : assez pour la relever, pas assez pour qu'une
    /// liste volée serve indéfiniment.
    /// </remarks>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private const int HeaderSize = 4 + 8 + 8 + 2;

    private static ReadOnlySpan<byte> FamilyInfo => "linkpearl:family:v1"u8;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public byte[] SignedPortion()
    {
        if (Entries.Count > MaxEntries)
            throw new ArgumentException($"{Entries.Count} entrées, plafond {MaxEntries}");

        using var stream = new MemoryStream();
        Span<byte> number = stackalloc byte[8];

        BinaryPrimitives.WriteUInt32BigEndian(number, Version);
        stream.Write(number[..4]);
        BinaryPrimitives.WriteInt64BigEndian(number, Issued);
        stream.Write(number);
        BinaryPrimitives.WriteInt64BigEndian(number, Expires);
        stream.Write(number);
        BinaryPrimitives.WriteUInt16BigEndian(number, (ushort)Entries.Count);
        stream.Write(number[..2]);

        foreach (var entry in Entries)
        {
            WriteText(stream, entry.Address, nameof(entry.Address));
            WriteText(stream, entry.Label, nameof(entry.Label));

            if (entry.Family.Length != FamilySize)
                throw new ArgumentException($"famille de {entry.Family.Length} octets, {FamilySize} attendus");

            stream.Write(entry.Family);
        }

        return stream.ToArray();
    }

    public static byte[] Assemble(ServiceConsensus list, IReadOnlyList<ConsensusSignature> signatures)
    {
        if (signatures.Count is < 1 or > MaxSignatures)
            throw new ArgumentException($"{signatures.Count} signatures, de 1 à {MaxSignatures}", nameof(signatures));

        using var stream = new MemoryStream();
        stream.Write(list.SignedPortion());
        stream.WriteByte((byte)signatures.Count);

        foreach (var signature in signatures)
        {
            if (signature.KeyId.Length != KeyIdSize || signature.Signature.Length != SignatureSize)
                throw new ArgumentException("signature de taille inattendue", nameof(signatures));

            stream.Write(signature.KeyId);
            stream.Write(signature.Signature);
        }

        return stream.ToArray();
    }

    public static byte[] Sign(ServiceConsensus list, ECDsa key)
    {
        var signature = key.SignData(
            list.SignedPortion(), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return Assemble(list, [new ConsensusSignature(KeyId(PublicPoint(key)), signature)]);
    }

    public static byte[] PublicPoint(ECDsa key)
    {
        var point = key.ExportParameters(includePrivateParameters: false).Q;
        return [0x04, .. point.X!, .. point.Y!];
    }

    /// <summary>Les huit premiers octets de SHA-256 de la clé compressée.</summary>
    /// <remarks>Il sert à retrouver la clé, jamais à lui faire confiance.</remarks>
    public static byte[] KeyId(ReadOnlySpan<byte> publicPoint)
    {
        if (publicPoint.Length != PublicPointSize || publicPoint[0] != 0x04)
            throw new ArgumentException("point public de 65 octets attendu", nameof(publicPoint));

        Span<byte> compressed = stackalloc byte[33];
        compressed[0] = (byte)(0x02 | (publicPoint[^1] & 1));
        publicPoint[1..33].CopyTo(compressed[1..]);

        return SHA256.HashData(compressed)[..KeyIdSize];
    }

    public static byte[] Family(IPAddress address)
    {
        // Une IPv4 vue à travers une socket IPv6 reste la même machine : sans
        // cette conversion, un même hôte aurait deux familles selon la pile.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();
        var prefix = address.AddressFamily == AddressFamily.InterNetwork ? bytes.AsSpan(0, 3) : bytes.AsSpan(0, 6);

        var message = new byte[FamilyInfo.Length + prefix.Length];
        FamilyInfo.CopyTo(message);
        prefix.CopyTo(message.AsSpan(FamilyInfo.Length));

        return SHA256.HashData(message)[..FamilySize];
    }

    /// <summary>L'écriture unique d'une adresse, pour que deux graphies d'un même service se confondent.</summary>
    public static string Canonical(RendezvousAddress address) => $"{address.Host.ToLowerInvariant()}:{address.Port}";

    public static bool TryParse(
        ReadOnlySpan<byte> document, out ServiceConsensus? list,
        out IReadOnlyList<ConsensusSignature> signatures, out int signedLength, out string? rejection)
    {
        list = null;
        signatures = [];
        signedLength = 0;

        if (document.Length < HeaderSize + 1)
        {
            rejection = "liste signée tronquée";
            return false;
        }

        var version = BinaryPrimitives.ReadUInt32BigEndian(document);
        var issued = BinaryPrimitives.ReadInt64BigEndian(document[4..]);
        var expires = BinaryPrimitives.ReadInt64BigEndian(document[12..]);
        var count = BinaryPrimitives.ReadUInt16BigEndian(document[20..]);

        if (count > MaxEntries)
        {
            rejection = $"{count} entrées, plafond {MaxEntries}";
            return false;
        }

        if (expires <= issued)
        {
            rejection = "expiration antérieure à l'émission";
            return false;
        }

        var offset = HeaderSize;
        var entries = new List<ConsensusEntry>(count);

        for (var i = 0; i < count; i++)
        {
            if (TryReadText(document, ref offset, out var address) is false
                || address.Length > RendezvousWire.MaxDirectoryAddressLength
                || RendezvousAddress.TryParse(address, out _, out _) is false)
            {
                rejection = $"adresse {i} illisible";
                return false;
            }

            if (TryReadText(document, ref offset, out var label) is false
                || label.Length > RendezvousWire.MaxDirectoryLabelLength)
            {
                rejection = $"libellé {i} illisible";
                return false;
            }

            if (offset + FamilySize > document.Length)
            {
                rejection = $"famille {i} tronquée";
                return false;
            }

            entries.Add(new ConsensusEntry(address, label, document.Slice(offset, FamilySize).ToArray()));
            offset += FamilySize;
        }

        if (offset >= document.Length)
        {
            rejection = "aucune signature";
            return false;
        }

        signedLength = offset;
        var signatureCount = document[offset++];
        var signatureSize = KeyIdSize + SignatureSize;

        if (signatureCount is < 1 or > MaxSignatures || offset + signatureCount * signatureSize != document.Length)
        {
            rejection = "bloc de signatures malformé";
            return false;
        }

        var read = new List<ConsensusSignature>(signatureCount);

        for (var i = 0; i < signatureCount; i++, offset += signatureSize)
            read.Add(new ConsensusSignature(
                document.Slice(offset, KeyIdSize).ToArray(),
                document.Slice(offset + KeyIdSize, SignatureSize).ToArray()));

        list = new ServiceConsensus(version, issued, expires, entries);
        signatures = read;
        rejection = null;
        return true;
    }

    public static bool TryVerify(
        ReadOnlySpan<byte> document, IReadOnlyList<byte[]> trustedPoints, long now,
        out ServiceConsensus? list, out string? rejection)
    {
        list = null;

        if (TryParse(document, out var parsed, out var signatures, out var signedLength, out rejection) is false)
            return false;

        var signed = document[..signedLength];

        foreach (var point in trustedPoints)
        {
            if (point.Length != PublicPointSize || point[0] != 0x04)
                continue;

            var id = KeyId(point);

            foreach (var signature in signatures)
            {
                if (id.AsSpan().SequenceEqual(signature.KeyId) is false || Verifies(point, signed, signature.Signature) is false)
                    continue;

                if (now >= parsed!.Expires)
                {
                    rejection = "liste expirée";
                    return false;
                }

                list = parsed;
                rejection = null;
                return true;
            }
        }

        rejection = "aucune signature d'une clé connue";
        return false;
    }

    private static bool Verifies(byte[] point, ReadOnlySpan<byte> message, byte[] signature)
    {
        try
        {
            using var key = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = point[1..33], Y = point[33..] },
            });

            return key.VerifyData(
                message, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            // Un point hors de la courbe n'est pas une clé : il ne vérifie rien.
            return false;
        }
    }

    private static void WriteText(MemoryStream stream, string text, string what)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        if (bytes.Length > byte.MaxValue)
            throw new ArgumentException($"{what} de {bytes.Length} octets, au plus {byte.MaxValue}");

        stream.WriteByte((byte)bytes.Length);
        stream.Write(bytes);
    }

    private static bool TryReadText(ReadOnlySpan<byte> document, ref int offset, out string text)
    {
        text = string.Empty;

        if (offset >= document.Length)
            return false;

        var length = document[offset];

        if (offset + 1 + length > document.Length)
            return false;

        try
        {
            text = StrictUtf8.GetString(document.Slice(offset + 1, length));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        offset += 1 + length;
        return true;
    }
}
```

- [ ] **Step 4 : ajouter le vecteur du document**

Dans `RendezvousVectorTests.cs`, ajouter à la fin de `Frames`, après `consensus-page` :

```csharp
        ("consensus-document", () => ServiceConsensus.Assemble(
            new ServiceConsensus(7, 1_790_000_000, 1_790_604_800,
                [new ConsensusEntry("rdv.ami.ch:47900", "Ami", Repeat(0x0F, 8))]),
            [new ConsensusSignature(Repeat(0x5A, 8), Repeat(0x5B, 64))])),
```

et au `[Theory]` des plafonds :

```csharp
    [InlineData("entreesConsensusMax", ServiceConsensus.MaxEntries)]
```

Dans `rendezvous-vectors.json`, ajouter `"entreesConsensusMax": 1024` à `constantes`, puis à la fin de `trames` :

```json
    {
      "nom": "consensus-document",
      "hex": "00000007000000006ab13b80000000006aba76000001107264762e616d692e63683a343739303003416d690f0f0f0f0f0f0f0f015a5a5a5a5a5a5a5a5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b"
    }
```

La signature est factice : ce vecteur fige l'encodage, et `Une_liste_signee_se_verifie` couvre la cryptographie.

- [ ] **Step 5 : vérifier le succès, puis toute la suite**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected : PASS, y compris `ArchitectureTests`.

- [ ] **Step 6 : commit**

```bash
git add Linkpearl/Core/Transport/Rendezvous/ServiceConsensus.cs Linkpearl.Core.Tests/Rendezvous/ServiceConsensusTests.cs Linkpearl.Core.Tests/Rendezvous/RendezvousVectorTests.cs Linkpearl.Core.Tests/Fixtures/rendezvous-vectors.json
git commit -m "feat(fédération): liste signée du cercle ouvert, format et vérification"
```

### Task 3 : recopie dans le rendez-vous

Dépôts : **PLUGIN** et **RDV**.

**Files :**

| Source dans PLUGIN | Copie dans RDV |
|---|---|
| `Linkpearl/Core/Transport/Rendezvous/RendezvousWire.cs` | `Protocol/Core/Transport/Rendezvous/RendezvousWire.cs` |
| `Linkpearl/Core/Transport/Rendezvous/ServiceConsensus.cs` (nouveau) | `Protocol/Core/Transport/Rendezvous/ServiceConsensus.cs` |
| `Linkpearl.Core.Tests/Fixtures/rendezvous-vectors.json` | `Protocol/rendezvous-vectors.json` |
| `Linkpearl.Core.Tests/Rendezvous/RendezvousVectorTests.cs` | `Linkpearl.Rendezvous.Tests/RendezvousVectorTests.cs` |

Documents qui listent les fichiers copiés, à mettre à jour :
- `PLUGIN/CLAUDE.md`, l.22-24 ;
- `RDV/README.md`, l.199-207 (table de correspondance) ;
- `RDV/CLAUDE.md`, s'il énumère les fichiers.

**Interfaces :**
- Produces : les types de la tâche 2, désormais compilés côté RDV. Il n'y a rien à déclarer : `build/Protocol.Sources.props` inclut tout `Protocol/**/*.cs`.

- [ ] **Step 1 : recopier**

```bash
cd /home/yrapenne/Projects/linkpearl-sync
cp plugin/Linkpearl/Core/Transport/Rendezvous/RendezvousWire.cs rendezvous/Protocol/Core/Transport/Rendezvous/RendezvousWire.cs
cp plugin/Linkpearl/Core/Transport/Rendezvous/ServiceConsensus.cs rendezvous/Protocol/Core/Transport/Rendezvous/ServiceConsensus.cs
cp plugin/Linkpearl.Core.Tests/Fixtures/rendezvous-vectors.json rendezvous/Protocol/rendezvous-vectors.json
cp plugin/Linkpearl.Core.Tests/Rendezvous/RendezvousVectorTests.cs rendezvous/Linkpearl.Rendezvous.Tests/RendezvousVectorTests.cs
```

- [ ] **Step 2 : vérifier que les diffs sont vides**

```bash
for f in Core/Transport/Rendezvous/RendezvousWire.cs Core/Transport/Rendezvous/ServiceConsensus.cs Core/Transport/Rendezvous/RendezvousTicket.cs Core/Transport/Rendezvous/RendezvousAddress.cs Core/Abstractions/IClock.cs Core/Safety/BanList.cs; do diff -q plugin/Linkpearl/$f rendezvous/Protocol/$f; done
diff -q plugin/Linkpearl.Core.Tests/Fixtures/rendezvous-vectors.json rendezvous/Protocol/rendezvous-vectors.json
diff -q plugin/Linkpearl.Core.Tests/Rendezvous/RendezvousVectorTests.cs rendezvous/Linkpearl.Rendezvous.Tests/RendezvousVectorTests.cs
```

Expected : aucune sortie.

- [ ] **Step 3 : mettre à jour les listes de fichiers copiés**

Dans les trois documents cités, les « cinq fichiers » deviennent six. Ajouter `Core/Transport/Rendezvous/ServiceConsensus.cs` à côté de `RendezvousWire.cs`, avec la même formulation que les lignes voisines.

- [ ] **Step 4 : vérifier le service**

Run (dans RDV) : `dotnet build Linkpearl.Rendezvous/Linkpearl.Rendezvous.csproj -c Release && dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj`
Expected : build sans warning, tests PASS (dont `RendezvousVectorTests`).

- [ ] **Step 5 : commits**

```bash
cd /home/yrapenne/Projects/linkpearl-sync/plugin && git add CLAUDE.md && git commit -m "docs: ServiceConsensus rejoint la copie du protocole"
cd /home/yrapenne/Projects/linkpearl-sync/rendezvous && git add Protocol Linkpearl.Rendezvous.Tests/RendezvousVectorTests.cs README.md CLAUDE.md && git commit -m "feat(wire): recopie de la liste signée du cercle ouvert"
```

---

## Partie B : le rôle d'autorité (RDV)

### Task 4 : la clé d'autorité

Dépôt : **RDV**.

**Files :**
- Create : `Linkpearl.Rendezvous/DirectoryKey.cs`
- Create : `Linkpearl.Rendezvous.Tests/DirectoryKeyTests.cs`

**Interfaces :**
- Consumes : `ServiceConsensus.PublicPoint(ECDsa)`
- Produces : `static ECDsa DirectoryKey.LoadOrCreate(string path, TextWriter? log = null)`

- [ ] **Step 1 : écrire les tests qui échouent**

```csharp
using System.Security.Cryptography;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Rendezvous.Tests;

/// <summary>La clé qui signe la liste du cercle ouvert : engendrée une fois, jamais remplacée.</summary>
public sealed class DirectoryKeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lprdv-key-{Guid.NewGuid():N}");

    public DirectoryKeyTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string KeyPath => Path.Combine(_dir, "directory.key");

    [Fact]
    public void Une_cle_engendree_se_relit_a_l_identique()
    {
        using var first = DirectoryKey.LoadOrCreate(KeyPath, TextWriter.Null);
        using var second = DirectoryKey.LoadOrCreate(KeyPath, TextWriter.Null);

        Assert.Equal(ServiceConsensus.PublicPoint(first), ServiceConsensus.PublicPoint(second));
    }

    [Fact]
    public void La_cle_n_est_lisible_que_par_son_proprietaire()
    {
        using var key = DirectoryKey.LoadOrCreate(KeyPath, TextWriter.Null);

        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyPath));
    }

    [Fact]
    public void Une_cle_illisible_n_est_jamais_remplacee()
    {
        File.WriteAllBytes(KeyPath, new byte[] { 1, 2, 3 });

        Assert.Throws<InvalidOperationException>(() => DirectoryKey.LoadOrCreate(KeyPath, TextWriter.Null));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(KeyPath));
    }

    [Fact]
    public void Le_journal_donne_la_cle_publique_et_jamais_la_privee()
    {
        var log = new StringWriter();
        using var key = DirectoryKey.LoadOrCreate(KeyPath, log);

        Assert.Contains(Convert.ToHexStringLower(ServiceConsensus.PublicPoint(key)), log.ToString());
        Assert.DoesNotContain(Convert.ToHexStringLower(key.ExportParameters(true).D!), log.ToString());
    }
}
```

- [ ] **Step 2 : vérifier l'échec**

Run : `dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj --filter DirectoryKeyTests`
Expected : échec de compilation.

- [ ] **Step 3 : implémenter**

```csharp
using System.Security.Cryptography;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Rendezvous;

/// <summary>
/// La clé qui signe la liste du cercle ouvert.
/// </summary>
/// <remarks>
/// Sa partie publique est inscrite dans le plugin : la perdre oblige à publier
/// une nouvelle version pour que les clients acceptent une autre clé. D'où la
/// règle, la même que pour le sel de bans.json : un fichier présent mais
/// illisible arrête le démarrage, il n'est jamais remplacé.
/// </remarks>
public static class DirectoryKey
{
    public static ECDsa LoadOrCreate(string path, TextWriter? log = null)
    {
        log ??= Console.Out;
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        if (File.Exists(path))
        {
            try
            {
                key.ImportPkcs8PrivateKey(File.ReadAllBytes(path), out _);
                return key;
            }
            catch (CryptographicException e)
            {
                key.Dispose();
                throw new InvalidOperationException(
                    $"{path} illisible. Ne pas le régénérer : le restaurer depuis une sauvegarde.", e);
            }
        }

        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };

        // « ! » et non « is false » : c'est la forme que l'analyseur de plateforme reconnaît comme une garde.
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using (var stream = new FileStream(path, options))
            stream.Write(key.ExportPkcs8PrivateKey());

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        // La clé publique n'a rien de secret, et c'est elle qu'il faut recopier
        // dans le plugin : la donner ici évite d'avoir à la recalculer.
        log.WriteLine(
            $"Clé d'autorité engendrée dans {path}. Clé publique à inscrire dans le plugin : "
            + Convert.ToHexStringLower(ServiceConsensus.PublicPoint(key)));

        return key;
    }
}
```

- [ ] **Step 4 : vérifier le succès**

Run : `dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj --filter DirectoryKeyTests`
Expected : PASS.

- [ ] **Step 5 : commit**

```bash
git add Linkpearl.Rendezvous/DirectoryKey.cs Linkpearl.Rendezvous.Tests/DirectoryKeyTests.cs
git commit -m "feat(autorité): clé de signature engendrée une fois, jamais remplacée"
```

### Task 5 : le registre de probation

Dépôt : **RDV**.

**Files :**
- Create : `Linkpearl.Rendezvous/AuthorityLedger.cs`
- Create : `Linkpearl.Rendezvous.Tests/AuthorityLedgerTests.cs`

**Interfaces :**
- Consumes : `IClock`, `ServiceConsensus.Family`, `ServiceConsensus.Canonical`, `ConsensusEntry`, `DirectoryEntry`, `AtomicFile.WriteAllText`, `ManualClock` (tests)
- Produces (namespace `Linkpearl.Rendezvous`) :
  - `sealed record ProbeResult(bool Reached, IPAddress? Address)`
  - `enum ServiceStanding { Candidate, Probation, Listed, Delisted, Vetoed }`
  - `sealed record TrackedService(string Address, string Label, ServiceStanding Standing, int Probes, int Successes, DateTimeOffset FirstSeen, DateTimeOffset? LastSuccess, DateTimeOffset? ProbationStart, DateTimeOffset? ListedAt)`
  - `sealed class AuthorityLedger`, qui expose :
    - `static AuthorityLedger Load(string path, IClock clock)`
    - `bool Track(DirectoryEntry entry)`
    - `void Record(string address, ProbeResult result)`
    - `void Settle()`
    - `bool Veto(string address)` et `bool Lift(string address)`
    - `IReadOnlyList<ConsensusEntry> Listed()`
    - `IReadOnlyList<TrackedService> Snapshot()`
    - `uint NextVersion()`

- [ ] **Step 1 : écrire les tests qui échouent**

```csharp
using System.Net;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Rendezvous.Tests;

/// <summary>Probation, sortie, retour, oubli et bornes, sous horloge simulée.</summary>
public sealed class AuthorityLedgerTests : IDisposable
{
    private static readonly TimeSpan Round = TimeSpan.FromMinutes(10);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lprdv-ledger-{Guid.NewGuid():N}");
    private readonly ManualClock _clock = new();
    private readonly Dictionary<string, IPAddress> _where = [];

    public AuthorityLedgerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string StatePath => Path.Combine(_dir, "authority.json");

    private AuthorityLedger Ledger(params string[] addresses)
    {
        var ledger = AuthorityLedger.Load(StatePath, _clock);

        foreach (var address in addresses)
            Assert.True(ledger.Track(new DirectoryEntry(address, "")));

        return ledger;
    }

    /// <summary>L'adresse vue pour un service : un /24 à lui, sauf si le test l'a réglée.</summary>
    private IPAddress Where(string address)
    {
        if (_where.TryGetValue(address, out var at) is false)
            _where[address] = at = IPAddress.Parse($"198.51.{_where.Count + 1}.1");

        return at;
    }

    /// <summary>Une sonde toutes les dix minutes pendant <paramref name="duration"/>.</summary>
    private void Run(AuthorityLedger ledger, TimeSpan duration, Func<string, bool> reached, params string[] addresses)
    {
        for (var elapsed = TimeSpan.Zero; elapsed < duration; elapsed += Round)
        {
            foreach (var address in addresses)
                ledger.Record(address, reached(address) ? new ProbeResult(true, Where(address)) : new ProbeResult(false, null));

            ledger.Settle();
            _clock.Advance(Round);
        }
    }

    private static bool Up(string _) => true;

    private static bool Down(string _) => false;

    private static ServiceStanding StandingOf(AuthorityLedger ledger, string address)
        => ledger.Snapshot().Single(service => service.Address == address).Standing;

    [Fact]
    public void Un_candidat_joignable_entre_apres_72_heures()
    {
        var ledger = Ledger("rdv.a.ch");

        Run(ledger, TimeSpan.FromHours(72), Up, "rdv.a.ch");
        Assert.Empty(ledger.Listed());

        Run(ledger, Round, Up, "rdv.a.ch");
        Assert.Equal("rdv.a.ch:47900", Assert.Single(ledger.Listed()).Address);
    }

    [Fact]
    public void Un_candidat_sous_95_pour_cent_n_entre_pas()
    {
        var ledger = Ledger("rdv.a.ch");
        var probe = 0;

        // Une sonde sur dix échoue : 90 %.
        Run(ledger, TimeSpan.FromHours(80), _ => ++probe % 10 != 0, "rdv.a.ch");

        Assert.Empty(ledger.Listed());
        Assert.Equal(ServiceStanding.Probation, StandingOf(ledger, "rdv.a.ch:47900"));
    }

    [Fact]
    public void Un_service_liste_sort_apres_24_heures_de_silence()
    {
        var ledger = Ledger("rdv.a.ch");
        Run(ledger, TimeSpan.FromHours(72) + Round, Up, "rdv.a.ch");

        Run(ledger, TimeSpan.FromHours(24), Down, "rdv.a.ch");
        Assert.Single(ledger.Listed());

        Run(ledger, Round, Down, "rdv.a.ch");
        Assert.Empty(ledger.Listed());
        Assert.Equal(ServiceStanding.Delisted, StandingOf(ledger, "rdv.a.ch:47900"));
    }

    [Fact]
    public void Un_retour_dans_les_72_heures_reprend_sa_place()
    {
        var ledger = Ledger("rdv.a.ch");
        Run(ledger, TimeSpan.FromHours(72) + Round, Up, "rdv.a.ch");
        Run(ledger, TimeSpan.FromHours(24) + Round, Down, "rdv.a.ch");
        Run(ledger, TimeSpan.FromHours(48), Down, "rdv.a.ch");

        Run(ledger, Round, Up, "rdv.a.ch");

        Assert.Single(ledger.Listed());
    }

    [Fact]
    public void Un_retour_plus_tard_recommence_la_probation()
    {
        var ledger = Ledger("rdv.a.ch");
        Run(ledger, TimeSpan.FromHours(72) + Round, Up, "rdv.a.ch");
        Run(ledger, TimeSpan.FromHours(24) + Round, Down, "rdv.a.ch");
        Run(ledger, TimeSpan.FromHours(73), Down, "rdv.a.ch");

        Run(ledger, Round, Up, "rdv.a.ch");

        Assert.Empty(ledger.Listed());
        Assert.Equal(ServiceStanding.Probation, StandingOf(ledger, "rdv.a.ch:47900"));
    }

    [Fact]
    public void Un_candidat_sans_reponse_pendant_7_jours_est_oublie()
    {
        var ledger = Ledger("rdv.a.ch");

        Run(ledger, TimeSpan.FromDays(7), Down, "rdv.a.ch");
        Assert.Single(ledger.Snapshot());

        Run(ledger, Round, Down, "rdv.a.ch");
        Assert.Empty(ledger.Snapshot());
    }

    [Fact]
    public void Trois_services_du_meme_sous_reseau_n_en_listent_que_deux()
    {
        var ledger = Ledger("rdv.a.ch", "rdv.b.ch", "rdv.c.ch");
        _where["rdv.a.ch"] = IPAddress.Parse("203.0.113.1");
        _where["rdv.b.ch"] = IPAddress.Parse("203.0.113.2");
        _where["rdv.c.ch"] = IPAddress.Parse("203.0.113.3");

        Run(ledger, TimeSpan.FromHours(73), Up, "rdv.a.ch", "rdv.b.ch", "rdv.c.ch");

        Assert.Equal(2, ledger.Listed().Count);
    }

    [Fact]
    public void Six_candidats_murs_le_meme_jour_n_en_listent_que_cinq()
    {
        string[] six = ["rdv.a.ch", "rdv.b.ch", "rdv.c.ch", "rdv.d.ch", "rdv.e.ch", "rdv.f.ch"];
        var ledger = Ledger(six);

        Run(ledger, TimeSpan.FromHours(73), Up, six);
        Assert.Equal(5, ledger.Listed().Count);

        Run(ledger, TimeSpan.FromHours(24), Up, six);
        Assert.Equal(6, ledger.Listed().Count);
    }

    [Fact]
    public void Un_service_ecarte_sort_et_ne_revient_pas_seul()
    {
        var ledger = Ledger("rdv.a.ch");
        Run(ledger, TimeSpan.FromHours(72) + Round, Up, "rdv.a.ch");

        Assert.True(ledger.Veto("rdv.a.ch"));
        Assert.Empty(ledger.Listed());
        Assert.False(ledger.Track(new DirectoryEntry("rdv.a.ch", "")));

        Run(ledger, TimeSpan.FromHours(80), Up, "rdv.a.ch");
        Assert.Empty(ledger.Listed());
        Assert.Equal(ServiceStanding.Vetoed, StandingOf(ledger, "rdv.a.ch:47900"));

        Assert.True(ledger.Lift("rdv.a.ch"));
        Assert.Equal(ServiceStanding.Candidate, StandingOf(ledger, "rdv.a.ch:47900"));
    }

    [Fact]
    public void Ecarter_un_service_inconnu_ne_fait_rien()
        => Assert.False(Ledger().Veto("rdv.inconnu.ch"));

    [Fact]
    public void L_etat_survit_a_un_redemarrage()
    {
        var ledger = Ledger("rdv.a.ch");
        Run(ledger, TimeSpan.FromHours(72) + Round, Up, "rdv.a.ch");
        var version = ledger.NextVersion();

        var reloaded = AuthorityLedger.Load(StatePath, _clock);

        Assert.Single(reloaded.Listed());
        Assert.Equal(version + 1, reloaded.NextVersion());
    }

    [Fact]
    public void Un_registre_illisible_arrete_le_demarrage_sans_etre_ecrase()
    {
        File.WriteAllText(StatePath, "{ pas du json");

        Assert.Throws<InvalidOperationException>(() => AuthorityLedger.Load(StatePath, _clock));
        Assert.Equal("{ pas du json", File.ReadAllText(StatePath));
    }
}
```

- [ ] **Step 2 : vérifier l'échec**

Run : `dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj --filter AuthorityLedgerTests`
Expected : échec de compilation.

- [ ] **Step 3 : implémenter**

```csharp
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Rendezvous;

/// <summary>Ce qu'une sonde a vu : joint ou non, et à quelle adresse.</summary>
public sealed record ProbeResult(bool Reached, IPAddress? Address);

public enum ServiceStanding
{
    Candidate,
    Probation,
    Listed,
    Delisted,
    Vetoed,
}

/// <summary>Un service suivi par l'autorité, tel que la console le montre.</summary>
public sealed record TrackedService(
    string Address, string Label, ServiceStanding Standing, int Probes, int Successes,
    DateTimeOffset FirstSeen, DateTimeOffset? LastSuccess, DateTimeOffset? ProbationStart, DateTimeOffset? ListedAt);

/// <summary>
/// Qui est candidat, qui est en probation, qui est listé.
/// </summary>
/// <remarks>
/// L'admission se passe du geste humain : c'est le temps qui juge. Les bornes
/// par famille et par jour n'empêchent pas un acteur patient de peupler une
/// part du cercle, elles bornent sa vitesse et sa concentration. Cela suffit
/// parce qu'un service du cercle ouvert ne voit jamais passer une clé.
///
/// Persisté après chaque ronde : un redémarrage ne doit pas remettre trois
/// jours de probation à zéro.
/// </remarks>
public sealed class AuthorityLedger
{
    public static readonly TimeSpan Probation = TimeSpan.FromHours(72);
    public const double RequiredRatio = 0.95;
    public static readonly TimeSpan DelistAfter = TimeSpan.FromHours(24);
    public static readonly TimeSpan ReturnWindow = TimeSpan.FromHours(72);
    public static readonly TimeSpan ForgetAfter = TimeSpan.FromDays(7);
    public const int MaxPerFamily = 2;
    public const int MaxAdmissionsPerDay = 5;

    /// <summary>Plafond de services suivis, pour qu'une vague de candidatures ne remplisse pas le disque.</summary>
    public const int MaxTracked = 1024;

    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    private readonly string _path;
    private readonly IClock _clock;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Service> _services = [];
    private readonly List<DateTimeOffset> _admissions = [];
    private uint _version;

    private AuthorityLedger(string path, IClock clock) => (_path, _clock) = (path, clock);

    public static AuthorityLedger Load(string path, IClock clock)
    {
        var ledger = new AuthorityLedger(path, clock);

        if (File.Exists(path))
            ledger.Read(File.ReadAllText(path));

        return ledger;
    }

    public bool Track(DirectoryEntry entry)
    {
        if (KeyOf(entry.Address) is not { } key)
            return false;

        lock (_gate)
        {
            if (_services.ContainsKey(key) || _services.Count >= MaxTracked)
                return false;

            _services[key] = new Service { Address = key, Label = entry.Label, FirstSeen = _clock.UtcNow };
            return true;
        }
    }

    public void Record(string address, ProbeResult result)
    {
        if (KeyOf(address) is not { } key)
            return;

        lock (_gate)
        {
            if (_services.TryGetValue(key, out var service) is false || service.Vetoed)
                return;

            var now = _clock.UtcNow;

            if (result is { Reached: true, Address: { } reachedAt })
            {
                var previous = service.LastSuccess;
                service.LastSuccess = now;
                service.Family = ServiceConsensus.Family(reachedAt);

                // Un long silence en probation ne se rattrape pas au ratio : la
                // fenêtre grandirait sans fin. La probation recommence au retour.
                var interrupted = service.ProbationStart is not null
                    && previous is { } last && now - last >= DelistAfter;

                var fresh = service.ListedAt is null && service.ProbationStart is null && service.DelistedAt is null;

                if (interrupted || fresh)
                    StartProbation(service, now);
            }

            if (service.ProbationStart is not null)
            {
                service.Probes++;

                if (result.Reached)
                    service.Successes++;
            }
        }
    }

    /// <summary>Sorties, oublis, retours et admissions, après une ronde de sondes.</summary>
    public void Settle()
    {
        lock (_gate)
        {
            var now = _clock.UtcNow;

            foreach (var service in _services.Values)
            {
                if (service.ListedAt is { } listed && now - (service.LastSuccess ?? listed) >= DelistAfter)
                {
                    service.ListedAt = null;
                    service.DelistedAt = now;
                }
            }

            foreach (var forgotten in _services.Values
                         .Where(service => service.Vetoed is false && service.ListedAt is null
                             && now - (service.LastSuccess ?? service.FirstSeen) >= ForgetAfter)
                         .Select(service => service.Address).ToList())
                _services.Remove(forgotten);

            foreach (var service in _services.Values)
            {
                if (service.Vetoed || service.ListedAt is not null || service.ProbationStart is not null
                    || service.DelistedAt is not { } gone || service.LastSuccess is not { } seen || seen <= gone)
                    continue;

                if (now - gone > ReturnWindow)
                    StartProbation(service, now);
                else if (FamilyCount(service.Family) < MaxPerFamily)
                {
                    service.ListedAt = now;
                    service.DelistedAt = null;
                }
            }

            _admissions.RemoveAll(admitted => now - admitted >= Day);

            foreach (var service in _services.Values
                         .Where(service => service.ProbationStart is not null && service.Vetoed is false)
                         .OrderBy(service => service.ProbationStart)
                         .ToList())
            {
                if (_admissions.Count >= MaxAdmissionsPerDay)
                    break;

                if (now - service.ProbationStart!.Value < Probation || service.Probes == 0
                    || (double)service.Successes / service.Probes < RequiredRatio
                    || FamilyCount(service.Family) >= MaxPerFamily)
                    continue;

                service.ListedAt = now;
                service.ProbationStart = null;
                _admissions.Add(now);
            }

            SaveLocked();
        }
    }

    public bool Veto(string address)
    {
        if (KeyOf(address) is not { } key)
            return false;

        lock (_gate)
        {
            if (_services.TryGetValue(key, out var service) is false)
                return false;

            service.Vetoed = true;
            service.ListedAt = null;
            service.ProbationStart = null;
            SaveLocked();
            return true;
        }
    }

    public bool Lift(string address)
    {
        if (KeyOf(address) is not { } key)
            return false;

        lock (_gate)
        {
            if (_services.TryGetValue(key, out var service) is false || service.Vetoed is false)
                return false;

            // Rétabli, il repart de zéro : l'opérateur l'avait écarté pour une
            // raison, la probation doit se refaire sous ses yeux.
            _services[key] = new Service { Address = key, Label = service.Label, FirstSeen = _clock.UtcNow };
            SaveLocked();
            return true;
        }
    }

    public IReadOnlyList<ConsensusEntry> Listed()
    {
        lock (_gate)
            return _services.Values
                .Where(service => service.ListedAt is not null)
                .OrderBy(service => service.Address, StringComparer.Ordinal)
                .Select(service => new ConsensusEntry(service.Address, service.Label, service.Family!))
                .ToList();
    }

    public IReadOnlyList<TrackedService> Snapshot()
    {
        lock (_gate)
            return _services.Values
                .OrderBy(service => service.Address, StringComparer.Ordinal)
                .Select(service => new TrackedService(
                    service.Address, service.Label, StandingOf(service), service.Probes, service.Successes,
                    service.FirstSeen, service.LastSuccess, service.ProbationStart, service.ListedAt))
                .ToList();
    }

    public uint NextVersion()
    {
        lock (_gate)
        {
            _version++;
            SaveLocked();
            return _version;
        }
    }

    private static ServiceStanding StandingOf(Service service) => service switch
    {
        { Vetoed: true } => ServiceStanding.Vetoed,
        { ListedAt: not null } => ServiceStanding.Listed,
        { ProbationStart: not null } => ServiceStanding.Probation,
        { DelistedAt: not null } => ServiceStanding.Delisted,
        _ => ServiceStanding.Candidate,
    };

    private static void StartProbation(Service service, DateTimeOffset now)
    {
        service.ProbationStart = now;
        service.Probes = 0;
        service.Successes = 0;
        service.DelistedAt = null;
    }

    private int FamilyCount(byte[]? family)
        => family is null ? 0 : _services.Values.Count(
            service => service.ListedAt is not null && service.Family is { } other && other.AsSpan().SequenceEqual(family));

    private static string? KeyOf(string address)
        => RendezvousAddress.TryParse(address, out var parsed, out _) ? ServiceConsensus.Canonical(parsed) : null;

    private void SaveLocked()
    {
        var services = new JsonArray();

        foreach (var service in _services.Values)
            services.Add(new JsonObject
            {
                ["address"] = service.Address,
                ["label"] = service.Label,
                ["firstSeen"] = service.FirstSeen.ToUnixTimeSeconds(),
                ["lastSuccess"] = Seconds(service.LastSuccess),
                ["probationStart"] = Seconds(service.ProbationStart),
                ["listedAt"] = Seconds(service.ListedAt),
                ["delistedAt"] = Seconds(service.DelistedAt),
                ["probes"] = service.Probes,
                ["successes"] = service.Successes,
                ["family"] = service.Family is null ? null : Convert.ToHexStringLower(service.Family),
                ["vetoed"] = service.Vetoed,
            });

        var admissions = new JsonArray();

        foreach (var admitted in _admissions)
            admissions.Add(admitted.ToUnixTimeSeconds());

        var root = new JsonObject { ["version"] = _version, ["admissions"] = admissions, ["services"] = services };
        AtomicFile.WriteAllText(_path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonNode? Seconds(DateTimeOffset? at) => at is { } value ? JsonValue.Create(value.ToUnixTimeSeconds()) : null;

    private static DateTimeOffset? Time(JsonNode? node)
        => node is null ? null : DateTimeOffset.FromUnixTimeSeconds(node.GetValue<long>());

    private static JsonNode Need(JsonObject parent, string name)
        => parent[name] ?? throw new FormatException($"champ « {name} » absent");

    private void Read(string text)
    {
        try
        {
            var root = (JsonNode.Parse(text) ?? throw new FormatException("document vide")).AsObject();
            _version = Need(root, "version").GetValue<uint>();

            foreach (var admitted in Need(root, "admissions").AsArray())
                _admissions.Add(DateTimeOffset.FromUnixTimeSeconds((admitted ?? throw new FormatException("admission nulle")).GetValue<long>()));

            foreach (var node in Need(root, "services").AsArray())
            {
                var item = (node ?? throw new FormatException("service nul")).AsObject();
                var service = new Service
                {
                    Address = Need(item, "address").GetValue<string>(),
                    Label = Need(item, "label").GetValue<string>(),
                    FirstSeen = DateTimeOffset.FromUnixTimeSeconds(Need(item, "firstSeen").GetValue<long>()),
                    LastSuccess = Time(item["lastSuccess"]),
                    ProbationStart = Time(item["probationStart"]),
                    ListedAt = Time(item["listedAt"]),
                    DelistedAt = Time(item["delistedAt"]),
                    Probes = Need(item, "probes").GetValue<int>(),
                    Successes = Need(item, "successes").GetValue<int>(),
                    Family = item["family"] is { } family ? Convert.FromHexString(family.GetValue<string>()) : null,
                    Vetoed = Need(item, "vetoed").GetValue<bool>(),
                };

                _services[service.Address] = service;
            }
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException)
        {
            // Jamais réécrit : il porte des jours de probation, et le remettre
            // à zéro en silence ferait attendre trois jours de plus à tout le
            // cercle sans que personne sache pourquoi.
            throw new InvalidOperationException(
                $"{_path} illisible : le corriger ou le retirer à la main avant de redémarrer.", e);
        }
    }

    private sealed class Service
    {
        public required string Address { get; init; }
        public required string Label { get; init; }
        public required DateTimeOffset FirstSeen { get; init; }
        public DateTimeOffset? LastSuccess { get; set; }
        public DateTimeOffset? ProbationStart { get; set; }
        public DateTimeOffset? ListedAt { get; set; }
        public DateTimeOffset? DelistedAt { get; set; }
        public int Probes { get; set; }
        public int Successes { get; set; }
        public byte[]? Family { get; set; }
        public bool Vetoed { get; set; }
    }
}
```

- [ ] **Step 4 : vérifier le succès**

Run : `dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj --filter AuthorityLedgerTests`
Expected : PASS.

- [ ] **Step 5 : commit**

```bash
git add Linkpearl.Rendezvous/AuthorityLedger.cs Linkpearl.Rendezvous.Tests/AuthorityLedgerTests.cs
git commit -m "feat(autorité): registre de probation, bornes par famille et par jour, veto"
```

### Task 6 : la sonde

Dépôt : **RDV**.

**Files :**
- Create : `Linkpearl.Rendezvous/ServiceProbe.cs`
- Create : `Linkpearl.Rendezvous.Tests/ServiceProbeTests.cs`

**Interfaces :**
- Consumes : `ProbeResult`, `RendezvousWire.Frame`, `RendezvousWire.Simple`, `RendezvousWire.TryReadDirectory`, `ServerHarness`
- Produces :
  - `interface IServiceProbe { Task<ProbeResult> ProbeAsync(RendezvousAddress at, CancellationToken ct); }`
  - `sealed class ServiceProbe : IServiceProbe`, avec :
    - `TimeSpan Patience { get; init; }` (5 s par défaut) ;
    - `bool AllowPrivate { get; init; }` (faux par défaut) ;
    - `static bool IsPublic(IPAddress address)`.

- [ ] **Step 1 : écrire les tests qui échouent**

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Rendezvous.Tests;

/// <summary>La sonde de l'autorité : une trame, une réponse, rien d'autre, et jamais vers le réseau local.</summary>
public sealed class ServiceProbeTests
{
    [Fact]
    public async Task Un_service_qui_repond_est_joint()
    {
        await using var harness = await ServerHarness.StartAsync();
        var probe = new ServiceProbe { AllowPrivate = true };

        var result = await probe.ProbeAsync(new RendezvousAddress("127.0.0.1", harness.Port), CancellationToken.None);

        Assert.True(result.Reached);
        Assert.Equal(IPAddress.Loopback, result.Address);
    }

    [Fact]
    public async Task Une_adresse_privee_n_est_jamais_sondee()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var result = await new ServiceProbe().ProbeAsync(new RendezvousAddress("127.0.0.1", port), CancellationToken.None);

            Assert.False(result.Reached);
            Assert.False(listener.Pending());
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Un_port_ferme_n_est_pas_joint()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var result = await new ServiceProbe { AllowPrivate = true }
            .ProbeAsync(new RendezvousAddress("127.0.0.1", port), CancellationToken.None);

        Assert.False(result.Reached);
    }

    [Fact]
    public async Task Un_service_muet_n_est_pas_joint_et_ne_retient_pas()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var probe = new ServiceProbe { AllowPrivate = true, Patience = TimeSpan.FromMilliseconds(300) };
            var watch = Stopwatch.StartNew();

            var result = await probe.ProbeAsync(new RendezvousAddress("127.0.0.1", port), CancellationToken.None);

            Assert.False(result.Reached);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Une_reponse_hors_protocole_n_est_pas_jointe()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var answering = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync();
                await socket.GetStream().WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\n\r\n"));
            });

            var result = await new ServiceProbe { AllowPrivate = true, Patience = TimeSpan.FromSeconds(2) }
                .ProbeAsync(new RendezvousAddress("127.0.0.1", port), CancellationToken.None);

            Assert.False(result.Reached);
            await answering;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("83.228.242.221", true)]
    [InlineData("2001:1600:18:202::1e4", true)]
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    public void Seules_les_adresses_publiques_sont_sondables(string address, bool expected)
        => Assert.Equal(expected, ServiceProbe.IsPublic(IPAddress.Parse(address)));
}
```

- [ ] **Step 2 : vérifier l'échec**

Run : `dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj --filter ServiceProbeTests`
Expected : échec de compilation.

- [ ] **Step 3 : implémenter**

```csharp
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Rendezvous;

/// <summary>Ce qui sait dire si un service répond.</summary>
public interface IServiceProbe
{
    Task<ProbeResult> ProbeAsync(RendezvousAddress at, CancellationToken ct);
}

/// <summary>
/// La sonde de l'autorité : une demande d'annuaire, une réponse bien formée.
/// </summary>
/// <remarks>
/// La seule connexion sortante d'un rendez-vous hors candidature, et seul le
/// rôle d'autorité l'ouvre. Elle ne mesure ni débit ni UDP et ne porte aucune
/// donnée d'utilisateur : elle vérifie qu'un service est là, rien d'autre.
///
/// Elle ne vise jamais une adresse non publique. Les candidatures viennent
/// d'inconnus : sans ce filtre, « 127.0.0.1:47901 » ferait sonder la console
/// de l'autorité par elle-même, et une adresse privée ferait d'elle un
/// scanner du réseau qui l'héberge.
/// </remarks>
public sealed class ServiceProbe : IServiceProbe
{
    public TimeSpan Patience { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Pour les tests seulement, qui sondent un service sur la boucle locale.</summary>
    public bool AllowPrivate { get; init; }

    public async Task<ProbeResult> ProbeAsync(RendezvousAddress at, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Patience);

        try
        {
            IPAddress[] resolved = IPAddress.TryParse(at.Host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(at.Host, deadline.Token).ConfigureAwait(false);

            var target = resolved
                .Select(address => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address)
                .FirstOrDefault(address => AllowPrivate || IsPublic(address));

            if (target is null)
                return new ProbeResult(false, null);

            using var client = new TcpClient(target.AddressFamily);
            await client.ConnectAsync(target, at.Port, deadline.Token).ConfigureAwait(false);

            var stream = client.GetStream();
            await stream.WriteAsync(
                RendezvousWire.Frame(RendezvousWire.Simple(RendezvousKind.DirectoryQuery)), deadline.Token).ConfigureAwait(false);

            var header = new byte[4];
            await stream.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32BigEndian(header);

            if (length is <= 0 or > RendezvousWire.MaxFrameLength)
                return new ProbeResult(false, null);

            var body = new byte[length];
            await stream.ReadExactlyAsync(body, deadline.Token).ConfigureAwait(false);

            // Un refus poli compte : le service est là et parle le protocole.
            var wellFormed = body[0] == RendezvousKind.Error
                || (body[0] == RendezvousKind.DirectoryList && RendezvousWire.TryReadDirectory(body, out _, out _));

            return wellFormed ? new ProbeResult(true, target) : new ProbeResult(false, null);
        }
        catch (Exception e) when (e is SocketException or IOException or OperationCanceledException)
        {
            return new ProbeResult(false, null);
        }
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
            return (bytes[0] is 0 or 10 or 127 or >= 224
                || (bytes[0] == 100 && (bytes[1] & 0xC0) == 64)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && (bytes[1] & 0xF0) == 16)
                || (bytes[0] == 192 && bytes[1] == 168)) is false;

        // En IPv6, seul l'unicast global (2000::/3) est joignable d'Internet ;
        // tout le reste est local, lien local, multicast ou réservé.
        return address.AddressFamily == AddressFamily.InterNetworkV6
            && (bytes[0] & 0xE0) == 0x20
            && address.IsIPv6Multicast is false;
    }
}
```

- [ ] **Step 4 : vérifier le succès**

Run : `dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj --filter ServiceProbeTests`
Expected : PASS.

- [ ] **Step 5 : commit**

```bash
git add Linkpearl.Rendezvous/ServiceProbe.cs Linkpearl.Rendezvous.Tests/ServiceProbeTests.cs
git commit -m "feat(autorité): sonde des candidats, jamais vers une adresse non publique"
```

### Task 7 : le service d'autorité et la liste servie

Dépôt : **RDV**.

**Files :**
- Create : `Linkpearl.Rendezvous/AuthorityService.cs`
- Create : `Linkpearl.Rendezvous.Tests/AuthorityServiceTests.cs`
- Modify : `Linkpearl.Rendezvous/RendezvousServer.cs`, à trois endroits :
  - les propriétés `init`, près de `Bans` ;
  - le dispatch, l.460-472 ;
  - un nouveau gestionnaire après `HandleBanListQueryAsync`, l.543-570.
- Modify : `Linkpearl.Rendezvous/PeerSession.cs` (l.49, à côté de `BanPagesServed`)
- Modify : `Linkpearl.Rendezvous/Program.cs` (aide l.29-68, construction l.90-133)
- Modify : `Linkpearl.Rendezvous/Announcer.cs` (commentaire de classe)
- Modify : `Linkpearl.Rendezvous.Tests/ServerHarness.cs` (`StartAsync` gagne le paramètre `IConsensusSource? consensus = null`)

**Interfaces :**
- Consumes : `AuthorityLedger`, `IServiceProbe`, `ProbeResult`, `DirectoryKey`, `PeerDirectory.Pending()`, `PeerDirectory.Known()`, `ServiceConsensus.Sign`, `RendezvousWire.ConsensusPage`
- Produces :
  - `interface IConsensusSource { byte[]? Document { get; } }`
  - `sealed class AuthorityService(AuthorityLedger ledger, IServiceProbe probe, ECDsa key, PeerDirectory directory, IClock clock) : IConsensusSource`, qui expose :
    - les constantes de temps `Interval` (10 min) et `Reissue` (24 h) ;
    - l'état : `Document`, `Current` (`ServiceConsensus?`), `PublicPoint` (`byte[]`), `Ledger`, et `Log { get; init; }` ;
    - les méthodes `Task RoundAsync(CancellationToken ct)`, `void IssueIfNeeded()` et `Task RunAsync(CancellationToken ct)`.
  - `RendezvousServer.Consensus { get; init; }` (`IConsensusSource?`)
  - `PeerSession.ConsensusPagesServed`

- [ ] **Step 1 : adapter le harnais de test**

Dans `ServerHarness.StartAsync`, ajouter le paramètre `IConsensusSource? consensus = null` en dernière position. Le passer à la construction du serveur, `new RendezvousServer(...) { Bans = ..., Consensus = consensus }`, en gardant les propriétés déjà posées.

- [ ] **Step 2 : écrire les tests qui échouent**

```csharp
using System.Net;
using System.Security.Cryptography;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Rendezvous.Tests;

/// <summary>Une sonde scriptée : les services cités répondent depuis l'adresse donnée.</summary>
internal sealed class ScriptedProbe : IServiceProbe
{
    public Dictionary<string, IPAddress> Up { get; } = [];

    public Task<ProbeResult> ProbeAsync(RendezvousAddress at, CancellationToken ct)
        => Task.FromResult(Up.TryGetValue(ServiceConsensus.Canonical(at), out var address)
            ? new ProbeResult(true, address)
            : new ProbeResult(false, null));
}

/// <summary>Une source de liste fixe, pour tester le service de pages.</summary>
internal sealed class FixedConsensus(byte[]? document) : IConsensusSource
{
    public byte[]? Document => document;
}

public sealed class AuthorityServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lprdv-authority-{Guid.NewGuid():N}");
    private readonly ManualClock _clock = new();
    private readonly ScriptedProbe _probe = new();
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly PeerDirectory _directory;
    private readonly AuthorityService _authority;

    public AuthorityServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _directory = new PeerDirectory(Path.Combine(_dir, "peers.txt"), Path.Combine(_dir, "pending.txt"), _clock);
        _authority = new AuthorityService(
            AuthorityLedger.Load(Path.Combine(_dir, "authority.json"), _clock), _probe, _key, _directory, _clock)
        {
            Log = TextWriter.Null,
        };
    }

    public void Dispose()
    {
        _key.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Une_candidature_est_sondee_puis_listee_apres_la_probation()
    {
        _directory.Submit("198.51.100.1", new DirectoryEntry("rdv.candidat.ch", "Candidat"));
        _probe.Up["rdv.candidat.ch:47900"] = IPAddress.Parse("203.0.113.7");

        for (var round = 0; round <= 432; round++)
        {
            await _authority.RoundAsync(CancellationToken.None);
            _clock.Advance(AuthorityService.Interval);
        }

        Assert.True(ServiceConsensus.TryVerify(
            _authority.Document!, [_authority.PublicPoint], _clock.UtcNow.ToUnixTimeSeconds(), out var list, out var why), why);
        Assert.Equal("rdv.candidat.ch:47900", Assert.Single(list!.Entries).Address);
        Assert.Equal("Candidat", list.Entries[0].Label);
    }

    [Fact]
    public void Une_liste_inchangee_n_est_reemise_qu_au_bout_de_24_heures()
    {
        _authority.IssueIfNeeded();
        var first = _authority.Current!.Version;

        _clock.Advance(TimeSpan.FromHours(23));
        _authority.IssueIfNeeded();
        Assert.Equal(first, _authority.Current!.Version);

        _clock.Advance(TimeSpan.FromHours(1));
        _authority.IssueIfNeeded();
        Assert.Equal(first + 1, _authority.Current!.Version);
    }

    [Fact]
    public async Task Sans_autorite_le_service_refuse_poliment_la_demande()
    {
        await using var harness = await ServerHarness.StartAsync();
        using var client = await harness.ConnectAsync();

        await client.SendAsync(RendezvousWire.ConsensusQuery(0));
        var frame = await client.ReadFrameAsync();

        Assert.Equal(RendezvousKind.Error, frame![0]);
    }

    [Fact]
    public async Task Le_service_sert_la_liste_par_pages()
    {
        var document = RandomNumberGenerator.GetBytes(70_000);
        await using var harness = await ServerHarness.StartAsync(consensus: new FixedConsensus(document));
        using var client = await harness.ConnectAsync();
        var received = new List<byte>();

        for (var page = 0; page < 3; page++)
        {
            await client.SendAsync(RendezvousWire.ConsensusQuery(page));
            var frame = await client.ReadFrameAsync();

            Assert.True(RendezvousWire.TryReadConsensusPage(frame!, out var index, out var pages, out var chunk, out var why), why);
            Assert.Equal(page, index);
            Assert.Equal(3, pages);
            received.AddRange(chunk);
        }

        Assert.Equal(document, received.ToArray());
    }

    [Fact]
    public async Task Trop_de_pages_ferme_la_session()
    {
        await using var harness = await ServerHarness.StartAsync(consensus: new FixedConsensus(new byte[] { 1, 2, 3 }));
        using var client = await harness.ConnectAsync();

        for (var i = 0; i < RendezvousWire.MaxConsensusPages; i++)
        {
            await client.SendAsync(RendezvousWire.ConsensusQuery(0));
            Assert.Equal(RendezvousKind.ConsensusPage, (await client.ReadFrameAsync())![0]);
        }

        await client.SendAsync(RendezvousWire.ConsensusQuery(0));
        Assert.Equal(RendezvousKind.Error, (await client.ReadFrameAsync())![0]);
        Assert.True(await client.IsClosedAsync());
    }
}
```

Si `IsClosedAsync` attend un argument, reprendre la forme utilisée dans `RendezvousServerTests`.

- [ ] **Step 3 : vérifier l'échec**

Run : `dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj --filter AuthorityServiceTests`
Expected : échec de compilation.

- [ ] **Step 4 : implémenter `AuthorityService`**

```csharp
using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Rendezvous;

/// <summary>Ce qui détient la liste signée à servir, s'il y en a une.</summary>
public interface IConsensusSource
{
    byte[]? Document { get; }
}

/// <summary>
/// Le rôle d'autorité : sonder, juger, signer.
/// </summary>
/// <remarks>
/// Une autorité ne fait foi que sur le cercle ouvert, qui ne voit jamais passer
/// une clé : le pire qu'elle puisse y admettre est un service qui refuse ou
/// observe des métadonnées. Le cercle d'ancrage, où passent les pairages,
/// reste composé à la main par chaque utilisateur.
/// </remarks>
public sealed class AuthorityService(
    AuthorityLedger ledger, IServiceProbe probe, ECDsa key, PeerDirectory directory, IClock clock) : IConsensusSource
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    /// <summary>Même inchangée, la liste est resignée chaque jour, pour ne jamais approcher son expiration.</summary>
    public static readonly TimeSpan Reissue = TimeSpan.FromHours(24);

    private const int Parallelism = 16;

    private readonly Lock _gate = new();
    private ServiceConsensus? _current;
    private byte[]? _document;

    public TextWriter Log { get; init; } = Console.Out;

    public AuthorityLedger Ledger => ledger;

    public byte[] PublicPoint { get; } = ServiceConsensus.PublicPoint(key);

    public byte[]? Document
    {
        get
        {
            lock (_gate)
                return _document;
        }
    }

    public ServiceConsensus? Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        IssueIfNeeded();

        while (ct.IsCancellationRequested is false)
        {
            try
            {
                await RoundAsync(ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Une ronde ratée n'arrête pas l'autorité : la liste en place
                // reste servie, et la suivante réessaie.
                Log.WriteLine($"Ronde de sondes en échec ({e.GetType().Name}) : {e.Message}");
            }

            try
            {
                await Task.Delay(Interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task RoundAsync(CancellationToken ct)
    {
        foreach (var entry in directory.Pending().Concat(directory.Known()))
            ledger.Track(entry);

        var targets = ledger.Snapshot()
            .Where(service => service.Standing is not ServiceStanding.Vetoed)
            .Select(service => service.Address)
            .ToList();

        await Parallel.ForEachAsync(
            targets,
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
            async (address, token) =>
            {
                if (RendezvousAddress.TryParse(address, out var at, out _))
                    ledger.Record(address, await probe.ProbeAsync(at, token).ConfigureAwait(false));
            }).ConfigureAwait(false);

        ledger.Settle();
        IssueIfNeeded();
    }

    public void IssueIfNeeded()
    {
        var listed = ledger.Listed();
        var now = clock.UtcNow.ToUnixTimeSeconds();

        lock (_gate)
        {
            if (_current is { } current && Same(current.Entries, listed) && now - current.Issued < (long)Reissue.TotalSeconds)
                return;

            var list = new ServiceConsensus(
                ledger.NextVersion(), now, now + (long)ServiceConsensus.Lifetime.TotalSeconds, listed);

            _document = ServiceConsensus.Sign(list, key);
            _current = list;
            Log.WriteLine($"Liste signée émise : version {list.Version}, {listed.Count} service(s).");
        }
    }

    private static bool Same(IReadOnlyList<ConsensusEntry> one, IReadOnlyList<ConsensusEntry> other)
        => one.Count == other.Count && one.Zip(other).All(pair =>
            pair.First.Address == pair.Second.Address
            && pair.First.Label == pair.Second.Label
            && pair.First.Family.AsSpan().SequenceEqual(pair.Second.Family));
}
```

- [ ] **Step 5 : servir la trame**

Dans `PeerSession.cs`, à côté de `BanPagesServed` :

```csharp
    /// <summary>Pages de liste signée servies sur cette connexion, bornées comme celles des bans.</summary>
    public int ConsensusPagesServed { get; set; }
```

Dans `RendezvousServer.cs`, à côté de `public BanStore? Bans { get; init; }` :

```csharp
    /// <summary>La liste signée à servir, présente seulement sur une autorité.</summary>
    public IConsensusSource? Consensus { get; init; }
```

Au dispatch, après la ligne `BanListQuery` :

```csharp
            RendezvousKind.ConsensusQuery => await HandleConsensusQueryAsync(session, frame, ct).ConfigureAwait(false),
```

Après `HandleBanListQueryAsync` :

```csharp
    private async Task<bool> HandleConsensusQueryAsync(PeerSession session, byte[] frame, CancellationToken ct)
    {
        if (RendezvousWire.TryReadConsensusQuery(frame, out var page) is false)
        {
            await session.SendAsync(RendezvousWire.Error("demande de liste signée malformée"), ct).ConfigureAwait(false);
            return false;
        }

        if (++session.ConsensusPagesServed > RendezvousWire.MaxConsensusPages)
        {
            await session.SendAsync(RendezvousWire.Error("trop de pages demandées"), ct).ConfigureAwait(false);
            return false;
        }

        // Lu une fois par page : si l'autorité réémet entre deux pages, le
        // client recolle deux versions, la signature échoue, et il garde sa
        // liste jusqu'à la récupération suivante. Rien à verrouiller ici.
        if (Consensus?.Document is not { } document)
        {
            await session.SendAsync(RendezvousWire.Error("ce service ne publie pas de liste signée"), ct).ConfigureAwait(false);
            return true;
        }

        var size = RendezvousWire.ConsensusPageBytes;
        var pages = Math.Max(1, (document.Length + size - 1) / size);

        if (page >= pages)
        {
            await session.SendAsync(RendezvousWire.Error("page hors de la liste"), ct).ConfigureAwait(false);
            return true;
        }

        var chunk = document.AsSpan(page * size, Math.Min(size, document.Length - page * size)).ToArray();
        await session.SendAsync(RendezvousWire.ConsensusPage(page, pages, chunk), ct).ConfigureAwait(false);
        return true;
    }
```

- [ ] **Step 6 : brancher dans `Program.cs`**

Dans l'aide, à côté de `--announce-to` :

```
  --directory-authority     Tient le rôle d'autorité du cercle ouvert : sonde les
                            candidats, signe et sert la liste. Désactivé par défaut.
  --directory-key PATH      Clé de signature de l'autorité (directory.key). Engendrée au
                            premier démarrage ; ne jamais l'écraser ni la régénérer.
  --authority-state PATH    Registre des probations (authority.json).
```

Après la création de `bans`, avant celle de `service` :

```csharp
AuthorityService? authority = null;

if (args.Contains("--directory-authority"))
{
    var key = DirectoryKey.LoadOrCreate(ArgString("--directory-key", "directory.key"));
    var ledger = AuthorityLedger.Load(ArgString("--authority-state", "authority.json"), clock);
    authority = new AuthorityService(ledger, new ServiceProbe(), key, directory, clock);
    Console.WriteLine($"Autorité du cercle ouvert, clé publique {Convert.ToHexStringLower(authority.PublicPoint)}.");
}
```

Ajouter ensuite `Consensus = authority` dans l'initialiseur du `RendezvousServer`, à côté de `Bans = bans`. Après `var running = ...` :

```csharp
if (authority is not null)
    running.Add(authority.RunAsync(stopping.Token));
```

- [ ] **Step 7 : amender le commentaire d'`Announcer`**

Dans le commentaire de classe d'`Announcer.cs`, la phrase qui en fait « la seule connexion sortante qu'un service ouvre de lui-même » devient : « la seule connexion sortante d'un service ordinaire. Une autorité du cercle ouvert en ouvre d'autres, vers les candidats qu'elle sonde (voir `ServiceProbe`). »

- [ ] **Step 8 : vérifier le succès, puis toute la suite**

Run : `dotnet build Linkpearl.Rendezvous/Linkpearl.Rendezvous.csproj -c Release && dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj`
Expected : build sans warning, tests PASS.

- [ ] **Step 9 : commit**

```bash
git add Linkpearl.Rendezvous Linkpearl.Rendezvous.Tests
git commit -m "feat(autorité): sondes périodiques, émission signée et service par pages"
```

### Task 8 : la console

Dépôt : **RDV**.

**Files :**
- Modify : `Linkpearl.Rendezvous/AdminServer.cs`, à trois endroits :
  - les propriétés `init`, près de `Log` ;
  - la table de routes, l.184-392 ;
  - `Status()`, l.395-474.
- Modify : `Linkpearl.Rendezvous/AdminPage.cs` : une section après celle de l'annuaire, et le JS de `rafraichir()` vers l.585-597.
- Modify : `Linkpearl.Rendezvous/Program.cs` (initialiseur de `AdminServer`)
- Modify : `Linkpearl.Rendezvous.Tests/AdminServerTests.cs`

**Interfaces :**
- Consumes :
  - de `AuthorityService` : `Ledger`, `Current`, `PublicPoint` et `IssueIfNeeded()` ;
  - de `AuthorityLedger` : `Veto`, `Lift` et `Snapshot`.
- Produces :
  - la propriété `AdminServer.Authority { get; init; }` (`AuthorityService?`) ;
  - un champ `authority` dans `GET /api/status`, nul sans autorité ;
  - les routes `POST /api/authority/veto` et `DELETE /api/authority/veto`, de corps `{"address": "..."}`.

- [ ] **Step 1 : écrire les tests qui échouent**

Dans `AdminServerTests.InitializeAsync`, après la création de `_directory` et de l'horloge, construire une autorité et la passer à la console :

```csharp
        _authority = new AuthorityService(
            AuthorityLedger.Load(Path.Combine(_dir, "authority.json"), _clock), new ScriptedProbe(),
            ECDsa.Create(ECCurve.NamedCurves.nistP256), _directory, _clock)
        {
            Log = TextWriter.Null,
        };
```

Il faut aussi :
- le champ `private AuthorityService _authority = null!;` ;
- `Authority = _authority` dans l'initialiseur de `new AdminServer(...)` ;
- `using System.Security.Cryptography;`.

Reprendre les noms de champs réels de la classe s'ils diffèrent de `_dir`, `_clock` et `_directory`.

Nouveaux tests :

```csharp
    [Fact]
    public async Task L_etat_montre_la_cle_de_l_autorite()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/status");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(Convert.ToHexStringLower(_authority.PublicPoint), body);
    }

    [Fact]
    public async Task Ecarter_un_service_suivi_le_retire_du_cercle()
    {
        _authority.Ledger.Track(new DirectoryEntry("rdv.suspect.ch", "Suspect"));

        var response = await SendAsync(HttpMethod.Post, "/api/authority/veto", body: """{"address":"rdv.suspect.ch"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ServiceStanding.Vetoed, _authority.Ledger.Snapshot().Single().Standing);
    }

    [Fact]
    public async Task Ecarter_un_service_inconnu_rend_404()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/authority/veto", body: """{"address":"rdv.inconnu.ch"}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Retablir_un_service_ecarte_le_rend_candidat()
    {
        _authority.Ledger.Track(new DirectoryEntry("rdv.suspect.ch", "Suspect"));
        _authority.Ledger.Veto("rdv.suspect.ch");

        var response = await SendAsync(HttpMethod.Delete, "/api/authority/veto", body: """{"address":"rdv.suspect.ch"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ServiceStanding.Candidate, _authority.Ledger.Snapshot().Single().Standing);
    }

    [Fact]
    public async Task Ecarter_sans_jeton_est_refuse()
    {
        var response = await SendAsync(
            HttpMethod.Post, "/api/authority/veto", body: """{"address":"rdv.suspect.ch"}""", token: "mauvais");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
```

Si le paramètre de jeton de `SendAsync` a un autre nom ou une autre forme, reprendre celle des tests 401 existants.

- [ ] **Step 2 : vérifier l'échec**

Run : `dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj --filter AdminServerTests`
Expected : échec de compilation (`Authority` n'existe pas).

- [ ] **Step 3 : implémenter la console**

Dans `AdminServer.cs`, à côté de `Log` :

```csharp
    /// <summary>L'autorité du cercle ouvert, si ce service en tient le rôle.</summary>
    public AuthorityService? Authority { get; init; }
```

Dans la table de routes, avant `default`. Ici `method` est le nom de la variable du verbe HTTP dans le `switch (path, method)` ; reprendre le nom réel s'il diffère.

```csharp
            case ("/api/authority/veto", "POST"):
            case ("/api/authority/veto", "DELETE"):
            {
                var body = await BodyAsync(context).ConfigureAwait(false);
                var address = body?["address"]?.GetValue<string>() ?? "";
                var veto = method is "POST";
                var done = Authority is { } authority && (veto ? authority.Ledger.Veto(address) : authority.Ledger.Lift(address));

                if (done)
                {
                    // Réémise aussitôt : un service écarté ne doit pas rester
                    // servi jusqu'à la ronde suivante.
                    Authority!.IssueIfNeeded();
                    Log.WriteLine(veto ? $"Service écarté du cercle ouvert : {address}." : $"Service rétabli : {address}.");
                }

                Respond(context, done ? 200 : 404, "application/json", Result(done, address));
                return;
            }
```

Dans `Status()`, ajouter à l'objet racine :

```csharp
            ["authority"] = Authority is null ? null : AuthorityStatus(Authority),
```

et la méthode :

```csharp
    private static JsonObject AuthorityStatus(AuthorityService authority)
    {
        var current = authority.Current;
        var services = new JsonArray();

        foreach (var service in authority.Ledger.Snapshot())
            services.Add(new JsonObject
            {
                ["address"] = service.Address,
                ["label"] = service.Label,
                ["standing"] = service.Standing.ToString(),
                ["probes"] = service.Probes,
                ["successes"] = service.Successes,
                ["lastSuccess"] = service.LastSuccess?.ToUnixTimeSeconds(),
                ["probationStart"] = service.ProbationStart?.ToUnixTimeSeconds(),
                ["listedAt"] = service.ListedAt?.ToUnixTimeSeconds(),
            });

        return new JsonObject
        {
            ["publicKey"] = Convert.ToHexStringLower(authority.PublicPoint),
            ["version"] = current?.Version,
            ["issued"] = current?.Issued,
            ["expires"] = current?.Expires,
            ["services"] = services,
        };
    }
```

Dans `Program.cs`, ajouter `Authority = authority` à la construction de `AdminServer`, dans un initialiseur `{ ... }`.

- [ ] **Step 4 : la page**

Dans `AdminPage.Html`, après la section de l'annuaire :

```html
<section id="autorite-section" hidden>
  <h2>Cercle ouvert</h2>
  <p class="note" id="autorite-etat"></p>
  <table><tbody id="autorite"></tbody></table>
</section>
```

Dans le JS, après le remplissage de `candidats` dans `rafraichir()` :

```js
    const a = s.authority;
    $("autorite-section").hidden = !a;
    if (a) {
      $("autorite-etat").textContent = "Clé publique " + a.publicKey + (a.version
        ? " · version " + a.version + ", valable jusqu'au " + new Date(a.expires * 1000).toLocaleString()
        : " · aucune liste émise");
      remplir($("autorite"), a.services.map(p => ligne([
        p.address, p.label || "", etatAutorite(p),
        p.standing === "Vetoed"
          ? bouton("rétablir", false, () => agir("/api/authority/veto", "DELETE", { address: p.address }, p.address + " rétabli"))
          : bouton("écarter", true, () => {
              if (confirm("Écarter " + p.address + " du cercle ouvert ?"))
                return agir("/api/authority/veto", "POST", { address: p.address }, p.address + " écarté");
            })
      ])), "aucun service suivi. Une candidature reçue par --announce-to apparaît ici après la prochaine sonde.");
    }
```

et, à côté des autres fonctions utilitaires :

```js
  function etatAutorite(p) {
    const noms = { Candidate: "candidat", Probation: "en probation", Listed: "listé", Delisted: "sorti", Vetoed: "écarté" };
    const ratio = p.probes ? " (" + Math.round(100 * p.successes / p.probes) + " %)" : "";
    return noms[p.standing] + ratio;
  }
```

- [ ] **Step 5 : vérifier le succès, et à l'œil**

Run : `dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj`
Expected : PASS.

Vérification à l'œil, dans un dossier temporaire pour ne rien laisser dans le dépôt :

```bash
d=$(mktemp -d) && cd $d && dotnet run --project /home/yrapenne/Projects/linkpearl-sync/rendezvous/Linkpearl.Rendezvous -- --port 47900 --admin-port 47901 --directory-authority
```

Ouvrir `http://127.0.0.1:47901/` avec le jeton de `$d/admin.token`. La section « Cercle ouvert » doit afficher la clé publique et une version émise. Arrêter ensuite le service et supprimer `$d`.

- [ ] **Step 6 : commit**

```bash
git add Linkpearl.Rendezvous Linkpearl.Rendezvous.Tests
git commit -m "feat(console): suivi du cercle ouvert et veto de l'opérateur"
```

---

## Partie C : le client (PLUGIN)

### Task 9 : le placement

Dépôt : **PLUGIN**.

**Files :**
- Create : `Linkpearl/Core/Sync/ServicePlacement.cs`
- Create : `Linkpearl.Core.Tests/Sync/ServicePlacementTests.cs`

**Interfaces :**
- Consumes : `ServiceConsensus.Canonical`, `ConsensusEntry`, `RendezvousAddress.TryParse`
- Produces :
  - `static class ServicePlacement` avec `const int Chosen = 2`
  - `static byte[] Score(ReadOnlySpan<byte> pairSecret, string canonicalAddress)`
  - `static IReadOnlyList<RendezvousAddress> Choose(ReadOnlySpan<byte> pairSecret, IReadOnlyList<ConsensusEntry> entries)`

- [ ] **Step 1 : écrire les tests qui échouent**

Les scores attendus ont été calculés à part : SHA-256 de `"linkpearl:place:v1" || secret || adresse`, avec pour secret les octets 0 à 31. Par score décroissant, l'ordre est b, d, a, c.

```csharp
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

/// <summary>Le hachage de rendez-vous qui désigne les deux services d'une paire.</summary>
public class ServicePlacementTests
{
    private static readonly byte[] Secret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];

    private static ConsensusEntry Entry(string address, byte family)
        => new(address, "", [.. Enumerable.Repeat(family, 8)]);

    private static string[] Hosts(IReadOnlyList<RendezvousAddress> chosen)
        => [.. chosen.Select(ServiceConsensus.Canonical)];

    [Fact]
    public void Le_score_est_fige()
        => Assert.Equal(
            "82cc9af59e38373519a29f7ecd2ff72b3dd4eefdc6bde0893dd63b9dbea01967",
            Convert.ToHexStringLower(ServicePlacement.Score(Secret, "rdv.a.ch:47900")));

    [Fact]
    public void Les_deux_meilleurs_scores_sont_retenus()
    {
        var chosen = ServicePlacement.Choose(Secret,
            [Entry("rdv.a.ch:47900", 1), Entry("rdv.b.ch:47900", 2), Entry("rdv.c.ch:443", 3), Entry("rdv.d.ch:47900", 4)]);

        Assert.Equal(new[] { "rdv.b.ch:47900", "rdv.d.ch:47900" }, Hosts(chosen));
    }

    [Fact]
    public void Deux_services_d_une_meme_famille_ne_sont_jamais_retenus_ensemble()
    {
        var chosen = ServicePlacement.Choose(Secret,
            [Entry("rdv.a.ch:47900", 1), Entry("rdv.b.ch:47900", 1), Entry("rdv.c.ch:443", 2), Entry("rdv.d.ch:47900", 1)]);

        Assert.Equal(new[] { "rdv.b.ch:47900", "rdv.c.ch:443" }, Hosts(chosen));
    }

    [Fact]
    public void Une_seule_famille_ne_donne_qu_un_service()
    {
        var chosen = ServicePlacement.Choose(Secret, [Entry("rdv.a.ch:47900", 1), Entry("rdv.b.ch:47900", 1)]);

        Assert.Equal(new[] { "rdv.b.ch:47900" }, Hosts(chosen));
    }

    [Fact]
    public void Un_service_sans_rapport_qui_sort_ne_deplace_pas_la_paire()
    {
        ConsensusEntry[] before = [Entry("rdv.a.ch:47900", 1), Entry("rdv.b.ch:47900", 2), Entry("rdv.c.ch:443", 3), Entry("rdv.d.ch:47900", 4)];
        ConsensusEntry[] after = [Entry("rdv.b.ch:47900", 2), Entry("rdv.c.ch:443", 3), Entry("rdv.d.ch:47900", 4)];

        Assert.Equal(Hosts(ServicePlacement.Choose(Secret, before)), Hosts(ServicePlacement.Choose(Secret, after)));
    }

    [Fact]
    public void La_casse_ne_change_pas_la_place()
    {
        var chosen = ServicePlacement.Choose(Secret,
            [Entry("RDV.B.CH:47900", 2), Entry("rdv.d.ch", 4), Entry("rdv.a.ch:47900", 1)]);

        Assert.Equal(new[] { "rdv.b.ch:47900", "rdv.d.ch:47900" }, Hosts(chosen));
    }

    [Fact]
    public void Une_liste_vide_ne_donne_rien()
        => Assert.Empty(ServicePlacement.Choose(Secret, []));
}
```

- [ ] **Step 2 : vérifier l'échec**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter ServicePlacementTests`
Expected : échec de compilation.

- [ ] **Step 3 : implémenter**

```csharp
using System.Security.Cryptography;
using System.Text;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Sync;

/// <summary>
/// Les deux services du cercle ouvert où une paire se retrouve.
/// </summary>
/// <remarks>
/// Un hachage de rendez-vous : chaque service reçoit un score tiré du secret de
/// paire, et les deux meilleurs l'emportent. Quand la liste change, seules les
/// paires dont un service retenu est entré ou sorti changent de place.
///
/// Aucune rotation dans le temps. Chez Tor, les descripteurs tournent parce
/// qu'un attaquant peut calculer leur place et s'y poster. Ici la place dépend
/// du secret de paire, qu'il ignore : il ne capture que des paires prises au
/// hasard, et faire tourner n'exposerait chacune qu'à plus d'opérateurs.
/// </remarks>
public static class ServicePlacement
{
    public const int Chosen = 2;

    private static ReadOnlySpan<byte> PlaceInfo => "linkpearl:place:v1"u8;

    public static byte[] Score(ReadOnlySpan<byte> pairSecret, string canonicalAddress)
    {
        var address = Encoding.UTF8.GetBytes(canonicalAddress);
        var message = new byte[PlaceInfo.Length + pairSecret.Length + address.Length];

        PlaceInfo.CopyTo(message);
        pairSecret.CopyTo(message.AsSpan(PlaceInfo.Length));
        address.CopyTo(message.AsSpan(PlaceInfo.Length + pairSecret.Length));

        return SHA256.HashData(message);
    }

    public static IReadOnlyList<RendezvousAddress> Choose(ReadOnlySpan<byte> pairSecret, IReadOnlyList<ConsensusEntry> entries)
    {
        var scored = new List<(RendezvousAddress At, byte[] Family, byte[] Score)>(entries.Count);

        foreach (var entry in entries)
        {
            if (RendezvousAddress.TryParse(entry.Address, out var at, out _) is false)
                continue;

            scored.Add((at, entry.Family, Score(pairSecret, ServiceConsensus.Canonical(at))));
        }

        // Décroissant, octet par octet : les deux côtés d'une paire doivent
        // obtenir exactement le même ordre.
        scored.Sort((one, other) => other.Score.AsSpan().SequenceCompareTo(one.Score));

        var chosen = new List<RendezvousAddress>(Chosen);
        var families = new List<byte[]>(Chosen);

        foreach (var (at, family, _) in scored)
        {
            // Deux services d'un même sous-réseau tombent ensemble : les
            // retenir tous deux ne donnerait qu'une redondance de façade.
            if (families.Any(taken => taken.AsSpan().SequenceEqual(family)))
                continue;

            chosen.Add(at);
            families.Add(family);

            if (chosen.Count == Chosen)
                break;
        }

        return chosen;
    }
}
```

- [ ] **Step 4 : vérifier le succès**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter ServicePlacementTests`
Expected : PASS.

- [ ] **Step 5 : commit**

```bash
git add Linkpearl/Core/Sync/ServicePlacement.cs Linkpearl.Core.Tests/Sync/ServicePlacementTests.cs
git commit -m "feat(fédération): placement d'une paire par hachage de rendez-vous"
```

### Task 10 : le cercle ouvert et le droit d'y passer

Dépôt : **PLUGIN**.

**Files :**
- Create : `Linkpearl/Core/Sync/OpenCircle.cs`
- Create : `Linkpearl.Core.Tests/Sync/OpenCircleTests.cs`
- Modify : `Linkpearl/Core/Groups/GroupRecord.cs:13` (`GroupOrigin`)
- Modify : `Linkpearl/Core/Groups/GroupDialPlanner.cs` (`Record`, vers l.111-128)
- Modify : `Linkpearl.Core.Tests/Groups/GroupDialPlannerTests.cs`

**Interfaces :**
- Consumes : `ServiceConsensus.TryVerify`, `ServicePlacement.Choose`, `IClock`, `PairRecord`, `MovableClock` (tests, `Sync/SyncPiecesTests.cs:12`)
- Produces :
  - `interface IOpenCircle { IReadOnlyList<RendezvousAddress> PlacesFor(PairRecord pair); }`
  - `sealed class OpenCircle(IReadOnlyList<byte[]> trustedKeys, IClock clock) : IOpenCircle`, avec :
    - `bool Enabled { get; set; }`, vrai par défaut ;
    - `ServiceConsensus? Current { get; }`, nul si la liste est absente ou expirée ;
    - `bool Offer(ReadOnlySpan<byte> document, out string? rejection)` ;
    - `static bool MayUse(PairRecord pair)`.
  - `GroupOrigin.Pinned` et `GroupOrigin.Public` (`bool`, `init`)

- [ ] **Step 1 : écrire les tests qui échouent**

`Linkpearl.Core.Tests/Sync/OpenCircleTests.cs` :

```csharp
using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

/// <summary>La liste gardée par le client, et qui a le droit d'y passer.</summary>
public sealed class OpenCircleTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly MovableClock _clock = new();

    public void Dispose() => _key.Dispose();

    private OpenCircle Circle() => new([ServiceConsensus.PublicPoint(_key)], _clock);

    private byte[] Document(uint version = 1)
    {
        var issued = _clock.UtcNow.ToUnixTimeSeconds();
        return ServiceConsensus.Sign(new ServiceConsensus(
            version, issued, issued + (long)ServiceConsensus.Lifetime.TotalSeconds,
            [
                new ConsensusEntry("rdv.a.ch:47900", "A", [.. Enumerable.Repeat((byte)1, 8)]),
                new ConsensusEntry("rdv.b.ch:47900", "B", [.. Enumerable.Repeat((byte)2, 8)]),
                new ConsensusEntry("rdv.c.ch:47900", "C", [.. Enumerable.Repeat((byte)3, 8)]),
            ]), _key);
    }

    private static PairRecord Pair(GroupOrigin? group = null) => new()
    {
        Id = PeerId.Of(new byte[65]),
        PairSecret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)],
        DisplayName = "Pair",
        Rendezvous = [new RendezvousAddress("rdv.ancre.ch", 47900)],
        Trust = PairTrust.Accepted,
        PairedAt = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero),
        Group = group,
    };

    private static GroupOrigin Origin(bool pinned, bool isPublic)
        => new(GroupId.Of(new byte[32]), PlayerFingerprint.Of("alice", 21), PlayerFingerprint.Of("bob", 21))
        {
            Pinned = pinned,
            Public = isPublic,
        };

    [Fact]
    public void Sans_liste_aucun_service_ouvert()
        => Assert.Empty(Circle().PlacesFor(Pair()));

    [Fact]
    public void Une_paire_du_carnet_recoit_deux_services()
    {
        var circle = Circle();
        Assert.True(circle.Offer(Document(), out var why), why);

        Assert.Equal(ServicePlacement.Chosen, circle.PlacesFor(Pair()).Count);
    }

    [Fact]
    public void Une_liste_expiree_rend_la_main_a_l_ancrage()
    {
        var circle = Circle();
        circle.Offer(Document(), out _);

        _clock.Advance(ServiceConsensus.Lifetime);

        Assert.Null(circle.Current);
        Assert.Empty(circle.PlacesFor(Pair()));
    }

    [Fact]
    public void Une_version_anterieure_est_refusee_et_la_precedente_reste()
    {
        var circle = Circle();
        circle.Offer(Document(version: 5), out _);

        Assert.False(circle.Offer(Document(version: 4), out var why));
        Assert.Contains("antérieure", why);
        Assert.Equal(5u, circle.Current!.Version);
    }

    [Fact]
    public void Une_liste_alteree_est_refusee_et_la_precedente_reste()
    {
        var circle = Circle();
        circle.Offer(Document(version: 1), out _);
        var altered = Document(version: 2);
        altered[30] ^= 1;

        Assert.False(circle.Offer(altered, out _));
        Assert.Equal(1u, circle.Current!.Version);
    }

    [Fact]
    public void Le_cercle_desactive_ne_donne_rien()
    {
        var circle = Circle();
        circle.Offer(Document(), out _);
        circle.Enabled = false;

        Assert.Empty(circle.PlacesFor(Pair()));
    }

    [Fact]
    public void Un_membre_de_groupe_non_epingle_reste_dans_l_ancrage()
    {
        var circle = Circle();
        circle.Offer(Document(), out _);

        Assert.Empty(circle.PlacesFor(Pair(Origin(pinned: false, isPublic: false))));
    }

    [Fact]
    public void Un_membre_du_groupe_Public_reste_dans_l_ancrage()
    {
        var circle = Circle();
        circle.Offer(Document(), out _);

        Assert.Empty(circle.PlacesFor(Pair(Origin(pinned: true, isPublic: true))));
    }

    [Fact]
    public void Un_membre_epingle_d_un_groupe_prive_passe_par_le_cercle_ouvert()
    {
        var circle = Circle();
        circle.Offer(Document(), out _);

        Assert.Equal(ServicePlacement.Chosen, circle.PlacesFor(Pair(Origin(pinned: true, isPublic: false))).Count);
    }
}
```

Dans `GroupDialPlannerTests.cs`, ajouter le test suivant, et `using Linkpearl.Core.Identity;` si `PeerId` n'est pas déjà importé :

```csharp
    [Fact]
    public void Le_pair_de_groupe_porte_l_epinglage_du_membre()
    {
        var group = Group(Secret);
        var pinned = group with
        {
            Members = new Dictionary<PlayerFingerprint, GroupMember>
            {
                [Bob] = new() { Fingerprint = Bob, DisplayName = "Bob", Id = PeerId.Of(new byte[65]) },
            },
        };

        var fresh = Assert.Single(new GroupDialPlanner(_clock).Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [group], []));
        var known = Assert.Single(new GroupDialPlanner(_clock).Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [pinned], []));

        Assert.False(fresh.Group!.Pinned);
        Assert.True(known.Group!.Pinned);
        Assert.False(known.Group.Public);
    }
```

- [ ] **Step 2 : vérifier l'échec**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter "OpenCircleTests|GroupDialPlannerTests"`
Expected : échec de compilation.

- [ ] **Step 3 : implémenter**

`GroupOrigin`, dans `GroupRecord.cs:13` :

```csharp
public sealed record GroupOrigin(GroupId Group, PlayerFingerprint Ours, PlayerFingerprint Theirs)
{
    /// <summary>Vrai si la clé du membre est déjà épinglée dans le carnet de groupe.</summary>
    /// <remarks>
    /// Avant l'épinglage, le premier handshake est une confiance au premier
    /// contact que ne lie pas le secret du groupe : un service du cercle ouvert
    /// pourrait le gagner. Ce drapeau garde ces membres dans l'ancrage.
    /// </remarks>
    public bool Pinned { get; init; }

    /// <summary>Vrai pour le groupe Public, dont le secret est connu de tous.</summary>
    public bool Public { get; init; }
}
```

Dans `GroupDialPlanner.Record`, remplacer `Group = new GroupOrigin(group.Id, ours, theirs),` par :

```csharp
            Group = new GroupOrigin(group.Id, ours, theirs) { Pinned = known?.Id is not null, Public = group.IsPublic },
```

`Linkpearl/Core/Sync/OpenCircle.cs` :

```csharp
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Sync;

/// <summary>Ce qui sait où une paire se retrouve dans le cercle ouvert.</summary>
public interface IOpenCircle
{
    /// <summary>Les services ouverts de cette paire, ou rien si elle doit rester dans l'ancrage.</summary>
    IReadOnlyList<RendezvousAddress> PlacesFor(PairRecord pair);
}

/// <summary>
/// La liste signée que ce client tient pour valable, et le droit d'y passer.
/// </summary>
/// <remarks>
/// Le cercle ouvert est un gain, jamais une dépendance : sans liste, avec une
/// liste expirée ou désactivé, il ne donne rien, et tout passe par l'ancrage
/// comme avant son existence.
/// </remarks>
public sealed class OpenCircle(IReadOnlyList<byte[]> trustedKeys, IClock clock) : IOpenCircle
{
    private readonly Lock _gate = new();
    private ServiceConsensus? _list;

    public bool Enabled { get; set; } = true;

    public ServiceConsensus? Current
    {
        get
        {
            lock (_gate)
                return _list is { } list && clock.UtcNow.ToUnixTimeSeconds() < list.Expires ? list : null;
        }
    }

    public bool Offer(ReadOnlySpan<byte> document, out string? rejection)
    {
        if (ServiceConsensus.TryVerify(document, trustedKeys, clock.UtcNow.ToUnixTimeSeconds(), out var list, out rejection) is false)
            return false;

        lock (_gate)
        {
            // Une liste plus ancienne que celle qu'on tient ne remplace rien :
            // sans cette règle, rejouer une vieille liste signée ramènerait
            // des services que l'autorité a depuis écartés.
            if (_list is { } held && list!.Version < held.Version)
            {
                rejection = $"version {list.Version} antérieure à celle détenue ({held.Version})";
                return false;
            }

            _list = list;
        }

        return true;
    }

    public IReadOnlyList<RendezvousAddress> PlacesFor(PairRecord pair)
        => Enabled && MayUse(pair) && Current is { } list ? ServicePlacement.Choose(pair.PairSecret, list.Entries) : [];

    /// <summary>
    /// Vrai si la clé de ce pair ne peut plus être substituée par un service.
    /// </summary>
    /// <remarks>
    /// Une paire du carnet l'est toujours : son identifiant est l'empreinte de
    /// sa clé, qu'une session vérifie. Un membre de groupe ne l'est qu'une
    /// fois épinglé, et jamais dans le groupe Public, dont le secret connu de
    /// tous rend la place de chacun calculable par n'importe qui.
    /// </remarks>
    public static bool MayUse(PairRecord pair)
        => pair.Group is not { } origin || (origin.Pinned && origin.Public is false);
}
```

- [ ] **Step 4 : vérifier le succès, puis toute la suite**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected : PASS. Le test existant `Un_membre_vu_devient_un_pair_de_groupe_identique_des_deux_cotes` compare avec `new GroupOrigin(group.Id, Alice, Bob)`, et il passe toujours : Bob n'y est pas épinglé et le groupe n'est pas Public.

- [ ] **Step 5 : commit**

```bash
git add Linkpearl/Core/Sync/OpenCircle.cs Linkpearl/Core/Groups/GroupRecord.cs Linkpearl/Core/Groups/GroupDialPlanner.cs Linkpearl.Core.Tests/Sync/OpenCircleTests.cs Linkpearl.Core.Tests/Groups/GroupDialPlannerTests.cs
git commit -m "feat(fédération): cercle ouvert côté client, réservé aux pairs épinglés"
```

### Task 11 : l'annonce en deux cercles

Dépôt : **PLUGIN**.

**Files :**
- Modify : `Linkpearl/Core/Sync/PeerConnector.cs`, à quatre endroits :
  - `ConnectionAttempt`, l.26 ;
  - le constructeur, l.37-39 ;
  - `AnnounceEverywhereAsync`, l.99-142 ;
  - `ConnectAsync`, l.144-217.
- Modify : `Linkpearl.Core.Tests/Sync/PeerConnectorTests.cs`

**Interfaces :**
- Consumes : `IOpenCircle.PlacesFor`, `ServiceConsensus.Canonical`
- Produces :
  - le constructeur `PeerConnector(PeerLinkFactory links, RendezvousEndpoint rendezvous, IClock clock, ILogSink log, TimeSpan? announceBudget = null, IOpenCircle? circle = null)` ;
  - `public static readonly TimeSpan OpenHead` (10 s) ;
  - `static Task<(RendezvousAddress At, byte[] Theirs)?> AnnounceInCirclesAsync(IRendezvousDialer dialer, IReadOnlyList<RendezvousAddress> open, IReadOnlyList<RendezvousAddress> anchor, Announcement announcement, TimeSpan head, TimeSpan budget, CancellationToken ct)` ;
  - `sealed record ConnectionAttempt(IPeerLink? Link, bool PeerWasAbsent, string? Failure, RendezvousAddress? Via = null)`.

- [ ] **Step 1 : écrire les tests qui échouent**

Ajouter à `PeerConnectorTests.cs` un annonceur qui date ses appels et sait lever :

```csharp
/// <summary>Un annonceur qui date chaque appel, et dont certains lieux lèvent aussitôt.</summary>
internal sealed class TimedDialer(Dictionary<string, byte[]?> answers, HashSet<string>? failing = null) : IRendezvousDialer
{
    private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();

    public List<(string Host, TimeSpan At)> Asked { get; } = [];

    public async Task<byte[]?> AnnounceAsync(RendezvousAddress at, Announcement announcement, CancellationToken ct)
    {
        lock (Asked)
            Asked.Add((at.Host, _watch.Elapsed));

        if (failing?.Contains(at.Host) is true)
            throw new IOException("service injoignable");

        if (answers.TryGetValue(at.Host, out var answer) is false)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return null;
        }

        return answer;
    }
}
```

et, dans la classe `PeerConnectorTests` :

```csharp
    private static RendezvousAddress At(string host) => new(host, 47900);

    [Fact]
    public async Task Le_cercle_ouvert_apparie_sans_deranger_l_ancrage()
    {
        var dialer = new TimedDialer(new() { ["ouvert.ch"] = [1] });

        var match = await PeerConnector.AnnounceInCirclesAsync(
            dialer, [At("ouvert.ch")], [At("ancre.ch")], Some(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal("ouvert.ch", match!.Value.At.Host);
        Assert.DoesNotContain(dialer.Asked, asked => asked.Host == "ancre.ch");
    }

    [Fact]
    public async Task L_ancrage_attend_la_tete_laissee_au_cercle_ouvert()
    {
        var dialer = new TimedDialer(new() { ["ancre.ch"] = [1] });

        var match = await PeerConnector.AnnounceInCirclesAsync(
            dialer, [At("ouvert.ch")], [At("ancre.ch")], Some(), TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal("ancre.ch", match!.Value.At.Host);
        Assert.True(dialer.Asked.Single(asked => asked.Host == "ancre.ch").At >= TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task Des_services_ouverts_injoignables_liberent_l_ancrage_aussitot()
    {
        var dialer = new TimedDialer(new() { ["ancre.ch"] = [1] }, failing: ["ouvert1.ch", "ouvert2.ch"]);

        var match = await PeerConnector.AnnounceInCirclesAsync(
            dialer, [At("ouvert1.ch"), At("ouvert2.ch")], [At("ancre.ch")], Some(),
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), CancellationToken.None);

        Assert.Equal("ancre.ch", match!.Value.At.Host);
        Assert.True(dialer.Asked.Single(asked => asked.Host == "ancre.ch").At < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Sans_cercle_ouvert_l_ancrage_part_aussitot()
    {
        var dialer = new TimedDialer(new() { ["ancre.ch"] = [1] });

        var match = await PeerConnector.AnnounceInCirclesAsync(
            dialer, [], [At("ancre.ch")], Some(), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), CancellationToken.None);

        Assert.Equal("ancre.ch", match!.Value.At.Host);
        Assert.True(dialer.Asked.Single().At < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Un_service_des_deux_cercles_n_est_annonce_qu_une_fois()
    {
        var dialer = new TimedDialer([]);

        var match = await PeerConnector.AnnounceInCirclesAsync(
            dialer, [At("rdv.x.ch")], [new RendezvousAddress("RDV.X.CH", 47900)], Some(),
            TimeSpan.Zero, TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.Null(match);
        Assert.Single(dialer.Asked);
    }
```

- [ ] **Step 2 : vérifier l'échec**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter PeerConnectorTests`
Expected : échec de compilation.

- [ ] **Step 3 : implémenter**

`ConnectionAttempt` :

```csharp
/// <summary>Ce qu'une tentative de connexion a donné, et par quel lieu elle est passée.</summary>
public sealed record ConnectionAttempt(IPeerLink? Link, bool PeerWasAbsent, string? Failure, RendezvousAddress? Via = null);
```

Constructeur de `PeerConnector` : ajouter `IOpenCircle? circle = null` après `TimeSpan? announceBudget = null`.

À côté de `DefaultAnnounceBudget` :

```csharp
    /// <summary>
    /// L'avance laissée au cercle ouvert avant d'annoncer aussi sur l'ancrage.
    /// </summary>
    /// <remarks>
    /// Assez pour que deux clients récents s'y trouvent, et que l'ancrage ne
    /// voie pas passer leur annonce. Trop peu pour qu'un client d'avant le
    /// cercle ouvert, qui n'annonce que sur l'ancrage, soit manqué : les
    /// annonces ouvertes restent tenues tout le budget, et celles de l'ancrage
    /// en couvrent encore quinze secondes.
    /// </remarks>
    public static readonly TimeSpan OpenHead = TimeSpan.FromSeconds(10);
```

Remplacer le corps d'`AnnounceEverywhereAsync` par un appel à la nouvelle méthode, et ajouter celle-ci. Il faut `using System.Runtime.CompilerServices;` pour `StrongBox`.

```csharp
    public static Task<(RendezvousAddress At, byte[] Theirs)?> AnnounceEverywhereAsync(
        IRendezvousDialer dialer, IReadOnlyList<RendezvousAddress> places,
        Announcement announcement, TimeSpan budget, CancellationToken ct)
        => AnnounceInCirclesAsync(dialer, places, [], announcement, TimeSpan.Zero, budget, ct);

    /// <summary>
    /// S'annonce sur le cercle ouvert tout de suite, et sur l'ancrage après
    /// <paramref name="head"/>, ou dès que tous les services ouverts ont
    /// échoué. Le premier appariement gagne.
    /// </summary>
    public static async Task<(RendezvousAddress At, byte[] Theirs)?> AnnounceInCirclesAsync(
        IRendezvousDialer dialer, IReadOnlyList<RendezvousAddress> open, IReadOnlyList<RendezvousAddress> anchor,
        Announcement announcement, TimeSpan head, TimeSpan budget, CancellationToken ct)
    {
        // Un service des deux cercles n'est annoncé qu'une fois, et côté
        // ouvert : deux annonces d'un même client chez lui s'apparieraient
        // entre elles.
        var openKeys = open.Select(ServiceConsensus.Canonical).ToHashSet(StringComparer.Ordinal);
        var fallback = anchor.Where(place => openKeys.Contains(ServiceConsensus.Canonical(place)) is false).ToList();

        if (open.Count == 0 && fallback.Count == 0)
            return null;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(budget);

        var openExhausted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var openLeft = new StrongBox<int>(open.Count);

        if (open.Count == 0)
            openExhausted.TrySetResult();

        async Task<(RendezvousAddress At, byte[] Theirs)?> TryAnnounceAsync(RendezvousAddress place, bool isOpen)
        {
            try
            {
                if (isOpen is false)
                {
                    await Task.WhenAny(Task.Delay(head, deadline.Token), openExhausted.Task).ConfigureAwait(false);
                    deadline.Token.ThrowIfCancellationRequested();
                }

                var theirs = await dialer.AnnounceAsync(place, announcement, deadline.Token).ConfigureAwait(false);

                return theirs is null ? null : (place, theirs);
            }
            catch (Exception)
            {
                // Un service injoignable n'est pas une erreur : c'est
                // précisément ce à quoi sert d'en avoir plusieurs. Rattrapé ici
                // pour qu'aucune tâche ne se termine en faute, dont l'exception
                // resterait non observée après qu'une autre a gagné.
                return null;
            }
        }

        async Task<(RendezvousAddress At, byte[] Theirs)?> AttemptAsync(RendezvousAddress place, bool isOpen)
        {
            var result = await TryAnnounceAsync(place, isOpen).ConfigureAwait(false);

            // Seul un échec libère l'ancrage. Un appariement ouvert ne doit
            // pas le faire : l'ancrage partirait avant que l'annulation qui
            // suit la victoire ait eu le temps de l'arrêter.
            if (isOpen && result is null && Interlocked.Decrement(ref openLeft.Value) == 0)
                openExhausted.TrySetResult();

            return result;
        }

        var attempts = open.Select(place => AttemptAsync(place, isOpen: true))
            .Concat(fallback.Select(place => AttemptAsync(place, isOpen: false)))
            .ToList();

        while (attempts.Count > 0)
        {
            var finished = await Task.WhenAny(attempts).ConfigureAwait(false);
            attempts.Remove(finished);

            if (await finished.ConfigureAwait(false) is { } match)
            {
                // Le premier gagne : les autres n'ont plus lieu d'être, et leur
                // annulation ferme leurs connexions.
                await deadline.CancelAsync().ConfigureAwait(false);
                return match;
            }
        }

        return null;
    }
```

Dans `ConnectAsync`, remplacer le test d'entrée et l'appel d'annonce :

```csharp
        var open = circle?.PlacesFor(pair) ?? [];

        if (pair.Rendezvous.Count == 0 && open.Count == 0)
            return new ConnectionAttempt(null, false, "aucun lieu de rendez-vous enregistré pour ce pair");
```

```csharp
        var match = await AnnounceInCirclesAsync(
            dialer, open, pair.Rendezvous, announcement, OpenHead, _announceBudget, ct).ConfigureAwait(false);
```

Juste après le bloc `if (match is null) { ... }` :

```csharp
        var viaOpen = open.Contains(match.Value.At);
        log.Info($"{pair.DisplayName} : apparié sur {match.Value.At}{(viaOpen ? " (cercle ouvert)" : "")}.");
```

Passer ensuite `Via: match.Value.At` aux deux constructions de réussite, `new ConnectionAttempt(link, false, null)` et `new ConnectionAttempt(relayed, false, null)`.

- [ ] **Step 4 : vérifier le succès, puis toute la suite**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected : PASS, y compris les tests existants d'`AnnounceEverywhereAsync`. Relancer trois fois `--filter PeerConnectorTests` pour débusquer un test instable.

- [ ] **Step 5 : commit**

```bash
git add Linkpearl/Core/Sync/PeerConnector.cs Linkpearl.Core.Tests/Sync/PeerConnectorTests.cs
git commit -m "feat(fédération): annonce sur le cercle ouvert d'abord, l'ancrage en repli"
```

### Task 12 : récupérer la liste, et la brancher

Dépôt : **PLUGIN**.

**Files :**
- Modify : `Linkpearl/Core/Transport/Rendezvous/RendezvousClient.cs` (après `QueryBanListAsync`, l.175-207)
- Create : `Linkpearl/Core/Sync/ConsensusKeys.cs`
- Modify : `Linkpearl/Core/Sync/RendezvousList.cs` (constante `Authority`)
- Create : `Linkpearl/Integration/ConsensusFetcher.cs`
- Modify : `Linkpearl/Configuration.cs` (propriété `OpenCircle`)
- Modify : `Linkpearl/Plugin.cs`, à six endroits :
  - les champs, vers l.110 ;
  - la construction, vers l.229-232 ;
  - `new PeerConnector(`, l.667 ;
  - `Start`, vers l.415 ;
  - `Dispose`, vers l.1695 ;
  - la construction de `SettingsPage`.
- Modify : `Linkpearl/Ui/Pages/SettingsPage.cs` (constructeur l.42-44, `DrawNetwork`)

`RendezvousClient.cs` ne fait pas partie de la copie du protocole : il n'y a rien à recopier.

**Interfaces :**
- Consumes : `RendezvousWire.ConsensusQuery`, `RendezvousWire.TryReadConsensusPage`, `OpenCircle.Offer`, `OpenCircle.Current`, `OpenCircle.Enabled`
- Produces :
  - `Task<(byte[]? Document, string? Failure)> RendezvousClient.QueryConsensusAsync(CancellationToken ct)`
  - `ConsensusKeys.Trusted` (`IReadOnlyList<byte[]>`)
  - `RendezvousList.Authority` (`RendezvousAddress`)
  - `sealed class ConsensusFetcher(OpenCircle circle, string path, IPluginLog log) : IDisposable`, avec `Start()` et `RefreshSoon()`
  - `Configuration.OpenCircle` (`bool`, vrai par défaut)

- [ ] **Step 1 : le client de pages**

Dans `RendezvousClient.cs`, après `QueryBanListAsync` :

```csharp
    /// <summary>
    /// Récupère la liste signée du cercle ouvert, page après page.
    /// </summary>
    /// <remarks>
    /// Rend les octets bruts : rien n'est cru avant la vérification de la
    /// signature, faite par l'appelant. Une page manquante ou incohérente
    /// rend un échec lisible, jamais une exception.
    /// </remarks>
    public async Task<(byte[]? Document, string? Failure)> QueryConsensusAsync(CancellationToken ct)
    {
        using var document = new MemoryStream();
        var total = 1;

        for (var page = 0; page < total; page++)
        {
            await SendAsync(RendezvousWire.ConsensusQuery(page), ct).ConfigureAwait(false);

            var frame = await ReadFrameAsync(ct).ConfigureAwait(false);

            if (frame is null)
                return (null, "connexion fermée par le service");

            if (frame[0] == RendezvousKind.Error)
                return (null, System.Text.Encoding.UTF8.GetString(frame.AsSpan(1)));

            if (RendezvousWire.TryReadConsensusPage(frame, out var index, out var count, out var chunk, out var why) is false)
                return (null, why);

            if (index != page || (page > 0 && count != total))
                return (null, "pages incohérentes");

            total = count;
            document.Write(chunk);
        }

        return (document.ToArray(), null);
    }
```

- [ ] **Step 2 : la clé de confiance et l'adresse de l'autorité**

`Linkpearl/Core/Sync/ConsensusKeys.cs` :

```csharp
namespace Linkpearl.Core.Sync;

/// <summary>Les clés publiques dont le plugin accepte la liste signée.</summary>
/// <remarks>
/// Vide tant que l'autorité n'a pas démarré en production : sans clé, aucune
/// liste n'est acceptée et tout passe par l'ancrage, comme avant le cercle
/// ouvert. Changer de clé exige une nouvelle version du plugin, et c'est voulu :
/// personne ne peut désigner une autre autorité à distance.
/// </remarks>
public static class ConsensusKeys
{
    public static readonly IReadOnlyList<byte[]> Trusted = [];
}
```

Dans `RendezvousList.cs`, à côté de `DefaultHost` :

```csharp
    /// <summary>L'autorité du cercle ouvert : le service du projet, sur son port habituel.</summary>
    public static readonly RendezvousAddress Authority = new(DefaultHost, RendezvousAddress.DefaultPort);
```

- [ ] **Step 3 : la récupération périodique**

`Linkpearl/Integration/ConsensusFetcher.cs` :

```csharp
using Dalamud.Plugin.Services;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Integration;

/// <summary>
/// Redemande la liste signée à l'autorité toutes les six heures.
/// </summary>
/// <remarks>
/// La dernière liste acceptée est gardée sur disque : un démarrage sans
/// réseau, ou avec l'autorité tombée, repart de là tant qu'elle n'a pas
/// expiré. Un échec ne retire jamais la liste en place.
/// </remarks>
public sealed class ConsensusFetcher(OpenCircle circle, string path, IPluginLog log) : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _life = new();
    private readonly SemaphoreSlim _wake = new(0, 1);

    public void Start()
    {
        LoadSaved();
        _ = Task.Run(() => LoopAsync(_life.Token));
    }

    public void RefreshSoon()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Déjà réveillé : une seule récupération suffit.
        }
    }

    private void LoadSaved()
    {
        try
        {
            if (File.Exists(path) && circle.Offer(File.ReadAllBytes(path), out var why) is false)
                log.Info($"Liste signée enregistrée écartée : {why}");
        }
        catch (IOException e)
        {
            log.Warning($"Liste signée enregistrée illisible : {e.Message}");
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (ct.IsCancellationRequested is false)
        {
            await FetchAsync(ct).ConfigureAwait(false);

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

    private async Task FetchAsync(CancellationToken ct)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(Patience);

            await using var client = new RendezvousClient();
            await client.ConnectAsync(RendezvousList.Authority.Host, RendezvousList.Authority.Port, deadline.Token).ConfigureAwait(false);

            var (document, failure) = await client.QueryConsensusAsync(deadline.Token).ConfigureAwait(false);

            if (document is null)
            {
                log.Info($"Liste signée indisponible : {failure}");
                return;
            }

            if (circle.Offer(document, out var why) is false)
            {
                log.Warning($"Liste signée refusée : {why}");
                return;
            }

            var temporary = path + ".part";
            File.WriteAllBytes(temporary, document);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (ct.IsCancellationRequested is false)
        {
            log.Info($"Autorité du cercle ouvert injoignable : {e.Message}");
        }
    }

    public void Dispose()
    {
        _life.Cancel();
        _life.Dispose();
        _wake.Dispose();
    }
}
```

- [ ] **Step 4 : le réglage**

Dans `Configuration.cs`, à côté de `Rendezvous` :

```csharp
    /// <summary>
    /// Passer par le cercle ouvert pour les pairs déjà épinglés.
    /// </summary>
    /// <remarks>
    /// Activé par défaut : il ne voit jamais passer une clé, et il répartit la
    /// charge et le relais. Désactivé, tout passe par les services de la liste.
    /// </remarks>
    public bool OpenCircle { get; set; } = true;
```

- [ ] **Step 5 : le câblage dans `Plugin.cs`**

Champs, à côté de `_banFetcher` :

```csharp
    private readonly OpenCircle _openCircle;
    private readonly ConsensusFetcher _consensusFetcher;
```

Construction, juste après `_banFetcher = new ServiceBanFetcher(...)` (l.230) :

```csharp
        _openCircle = new OpenCircle(ConsensusKeys.Trusted, _clock) { Enabled = _configuration.OpenCircle };
        _consensusFetcher = new ConsensusFetcher(_openCircle, Path.Combine(root, "consensus.bin"), Log);
```

`root` est la variable locale posée en l.204. `_clock` est un initialiseur de champ (`private readonly SystemClock _clock = new();`, l.169), donc déjà prêt à ce point.

Dans `new PeerConnector(` (l.667), ajouter en dernier l'argument nommé :

```csharp
            new PeerConnector(
                _links,
                new RendezvousEndpoint(_configuration.RendezvousHost, _configuration.RendezvousPort),
                _clock,
                new PluginLogSink(Log, "moteur"),
                circle: _openCircle),
```

Ajouter `_consensusFetcher.Start();` après `_banFetcher.Start();` (l.415), et `_consensusFetcher.Dispose();` après `_banFetcher.Dispose();` (l.1695).

- [ ] **Step 6 : la page Réglages**

Ajouter le paramètre `OpenCircle openCircle` à la fin du constructeur primaire de `SettingsPage`, et passer `_openCircle` là où `Plugin.cs` construit la page. Dans `DrawNetwork`, après la liste des services :

```csharp
        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        var open = configuration.OpenCircle;

        if (Toggle.Draw("Cercle ouvert", ref open, "open_circle",
                        hint: "Pour les pairs déjà reconnus, passer par des services admis automatiquement "
                            + "après trois jours d'observation. Ils ne voient jamais passer une clé : le premier "
                            + "pairage reste sur les services de la liste ci-dessus."))
        {
            configuration.OpenCircle = open;
            configuration.Save();
            openCircle.Enabled = open;
        }

        Text.Small(openCircle.Current is { } list
            ? $"{list.Entries.Count} service(s) ouvert(s), liste valable jusqu'au {DateTimeOffset.FromUnixTimeSeconds(list.Expires).ToLocalTime():d MMMM HH:mm}."
            : "Aucune liste valable : tout passe par les services ci-dessus.", Theme.TextFaint);
```

- [ ] **Step 7 : vérifier**

Run : `dotnet build Linkpearl/Linkpearl.csproj -c Release && dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected : build sans warning, tests PASS.

- [ ] **Step 8 : commit**

```bash
git add Linkpearl
git commit -m "feat(fédération): récupération de la liste signée et réglage du cercle ouvert"
```

### Task 13 : le harnais

Dépôt : **PLUGIN**.

**Files :**
- Create : `Linkpearl.Harness/OpenCircleRun.cs`
- Modify : `Linkpearl.Harness/Program.cs` (dispatch l.55-78)

**Interfaces :**
- Consumes :
  - comme modèle : `FederationRun.ScenarioAsync` (l.70-167) et son aide `Pair(...)` (l.194-205) ;
  - `ConsoleLog`, `ILogSink`, `OpenCircle`, `ServiceConsensus.Sign` et `RendezvousClient.QueryConsensusAsync`.
- Produces : la commande `dotnet run --project Linkpearl.Harness -c Release -- cercle-ouvert --cle-autorite HEX [--rdv-ouverts H1,H2,H3] [--rdv-ancre H4] [--rdv-autorite H5] [--timeout 60]`

- [ ] **Step 1 : le compteur de journal**

Dans `OpenCircleRun.cs` :

```csharp
/// <summary>Journal de console qui compte les appariements passés par le cercle ouvert.</summary>
internal sealed class CountingLog(string who) : ILogSink
{
    private readonly ConsoleLog _inner = new(who);
    private int _open;

    public int OpenMatches => Volatile.Read(ref _open);

    public void Debug(string message) => _inner.Debug(message);

    public void Info(string message)
    {
        if (message.Contains("(cercle ouvert)", StringComparison.Ordinal))
            Interlocked.Increment(ref _open);

        _inner.Info(message);
    }

    public void Warning(string message, Exception? exception = null) => _inner.Warning(message, exception);
}
```

Si `ILogSink` déclare d'autres membres que `Debug`, `Info` et `Warning`, les déléguer de la même façon à `_inner`.

- [ ] **Step 2 : le scénario**

`OpenCircleRun.ExecuteAsync` reprend la structure de `FederationRun` : mêmes identités, même carnet, même pompe `PeerLinkFactory`, même boucle `TickAsync` jusqu'à `narrator.Applications > 0` ou l'échéance. Il enchaîne trois vérifications :

1. **La liste de l'autorité réelle.**
   - Se connecter à `--rdv-autorite`, appeler `QueryConsensusAsync`, puis vérifier avec `ServiceConsensus.TryVerify(document, [Convert.FromHexString(cleAutorite)], maintenant, ...)`.
   - Afficher « liste de l'autorité vérifiée : version N, M service(s) », et échouer si la vérification échoue.
   - Une liste vide est attendue : aucun service n'a encore fait ses 72 heures.
2. **Le scénario « cercle ouvert ».**
   - Engendrer une clé avec `ECDsa.Create(ECCurve.NamedCurves.nistP256)`, puis signer une liste des trois `--rdv-ouverts`, avec une famille différente par service : `Enumerable.Repeat((byte)(i + 1), 8)`.
   - Chaque client reçoit un `new OpenCircle([ServiceConsensus.PublicPoint(clé)], horloge)` qui a accepté ce document par `Offer`, et un `PeerConnector(liens, point, horloge, sonCountingLog, circle: sonCercle)`.
   - Le carnet des deux clients n'a pour ancrage que `[--rdv-ancre]`.
   - Attendu : l'apparence est appliquée, **et** `OpenMatches > 0` pour au moins un des deux.
3. **Le scénario « repli ».**
   - Même paire, mais la liste signée ne contient que deux services morts, de familles différentes : `127.0.0.1:47998` et `127.0.0.1:47997`.
   - Attendu : l'apparence est appliquée, **et** `OpenMatches == 0` pour les deux.

Afficher « TOUT EST PASSÉ » si les trois tiennent. Sinon, poser `Environment.ExitCode = 1`, comme `FederationRun`.

- [ ] **Step 3 : le dispatch**

Dans `Program.cs`, à côté de `if (args[0] == "federation")`, ajouter une branche `cercle-ouvert` qui lit les options avec les aides `ArgString` locales. Défauts :
- `--rdv-ouverts 127.0.0.1:47911,127.0.0.1:47912,127.0.0.1:47913` ;
- `--rdv-ancre 127.0.0.1:47914` ;
- `--rdv-autorite 127.0.0.1:47915` ;
- `--timeout 60`.

`--cle-autorite` n'a pas de défaut : elle est obligatoire, avec un message clair si elle manque.

- [ ] **Step 4 : lancer contre cinq services locaux**

Chaque service tourne dans son propre dossier temporaire, pour qu'ils ne partagent ni `admin.token` ni `authority.json` :

```bash
for p in 47911 47912 47913 47914; do (d=$(mktemp -d); cd $d && dotnet run --project /home/yrapenne/Projects/linkpearl-sync/rendezvous/Linkpearl.Rendezvous -c Release -- --port $p --no-admin) & done
(d=$(mktemp -d); cd $d && dotnet run --project /home/yrapenne/Projects/linkpearl-sync/rendezvous/Linkpearl.Rendezvous -c Release -- --port 47915 --no-admin --directory-authority) &
```

Relever la clé publique qu'affiche le cinquième (« Autorité du cercle ouvert, clé publique ... »), puis, depuis PLUGIN :

```bash
dotnet run --project Linkpearl.Harness -c Release -- cercle-ouvert --cle-autorite <clé relevée>
```

Expected : « TOUT EST PASSÉ ». Arrêter ensuite les cinq services.

- [ ] **Step 5 : commit**

```bash
git add Linkpearl.Harness
git commit -m "test(harnais): scénario du cercle ouvert et de son repli"
```

### Task 14 : la documentation des deux dépôts

Dépôts : **PLUGIN** et **RDV**.

**Files :**
- Modify : `PLUGIN/docs/superpowers/specs/2026-09-22-federation-design.md`
- Modify : `PLUGIN/docs/superpowers/specs/2026-09-26-cercle-ouvert-design.md`
- Modify : `PLUGIN/docs/protocol.md`
- Modify : `RDV/CLAUDE.md`, `RDV/README.md`

- [ ] **Step 1 : amender la conception de fédération**

La section « Ce que cette conception rouvre » de la spec du cercle ouvert cite trois décisions de `2026-09-22-federation-design.md`. Sous chacune d'elles, ajouter un paragraphe **« Amendé le 26 septembre 2026 »** qui reprend la nouvelle formulation et renvoie à `2026-09-26-cercle-ouvert-design.md`.

- [ ] **Step 2 : reporter les écarts dans la spec du cercle ouvert**

Ajouter à la fin de `2026-09-26-cercle-ouvert-design.md` une section « Corrigé à la mise en œuvre ». Elle reprend les huit points de la section « Écarts assumés avec la spec » de ce plan, sur le même ton que la section du même nom de `rendezvous/docs/specs/2026-09-23-console-et-moderation.md`.

- [ ] **Step 3 : `docs/protocol.md`**

- Décrire les deux cercles, et ce qui passe par chacun.
- Ajouter les trames `0x17` et `0x18` au tableau des trames de rendez-vous.
- Décrire le format du document, la famille et le placement : `"linkpearl:place:v1"`, deux familles distinctes, pas de rotation.
- Ajouter une ligne au tableau des adversaires :

  | Adversaire | Peut | Ne peut pas |
  |---|---|---|
  | Service malveillant du cercle ouvert | refuser, mentir sur une annonce, observer qui relaie avec qui | voir une clé, s'intercaler dans un pairage |

- Ajouter les nouvelles constantes au tableau des plafonds.

- [ ] **Step 4 : le service**

- `RDV/CLAUDE.md`, règle 2 : ajouter que la seule exception est le rôle d'autorité. Il fait foi sur le cercle ouvert, et seulement sur lui ; il ne voit toujours ni clé, ni nom, ni manifeste.
- `RDV/CLAUDE.md`, section Déploiement : `directory.key` et `authority.json` vivent dans `/var/lib/lprdv`. `directory.key` se garde comme `bans.json` : ne jamais l'écraser ni la régénérer.
- `RDV/README.md` : documenter `--directory-authority`, `--directory-key` et `--authority-state`, la section « Cercle ouvert » de la console, et les deux routes `/api/authority/veto`.

- [ ] **Step 5 : vérifier l'absence de tiret cadratin**

Le motif se construit avec `printf` pour ne pas écrire le caractère lui-même :

```bash
grep -rn "$(printf '\342\200\224')" /home/yrapenne/Projects/linkpearl-sync/plugin/docs /home/yrapenne/Projects/linkpearl-sync/plugin/Linkpearl /home/yrapenne/Projects/linkpearl-sync/rendezvous/Linkpearl.Rendezvous /home/yrapenne/Projects/linkpearl-sync/rendezvous/README.md /home/yrapenne/Projects/linkpearl-sync/rendezvous/CLAUDE.md
```

Expected : aucune sortie.

- [ ] **Step 6 : commits**

```bash
cd /home/yrapenne/Projects/linkpearl-sync/plugin && git add docs && git commit -m "docs(fédération): deux cercles, et les décisions amendées"
cd /home/yrapenne/Projects/linkpearl-sync/rendezvous && git add CLAUDE.md README.md && git commit -m "docs(autorité): rôle d'autorité du cercle ouvert"
```

### Task 15 : déployer l'autorité et inscrire sa clé

Dépôts : **RDV**, puis **PLUGIN**.

**Cette tâche touche la production.** Chaque étape marquée ⚠ attend l'accord explicite de l'utilisateur, demandé au moment de l'exécuter.

**Files :**
- Modify : `RDV/deploy/lprdv.service` (ligne `ExecStart`)
- Modify : `PLUGIN/Linkpearl/Core/Sync/ConsensusKeys.cs`

- [ ] **Step 1 : activer le rôle dans l'unité systemd**

Ajouter `--directory-authority` à la ligne `ExecStart` de `deploy/lprdv.service`. Grâce à `WorkingDirectory`, les chemins par défaut (`directory.key` et `authority.json`) tombent dans `/var/lib/lprdv`. Commit : `feat(deploy): le service de production tient le rôle d'autorité`.

- [ ] **Step 2 : ⚠ publier une version et déployer**

Avec l'accord de l'utilisateur : poser le tag `vX.Y.Z` sur `main`, le pousser, attendre la release, puis :

```bash
LPRDV_HOST=debian@rdv.linkpearl.eorzea.events LPRDV_KEY=~/.ssh/linkpearl_rdv ./deploy/deploy.sh
```

- [ ] **Step 3 : relever la clé publique**

```bash
ssh -i ~/.ssh/linkpearl_rdv debian@rdv.linkpearl.eorzea.events 'journalctl -u lprdv | grep "clé publique" | tail -1'
```

- [ ] **Step 4 : ⚠ sauvegarder `directory.key` hors du VPS**

Proposer à l'utilisateur de copier `/var/lib/lprdv/directory.key` dans un endroit sûr de son choix. C'est une clé privée : ne rien copier sans son accord.

- [ ] **Step 5 : inscrire la clé dans le plugin**

```csharp
    public static readonly IReadOnlyList<byte[]> Trusted =
    [
        // Autorité de rdv.linkpearl.eorzea.events, engendrée le <date du jour>.
        Convert.FromHexString("<clé relevée à l'étape 3>"),
    ];
```

Ajouter dans `OpenCircleTests` le test suivant. Une clé mal collée serait sinon ignorée sans bruit.

```csharp
    [Fact]
    public void Les_cles_de_confiance_sont_des_points_publics_complets()
        => Assert.All(ConsensusKeys.Trusted, key =>
        {
            Assert.Equal(ServiceConsensus.PublicPointSize, key.Length);
            Assert.Equal(0x04, key[0]);
        });
```

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Commit : `feat(fédération): clé de l'autorité de production`.

- [ ] **Step 6 : vérifier de l'extérieur**

Depuis PLUGIN, lancer en parallèle, comme le prescrit `RDV/CLAUDE.md` :

```bash
dotnet run --project Linkpearl.Harness -c Release -- rdv rdv.linkpearl.eorzea.events 47900 alice
dotnet run --project Linkpearl.Harness -c Release -- rdv rdv.linkpearl.eorzea.events 47900 bob
```

Expected : les deux finissent par « TOUT EST PASSÉ ». Ouvrir ensuite la console par `ssh -L 47901:127.0.0.1:47901` : la section « Cercle ouvert » doit montrer la même clé et une version émise.
