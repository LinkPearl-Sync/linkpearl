# Intégrations : Customize+, SimpleHeels, Honorific, Moodles, PetNicknames

Conçu le 23 septembre 2026. Sous-projet A de la feuille de route fixée ce
jour-là (voir `docs/reprise.md`). Le sous-projet B, les animations, VFX et
sons avec leur blocage global et par pair, fera l'objet d'une autre spec.

## But

Que chaque pair voie ce qu'il verrait avec les clients comparables (Snowcloak,
UmbraSync) : proportions Customize+, décalage SimpleHeels, titre Honorific,
statuts Moodles, surnoms PetNicknames. Sans rien faire fuir que le projet
s'interdit ailleurs, et sans qu'un plugin absent chez l'un ou l'autre casse
quoi que ce soit.

## Décisions prises avec l'utilisateur

- Les cinq intégrations, toutes dans ce sous-projet.
- **Aucun interrupteur pour elles** : elles suivent toujours, comme
  l'apparence. Le blocage global et par pair est réservé aux ressources
  transitoires (sous-projet B).
- **Un bloc typé dans le manifeste**, qui passe en version 2, plutôt qu'un
  dictionnaire ouvert ou des messages séparés.
- **Moodles est décodé et nettoyé** avant l'envoi, plutôt que transmis tel quel
  ou reporté.

## Sources

Libellés, signatures et formats relevés dans le code des plugins et de
Snowcloak et UmbraSync, aux commits suivants : CustomizePlus 44d2541,
SimpleHeels 162466c, Honorific 9d66b63, Moodles f4b7578, FFXIVPetRenamer
7192264, SnowcloakClient fe98096, UmbraClient 541100a. Rien n'a été exécuté :
les exemples de format sont déduits du code.

## Modèle

`CharacterManifest` gagne un champ `Extras` de type `CharacterExtras` :

```csharp
public sealed record CharacterExtras(
    string? CustomizePlus,   // JSON des os
    string? Heels,           // JSON SimpleHeels nettoyé
    string? Honorific,       // JSON du titre
    string? Moodles,         // base64 MemoryPack nettoyé
    string? PetNicknames);   // base64 UTF-16, lignes d'identité neutralisées
```

`null` veut dire « pas de ce plugin, ou rien à montrer ». `CurrentVersion`
passe à 2. Le codec écrit une clé JSON courte par champ présent (`xc`, `xh`,
`xt`, `xm`, `xp`) et relit un manifeste v1, dont les extras sont alors vides.
Le hachage du manifeste couvre les extras : un titre qui change réannonce.

## Contrôles à la réception (`Core/Safety`)

`ExtrasValidator`, appelé par `ManifestValidator`. Un seul champ fautif rejette
le manifeste entier. Le noyau vérifie la forme et la taille, jamais le sens.

| Champ | Plafond (`Quotas`) | Forme |
|---|---|---|
| CustomizePlus | 64 Kio | JSON bien formé, profondeur 8 au plus, objet racine |
| Heels | 16 Kio | JSON bien formé, profondeur 8 au plus, objet racine, sans les champs retirés à l'envoi |
| Honorific | 4 Kio | JSON bien formé, objet racine ; `Title` de 32 caractères au plus, sans caractère de contrôle |
| Moodles | 32 Kio | base64 valide qui se décode par `MoodlesCodec` dans ses bornes, avec `Applier`, `Dispeller` et `CustomFXPath` vides |
| PetNicknames | 16 Kio | base64 valide, UTF-16 décodable, en-tête `[PetNicknames(4)]`, lignes d'identité neutres, au plus 64 lignes de 256 caractères |

## Nettoyage à l'envoi (noyau, fonctions pures)

- **`HeelsSanitizer`** retire `EmotePosition`, `MinionPosition` (coordonnées
  absolues dans le monde), `Tags` (chaînes libres d'autres plugins), `E`
  (révèle le plugin Echo) et `PluginVersion`. L'offset reste fonctionnel.
- **`PetNicknamesData`** remplace les lignes 1 à 3 (nom, monde, ContentId)
  par des valeurs neutres fixes, et relit la structure dans ses bornes.
- **`MoodlesCodec`** décode le binaire MemoryPack de `List<MyStatus>` (14
  membres : `GUID`, `IconID`, `Title`, `Description`, `CustomFXPath`,
  `ExpiresAt`, `Type`, `Modifiers`, `Stacks`, `StackSteps`, `ChainedStatus`,
  `ChainTrigger`, `Applier`, `Dispeller`), puis `MoodlesSanitizer` :
  - vide `Applier` et `Dispeller`, qui portent « Nom@Monde », parfois d'un tiers ;
  - remplace `GUID` et `ChainedStatus` par un HMAC-SHA256 tronqué à 16
    octets, sous une clé dérivée par HKDF de l'identité du personnage
    (`"linkpearl:moodles-guid:v1"`). Stable pour un même personnage, donc les
    mises à jour se reconnaissent ; impossible à relier d'un personnage à
    l'autre, alors que le GUID d'origine est identique pour tous les
    personnages d'une même personne ;
  - vide `CustomFXPath` : c'est un VFX joué, donc une ressource transitoire,
    qui relève du sous-projet B et de son blocage.
  - Bornes du décodeur : au plus 64 statuts, chaînes de 1024 caractères au
    plus, rien après la fin de la liste.

## Adaptateur Dalamud

Un `IpcCaller` par plugin, sur le modèle de `GlamourerIpc` : détection et
version, lecture de l'état local, événement de changement, application et
retrait sur un autre personnage. Tout appel se fait sur le thread du
framework.

| Plugin | Version exigée | Lire | Appliquer | Retirer | Changement local |
|---|---|---|---|---|---|
| Customize+ | `CustomizePlus.General.GetApiVersion`, majeure 6 | `Profile.GetActiveProfileIdOnCharacter` (index 0) puis `Profile.GetByUniqueId` | `Profile.SetTemporaryProfileOnCharacter(index, json)` | `Profile.DeleteTemporaryProfileOnCharacter(index)` | `Profile.OnUpdate(index, guid)`, filtré sur l'index 0 |
| SimpleHeels | `SimpleHeels.ApiVersion`, majeure 2 | `GetLocalPlayer` | `RegisterPlayer(index, json)` | `UnregisterPlayer(index)` | `LocalChanged(json)` |
| Honorific | `Honorific.ApiVersion`, majeure 3 | `GetLocalCharacterTitle` | `SetCharacterTitle(index, json)` | `ClearCharacterTitle(index)` | `LocalCharacterTitleChanged(json)` ; `Ready` réapplique |
| Moodles | `Moodles.Version` = 4 | `GetStatusManagerByPtrV2(adresse locale)` | `SetStatusManagerByPtrV2(adresse, données)` | `ClearStatusManagerByPtrV2(adresse)` | `StatusManagerModified(adresse)`, filtré sur l'adresse locale ; `Ready` réapplique |
| PetNicknames | `PetRenamer.IsEnabled` puis `PetRenamer.ApiVersion`, majeure 4 | `GetPlayerData` | `SetPlayerDataV2(contentId, données)` | `ClearPlayerDataV2(adresse)` | `OnPlayerDataChanged` ; `OnReady` réapplique |

- Les variantes « par nom » (`...ByNameV2` de Moodles) ne sont jamais
  employées, ni `ClearPlayerData` V1 de PetNicknames, qui effacerait une entrée
  que l'utilisateur aurait importée lui-même pour ce joueur.
- **PetNicknames côté receveur** : l'adaptateur réinjecte dans les lignes 1 à
  3 le nom, `HomeWorld.RowId` et `BattleChara->ContentId` du personnage visé,
  lus localement, juste avant `SetPlayerDataV2`. Ces valeurs ne quittent pas
  l'adaptateur, ne sont jamais journalisées, et le noyau ne les voit pas.
- **Lecture locale** : `LocalAppearance` lit les cinq états dans le même
  passage sur le thread du framework que les ressources Penumbra, une fois le
  personnage entièrement chargé (`DrawReadiness`), puis appelle les nettoyeurs
  du noyau. Les cinq événements de changement alimentent l'anti-rebond
  existant.

## Application chez le receveur

Le moteur compare le manifeste reçu à celui qui est posé :

- **fichiers, métadonnées ou état Glamourer changés** : application complète
  comme aujourd'hui (collection, mod temporaire, redessin, Glamourer), puis
  les extras ;
- **seuls les extras changés** : `IRemoteApplicator.ApplyExtrasAsync` avec la
  différence, qui n'appelle que les plugins dont la valeur a changé, **sans
  redessin**. Un extra devenu `null` est retiré chez son plugin.

La comparaison vit dans le noyau (`ExtrasDiff`) et se teste sous Linux.

Les extras s'appliquent après le redessin et Glamourer, conformément à l'ordre
imposé par le projet, et sur une cible entièrement chargée (`DrawReadiness`) :
Moodles ignore en silence un personnage qui n'est pas encore affiché.
Customize+, Honorific et PetNicknames oublient ce qu'on leur a posé quand le
personnage sort du champ ; comme le moteur repose tout à chaque réapparition,
ils sont réappliqués naturellement. Sur l'événement `Ready` d'Honorific, de
Moodles ou de PetNicknames, les extras de tous les pairs posés sont
réappliqués.

**Retrait** : au départ du pair, à la pause, au retrait du carnet et à
l'extinction du plugin, les cinq sont défaits avec le reste dans
`RemoveAsync`, par index ou adresse, sur l'objet encore visible.

## Erreurs

- Plugin absent ou de version incompatible chez le receveur : l'extra est
  ignoré, signalé une fois dans le journal, et appliqué dès que le plugin
  apparaît, au manifeste suivant ou sur « Réappliquer ».
- Un appel IPC qui échoue n'empêche ni les autres extras, ni l'apparence.
- Un extra invalide rejette le manifeste entier (règle 3 du projet).
- Plugin absent chez l'émetteur : le champ est `null`.

## Limite connue

SimpleHeels garde un décalage reçu jusqu'à ce qu'on le désenregistre, et ne se
désenregistre qu'avec un personnage visible. Un pair retiré alors qu'il est hors
de vue garde son décalage chez nous jusqu'au rechargement de SimpleHeels. Bénin :
cela ne concerne que ce personnage-là.

## Tests (noyau, sous Linux)

- Codec : aller-retour v2, relecture d'un v1, extras absents, hachage qui
  change quand un extra change.
- `ExtrasValidator` : chaque champ au plafond et juste au-dessus, JSON invalide
  ou trop profond, titre trop long ou avec un caractère de contrôle, et un seul
  champ fautif qui rejette tout.
- `HeelsSanitizer` : champs sensibles retirés, offset conservé.
- `PetNicknamesData` : lignes d'identité neutralisées, bornes du parseur,
  données malformées refusées sans exception.
- `MoodlesCodec` : aller-retour octet pour octet contre la vraie bibliothèque
  MemoryPack (référencée par le projet de tests seulement, jamais par le
  noyau), refus des données tronquées ou hors bornes.
- `MoodlesSanitizer` : noms et VFX vidés, HMAC stable pour une même identité et
  différent pour deux identités.
- `ExtrasDiff` et moteur : un manifeste dont seuls les extras changent
  déclenche `ApplyExtrasAsync` avec la seule différence, sans application
  complète.

Côté jeu, une liste d'essais à deux personnages, plugin par plugin, est remise
avec l'implémentation.

## Hors périmètre

Les ressources transitoires et leur blocage (sous-projet B), les objets
possédés (familiers, compagnons) pour Customize+, et les groupes.
