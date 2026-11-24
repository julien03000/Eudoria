# ADR-0003 — Moteur de jeu pour le client

- **Statut :** Proposé
- **Date :** 2026-11-24
- **Liée à :** ADR-0001 (backend), ADR-0004 (réseau)

## Contexte

Le client affiche l'univers, gère les entrées du joueur et communique avec le noyau et les mondes.
Il doit cibler le **PC d'abord**, puis la **VR** et le **mobile**, sans réécriture complète.
Le moteur choisi pourra aussi servir à faire tourner les **serveurs de monde** (voir ADR-0004).

Critères retenus :
- Porteur de projet seul, niveau intermédiaire, assisté par Claude.
- **Claude doit pouvoir lire et modifier le projet** : les scènes, ressources et réglages doivent être en **texte** autant que possible. Un format binaire empêche Claude de travailler dessus et rend les différences git illisibles.
- Support PC, VR (OpenXR, casques autonomes type Quest) et mobile (Android, iOS).
- Vision long terme : pas de dépendance risquée à la politique commerciale d'un éditeur.
- Possibilité de lancer le moteur **sans affichage** (« headless ») pour les serveurs.

## Options étudiées

### Option A — Godot 4 (avec C#)
**Avantages**
- **Formats texte natifs** : scènes (`.tscn`), ressources (`.tres`), réglages du projet, scripts. Claude peut créer et modifier des scènes directement ; les différences git sont lisibles. C'est le meilleur moteur sur ce critère.
- **Open source (licence MIT)**, gratuit, sans redevance : aucun risque de changement de tarif ou de conditions.
- Léger : téléchargement petit, ouverture et lancement rapides.
- VR via **OpenXR** intégré, support des casques Meta Quest (export Android).
- Export PC (Windows/Linux/macOS) et mobile (Android, iOS).
- Mode **headless** pour faire tourner un serveur de monde avec la même physique et les mêmes scènes que le client.
- Le **C#** permet de partager du code avec le noyau .NET (ADR-0001).

**Inconvénients**
- Moins puissant graphiquement qu'Unreal, écosystème (assets, plugins) plus petit qu'Unity.
- Le support du C# sur **mobile** est plus récent que celui de GDScript (le langage maison de Godot) : à surveiller, à tester tôt.
- Pas d'export web en C# (non nécessaire pour nos cibles actuelles).
- Moins de jeux massivement multijoueurs publiés avec Godot : moins d'exemples à grande échelle.

### Option B — Unity (C#)
**Avantages**
- Écosystème énorme (Asset Store, tutoriels, développeurs), très éprouvé en VR et sur mobile.
- C# : partage de code avec le noyau .NET.
- Solutions réseau multijoueur nombreuses.

**Inconvénients**
- Scènes et prefabs : texte (YAML) possible, mais **verbeux, plein d'identifiants internes, difficile à éditer à la main** ; en pratique Claude devrait passer par l'éditeur pour beaucoup de choses.
- Moteur propriétaire : l'épisode de la « Runtime Fee » (2023, annulée en 2024) montre le risque de dépendre de la politique tarifaire d'un éditeur sur un projet de plusieurs décennies.
- Éditeur lourd, projets volumineux.

### Option C — Unreal Engine 5 (C++ / Blueprints)
**Avantages**
- Rendu graphique de très haut niveau, outils de création de mondes puissants.
- Réseau multijoueur intégré très éprouvé (jeux en ligne à grande échelle).

**Inconvénients**
- **Assets et Blueprints en binaire** : Claude ne peut ni les lire ni les modifier, git ne montre pas les différences. Le critère « formats texte » est fortement pénalisé.
- C++ : langage difficile pour un niveau intermédiaire, compilation longue.
- Très lourd (dizaines de Go, machine puissante requise), redevance de 5 % au-delà d'un certain chiffre d'affaires.
- Le mobile et la VR autonome sont possibles mais exigeants en optimisation.

## Recommandation

**Option A — Godot 4 (dernière version stable au démarrage de l'étape 1), avec C#.**

C'est le seul moteur où **la quasi-totalité du projet reste en texte**, ce qui me permet de travailler
efficacement et te permet de relire ce que je change. Son caractère open source protège le projet sur le long
terme. Le C# permet un langage unique pour tout EUDORIA (ADR-0001).

Point de vigilance accepté : le C# sur mobile. Pour réduire ce risque, on fera **tôt** (dès l'étape 2 ou 3) un
petit test d'export Android, plutôt que de le découvrir tard. Si ce point bloque, l'alternative est d'écrire le
code du client en GDScript tout en gardant C# côté serveur (cela ferait perdre une partie du partage de code).

## Conséquences si cette option est validée
- Installer **Godot 4 édition .NET** et le SDK .NET.
- Le projet client vivra dans `client/` ; le `.gitignore` recevra les règles Godot (`.godot/`, exports).
- Organisation du client en « cœur partagé » + « couche plateforme » (voir `ARCHITECTURE.md`).
- Les modèles 3D/textures (binaires) seront limités au strict nécessaire et rangés à part ; Git LFS pourra être envisagé plus tard (ADR dédié).
