# ADR-0004 — Solution réseau multijoueur

- **Statut :** Proposé
- **Date :** 2026-11-24
- **Liée à :** ADR-0001 (backend), ADR-0003 (moteur client)

## Contexte

Il faut faire communiquer en **temps réel** les clients et les **serveurs de monde** :
déplacements, actions, état du monde, discussion locale. (La communication client → noyau, elle, se fait en HTTPS
classique et n'est pas concernée ici.)

Exigences issues de la vision et de `CLAUDE.md` :
- **Serveur qui fait autorité** : le client envoie des intentions, le serveur simule et tranche (anti-triche).
- **Serveurs dédiés** (pas un joueur qui héberge la partie), car les mondes sont persistants.
- Mondes **indépendants**, qui peuvent être mis à jour séparément : le protocole doit être **versionné**.
- PC, VR et mobile doivent pouvoir se retrouver dans le même monde.
- Solution compréhensible et maintenable par une personne seule aidée par Claude.
- Pas de dépendance à un service payant indispensable.

Hypothèse : ADR-0001 (C#/.NET) et ADR-0003 (Godot en C#) sont validés. Si ce n'est pas le cas, cet ADR doit être revu.

## Options étudiées

### Option A — Serveur de monde Godot headless + réseau « haut niveau » intégré de Godot
Le serveur de monde est le même projet Godot lancé sans affichage. On utilise les outils réseau intégrés
(appels distants « RPC », nœuds de synchronisation automatique, transport ENet en UDP).

**Avantages**
- Le plus rapide à mettre en place : beaucoup de choses sont automatiques.
- Même physique, mêmes scènes, mêmes collisions côté client et côté serveur.
- Gratuit, intégré, documenté.

**Inconvénients**
- Le protocole est **implicite** : il dépend de l'arbre de scènes Godot. Difficile à versionner proprement, ce qui gêne l'indépendance des mondes et la compatibilité entre versions.
- Synchronisation automatique peu optimisée pour beaucoup de joueurs (envoie souvent plus que nécessaire).
- Plus difficile à tester automatiquement hors du moteur.

### Option B — Serveur de monde Godot headless + protocole EUDORIA explicite (recommandée)
Même serveur Godot headless, mais les messages réseau sont **définis explicitement** dans une bibliothèque C#
partagée (`shared/`) : chaque message a un type, un format et une version. Le transport reste **ENet**
(UDP fiable, déjà inclus dans Godot), utilisé à bas niveau.

**Avantages**
- Garde l'avantage clé : même moteur, mêmes scènes, même physique des deux côtés.
- **Protocole versionné et documenté** : un monde et un client savent s'ils sont compatibles ; cohérent avec le principe d'API versionnées.
- Contrôle de ce qui est envoyé (seulement ce que le joueur doit voir) : meilleur pour la performance et l'anti-triche.
- Le protocole et la logique de jeu peuvent être **testés automatiquement** en C# pur, sans lancer le moteur.
- Aucune dépendance supplémentaire, aucun coût.

**Inconvénients**
- Plus de travail initial que l'option A : il faut écrire soi-même la sérialisation, la prédiction côté client et la correction serveur (prévu à l'étape 4).
- Demande de la rigueur pour faire évoluer le protocole.

### Option C — Serveur de monde en .NET pur (sans moteur) + LiteNetLib
Les serveurs de monde sont des programmes .NET indépendants du moteur, utilisant la librairie réseau LiteNetLib (UDP fiable).

**Avantages**
- Serveurs très légers et très performants, indépendants de Godot.
- Même langage et mêmes outils que le noyau.

**Inconvénients**
- Le serveur ne connaît ni les scènes ni la physique de Godot : il faut **réécrire ou exporter** collisions, cartes et navigation, avec un risque d'écarts entre ce que voit le client et ce que décide le serveur.
- Nettement plus de travail pour une personne seule.

### Options écartées
- **Photon (Fusion/Quantum)** : excellent mais lié à Unity, payant à l'échelle et hébergé par un tiers.
- **Nakama** (serveur de jeu open source) : très complet (comptes, stockage, économie, temps réel), mais il **remplacerait une grande partie du noyau** par un produit externe écrit en Go, avec ses propres choix. Contraire à l'objectif de maîtriser le noyau. Pourrait être reconsidéré si le porteur préfère aller plus vite au détriment du contrôle.

## Recommandation

**Option B — serveurs de monde Godot headless (C#) avec un protocole EUDORIA explicite et versionné, transporté par ENet.**

Elle combine le meilleur des deux mondes : la simplicité de partager les scènes et la physique avec le client,
et la rigueur d'un protocole versionné qui respecte l'architecture (mondes indépendants, serveur qui fait autorité).
Le surcoût par rapport à l'option A est réel mais concentré sur l'étape 4, et c'est un investissement durable.

Point d'attention : ENet fonctionne en UDP, ce qui convient à PC, VR et mobile natifs. Un client navigateur web
ne serait pas compatible ; ce n'est pas une cible actuelle. Si cela le devient, un transport WebSocket/WebRTC
pourra être ajouté sans changer le protocole, puisque les messages sont indépendants du transport.

## Conséquences si cette option est validée
- Création, à l'étape 3, d'une bibliothèque `shared/` contenant le protocole (messages, versions).
- Les serveurs de monde vivent dans `worlds/` et réutilisent les scènes nécessaires du client.
- Chaque serveur de monde a sa propre identité auprès du noyau (voir `ARCHITECTURE.md`).
- La prédiction client / correction serveur et la gestion de la latence seront traitées à l'étape 4.
