# EUDORIA — Suivi d'avancement

> À mettre à jour à la fin de chaque session.

**Dernière mise à jour :** 2026-11-24
**Étape en cours :** Phase 0 — Fondations du projet (branche `main`)

## ✅ Fait
- Git installé (2.56.1, avec Git LFS 3.8.1), dépôt initialisé sur `main`.
- `docs/ROADMAP.md` : étapes 0 à 10 ; étape 1 détaillée (client **Unreal** minimal) ; étape 2 commence par l'**évaluation de MetaHuman** ; étape 3 inclut la compilation d'Unreal depuis les sources.
- `docs/ARCHITECTURE.md` (révision 2) : noyau .NET / mondes en serveurs dédiés Unreal (plugins Game Feature) / clients Unreal ; schémas Mermaid ; structure du dépôt.
- **Changement de décision du porteur : moteur client = Unreal Engine 5** (photoréalisme prioritaire).
- ADR (tous au statut « Proposé ») dans `docs/decisions/` :
  - ADR-0001 (réévalué) — Backend du noyau → C# / ASP.NET Core (.NET 10 LTS), contrats OpenAPI d'abord ; Go en alternative sérieuse
  - ADR-0002 — Base de données → PostgreSQL (inchangé)
  - ADR-0003 (réécrit) — Moteur client → **Unreal Engine 5.8.x**, règles de travail (C++, Blueprints minimaux, Python), qualité par plateforme, options du moteur
  - ADR-0004 (révisé) — Réseau → réplication native Unreal + serveurs dédiés ; indépendance des mondes par plugins Game Feature ; compilation depuis les sources à l'étape 3
  - ADR-0005 (nouveau) — Fichiers binaires → Git LFS + verrouillage
- `.gitignore` et `.gitattributes` adaptés à Unreal (LFS, fichiers verrouillables).
- `docs/SETUP.md` : outils nécessaires, tailles, répartition C:/E:, caches à rediriger.
- `CLAUDE.md` : nouvelle section « Environnement » (règle « tout sur E: », chemins d'Unreal et de Git).
- Unreal Engine **5.8.4** installé par le porteur dans `E:\Epic Games\UE_5.8` — détecté et vérifié (29,8 Go).

## 🟡 En cours
- Validation des 5 ADR par le porteur de projet.

## 🧹 Traces de l'ancienne installation d'Unreal (rien n'a été supprimé — à ta décision)

| Trace | Emplacement | Taille | Remarque |
|---|---|---|---|
| Clé de registre d'un ancien moteur « 4.0 » | `HKLM\SOFTWARE\EpicGames\Unreal Engine\4.0` (pointe vers `C:\Program Files\Epic Games\4.0\`, dossier inexistant) | négligeable | Orpheline. Peut faire apparaître un moteur fantôme dans certains outils. |
| Cache de données dérivées (DDC) partagé | `%LOCALAPPDATA%\UnrealEngine\Common\DerivedDataCache` | 1,46 Go | Cache régénérable. Sera de toute façon redirigé vers E:. |
| Cache Zen | `%LOCALAPPDATA%\UnrealEngine\Common\Zen` | 0,43 Go | Cache régénérable. |
| Réglages d'anciennes versions | `%LOCALAPPDATA%\UnrealEngine\4.27`, `\5.3`, `\5.5`, `\6.0` | ~0 Go | Fichiers de configuration d'anciennes versions. `6.0` (daté du 03/11/2026) est inhabituel : peut-être une version de test ou de prévisualisation. |
| Données d'un projet « Eudoria » | `%LOCALAPPDATA%\UnrealEngine\Eudoria` | 0,12 Go | Cache web créé les 22–23/11/2026 : un projet Unreal nommé « Eudoria » a été ouvert sur ce PC. Le projet lui-même n'a pas été trouvé (`Documents\Unreal Projects` est vide). **Dis-moi s'il existe un projet à conserver.** |
| Réglages éditeur 5.8 | `%LOCALAPPDATA%\UnrealEngine\5.8` | 0,12 Go | Utilisé par l'installation actuelle : **à garder**. |
| Bibliothèque Fab (assets téléchargés) | `%LOCALAPPDATA%\Temp\FabLibrary` | 6,12 Go | Daté du 23/11/2026. Assets Fab téléchargés ; récupérable en les retéléchargeant. Plus gros gain possible sur C:. |
| Entrée orpheline du lanceur | Ancienne entrée « UE_5.8 » vers `C:\Program Files\Epic Games\UE_5.8` | — | **Déjà disparue** de `LauncherInstalled.dat` depuis ta réinstallation. |

Autres éléments Epic sur C: (non liés à l'ancienne installation, à garder) : lanceur (0,8 Go),
Epic Online Services (0,7 Go), Fortnite et Rocket League (~98 Go dans `C:\Program Files\Epic Games`).

## ⚠️ Problèmes connus / points d'attention
- **Mémoire vive : 16 Go**, pour 32 Go recommandés par Epic. Gênant surtout pour la compilation du moteur (étape 3).
- **Disque C: (~50 Go libres)** : est descendu temporairement à 28,6 Go pendant l'installation d'Unreal. Rediriger les caches vers E: (voir `docs/SETUP.md`).
- `LauncherInstalled.dat` ne liste pas encore UE 5.8 : vérifier que le moteur apparaît dans la Bibliothèque du lanceur.
- Les tailles de Visual Studio sur C: sont **estimées** (Microsoft ne publie pas de chiffre) : l'installateur affichera la répartition exacte.
- Quota Git LFS gratuit de GitHub (10 Gio) trop faible pour des assets photoréalistes : hébergeur à choisir avant le premier envoi d'assets (ADR futur).
- Unreal 5.9 est en Preview (2026-11-09) ; on reste sur 5.8.x.

## ⏭️ Prochaine étape exacte
1. Le porteur relit et valide (ou amende) les ADR 0001 à 0005, et décide quoi faire des traces listées ci-dessus.
2. Passer les ADR validés au statut « Accepté » ; mettre à jour `CLAUDE.md` (« Stack technique », « Structure du dépôt »).
3. Avec l'accord du porteur : installer Visual Studio 2026 (emplacements sur E:) et Docker Desktop, selon `docs/SETUP.md`.
4. Clôturer la Phase 0, créer la branche `etape-01-identite` et proposer le plan de la sous-étape 1.1
   (squelette du noyau + environnement local + route « santé »).
