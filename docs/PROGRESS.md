# EUDORIA — Suivi d'avancement

> À mettre à jour à la fin de chaque session.

**Dernière mise à jour :** 2026-11-24
**Étape en cours :** Étape 1 — Identité numérique (branche `etape-01-identite`)
**Phase 0 :** 🟢 validée par le porteur le 2026-11-24 (sur `main`).

## ✅ Fait (Phase 0)
- Git installé (2.56.1, avec Git LFS 3.8.1), dépôt initialisé sur `main`.
- `docs/ROADMAP.md` : étapes 0 à 10 ; étape 1 détaillée (client Unreal minimal) ; étape 2 commence par l'évaluation de MetaHuman ; étape 3 a pour prérequis le passage à 32 Go de RAM et la compilation d'Unreal depuis les sources.
- `docs/ARCHITECTURE.md` (révision 2) : noyau .NET / mondes en serveurs dédiés Unreal (plugins Game Feature) / clients Unreal ; schémas Mermaid ; structure du dépôt.
- **ADR 0001 à 0005 validés (« Accepté ») le 2026-11-24** :
  - ADR-0001 — Noyau : C# / ASP.NET Core (.NET 10 LTS), contrats OpenAPI d'abord
  - ADR-0002 — Base de données : PostgreSQL
  - ADR-0003 — Client : Unreal Engine 5.8.x (C++, Blueprints minimaux, Python, qualité par plateforme)
  - ADR-0004 — Réseau : réplication native + serveurs dédiés, mondes en plugins Game Feature
  - ADR-0005 — Fichiers binaires : Git LFS + verrouillage
- ADR-0006 — Hébergement du dépôt distant : rédigé (« Proposé »), recommandation GitHub privé après test du verrouillage LFS, alternative Azure DevOps.
- `CLAUDE.md` : sections « Stack technique », « Structure du dépôt » et « Environnement » à jour (règle « tout sur E: », limitation des compilations parallèles d'Unreal tant que le poste a 16 Go).
- `.gitignore` et `.gitattributes` adaptés à Unreal ; `docs/SETUP.md` (outils, tailles, répartition C:/E:).
- Unreal Engine **5.8.4** installé par le porteur dans `E:\Epic Games\UE_5.8` — détecté et vérifié (29,8 Go).
- **Nettoyage** : bibliothèque Fab supprimée (`%LOCALAPPDATA%\Temp\FabLibrary`, 6,1 Go) à la demande du porteur → C: passé de 50 à 56 Go libres.

- **ADR-0006 accepté** : GitHub, dépôt privé `julien03000/Eudoria` ; remote `origin` configuré ; Git LFS activé dans le dépôt (`git lfs install --local`).
- `CLAUDE.md` : règles d'authentification GitHub (jamais de jeton dans un fichier) et **push sur GitHub en fin de session**.
- **Visual Studio 2026 Community 18.12.1** installé sur E: (composants : `tools/vs/eudoria.vsconfig`) — C: 7,5 Go, E: 8,2 Go.
- **Docker Desktop** installé sur E: (programme + données WSL) — C: 0,6 Go, E: 3,2 Go ; mémoire WSL limitée à 4 Go (`.wslconfig`).
- Vérifié : VS détecté à `E:\Dev\VisualStudio\2026\Community`, MSVC 14.52, Windows SDK 10.0.26100, .NET SDK 11.0.100 + runtime .NET 10.0.15.

- **`main` envoyé sur GitHub** (`https://github.com/julien03000/Eudoria`, suivi de `origin/main`). Authentification par Git Credential Manager, faite par le porteur dans le navigateur ; aucun jeton dans un fichier.
- **Verrouillage LFS testé avec succès sur GitHub** : fichier `.uasset` en lecture seule tant qu'il n'est pas verrouillé, `git lfs lock` / `locks` / `locks --verify` / `unlock` fonctionnels. Fichier de test supprimé ensuite (il reste dans l'historique, 2 Ko en LFS).

- PC redémarré ; licence Docker acceptée par le porteur. **Docker testé** : client et serveur 29.9.3, `docker run --rm hello-world` réussi ; distribution `docker-desktop` en WSL 2 ; données dans `E:\Dev\Docker\Data` (1,5 Go). C: : 68,8 Go libres après redémarrage.

## 🟡 En cours
- Étape 1 démarrée sur `etape-01-identite`.
- **Tâches techniques de début d'étape faites (2026-11-24)** :
  - **DDC et Zen d'Unreal redirigés vers E:** par la variable d'environnement utilisateur
    `UE-LocalDataCachePath = E:\Dev\UnrealCache\DDC`. Le code d'UE 5.8 (`ZenServerInterface.cpp`) fait suivre
    Zen automatiquement : ses données iront dans `E:\Dev\UnrealCache\DDC\Zen`. Pris en compte par les
    programmes lancés **après** ce changement (lanceur Epic à redémarrer s'il était ouvert).
    À confirmer au premier lancement de l'éditeur (1.5) : ligne `Found local data cache path=` dans le journal.
  - Anciens caches supprimés (tableau ci-dessous) : **1,95 Go libérés sur C:** (70,8 Go libres).
  - `%APPDATA%\Unreal Engine\UnrealBuildTool\BuildConfiguration.xml` : `MaxParallelActions` = 6,
    `ParallelExecutor/MemoryPerActionBytes` = 2 Gio ; fichier validé contre le schéma XSD d'UBT.
    **Vérifié dans le code d'UBT 5.8** (`ExecutorFactory.cs`, `UBAExecutor.cs`) : l'UBA, toujours utilisé
    en 5.8, reçoit `MaxParallelActions` et lit `MemoryPerActionBytes` → **inutile de désactiver l'UBA**.
    Confirmation pratique (nombre de tâches parallèles dans le journal d'UBT) à la première compilation (1.5).
  - Verrou git périmé `.git/stale-maintenance-lock-from-claude-chat` supprimé.
- **Plan de la sous-étape 1.1** rédigé : `docs/plans/etape-01-1.md` — **en attente du « OK » du porteur**. Aucun code écrit.

## 🧹 Traces de l'ancienne installation d'Unreal (décisions du porteur, 2026-11-24)

| Trace | Emplacement | Taille | Décision |
|---|---|---|---|
| Bibliothèque Fab | `%LOCALAPPDATA%\Temp\FabLibrary` | 6,12 Go | ✅ **Supprimée** le 2026-11-24 |
| Ancien cache de données dérivées (DDC) | `%LOCALAPPDATA%\UnrealEngine\Common\DerivedDataCache` | 1,46 Go | ✅ **Supprimé** le 2026-11-24 (après redirection vers E:) |
| Ancien cache Zen | `%LOCALAPPDATA%\UnrealEngine\Common\Zen` | 0,43 Go | ✅ **Données supprimées** le 2026-11-24 (`Zen\Data`, 0,37 Go). `Zen\Install` (programme Zen, 0,06 Go) conservé : l'éditeur s'en sert |
| Cache de l'ancien projet « Eudoria » | `%LOCALAPPDATA%\UnrealEngine\Eudoria` | 0,12 Go | ✅ **Supprimé** le 2026-11-24 (projet non conservé) |
| Clé de registre « 4.0 » | `HKLM\SOFTWARE\EpicGames\Unreal Engine\4.0` | — | 🚫 **Ne pas toucher** |
| Réglages d'anciennes versions | `%LOCALAPPDATA%\UnrealEngine\4.27`, `\5.3`, `\5.5`, `\6.0` | ~0 Go | 🚫 **Ne pas toucher** |
| Réglages éditeur 5.8 | `%LOCALAPPDATA%\UnrealEngine\5.8` | 0,12 Go | À garder (installation actuelle) |

## ⚠️ Problèmes connus / points d'attention
- **Sauvegarde** : le dépôt est sur GitHub ; la seconde sauvegarde indépendante recommandée par l'ADR-0006 (disque externe) n'est pas encore en place.
- Quota LFS gratuit de GitHub : 10 Gio de stockage et 10 Gio de téléchargement par mois — à surveiller dès l'arrivée des premiers assets.
- **.NET** : Visual Studio 18.12 a installé le SDK **11** (et non 10). Le runtime .NET 10 LTS est présent et le SDK 11 compile pour net10.0 : conforme à l'ADR-0001. Recommandation (plan 1.1, § 2) : SDK 11 épinglé par `global.json`, cible `net10.0` — en attente de décision.
- **Mémoire vive : 16 Go** (32 recommandés). Passage à 32 Go prévu avant l'étape 3. En attendant : `MaxParallelActions` = 6 (fait ; s'applique à l'UBA d'après le code d'UBT 5.8). Retirer la limite une fois à 32 Go.
- **Disque C:** ~71 Go libres ; il descend temporairement pendant les installations.
- `LauncherInstalled.dat` ne liste pas encore UE 5.8 : vérifier que le moteur apparaît dans la Bibliothèque du lanceur.
- La limite mémoire WSL (4 Go) s'applique aussi à la distribution Ubuntu déjà présente sur le PC.
- Unreal 5.9 est en Preview (2026-11-09) ; on reste sur 5.8.x.

## ⏭️ Prochaine étape exacte
1. Le porteur relit `docs/plans/etape-01-1.md` et répond aux points du § 7 (SDK 11 épinglé ou SDK 10 ;
   cache NuGet sur E: ; Testcontainers ; dépendances).
2. Après son « OK » : réaliser la **sous-étape 1.1** (squelette du noyau, PostgreSQL via Docker Compose,
   routes `/v1/health/live` et `/v1/health/ready`, tests) en suivant les commits du § 9 du plan.
3. Ne pas commencer la 1.2 avant la validation de la 1.1.
