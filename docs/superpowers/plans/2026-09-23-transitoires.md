# Animations, VFX et sons : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capter les animations, VFX et sons moddés du joueur, les envoyer avec l'apparence, refuser chez le receveur tout fichier mal formé, et les rendre bloquables globalement et par pair.

**Architecture:** Le noyau porte les règles testables : catégories, liste blanche, contrôle de structure, filtrage, mémoire par job. L'adaptateur porte la capture par l'événement Penumbra, la re-résolution, le contrôle de squelette par Havok et l'interface.

**Tech Stack:** C# / .NET 10, Dalamud, Penumbra.Api 5.19.2, FFXIVClientStructs (Havok), xUnit.

**Spec:** `docs/superpowers/specs/2026-09-23-transitoires-design.md`

**Note d'exécution :** plan exécuté en direct par son auteur, à la demande de l'utilisateur de passer vite à l'implémentation. Chaque tâche donne ses interfaces et ses tests précis ; le code s'écrit en TDD au fil de l'exécution plutôt que d'être recopié ici.

## Global Constraints

- `Linkpearl/Core/` ne référence jamais Dalamud ; toute donnée réseau passe par `Core/Safety`.
- Aucun nom de personnage, monde ou ContentId dans le noyau, les journaux ou `transients.json`.
- Événements IPC potentiellement hors du thread du framework : ne rien y faire d'autre que déposer dans une file concurrente.
- Havok natif uniquement sur le thread du framework.
- Pas de tiret cadratin ; commentaires en français qui expliquent le pourquoi ; constantes mesurées citées.
- Commits en Conventional Commits en français, sans `Co-Authored-By`, `Claude-Session` ni « Generated with ».
- Plugin sans warning ; déploiement par `./scripts/deploy-plugin-dev.sh` après chaque tâche du plugin.

## Review Focus

- Un `.tmb` ou `.avfx` au chemin préfixé `|…|` mais vanilla : jamais capté comme moddé.
- Un blob transitoire tronqué ou vide : refusé sans exception, le reste de l'apparence posé.
- Un ancien carnet sans réglage transitoire : relu avec tout accepté.
- Un réglage changé pendant qu'un pair est affiché : son manifeste est redemandé et refiltré.
- L'événement Penumbra levé pour un autre personnage que le joueur : ignoré.

---

### Task 1: Catégories et liste blanche

**Files:** Create `Linkpearl/Core/Safety/TransientCategories.cs` ; Modify `Linkpearl/Core/Safety/ExtensionAllowList.cs` ; Test `Linkpearl.Core.Tests/Safety/ExtensionAllowListTests.cs`, `Linkpearl.Core.Tests/Safety/TransientCategoriesTests.cs`.

**Produces:** `public readonly record struct TransientCategories(bool Animations, bool Vfx, bool Sounds)` avec `All`, `None`, `And(other)`, `Allows(string gamePath) : bool` (vrai pour tout chemin non transitoire), `static IsTransient(string gamePath) : bool`. `ExtensionAllowList.IsAllowed` accepte `.pap .tmb .avfx .atex .scd`.

- [ ] Tests : `.pap`/`.tmb` sont des animations, `.avfx`/`.atex` des VFX, `.scd` un son ; un `.mdl` est toujours permis ; `None` bloque les trois ; `And` combine ; la liste blanche accepte les transitoires et refuse toujours `.shpk` ; les tests existants qui attendaient un refus des transitoires sont mis à jour.
- [ ] Voir échouer, implémenter, voir passer la suite, commit `feat(safety): catégories transitoires, et les animations, VFX et sons admis`.

### Task 2: Contrôle de structure des blobs transitoires

**Files:** Create `Linkpearl/Core/Safety/TransientFileCheck.cs` ; Test `Linkpearl.Core.Tests/Safety/TransientFileCheckTests.cs`.

**Produces:** `TransientFileCheck.IsWellFormed(string gamePath, Stream content, out string? rejection) : bool` (flux positionnable, lecture bornée, jamais d'exception ; vrai pour un chemin non transitoire).

- [ ] Tests, sur un `.pap` synthétique construit d'après les mesures de la spec (magie, version `0x20001`, info à 26, n animations, Havok à `26 + 40n`, marque tagfile, footer) : accepté ; avec 0 animation : accepté ; avec remplissage avant Havok : accepté ; type monstre : accepté ; mauvaise magie, mauvaise version, info ≠ 26, n > 1024, Havok avant `26 + 40n`, footer hors fichier, section Havok sans marque : refusés un par un ; chaque longueur tronquée : refusée sans exception. `.tmb` `TMLB`, `.avfx` `XFVA`, `.scd` `SEDBSSCF` : acceptés, autre marque refusée ; `.atex` accepté dès qu'il n'est pas vide.
- [ ] Voir échouer, implémenter, voir passer, commit `feat(safety): refuser un fichier d'animation, de VFX ou de son mal formé`.

### Task 3: Filtrage par catégories et retrait des blobs mal formés

**Files:** Create `Linkpearl/Core/Sync/TransientPolicy.cs` ; Modify `Linkpearl/Core/Sync/AppearancePlan.cs` ; Test `Linkpearl.Core.Tests/Sync/TransientPolicyTests.cs`, `Linkpearl.Core.Tests/Sync/AppearancePlanTests.cs`.

**Produces:** `TransientPolicy.Filter(CharacterManifest manifest, TransientCategories allowed) : CharacterManifest` (retire les chemins bloqués, puis l'entrée devenue vide ; renvoie la même instance si rien n'est retiré). `AppearancePlanner.TryBuild` écarte toute entrée transitoire dont le blob échoue `TransientFileCheck`, et `AppearancePlan` gagne `IReadOnlyList<string> Dropped`.

- [ ] Tests : `All` ne retire rien ; animations bloquées retirent `.pap` et `.tmb` seulement ; entrée à chemins mêlés garde ses chemins permis ; un `.pap` au contenu mal formé est absent du plan et nommé dans `Dropped`, le reste posé.
- [ ] Voir échouer, implémenter, voir passer, commit `feat(sync): filtrer par catégorie et écarter un transitoire mal formé avant de poser`.

### Task 4: Mémoire des transitoires par job

**Files:** Create `Linkpearl/Core/Manifest/TransientMemory.cs` ; Test `Linkpearl.Core.Tests/Manifest/TransientMemoryTests.cs`.

**Produces:** `TransientMemory(IClock clock)` : `bool Record(string gamePath, uint job)` (vrai si nouveau pour ce job ou en commun), `IReadOnlyCollection<string> PathsFor(uint job)`, `void Forget(string gamePath)`, `int Purge(TimeSpan olderThan)`, `string ToJson()`, `static TransientMemory FromJson(string json, IClock clock)` (mémoire vide si illisible, jamais d'exception). Chemins normalisés en minuscules et `/`.

- [ ] Tests : un chemin vu sous le job 19 n'est rendu que pour 19 ; revu sous 24, il passe en commun et est rendu pour tous ; `Forget` l'efface partout ; `Purge(30 j)` retire ce qui n'a pas été revu ; aller-retour JSON ; JSON illisible donne une mémoire vide ; plafond de 4 096 chemins.
- [ ] Voir échouer, implémenter, voir passer, commit `feat(core): retenir les animations vues, par job, pour les envoyer même quand elles ne jouent pas`.

### Task 5: Réglages globaux et par pair, appliqués à la réception

**Files:** Modify `Linkpearl/Core/Identity/PairBook.cs` (`PairRecord.Receive`, `SetReceive`) ; `Linkpearl/Integration/PairBookStore.cs` (champs optionnels) ; `Linkpearl/Core/Sync/PeerExchange.cs` (filtre avant plan) ; `Linkpearl/Core/Sync/SyncEngine.cs` (effectif = global et pair, `SetGlobalReceive`, réapplication au changement) ; Test `Linkpearl.Core.Tests/Sync/SyncEngineTests.cs`.

**Produces:** `PairRecord.Receive : TransientCategories` (défaut `All`) ; `PairBook.SetReceive(PeerId, TransientCategories)` ; `SyncEngine.SetGlobalReceive(TransientCategories)` ; `SyncEngineSettings.Receive` (défaut `All`).

- [ ] Test moteur : Alice annonce un `.pap` ; Bob bloque les animations d'Alice ; Bob ne le demande pas et ne le pose pas, le reste est posé ; Bob débloque : le `.pap` est demandé et posé.
- [ ] Voir échouer, implémenter, voir passer la suite, compiler le plugin, commit `feat(sync): bloquer animations, VFX et sons, globalement et par pair, avant tout téléchargement`.

### Task 6: Capture et re-résolution chez l'émetteur

**Files:** Create `Linkpearl/Integration/TransientCapture.cs` ; Modify `Linkpearl/Integration/PenumbraIpc.cs` (`GameObjectResourcePathResolved`, `ResolvePlayerPaths`), `Linkpearl/Integration/LocalAppearance.cs`, `Linkpearl/Plugin.cs`.

- [ ] Abonnement à l'événement ; filtre objet local (adresse relevée à chaque image), préfixe `|…|` retiré, moddé seulement, extension transitoire ; dépôt dans une file concurrente et signal à l'anti-rebond.
- [ ] À la reconstruction : vider la file dans `TransientMemory` sous le job courant (lu sur le thread du framework), re-résoudre `PathsFor(job)` par `ResolvePlayerPaths`, oublier les vanilla, ajouter les autres aux ressources avant classement, sauvegarder `transients.json` dans le dossier du personnage, purge à 30 jours.
- [ ] Compiler sans warning, tester, déployer, commit `feat(integration): capter nos animations, VFX et sons chez Penumbra et les annoncer`.

### Task 7: Contrôle de squelette chez l'émetteur

**Files:** Create `Linkpearl/Integration/PapSkeletonCheck.cs` ; Modify `Linkpearl/Integration/LocalAppearance.cs`.

- [ ] Sur le thread du framework : plus grand indice d'os du squelette local (squelettes partiels du personnage), et, pour chaque `.pap` candidat, section Havok chargée par `hkSerializeUtil`, plus grand indice d'os animé des liaisons. Un `.pap` qui dépasse est retiré des ressources et de la mémoire, et journalisé sans chemin local complet. Exception : écarté. Cache par empreinte du fichier.
- [ ] Compiler sans warning, déployer, commit `feat(integration): n'annoncer que les animations que notre squelette sait jouer`.

### Task 8: Interface et documentation

**Files:** Modify `Linkpearl/Configuration.cs`, `Linkpearl/Ui/Shell/TitleBar.cs` (ou la barre du haut), `Linkpearl/Ui/Pages/PairsPage.cs`, `Linkpearl/Ui/Icons.cs`, `Linkpearl/Plugin.cs`, `docs/reprise.md`, `docs/essais-integrations.md`.

- [ ] Trois bascules dans la barre du haut (réglage global, sauvegardé, transmis au moteur) ; bouton par ligne du carnet ouvrant trois cases (réglage du pair, sauvegardé, réapplication).
- [ ] Essais en jeu ajoutés ; reprise à jour.
- [ ] Compiler, tester, déployer, commit `feat(ui): bascules des animations, VFX et sons, globales et par pair`.
