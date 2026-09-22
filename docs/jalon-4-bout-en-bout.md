# Jalon 4 : la chaîne complète, sur des données réelles

Exécuté le 22 septembre 2026 sur la capture d'un personnage réel.

```
Manifeste : 70 entrées, 81 chemins de jeu, 11934 octets compressés.
Handshake établi en 49 ms.
  Chaîne d'authentification identique des deux côtés.
Plan : 70 blobs, 404,9 Mo.
Transfert : 404,9 Mo en 121,8 s, 70 blobs.
Vérifié : les 70 blobs sont présents, chacun publié parce que son empreinte
recalculée correspondait.
```

Ce qui a été éprouvé : handshake authentifié mutuellement, canal chiffré,
manifeste réel passé par `GamePathPolicy` et `ManifestValidator` avec de vrais
chemins de jeu, plan du manquant, découpage, transfert sur vingt-quatre canaux
LiteNetLib, réassemblage, et vérification d'empreinte à l'arrivée.

Ce qui ne l'est pas : le rendez-vous et la traversée de NAT, qui ne s'éprouvent
pas en boucle locale.

## Le défaut que ce jalon a trouvé

Le harnais envoyait d'abord une trame puis attendait sa réception avant
d'envoyer la suivante. Ce pas-à-pas masquait un défaut de conception réel :

**L'annonce et la clôture d'un blob partaient sur le canal de contrôle, et ses
blocs sur les canaux de données.** LiteNetLib ne garantit l'ordre qu'à
l'intérieur d'un canal : la clôture pouvait donc précéder le dernier bloc.

En pas-à-pas, chaque trame était acquittée avant l'envoi de la suivante, et le
défaut était structurellement invisible. Il ne serait apparu qu'en jeu, de façon
intermittente, sur les gros fichiers, chez certaines personnes seulement.

Correction : un blob entier tient sur un seul canal, et le parallélisme vient de
plusieurs blobs en vol sur des canaux différents. Le récepteur tient un état par
canal.

## Ce que les chiffres disent, et ne disent pas

**405 Mo pour l'apparence statique d'un personnage.** La capture précédente en
annonçait 298, elle était partielle. Le cache contenait au total 143 blobs pour
625 Mo, dont 73 venant de la capture précédente : ce qui est déjà là ne sera
jamais retransféré.

**Le manifeste tient en 12 Ko compressés pour 405 Mo de contenu**, soit un
rapport de 1 pour 34 000. Annoncer son apparence ne coûte rien.

**Les 3,32 Mo/s observés ne sont pas une mesure de transport.** Ils incluent la
lecture depuis un disque Windows à travers WSL, le chiffrement AES-GCM de chaque
trame de 16 Kio et le SHA-256 à l'écriture. C'est un plancher de la chaîne
complète, pas le débit de LiteNetLib ; le banc du jalon 2 reste la référence.

## Une correction d'ergonomie à faire

La capture a été lancée une fois avec les mods désactivés. Le manifeste
enregistré était vide, et il a écrasé le précédent. En usage réel, cela
reviendrait à annoncer à ses pairs que l'on n'a plus aucun mod. Une capture qui
ne trouve rien doit au minimum prévenir.
