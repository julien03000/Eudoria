# EUDORIA — Outils et installation du poste de développement

> **Rien n'est à installer tant que les ADR ne sont pas validés.** Ce document sert à préparer les installations.
> Dernière vérification : 2026-11-24.

## Règle d'installation

**Tout va sur E:.** Le disque C: est petit (~50 Go libres) : seuls les composants qui l'exigent y vont.
Voir aussi `CLAUDE.md`, section « Environnement ».

## Le poste (vérifié le 2026-11-24)

| Élément | Valeur | Commentaire |
|---|---|---|
| Système | Windows 11 Home | OK (Epic recommande Windows 11) |
| Processeur | AMD Ryzen 9 3900X, 12 cœurs / 24 threads | OK (Epic cite 12 à 16 cœurs comme base pratique pour compiler) |
| Mémoire vive | **16 Go** | ⚠️ **En dessous des 32 Go recommandés par Epic** : l'éditeur et surtout la compilation du moteur depuis les sources (étape 3) seront lents, avec risque de manque de mémoire. À surveiller ; une extension à 32 Go serait utile avant l'étape 3. |
| Carte graphique | AMD Radeon RX 9060 XT | DirectX 12, compatible Nanite/Lumen. |
| Casque VR | Pilote « Meta Virtual Monitor » détecté | Un casque Meta Quest semble avoir été utilisé sur ce PC (utile pour les tests VR plus tard). |
| Disque C: | ~50 Go libres | ⚠️ Pendant l'installation d'Unreal, l'espace libre est temporairement descendu à 28,6 Go : les installateurs utilisent C: comme zone de travail. |
| Disque E: | ~870 Go libres | Disque principal du projet. |

Exigences recommandées par Epic : Windows 11, 32 Go de RAM, carte DirectX 12 avec 8 Go de mémoire vidéo ou plus
([spécifications](https://dev.epicgames.com/documentation/en-us/unreal-engine/hardware-and-software-specifications-for-unreal-engine)).

## Outils nécessaires

Légende de fiabilité des tailles : **mesuré** (sur ce PC) · **officiel** (documentation de l'éditeur) · **estimé** (témoignages ou expérience, à confirmer par l'installateur, qui affiche toujours la taille avant d'installer).

### Déjà installés

| Outil | Rôle | Emplacement | Taille |
|---|---|---|---|
| Git 2.56.1 (avec Git LFS 3.8.1) | Versionnement, fichiers binaires (ADR-0005) | `C:\Program Files\Git` | 0,4 Go (mesuré) |
| Epic Games Launcher | Installer et mettre à jour Unreal | `C:\Program Files\Epic Games\Launcher` | 0,8 Go (mesuré) |
| Unreal Engine 5.8.4 | Moteur client (ADR-0003) | `E:\Epic Games\UE_5.8` | 29,8 Go (mesuré) |

### À installer pour l'étape 1 (après validation des ADR)

| Outil | Rôle | Taille | Sur C: (forcé) | Sur E: |
|---|---|---|---|---|
| **Visual Studio 2026 Community** (18.0 ou plus) | Compiler le C++ d'Unreal ; aussi le SDK .NET 10 du noyau | 20 à 50 Go pour une installation typique (officiel, fourchette globale) ; **~25 à 35 Go** pour les composants ci-dessous (estimé) | **~8 à 12 Go** (estimé) | le reste |
| **Docker Desktop** | Lancer PostgreSQL en local (ADR-0002) | ~3 Go de programme (estimé) + images et données | ~3 Go (programme, estimé) | Données/images (le disque virtuel est déplaçable vers E: dans les réglages) |
| **Total étape 1** | | **~30 à 40 Go** (estimé) | **~11 à 15 Go** | **~20 à 25 Go** |

**Visual Studio — version et composants exigés par UE 5.8** ([configuration Visual Studio pour Unreal](https://dev.epicgames.com/documentation/en-us/unreal-engine/setting-up-visual-studio-development-environment-for-cplusplus-projects-in-unreal-engine)) :
- Version : VS 2022 17.14+ ou **VS 2026 18.0+** ; Epic recommande **VS 2026** pour le développement général.
- Charges de travail :
  - « Développement Desktop en C++ » (*Desktop development with C++*)
  - « Développement de jeux en C++ » (*Game development with C++*)
  - « Développement .NET Desktop » (*.NET desktop development*) — apporte aussi le SDK .NET 10 utilisé par le noyau (ADR-0001)
  - (*.NET Multi-platform App UI development* est listé par Epic ; à confirmer s'il est vraiment nécessaire pour nous, il est volumineux)
- Composants individuels (dans « Développement de jeux en C++ ») : *C++ profiling tools*, *C++ AddressSanitizer*, *Windows 10/11 SDK*, *Unreal Engine installer*.
- Chaîne de compilation : MSVC 14.38 minimum (14.50 recommandé), Windows SDK 10.0.22621 minimum (10.0.26100 recommandé), .NET 10.

**Pourquoi une partie va forcément sur C: :** Microsoft permet de déplacer l'IDE, le cache de téléchargement
et les composants partagés, mais précise que certains outils et SDK s'installent toujours sur le disque
système ([emplacements d'installation](https://learn.microsoft.com/en-us/visualstudio/install/change-installation-locations)).
Cela concerne notamment le **Windows SDK**, les bibliothèques d'exécution C++ et .NET, et le programme
d'installation lui-même. Microsoft ne publie pas de chiffre exact ; un utilisateur rapporte que l'installateur
exigeait encore ~27 Go libres sur C: malgré d'autres emplacements choisis ([témoignage](https://learn.microsoft.com/en-us/answers/questions/1157069/how-to-force-visual-studio-2022-setup-to-download)).
**Important :** les emplacements se choisissent **à la première installation** seulement ; il faudra régler
l'onglet « Emplacements d'installation » vers E: avant de cliquer sur Installer, et lire la répartition C:/E:
affichée en bas de l'installateur.

### À installer plus tard

| Outil | Étape | Rôle | Taille | Sur C: (forcé) |
|---|---|---|---|---|
| **Unreal 5.8 compilé depuis les sources** | 3 | Serveurs dédiés (ADR-0004) | **~150 à 350 Go** (estimé, forums Epic ; pic pendant la compilation) | 0 (tout sur E:) |
| Chaîne de compilation croisée Linux d'Epic | 3 à 6 | Serveurs dédiés Linux | quelques Go (estimé) | 0 |
| Android Studio + SDK/NDK Android + JDK | fin d'étape 4 | Test d'export Meta Quest / Android | ~10 à 20 Go (estimé) | ⚠️ le SDK Android s'installe par défaut dans `%LOCALAPPDATA%` (C:) → à rediriger vers E: |
| Meta Quest Link / outils Meta XR | 4+ | Tests VR | quelques Go (estimé) | partie programme sur C: |
| Symboles de débogage d'Unreal (option du lanceur) | si besoin, étapes 1–2 | Lire les plantages du moteur | plusieurs dizaines de Go (estimé) | 0 |

## Caches à rediriger vers E: (réglages, pas d'installation)

| Cache | Emplacement par défaut | Taille actuelle | Quand |
|---|---|---|---|
| Cache de données dérivées Unreal (DDC, Zen) | `%LOCALAPPDATA%\UnrealEngine\Common` | 1,9 Go (mesuré) — grossit avec les projets, souvent plusieurs dizaines de Go | Début d'étape 1 |
| Cache de téléchargement Visual Studio | `C:\ProgramData\Microsoft\VisualStudio\Packages` | — | À l'installation de VS |
| Disque virtuel Docker (WSL 2) | `%LOCALAPPDATA%\Docker` | — | À l'installation de Docker |
| Bibliothèque Fab (assets téléchargés) | `%LOCALAPPDATA%\Temp\FabLibrary` | 6,1 Go (mesuré) | Dans les réglages du lanceur/Fab |

## Bilan pour le disque C:

| | Go |
|---|---|
| Libre aujourd'hui | ~50 |
| Étape 1 (VS + Docker, part forcée) | − 11 à 15 (estimé) |
| Marge pour Windows, mises à jour, zones de travail des installateurs | ≥ 15 conseillés |
| **Reste après l'étape 1** | **~35 Go** — suffisant **si** les caches ci-dessus sont redirigés vers E: |

Pour gagner de la place sur C: sans rien désinstaller d'utile : voir les traces de l'ancienne installation
d'Unreal et le cache Fab listés dans `docs/PROGRESS.md` (à ta décision).
