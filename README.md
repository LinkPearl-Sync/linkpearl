<p align="center">
  <img src="Linkpearl/Assets/Images/banner.png" alt="Linkpearl" width="640">
</p>

<p align="center">
  <b>Voyez les apparences moddées de vos amis dans Final Fantasy XIV,<br>
  directement de joueur à joueur, sans serveur au milieu.</b>
</p>

---

## Installer

Dans le jeu, tapez `/xlsettings`, ouvrez l'onglet **Experimental**, et collez cette adresse dans **Custom Plugin Repositories** :

```
https://raw.githubusercontent.com/LinkPearl-Sync/linkpearl/main/repo.json
```

Cliquez sur **+**, puis sur **Enregistrer**. Ouvrez ensuite `/xlplugins`, cherchez **Linkpearl Sync** et installez-le.

**Prérequis :** [Penumbra](https://github.com/xivdev/Penumbra) et [Glamourer](https://github.com/Ottermandias/Glamourer), installés et activés.

---

## Ce que fait Linkpearl

Vous vous pairez avec un ami, en jeu, en deux clics. Dès lors, chacun voit l'autre tel qu'il s'est habillé : mods Penumbra, état Glamourer, et ce que montrent les plugins voisins.

<p align="center">
  <img src="docs/images/comment-ca-marche.svg" alt="Deux joueurs reliés directement par un lien chiffré, le service de rendez-vous à l'écart" width="608">
</p>

- **De joueur à joueur.** Vos fichiers passent directement de votre jeu à celui de votre ami, chiffrés de bout en bout. Aucun serveur ne les stocke ni ne les redistribue.
- **Seulement avec qui vous avez choisi.** Rien ne s'échange sans l'accord des deux.
- **Au-delà des tenues.** Animations, effets visuels et sons moddés, et ce que montrent Customize+, SimpleHeels, Honorific, Moodles et PetNicknames.

---

## Premiers pas

### Au premier lancement

Une courte présentation s'ouvre et vous fait choisir **où ranger le cache** (les apparences reçues, gardées sur votre disque pour ne pas les retélécharger) et **sa taille maximale**. Vous pourrez la revoir à tout moment depuis les réglages.

La fenêtre du plugin s'ouvre avec `/lpearl`, ou en cliquant sur l'entrée Linkpearl de la barre de statut du jeu.

### Se pairer

<p align="center">
  <img src="docs/images/se-pairer.svg" alt="Un glyphe orange à côté du nom, le clic droit « Linkpearl : demander le pairage », puis la demande acceptée par l'autre" width="608">
</p>

1. Approchez-vous de votre ami. Un **glyphe orange** à côté de son nom signale qu'il utilise Linkpearl.
2. **Clic droit** sur son personnage, puis **Linkpearl : demander le pairage**. Vous pouvez aussi passer par la page **Autour** de la fenêtre.
3. Votre ami reçoit une notification et **accepte** d'un clic.

C'est tout : vos apparences s'échangent dès que vous êtes à portée l'un de l'autre.

### Les couleurs des glyphes

| Couleur | Signification |
|---|---|
| Vert | pairé et connecté |
| Orange | utilise Linkpearl, pas encore pairé |
| Bleu | vous a envoyé une demande de pairage |
| Gris | pairé, hors ligne ou en pause |
| Rouge | pairé, mais quelque chose a échoué : voir la page Pairs |

---

## Au quotidien

<p align="center">
  <img src="docs/images/garder-la-main.svg" alt="Une ligne de pair avec les bascules animations, effets, sons et pause" width="608">
</p>

- **Réappliquer** une apparence qui s'est mal posée : clic droit sur le personnage, **Linkpearl : réappliquer**.
- **Mettre un pair en pause** : page **Pairs**, bouton pause. La connexion se ferme et son apparence est retirée.
- **Bloquer animations, effets ou sons** : pour tout le monde depuis la barre de titre de la fenêtre, ou pour un seul pair depuis la page **Pairs**. Rien n'est téléchargé de ce que vous bloquez.
- **Le cache** : dossier et taille dans **Réglages > Cache**. Au-delà de la taille choisie, les apparences les plus anciennes partent, jamais celles que vous avez sous les yeux.
- **Sauvegarder votre identité** : **Réglages > Identité**. Un seul fichier, protégé par un mot de passe si vous le souhaitez. Après une réinstallation ou sur un autre PC, le restaurer évite de refaire chaque pairage.

---

## Vie privée

- Vos fichiers ne quittent votre jeu que vers les pairs que vous avez acceptés, chiffrés de bout en bout.
- Pour que deux joueurs se trouvent, Linkpearl passe par un **service de rendez-vous**. Il ne voit ni vos fichiers, ni vos apparences, ni vos clés, ni le nom de votre personnage.
- Si vous activez **Me signaler aux autres joueurs** (réglage par défaut), le service peut savoir que votre personnage est en ligne : c'est ce qui permet aux autres de vous reconnaître. Vous pouvez le désactiver dans **Réglages > Visibilité**.
- Vous pouvez héberger votre propre service de rendez-vous : voir [linkpearl-rendezvous](https://github.com/LinkPearl-Sync/linkpearl-rendezvous). Vous et vos amis devez simplement en partager au moins un.

---

## Une question, un souci ?

Ouvrez une [issue](https://github.com/LinkPearl-Sync/linkpearl/issues) en décrivant ce que vous avez fait et ce que vous avez vu. Le journal de Dalamud (`/xllog`) aide beaucoup.

Pour les curieux et les contributeurs, la conception et le protocole sont décrits dans [`docs/`](docs/).
