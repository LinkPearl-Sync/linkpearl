# Protocole Linkpearl, version 1.0

Ce document décrit le protocole sur le fil. Il existe pour qu'une personne
extérieure puisse le relire sans lire le code.

> **La construction n'a pas encore été relue.** Elle est maison, et les vecteurs
> figés du dépôt ne valident que sa cohérence avec elle-même, puisqu'ils ont été
> produits par cette même implémentation. Une relecture extérieure est une
> condition de diffusion, pas une amélioration souhaitable.

## Primitives

| Rôle | Primitive |
|---|---|
| Identité, signature | ECDSA P-256, SHA-256, signature au format concaténé r&#124;&#124;s, 64 octets |
| Accord de clé | ECDH P-256, éphémère à chaque session |
| Dérivation | HKDF-SHA256 |
| Chiffrement authentifié | AES-256-GCM, étiquette de 16 octets |
| Adressage de contenu | SHA-256 |

Toutes viennent de `System.Security.Cryptography`. Aucune dépendance. Voir
`Core/Crypto/CryptoPrimitives.cs` pour la justification du choix de P-256 plutôt
que des courbes 25519, et d'AES-GCM plutôt que ChaCha20-Poly1305.

Un point public reçu du réseau est vérifié comme appartenant à la courbe avant
tout usage : `y² ≡ x³ - 3x + b (mod p)`, et coordonnées dans le corps. **Le
runtime ne fait pas cette vérification à l'import**, ce qui laisserait la porte
ouverte aux attaques par courbe invalide.

## Handshake, SIGMA-I

Trois trames. L'initiateur est A, le répondeur est B.

```
prologue = "linkpearl-handshake-v1"

msg1  A -> B   clair    version(2) || horodatage(8) || ephA(65) || aleaA(16)          91 octets
msg2  B -> A   clair    version(2) || ephB(65) || aleaB(16)                           83 octets
               scelle   { idB(65) || sigB(64) || liaisonB(32) } + etiquette(16)       177 octets
msg3  A -> B   scelle   { idA(65) || sigA(64) || liaisonA(32) } + etiquette(16)       177 octets
```

Dérivation :

```
dh      = ECDH(ephA, ephB)
th1     = SHA256(prologue || msg1 || msg2_clair)
prk     = HKDF-Extract(sel = th1, ikm = dh)
k_b     = HKDF-Expand(prk, "b->a", 32)          scelle msg2
k_a     = HKDF-Expand(prk, "a->b", 32)          scelle msg3
k_liai  = HKDF-Expand(prk, "bind", 32)
sid     = HKDF-Expand(prk, "session id", 16)
k_a2b   = HKDF-Expand(prk, "data a->b", 32)     canal de donnees
k_b2a   = HKDF-Expand(prk, "data b->a", 32)

sigB      = ECDSA(idB, "linkpearl-sig-responder-v1" || th1)
liaisonB  = HMAC-SHA256(k_liai, idB_pub || th1)
th2       = SHA256(th1 || msg2_scelle)
sigA      = ECDSA(idA, "linkpearl-sig-initiator-v1" || th2)
liaisonA  = HMAC-SHA256(k_liai, idA_pub || th2)
```

Les deux trames scellées utilisent un **nonce constant**, ce qui est sûr ici et
seulement ici : chaque clé vient d'un accord éphémère neuf, ne chiffre qu'un
seul message, et les deux sens ont des clés distinctes. Le transcript sert de
donnée associée.

### Propriétés visées

- **Confidentialité persistante.** Les clés de session viennent d'éphémères.
  Compromettre une identité ne déchiffre pas les sessions passées.
- **Authentification mutuelle.** Chacun signe un transcript qui contient les
  deux éphémères et les deux aléas.
- **Liaison identité-secret.** Le HMAC sous une clé dérivée du secret d'accord
  interdit de relayer l'authentification d'un tiers. Le chiffrement authentifié
  de l'identité sous une clé dérivée de `dh` remplit déjà ce rôle : la liaison
  est une ceinture par-dessus une bretelle, pour trente-deux octets.
- **Anti-rejeu.** L'horodatage de `msg1` doit tomber dans une fenêtre de
  soixante secondes, dans les deux sens.

### Ce qui autorise, et ce qui n'autorise pas

**L'autorisation vient du carnet local.** La clé publique reçue doit y figurer
comme acceptée, et elle est comparée avant tout travail cryptographique.

Le service de rendez-vous n'intervient à aucun moment dans cette décision. Il
peut refuser son service ou mentir sur une adresse, ce qui produit un échec de
connexion ; **il ne peut pas faire accepter une identité**, puisque le code
d'invitation porte déjà la clé publique complète.

## Canal de données

Chiffrement au niveau du message applicatif, pas du datagramme. Chiffrer chaque
datagramme s'appliquerait aussi aux paquets de connexion et à la découverte de
MTU du transport, ce qui rendrait le handshake circulaire.

```
trame = type(1) || canal(1) || compteur(8) || chiffre || etiquette(16)
nonce = sid[0..4] || compteur(8)
donnee associee = type || canal || compteur
```

**L'octet de poids fort du compteur porte l'index de canal.** Deux canaux ne
peuvent donc pas produire le même nonce, par construction et non par convention.
Les cinquante-six bits restants comptent les messages du canal ; au-delà, la
session doit être renégociée.

Le récepteur tient un compteur par canal et refuse tout compteur inférieur ou
égal au dernier accepté. **Le compteur n'avance qu'après authentification**,
sans quoi une trame forgée au compteur élevé ferait rejeter les trames
légitimes qui suivent.

Conséquence assumée : les en-têtes du transport restent en clair. Ils ne
révèlent rien qu'un observateur ne déduirait des tailles et des temps.

## Chaîne d'authentification courte

Six mots tirés d'une liste de soixante-quatre, dérivés de `sid`, soit
trente-six bits. Destinée à être comparée de vive voix. Elle n'est pas la
barrière principale, puisque le code d'invitation porte la clé publique : elle
couvre le cas où ce code aurait transité par un canal douteux.

## Versionnage

`version` porte un majeur et un mineur. Le majeur doit être identique des deux
côtés. Le mineur se négocie au minimum des deux.

## Vecteurs figés

`Linkpearl.Core.Tests/Fixtures/protocol-vectors.json` couvre la dérivation de
clés et le format de trame du canal, qui sont déterministes. Le handshake tire
des éphémères et des aléas, ses trames ne sont donc pas reproductibles.

Régénérer : `dotnet run --project Linkpearl.Harness -- vectors`. **Une
modification de ce fichier est un changement de protocole sur le fil**, et doit
s'accompagner d'une montée de version.
