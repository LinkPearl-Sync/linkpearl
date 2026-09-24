<p align="center">
  <img src="Linkpearl/Assets/Images/banner.png" alt="Linkpearl" width="640">
</p>

<p align="center">
  <b>See your friends' modded looks in Final Fantasy XIV,<br>
  straight from player to player, with no server in between.</b>
</p>

<p align="center">
  <a href="https://linkpearl-sync.github.io/">linkpearl-sync.github.io</a>
</p>

<p align="center">
  <b>English</b> · <a href="README.fr.md">Français</a>
</p>

> [!NOTE]
> The plugin's interface is in French for now. Below, each in-game label is quoted exactly as it appears, followed by its meaning.

---

## Install

In game, type `/xlsettings`, open the **Experimental** tab, and paste this address into **Custom Plugin Repositories**:

```
https://linkpearl-sync.github.io/repo.json
```

Click **+**, then **Save**. Then open `/xlplugins`, search for **Linkpearl Sync** and install it.

**Requirements:** [Penumbra](https://github.com/xivdev/Penumbra) and [Glamourer](https://github.com/Ottermandias/Glamourer), installed and enabled.

---

## What Linkpearl does

You pair with a friend, in game, in two clicks. From then on, each of you sees the other as they dressed up: Penumbra mods, Glamourer state, and what neighbouring plugins show.

<p align="center">
  <img src="docs/images/comment-ca-marche.svg" alt="Two players linked directly by an encrypted connection, with the rendezvous service off to the side" width="608">
</p>

- **Player to player.** Your files go straight from your game to your friend's, end-to-end encrypted. No server stores or redistributes them.
- **Only with the people you chose.** Nothing is shared unless you both agree.
- **More than outfits.** Modded animations, visual effects and sounds, plus what Customize+, SimpleHeels, Honorific, Moodles and PetNicknames show.

---

## Getting started

### First launch

A short introduction opens and asks you to choose **where to keep the cache** (the looks you receive, kept on your disk so they don't have to be downloaded again) and **its maximum size**. You can change both at any time in the settings.

The plugin window opens with `/lpearl`, or by clicking the Linkpearl entry in the game's server info bar.

### Pairing

<p align="center">
  <img src="docs/images/se-pairer.svg" alt="An orange glyph next to the name, the right-click entry « Linkpearl : demander le pairage », then the other player accepting the request" width="608">
</p>

1. Get close to your friend. An **orange glyph** next to their name means they use Linkpearl.
2. **Right-click** their character, then **Linkpearl : demander le pairage** (request pairing). You can also use the **Autour** (nearby) page of the window.
3. Your friend gets a notification and **accepts** with one click.

That's it: your looks are exchanged whenever you are within range of each other.

### Glyph colours

| Colour | Meaning |
|---|---|
| Green | paired and connected |
| Orange | uses Linkpearl, not paired yet |
| Blue | sent you a pairing request |
| Grey | paired, offline or paused |
| Red | paired, but something failed: see the **Pairs** page |

---

## Day to day

<p align="center">
  <img src="docs/images/garder-la-main.svg" alt="A peer row with the animations, effects, sounds and pause toggles" width="608">
</p>

- **Reapply** a look that didn't apply properly: right-click the character, **Linkpearl : réappliquer** (reapply).
- **Pause a peer**: **Pairs** page, pause button. The connection closes and their look is removed.
- **Block animations, effects or sounds**: for everyone from the window's title bar, or for a single peer from the **Pairs** page. Nothing you block is downloaded.
- **The cache**: folder and size in **Réglages > Cache** (Settings > Cache). Past the size you chose, the oldest looks go first, never the ones currently in front of you.
- **Back up your identity**: **Réglages > Identité** (Settings > Identity). A single file, password-protected if you like. After a reinstall or on another PC, restoring it saves you from pairing with everyone again.

---

## Privacy

- Your files leave your game only for the peers you accepted, end-to-end encrypted.
- For two players to find each other, Linkpearl goes through a **rendezvous service**. It sees neither your files, nor your looks, nor your keys, nor your character's name.
- If you keep **Me signaler aux autres joueurs** (let other players see me, on by default), the service can know that your character is online: that is what lets others recognise you. You can turn it off in **Réglages > Visibilité** (Settings > Visibility).
- You can host your own rendezvous service: see [linkpearl-sync-rendezvous](https://github.com/LinkPearl-Sync/linkpearl-sync-rendezvous). You and your friends just need to share at least one.

---

## Questions or problems?

Open an [issue](https://github.com/LinkPearl-Sync/linkpearl-sync-plugin/issues) describing what you did and what you saw. The Dalamud log (`/xllog`) helps a lot. Issues in English or French are both welcome.

For the curious and for contributors, the design and the protocol are described in [`docs/`](docs/) (in French).
