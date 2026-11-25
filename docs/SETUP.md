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
| Visual Studio 2026 Community 18.12.1 (installé le 2026-11-24) | C++ Unreal, .NET | `E:\Dev\VisualStudio\...` (+ SDK Windows et .NET sur C:) | **C: 7,5 Go · E: 8,2 Go** (mesuré) |
| .NET SDK 11.0.100 + runtimes 10.0.15 / 11.0.0 (via VS) | Noyau (cible net10.0) | `C:\Program Files\dotnet` | inclus ci-dessus |
| Docker Desktop (installé le 2026-11-24) | PostgreSQL en local | `E:\Dev\Docker\Program`, données `E:\Dev\Docker\Data` | **C: 0,6 Go · E: 3,2 Go** (mesuré, avant toute image) |

Bilan mesuré des deux installations : **C: 8,1 Go** (estimation : 9 à 13), **E: 11,4 Go**. C: : 47,9 Go libres après installation.

### Prévu pour l'étape 1 (plan initial, conservé pour référence)

| Outil | Rôle | Taille | Sur C: (forcé) | Sur E: |
|---|---|---|---|---|
| **Visual Studio 2026 Community** (18.0 ou plus) | Compiler le C++ d'Unreal ; aussi le SDK .NET 10 du noyau | 20 à 50 Go pour une installation typique (officiel, fourchette globale) ; **~25 à 35 Go** pour les composants ci-dessous (estimé) | **~8 à 12 Go** (estimé) | le reste |
| **Docker Desktop** | Lancer PostgreSQL en local (ADR-0002) | ~3 Go de programme (estimé) + images et données | < 1 Go (estimé) | Programme et données (options `--installation-dir` et `--wsl-default-data-root`) |
| **Total étape 1** | | **~25 à 35 Go** (estimé) | **~9 à 13 Go** | **~19 à 29 Go** |

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
| Cache de données dérivées Unreal (DDC, Zen) | `%LOCALAPPDATA%\UnrealEngine\Common` | ✅ **Redirigé le 2026-11-24** vers `E:\Dev\UnrealCache\DDC` (Zen dans `...\DDC\Zen`) par la variable d'environnement utilisateur `UE-LocalDataCachePath` ; anciens caches supprimés | Fait |
| Paquets NuGet (.NET) | `%USERPROFILE%\.nuget\packages` | ✅ **Redirigé le 2026-11-24** vers `E:\Dev\NuGet\packages` (variable utilisateur `NUGET_PACKAGES`) | Fait |
| Cache de téléchargement Visual Studio | `C:\ProgramData\Microsoft\VisualStudio\Packages` | — | À l'installation de VS |
| Disque virtuel Docker (WSL 2) | `%LOCALAPPDATA%\Docker` | — | À l'installation de Docker |
| Bibliothèque Fab (assets téléchargés) | `%LOCALAPPDATA%\Temp\FabLibrary` | supprimée le 2026-11-24 (6,1 Go libérés) | Rediriger vers E: dans les réglages du lanceur/Fab avant de retélécharger des assets |

## Lancer le noyau en local (depuis l'étape 1.1)

Prérequis : Docker Desktop **démarré**, et (depuis 1.5) le **certificat HTTPS de développement approuvé** :
le noyau n'écoute qu'en HTTPS (`https://localhost:5443`). Une seule fois, **dans un onglet du panneau Terminal**
(la fenêtre de confirmation de Windows doit pouvoir s'afficher) :

```powershell
dotnet dev-certs https --trust     # puis « Oui » dans l'avertissement de sécurité de Windows
dotnet dev-certs https --check --trust   # vérification : « A trusted certificate was found »
```

Ce certificat ne vaut que pour `localhost` ; sa clé reste sur le PC. Unreal l'accepte aussi (il consulte le magasin
de certificats de Windows). `run-core.ps1` refuse de démarrer s'il n'est pas approuvé. Il **expire le 2027-11-24** :
le renouveler avec `dotnet dev-certs https --clean`, puis `dotnet dev-certs https --trust`. Pour tout retirer :
`dotnet dev-certs https --clean`.

Depuis la racine du dépôt, dans PowerShell :

```powershell
Copy-Item .env.example .env       # une seule fois, puis remplacer le mot de passe dans .env
.\tools\dev\new-signing-key.ps1   # une seule fois (depuis 1.3) : clé de signature des jetons, écrite dans .env sans être affichée
.\tools\dev\start-db.ps1          # démarre PostgreSQL (attend qu'il soit prêt)
.\tools\dev\migrate.ps1           # applique les migrations manquantes (à relancer après chaque nouvelle migration)
.\tools\dev\run-core.ps1          # démarre le noyau sur https://localhost:5443
```

Vérifier : <https://localhost:5443/v1/health/live> et <https://localhost:5443/v1/health/ready> → `{"status":"ok"}`.
Créer un compte (exemple) :

```powershell
Invoke-RestMethod -Method Post -Uri https://localhost:5443/v1/accounts -ContentType 'application/json' -Body '{"email":"ana@example.com","pseudo":"Ana_42","password":"une-phrase-de-passe-longue"}'
```
→ `status : accepted`. Rejouer la même commande → erreur 409 (`pseudo_taken`).

Se connecter, consulter son profil, renouveler, se déconnecter (depuis 1.3) :

```powershell
$session = Invoke-RestMethod -Method Post -Uri https://localhost:5443/v1/auth/login -ContentType 'application/json' -Body '{"email":"ana@example.com","password":"une-phrase-de-passe-longue"}'
Invoke-RestMethod -Uri https://localhost:5443/v1/me -Headers @{ Authorization = "Bearer $($session.accessToken)" }
$session = Invoke-RestMethod -Method Post -Uri https://localhost:5443/v1/auth/refresh -ContentType 'application/json' -Body (@{ refreshToken = $session.refreshToken } | ConvertTo-Json)
Invoke-RestMethod -Method Post -Uri https://localhost:5443/v1/auth/logout -ContentType 'application/json' -Body (@{ refreshToken = $session.refreshToken } | ConvertTo-Json)
```
→ `/me` renvoie `id`, `pseudo`, `createdAt`. Après la déconnexion, `refresh` avec le même jeton → 401.

Arrêter : `Ctrl+C` pour le noyau, `.\tools\dev\start-db.ps1 -Stop` pour la base (les données sont conservées).

Vérifications de « terminé », depuis `core/` :

```powershell
dotnet build Eudoria.Core.slnx                  # 0 avertissement, 0 erreur
dotnet test --solution Eudoria.Core.slnx        # Docker Desktop doit être démarré
dotnet format Eudoria.Core.slnx --verify-no-changes
```

Notes :
- **Smart App Control** (Windows 11) doit être **désactivé** : il bloquait au cas par cas les DLL fraîchement
  compilées (erreur `0x800711C7`). Désactivé par le porteur le 2026-11-24.
- Le cache NuGet est sur E: grâce à la variable utilisateur `NUGET_PACKAGES` ; un terminal ouvert avant sa création
  ne la voit pas (les paquets iraient sur C:) : ouvrir un nouveau terminal.
- Les tests créent leur propre base PostgreSQL jetable (Testcontainers) : ils n'utilisent pas celle de `start-db.ps1`.
- La clé de signature (`IDENTITY_TOKEN_SIGNING_KEY`) est secrète : ne jamais la copier ailleurs que dans `.env`.
  La remplacer (`new-signing-key.ps1 -Force`) oblige tout le monde à se reconnecter. Sans elle, le noyau refuse de démarrer.
- `new-signing-key.ps1` compile un petit programme C# à la volée (`dotnet run --file`) ; le SDK range ce résultat
  (environ 9 Mo) dans `%TEMP%\dotnet\runfile` sur C: (supprimable).
- `run-core.ps1` vise `127.0.0.1` et non `localhost` : `localhost` se résout d'abord en IPv6 (`::1`), où la base
  n'écoute pas, et Windows met environ 2 s à abandonner, soit tout le délai du contrôle de santé.

## Plan d'installation — Visual Studio 2026 et Docker Desktop (en attente de validation)

### Arborescence proposée sur E:
```
E:\Dev\
├── VisualStudio\
│   ├── 2026\Community\   # l'IDE
│   ├── Shared\           # composants partagés, outils, SDK déplaçables
│   └── Cache\            # cache de téléchargement
└── Docker\
    ├── Program\          # programme Docker Desktop
    └── Data\             # disque virtuel WSL de Docker (images, conteneurs, données PostgreSQL)
```

### Visual Studio 2026 Community

**Méthode :** la liste des composants est enregistrée dans un fichier texte versionné
`tools/vs/eudoria.vsconfig` (créé au moment de l'installation), puis l'installateur officiel est lancé
en ligne de commande avec les emplacements sur E: :
```
vs_community.exe --installPath "E:\Dev\VisualStudio\2026\Community"
                 --path shared="E:\Dev\VisualStudio\Shared"
                 --path cache="E:\Dev\VisualStudio\Cache"
                 --config "<dépôt>\tools\vs\eudoria.vsconfig" --passive --norestart
```
(Programme d'installation officiel : https://aka.ms/vs/stable/vs_community.exe — droits administrateur
requis. Les emplacements `shared` et `cache` ne peuvent être choisis **qu'à la première installation**.)

**Composants** (identifiants officiels VS 2026, [liste Microsoft](https://learn.microsoft.com/en-us/visualstudio/install/workload-component-id-vs-community)) :

| Identifiant | Nom affiché | Pourquoi |
|---|---|---|
| `Microsoft.VisualStudio.Workload.NativeDesktop` | Développement Desktop en C++ | Exigé par Epic |
| `Microsoft.VisualStudio.Workload.NativeGame` | Développement de jeux en C++ | Exigé par Epic |
| `Microsoft.VisualStudio.Workload.ManagedDesktop` | Développement .NET Desktop | Exigé par Epic ; apporte le **SDK .NET 10** du noyau (ADR-0001) |
| `Microsoft.VisualStudio.Component.VC.Tools.x86.x64` | MSVC Build Tools x64/x86 (dernière version) | Compilateur C++ |
| `Microsoft.VisualStudio.Component.Windows11SDK.26100` | Windows 11 SDK (10.0.26100) | Version recommandée par Epic |
| `Microsoft.VisualStudio.Component.VC.DiagnosticTools` | Outils de profilage C++ | Listé par Epic |
| `Microsoft.VisualStudio.Component.VC.ASAN` | MSVC AddressSanitizer | Listé par Epic (détection d'erreurs mémoire) |
| `Component.Unreal.Ide` | Visual Studio Tools for Unreal Engine | Aide à la navigation dans le code Unreal |

**Volontairement exclus :**
- `Component.Unreal` (« Unreal Engine installer ») : installe le lanceur Epic, **déjà présent**.
- *.NET Multi-platform App UI development* (MAUI) : listé par Epic, mais très volumineux et sans usage prévu pour nous ; à ajouter seulement si une erreur de compilation le réclame.
- `Component.Unreal.Debugger` (débogage des Blueprints) : nos Blueprints ne contiennent pas de logique.

**Place estimée** (l'installateur affiche la répartition exacte avant de lancer ; à vérifier à ce moment-là) :

| Où | Contenu | Estimation |
|---|---|---|
| **E:** | IDE, MSVC, outils, cache | ~15 à 25 Go |
| **C: (forcé)** | Windows SDK (`C:\Program Files (x86)\Windows Kits`), SDK .NET (`C:\Program Files\dotnet`), runtimes C++/.NET, packs de ciblage .NET Framework, WebView2, programme d'installation de VS | **~8 à 12 Go** |
| C: temporaire | Zones de travail pendant l'installation | quelques Go, libérés ensuite |

### Docker Desktop

**Prérequis vérifiés sur ce PC (2026-11-24) :**
- Windows 11 Home : supporté avec le moteur WSL 2 (conteneurs Linux uniquement, ce qui nous suffit).
- WSL **3.0.5** déjà installé (distribution Ubuntu existante, 3,5 Go sur C:) ; virtualisation activée.

**Méthode :** installateur officiel en mode « tous les utilisateurs » (droits administrateur), avec les options
documentées pour placer programme et données sur E: ([installation Windows](https://docs.docker.com/desktop/setup/install/windows-install/)) :
```
"Docker Desktop Installer.exe" install --backend=wsl-2
     --installation-dir="E:\Dev\Docker\Program"
     --wsl-default-data-root="E:\Dev\Docker\Data"
```
La licence Docker Desktop (gratuite pour un usage personnel et les petites structures) est à **accepter par toi** au premier lancement.

**Réglage important avec 16 Go de RAM :** WSL 2 peut réserver jusqu'à la moitié de la mémoire du PC. Pour
qu'Unreal garde assez de mémoire, créer `%USERPROFILE%\.wslconfig` avec une limite (ex. `memory=4GB`,
largement suffisant pour PostgreSQL). À relever une fois à 32 Go.

**Place estimée :**

| Où | Contenu | Estimation |
|---|---|---|
| **E:** | Programme (~3 Go) + données (image PostgreSQL ~0,5 Go, grossit avec l'usage) | ~4 Go au départ |
| **C:** | Configuration utilisateur, journaux, intégration WSL | **< 1 Go** |

### Ordre proposé
1. Visual Studio 2026 (redémarrage éventuel).
2. Docker Desktop, puis `.wslconfig`.
3. Vérifications : `dotnet --version` (10.x), compilation d'un projet C++ Unreal vide (dans le cadre de l'étape 1), `docker run hello-world`.
4. Mise à jour de `CLAUDE.md` (section Environnement) avec les chemins réels.

## Bilan pour le disque C:

| | Go |
|---|---|
| Libre aujourd'hui (après suppression de la bibliothèque Fab) | ~56 |
| Visual Studio 2026 (part forcée) | − 8 à 12 (estimé) |
| Docker Desktop (part sur C:) | − 1 (estimé) |
| Marge pour Windows, mises à jour, zones de travail des installateurs | ≥ 15 conseillés |
| **Reste après ces installations** | **~43 à 47 Go** — confortable **si** les caches ci-dessus sont redirigés vers E: |

Pour gagner de la place sur C: sans rien désinstaller d'utile : voir les traces de l'ancienne installation
d'Unreal et le cache Fab listés dans `docs/PROGRESS.md` (à ta décision).
