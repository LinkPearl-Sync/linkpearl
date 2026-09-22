# Jalon 0 : décider et mesurer

Trois choses à établir avant d'écrire une ligne de code. Elles ne se délèguent pas : deux
demandent une décision, la troisième demande le concours du cercle.

Chacune peut invalider une décision de conception. C'est pour cela qu'elles viennent en
premier.

---

## 1. Décision d'exposition

Le 28 août 2025, Naoki Yoshida a publié une déclaration sur le Lodestone. La ligne rouge
qu'il y nomme n'est pas l'hébergement de fichiers, c'est **le fait que les autres joueurs
voient vos mods**. Le pair à pair supprime la cible « opérateur de serveur de fichiers »,
mais pas cette objection.

Trois rôles restent identifiables, et ce ne sont pas forcément la même personne :

| Rôle | Ce qu'il expose | Question à trancher |
|---|---|---|
| Auteur du code | Un nom ou pseudonyme sur un dépôt public | Dépôt public ou privé ? Sous quel pseudonyme ? |
| Hébergeur du dépôt Dalamud tiers | Une URL de distribution, donc une liste d'utilisateurs potentiels | GitHub Pages personnel, ou autre ? |
| Opérateur du rendez-vous | Une machine joignable, qui voit des adresses IP | VPS à ton nom, ou opéré par quelqu'un d'autre ? |

À décider aussi : **est-ce que le rendez-vous reste privé au cercle**, ce qui le rend
invisible, ou publié pour que d'autres puissent s'auto-héberger, ce qui le rend visible.

> Réponse : _à compléter_

---

## 2. Volume réel des personnages du cercle

Ce chiffre pilote tout : le quota de cache par défaut, le critère de débit du jalon 2, et la
question de savoir si le pré-téléchargement est indispensable ou confortable.

**Méthode approximative, disponible maintenant.** Dans Penumbra, pour chaque personnage du
cercle, relever la taille sur disque des dossiers de mods activés dans sa collection. Cela
**surestime** : un mod contient souvent des options non appliquées.

**Méthode exacte, disponible au jalon 1.** La liste des ressources réellement résolues pour
le personnage. C'est précisément ce que le plugin minimal du jalon 1 produira, donc ce
tableau se corrigera à ce moment-là.

| Personnage | Fichiers | Taille brute | Manifeste compressé | Échanges | Écartés | Méthode |
|---|---|---|---|---|---|---|
| Jhalen Tavari | 60 | 298,1 Mo | 12 Ko | 0 | 11 | exacte, 2026-09-22 |
| _autres à compléter_ | | | | | | |

Relevé avec `/lpearl capture`. Hachage des 298 Mo en 537 ms (environ 555 Mo/s,
SHA-NI), copie vers le cache en 789 ms. Le hachage n'est donc pas un goulot.

**Conséquence directe pour le jalon 2** : à 60 ms de latence, un seul canal
LiteNetLib plafonne à 1,5 Mo/s, soit **3 min 20 pour 298 Mo**. Avec 24 canaux,
une quarantaine de secondes. Le critère de passage du jalon 2 doit donc être
vérifié sur un corpus de cet ordre, et non sur les 300 Mo synthétiques prévus
au hasard.

Repère communautaire hérité de Mare : la recommandation était de rester **sous 500 Mo** par
personnage. Un personnage lourd (textures 4K, animations) monte à 200 à 600 Mo, un cas
extrême dépasse le gigaoctet.

**Ce qu'il faut en tirer.** En pair à pair, il n'y a pas de CDN : c'est N envois depuis ta
propre box, pas un envoi puis N téléchargements. Un personnage de 500 Mo sur un débit montant
de 10 Mbit/s, c'est 7 minutes par pair, et 35 minutes si cinq pairs arrivent ensemble.

---

## 3. Enquête connectivité du cercle

À faire remplir par chaque personne du cercle. C'est cette enquête qui dit si le relais est
un secours occasionnel ou le chemin principal.

Pour chacun :

| Question | Comment l'obtenir |
|---|---|
| Version de Windows | `winver`. Noter le numéro de build : sous 20142, certaines primitives crypto récentes sont absentes. |
| Opérateur et type d'accès | Fibre, câble, ADSL, 4G/5G. Le mobile est presque toujours derrière un CGNAT. |
| IPv6 disponible ? | https://test-ipv6.com : noter si une adresse IPv6 **globale** est attribuée. |
| Adresse IPv4 publique ? | Comparer l'IP vue par un site d'écho avec celle de l'interface WAN du routeur. Si elles diffèrent et que la WAN est en 100.64.x.x, c'est du CGNAT. |
| Débit montant | Un test de débit. C'est le débit **montant** qui compte, pas le descendant. |
| UPnP activé sur le routeur ? | Interface d'administration du routeur. |

| Personne | Accès | IPv6 global | Type de NAT | CGNAT | Montant | Relevé |
|---|---|---|---|---|---|---|
| Yann | fixe, Sunrise | **non** (confirmé) | mapping indépendant de la destination, **port préservé** | non | à relever | 2026-09-22 |
| _autres à compléter_ | | | | | | |

Relevé avec `lpprobe classify` contre le VPS. Le port public observé est
identique sur les deux ports du serveur **et** identique au port local : c'est le
cas IPv4 le plus favorable qui existe, la traversée directe devrait fonctionner.

Confirmé depuis Windows, sans la couche de NAT supplémentaire de WSL : port
public identique au port local dans les deux cas. Les deux relevés concordent.

**L'absence d'IPv6 est la mauvaise nouvelle** : c'était l'atténuation gratuite,
celle qui supprime le NAT entièrement. Elle reste utile pour qui en dispose, mais
on ne peut pas bâtir dessus.

### Comment lire le résultat

- **Deux IPv6 globales de part et d'autre** : il n'y a pas de NAT du tout, juste un pare-feu
  d'état. C'est le cas le plus facile, et c'est pour cela qu'IPv6 est tenté en premier.
- **Une personne en CGNAT sans IPv6** : la connexion directe est improbable. Toutes ses paires
  passeront par le relais.
- **Si deux personnes sur huit sont dans ce cas**, le relais n'est plus un secours, c'est une
  fonction principale, et son dimensionnement doit être revu à la hausse dès le jalon 5.

---

## Révision du risque de traversée, 22 septembre 2026

La revue chiffrait le risque de CGNAT sur une population générale : plus de 90 %
des réseaux mobiles, plus de 17 % des réseaux résidentiels.

**Le premier chiffre ne nous concerne pas.** FFXIV se joue sur un PC, à la
maison, sur une ligne fixe. Un utilisateur en 4G n'est pas notre population, sauf
pour les box mobiles, encore minoritaires. Le chiffre pertinent est celui du
CGNAT résidentiel, plus bas, et plus bas encore sur les lignes fixes suisses.

Le risque de traversée est donc plus faible que la revue ne le laissait
entendre. Cela ne remet pas en cause le relais, qui reste nécessaire pour la
minorité concernée et sert aussi le mode « relais seulement » de la vie privée,
mais cela change son dimensionnement : un secours, et non un chemin principal.

### Conséquence sur le test en 4G

Il était présenté comme le test le plus instructif parce que le plus
défavorable. En réalité son résultat est prévisible : le mobile est en CGNAT,
la traversée échouera. Il ne mesure donc pas la faisabilité mais **le délai de
détection de l'échec et de bascule sur le relais**, qui est une question
d'expérience utilisateur.

Le test représentatif est **ligne fixe contre ligne fixe**, et il demande une
seconde personne.
