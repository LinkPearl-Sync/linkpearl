# Fédération des instances : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permettre à deux joueurs réglés sur des services de rendez-vous
différents de se détecter, se pairer et se synchroniser.

**Architecture:** La fédération vit dans le client. Le plugin tient une liste de
services de rendez-vous, ouvre sa boîte sur chacun, interroge tous ceux qui
répondent, et s'annonce en parallèle sur tous les lieux qu'un pair partage avec
lui. Les serveurs ne se parlent pas, à une exception près et dans un seul sens :
un serveur peut déposer sa candidature dans la file d'attente d'un annuaire, que
son opérateur approuve à la main.

**Tech Stack:** C# / .NET 10, xUnit, LiteNetLib, Dalamud SDK 15. Deux dépôts :
`Linkpearl` (noyau, plugin, harnais) et `linkpearl-rendezvous` (serveur).

**Spec:** `docs/superpowers/specs/2026-09-22-federation-design.md`

## Global Constraints

- **`Linkpearl/Core/` ne référence jamais Dalamud.** `ArchitectureTests` le
  vérifie. Tout ce qui décide vit dans `Core/` et se teste sous Linux.
- **Le noyau ne lit jamais l'horloge système.** `DateTime.UtcNow` et
  `DateTimeOffset.UtcNow` sont interdits dans `Core/`, test à l'appui. L'heure
  passe par `IClock`.
- **Toute donnée venant du réseau passe par `Core/Safety` ou par une validation
  explicite avant d'atteindre un IPC, un fichier ou l'écran.**
- **Pas de tiret cadratin** (le caractère `—`), ni dans le code, ni dans les
  commentaires, ni dans les messages de commit.
- **Jamais de ligne `Co-Authored-By:` ni `Claude-Session:`** dans un message de
  commit, ni d'URL de session, ni de mention « Generated with ».
- **Commits en Conventional Commits, sujet en français** : `feat(core):`,
  `fix(transport):`, `chore(build):`.
- Commentaires en français, qui expliquent le *pourquoi*. Les constantes issues
  d'une mesure citent la mesure.
- Préférer `record` et `readonly record struct` pour les types de données.
  Jamais d'`enum` pour un état qui traverse le réseau.
- **Les trois fichiers de protocole sont copiés à l'octet près** entre les deux
  dépôts : `RendezvousWire.cs`, `RendezvousTicket.cs`, `IClock.cs`.
  `rendezvous-vectors.json` et `RendezvousVectorTests.cs` sont eux aussi
  identiques des deux côtés, et `diff` doit rester vide.
- Le port par défaut d'un service de rendez-vous est **47900**.

**Commandes de vérification :**

```sh
dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj    # le noyau, sous Linux
dotnet build Linkpearl/Linkpearl.csproj -c Release              # doit passer sans warning
dotnet build Linkpearl.Harness/Linkpearl.Harness.csproj
```

Dans `~/Projects/linkpearl-rendezvous` :

```sh
dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj
dotnet build Linkpearl.Rendezvous/Linkpearl.Rendezvous.csproj -c Release
```

---

## Structure des fichiers

**Dépôt `Linkpearl`**

| Fichier | Responsabilité |
|---|---|
| `Linkpearl/Core/Transport/Rendezvous/RendezvousAddress.cs` | *créé* : hôte et port, lecture et écriture |
| `Linkpearl/Core/Transport/Rendezvous/RendezvousWire.cs` | *modifié* : trames d'annuaire et de candidature |
| `Linkpearl/Core/Identity/PairBook.cs` | *modifié* : `Rendezvous` remplace `RendezvousHost` |
| `Linkpearl/Core/Identity/InvitationTicketText.cs` | *créé* : le ticket collable, avec son hôte |
| `Linkpearl/Core/Sync/PeerConnector.cs` | *modifié* : annonce parallèle sur plusieurs lieux |
| `Linkpearl/Core/Sync/RendezvousList.cs` | *créé* : la liste de services et sa migration |
| `Linkpearl/Integration/PairBookStore.cs` | *modifié* : lecture de l'ancien format |
| `Linkpearl/Integration/PresenceService.cs` | *modifié* : une session par service |
| `Linkpearl/Configuration.cs` | *modifié* : liste de services, version 2 |
| `Linkpearl/Ui/Pages/SettingsPage.cs` | *modifié* : liste, ajout, retrait, découverte |
| `Linkpearl.Harness/FederationRun.cs` | *créé* : deux services, deux clients |

**Dépôt `linkpearl-rendezvous`**

| Fichier | Responsabilité |
|---|---|
| `Protocol/Core/Transport/Rendezvous/RendezvousWire.cs` | *recopié* |
| `Protocol/rendezvous-vectors.json` | *recopié* |
| `Linkpearl.Rendezvous/Directory.cs` | *créé* : fichier de pairs relu à chaud, file de candidatures |
| `Linkpearl.Rendezvous/RendezvousServer.cs` | *modifié* : traite les trames d'annuaire |
| `Linkpearl.Rendezvous/Program.cs` | *modifié* : `--peers`, `--pending`, `--announce-to` |
| `Linkpearl.Rendezvous.Tests/DirectoryTests.cs` | *créé* |

---

### Task 1 : l'adresse d'un service de rendez-vous

**Files:**
- Create: `Linkpearl/Core/Transport/Rendezvous/RendezvousAddress.cs`
- Test: `Linkpearl.Core.Tests/Rendezvous/RendezvousAddressTests.cs`

**Interfaces:**
- Consumes: rien.
- Produces: `readonly record struct RendezvousAddress(string Host, int Port)`,
  `const int RendezvousAddress.DefaultPort = 47900`,
  `static bool TryParse(string?, out RendezvousAddress, out string?)`,
  `override string ToString()`.

- [ ] **Step 1: Écrire le test qui échoue**

Créer `Linkpearl.Core.Tests/Rendezvous/RendezvousAddressTests.cs` :

```csharp
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Rendezvous;

/// <summary>
/// L'adresse d'un service de rendez-vous. Le port fait partie de l'adresse et
/// non d'un réglage global : la documentation recommande le 443 pour les
/// réseaux restrictifs, donc deux services peuvent écouter ailleurs.
/// </summary>
public class RendezvousAddressTests
{
    [Fact]
    public void Un_hote_seul_prend_le_port_par_defaut()
    {
        Assert.True(RendezvousAddress.TryParse("rdv.exemple.ch", out var address, out var why), why);
        Assert.Equal("rdv.exemple.ch", address.Host);
        Assert.Equal(RendezvousAddress.DefaultPort, address.Port);
    }

    [Fact]
    public void Un_port_explicite_est_retenu()
    {
        Assert.True(RendezvousAddress.TryParse("rdv.exemple.ch:443", out var address, out _));
        Assert.Equal(443, address.Port);
    }

    [Fact]
    public void Le_port_par_defaut_ne_s_ecrit_pas()
    {
        // Sans cela, la liste des services afficherait « :47900 » partout, et
        // l'utilisateur croirait à un réglage qu'il doit comprendre.
        Assert.Equal("rdv.exemple.ch", new RendezvousAddress("rdv.exemple.ch", 47900).ToString());
        Assert.Equal("rdv.exemple.ch:443", new RendezvousAddress("rdv.exemple.ch", 443).ToString());
    }

    [Fact]
    public void L_ecriture_se_relit()
    {
        var original = new RendezvousAddress("192.0.2.10", 8080);
        Assert.True(RendezvousAddress.TryParse(original.ToString(), out var back, out _));
        Assert.Equal(original, back);
    }

    [Theory]
    [InlineData(null, "adresse vide")]
    [InlineData("", "adresse vide")]
    [InlineData("   ", "adresse vide")]
    [InlineData("rdv.exemple.ch:0", "port")]
    [InlineData("rdv.exemple.ch:70000", "port")]
    [InlineData("rdv.exemple.ch:abc", "port")]
    [InlineData("rdv exemple.ch", "caractère")]
    [InlineData("http://rdv.exemple.ch", "caractère")]
    [InlineData("pair@rdv.exemple.ch", "caractère")]
    public void Une_adresse_malformee_est_refusee_avec_sa_raison(string? text, string expected)
    {
        Assert.False(RendezvousAddress.TryParse(text, out _, out var why));
        Assert.Contains(expected, why);
    }

    [Fact]
    public void Un_hote_demesure_est_refuse()
    {
        // Le libellé et l'hôte viennent du réseau dans la trame d'annuaire : la
        // borne est ici, avant que quoi que ce soit ne s'affiche.
        Assert.False(RendezvousAddress.TryParse(new string('a', 300), out _, out var why));
        Assert.Contains("trop long", why);
    }
}
```

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter 'FullyQualifiedName~RendezvousAddressTests'`
Expected: échec de compilation, « The type or namespace name 'RendezvousAddress' could not be found ».

- [ ] **Step 3: Écrire l'implémentation minimale**

Créer `Linkpearl/Core/Transport/Rendezvous/RendezvousAddress.cs` :

```csharp
namespace Linkpearl.Core.Transport.Rendezvous;

/// <summary>
/// L'adresse d'un service de rendez-vous.
/// </summary>
/// <remarks>
/// Le port fait partie de l'adresse et non d'un réglage global, parce que la
/// fédération met plusieurs services en présence : la documentation recommande
/// déjà le 443 pour les réseaux restrictifs, donc deux d'entre eux peuvent
/// parfaitement écouter ailleurs.
///
/// La validation est stricte et sans traduction : un caractère inattendu fait
/// refuser plutôt que corriger. Cette chaîne arrive par la trame d'annuaire,
/// donc du réseau, et corriger reviendrait à valider une valeur puis à en
/// utiliser une autre.
/// </remarks>
public readonly record struct RendezvousAddress(string Host, int Port)
{
    public const int DefaultPort = 47900;

    private const int MaxHostLength = 255;

    public static bool TryParse(string? text, out RendezvousAddress address, out string? rejection)
    {
        address = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            rejection = "adresse vide";
            return false;
        }

        var cleaned = text.Trim();
        var separator = cleaned.LastIndexOf(':');

        var host = separator < 0 ? cleaned : cleaned[..separator];
        var port = DefaultPort;

        if (separator >= 0)
        {
            if (int.TryParse(cleaned[(separator + 1)..], out port) is false || port is < 1 or > 65535)
            {
                rejection = $"port hors bornes ou illisible : {cleaned[(separator + 1)..]}";
                return false;
            }
        }

        if (host.Length is 0)
        {
            rejection = "adresse vide";
            return false;
        }

        if (host.Length > MaxHostLength)
        {
            rejection = $"hôte trop long ({host.Length} caractères, maximum {MaxHostLength})";
            return false;
        }

        foreach (var c in host)
        {
            if (IsAllowed(c) is false)
            {
                // Le caractère fautif n'est pas repris dans la raison : il vient
                // du réseau et finirait dans un journal.
                rejection = $"caractère interdit en position {host.IndexOf(c)}";
                return false;
            }
        }

        address = new RendezvousAddress(host, port);
        rejection = null;
        return true;
    }

    /// <summary>Lettres, chiffres, point, tiret et deux-points des adresses IPv6.</summary>
    private static bool IsAllowed(char c)
        => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_';

    public override string ToString() => Port == DefaultPort ? Host : $"{Host}:{Port}";
}
```

- [ ] **Step 4: Lancer les tests et vérifier qu'ils passent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter 'FullyQualifiedName~RendezvousAddressTests'`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add Linkpearl/Core/Transport/Rendezvous/RendezvousAddress.cs \
        Linkpearl.Core.Tests/Rendezvous/RendezvousAddressTests.cs
git commit -m "feat(core): l'adresse d'un service de rendez-vous, port compris"
```

---

### Task 2 : le carnet garde une liste de lieux par pair

**Files:**
- Modify: `Linkpearl/Core/Identity/PairBook.cs` (champ `RendezvousHost`, méthode `Add`)
- Modify: `Linkpearl/Integration/PairBookStore.cs` (DTO et migration)
- Modify: `Linkpearl/Core/Sync/PeerConnector.cs:64` (lecture du champ)
- Modify: `Linkpearl/Integration/PairingService.cs` (appels à `Add`)
- Test: `Linkpearl.Core.Tests/Sync/SyncPiecesTests.cs` (classe `PairBookTests`)

**Interfaces:**
- Consumes: `RendezvousAddress` de la tâche 1.
- Produces: `PairRecord.Rendezvous` de type
  `IReadOnlyList<RendezvousAddress>`, en remplacement de `RendezvousHost` ;
  `PairBook.Add(PeerId, byte[], ReadOnlySpan<byte>, PeerId, string, IReadOnlyList<RendezvousAddress>)`.

- [ ] **Step 1: Écrire le test qui échoue**

Ajouter dans `Linkpearl.Core.Tests/Sync/SyncPiecesTests.cs`, classe `PairBookTests` :

```csharp
    [Fact]
    public void Un_pair_garde_plusieurs_lieux_de_rendez_vous()
    {
        // C'est ce qui fait survivre un pairage à la mort d'un service : les
        // deux côtés s'annoncent sur tous les lieux partagés et se trouvent sur
        // le premier qui répond.
        var book = New();
        var (code, ours, theirKey) = Invitation();

        var lieux = new[]
        {
            new RendezvousAddress("rdv.ami.ch", 47900),
            new RendezvousAddress("rdv.exemple.ch", 443),
        };

        var record = book.Add(code.Id, theirKey, code.PairingNonce, ours, "Amie", lieux);

        Assert.Equal(lieux, record.Rendezvous);
    }

    [Fact]
    public void L_ordre_des_lieux_est_conserve()
    {
        // Le premier sert de relais préféré : le réordonner silencieusement
        // changerait par qui transitent les octets.
        var book = New();
        var (code, ours, theirKey) = Invitation();

        var lieux = new[]
        {
            new RendezvousAddress("second.exemple.ch", 47900),
            new RendezvousAddress("premier.exemple.ch", 47900),
        };

        var record = book.Add(code.Id, theirKey, code.PairingNonce, ours, "Amie", lieux);

        Assert.Equal("second.exemple.ch", record.Rendezvous[0].Host);
    }
```

Ajouter `using Linkpearl.Core.Transport.Rendezvous;` en tête du fichier.

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter 'FullyQualifiedName~PairBookTests'`
Expected: échec de compilation, `Add` n'accepte pas ce type et `Rendezvous` n'existe pas.

- [ ] **Step 3: Modifier `PairRecord` et `PairBook`**

Dans `Linkpearl/Core/Identity/PairBook.cs`, remplacer le champ :

```csharp
    /// <summary>
    /// Les lieux où ce pair et nous nous donnons rendez-vous, par ordre de
    /// préférence.
    /// </summary>
    /// <remarks>
    /// Une liste et non un service unique : c'est ce qui fait survivre un
    /// pairage à la disparition de l'un d'eux. Les deux côtés s'annoncent sur
    /// tous ceux qu'ils partagent, et se trouvent sur le premier qui répond.
    /// Le premier de la liste sert aussi de relais préféré.
    /// </remarks>
    public required IReadOnlyList<RendezvousAddress> Rendezvous { get; init; }
```

et ajouter `using Linkpearl.Core.Transport.Rendezvous;` en tête.

Changer la signature de `Add` :

```csharp
    public PairRecord Add(
        PeerId theirId, byte[] theirPublicKey, ReadOnlySpan<byte> pairingNonce,
        PeerId ourId, string displayName, IReadOnlyList<RendezvousAddress> rendezvous)
    {
        var record = new PairRecord
        {
            Id = theirId,
            PublicKey = theirPublicKey,
            PairSecret = PairSecret.Derive(pairingNonce, ourId, theirId),
            DisplayName = displayName,
            Rendezvous = rendezvous,
            Trust = PairTrust.Accepted,
            PairedAt = clock.UtcNow,
        };

        _pairs[record.Id] = record;
        return record;
    }
```

- [ ] **Step 4: Adapter les appelants**

Dans `Linkpearl/Core/Sync/PeerConnector.cs`, remplacer la ligne 64 :

```csharp
            await client.ConnectAsync(pair.Rendezvous[0].Host, pair.Rendezvous[0].Port, ct).ConfigureAwait(false);
```

C'est un état de transition : la tâche 9 remplacera cet appel par l'annonce
parallèle. Le noter dans le message de commit.

Dans `Linkpearl/Integration/PairingService.cs`, les trois appels à `_book.Add`
et le `PairRecord` construit ligne 188 passent
`[new RendezvousAddress(_configuration.RendezvousHost, _configuration.RendezvousPort)]`.
La tâche 7 leur donnera la vraie liste.

Dans les tests existants, remplacer chaque `RendezvousHost = "rdv.exemple.ch"`
par `Rendezvous = [new RendezvousAddress("rdv.exemple.ch", 47900)]`, et dans
`Linkpearl.Harness/FakePeerRun.cs:341` par
`Rendezvous = [new RendezvousAddress("127.0.0.1", 47900)]`.

- [ ] **Step 5: Migrer le stockage du carnet**

Dans `Linkpearl/Integration/PairBookStore.cs`, le DTO devient :

```csharp
    private sealed record Dto(
        string Id, string? PublicKey, string PairSecret, string DisplayName, string? RendezvousHost,
        int Trust, int Permissions, int Policy, bool Paused,
        long PairedAt, long? LastSeenAt, string? PinnedFingerprint,
        string[]? Rendezvous = null);
```

`RendezvousHost` devient nullable et reste lu : un carnet écrit par une version
précédente ne porte que lui. `Rendezvous` est en dernier et vaut null à
l'absence, ce que la désérialisation d'un enregistrement positionnel donne
naturellement.

Dans `Save`, écrire `record.Rendezvous.Select(a => a.ToString()).ToArray()` en
dernier argument, et `null` pour `RendezvousHost`.

Dans `Rehydrate`, lire :

```csharp
        // Migration : un carnet d'avant la fédération ne porte qu'un hôte, et
        // le port était alors une variable globale de la configuration.
        var rendezvous = dto.Rendezvous is { Length: > 0 } stored
            ? stored.Select(text => RendezvousAddress.TryParse(text, out var a, out _) ? a : (RendezvousAddress?)null)
                    .OfType<RendezvousAddress>()
                    .ToList()
            : dto.RendezvousHost is { } legacy && RendezvousAddress.TryParse(legacy, out var one, out _)
                ? [one]
                : [];

        if (rendezvous.Count == 0)
            return null;   // un pair sans lieu de rendez-vous est injoignable
```

- [ ] **Step 6: Lancer toute la suite**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: PASS, les tests existants compris.

Run: `dotnet build Linkpearl/Linkpearl.csproj -c Release`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(core): un pair garde une liste de lieux de rendez-vous"
```

---

### Task 3 : le ticket d'invitation porte son lieu

**Files:**
- Create: `Linkpearl/Core/Identity/InvitationTicketText.cs`
- Test: `Linkpearl.Core.Tests/Identity/InvitationTicketTests.cs`

**Interfaces:**
- Consumes: `InvitationTicket` (existant), `RendezvousAddress` (tâche 1).
- Produces: `static string InvitationTicketText.Encode(InvitationTicket, RendezvousAddress)`,
  `static bool InvitationTicketText.TryParse(string?, out InvitationTicket, out RendezvousAddress?, out string?)`.

- [ ] **Step 1: Écrire le test qui échoue**

Ajouter à `Linkpearl.Core.Tests/Identity/InvitationTicketTests.cs` :

```csharp
    [Fact]
    public void Un_ticket_colle_porte_le_lieu_ou_le_retirer()
    {
        var ticket = InvitationTicket.Create();
        var at = new RendezvousAddress("rdv.ami.ch", 443);

        var text = InvitationTicketText.Encode(ticket, at);

        Assert.True(InvitationTicketText.TryParse(text, out var back, out var where, out var why), why);
        Assert.Equal(ticket, back);
        Assert.Equal(at, where);
    }

    [Fact]
    public void Un_ticket_sans_lieu_reste_lisible()
    {
        // Les tickets émis avant la fédération n'ont pas de suffixe. Celui qui
        // colle retombe alors sur son premier service, ce que l'appelant décide.
        var ticket = InvitationTicket.Create();

        Assert.True(InvitationTicketText.TryParse(ticket.Encode(), out var back, out var where, out _));
        Assert.Equal(ticket, back);
        Assert.Null(where);
    }

    [Fact]
    public void Un_lieu_malforme_fait_refuser_le_ticket_entier()
    {
        // Refuser en entier plutôt que d'ignorer le suffixe : coller un ticket
        // vers un service illisible et le retirer silencieusement ailleurs
        // enverrait la demande à quelqu'un d'autre.
        var text = InvitationTicket.Create().Encode() + "@rdv exemple.ch";

        Assert.False(InvitationTicketText.TryParse(text, out _, out _, out var why));
        Assert.Contains("caractère", why);
    }

    [Fact]
    public void Les_espaces_et_tirets_de_recopie_sont_tolores()
    {
        var ticket = InvitationTicket.Create();
        var text = " " + InvitationTicketText.Encode(ticket, new RendezvousAddress("rdv.ami.ch", 47900)) + " ";

        Assert.True(InvitationTicketText.TryParse(text, out var back, out _, out _));
        Assert.Equal(ticket, back);
    }
```

Ajouter `using Linkpearl.Core.Transport.Rendezvous;` en tête du fichier.

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter 'FullyQualifiedName~InvitationTicketTests'`
Expected: échec de compilation, `InvitationTicketText` n'existe pas.

- [ ] **Step 3: Écrire l'implémentation**

Créer `Linkpearl/Core/Identity/InvitationTicketText.cs` :

```csharp
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Identity;

/// <summary>
/// La forme collable d'un ticket d'invitation : douze caractères, puis le lieu
/// où le retirer.
/// </summary>
/// <remarks>
/// Le ticket lui-même ne peut pas porter l'adresse : soixante bits sont déjà
/// tout ce qu'il contient. Le suffixe est donc ajouté au texte, exactement
/// comme le code de pairage long le fait déjà.
///
/// Celui qui colle voit où le ticket pointe avant de le remettre, et c'est là
/// qu'il décide s'il fait confiance à ce service. Le cacher reviendrait à lui
/// faire ouvrir une connexion vers un inconnu sans le lui dire.
/// </remarks>
public static class InvitationTicketText
{
    public static string Encode(InvitationTicket ticket, RendezvousAddress at)
        => $"{ticket.Encode()}@{at}";

    /// <summary>
    /// Lit un ticket collé. Le lieu vaut null quand le texte n'en porte pas,
    /// à charge de l'appelant de retomber sur son premier service.
    /// </summary>
    public static bool TryParse(
        string? text, out InvitationTicket ticket, out RendezvousAddress? at, out string? rejection)
    {
        ticket = default;
        at = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            rejection = "ticket vide";
            return false;
        }

        var cleaned = text.Trim();
        var separator = cleaned.IndexOf('@');

        if (separator >= 0)
        {
            // Refus en entier si le lieu est illisible : retirer le ticket
            // ailleurs enverrait la demande à quelqu'un d'autre.
            if (RendezvousAddress.TryParse(cleaned[(separator + 1)..], out var parsed, out var why) is false)
            {
                rejection = $"lieu de retrait illisible : {why}";
                return false;
            }

            at = parsed;
            cleaned = cleaned[..separator];
        }

        return InvitationTicket.TryParse(cleaned, out ticket, out rejection);
    }
}
```

- [ ] **Step 4: Lancer les tests et vérifier qu'ils passent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter 'FullyQualifiedName~InvitationTicketTests'`
Expected: PASS.

- [ ] **Step 5: Brancher le ticket dans le pairage**

Sans cette étape, le format existe mais personne ne l'émet ni ne le lit.

Dans `Linkpearl/Integration/PairingService.cs`, `CreateInvitationAsync` dépose le
ticket sur le premier service activé et rend le texte suffixé :

```csharp
        var at = _configuration.ActiveRendezvous.FirstOrDefault()?.Address;

        if (at is null)
            return (null, "aucun service de rendez-vous actif : ajoutez-en un dans les réglages");

        // Le texte porte le lieu : celui qui colle doit pouvoir retirer le
        // ticket là où il a été déposé, et non là où il a réglé le sien.
        return (InvitationTicketText.Encode(ticket, at.Value), null);
```

`RedeemAsync` se connecte au lieu que le ticket désigne, et non au sien :

```csharp
        if (InvitationTicketText.TryParse(text, out var ticket, out var at, out var why) is false)
            return $"ticket refusé : {why}";

        // Sans suffixe, le ticket vient d'avant la fédération : on retombe sur
        // notre premier service, qui est ce qu'il désignait implicitement.
        var where = at ?? _configuration.ActiveRendezvous.FirstOrDefault()?.Address;

        if (where is null)
            return "aucun service de rendez-vous actif : ajoutez-en un dans les réglages";

        await client.ConnectAsync(where.Value.Host, where.Value.Port, ct).ConfigureAwait(false);
```

Et le pair enregistré retient **les deux lieux**, celui du ticket et le nôtre,
dans cet ordre : c'est ce qui fait survivre le pairage à la mort de l'un d'eux.

```csharp
        var places = new List<RendezvousAddress> { where.Value };

        foreach (var entry in _configuration.ActiveRendezvous)
            if (places.Contains(entry.Address) is false)
                places.Add(entry.Address);

        _book.Add(theirId, theirKey, nonce, _identity.Id, displayName, places);
```

Appliquer la même construction dans `AddFromRequest` et à la ligne 188.

- [ ] **Step 6: Compiler**

Run: `dotnet build Linkpearl/Linkpearl.csproj -c Release`
Expected: 0 Warning(s).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(core): le ticket d'invitation porte le lieu où le retirer"
```

---

### Task 4 : les trames d'annuaire et de candidature

**Files:**
- Modify: `Linkpearl/Core/Transport/Rendezvous/RendezvousWire.cs`
- Test: `Linkpearl.Core.Tests/Rendezvous/RendezvousWireTests.cs`

**Interfaces:**
- Consumes: rien.
- Produces:
  `RendezvousKind.DirectoryQuery = 0x12`, `DirectoryList = 0x13`, `DirectorySubmit = 0x14` ;
  `sealed record DirectoryEntry(string Address, string Label)` ;
  `static byte[] RendezvousWire.Directory(IReadOnlyList<DirectoryEntry>)` ;
  `static bool RendezvousWire.TryReadDirectory(ReadOnlySpan<byte>, out List<DirectoryEntry>, out string?)` ;
  `static byte[] RendezvousWire.DirectorySubmit(string address, string label)` ;
  `const int MaxDirectoryEntries = 64`, `MaxDirectoryLabelLength = 64`.

- [ ] **Step 1: Écrire le test qui échoue**

Ajouter à `Linkpearl.Core.Tests/Rendezvous/RendezvousWireTests.cs` :

```csharp
    [Fact]
    public void Un_annuaire_fait_l_aller_retour()
    {
        var entries = new List<DirectoryEntry>
        {
            new("rdv.ami.ch", "Chez l'amie"),
            new("rdv.exemple.ch:443", "Service commun"),
        };

        Assert.True(RendezvousWire.TryReadDirectory(RendezvousWire.Directory(entries), out var back, out var why), why);
        Assert.Equal(2, back.Count);
        Assert.Equal("rdv.ami.ch", back[0].Address);
        Assert.Equal("Service commun", back[1].Label);
    }

    [Fact]
    public void Un_annuaire_vide_est_lisible()
    {
        // Un service sans pair déclaré doit pouvoir répondre, sinon le client
        // ne distingue pas « personne » de « service en panne ».
        Assert.True(RendezvousWire.TryReadDirectory(RendezvousWire.Directory([]), out var back, out _));
        Assert.Empty(back);
    }

    [Fact]
    public void Un_annuaire_trop_long_est_refuse()
    {
        var entries = Enumerable.Range(0, RendezvousWire.MaxDirectoryEntries + 1)
            .Select(i => new DirectoryEntry($"rdv{i}.exemple.ch", "x"))
            .ToList();

        Assert.Throws<ArgumentException>(() => RendezvousWire.Directory(entries));
    }

    [Fact]
    public void Un_libelle_demesure_est_refuse_a_la_lecture()
    {
        // Le libellé vient du réseau et finit à l'écran : la borne est ici.
        var frame = new byte[] { RendezvousKind.DirectoryList, 1, 3 }
            .Concat("abc"u8.ToArray())
            .Concat(new byte[] { 200 })
            .Concat(Enumerable.Repeat((byte)0x41, 200))
            .ToArray();

        Assert.False(RendezvousWire.TryReadDirectory(frame, out _, out var why));
        Assert.Contains("libellé", why);
    }

    [Fact]
    public void Un_annuaire_tronque_est_refuse()
    {
        var frame = new byte[] { RendezvousKind.DirectoryList, 2, 3 }.Concat("abc"u8.ToArray()).ToArray();

        Assert.False(RendezvousWire.TryReadDirectory(frame, out _, out var why));
        Assert.Contains("tronqué", why);
    }

    [Fact]
    public void Une_candidature_fait_l_aller_retour()
    {
        var frame = RendezvousWire.DirectorySubmit("rdv.nouveau.ch:47900", "Chez le nouveau");

        Assert.True(RendezvousWire.TryReadDirectory(frame, out var back, out var why), why);
        var entry = Assert.Single(back);
        Assert.Equal("rdv.nouveau.ch:47900", entry.Address);
        Assert.Equal("Chez le nouveau", entry.Label);
    }
```

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter 'FullyQualifiedName~RendezvousWireTests'`
Expected: échec de compilation, `DirectoryEntry` et `TryReadDirectory` n'existent pas.

- [ ] **Step 3: Écrire l'implémentation**

Dans `RendezvousKind`, ajouter :

```csharp
    /// <summary>Un client demande à un service la liste de ceux qu'il connaît.</summary>
    public const byte DirectoryQuery = 0x12;

    /// <summary>La réponse, une liste d'adresses et de libellés.</summary>
    public const byte DirectoryList = 0x13;

    /// <summary>
    /// Un service se présente à un annuaire.
    /// </summary>
    /// <remarks>
    /// La seule trame qu'un service envoie à un autre, dans un seul sens, sans
    /// rien attendre en retour. Elle ne lui accorde aucune confiance : elle
    /// dépose une candidature dans une file que l'opérateur lira.
    /// </remarks>
    public const byte DirectorySubmit = 0x14;
```

Dans `RendezvousWire`, ajouter :

```csharp
    public const int MaxDirectoryEntries = 64;
    public const int MaxDirectoryLabelLength = 64;
    public const int MaxDirectoryAddressLength = 255;

    /// <summary>
    /// La liste des services qu'un service connaît.
    /// </summary>
    /// <remarks>
    /// Elle vient de sa configuration, écrite par son opérateur, jamais d'un
    /// échange entre services : il publie ce qu'il connaît, il n'interroge
    /// personne.
    /// </remarks>
    public static byte[] Directory(IReadOnlyList<DirectoryEntry> entries)
        => WriteEntries(RendezvousKind.DirectoryList, entries);

    public static byte[] DirectorySubmit(string address, string label)
        => WriteEntries(RendezvousKind.DirectorySubmit, [new DirectoryEntry(address, label)]);

    private static byte[] WriteEntries(byte kind, IReadOnlyList<DirectoryEntry> entries)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(entries.Count, MaxDirectoryEntries);

        var bytes = new List<byte> { kind, (byte)entries.Count };

        foreach (var entry in entries)
        {
            var address = System.Text.Encoding.UTF8.GetBytes(entry.Address);
            var label = System.Text.Encoding.UTF8.GetBytes(entry.Label);

            if (address.Length is 0 or > MaxDirectoryAddressLength)
                throw new ArgumentException($"adresse hors bornes : {address.Length} octets", nameof(entries));

            if (label.Length > MaxDirectoryLabelLength)
                throw new ArgumentException($"libellé hors bornes : {label.Length} octets", nameof(entries));

            bytes.Add((byte)address.Length);
            bytes.AddRange(address);
            bytes.Add((byte)label.Length);
            bytes.AddRange(label);
        }

        return [.. bytes];
    }

    /// <summary>Lit une liste d'entrées, qu'elle vienne d'un annuaire ou d'une candidature.</summary>
    public static bool TryReadDirectory(
        ReadOnlySpan<byte> frame, out List<DirectoryEntry> entries, out string? rejection)
    {
        entries = [];

        if (frame.Length < 2)
        {
            rejection = "trame trop courte";
            return false;
        }

        var count = frame[1];

        if (count > MaxDirectoryEntries)
        {
            rejection = $"nombre d'entrées hors bornes ({count}, plafond {MaxDirectoryEntries})";
            return false;
        }

        var offset = 2;

        for (var i = 0; i < count; i++)
        {
            if (TryReadString(frame, ref offset, MaxDirectoryAddressLength, "adresse", out var address, out rejection) is false)
                return false;

            if (TryReadString(frame, ref offset, MaxDirectoryLabelLength, "libellé", out var label, out rejection) is false)
                return false;

            entries.Add(new DirectoryEntry(address, label));
        }

        rejection = null;
        return true;
    }

    private static bool TryReadString(
        ReadOnlySpan<byte> frame, ref int offset, int max, string what, out string value, out string? rejection)
    {
        value = string.Empty;

        if (offset >= frame.Length)
        {
            rejection = "trame tronquée";
            return false;
        }

        var length = frame[offset++];

        if (length > max)
        {
            rejection = $"{what} hors bornes ({length} octets, plafond {max})";
            return false;
        }

        if (offset + length > frame.Length)
        {
            rejection = "trame tronquée";
            return false;
        }

        value = System.Text.Encoding.UTF8.GetString(frame.Slice(offset, length));
        offset += length;
        rejection = null;
        return true;
    }
```

Et le type, à côté de `Announcement` :

```csharp
/// <summary>Un service de rendez-vous tel qu'un annuaire le présente.</summary>
/// <remarks>
/// Le libellé vient du réseau : il est borné ici, et normalisé par l'interface
/// avant affichage, comme les noms de pairs.
/// </remarks>
public sealed record DirectoryEntry(string Address, string Label);
```

- [ ] **Step 4: Lancer les tests et vérifier qu'ils passent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter 'FullyQualifiedName~RendezvousWireTests'`
Expected: PASS.

- [ ] **Step 5: Régénérer les vecteurs figés dans les deux dépôts**

Ajouter les trois trames nouvelles à `RendezvousVectorTests.cs`, dans le tableau
`Frames`, et à `rendezvous-vectors.json` :

```csharp
        ("annuaire", () => RendezvousWire.Directory(
            [new DirectoryEntry("rdv.ami.ch", "Chez l'amie"),
             new DirectoryEntry("rdv.exemple.ch:443", "Service commun")])),
        ("annuaire-demande", () => RendezvousWire.Simple(RendezvousKind.DirectoryQuery)),
        ("annuaire-candidature", () => RendezvousWire.DirectorySubmit("rdv.nouveau.ch", "Chez le nouveau")),
```

Ajouter aussi les constantes à la section `constantes` du fichier de vecteurs :
`entreesAnnuaireMax`, `libelleAnnuaireMax`, `adresseAnnuaireMax`, et les
`[InlineData]` correspondants dans `Les_plafonds_sont_les_memes_des_deux_cotes`.

Calculer les valeurs hexadécimales en lançant le test une fois, en relevant
l'écart signalé, puis en inscrivant la valeur obtenue. Vérifier ensuite que
chaque trame se relit avec `TryReadDirectory`.

Copier les fichiers dans le dépôt du serveur et vérifier que `diff` est vide :

```bash
SRV=~/Projects/linkpearl-rendezvous
cp Linkpearl/Core/Transport/Rendezvous/RendezvousWire.cs "$SRV/Protocol/Core/Transport/Rendezvous/"
cp Linkpearl.Core.Tests/Fixtures/rendezvous-vectors.json "$SRV/Protocol/"
cp Linkpearl.Core.Tests/Rendezvous/RendezvousVectorTests.cs "$SRV/Linkpearl.Rendezvous.Tests/"
diff Linkpearl/Core/Transport/Rendezvous/RendezvousWire.cs "$SRV/Protocol/Core/Transport/Rendezvous/RendezvousWire.cs"
```

- [ ] **Step 6: Lancer les deux suites**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: PASS.

Run (dans `~/Projects/linkpearl-rendezvous`) : `dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit dans les deux dépôts**

```bash
git add -A && git commit -m "feat(core): trames d'annuaire et de candidature"
cd ~/Projects/linkpearl-rendezvous && git add -A \
  && git commit -m "chore(protocol): recopier les trames d'annuaire et leurs vecteurs"
```

---

### Task 5 : le service publie son annuaire

**Files:**
- Create: `linkpearl-rendezvous/Linkpearl.Rendezvous/Directory.cs`
- Modify: `linkpearl-rendezvous/Linkpearl.Rendezvous/RendezvousServer.cs`
- Modify: `linkpearl-rendezvous/Linkpearl.Rendezvous/Program.cs`
- Test: `linkpearl-rendezvous/Linkpearl.Rendezvous.Tests/DirectoryTests.cs`

**Interfaces:**
- Consumes: `DirectoryEntry`, `RendezvousWire.Directory`, `RendezvousWire.TryReadDirectory` (tâche 4).
- Produces: `sealed class Directory(string peersPath, string pendingPath)` avec
  `IReadOnlyList<DirectoryEntry> Known()`, `bool Submit(string sourceAddress, DirectoryEntry entry)`,
  `IReadOnlyList<DirectoryEntry> Pending()`.

- [ ] **Step 1: Écrire le test qui échoue**

Créer `Linkpearl.Rendezvous.Tests/DirectoryTests.cs` :

```csharp
using Linkpearl.Core.Transport.Rendezvous;
using Linkpearl.Rendezvous;
using Xunit;

namespace Linkpearl.Rendezvous.Tests;

/// <summary>
/// L'annuaire : un fichier écrit par l'opérateur, relu à chaud, et une file de
/// candidatures qu'il approuve à la main.
/// </summary>
public sealed class DirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lprdv-" + Guid.NewGuid().ToString("N"));

    private Directory New()
    {
        System.IO.Directory.CreateDirectory(_root);
        return new Directory(Path.Combine(_root, "peers.txt"), Path.Combine(_root, "pending.txt"));
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_root))
            System.IO.Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Un_fichier_absent_donne_un_annuaire_vide()
    {
        // Un service sans pair déclaré doit répondre, pas échouer.
        Assert.Empty(New().Known());
    }

    [Fact]
    public void Le_fichier_est_relu_sans_redemarrage()
    {
        // Sans cela, ajouter un service imposerait un redémarrage, et personne
        // ne le ferait.
        var directory = New();
        var path = Path.Combine(_root, "peers.txt");

        File.WriteAllText(path, "rdv.ami.ch  Chez l'amie\n");
        Assert.Single(directory.Known());

        File.WriteAllText(path, "rdv.ami.ch  Chez l'amie\nrdv.autre.ch:443  Ailleurs\n");
        Assert.Equal(2, directory.Known().Count);
    }

    [Fact]
    public void Une_ligne_illisible_est_ignoree_sans_perdre_les_autres()
    {
        var directory = New();

        File.WriteAllText(Path.Combine(_root, "peers.txt"),
            "# un commentaire\n\nrdv exemple.ch  mauvais\nrdv.bon.ch  Bon\n");

        var entry = Assert.Single(directory.Known());
        Assert.Equal("rdv.bon.ch", entry.Address);
    }

    [Fact]
    public void Une_candidature_entre_dans_la_file_et_pas_dans_l_annuaire()
    {
        var directory = New();

        Assert.True(directory.Submit("198.51.100.7", new DirectoryEntry("rdv.nouveau.ch", "Nouveau")));

        Assert.Empty(directory.Known());
        Assert.Single(directory.Pending());
    }

    [Fact]
    public void Une_seconde_candidature_de_la_meme_source_est_refusee()
    {
        // Une par adresse et par heure : sans cela, la file se remplit toute
        // seule et l'opérateur ne la lit plus.
        var directory = New();

        Assert.True(directory.Submit("198.51.100.7", new DirectoryEntry("rdv.a.ch", "A")));
        Assert.False(directory.Submit("198.51.100.7", new DirectoryEntry("rdv.b.ch", "B")));
    }

    [Fact]
    public void Une_candidature_deja_connue_est_ignoree()
    {
        var directory = New();
        File.WriteAllText(Path.Combine(_root, "peers.txt"), "rdv.connu.ch  Connu\n");

        Assert.False(directory.Submit("198.51.100.7", new DirectoryEntry("rdv.connu.ch", "Connu")));
        Assert.Empty(directory.Pending());
    }

    [Fact]
    public void La_file_survit_a_un_redemarrage()
    {
        // Une seule candidature doit suffire : la reperdre obligerait le
        // candidat à recommencer sans savoir pourquoi.
        New().Submit("198.51.100.7", new DirectoryEntry("rdv.nouveau.ch", "Nouveau"));

        Assert.Single(New().Pending());
    }
}
```

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run (dans `~/Projects/linkpearl-rendezvous`) :
`dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj --filter 'FullyQualifiedName~DirectoryTests'`
Expected: échec de compilation, `Directory` n'existe pas.

- [ ] **Step 3: Écrire l'implémentation**

Créer `Linkpearl.Rendezvous/Directory.cs` :

```csharp
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Rendezvous;

/// <summary>
/// Les services que celui-ci connaît, et les candidatures en attente.
/// </summary>
/// <remarks>
/// L'annuaire publie, il n'interroge personne et ne propage rien. Sa liste
/// vient d'un fichier écrit par l'opérateur, une ligne par service :
///
///     rdv.ami.ch        Chez l'amie
///     rdv.autre.ch:443  Ailleurs
///
/// Le fichier est relu à chaud, sur l'horodatage : ajouter un service doit être
/// une ligne écrite, pas un redémarrage, sans quoi personne ne le fera.
///
/// Une candidature n'entre jamais dans cette liste. Elle tombe dans une file
/// que l'opérateur lit et approuve en déplaçant la ligne. C'est ce qui empêche
/// l'annuaire de devenir une autorité par accumulation.
/// </remarks>
public sealed class Directory(string peersPath, string pendingPath)
{
    /// <summary>Une candidature par adresse source et par heure.</summary>
    private static readonly TimeSpan SubmitInterval = TimeSpan.FromHours(1);

    private const int MaxPending = 256;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, DateTime> _lastSubmit = [];

    private List<DirectoryEntry> _known = [];
    private DateTime _knownStamp = DateTime.MinValue;

    public IReadOnlyList<DirectoryEntry> Known()
    {
        lock (_gate)
        {
            var stamp = File.Exists(peersPath) ? File.GetLastWriteTimeUtc(peersPath) : DateTime.MinValue;

            if (stamp != _knownStamp)
            {
                _known = Read(peersPath);
                _knownStamp = stamp;
            }

            return _known;
        }
    }

    public IReadOnlyList<DirectoryEntry> Pending() => Read(pendingPath);

    /// <summary>Enregistre une candidature. Rend faux quand elle est écartée.</summary>
    public bool Submit(string sourceAddress, DirectoryEntry entry)
    {
        if (RendezvousAddress.TryParse(entry.Address, out _, out _) is false)
            return false;

        lock (_gate)
        {
            if (_lastSubmit.TryGetValue(sourceAddress, out var last)
                && DateTime.UtcNow - last < SubmitInterval)
                return false;

            var known = Known();
            var pending = Read(pendingPath);

            if (known.Any(e => e.Address == entry.Address) || pending.Any(e => e.Address == entry.Address))
                return false;

            if (pending.Count >= MaxPending)
                pending.RemoveAt(0);   // les plus anciennes cèdent la place

            pending.Add(entry);
            Write(pendingPath, pending);

            _lastSubmit[sourceAddress] = DateTime.UtcNow;
            return true;
        }
    }

    private static List<DirectoryEntry> Read(string path)
    {
        var entries = new List<DirectoryEntry>();

        if (File.Exists(path) is false)
            return entries;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();

            if (trimmed.Length is 0 || trimmed[0] is '#')
                continue;

            var cut = trimmed.IndexOfAny([' ', '\t']);
            var address = cut < 0 ? trimmed : trimmed[..cut];
            var label = cut < 0 ? "" : trimmed[(cut + 1)..].Trim();

            // Une ligne illisible est ignorée, les autres restent : un fichier
            // à demi valide vaut mieux qu'un service muet.
            if (RendezvousAddress.TryParse(address, out _, out _) is false)
                continue;

            if (label.Length > RendezvousWire.MaxDirectoryLabelLength)
                label = label[..RendezvousWire.MaxDirectoryLabelLength];

            entries.Add(new DirectoryEntry(address, label));

            if (entries.Count >= RendezvousWire.MaxDirectoryEntries)
                break;
        }

        return entries;
    }

    private static void Write(string path, IReadOnlyList<DirectoryEntry> entries)
        => File.WriteAllLines(path, entries.Select(e => $"{e.Address}  {e.Label}"));
}
```

- [ ] **Step 3 bis: Tenir les compteurs que la console lira un jour**

La spec demande que le serveur tienne dès maintenant, comme état interne, ce
qu'une console d'administration voudra afficher. Les exposer coûtera alors une
trame, pas une réécriture. Ajouter à `RendezvousServer` :

```csharp
    /// <summary>
    /// Ce que le service a fait depuis son démarrage.
    /// </summary>
    /// <remarks>
    /// Tenu même sans personne pour le lire : une console d'administration
    /// viendra plus tard, et reconstituer ces compteurs après coup demanderait
    /// de retoucher chaque chemin de code.
    /// </remarks>
    public sealed record Counters(
        int OpenMailboxes, int PendingAnnouncements, long Matches, long RelayedBytes,
        int KnownPeers, int PendingSubmissions);

    public Counters Snapshot() => new(
        _mailboxes.Count, _announcements.Count, _matches, _relayedBytes,
        _directory.Known().Count, _directory.Pending().Count);
```

`_matches` et `_relayedBytes` sont deux `long` incrémentés là où l'appariement
réussit et là où le relais transmet. Aucune trame ne les expose pour l'instant.

Ajouter un test qui vérifie qu'un appariement incrémente bien le compteur, sans
quoi il dérivera sans que personne ne s'en aperçoive.

- [ ] **Step 4: Brancher les trames dans le serveur**

Dans `RendezvousServer.cs`, là où les autres types de trame sont traités,
ajouter :

```csharp
            case RendezvousKind.DirectoryQuery:
                await SendAsync(session, RendezvousWire.Directory(_directory.Known()), ct).ConfigureAwait(false);
                break;

            case RendezvousKind.DirectorySubmit:
                // On ne répond rien : le candidat n'a pas à savoir s'il a été
                // retenu, et une réponse en ferait un moyen de sonder la file.
                if (RendezvousWire.TryReadDirectory(frame, out var submitted, out _) && submitted.Count is 1)
                    _directory.Submit(session.RemoteAddress, submitted[0]);
                break;
```

Le serveur reçoit `Directory` par son constructeur. L'adresse source vient de
`client.Client.RemoteEndPoint`, que `PeerSession.cs:51` lit déjà, normalisée par
`RendezvousServer.Normalize` puis réduite à sa seule adresse : c'est elle, sans
le port, qui porte la limite d'une candidature par heure. Vérifier le nom exact
de la propriété exposée par `PeerSession` avant d'écrire la ligne.

Dans `Program.cs`, ajouter les drapeaux :

```csharp
var peers   = ArgString("--peers", "peers.txt");
var pending = ArgString("--pending", "pending.txt");
```

et les passer au serveur. Compléter le texte de `--help` :

```
--peers    fichier des services connus, une ligne « adresse  libellé »,
           relu à chaud.
--pending  fichier des candidatures reçues, à relire et à recopier dans
           --peers pour les approuver.
```

- [ ] **Step 5: Lancer les tests et vérifier qu'ils passent**

Run: `dotnet test Linkpearl.Rendezvous.Tests/Linkpearl.Rendezvous.Tests.csproj`
Expected: PASS.

Run: `dotnet build Linkpearl.Rendezvous/Linkpearl.Rendezvous.csproj -c Release`
Expected: 0 Warning(s).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(rendezvous): publier les services connus, recevoir les candidatures"
```

---

### Task 6 : le service se porte candidat

**Files:**
- Modify: `linkpearl-rendezvous/Linkpearl.Rendezvous/Program.cs`
- Create: `linkpearl-rendezvous/Linkpearl.Rendezvous/Announcer.cs`

**Interfaces:**
- Consumes: `RendezvousWire.DirectorySubmit` (tâche 4).
- Produces: `static Task Announcer.SubmitAsync(RendezvousAddress to, string self, string label, CancellationToken)`.

- [ ] **Step 1: Écrire l'implémentation**

Pas de test automatisé : la méthode n'est qu'une connexion TCP et un envoi, et
la vérifier demanderait un service en face, ce que la tâche 9 fait dans le
harnais. Créer `Linkpearl.Rendezvous/Announcer.cs` :

```csharp
using System.Net.Sockets;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Rendezvous;

/// <summary>
/// La candidature d'un service auprès d'un annuaire.
/// </summary>
/// <remarks>
/// La seule connexion sortante qu'un service ouvre de lui-même, une fois au
/// démarrage. Il ne consulte jamais un autre annuaire, ne vérifie jamais un
/// autre service, et ne propage jamais ce qu'il a reçu.
///
/// L'échec est silencieux et sans reprise : l'annuaire est peut-être éteint, ou
/// l'opérateur peut ne pas vouloir de nous. Insister ne changerait rien et
/// remplirait son journal.
/// </remarks>
public static class Announcer
{
    public static async Task SubmitAsync(
        RendezvousAddress to, string self, string label, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(to.Host, to.Port, ct).ConfigureAwait(false);

            var frame = RendezvousWire.Frame(RendezvousWire.DirectorySubmit(self, label));
            await client.GetStream().WriteAsync(frame, ct).ConfigureAwait(false);

            Console.WriteLine($"Candidature déposée auprès de {to}.");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Candidature auprès de {to} impossible : {e.Message}");
        }
    }
}
```

- [ ] **Step 2: Brancher le drapeau**

Dans `Program.cs`, après le démarrage du serveur :

```csharp
foreach (var target in ArgAll("--announce-to"))
{
    if (RendezvousAddress.TryParse(target, out var to, out var why) is false)
    {
        Console.WriteLine($"--announce-to {target} ignoré : {why}");
        continue;
    }

    _ = Announcer.SubmitAsync(to, ArgString("--public-address", $"localhost:{port}"),
                              ArgString("--label", "service sans nom"), stopping.Token);
}
```

`ArgAll` lit un drapeau répétable. Compléter `--help` :

```
--announce-to      annuaire auprès duquel se porter candidat, répétable.
--public-address   l'adresse sous laquelle les autres vous joignent, à
                   donner avec --announce-to.
--label            le nom qui s'affichera dans les annuaires.
```

- [ ] **Step 3: Vérifier à la main, deux services en local**

```bash
dotnet run --project Linkpearl.Rendezvous -- --port 47901 --peers /tmp/a-peers.txt --pending /tmp/a-pending.txt &
dotnet run --project Linkpearl.Rendezvous -- --port 47902 \
  --announce-to 127.0.0.1:47901 --public-address 127.0.0.1:47902 --label "Le nouveau"
cat /tmp/a-pending.txt
```

Expected: `/tmp/a-pending.txt` contient `127.0.0.1:47902  Le nouveau`.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(rendezvous): se porter candidat auprès d'un annuaire"
```

---

### Task 7 : la configuration porte une liste de services

**Files:**
- Create: `Linkpearl/Core/Sync/RendezvousList.cs`
- Modify: `Linkpearl/Configuration.cs`
- Test: `Linkpearl.Core.Tests/Sync/RendezvousListTests.cs`

**Interfaces:**
- Consumes: `RendezvousAddress` (tâche 1).
- Produces: `sealed record RendezvousEntry(RendezvousAddress Address, string Label, bool Enabled)` ;
  `static IReadOnlyList<RendezvousEntry> RendezvousList.Migrate(string? legacyHost, int legacyPort, IReadOnlyList<RendezvousEntry>? current)`.

- [ ] **Step 1: Écrire le test qui échoue**

Créer `Linkpearl.Core.Tests/Sync/RendezvousListTests.cs` :

```csharp
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

/// <summary>
/// La migration d'un réglage unique vers une liste. Elle vit dans le noyau et
/// non dans Configuration, qui dépend de Dalamud et ne se teste pas sous Linux.
/// </summary>
public class RendezvousListTests
{
    [Fact]
    public void Un_reglage_d_avant_la_federation_devient_une_liste_d_une_entree()
    {
        var list = RendezvousList.Migrate("83.228.242.221", 47900, null);

        var entry = Assert.Single(list);
        Assert.Equal("83.228.242.221", entry.Address.Host);
        Assert.Equal(47900, entry.Address.Port);
        Assert.True(entry.Enabled);
    }

    [Fact]
    public void Une_liste_deja_migree_n_est_pas_retouchee()
    {
        var current = new[] { new RendezvousEntry(new RendezvousAddress("rdv.ami.ch", 443), "Amie", true) };

        Assert.Same(current, RendezvousList.Migrate("83.228.242.221", 47900, current));
    }

    [Fact]
    public void Un_reglage_vide_ne_fabrique_pas_d_entree()
    {
        // Mieux vaut une liste vide, que l'interface signale, qu'une entrée
        // pointant nulle part dont l'utilisateur ne comprendrait pas l'échec.
        Assert.Empty(RendezvousList.Migrate("", 47900, null));
        Assert.Empty(RendezvousList.Migrate(null, 47900, []));
    }

    [Fact]
    public void Un_reglage_illisible_ne_fabrique_pas_d_entree()
    {
        Assert.Empty(RendezvousList.Migrate("rdv exemple.ch", 47900, null));
    }
}
```

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter 'FullyQualifiedName~RendezvousListTests'`
Expected: échec de compilation.

- [ ] **Step 3: Écrire l'implémentation**

Créer `Linkpearl/Core/Sync/RendezvousList.cs` :

```csharp
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Sync;

/// <summary>Un service de rendez-vous de la liste de l'utilisateur.</summary>
/// <remarks>
/// L'interrupteur permet d'en écarter un sans perdre son adresse : un service
/// en panne se désactive le temps qu'il revienne, plutôt que de se retaper.
/// </remarks>
public sealed record RendezvousEntry(RendezvousAddress Address, string Label, bool Enabled);

/// <summary>
/// La liste des services de l'utilisateur, et sa migration.
/// </summary>
/// <remarks>
/// La migration vit ici et non dans <c>Configuration</c>, qui dépend de Dalamud
/// et ne se testerait pas sous Linux. Le plugin n'appelle qu'une fonction.
/// </remarks>
public static class RendezvousList
{
    /// <summary>
    /// Construit la liste depuis l'ancien réglage, si elle n'existe pas encore.
    /// </summary>
    /// <remarks>
    /// Rend la liste courante telle quelle dès qu'elle porte quelque chose :
    /// la migration ne doit jamais écraser un choix que l'utilisateur a fait
    /// depuis.
    /// </remarks>
    public static IReadOnlyList<RendezvousEntry> Migrate(
        string? legacyHost, int legacyPort, IReadOnlyList<RendezvousEntry>? current)
    {
        if (current is { Count: > 0 })
            return current;

        if (string.IsNullOrWhiteSpace(legacyHost))
            return [];

        var text = legacyPort == RendezvousAddress.DefaultPort ? legacyHost : $"{legacyHost}:{legacyPort}";

        return RendezvousAddress.TryParse(text, out var address, out _)
            ? [new RendezvousEntry(address, "", true)]
            : [];
    }
}
```

- [ ] **Step 4: Adapter `Configuration`**

Dans `Linkpearl/Configuration.cs` :

```csharp
    public int Version { get; set; } = 2;

    /// <summary>Les services de rendez-vous, par ordre de préférence.</summary>
    /// <remarks>
    /// Une liste et non un service unique : c'est ce qui permet de voir des
    /// joueurs qui n'ont pas fait le même choix que vous. Chaque entrée que vous
    /// activez apprend que votre personnage est en ligne, donc la liste reste
    /// courte par défaut.
    /// </remarks>
    public List<RendezvousEntry> Rendezvous { get; set; } = [];

    /// <summary>Réglage d'avant la fédération, lu une dernière fois à la migration.</summary>
    public string RendezvousHost { get; set; } = "83.228.242.221";

    public int RendezvousPort { get; set; } = 47900;

    /// <summary>Les services activés, ceux que le moteur emploiera.</summary>
    public IReadOnlyList<RendezvousEntry> ActiveRendezvous =>
        Rendezvous.Where(entry => entry.Enabled).ToList();

    public void MigrateIfNeeded()
    {
        var migrated = RendezvousList.Migrate(RendezvousHost, RendezvousPort, Rendezvous);

        if (ReferenceEquals(migrated, Rendezvous))
            return;

        Rendezvous = [.. migrated];
        Version = 2;
        Save();
    }
```

Dans `Plugin.cs`, juste après la lecture de la configuration :

```csharp
        _configuration.MigrateIfNeeded();
```

- [ ] **Step 5: Lancer les tests et compiler**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: PASS.

Run: `dotnet build Linkpearl/Linkpearl.csproj -c Release`
Expected: 0 Warning(s).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(plugin): la configuration porte une liste de services"
```

---

### Task 8 : l'annonce parallèle sur tous les lieux partagés

**Files:**
- Modify: `Linkpearl/Core/Sync/PeerConnector.cs`
- Test: `Linkpearl.Core.Tests/Sync/PeerConnectorTests.cs` (créé)

**Interfaces:**
- Consumes: `PairRecord.Rendezvous` (tâche 2).
- Produces: `PeerConnector.ConnectAsync` inchangé en signature, l'annonce se
  faisant désormais sur tous les lieux ; `interface IRendezvousDialer` avec
  `Task<byte[]?> AnnounceAsync(RendezvousAddress at, Announcement announcement, CancellationToken ct)`,
  injectée au constructeur pour que le choix du lieu se teste sans réseau.

- [ ] **Step 1: Écrire le test qui échoue**

Créer `Linkpearl.Core.Tests/Sync/PeerConnectorTests.cs` :

```csharp
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

/// <summary>Un annonceur qui répond selon le lieu, sans réseau.</summary>
internal sealed class ScriptedDialer : IRendezvousDialer
{
    private readonly Dictionary<string, byte[]?> _answers;

    public List<string> Asked { get; } = [];

    public ScriptedDialer(Dictionary<string, byte[]?> answers) => _answers = answers;

    public async Task<byte[]?> AnnounceAsync(
        RendezvousAddress at, Announcement announcement, CancellationToken ct)
    {
        lock (Asked)
            Asked.Add(at.Host);

        if (_answers.TryGetValue(at.Host, out var answer) is false)
        {
            // Service muet : il ne répond jamais, il ne refuse pas.
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return null;
        }

        return answer;
    }
}

public class PeerConnectorTests
{
    [Fact]
    public async Task Tous_les_lieux_sont_essayes_en_meme_temps()
    {
        // En séquence, deux personnes en ligne se manqueraient : l'une sur le
        // premier service pendant que l'autre est sur le second.
        var dialer = new ScriptedDialer(new() { ["lent.ch"] = null, ["rapide.ch"] = [1, 2, 3] });

        var result = await PeerConnector.AnnounceEverywhereAsync(
            dialer,
            [new RendezvousAddress("lent.ch", 47900), new RendezvousAddress("rapide.ch", 47900)],
            new Announcement([new byte[16]], [9]),
            TimeSpan.FromSeconds(5),
            default);

        Assert.NotNull(result);
        Assert.Equal("rapide.ch", result!.Value.At.Host);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Value.Theirs);
        Assert.Equal(2, dialer.Asked.Count);
    }

    [Fact]
    public async Task Le_lieu_qui_apparie_est_rendu_pour_le_relais()
    {
        // Le relais doit passer par le service que les deux ont atteint.
        var dialer = new ScriptedDialer(new() { ["seul.ch"] = [7] });

        var result = await PeerConnector.AnnounceEverywhereAsync(
            dialer, [new RendezvousAddress("seul.ch", 443)],
            new Announcement([new byte[16]], [9]), TimeSpan.FromSeconds(5), default);

        Assert.Equal(443, result!.Value.At.Port);
    }

    [Fact]
    public async Task Aucun_lieu_joignable_rend_null_sans_lever()
    {
        var dialer = new ScriptedDialer([]);

        var result = await PeerConnector.AnnounceEverywhereAsync(
            dialer, [new RendezvousAddress("muet.ch", 47900)],
            new Announcement([new byte[16]], [9]), TimeSpan.FromMilliseconds(200), default);

        Assert.Null(result);
    }

    [Fact]
    public async Task Une_liste_vide_rend_null_sans_rien_demander()
    {
        var dialer = new ScriptedDialer([]);

        Assert.Null(await PeerConnector.AnnounceEverywhereAsync(
            dialer, [], new Announcement([new byte[16]], [9]), TimeSpan.FromSeconds(1), default));

        Assert.Empty(dialer.Asked);
    }
}
```

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter 'FullyQualifiedName~PeerConnectorTests'`
Expected: échec de compilation.

- [ ] **Step 3: Écrire l'implémentation**

Dans `PeerConnector.cs`, ajouter l'abstraction et la méthode :

```csharp
/// <summary>Ce qui sait s'annoncer auprès d'un service de rendez-vous.</summary>
/// <remarks>
/// Abstrait pour que le choix du lieu se teste sans réseau. L'implémentation
/// réelle ouvre un <see cref="RendezvousClient"/> et attend l'appariement.
/// </remarks>
public interface IRendezvousDialer
{
    Task<byte[]?> AnnounceAsync(RendezvousAddress at, Announcement announcement, CancellationToken ct);
}

// dans PeerConnector :

    /// <summary>
    /// S'annonce sur tous les lieux à la fois, et rend le premier appariement.
    /// </summary>
    /// <remarks>
    /// En parallèle et non l'un après l'autre : en séquence, deux personnes
    /// pourtant en ligne se manquent dès qu'elles essaient les services dans un
    /// ordre différent, l'une attendant sur le premier pendant que l'autre
    /// attend sur le second.
    ///
    /// Le lieu rendu est celui qui a apparié : c'est par lui que passera le
    /// relais si le direct échoue, puisque c'est le seul que les deux ont
    /// atteint.
    /// </remarks>
    public static async Task<(RendezvousAddress At, byte[] Theirs)?> AnnounceEverywhereAsync(
        IRendezvousDialer dialer, IReadOnlyList<RendezvousAddress> places,
        Announcement announcement, TimeSpan budget, CancellationToken ct)
    {
        if (places.Count == 0)
            return null;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(budget);

        var attempts = places
            .Select(async place =>
            {
                var theirs = await dialer.AnnounceAsync(place, announcement, deadline.Token).ConfigureAwait(false);
                return theirs is null ? ((RendezvousAddress, byte[])?)null : (place, theirs);
            })
            .ToList();

        try
        {
            while (attempts.Count > 0)
            {
                var finished = await Task.WhenAny(attempts).ConfigureAwait(false);
                attempts.Remove(finished);

                if (finished.IsCompletedSuccessfully && await finished.ConfigureAwait(false) is { } match)
                {
                    // Le premier gagne : les autres tentatives n'ont plus lieu
                    // d'être, et leur annulation ferme leurs connexions.
                    await deadline.CancelAsync().ConfigureAwait(false);
                    return match;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        return null;
    }
```

Puis remplacer dans `ConnectAsync` le bloc qui se connecte à un seul hôte par un
appel à `AnnounceEverywhereAsync(_dialer, pair.Rendezvous, ...)`, et retenir le
lieu rendu pour un futur relais.

- [ ] **Step 3 bis: Distinguer « aucun lieu commun » de « hors ligne »**

La spec le demande explicitement, et ce n'est pas un détail de formulation :
dire « hors ligne » enverrait l'utilisateur attendre pour rien, alors que la
réparation est d'ajouter un service ou de se repairer.

Dans `ConnectAsync`, quand tous les lieux ont été essayés sans réponse et qu'au
moins un service a refusé la connexion (par opposition à un pair simplement
absent) :

```csharp
        if (match is null)
        {
            // Deux silences très différents : le pair n'était pas là, ou aucun
            // service ne nous a reçus tous les deux. Le second se répare.
            return reachedAny
                ? new ConnectionAttempt(null, true, null)
                : new ConnectionAttempt(null, false, "aucun lieu de rendez-vous commun joignable");
        }
```

`reachedAny` vaut vrai dès qu'un service a accepté notre annonce, même sans
appariement. `PeerStatus.LastFailure` le porte déjà jusqu'à l'interface, et
`PairsPage` l'affiche déjà sous le nom du pair : rien d'autre à câbler.

- [ ] **Step 4: Lancer les tests et vérifier qu'ils passent**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): s'annoncer en parallèle sur tous les lieux partagés"
```

---

### Task 9 : présence et détection sur plusieurs services

**Files:**
- Modify: `Linkpearl/Integration/PresenceService.cs`
- Modify: `Linkpearl/Plugin.cs` (cadence de la boucle)

**Interfaces:**
- Consumes: `Configuration.ActiveRendezvous` (tâche 7).
- Produces: `PresenceService.Detected` inchangé en type, alimenté par l'union
  des services ; `PresenceService.Connected` devient « au moins un service
  répond » ; `PresenceService.ConnectedCount` et `ConfiguredCount` pour la barre
  d'état.

- [ ] **Step 1: Une session par service**

Extraire de `PresenceService` une classe privée `Session`, qui porte ce que la
classe actuelle tient pour un service unique : `RendezvousClient`, le jeton
d'annulation, l'empreinte pour laquelle les boîtes sont ouvertes, et la dernière
erreur. `PresenceService` en tient un dictionnaire, clé `RendezvousAddress`.

`EnsureOpenAsync` ouvre ou referme les sessions selon `ActiveRendezvous`, et
laisse chaque session prendre son propre compte à rebours de reprise : un
service en panne ne doit pas retarder les autres.

- [ ] **Step 2: L'union à la détection**

```csharp
    /// <summary>
    /// Interroge tous les services et fait l'union de ce qu'ils reconnaissent.
    /// </summary>
    /// <remarks>
    /// Un joueur est détecté dès qu'un seul service reconnaît sa boîte. Exiger
    /// l'accord de tous rendrait la détection dépendante du plus mal en point.
    ///
    /// Les services qui ne répondent pas sortent de la ronde sans la bloquer :
    /// leur silence n'est pas une réponse négative.
    /// </remarks>
    public async Task RefreshDetectionAsync(IReadOnlyList<NearbyPlayer> nearby, CancellationToken ct)
```

Les demandes reçues de plusieurs sessions sont versées dans la même file et
dédoublonnées sur la clé du demandeur et le nonce :

```csharp
        // Un expéditeur qui dépose sur plusieurs services ne doit produire
        // qu'une invite.
        var key = (request.Id, Convert.ToHexStringLower(request.PairingNonce));

        if (_seen.Add(key))
            _incoming.Enqueue(request);
```

- [ ] **Step 3: La cadence passe à quinze secondes**

Dans `Plugin.cs`, `RefreshLoopAsync` : `TimeSpan.FromSeconds(3)` devient
`TimeSpan.FromSeconds(15)`, et `RefreshDetectionAsync` n'est appelé que lorsque
l'ensemble des empreintes visibles a changé depuis la ronde précédente.

```csharp
                        var visible = _state.Nearby.Select(p => p.Fingerprint).ToHashSet();

                        // Mesuré : 345 Mo par mois et par joueur à trois
                        // secondes, 20 à quinze et seulement quand le champ
                        // change. Facteur 17 pour un confort invisible, et la
                        // fédération multiplie encore ce coût par le nombre de
                        // services.
                        if (visible.SetEquals(_lastVisible) is false)
                        {
                            _lastVisible = visible;
                            await _presence.RefreshDetectionAsync(_state.Nearby, ct).ConfigureAwait(false);
                        }
```

- [ ] **Step 4: Compiler et vérifier à la main**

Run: `dotnet build Linkpearl/Linkpearl.csproj -c Release`
Expected: 0 Warning(s).

Run: `./scripts/deploy-plugin-dev.sh`, puis en jeu `/lpearl sync`.
Expected: la barre d'état montre le nombre de services joints sur le nombre
configuré.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(plugin): présence et détection sur plusieurs services"
```

---

### Task 10 : l'interface des services

**Files:**
- Modify: `Linkpearl/Ui/Pages/SettingsPage.cs`
- Modify: `Linkpearl/Ui/Icons.cs` (une icône de découverte)

**Interfaces:**
- Consumes: `Configuration.Rendezvous` (tâche 7), `RendezvousWire.Directory` (tâche 4).
- Produces: rien que d'autres tâches consomment.

- [ ] **Step 1: La liste**

Remplacer le champ unique par une carte par service : libellé, adresse,
interrupteur, bouton de retrait. Le libellé et l'adresse passent par
`Glyphs.Safe` avant affichage, la découverte les faisant venir du réseau.

- [ ] **Step 2: L'ajout à la main**

Un champ et un bouton. L'adresse passe par `RendezvousAddress.TryParse`, et la
raison du refus s'affiche telle quelle : elle est écrite pour être lue.

- [ ] **Step 3: La découverte**

Un bouton par service, qui envoie `DirectoryQuery` et présente ce qui revient
avec une case à cocher par entrée. **Rien n'est ajouté sans la case cochée** :
c'est ce qui empêche l'annuaire de devenir une autorité.

Une phrase sous la liste, pour que le prix soit lu avant d'être payé :

```
Chaque service activé apprend que votre personnage est en ligne et qui
se tient autour de vous. En ajouter augmente vos chances de voir du
monde, et le nombre de personnes qui le savent.
```

- [ ] **Step 4: Compiler et regarder**

Run: `dotnet build Linkpearl/Linkpearl.csproj -c Release && ./scripts/deploy-plugin-dev.sh`
Expected: 0 Warning(s), et la page des réglages montre la liste.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(ui): gérer la liste des services de rendez-vous"
```

---

### Task 11 : la preuve, deux services et deux clients

**Files:**
- Create: `Linkpearl.Harness/FederationRun.cs`
- Modify: `Linkpearl.Harness/Program.cs` (mode `federation`)

**Interfaces:**
- Consumes: tout ce qui précède.
- Produces: `static Task<bool> FederationRun.ExecuteAsync(FederationSettings, CancellationToken)`.

- [ ] **Step 1: Écrire le run**

Deux instances de `RendezvousServer` sur deux ports, et deux moteurs dont les
listes **ne se recouvrent que partiellement** : Alice sur les services A et B,
Bob sur B seulement. Ils doivent se trouver sur B, transférer, et poser.

Puis un second passage où Alice n'a que A et Bob que B : ils ne doivent **pas**
se trouver, et le rapport doit le dire sans ambiguïté. Un test qui ne sait
échouer ne prouve rien.

S'inspirer de `FakePeerRun.cs`, qui monte déjà deux moteurs complets, en
remplaçant `LoopbackDialer` par le vrai `PeerConnector` pointant sur les services
en local.

- [ ] **Step 2: Lancer**

```bash
dotnet run --project Linkpearl.Harness -- federation --from-cache
```

Expected: « recouvrement partiel : apparence posée », puis « listes disjointes :
aucun lieu commun, comme attendu ».

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(harness): deux services, deux clients, listes partiellement communes"
```

---

## Ce que ce plan ne fait pas

- **La modération.** Liste de bannissement, signalements, dérivation lente : une
  autre spec, un autre plan.
- **La console d'administration.** Les compteurs que la tâche 5 tient déjà
  (services connus, candidatures en attente) lui suffiront le moment venu.
- **La signature des annuaires.** Le client fait déjà confiance au service qu'il
  interroge, et l'utilisateur confirme chaque ajout. À reprendre si un annuaire
  public voit le jour.
