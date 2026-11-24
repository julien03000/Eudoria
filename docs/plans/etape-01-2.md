# Étape 1.2 — Modèle de compte + migrations + inscription

- **Statut :** ✅ Plan validé par le porteur le 2026-11-24 (choix au § 9.1) — réalisation en cours
- **Date :** 2026-11-24
- **Branche :** `etape-01-identite`
- **Périmètre :** sous-étape 1.2 **uniquement** : créer un compte. Pas de connexion, pas de jeton, pas de
  limitation des tentatives (1.3 et 1.4).

## 1. Objectif

Une personne peut **créer un compte EUDORIA** (e-mail + pseudo public + mot de passe) par l'API du noyau.
Le compte est enregistré dans PostgreSQL, dans une table créée par une **migration versionnée** ; le mot de
passe n'est **jamais** stocké ni journalisé en clair.

Résultat visible à la fin :
```
POST http://localhost:5080/v1/accounts
     {"email":"ana@example.com","pseudo":"Ana_42","password":"un-long-mot-de-passe"}
  → 202 Accepted                         (demande acceptée — voir § 9, décision 1)
  → 400 application/problem+json         (champ invalide : e-mail mal formé, pseudo interdit, mot de passe trop court…)
  → 409 application/problem+json         (pseudo déjà pris)
```

## 2. Ce qui est dans la 1.2 et ce qui n'y est pas

| Dans la 1.2 | Plus tard |
|---|---|
| Table des comptes + migration | Connexion, jetons, déconnexion (1.3) |
| Route d'inscription, décrite d'abord dans `core-v1.yaml` | Limitation des tentatives / anti-force brute (1.4) |
| Hachage Argon2id du mot de passe | Liste de mots de passe courants interdits (1.4) |
| Validation des champs **nécessaires à l'inscription** (format, longueur) | Consultation du profil, changement de mot de passe (1.3/1.4) |
| Unicité du pseudo et de l'e-mail (garantie par la base) | Vérification de l'e-mail par envoi réel (exclu de l'étape 1) |

La roadmap range « validations » en 1.4, mais on ne peut pas créer un compte sans valider au minimum ses
champs côté serveur : la 1.2 pose donc les règles de base, la 1.4 ajoute les protections (limitation, mots de
passe courants).

## 3. Fichiers créés / modifiés

```
Eudoria/
├── .config/dotnet-tools.json                     # NOUVEAU — outil « dotnet-ef » épinglé (local au dépôt)
├── shared/api/core-v1.yaml                       # MODIFIÉ — POST /accounts + schémas (version 1.1.0)
├── tools/dev/migrate.ps1                         # NOUVEAU — applique les migrations à la base locale (lit .env)
├── core/
│   ├── Eudoria.Core.slnx                         # MODIFIÉ — ajoute les 2 nouveaux projets
│   ├── Directory.Packages.props                  # MODIFIÉ — nouvelles versions (§ 8)
│   ├── src/Eudoria.Core.Identity/                # NOUVEAU — premier module du noyau
│   │   ├── Eudoria.Core.Identity.csproj
│   │   ├── IdentityModule.cs                     # AddIdentityModule() / MapIdentityEndpoints() : seule porte d'entrée
│   │   ├── Accounts/Account.cs                   # le compte (entité)
│   │   ├── Accounts/AccountRules.cs              # règles e-mail / pseudo / mot de passe + normalisation
│   │   ├── Registration/RegistrationEndpoint.cs  # POST /accounts
│   │   ├── Registration/RegistrationService.cs   # logique d'inscription (validation, hachage, enregistrement)
│   │   ├── Passwords/IPasswordHasher.cs          # interface (le reste du module ne connaît pas Argon2)
│   │   ├── Passwords/Argon2idPasswordHasher.cs   # implémentation
│   │   └── Persistence/
│   │       ├── IdentityDbContext.cs              # EF Core, schéma PostgreSQL « identity »
│   │       ├── AccountConfiguration.cs           # noms de tables/colonnes, index uniques
│   │       ├── DesignTimeDbContextFactory.cs     # pour l'outil dotnet-ef
│   │       └── Migrations/                       # GÉNÉRÉ — première migration « CreateAccounts »
│   ├── src/Eudoria.Core.Api/Program.cs           # MODIFIÉ — branche le module (2 lignes)
│   ├── tests/Eudoria.Core.Identity.Tests/        # NOUVEAU — tests unitaires du module (règles, hachage)
│   └── tests/Eudoria.Core.Api.Tests/             # MODIFIÉ — intégration + contrat de l'inscription
└── docs/  ARCHITECTURE.md, SETUP.md, PROGRESS.md (mis à jour)
```

**Monolithe modulaire :** `Eudoria.Core.Identity` est un projet séparé. L'hôte `Eudoria.Core.Api` ne fait que
l'appeler (`AddIdentityModule`, `MapIdentityEndpoints`) ; tout le reste du module est `internal`. Les futurs
modules (Avatars, Inventaire…) suivront le même modèle et **ne pourront pas** toucher aux tables du module
Identité directement.

## 4. Le modèle de compte

Table `identity.accounts` (schéma PostgreSQL **propre au module** : chaque module aura le sien) :

| Colonne | Type | Contraintes | Rôle |
|---|---|---|---|
| `id` | `uuid` | clé primaire | **Identifiant interne stable**, utilisé par tous les autres systèmes. Généré par le noyau (UUID v7 : trié par date, bon pour les index) |
| `email` | `varchar(254)` | **unique** | E-mail **normalisé** (espaces retirés, minuscules) |
| `pseudo` | `varchar(20)` | — | Pseudo public **tel que saisi** (ex. `Ana_42`), affiché dans EUDORIA |
| `pseudo_normalized` | `varchar(20)` | **unique** | Pseudo en minuscules (`ana_42`) : empêche `Ana_42` et `ana_42` de coexister |
| `password_hash` | `text` | — | Empreinte Argon2id au format standard `$argon2id$v=19$m=…,t=…,p=…$sel$empreinte` |
| `created_at` | `timestamptz` | — | Date de création (UTC), fournie par le noyau |

- Noms en `snake_case` (convention PostgreSQL), écrits explicitement dans `AccountConfiguration.cs` —
  pas de librairie de nommage en plus.
- L'unicité est garantie **par la base** (index uniques), pas seulement par le code : deux inscriptions
  simultanées avec le même pseudo ne peuvent pas passer toutes les deux.
- Le format d'empreinte inclut ses paramètres : on pourra **durcir Argon2id plus tard** sans casser les
  comptes existants (ré-hachage à la connexion, en 1.3).

## 5. Les migrations

- **Entity Framework Core** (décidé par l'ADR-0001), fournisseur PostgreSQL de Npgsql.
- Première migration `CreateAccounts`, générée par `dotnet ef migrations add`, **relue et commitée** (fichier C#
  lisible + je montre le SQL produit dans le compte rendu).
- La table d'historique des migrations vit aussi dans le schéma `identity` (`identity.__ef_migrations_history`) :
  chaque module gère ses migrations indépendamment.
- **Application des migrations : par une commande explicite, pas au démarrage du noyau** (voir § 9, décision 4) :
  ```powershell
  .\tools\dev\migrate.ps1      # lit .env, applique les migrations manquantes à la base locale
  ```
  Comme `run-core.ps1`, le script construit la chaîne de connexion **en mémoire du processus** (jamais sur le
  disque, jamais en argument de commande visible).
- L'outil `dotnet-ef` est installé **localement au dépôt** (`.config/dotnet-tools.json`, version épinglée) :
  `dotnet tool restore` suffit, rien d'installé globalement sur la machine.
- Dans les tests, la base jetable reçoit les migrations automatiquement avant les tests.

## 6. L'inscription, point par point

### 6.1 Contrat d'abord
`core-v1.yaml` passe en `version: 1.1.0` (ajout compatible, pas de rupture) :
- `POST /accounts`, corps `RegisterAccountRequest { email, pseudo, password }` (tous obligatoires,
  `additionalProperties: false`) ;
- réponses 202 / 400 / 409 ; erreurs au format standard **Problem Details** (RFC 9457, `application/problem+json`),
  avec un **code machine** par erreur (`email_invalid`, `pseudo_invalid`, `pseudo_taken`, `password_too_short`…) que
  le client Unreal traduira en messages (et en langues) lui-même ;
- les règles (longueurs, caractères) sont écrites dans le YAML **et** testées contre le code.

### 6.2 Règles de validation (côté serveur)
| Champ | Règle proposée |
|---|---|
| E-mail | Espaces de début/fin retirés, mis en minuscules ; 254 caractères max ; exactement un `@`, une partie avant et un domaine contenant un point après ; aucun espace ni caractère de contrôle. (Pas de vérification « l'adresse existe » : il faudrait un envoi réel, exclu.) |
| Pseudo | **3 à 20 caractères**, lettres **A–Z / a–z**, chiffres, `_` et `-` ; commence par une lettre ; unicité sans tenir compte des majuscules |
| Mot de passe | **Normalisé en Unicode NFKC** (recommandation NIST SP 800-63B-4) **avant** tout contrôle et avant le hachage, pour qu'un même mot de passe fonctionne quel que soit le clavier/système (PC, Mac, mobile, VR) ; puis **12 à 128 caractères**, comptés en caractères Unicode (points de code) ; tous les caractères autorisés (espaces, accents, émojis) ; **aucune** règle du type « une majuscule + un symbole » (le NIST recommande la longueur, pas la complexité imposée) ; le maximum évite qu'un mot de passe géant coûte cher à hacher |

Toutes les erreurs de champ sont renvoyées **ensemble** (pas une par une), pour un formulaire agréable.

### 6.3 Hachage du mot de passe : Argon2id
- Algorithme demandé par la roadmap, recommandé en premier par l'OWASP.
- Paramètres de départ (minimum OWASP) : **19 Mio de mémoire, 2 passes, 1 fil** ; sel aléatoire de 16 octets ;
  empreinte de 32 octets. Je mesurerai le temps réel sur ton poste (cible : ~50 à 200 ms par hachage) et
  l'écrirai dans le compte rendu.
- Pourquoi **pas** « ASP.NET Core Identity » (le système de comptes tout fait de Microsoft) : il hache en
  PBKDF2 (ni Argon2id ni bcrypt, contrairement à la roadmap), impose son propre schéma de tables (rôles,
  revendications, etc., inutiles ici) et est pensé pour des sites web à cookies, pas pour une API à jetons
  consommée par Unreal et par des serveurs de monde. Un module à nous, petit et testé, colle mieux à l'architecture.

### 6.4 Ne pas révéler si un e-mail existe (exigence de la roadmap)
Si l'e-mail est **déjà utilisé**, le noyau répond **exactement comme pour une inscription réussie** (202, même
corps, **même durée** : le mot de passe est haché quand même, pour qu'on ne puisse pas deviner à la montre).
Aucun compte n'est créé, et l'événement est journalisé **sans l'e-mail**.
- Le **pseudo**, lui, est public par nature : « pseudo déjà pris » (409) ne révèle rien de secret.
- Conséquence à connaître : une personne qui se réinscrit avec son propre e-mail reçoit « demande acceptée »
  mais n'aura pas de nouveau compte. Le jour où l'envoi d'e-mails existera, on lui écrira « vous avez déjà un
  compte ». C'est le comportement standard des grands services. → **décision 1 du § 9**.

### 6.5 Sécurité et journaux
- Le mot de passe et l'e-mail n'apparaissent **jamais** dans les journaux (seul l'identifiant du compte créé).
- Corps de requête limité (quelques Ko) sur cette route.
- La collision « deux inscriptions au même moment » est rattrapée proprement : l'erreur d'unicité de PostgreSQL
  est traduite en 409 (pseudo) ou en 202 (e-mail), jamais en erreur 500.
- Rien n'est renvoyé du compte dans la réponse 202 (pas d'identifiant) : le client se connectera ensuite (1.3).

## 7. Tests

| Test | Type | Vérifie |
|---|---|---|
| règles e-mail / pseudo / mot de passe (cas valides, limites, caractères interdits) | unitaire | les règles du § 6.2, y compris aux bornes (2/3/20/21 caractères…) |
| normalisation e-mail et pseudo | unitaire | `  Ana@Example.COM ` → `ana@example.com` |
| Argon2id : vérifie le bon mot de passe, refuse un mauvais, sel différent à chaque fois, format PHC | unitaire | le hachage |
| NFKC : `é` composé (U+00E9) et `e` + accent combinant (U+0065 U+0301), ou chiffre pleine largeur `１` et `1`, donnent le même mot de passe ; la longueur est comptée après normalisation | unitaire | la normalisation Unicode |
| inscription valide → 202, ligne en base, mot de passe **absent** de la base | intégration | le cas nominal |
| pseudo déjà pris (même casse et casse différente) → 409 | intégration | unicité du pseudo |
| e-mail déjà pris → 202 identique, **aucun** second compte | intégration | non-révélation de l'e-mail |
| plusieurs erreurs à la fois → 400 avec tous les codes | intégration | validation |
| champ manquant, JSON invalide, champ inconnu → 400 | intégration | robustesse |
| 10 inscriptions simultanées du même pseudo → 1 seule réussite, aucun 500 | intégration | unicité garantie par la base |
| mot de passe et e-mail absents des journaux | intégration | journaux capturés pendant le test |
| le modèle EF Core n'a aucune modification sans migration | intégration | on n'oublie jamais de générer une migration |
| conformité au contrat (routes, codes, schémas, y compris 400/409) | contrat | le YAML et le code restent alignés |

Commandes de « terminé » : `dotnet build` (0 avertissement), `dotnet test`, `dotnet format --verify-no-changes`,
plus un **lancement réel** (`migrate.ps1`, `run-core.ps1`, inscription par requête HTTP, vérification de la table
dans PostgreSQL).

Le validateur de schéma maison des tests de contrat (`SchemaValidator.cs`) sera étendu aux quelques mots-clés
nécessaires (objets imbriqués, longueurs, motifs) et aura ses propres tests — voir § 9, décision 5.

## 8. Dépendances proposées (à valider)

| Paquet | Où | Pourquoi | Éditeur |
|---|---|---|---|
| `Microsoft.EntityFrameworkCore` 10.0.x | Identity | ORM + migrations (ADR-0001) | Microsoft |
| `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x | Identity | EF Core pour PostgreSQL (ADR-0002) | projet Npgsql (même éditeur que le pilote déjà utilisé) |
| `Microsoft.EntityFrameworkCore.Design` 10.0.x | Identity, **développement seulement** | nécessaire pour générer les migrations ; non livré avec le noyau | Microsoft |
| outil `dotnet-ef` 10.0.x | dépôt (outil local) | commandes `migrations add` / `database update` | Microsoft |
| **librairie Argon2id** (une seule) | Identity | .NET ne fournit pas Argon2 | voir § 9, décision 2 |

Les versions exactes seront vérifiées (dernière 10.0.x stable) au moment de coder et notées au § 12.

## 9. Points qui demandent ton accord

1. **E-mail déjà utilisé** (§ 6.4) — **recommandé : réponse identique à une réussite (202)**, conforme à l'exigence
   « ne pas révéler si un e-mail existe ». Alternative : répondre clairement « e-mail déjà utilisé » (409),
   plus simple pour l'utilisateur mais qui permet à n'importe qui de tester si une adresse a un compte.
2. **Librairie Argon2id** — ~~recommandé : `Konscious.Security.Cryptography.Argon2`~~ (choix du porteur : NSec, voir § 9.1) (100 % .NET, aucune
   bibliothèque native à embarquer, la plus utilisée en .NET). Alternative : `NSec.Cryptography` (s'appuie sur
   libsodium, référence en cryptographie, mais ajoute un composant natif). Je vérifierai avant de coder
   l'activité de maintenance et la licence de celle retenue, et je te le dirai si quelque chose cloche.
3. **Règles du pseudo** — **recommandé : 3–20 caractères, ASCII (A–Z, a–z, 0–9, `_`, `-`), commence par une lettre.**
   Autoriser les accents/autres alphabets serait plus ouvert (VISION : liberté d'identité) mais ouvre la porte aux
   pseudos « sosies » (`Аna` en cyrillique ≈ `Ana`) : à traiter plus tard avec une vraie protection si tu le souhaites.
4. **Application des migrations** — **recommandé : commande explicite `migrate.ps1`**, jamais au démarrage du
   noyau (en production, plusieurs instances démarrant en même temps se disputeraient la migration, et une migration
   doit être un geste volontaire). Alternative : migration automatique au démarrage en développement uniquement.
5. **Validation de schéma dans les tests de contrat** — **recommandé : étendre le petit validateur maison** (aucune
   dépendance, les schémas restent modestes). Alternative : une librairie JSON Schema (à choisir, licence à vérifier).
6. **Mot de passe : 12 caractères minimum** — ou préfères-tu une autre valeur ? *(Correction : la version actuelle du NIST,
   SP 800-63B-4, recommande **15** caractères minimum pour un mot de passe utilisé seul, 8 seulement avec une double
   authentification. La mention « 8 (minimum NIST) » de la première version de ce plan était inexacte.)*

### 9.1 Choix du porteur (2026-11-24)

| # | Décision |
|---|---|
| 1 | E-mail déjà utilisé : **réponse identique à une réussite (202)**. |
| 2 | Argon2id : **`NSec.Cryptography`** (libsodium). Vérifié avant de coder, le 2026-11-24 : version **26.4.0** (30/04/2026), licence **MIT** ; libsodium **1.0.22**, licence **ISC** ; dépôt `ektrah/nsec` actif (non archivé, 2 tickets ouverts, publications régulières depuis 2024). Aucun problème relevé. NSec exige 1 seul fil de calcul (`p = 1`) et un sel de 16 octets : identique aux paramètres prévus. |
| 3 | Pseudo : règles du § 6.2 acceptées. Idée future notée dans la ROADMAP : un **« nom d'affichage » libre** (accents, autres alphabets), distinct du pseudo unique. |
| 4 | Migrations : par **`migrate.ps1`**. |
| 5 | Validateur de schéma : **maison**, étendu. |
| 6 | Mot de passe : **12 caractères minimum** (sous les 15 du NIST pour un mot de passe seul, choix assumé du porteur pour le prototype) ; **normalisation Unicode NFKC avant le hachage**, avec un test dédié (même mot de passe saisi sous deux formes Unicode différentes → accepté). |

## 10. Risques

| Risque | Parade |
|---|---|
| Argon2id consomme 19 Mio par hachage → beaucoup d'inscriptions simultanées = beaucoup de mémoire | Acceptable en local ; la limitation des tentatives (1.4) bornera le débit ; paramètres ajustables |
| Tests plus lents (hachage + base réelle) | Paramètres Argon2 réduits **uniquement** dans les tests unitaires qui ne testent pas le coût |
| `dotnet-ef` et SDK 11 / cible net10.0 | Outil épinglé en 10.0.x ; si incompatibilité, je m'arrête et je te le signale |
| Oublier de générer une migration après un changement du modèle | Test dédié qui échoue (§ 7) |
| Erreur d'unicité mal traduite → 500 | Test de concurrence (§ 7) |

## 11. Commits prévus (petits)

1. `docs(api): add account registration to core public v1 contract`
2. `chore(core): add Identity module project, EF Core and dotnet-ef local tool`
3. `feat(identity): add account model and initial migration`
4. `feat(identity): add Argon2id password hasher and registration rules`
5. `feat(identity): add POST /v1/accounts registration endpoint`
6. `chore(tools): add migrate.ps1 for the local database`
7. `test(core): add registration integration, concurrency and contract tests`
8. `docs(architecture): describe Identity module, schema and migrations` (+ `SETUP.md`, `PROGRESS.md`)

Puis envoi de la branche sur GitHub. La sous-étape 1.3 ne commence qu'après ta validation de la 1.2.

## 12. Écarts constatés pendant la réalisation

*(rempli pendant la réalisation)*
