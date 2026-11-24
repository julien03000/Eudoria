# Étape 1.3 — Connexion, jetons d'accès et de renouvellement, déconnexion

- **Statut :** ✅ Plan validé par le porteur le 2026-11-24 (7 recommandations du § 11 + compléments du § 11.1) — ✅ réalisé, puis **sous-étape validée par le porteur le 2026-11-24**
- **Date :** 2026-11-24
- **Branche :** `etape-01-identite`
- **Périmètre :** sous-étape 1.3 **uniquement** : se connecter, garder sa session (renouvellement), se
  déconnecter ; ré-hacher à la connexion les empreintes Argon2id à l'ancien coût. Pas de limitation des
  tentatives (1.4), pas de vérification par un serveur de monde (1.6), pas de client Unreal (1.5).

## 1. Objectif

Une personne qui a un compte peut **se connecter** avec son e-mail et son mot de passe. Le noyau lui remet :
- un **jeton d'accès** : courte durée (15 min), **signé** par le noyau, présenté à chaque appel protégé ;
- un **jeton de renouvellement** : plus longue durée, **enregistré côté serveur** donc **révocable**, qui sert
  uniquement à obtenir une nouvelle paire de jetons quand le jeton d'accès expire.

La **déconnexion** révoque le jeton de renouvellement : il ne fonctionne plus (critère de la roadmap).

Résultat visible à la fin :
```
POST /v1/auth/login    {"email":"ana@example.com","password":"…"}
  → 200 {"accessToken":"eyJ…","tokenType":"Bearer","expiresIn":900,"refreshToken":"…","refreshExpiresIn":604800}
  → 401 invalid_credentials      (e-mail inconnu OU mauvais mot de passe : impossible de savoir lequel)

GET  /v1/me            Authorization: Bearer eyJ…
  → 200 {"id":"0193…","pseudo":"Ana_42","createdAt":"2026-11-24T…"}
  → 401                          (jeton absent, expiré ou falsifié)

POST /v1/auth/refresh  {"refreshToken":"…"}
  → 200 nouvelle paire (l'ancien jeton de renouvellement est consommé)
  → 401 refresh_token_invalid    (inconnu, expiré, révoqué ou déjà utilisé)

POST /v1/auth/logout   {"refreshToken":"…"}
  → 204                          (toujours, même si le jeton est inconnu : rien à apprendre)
```

## 2. Ce qui est dans la 1.3 et ce qui n'y est pas

| Dans la 1.3 | Plus tard |
|---|---|
| Connexion, renouvellement, déconnexion | Limitation des tentatives / blocage après N échecs (1.4) |
| Jeton d'accès signé + jeton de renouvellement révocable (table en base) | Changement de mot de passe et « déconnecter tous mes appareils » (1.4, voir § 11 décision 5) |
| `GET /v1/me` : son propre profil — la route protégée qui **prouve** que le jeton fonctionne (§ 11 décision 5) | Publication de la clé publique pour les serveurs de monde, vérification par un tiers (1.6) |
| Ré-hachage à la connexion des empreintes à l'ancien coût (19 Mio / 2 passes → 64 Mio / 3 passes) | Rotation de la clé de signature (le jeton porte déjà un identifiant de clé `kid` pour la permettre) |
| Clé de signature dans `.env`, générée par un script | Nettoyage périodique des jetons de renouvellement expirés (dette notée) |
| | Client Unreal (1.5) |

## 3. Les deux jetons, simplement

| | Jeton d'accès | Jeton de renouvellement |
|---|---|---|
| Rôle | « Je suis le compte X » à chaque appel | Obtenir un nouveau jeton d'accès sans retaper le mot de passe |
| Forme | **JWT** signé (standard RFC 7519) : lisible par tous, infalsifiable sans la clé privée | **Valeur aléatoire** de 32 octets (aucune information dedans) |
| Durée | **15 minutes** | **7 jours** à partir du dernier renouvellement, **30 jours au maximum** après la connexion (§ 11 décision 2) |
| Vérification | Par la **signature** seule, sans base de données (rapide ; et en 1.6, un serveur de monde pourra le faire avec la clé **publique**) | Par la **base** : le noyau la cherche dans sa table |
| Révocable ? | Non : il expire tout seul en ≤ 15 min (§ 11 décision 4) | **Oui** |
| Stocké en base | Non | **Seulement son empreinte SHA-256**, jamais la valeur : une fuite de la base ne donne aucun jeton utilisable |

**Pourquoi deux jetons ?** Le jeton d'accès voyage à chaque requête : s'il fuit, il ne vaut que 15 minutes.
Le jeton de renouvellement voyage rarement, et le serveur peut l'annuler à tout moment.

### 3.1 Contenu du jeton d'accès
En-tête : `alg: ES256`, `typ: JWT`, `kid: <identifiant de la clé>`.
Revendications (« claims ») : `sub` = **UUID du compte** (l'identifiant stable, jamais l'e-mail), `iss` = `eudoria-core`,
`aud` = `eudoria-core` (§ 11.1, complément 1), `iat`, `exp`, `jti` (identifiant unique du jeton). **Rien d'autre** : ni e-mail, ni pseudo
(données personnelles ou modifiables plus tard → on les demande au noyau via `/v1/me`).

### 3.2 Signature : ES256 (§ 11 décision 1)
- **Asymétrique** : le noyau signe avec une **clé privée** (secrète, dans `.env`) ; quiconque a la **clé publique**
  peut vérifier sans pouvoir fabriquer de jeton. Indispensable pour la 1.6 et l'étape 3 (un serveur de monde doit
  vérifier un jeton **sans** détenir le secret du noyau). Une signature symétrique (HS256) est donc écartée.
- **ES256** (ECDSA P-256, RFC 7518) : algorithme standard, pris en charge nativement par .NET **et** par OpenSSL,
  livré avec Unreal (utile à l'étape 3 pour les serveurs dédiés en C++).
- Vérification stricte : **seul ES256 est accepté** (un jeton `alg: none` ou `HS256` est refusé), émetteur, audience
  (`eudoria-core` uniquement), expiration contrôlés ; tolérance d'horloge réduite à **30 s** (5 min par défaut dans .NET) ;
  `RequireSignedTokens` et `RequireExpirationTime` **explicites** ; `MapInboundClaims = false` pour garder `sub` tel quel
  (sinon .NET le renomme en un long nom de revendication Microsoft) — § 11.1, complément 2.

### 3.3 Rotation et détection de vol du jeton de renouvellement (§ 11 décision 3)
- À **chaque** renouvellement, l'ancien jeton est consommé et un **nouveau** est remis (rotation, recommandation
  de la RFC 9700 « OAuth 2.0 Security Best Current Practice »).
- Tous les jetons issus d'une même connexion forment une **famille** (= une session sur un appareil).
- Si un jeton **déjà consommé** est présenté à nouveau, c'est le signe qu'il a été copié : **toute la famille est
  révoquée** (le voleur et la vraie personne sont déconnectés ; la vraie personne se reconnecte avec son mot de passe).
- La consommation est **atomique** en base (`UPDATE … WHERE revoked_at IS NULL`) : deux renouvellements simultanés
  avec le même jeton → un seul réussit, jamais deux paires valides.

## 4. Fichiers créés / modifiés

```
Eudoria/
├── .env.example                                  # MODIFIÉ — IDENTITY_TOKEN_SIGNING_KEY (vide, à générer)
├── shared/api/core-v1.yaml                       # MODIFIÉ — /auth/login, /auth/refresh, /auth/logout, /me (version 1.2.0)
├── tools/dev/
│   ├── new-signing-key.ps1                       # NOUVEAU — génère la clé ES256 et l'écrit dans .env (jamais affichée)
│   ├── NewSigningKey.cs                          # NOUVEAU — petit programme C# « fichier unique » (dotnet run) appelé par le script
│   ├── DotEnv.ps1                                # MODIFIÉ — transmet la clé au noyau (en mémoire du processus)
│   └── run-core.ps1                              # MODIFIÉ — idem
├── core/
│   ├── Directory.Packages.props                  # MODIFIÉ — JwtBearer (§ 9)
│   ├── src/Eudoria.Core.Api/Program.cs           # MODIFIÉ — UseAuthentication/UseAuthorization
│   ├── src/Eudoria.Core.Api/appsettings.json     # MODIFIÉ — durées, émetteur, audience (rien de secret)
│   ├── src/Eudoria.Core.Identity/
│   │   ├── IdentityModule.cs                     # MODIFIÉ — nouveaux services et routes
│   │   ├── Accounts/Account.cs                   # MODIFIÉ — PasswordHash modifiable (ré-hachage)
│   │   ├── Passwords/IPasswordHasher.cs          # MODIFIÉ — + NeedsRehash(hash)
│   │   ├── Passwords/Argon2idPasswordHasher.cs   # MODIFIÉ — idem
│   │   ├── Tokens/TokenOptions.cs                # NOUVEAU — durées, émetteur, audience, clé (refus de démarrer sans clé valide)
│   │   ├── Tokens/AccessTokenIssuer.cs           # NOUVEAU — fabrique et signe le JWT
│   │   ├── Tokens/RefreshToken.cs                # NOUVEAU — entité (empreinte, famille, dates)
│   │   ├── Sessions/LoginEndpoint.cs             # NOUVEAU — POST /auth/login
│   │   ├── Sessions/SessionService.cs            # NOUVEAU — connexion, renouvellement, déconnexion
│   │   ├── Sessions/RefreshEndpoint.cs           # NOUVEAU — POST /auth/refresh
│   │   ├── Sessions/LogoutEndpoint.cs            # NOUVEAU — POST /auth/logout
│   │   ├── Profile/MeEndpoint.cs                 # NOUVEAU — GET /me (protégée)
│   │   └── Persistence/
│   │       ├── RefreshTokenConfiguration.cs      # NOUVEAU — table, index, clé étrangère
│   │       ├── IdentityDbContext.cs              # MODIFIÉ — DbSet RefreshTokens
│   │       └── Migrations/                       # GÉNÉRÉ — migration « CreateRefreshTokens »
│   └── tests/ …                                  # unitaires + intégration + contrat (§ 8)
└── docs/  ARCHITECTURE.md, SETUP.md, PROGRESS.md (mis à jour)
```

Les noms `Tokens/`, `Sessions/`, `Profile/` suivent le découpage par fonctionnalité de la 1.2 (`Registration/`).
Tout reste `internal` ; l'hôte ne voit toujours que `AddIdentityModule` / `MapIdentityEndpoints`.

## 5. Le modèle de données

Nouvelle table `identity.refresh_tokens` (migration `CreateRefreshTokens`) :

| Colonne | Type | Contraintes | Rôle |
|---|---|---|---|
| `id` | `uuid` | clé primaire | UUID v7 |
| `account_id` | `uuid` | clé étrangère → `accounts.id`, suppression en cascade, indexée | Le compte |
| `family_id` | `uuid` | indexée | La session (toutes les rotations d'une même connexion) |
| `token_hash` | `bytea` (32 octets) | **unique** | **SHA-256** du jeton (pas Argon2 : le jeton est déjà 256 bits aléatoires, impossible à deviner ; un hachage rapide suffit et permet la recherche directe) |
| `created_at` | `timestamptz` | — | Création |
| `expires_at` | `timestamptz` | — | Fin de validité de ce jeton (7 jours glissants) |
| `session_expires_at` | `timestamptz` | — | Fin absolue de la session (30 jours après la connexion) |
| `revoked_at` | `timestamptz` | nullable | Consommé (rotation), déconnexion ou vol détecté |

Table `identity.accounts` : **inchangée** (seule la valeur de `password_hash` peut être réécrite lors du ré-hachage).

## 6. Les routes, point par point

### 6.1 Contrat d'abord
`core-v1.yaml` passe en `version: 1.2.0` (ajouts compatibles). Nouveau tag `auth`. Schéma de sécurité
`bearerAuth` (HTTP Bearer, format JWT) appliqué à `/me` seulement. Mêmes **Problem Details** et **codes machine**
qu'en 1.2 (`body_invalid`, `invalid_credentials`, `refresh_token_invalid`). Toutes les réponses contenant des
jetons portent `Cache-Control: no-store` (RFC 6749 : un proxy ou un cache ne doit jamais les garder).

### 6.2 Connexion — `POST /v1/auth/login`
1. Lecture stricte du corps (même lecteur maison qu'en 1.2 : champ inconnu, JSON invalide, > 8 Kio → 400 `body_invalid`).
2. E-mail normalisé (comme à l'inscription), mot de passe normalisé **NFKC** (comme à l'inscription, sinon un
   mot de passe avec accent ne fonctionnerait pas d'un clavier à l'autre).
3. Recherche du compte par e-mail.
4. **E-mail inconnu** : on vérifie quand même le mot de passe contre une **empreinte factice** au coût actuel
   (même durée qu'un vrai compte), puis 401 `invalid_credentials`. **Mauvais mot de passe** : même réponse.
   Personne ne peut savoir si l'e-mail a un compte (exigence de la roadmap).
5. **Bon mot de passe** :
   - si l'empreinte a été faite avec un ancien coût (`NeedsRehash`), le mot de passe — qu'on a justement en main —
     est **ré-haché au coût actuel** (64 Mio / 3 passes) et l'empreinte remplacée (§ 7) ;
   - création d'un jeton de renouvellement (nouvelle famille) et d'un jeton d'accès → 200.
6. Journaux : « connexion réussie pour le compte <UUID> » / « échec de connexion » (**sans** e-mail ni mot de passe).

Pas de règle de longueur à la connexion (12–128) : un mot de passe hors bornes donne simplement `invalid_credentials`
(au-delà de 1024 caractères, refus immédiat sans hachage, pour éviter qu'un corps géant coûte cher).

### 6.3 Renouvellement — `POST /v1/auth/refresh`
Corps `{ refreshToken }`. Le noyau calcule l'empreinte, cherche la ligne :
- introuvable, expirée (`expires_at` ou `session_expires_at` dépassé) → 401 `refresh_token_invalid` ;
- **déjà révoquée** → révocation de **toute la famille** + journal d'avertissement « réutilisation détectée » → 401 (même code) ;
- valide → consommée atomiquement, nouvelle paire remise (même famille, nouveau `expires_at` = min(maintenant + 7 j,
  `session_expires_at`)) → 200.

Le compte est relu au passage : si un jour un compte est supprimé, ses sessions meurent avec lui (clé étrangère en cascade).

### 6.4 Déconnexion — `POST /v1/auth/logout`
Corps `{ refreshToken }`. Révoque **la famille** du jeton (la session de cet appareil). Répond **204 dans tous les cas**
(jeton inconnu, déjà révoqué, mal formé mais JSON valide) : la déconnexion est idempotente et n'apprend rien à
un curieux. Pas besoin du jeton d'accès (on peut se déconnecter même s'il a expiré).

### 6.5 Profil — `GET /v1/me` (protégée)
Exige `Authorization: Bearer <jeton d'accès>`. Renvoie `{ id, pseudo, createdAt }` — **pas l'e-mail** (inutile au
client pour l'instant ; on l'ajoutera quand un écran en aura besoin). Jeton absent, expiré, falsifié, mauvais
algorithme, mauvaise audience → **401** avec l'en-tête standard `WWW-Authenticate: Bearer`.
Compte introuvable (cas théorique) → 401.

## 7. Ré-hachage à la connexion

- `IPasswordHasher.NeedsRehash(hash)` lit les paramètres inscrits dans l'empreinte (`m=…,t=…`) et répond « oui » s'ils
  diffèrent du coût actuel.
- Fait **uniquement après** une vérification réussie (on n'a le mot de passe en clair qu'à ce moment-là).
- Si l'enregistrement de la nouvelle empreinte échoue, la connexion **réussit quand même** (journal d'avertissement) :
  l'ancienne empreinte reste valable, on réessaiera à la prochaine connexion.
- Les comptes créés en 1.2 avec 19 Mio / 2 passes basculent donc d'eux-mêmes à la première connexion. Test dédié.

## 8. Configuration et secret de signature

- Clé privée ES256 (P-256, format PKCS#8 encodé en Base64) dans `.env` : `IDENTITY_TOKEN_SIGNING_KEY` (§ 11 décision 6).
- `tools/dev/new-signing-key.ps1` la génère (via `NewSigningKey.cs`, exécuté par `dotnet run` : aucune installation)
  et l'écrit dans `.env` **sans l'afficher** ; refuse d'écraser une clé existante sans `-Force` (écraser = déconnecter
  tout le monde, sans gravité en local).
- `run-core.ps1` la transmet au noyau par une variable d'environnement **du processus** (comme la chaîne de connexion).
- Le noyau **refuse de démarrer** si la clé est absente ou invalide (message clair, sans afficher la valeur).
- `kid` = empreinte courte de la clé publique : la rotation de clé (plus tard) sera possible sans casser les jetons en cours.
- Durées, émetteur et audience dans `appsettings.json` (rien de secret).
- Dans les tests : une clé jetable générée en mémoire à chaque exécution.

## 9. Tests

| Test | Type | Vérifie |
|---|---|---|
| JWT émis : en-tête (`ES256`, `kid`), revendications exactes (`sub` = UUID, `iss`, `aud`, `exp` = +15 min), aucune donnée personnelle | unitaire | § 3.1 |
| `NeedsRehash` : ancien coût → oui, coût actuel → non, empreinte illisible → non | unitaire | § 7 |
| génération / empreinte du jeton de renouvellement (32 octets, Base64 URL, SHA-256) | unitaire | § 5 |
| connexion valide → 200, deux jetons, `Cache-Control: no-store`, ligne en base avec **empreinte** (jamais la valeur) | intégration | cas nominal |
| e-mail en majuscules / espaces → connexion OK ; mot de passe saisi en forme Unicode différente (NFKC) → OK | intégration | cohérence avec l'inscription |
| mauvais mot de passe / e-mail inconnu → **même** 401, même corps ; le hachage a bien lieu dans les deux cas | intégration | non-révélation |
| compte à l'ancien coût (19 Mio / 2 passes) → connexion OK **et** empreinte réécrite en `m=65536,t=3` | intégration | ré-hachage |
| `/me` avec jeton valide → 200 ; sans jeton, expiré, sans `exp`, signature modifiée, `alg: none`, `HS256`, signé par une autre clé → 401 | intégration | critère « jeton expiré ou falsifié refusé » |
| jeton correctement signé mais portant **une autre audience** (ex. `eudoria`, `world-xyz`) → 401 | intégration | § 11.1, complément 1 |
| renouvellement → nouvelle paire ; l'ancien jeton de renouvellement est refusé ensuite | intégration | rotation |
| **réutilisation** d'un jeton consommé → 401 **et** le jeton le plus récent de la famille ne marche plus | intégration | détection de vol |
| jeton de renouvellement expiré (7 j) et session au-delà de 30 j → 401 | intégration | durées (horloge simulée) |
| 10 renouvellements simultanés du même jeton → exactement 1 réussite, aucun 500 | intégration | atomicité |
| déconnexion → 204 ; le jeton de renouvellement ne marche plus ; jeton inconnu → 204 aussi ; une autre session du même compte reste valide | intégration | critère « après déconnexion… » |
| corps invalides (JSON cassé, champ inconnu, trop gros) → 400 `body_invalid` sur les 3 routes | intégration | robustesse |
| mot de passe, e-mail et jetons absents des journaux | intégration | journaux capturés |
| noyau sans clé / clé invalide → refuse de démarrer | intégration | configuration |
| le modèle EF Core n'a aucune modification sans migration | intégration | (test existant, couvre la nouvelle table) |
| conformité au contrat des 4 nouvelles routes (codes, schémas, en-têtes) | contrat | YAML ↔ code |

Commandes de « terminé » : `dotnet build` (0 avertissement), `dotnet test`, `dotnet format --verify-no-changes`, puis
**lancement réel** : `new-signing-key.ps1`, `migrate.ps1`, `run-core.ps1`, puis par `curl` : connexion → `/me` →
renouvellement → déconnexion → renouvellement refusé ; connexion d'un compte créé en 1.2 et vérification en base que
son empreinte est passée à `m=65536,t=3`. Tests de mutation prévus : retirer la révocation de famille, retirer la
vérification de l'algorithme → des tests doivent échouer. Mesure de la durée « e-mail inconnu » vs « mauvais mot de
passe » notée au compte rendu (un petit écart éventuel serait traité en 1.4, comme pour l'inscription).

## 10. Dépendances proposées (à valider)

| Paquet | Où | Pourquoi | Éditeur |
|---|---|---|---|
| `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.x | Identity (et hôte) | Vérification standard des jetons Bearer par ASP.NET Core ; apporte `Microsoft.IdentityModel.JsonWebTokens`, qu'on utilise aussi pour **fabriquer** les jetons | Microsoft |
| `Microsoft.Extensions.TimeProvider.Testing` 10.x | tests seulement | Horloge simulée pour tester les expirations (7 j, 30 j) sans attendre | Microsoft |

Rien d'autre : ECDSA P-256 et SHA-256 sont fournis par .NET ; le générateur aléatoire aussi. Versions exactes vérifiées
au moment de coder, notées au § 14.

## 11. Points qui demandent ton accord

1. **Format et signature du jeton d'accès** — **recommandé : JWT signé en ES256**, avec la librairie officielle de
   Microsoft (§ 3.2, § 10). Alternatives : *EdDSA (Ed25519) avec NSec*, déjà présent, aucune nouvelle dépendance, mais
   pas pris en charge par la librairie de Microsoft → il faudrait écrire nous-mêmes la lecture/vérification des jetons
   (code de sécurité maison, plus risqué) ; *HS256* (secret partagé) : écarté, un serveur de monde devrait connaître le
   secret du noyau.
2. **Durées** — **recommandé : jeton d'accès 15 min ; renouvellement 7 jours glissants, 30 jours maximum après la
   connexion.** Le client Unreal ne garde les jetons qu'en mémoire (roadmap) : sur PC, on se reconnecte à chaque lancement
   de toute façon ; ces durées serviront surtout plus tard (mobile, VR).
3. **Réutilisation d'un jeton de renouvellement déjà consommé** — **recommandé : révoquer toute la session** (§ 3.3).
   Conséquence : si le client envoie deux renouvellements en même temps par erreur, la session est perdue et il faut se
   reconnecter. Notre client sera écrit pour ne jamais le faire. Alternative : un court délai de grâce (plus complexe,
   affaiblit la détection).
4. **Déconnexion et jeton d'accès** — **recommandé : la déconnexion révoque le jeton de renouvellement ; le jeton d'accès
   déjà émis reste valable jusqu'à son expiration (≤ 15 min)**, le client l'oublie simplement. C'est le prix de la
   vérification sans base de données (indispensable pour les serveurs de monde). Alternative : une liste noire des jetons
   d'accès révoqués, consultée à chaque appel (le noyau et chaque serveur de monde devraient interroger la base).
5. **`GET /v1/me` dans la 1.3** — **recommandé : oui**, c'est le « profil » de la roadmap et la seule façon de prouver
   par un test réel qu'un jeton expiré ou falsifié est refusé. **Changement de mot de passe : en 1.4**, avec
   « déconnecter tous les appareils » (qui doit l'accompagner) et la limitation des tentatives. Alternative : ajouter
   le changement de mot de passe dès la 1.3 (sous-étape plus grosse).
6. **Clé de signature** — **recommandé : dans `.env`, générée une fois par `new-signing-key.ps1`, le noyau refuse de
   démarrer sans elle** (même logique que le mot de passe de la base en 1.1). Alternative : clé générée automatiquement
   au démarrage en développement (plus simple, mais tous les jetons meurent à chaque redémarrage du noyau, et ce
   comportement ne pourrait pas servir en production).
7. **Noms des routes** — **recommandé : `/v1/auth/login`, `/v1/auth/refresh`, `/v1/auth/logout`, `/v1/me`** (lisibles).
   Alternative plus « ressource » : `POST /v1/sessions`, `POST /v1/sessions/refresh`, `DELETE /v1/sessions/current`.

### 11.1 Choix du porteur (2026-11-24)

Les **7 recommandations** ci-dessus sont acceptées, avec ces compléments :

| # | Décision |
|---|---|
| 1 | **Audience du jeton d'accès : `eudoria-core`** (au lieu de `eudoria`) : le jeton ne sert qu'à l'API du noyau. Raison : à l'étape 10, des serveurs de monde communautaires recevront des jetons de joueurs et pourraient les **rejouer** auprès du noyau. Ajouté à la ROADMAP (sous-étape 1.6) : les serveurs de monde ne reçoivent jamais le jeton d'accès du noyau ; le noyau délivre un **ticket d'entrée** de courte durée, avec une audience propre à chaque monde (mécanisme à concevoir en 1.6). Test dédié : un jeton portant une autre audience est refusé. |
| 2 | **Validation JWT** : `MapInboundClaims = false` (garder `sub` tel quel), `RequireSignedTokens = true` et `RequireExpirationTime = true` écrits **explicitement**. |
| 3 | Choix 3 confirmé (**version stricte** : réutilisation = révocation de la session). **Dette notée** : sur un réseau instable (réponse de renouvellement perdue, puis nouvel essai du client), la session est coupée ; prévoir un éventuel **délai de grâce** quand les clients mobile et VR arriveront. |

## 12. Risques

| Risque | Parade |
|---|---|
| Aucune limitation des tentatives en 1.3 : la connexion peut être attaquée par force brute | Seulement en local (127.0.0.1) d'ici la 1.4, qui ajoute la limitation ; Argon2id (~80 ms) ralentit déjà fortement |
| Connexion coûteuse (Argon2id 64 Mio) → beaucoup de connexions simultanées = beaucoup de mémoire | Acceptable en local ; bornée par la limitation de la 1.4 |
| Écart de durée « e-mail inconnu » / « mauvais mot de passe » (comme en 1.2) | Empreinte factice au même coût ; mesure au compte rendu ; correction fine en 1.4 si besoin |
| Mauvaise configuration de la vérification JWT (algorithme, audience…) = faille | Tests dédiés pour chaque cas falsifié (§ 9) + tests de mutation |
| Fuite de la base → vol de sessions | Seules les empreintes SHA-256 des jetons de renouvellement sont stockées |
| Réseau instable : réponse de renouvellement perdue → le client réessaie → session coupée (version stricte) | Accepté pour le PC ; dette notée (délai de grâce à étudier avec les clients mobile/VR) |
| La table des jetons grossit (jetons expirés jamais supprimés) | Négligeable en prototype ; nettoyage noté comme dette |
| Fuite de la clé privée (`.env`) → n'importe qui peut fabriquer des jetons | `.env` jamais commité ; clé jamais affichée ni journalisée ; rotation via `kid` prévue plus tard |

## 13. Commits prévus (petits)

1. `docs(api): add login, refresh, logout and /me to core public v1 contract`
2. `chore(tools): add signing key generator and pass it to the core`
3. `feat(identity): add password rehash detection`
4. `feat(identity): add refresh token model and migration`
5. `feat(identity): add ES256 access token issuing and bearer validation`
6. `feat(identity): add login, refresh and logout endpoints`
7. `feat(identity): add GET /v1/me`
8. `test(core): add session integration, concurrency and contract tests`
9. `docs(architecture): describe sessions and tokens` (+ `SETUP.md`, `PROGRESS.md`)

Puis envoi de la branche sur GitHub. La sous-étape 1.4 ne commence qu'après ta validation de la 1.3.

## 14. Écarts constatés pendant la réalisation

| Prévu | Réalisé | Pourquoi |
|---|---|---|
| Dépendance de test `Microsoft.Extensions.TimeProvider.Testing` (horloge simulée) | **Non ajoutée** : les tests d'expiration modifient directement les dates du jeton en base (`expires_at`, `session_expires_at`) | Une dépendance de moins, test aussi probant |
| `Sessions/LoginEndpoint.cs`, `RefreshEndpoint.cs`, `LogoutEndpoint.cs` | Un seul fichier `Sessions/SessionEndpoints.cs` | Trois routes de quelques lignes, même lecture du corps |
| — | `Tokens/TokenSigningKey.cs` (lecture et contrôle de la clé, calcul du `kid`) | Isoler la clé et son contrôle au démarrage |
| Lecture du corps « même lecteur qu'en 1.2 » | **Petit remaniement annoncé** : `Registration/RegistrationRequestReader.cs` devient `Http/StrictJsonReader.cs` (générique) ; `Problem`/`FieldError` déplacés dans `Http/ProblemResults.cs` | Partagé par les 4 routes ; comportement de l'inscription inchangé (tous ses tests passent) |
| `Accounts/Account.cs` modifié (empreinte modifiable) | **Inchangé** : le ré-hachage est une mise à jour SQL directe, faite **seulement si l'empreinte n'a pas changé entre-temps** | Plus sûr (pas d'écrasement d'un changement simultané) |
| Contrat : codes d'erreur | Champs `credentials` et `refreshToken` ajoutés à `FieldError.field` ; `/me` 401 **sans corps** (en-tête `WWW-Authenticate: Bearer`) | Comportement standard d'ASP.NET Core pour un jeton Bearer refusé |
| Tests de contrat | Le banc de test sait maintenant préparer un compte / une session avant un scénario, vérifier les réponses **sans corps** (204, 401) et les **en-têtes documentés** (`Cache-Control: no-store`) | Nécessaire pour couvrir les 4 nouvelles routes |
| Journal « réutilisation détectée » | Avertissement **seulement** quand une session encore active est coupée ; un jeton d'une session déjà fermée (déconnexion) → simple information | Le lancement réel montrait un faux « vol possible » après une déconnexion |

**Problème trouvé et corrigé pendant les tests :** la librairie de Microsoft garde en cache ses objets de signature,
indexés par la **clé publique** et non par l'objet clé. Un deuxième noyau démarré avec la même clé (dans les tests ;
en production, après un redémarrage interne) recevait un objet dont la clé avait été détruite → erreur à chaque
connexion. Corrigé en désactivant ce cache pour notre clé (`CacheSignatureProviders = false`, coût négligeable) ;
commit `fix(identity): do not share cached signature providers between key instances`.

**Limite connue (acceptée, client unique) :** une déconnexion envoyée **exactement en même temps** qu'un renouvellement
de la même session pourrait laisser vivre le nouveau jeton de renouvellement (≤ 7 jours). Notre client n'enverra jamais
les deux en même temps. À revoir avec la dette « délai de grâce » (mobile/VR).

Version retenue : `Microsoft.AspNetCore.Authentication.JwtBearer` **10.0.15** (aligné sur le runtime ASP.NET Core 10.0.15).

### Résultats et constats

| Point | Résultat |
|---|---|
| Tests | **168 passent** (70 unitaires du module + 98 intégration/contrat ; 102 avant la 1.3), build 0 avertissement, `dotnet format` propre |
| Tests de mutation | Révocation de session retirée → 2 tests échouent ; audience non vérifiée → 2 ; expiration non exigée → 1 ; `MapInboundClaims = true` → 3 ; pas d'empreinte factice pour un e-mail inconnu → 1 ; pas de ré-hachage → 1. **Les 6 mutations sont détectées** |
| Lancement réel | `new-signing-key.ps1` (clé écrite dans `.env`, non affichée ; 2e appel : conservée) ; `migrate.ps1` crée `identity.refresh_tokens` ; noyau lancé par `run-core.ps1`. Par `curl` : connexion 200 + `Cache-Control: no-store` ; jeton décodé : `{alg: ES256, kid, typ: JWT}` et `{aud: eudoria-core, iss: eudoria-core, exp, iat, sub, jti}` ; `/me` 200 ; `/me` sans jeton → 401 + `WWW-Authenticate: Bearer` ; jeton modifié → 401 ; mauvais mot de passe → 401 `invalid_credentials` ; renouvellement 200 ; réutilisation de l'ancien jeton → 401 **et** le jeton récent ne marche plus ; déconnexion 204 puis renouvellement → 401 |
| Ré-hachage en réel | Compte de test dont l'empreinte est remplacée par une empreinte à l'ancien coût (`m=19456,t=2`) → connexion 200 → empreinte en base devenue `m=65536,t=3` ; reconnexion 200 |
| Comptes de la 1.2 | 6 comptes locaux sont encore à l'ancien coût (mots de passe inconnus de Claude) : ils seront ré-hachés à leur première connexion |
| « Même durée » e-mail inconnu / mauvais mot de passe | 3 séries de 7 mesures à chaud : médianes **96 / 95 ms**, **101 / 100 ms**, **101 / 106 ms** → écarts −1,3 / −0,3 / +4,7 ms, **sans direction constante** (bruit) |
| Journaux | Ni e-mail, ni mot de passe, ni jeton (test dédié + vérification du journal réel) |
