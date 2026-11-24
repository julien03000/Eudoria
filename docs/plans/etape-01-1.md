# Étape 1.1 — Squelette du noyau + environnement local + route « santé »

- **Statut :** ✅ Réalisé le 2026-11-24 (OK du porteur le 2026-11-24) — en attente de validation de la 1.1
- **Date :** 2026-11-24
- **Branche :** `etape-01-identite`
- **Périmètre :** sous-étape 1.1 **uniquement**. Aucun compte, aucune table, aucun Entity Framework : tout ça est la 1.2.

## 1. Objectif

Avoir un noyau ASP.NET Core qui **démarre**, qui **se connecte à PostgreSQL** lancé par Docker, et qui
expose deux routes « santé » décrites d'abord dans le contrat OpenAPI. Le tout testé automatiquement et
lançable en quelques commandes.

Résultat visible à la fin :
```
GET http://localhost:5080/v1/health/live   → 200 {"status":"ok"}
GET http://localhost:5080/v1/health/ready  → 200 {"status":"ok"}          (base joignable)
                                            → 503 {"status":"unavailable"} (base arrêtée)
```

Pourquoi deux routes :
- **live** (« vivant ») : le programme tourne. Ne touche pas la base. Sert à savoir s'il faut le redémarrer.
- **ready** (« prêt ») : le noyau peut vraiment servir (la base répond). Sert à savoir s'il faut lui envoyer du trafic.
  La réponse ne donne **aucun détail interne** (pas de message d'erreur, pas de nom de serveur) : sécurité.

## 2. Recommandation sur le SDK .NET

**Recommandation : utiliser le SDK 11 déjà installé, épinglé par un `global.json`, en ciblant `net10.0` (LTS).**

| | SDK 11 épinglé (recommandé) | SDK 10 épinglé |
|---|---|---|
| Installation | **Rien à installer** | Installer le SDK 10 (~0,3 Go sur C:, forcé) |
| Programme produit | `net10.0`, exécuté par le runtime **10.0.15** (LTS) — identique | identique |
| Conforme à l'ADR-0001 | Oui : l'ADR fixe le **runtime** (.NET 10 LTS), pas l'outil de compilation | Oui |
| Durée de support de l'outil | SDK 11 = version « STS », supportée jusqu'à fin 2028 environ | SDK 10 = LTS, jusqu'à fin 2028 aussi |
| Risque | Nouveaux avertissements d'analyse du SDK 11 | Deux SDK à garder à jour |

Ce qu'on fait concrètement :
- `core/global.json` : `"version": "11.0.100"`, `"rollForward": "latestFeature"` → on accepte les correctifs 11.0.x,
  mais **jamais** un SDK 12 qui changerait le comportement sans qu'on le décide.
- `TargetFramework` = `net10.0` partout (fichier commun `Directory.Build.props`).
- `AnalysisLevel` = `10.0` : on fige les règles d'analyse du code sur celles de .NET 10, pour que le SDK 11
  n'ajoute pas de nouvelles erreurs « surprise ».
- Les paquets ASP.NET Core / tests sont pris en version **10.0.x** (alignés sur le runtime).

Si un jour le SDK 11 pose problème (outil incompatible, bug), on installe le SDK 10 et on change une ligne du `global.json`.

## 3. Fichiers créés / modifiés

```
Eudoria/
├── .editorconfig                          # NOUVEAU — règles de style C# (lues par dotnet format et VS)
├── .gitignore                             # MODIFIÉ — voir § 3.1
├── .env.example                           # NOUVEAU — modèle des variables (sans vrai secret)
├── shared/api/core-v1.yaml                # NOUVEAU — contrat OpenAPI public v1 : les 2 routes santé
├── tools/dev/
│   ├── docker-compose.yml                 # NOUVEAU — PostgreSQL 18
│   ├── start-db.ps1                       # NOUVEAU — lance/arrête la base (lit .env)
│   └── run-core.ps1                       # NOUVEAU — charge .env puis lance le noyau
├── core/
│   ├── global.json                        # NOUVEAU — épinglage du SDK (§ 2)
│   ├── Eudoria.Core.slnx                  # NOUVEAU — solution (.slnx = format texte récent)
│   ├── Directory.Build.props              # NOUVEAU — net10.0, nullable, avertissements = erreurs
│   ├── Directory.Packages.props           # NOUVEAU — versions des paquets centralisées
│   ├── src/Eudoria.Core.Api/
│   │   ├── Eudoria.Core.Api.csproj
│   │   ├── Program.cs                     # démarrage, lecture de la configuration, routes
│   │   ├── Health/HealthEndpoints.cs      # routes /v1/health/live et /v1/health/ready
│   │   ├── Health/DatabaseHealthCheck.cs  # « SELECT 1 » sur PostgreSQL, avec délai max
│   │   └── appsettings.json               # réglages non secrets (journalisation)
│   └── tests/Eudoria.Core.Api.Tests/
│       ├── Eudoria.Core.Api.Tests.csproj
│       ├── HealthEndpointsTests.cs
│       ├── ContractTests.cs
│       └── PostgresFixture.cs             # base PostgreSQL jetable pour les tests
└── docs/  ARCHITECTURE.md, SETUP.md, PROGRESS.md (mis à jour)
```

**Monolithe modulaire :** en 1.1 il n'y a qu'un projet « hôte » (`Eudoria.Core.Api`). Le premier vrai module
(`Eudoria.Core.Identity`, projet séparé) arrive en 1.2. Je ne crée pas de dossiers vides « au cas où ».

### 3.1 Correction nécessaire du `.gitignore`
Le `.gitignore` actuel ignore `*.sln` et `*.slnx` **partout** (règle prévue pour Unreal, qui les régénère).
Sans correction, la solution du noyau ne serait jamais commitée. Je propose :
- limiter ces deux règles à `unreal/` (`/unreal/*.sln`, `/unreal/*.slnx`) ;
- ajouter les règles .NET : `bin/`, `obj/`, `TestResults/`, `*.trx`, `coverage*/` (prévu par l'ADR-0001).

## 4. Approche, point par point

### 4.1 Contrat d'abord
1. J'écris `shared/api/core-v1.yaml` (OpenAPI 3.1) : `info.version: 1.0.0`, serveur `/v1`, les deux routes,
   le schéma `HealthStatus { status: "ok" | "unavailable" }`, les codes 200 / 503.
2. Ensuite seulement le code C#, en **Minimal APIs** (style explicite, une fonction par route).
3. Un **test de contrat** vérifie que le noyau respecte le YAML (§ 5).

### 4.2 Configuration et secrets
- Un seul fichier `.env` à la racine (jamais commité, déjà ignoré), copié depuis `.env.example` :
  ```
  POSTGRES_USER=eudoria
  POSTGRES_PASSWORD=change-me      # à remplacer dans ton .env
  POSTGRES_DB=eudoria
  POSTGRES_PORT=5432
  CORE_HTTP_PORT=5080
  ```
- Docker Compose lit `.env` tout seul. Pour le noyau, `run-core.ps1` lit `.env` et construit la variable
  `ConnectionStrings__Core` (convention .NET) **en mémoire du processus uniquement** — rien n'est écrit sur disque.
  Pas de librairie « dotenv » : un petit script PowerShell suffit.
- Au démarrage, le noyau **refuse de démarrer** avec un message clair si la chaîne de connexion manque
  (mieux qu'une erreur obscure plus tard).
- Mot de passe jamais écrit dans les journaux (la chaîne de connexion n'est jamais journalisée).

### 4.3 PostgreSQL local
- Image officielle `postgres:18.x` (version **exacte** épinglée, je vérifierai la dernière 18.x au moment de coder).
- Port ouvert sur `127.0.0.1` uniquement (pas accessible depuis le réseau local).
- Données dans un volume Docker nommé → physiquement dans `E:\Dev\Docker\Data` (règle « tout sur E: » respectée).
- Contrôle de santé du conteneur (`pg_isready`).

### 4.4 Le noyau
- HTTP simple sur `http://localhost:5080` **en développement uniquement** (HTTPS obligatoire hors du poste, comme
  le prévoit la roadmap ; le certificat de développement sera traité quand le client Unreal arrivera en 1.5,
  car l'installer modifie le magasin de certificats de Windows et demande ton accord).
- Vérification de la base : ouverture d'une connexion + `SELECT 1`, délai maximum de 2 s, réponse 503 sinon.
- Journaux structurés en console (fournis par ASP.NET Core).

### 4.5 Lancer en local (procédure cible, documentée dans `docs/SETUP.md`)
```powershell
Copy-Item .env.example .env            # une seule fois, puis changer le mot de passe
.\tools\dev\start-db.ps1               # démarre PostgreSQL
.\tools\dev\run-core.ps1               # démarre le noyau
```

## 5. Tests

| Test | Type | Vérifie |
|---|---|---|
| `live` renvoie 200 `{"status":"ok"}` | intégration | le noyau répond, sans dépendre de la base |
| `ready` renvoie 200 avec une vraie base | intégration | la connexion PostgreSQL fonctionne |
| `ready` renvoie 503 si la base est injoignable | intégration | panne détectée, **aucun détail interne** dans la réponse |
| démarrage refusé sans chaîne de connexion | intégration | message clair, pas de démarrage à moitié |
| route inconnue → 404 | intégration | pas de route parasite |
| conformité au contrat | contrat | chaque route du YAML existe, codes et schémas de réponse respectés |

- Les tests d'intégration lancent le vrai noyau en mémoire (`WebApplicationFactory`, outil officiel) et une
  **vraie base PostgreSQL jetable** (ADR-0002), créée puis détruite automatiquement par les tests.
- Commandes de « terminé » : `dotnet build` (0 avertissement), `dotnet test`, `dotnet format --verify-no-changes`.

## 6. Dépendances proposées (à valider)

| Paquet | Pourquoi | Éditeur |
|---|---|---|
| `Npgsql` 10.x | Pilote PostgreSQL pour .NET (indispensable pour parler à la base) | projet Npgsql, référence de facto, utilisé par EF Core |
| `xunit.v3` + `xunit.runner.visualstudio` | Framework de test (décidé par l'ADR-0001) | xUnit |
| `Microsoft.NET.Test.Sdk` | Exécution des tests par `dotnet test` | Microsoft |
| `Microsoft.AspNetCore.Mvc.Testing` 10.0.x | Lancer le noyau en mémoire dans les tests | Microsoft |
| `Testcontainers.PostgreSql` | Crée/détruit une base PostgreSQL jetable dans Docker pour chaque série de tests | projet Testcontainers (open source, très répandu) |
| `Microsoft.OpenApi` (+ lecteur YAML) | Lire `core-v1.yaml` dans le test de contrat | Microsoft |

Les « health checks » d'ASP.NET Core sont **inclus dans le framework** : pas de paquet en plus.
Pour la validation des schémas JSON dans le test de contrat, je commencerai sans librairie (le schéma
`HealthStatus` est minuscule) ; une librairie de validation JSON Schema sera proposée en 1.2 si les schémas grossissent.

## 7. Points qui demandent ton accord

1. **SDK** : SDK 11 épinglé + cible `net10.0` (§ 2) — ou préfères-tu installer le SDK 10 ?
2. **Cache des paquets NuGet sur E:** : par défaut, .NET télécharge les paquets dans `%USERPROFILE%\.nuget\packages` (sur C:)
   (quelques centaines de Mo au début, grossit ensuite). Conformément à la règle « tout sur E: », je propose
   la variable d'environnement utilisateur `NUGET_PACKAGES = E:\Dev\NuGet\packages`, à poser **avant** le premier
   `dotnet restore`.
3. **Testcontainers** télécharge l'image PostgreSQL et un petit conteneur de nettoyage (« Ryuk ») dans Docker
   (donc sur E:). Docker Desktop doit être **démarré** pour lancer les tests.
4. **Dépendances** du § 6.

## 8. Risques

| Risque | Parade |
|---|---|
| Docker Desktop arrêté → tests d'intégration en échec | Message d'erreur explicite dans la fixture ; noté dans `SETUP.md` |
| Mémoire WSL limitée à 4 Go | PostgreSQL seul consomme ~100 Mo : suffisant |
| Port 5432 déjà utilisé sur le PC | Port configurable dans `.env` |
| SDK 11 plus récent que la cible | `global.json` + `AnalysisLevel` figé (§ 2) |
| Le nouveau format de solution `.slnx` | Supporté par le SDK 11 et VS 2026 ; repli possible sur `.sln` |

## 9. Commits prévus (petits)

1. `chore(repo): add .NET rules to .gitignore and scope solution rules to unreal/`
2. `docs(api): add core public v1 contract with health routes`
3. `chore(tools): add local PostgreSQL via Docker Compose and .env.example`
4. `feat(core): add ASP.NET Core host skeleton with health endpoints`
5. `test(core): add integration and contract tests for health endpoints`
6. `docs(architecture): describe core skeleton and local setup` (+ `SETUP.md`, `PROGRESS.md`)

Puis envoi de la branche sur GitHub. La sous-étape 1.2 ne commence qu'après ta validation de la 1.1.

## 10. Écarts constatés pendant la réalisation

| Prévu | Réalisé | Pourquoi |
|---|---|---|
| `core/global.json` | `global.json` **à la racine** | `dotnet` cherche ce fichier à partir du dossier où on lance la commande ; `run-core.ps1` est lancé depuis la racine |
| `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio` | **retirés** ; `global.json` active `Microsoft.Testing.Platform` | Le SDK .NET 10+ refuse l'ancien lanceur de tests (VSTest) avec xUnit v3 ; la nouvelle plateforme est intégrée à xUnit v3 |
| `Host=localhost` | `Host=127.0.0.1` dans `run-core.ps1` | `localhost` → IPv6 `::1` d'abord, ~2 s perdues (voir `docs/SETUP.md`) ; trouvé au premier lancement réel |
| `.editorconfig` en CRLF | LF, CRLF pour les scripts | Aligné sur `.gitattributes` (règle existante du dépôt) |
| Exceptions CA1707 / CA1711 | Tirets bas autorisés dans les **noms de tests** uniquement | Convention de lisibilité des tests ; règle maintenue dans le code de production |
| — | Fichier `tools/dev/DotEnv.ps1` en plus | Lecture de `.env` partagée par les deux scripts au lieu d'être recopiée |

Versions retenues : PostgreSQL **18.7**, Npgsql 10.0.4, Microsoft.AspNetCore.Mvc.Testing 10.0.15,
xunit.v3 4.0.3, Testcontainers.PostgreSql 4.17.0, Microsoft.OpenApi (+ YamlReader) 3.11.1.
