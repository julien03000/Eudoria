# ADR-0003 — Moteur de jeu pour le client

- **Statut :** Accepté (validé par le porteur de projet le 2026-11-24)
- **Date :** 2026-11-24 (révision 2 — remplace la proposition « Godot 4 » du même jour)
- **Liée à :** ADR-0001 (backend), ADR-0004 (réseau), ADR-0005 (fichiers binaires)

## Contexte

Le client affiche l'univers, gère les entrées du joueur et communique avec le noyau et les mondes.
Il cible le **PC d'abord**, puis la **VR** et le **mobile**. Le moteur servira aussi à produire les
**serveurs dédiés** des mondes (ADR-0004).

**Changement de priorité (décision du porteur de projet, 2026-11-24) :** l'objectif prioritaire du client
devient un **rendu aussi proche que possible du photoréalisme**. Ce critère passe devant celui des
« formats texte » qui avait conduit à proposer Godot dans la première version de cet ADR.

Critères, dans l'ordre :
1. Qualité visuelle de premier plan (éclairage global dynamique, géométrie très détaillée, humains réalistes).
2. Capacité à décliner le rendu en versions allégées pour la VR et le mobile.
3. Multijoueur en serveurs dédiés éprouvé à grande échelle.
4. Travail efficace d'un porteur seul assisté par Claude (code lisible en texte, automatisation).
5. Pérennité sur le long terme.

## Options étudiées

### Option A — Unreal Engine 5 (retenue)
**Avantages**
- **Nanite** : géométrie virtualisée, permet des modèles extrêmement détaillés sans gérer manuellement les niveaux de détail.
- **Lumen** : éclairage global et réflexions dynamiques en temps réel, sans précalcul.
- **MetaHuman** : humains photoréalistes (visage, corps, animation faciale), cohérent avec l'avatar au cœur de la vision. Depuis UE 5.6, MetaHuman relève de la licence Unreal Engine standard ([metahuman.com/license](https://www.metahuman.com/license)).
- Réseau multijoueur natif (réplication, serveurs dédiés) éprouvé sur des jeux en ligne à très grande échelle (ADR-0004).
- Support PC, VR (OpenXR, Meta Quest) et mobile (Android, iOS) dans le même moteur.
- Code source complet accessible (GitHub, après liaison des comptes Epic et GitHub).
- Éditeur scriptable en **Python** (intégré), ce qui permet d'automatiser une grande partie du travail dans l'éditeur.

**Inconvénients**
- **Assets binaires** (`.uasset`, `.umap`) : Claude ne peut ni les lire ni les modifier directement ; git ne montre pas leurs différences. Atténué par les règles de travail ci-dessous et par l'ADR-0005 (Git LFS, verrouillage).
- **C++** : langage exigeant, compilation longue. Pour un niveau intermédiaire, la relecture sera plus difficile qu'en C#.
- **Lourd** : moteur ~30 Go (installation actuelle), Visual Studio, plus tard ~150 à 350 Go pour la compilation depuis les sources (ADR-0004).
- Matériel recommandé par Epic : 32 Go de RAM, carte graphique DirectX 12 avec 8 Go de mémoire vidéo ou plus, Windows 11 ([spécifications](https://dev.epicgames.com/documentation/en-us/unreal-engine/hardware-and-software-specifications-for-unreal-engine)).
- Le photoréalisme sur PC ne se transpose pas tel quel en VR autonome et sur mobile : il faut une stratégie de qualité par plateforme (voir plus bas).
- Moteur propriétaire : redevance de 5 % au-delà d'1 million de dollars de revenus bruts par produit (conditions standard de la licence Unreal, à relire avant toute commercialisation).

### Option B — Unity
**Avantages :** énorme écosystème, très éprouvé en VR et mobile, C#.
**Inconvénients :** rendu haut de gamme possible (HDRP) mais en retrait d'Unreal pour le photoréalisme « clé en main » (pas d'équivalent direct de Nanite/Lumen/MetaHuman) ; scènes YAML difficiles à éditer à la main ; dépendance à la politique tarifaire de l'éditeur.

### Option C — Godot 4
**Avantages :** formats texte, open source (MIT), léger.
**Inconvénients :** rendu nettement en dessous de l'objectif photoréaliste ; peu de références en multijoueur massif. Ne répond plus au critère n°1.

## Décision proposée

**Unreal Engine 5, version 5.8.x** (dernière version stable). La 5.9 n'existe qu'en **Preview** depuis le 2026-11-09,
avec une version finale annoncée pour 2027 ([notes 5.9](https://dev.epicgames.com/documentation/unreal-engine/unreal-engine-5-9-release-notes)) :
on ne l'utilise pas. Toute montée de version du moteur fera l'objet d'une décision explicite.

## Installation détectée sur ce PC (vérifiée le 2026-11-24)

| Élément | Valeur |
|---|---|
| Chemin | `E:\Epic Games\UE_5.8` |
| Version exacte | **5.8.4** (Changelist 58902052, `Engine/Build/Build.version`) |
| Éditeur | `Engine\Binaries\Win64\UnrealEditor.exe` présent |
| Python intégré | `Engine\Binaries\ThirdParty\Python3\Win64\python.exe` présent |
| Taille sur disque | 29,8 Go |
| Dossiers de plateformes présents | Windows, Android, IOS, VisionOS (à confirmer dans les options du lanceur : la présence d'un dossier ne garantit pas que la plateforme cible complète est installée) |
| Symboles de débogage | Non installés |
| Linux (cible) | Non installé |

Remarque : le fichier du lanceur `C:\ProgramData\Epic\UnrealEngineLauncher\LauncherInstalled.dat` ne liste pas
encore cette installation. Il faut vérifier que le moteur apparaît bien dans la Bibliothèque du lanceur
(sinon, utiliser « Localiser » dans le lanceur). Les traces de l'ancienne installation sont listées dans `docs/PROGRESS.md`.

## Règles de travail avec Unreal

1. **Toute la logique en C++.** Règles du jeu, réseau, communication avec le noyau, interface (comportement) :
   tout est écrit en C++, donc en texte, relisible et versionné proprement.
2. **Blueprints réduits au minimum.** Un Blueprint n'est autorisé que comme **classe enfant d'une classe C++**,
   pour assigner des assets (maillages, matériaux, sons) et régler des valeurs exposées par le C++.
   Pas de logique dans les graphes Blueprint. Même règle pour les widgets d'interface : le C++ définit le
   comportement, le Widget Blueprint ne fait que la mise en page.
3. **Automatisation de l'éditeur par scripts Python** quand c'est possible : création et configuration d'assets,
   import, création des Blueprints enfants, vérifications de conformité (nommage, absence de logique Blueprint).
   Les scripts vivent dans le dépôt, en texte. Ce qui ne peut pas être scripté est décrit pas à pas pour toi.
4. **Configuration en texte** : réglages du projet dans les fichiers `Config/*.ini` (texte), privilégiés aux réglages faits à la main dans l'éditeur.
5. **Conventions de nommage** Unreal standard (préfixes `A`, `U`, `F`, `I`, `E` en C++ ; `BP_`, `M_`, `MI_`, `T_`, `SM_`, `SK_`, `WBP_`… pour les assets).
6. **Fichiers binaires** : gérés via Git LFS avec verrouillage (ADR-0005).

## Stratégie de qualité par plateforme

Le même projet produit plusieurs niveaux de qualité, pilotés par les **Device Profiles** et les réglages de
**Scalability** d'Unreal (fichiers `.ini`, donc en texte).

| Plateforme | Objectif | Rendu |
|---|---|---|
| **PC** (priorité) | Photoréalisme | Nanite, Lumen (éclairage global + réflexions), ombres virtuelles, MetaHuman en haute qualité. Niveaux Haut/Épique, avec un niveau Moyen pour les PC modestes. |
| **VR PC** (casque relié au PC) | Fluidité ≥ 90 images/s par œil | Mêmes contenus, Lumen réduit ou remplacé par un éclairage plus simple, résolution adaptative, effets coûteux désactivés. |
| **VR autonome** (Quest) | Fluidité sur puce mobile | Rendu mobile (pas de Lumen ni d'ombres virtuelles), éclairage précalculé ou simplifié, maillages et textures allégés, MetaHuman en niveau de détail bas. |
| **Mobile** | Fluidité et batterie | Comme la VR autonome, avec interface adaptée au tactile. |

Principes :
- Les contenus sont **créés une fois en haute qualité**, et déclinés par plateforme (niveaux de détail, textures, matériaux simplifiés).
- La logique de jeu ne dépend **jamais** de la plateforme ; seules les entrées, la caméra, l'interface et le rendu changent.
- Un **budget de performance** (images/s, mémoire) sera fixé pour chaque plateforme au moment où elle sera ciblée.

## Composants optionnels du moteur (à ajouter plus tard)

Rien n'est à installer maintenant. Le lanceur affiche la taille exacte de chaque option avant validation ;
Epic ne publie pas de chiffres officiels par option, et les témoignages montrent que les options peuvent
**doubler** la taille du moteur ([forum](https://forums.unrealengine.com/t/updating-to-5-5-1-from-5-5-0-brought-my-engine-size-from-36gib-to-101gib-how-to-reduce-it/2212985)).

| Composant | Utilité | Quand l'ajouter |
|---|---|---|
| **Symboles de débogage de l'éditeur** | Lire les piles d'appels quand un plantage se produit dans le code du moteur | Seulement si un plantage dans le moteur bloque aux étapes 1–2. À l'étape 3, la version compilée depuis les sources inclut ses propres symboles, ce qui rend cette option inutile. Elle est très volumineuse (plusieurs dizaines de Go d'après les témoignages). |
| **Plateforme cible Linux** | Serveurs dédiés Linux (moins chers et plus courants en hébergement) | Les serveurs dédiés exigent de toute façon la version compilée depuis les sources (ADR-0004). On ajoutera la **chaîne de compilation croisée Linux** d'Epic (clang 20.1.8 annoncé pour UE 5.7–5.8, [exigences Linux](https://dev.epicgames.com/documentation/unreal-engine/linux-development-requirements-for-unreal-engine), à vérifier au moment voulu) à l'étape 3 si l'on veut tester des serveurs Linux, au plus tard **avant l'étape 6**, quand plusieurs mondes devront être hébergés. Pendant les étapes 3 à 5, les serveurs peuvent tourner sous Windows en local. |
| **Plateforme cible Android** | VR autonome (Meta Quest) et mobile Android | Pour un **test d'export précoce** sur Quest/Android à la fin de l'étape 4, afin de mesurer tôt le coût du photoréalisme sur ces plateformes. Demandera aussi Android Studio, le SDK/NDK Android et un JDK (voir `docs/SETUP.md`). |
| **Plateforme cible iOS** | Mobile Apple | Non prévu avant longtemps (nécessite un Mac). |

## Conséquences si cette option est validée
- Installer Visual Studio avec les composants exigés par UE 5.8 (voir `docs/SETUP.md`).
- Le projet Unreal vivra dans `unreal/` (voir `ARCHITECTURE.md`).
- Le `.gitignore` et le `.gitattributes` sont adaptés à Unreal (ADR-0005).
- Le cache de données dérivées d'Unreal (DerivedDataCache, Zen) s'écrit **par défaut sur C:** (`%LOCALAPPDATA%\UnrealEngine\Common`, déjà 1,9 Go). Il faudra le **déplacer vers E:** au début de l'étape 1 (simple réglage, pas d'installation), conformément à la règle « tout sur E: ».
- Mise à jour de la section « Stack technique » de `CLAUDE.md` après validation.

## Points ouverts

- **Animation des avatars et règle « aucune logique Blueprint »** *(ajouté le 2026-11-27, à la demande du porteur,
  plan 1.7 décision D3)* : les **Animation Blueprints** et les **Control Rig** sont l'outil standard d'Unreal pour un
  avatar réaliste (transitions entre animations, ajustement des pieds au sol (IK), visage). La règle 2 (« pas de logique
  dans les graphes Blueprint ») devra être **précisée pour eux** — par exemple : toute la logique dans une classe C++
  dérivée d'`AnimInstance`, et le graphe d'animation autorisé pour le seul assemblage des poses. **Décision du porteur
  à l'étape 2** (évaluation de MetaHuman). D'ici là, la scène de test de la 1.7 anime le personnage en C++
  (mode « nœud unique », sans graphe ni IK). **Renforcé par l'ADR-0008** (première personne, corps visible, IK du corps
  entier en VR) : cette précision devient une condition de l'étape 2.
- **Validation sur le poste du porteur** : mesures de la sous-étape 1.7 (`docs/perf/`), à ajouter ici une fois faites ;
  seconde campagne identique prévue après le passage à 32 Go de RAM.
