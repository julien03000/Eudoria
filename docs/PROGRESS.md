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
- **Plan de la sous-étape 1.2 validé** le 2026-11-24 (choix du porteur : § 9.1 du plan ; NSec vérifié : MIT, maintenu).
- **Sous-étape 1.2 en cours — ⛔ bloquée par Smart App Control** :
  - fait et commité : contrat `POST /v1/accounts` (v1.1.0), module `Eudoria.Core.Identity` (règles, NFKC, Argon2id via NSec,
    modèle, migration `CreateAccounts`, route), `tools/dev/migrate.ps1` ; migration **appliquée avec succès** à la base locale
    (table `identity.accounts` vérifiée) ; compilation 0 avertissement ;
  - écrit **mais pas encore commité** (non vérifié) : tests unitaires (`Eudoria.Core.Identity.Tests`), tests d'intégration,
    de concurrence, de journaux et de contrat (`Eudoria.Core.Api.Tests`) ;
  - **blocage** : Smart App Control (Windows 11, activé sur le poste) refuse de charger la DLL recompilée
    `Eudoria.Core.Identity.dll` (erreur 0x800711C7, journal CodeIntegrity) → les tests et le noyau ne peuvent pas démarrer.
    Une version précédente de la même DLL était passée (jugement au cas par cas). Décision du porteur attendue.
- **Plan de la sous-étape 1.1** (`docs/plans/etape-01-1.md`) validé par le porteur (SDK 11 épinglé, cache NuGet sur E:, dépendances).
- **ROADMAP** : sous-étape **1.7 — Test de rendu** ajoutée (scène après connexion, troisième personne en C++,
  Lumen + Nanite, assets Fab gratuits, mesures images/s / mémoire vidéo / RAM notées dans `docs/`, validation de l'ADR-0003).
- **Sous-étape 1.1 réalisée et ✅ validée par le porteur le 2026-11-24** :
  - cache NuGet redirigé : variable utilisateur `NUGET_PACKAGES = E:\Dev\NuGet\packages` ;
  - `.gitignore` : règles .NET ajoutées, règles `*.sln`/`*.slnx` limitées à `unreal/` ;
  - contrat `shared/api/core-v1.yaml` (OpenAPI 3.1) : `GET /v1/health/live`, `GET /v1/health/ready` ;
  - PostgreSQL **18.7** via `tools/dev/docker-compose.yml` (127.0.0.1 seulement, volume Docker sur E:),
    scripts `start-db.ps1` / `run-core.ps1`, `.env.example` ; ton `.env` local a été créé avec un mot de passe
    aléatoire (jamais affiché, jamais commité) ;
  - noyau `core/` (net10.0, SDK 11 épinglé par `global.json` à la racine) : refuse de démarrer sans chaîne de connexion ;
  - **vérifié en lancement réel** : live → 200, ready → 200 (base démarrée) / 503 `{"status":"unavailable"}`
    en 42 ms (base arrêtée), route inconnue → 404, mot de passe absent des journaux ;
  - **10 tests passent** (intégration + contrat, base PostgreSQL jetable via Testcontainers) ; un test de mutation
    (réponse volontairement faussée) fait bien échouer 2 tests ; build 0 avertissement ; `dotnet format` propre ;
  - écarts par rapport au plan : § 10 de `docs/plans/etape-01-1.md`.
- Au premier lancement de `dotnet`, le SDK a créé un certificat HTTPS de développement **non approuvé** (normal,
  sans effet) ; l'approuver (`dotnet dev-certs https --trust`) reste à décider en 1.5.

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
- **.NET** : Visual Studio 18.12 a installé le SDK **11** (et non 10). Le runtime .NET 10 LTS est présent et le SDK 11 compile pour net10.0 : conforme à l'ADR-0001. Décision (2026-11-24) : SDK 11 épinglé par `global.json`, cible `net10.0`.
- **Tests du noyau** : Docker Desktop doit être démarré (base jetable Testcontainers).
- **Mémoire vive : 16 Go** (32 recommandés). Passage à 32 Go prévu avant l'étape 3. En attendant : `MaxParallelActions` = 6 (fait ; s'applique à l'UBA d'après le code d'UBT 5.8). Retirer la limite une fois à 32 Go.
- **Disque C:** ~71 Go libres ; il descend temporairement pendant les installations.
- `LauncherInstalled.dat` ne liste pas encore UE 5.8 : vérifier que le moteur apparaît dans la Bibliothèque du lanceur.
- La limite mémoire WSL (4 Go) s'applique aussi à la distribution Ubuntu déjà présente sur le PC.
- Unreal 5.9 est en Preview (2026-11-09) ; on reste sur 5.8.x.

## ⏭️ Prochaine étape exacte
1. Le porteur décide quoi faire de **Smart App Control** (voir « En cours »).
2. Ensuite : lancer `dotnet test`, corriger, commiter les tests, lancement réel (`migrate.ps1`, `run-core.ps1`, inscription
   par requête HTTP), mettre à jour `ARCHITECTURE.md` / `SETUP.md`, remplir le § 12 du plan, pousser la branche.
