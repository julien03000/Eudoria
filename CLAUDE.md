# EUDORIA — Instructions pour Claude Code

## Le projet
EUDORIA est un univers numérique persistant composé de nombreux mondes interconnectés
(inspiration : l'OASIS de *Ready Player One*). Vision complète : `docs/VISION.md`.
Toute décision doit servir cette vision. En cas de doute sur l'interprétation, demande.

## Contexte du porteur de projet
- Je porte le projet **seul**, avec toi comme développeur principal.
- Niveau **intermédiaire** : je lis et comprends du code, mais je ne suis pas expert.
  Explique tes choix simplement, en français, sans jargon inutile.
- Plateforme cible du premier prototype : **PC**. VR et mobile viendront plus tard,
  mais l'architecture doit les prévoir dès le départ.

## Documents de référence
Au début de chaque session, lis **toujours** :
- `docs/PROGRESS.md` — où on en est, et la prochaine étape exacte
- `docs/ROADMAP.md` — les phases du projet et l'étape en cours

Lis selon le besoin :
- `docs/VISION.md` — la vision (ne jamais la modifier sans demande explicite)
- `docs/ARCHITECTURE.md` — l'architecture technique actuelle
- `docs/decisions/` — les décisions techniques (ADR), un fichier par décision

## Règles de travail
1. **Une tâche à la fois.** Si une demande est trop grosse, découpe-la et propose le découpage avant de coder.
2. **Plan avant code.** Pour toute tâche non triviale : présente un plan (fichiers touchés, approche, risques) et attends mon « OK ».
3. **Pas de changement d'architecture ou de techno sans ADR validé.** Si tu penses qu'un choix existant est mauvais, dis-le et propose un ADR — ne le change pas de toi-même.
4. **Rester dans l'étape en cours.** Pas de fonctionnalités « au cas où » ni d'anticipation sur les étapes suivantes.
5. **Chaque étape doit produire quelque chose de réellement fonctionnel et testé.**
6. **Dépendances :** n'ajoute une librairie que si elle est justifiée ; dis-moi laquelle et pourquoi.
7. **Secrets :** jamais de mot de passe, clé ou token dans le code. Utilise `.env` (jamais commité) et un `.env.example`.
8. **Si tu es bloqué ou hésites entre deux options : arrête-toi et demande.** N'improvise pas, ne contourne pas un problème en silence.
9. **Ne supprime ni ne réécris massivement du code existant** sans me l'avoir annoncé.

## Définition de « terminé »
Une tâche n'est terminée que si :
- le code a été lancé et fonctionne ;
- des tests existent et passent ;
- il n'y a pas d'erreur de compilation ni de lint ;
- `docs/ARCHITECTURE.md` est à jour si la structure a changé ;
- `docs/PROGRESS.md` est à jour ;
- un commit git a été fait.

## Git
- Une branche par étape de la roadmap (ex. `etape-01-identite`), fusionnée dans `main` quand l'étape est validée par moi.
- Commits petits et fréquents, format : `type(zone): description`
  (types : `feat`, `fix`, `docs`, `refactor`, `test`, `chore`).
- Jamais de `push --force`, `rebase` ou suppression de branche sans ma demande.
- Dépôt distant : GitHub (`origin`, HTTPS). Authentification par le gestionnaire d'identifiants de Git
  (Git Credential Manager) : **c'est moi qui me connecte** dans la fenêtre GitHub ;
  **jamais de jeton ou mot de passe dans un fichier**, une URL ou une commande.
- Assets binaires : verrouiller (`git lfs lock`) avant de modifier un `.uasset`/`.umap`, déverrouiller après l'envoi.

## Fin de session
Avant de terminer une session :
1. mets à jour `docs/PROGRESS.md` : ce qui est fait, ce qui est en cours, les problèmes connus, et **la prochaine étape exacte** ;
2. fais le commit ;
3. **envoie (push) sur GitHub** la ou les branches concernées.

## Principes d'architecture (issus de la vision)
- Le **noyau** (identité, comptes, avatars, persistance, inventaire, économie, registre des mondes)
  est séparé des **mondes**.
- Les mondes sont des modules indépendants qui communiquent avec le noyau via des **API versionnées**.
- L'identité et l'avatar sont uniques et suivent l'utilisateur dans tous les mondes.
- Le **serveur fait autorité** : le client n'est jamais cru sur parole (anti-triche, économie).
- Sécurité, stabilité, performance et maintenabilité passent avant la vitesse du prototype.
- Penser multi-plateforme (PC, VR, mobile) dès la conception, même si on ne développe que PC au début.

## Stack technique
Validée le 2026-11-24 (ADR « Accepté » dans `docs/decisions/`) :
- **Noyau (backend)** : C# / ASP.NET Core sur **.NET 10 LTS**, monolithe modulaire ; contrats d'API en
  **OpenAPI** écrits avant le code (`shared/api/`) — ADR-0001.
- **Base de données** : **PostgreSQL**, migrations versionnées ; en local via Docker — ADR-0002.
- **Client** : **Unreal Engine 5.8.x** — ADR-0003. Règles impératives :
  - toute la logique en **C++** ;
  - Blueprints uniquement comme classes enfants de classes C++, pour assigner assets et réglages (aucune logique dans les graphes) ;
  - automatisation de l'éditeur par **scripts Python** quand c'est possible ;
  - réglages en texte (`Config/*.ini`) ; qualité par plateforme (PC photoréaliste, VR et mobile allégés).
- **Réseau** : réplication native Unreal + **serveurs dédiés** ; un monde = un plugin **Game Feature** qui ne
  dépend que du plugin socle `EudoriaCore` ; moteur compilé depuis les sources à partir de l'étape 3 — ADR-0004.
- **Fichiers binaires** : **Git LFS** avec verrouillage (`.uasset`, `.umap` verrouillables) — ADR-0005.
- **Hébergement du dépôt distant** : **GitHub, dépôt privé** `julien03000/Eudoria`
  (https://github.com/julien03000/Eudoria), remote `origin`, en HTTPS — ADR-0006.

## Environnement
- **Règle d'installation : tout sur E:.** Le disque C: n'a qu'environ 50 Go libres ; seuls les composants
  qui l'exigent y vont (Visual Studio en installe une partie de force, estimée à 8–12 Go ; Docker < 1 Go).
  Les caches (Unreal DDC/Zen, Docker, Fab, SDK Android) doivent être redirigés vers E:.
  Ne rien installer sans mon accord. Détail des outils et tailles : `docs/SETUP.md`.
- **Unreal Engine 5.8.4** (version du lanceur Epic) : `E:\Epic Games\UE_5.8`
  - Éditeur : `E:\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor.exe`
  - Python de l'éditeur : `E:\Epic Games\UE_5.8\Engine\Binaries\ThirdParty\Python3\Win64\python.exe`
- **Git 2.56.1** (+ Git LFS 3.8.1, Git Credential Manager 2.9.1) : `C:\Program Files\Git\cmd\git.exe` (peut être absent du PATH des sessions déjà ouvertes).
  La connexion à GitHub ne peut pas s'afficher depuis le terminal de Claude (non interactif) : la lancer dans un onglet du panneau Terminal.
- **Visual Studio 2026 Community 18.12.1** : `E:\Dev\VisualStudio\2026\Community` (composants partagés `E:\Dev\VisualStudio\Shared`,
  cache `E:\Dev\VisualStudio\Cache`) ; composants listés dans `tools/vs/eudoria.vsconfig`. MSVC 14.52, Windows SDK 10.0.26100 (sur C:).
- **.NET** (installé par VS, sur C: `C:\Program Files\dotnet`) : SDK **11.0.100** ; runtimes **10.0.15** et 11.0.0 (ASP.NET Core inclus).
  Le noyau cible **net10.0 (LTS)** comme décidé (ADR-0001) ; le SDK 11 sait compiler pour net10.0.
- **Docker Desktop** : programme `E:\Dev\Docker\Program`, données WSL `E:\Dev\Docker\Data`, moteur WSL 2.
  Mémoire de WSL limitée à 4 Go par `%USERPROFILE%\.wslconfig` (s'applique aussi à la distribution Ubuntu existante) ; à relever à 32 Go.
- Installateurs officiels conservés dans `E:\Dev\_installers` (supprimables).
- Poste : Windows 11, Ryzen 9 3900X (12 cœurs / 24 threads), **16 Go de RAM** (Epic en recommande 32 ;
  passage à 32 Go prévu, prérequis de l'étape 3), Radeon RX 9060 XT.
- **Tant que le poste a 16 Go de RAM : limiter les compilations parallèles d'Unreal Build Tool** pour éviter
  les plantages par manque de mémoire. Dans `%APPDATA%\Unreal Engine\UnrealBuildTool\BuildConfiguration.xml` :
  `MaxParallelActions` (ex. 6 au lieu de 24 automatiques) et `ParallelExecutor/MemoryPerActionBytes`.
  ⚠️ Epic indique que `MaxParallelActions` s'applique aux exécuteurs Parallel/Hybrid/Local, sans mentionner
  l'Unreal Build Accelerator (UBA, exécuteur local par défaut) : à la première compilation (étape 1), vérifier
  dans le journal d'UBT le nombre de tâches réellement lancées ; si la limite est ignorée, désactiver l'UBA
  local (`bAllowUBALocalExecutor` = false). Retirer ces limites une fois à 32 Go.

## Structure du dépôt
Validée le 2026-11-24 (monorepo). Le détail interne de chaque dossier est défini à l'étape qui le crée.
```
eudoria/
├── docs/                    # vision, roadmap, architecture, ADR (decisions/), progrès, installation (SETUP.md)
├── core/                    # le noyau (C# / ASP.NET Core)
├── shared/
│   └── api/                 # contrats OpenAPI du noyau (public + interne), en YAML
├── unreal/                  # le projet Unreal (client + serveurs de monde)
│   ├── Source/              # code C++ du module principal
│   ├── Plugins/
│   │   ├── EudoriaCore/     # plugin socle (C++)
│   │   └── GameFeatures/    # un plugin par monde (World_<Nom>)
│   ├── Config/              # réglages en texte (.ini), profils de qualité par plateforme
│   ├── Content/             # assets binaires (Git LFS)
│   └── Scripts/             # scripts Python d'automatisation de l'éditeur
└── tools/                   # scripts de dev, environnement local (Docker Compose…)
```

## Langue
- Échanges avec moi et documentation : **français**.
- Code, noms de variables, fonctions et commentaires techniques : **anglais**.
