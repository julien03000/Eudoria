# Étape 1.4 — Protections : limitation des tentatives, mots de passe courants, changement de mot de passe

- **Statut :** ✅ Plan validé par le porteur le 2026-11-24 (les 8 recommandations du § 11) — 🟡 en cours de réalisation
- **Date :** 2026-11-24
- **Branche :** `etape-01-identite`
- **Périmètre :** sous-étape 1.4 **uniquement** : tout ce qui a été reporté en 1.4 par les plans 1.2 et 1.3 et par la
  ROADMAP. Pas de client Unreal (1.5), pas de vérification par un serveur de monde (1.6), pas de « mot de passe oublié »
  par e-mail ni de double authentification (exclus de l'étape 1).

## 1. Objectif

Rendre le service Identité **résistant aux attaques simples** avant d'écrire le client :

1. **Limitation des tentatives** : on ne peut plus essayer des milliers de mots de passe sur un compte, ni un même mot
   de passe sur des milliers de comptes. Critère de la roadmap : *« après N échecs, les tentatives sont temporairement
   bloquées »*.
2. **Mots de passe courants refusés** à l'inscription et au changement de mot de passe (`azertyuiop123`, `motdepasse2024`…).
3. **Changement de mot de passe** (livrable de la roadmap) + **« déconnecter tous mes appareils »** (décision 5 du plan 1.3).
4. **Inscription : égaliser** les chemins « e-mail déjà utilisé » et « inscription réussie » (mêmes requêtes, même
   durée ; écart de ~5 ms mesuré en 1.2), **garder** la gestion des inscriptions simultanées, et **supprimer l'erreur
   EF Core** (`fail:`) des journaux pour un e-mail déjà utilisé.

Résultat visible à la fin :
```
POST /v1/auth/login   (6e mauvais mot de passe d'affilée sur le même compte, depuis la même adresse)
  → 429 too_many_attempts   + en-tête Retry-After: 60       (même réponse si l'e-mail n'existe pas)

POST /v1/accounts     {"email":"…","pseudo":"Ana_42","password":"azertyuiop123"}
  → 400 password_too_common

POST /v1/me/password  Authorization: Bearer eyJ…   {"currentPassword":"…","newPassword":"…"}
  → 200 nouvelle paire de jetons   (toutes les autres sessions sont coupées)
  → 400 password_too_common / password_too_short / password_unchanged…
  → 401 invalid_credentials        (mot de passe actuel faux)

POST /v1/me/sessions/revoke-all   Authorization: Bearer eyJ…
  → 204                            (tous les appareils déconnectés, celui-ci compris)
```

## 2. Ce qui est dans la 1.4 et ce qui n'y est pas

| Dans la 1.4 | Plus tard |
|---|---|
| Limitation des tentatives : connexion, inscription, changement de mot de passe | Partage des compteurs entre plusieurs instances du noyau (hébergement, étape 6 — § 11 décision 2) |
| Liste de mots de passe courants (fichier embarqué, hors ligne) | Vérification en ligne des mots de passe divulgués (« Have I Been Pwned ») — dette avant ouverture publique |
| `POST /v1/me/password`, `POST /v1/me/sessions/revoke-all` | « Mot de passe oublié » par e-mail, double authentification (exclus de l'étape 1) |
| Jetons d'accès coupés **immédiatement** après « tout déconnecter » (§ 11 décision 4) | Liste de ses sessions (« appareils connectés ») avec déconnexion à l'unité |
| Inscription : mêmes requêtes et même durée, plus d'erreur EF Core dans les journaux | Nettoyage des jetons expirés, délai de grâce du renouvellement (dettes de la 1.3, inchangées) |
| Course « tout déconnecter » / renouvellement simultané corrigée (§ 7.3) | Course « déconnexion » / renouvellement de la **même** session (dette 1.3, inchangée) |

## 3. Limitation des tentatives

### 3.1 Les attaques visées

| Attaque | Exemple | Parade |
|---|---|---|
| **Force brute sur un compte** | 10 000 mots de passe essayés sur `ana@example.com` | Compteur d'échecs **par e-mail + adresse IP**, blocage progressif |
| **Force brute distribuée** sur un compte | Mêmes essais, depuis 1 000 adresses IP | Compteur d'échecs **par e-mail seul**, seuil plus haut |
| **Pulvérisation** (« password spraying ») | `Azertyuiop123!` essayé sur 10 000 comptes | Limite de débit **par adresse IP**, toutes routes d'authentification confondues |
| **Épuisement mémoire / processeur** | Rafale d'inscriptions ou de connexions (Argon2id = 64 Mio et ~80 ms chacune) | La limite par IP + une limite **globale** de hachages simultanés |

### 3.2 Règles proposées (valeurs dans `appsettings.json`, ajustables — § 11 décision 1)

| Compteur | Clé | Règle |
|---|---|---|
| Échecs de connexion | e-mail normalisé + IP | **5 échecs gratuits**, puis blocage **progressif** : 1 min, 2, 4, 8, 15 min max. Une connexion réussie remet à zéro. Oubli après 24 h sans échec |
| Échecs de connexion | e-mail normalisé seul | **20 échecs en 15 min** → blocage 15 min (protège contre l'attaque distribuée ; voir le risque « blocage d'un compte par un tiers », § 12) |
| Débit par IP | adresse IP | Connexion : **30 / min** ; inscription : **10 / heure** ; renouvellement : 60 / min (fenêtre glissante) |
| Hachages simultanés | tout le noyau | Au plus **4 hachages Argon2id en même temps** (≈ 256 Mio) ; au-delà, on attend ; file d'attente bornée, puis 503 |
| Mot de passe actuel faux (changement) | compte + IP | Mêmes règles que la connexion (sinon un jeton d'accès volé permettrait de deviner le mot de passe) |

Principes :
- **Pendant un blocage, le mot de passe n'est même pas vérifié** : même un mot de passe correct reçoit 429. Sinon
  le blocage ne servirait à rien (l'attaquant verrait quand même le bon mot de passe passer).
- **Un e-mail inconnu est compté exactement comme un e-mail connu** : sinon, recevoir 429 révélerait qu'un compte
  existe. La réponse 429 est la même dans tous les cas, et elle est **rapide** (pas de hachage) dans tous les cas.
- Réponse : **429** au format d'erreur habituel, code `too_many_attempts`, en-tête **`Retry-After`** (secondes),
  pour que le client Unreal puisse afficher « réessayez dans 2 min ».
- Les incréments sont **atomiques** : 50 mauvais essais envoyés en même temps ne peuvent pas dépasser la limite.
- **Aucune donnée personnelle conservée en clair** : la clé « e-mail » du compteur est une empreinte (HMAC-SHA-256
  avec une clé aléatoire tirée au démarrage, jamais écrite nulle part) ; l'IP n'est jamais journalisée en clair non plus.
- L'adresse IP est celle de la connexion TCP (`RemoteIpAddress`). Il n'y a pas encore de proxy devant le noyau ;
  quand il y en aura un (hébergement), il faudra configurer les en-têtes transmis (`X-Forwarded-For`) — noté au § 12.

### 3.3 Outils
- **Limite de débit par IP** : le limiteur **intégré à ASP.NET Core** (`Microsoft.AspNetCore.RateLimiting`, fourni avec le
  framework : aucun paquet à ajouter), une politique par route.
- **Compteurs d'échecs** (e-mail + IP, e-mail seul) : petit service `LoginThrottle` écrit par nous, en **mémoire**
  (§ 11 décision 2), avec un **nombre maximal d'entrées** (ex. 100 000, les plus anciennes oubliées en premier) pour qu'un
  attaquant ne puisse pas remplir la mémoire avec des e-mails inventés. Horloge `TimeProvider` → testable sans attendre.
- **Hachages simultanés** : un `SemaphoreSlim` autour d'Argon2id (dans `Argon2idPasswordHasher`).

## 4. Mots de passe courants

- Une **liste de mots de passe connus** (issus de fuites publiques), embarquée dans le module Identité comme fichier
  texte, chargée **une fois** au démarrage dans un ensemble à recherche rapide (`FrozenSet`).
- Comparaison **après normalisation NFKC et passage en minuscules** (`AzertyUIOP123` = `azertyuiop123`).
- On ne garde que les entrées d'**au moins 12 caractères** (les plus courtes sont déjà refusées par la longueur
  minimale) : le fichier reste petit.
- **Mots liés au contexte** (recommandation NIST SP 800-63B) : refus aussi si le mot de passe **contient** le pseudo,
  la partie de l'e-mail avant `@`, ou `eudoria` — quand ces mots font au moins 4 caractères (§ 11 décision 3).
- Appliqué à l'**inscription** et au **changement de mot de passe**. **Pas à la connexion** : un compte existant
  garde son mot de passe (il n'y a que des comptes de test).
- Code d'erreur : `password_too_common` (champ `password` ou `newPassword`).
- **Source de la liste — à vérifier avant tout téléchargement** : je propose une liste publique de mots de passe
  issus de fuites (par exemple les listes du projet SecLists, licence MIT, ou la liste des 100 000 mots de passe les plus
  courants publiée par le NCSC britannique). Avant de télécharger quoi que ce soit, **je te donnerai le nom exact du
  fichier, sa source, sa taille et sa licence** et j'attendrai ton accord (§ 11 décision 3).
- Un script `tools/dev/build-password-blocklist.ps1` refait le fichier filtré à partir du fichier source (NFKC,
  minuscules, ≥ 12 caractères, sans doublons, trié) et note en tête du fichier la source et l'empreinte SHA-256 du fichier
  d'origine. Le fichier source téléchargé n'est **pas** commité, seule la liste filtrée l'est.

## 5. Changement de mot de passe — `POST /v1/me/password` (protégée)

Corps : `{"currentPassword": "…", "newPassword": "…"}` (lecture stricte habituelle, champ inconnu → 400).

Déroulé :
1. Jeton d'accès valide exigé (sinon 401).
2. Compteur d'échecs (compte + IP) : si bloqué → **429** sans rien vérifier.
3. Validation du nouveau mot de passe : mêmes règles qu'à l'inscription (NFKC, 12 à 128 caractères) + liste des
   mots de passe courants + mots du contexte. Erreurs → **400** (`newPassword` : `password_too_short`, `password_too_common`…).
4. Vérification du **mot de passe actuel** (Argon2id). Faux → **401** `invalid_credentials` (champ `currentPassword`),
   échec compté.
5. Nouveau mot de passe **identique** à l'actuel → **400** `password_unchanged` (§ 11 décision 5).
6. En **une transaction** : nouvelle empreinte Argon2id ; **toutes** les sessions du compte révoquées ; date
   « jetons valables après » du compte mise à jour (§ 7) ; **nouvelle session** ouverte pour l'appareil qui a fait la demande.
7. Réponse **200** avec une nouvelle paire de jetons (`Cache-Control: no-store`), comme une connexion : la personne
   reste connectée ici, tous les autres appareils sont déconnectés (§ 11 décision 6).

Ordre 3 avant 4 : on ne hache pas pour rien si le nouveau mot de passe est de toute façon refusé ; cela ne révèle rien
(la personne connaît déjà son compte).

## 6. « Déconnecter tous mes appareils » — `POST /v1/me/sessions/revoke-all` (protégée)

- Jeton d'accès valide exigé ; **pas** de mot de passe demandé (action sans danger : au pire, on se reconnecte).
- Révoque **toutes** les sessions du compte, **celle-ci comprise**, et met à jour « jetons valables après » (§ 7).
- Réponse **204**. Le client oublie ses jetons et revient à l'écran de connexion.
- Utile seul (téléphone perdu, PC partagé) et appelé automatiquement par le changement de mot de passe.

## 7. Couper aussi les jetons d'accès déjà émis (§ 11 décision 4)

### 7.1 Le problème
Décision 4 de la 1.3 : un jeton d'accès reste valable jusqu'à sa fin (≤ 15 min) même après une déconnexion. Pour une
déconnexion simple, c'est acceptable. Mais **après « tout déconnecter » ou un changement de mot de passe** (souvent fait
parce qu'on soupçonne un vol), laisser le voleur utiliser le noyau 15 minutes de plus n'est pas acceptable.

### 7.2 La solution proposée
- Nouvelle colonne `accounts.tokens_valid_after` (date, vide par défaut).
- À chaque appel protégé, après la vérification de la signature, le noyau lit cette date (une lecture par clé
  primaire, ~0,2 ms) et **refuse** un jeton dont la date d'émission `iat` est antérieure → 401.
- Ce coût ne touche **que le noyau** : depuis la décision du porteur en 1.3, le jeton d'accès (audience
  `eudoria-core`) n'est accepté que par le noyau ; les serveurs de monde recevront des tickets séparés (1.6). La
  vérification sans base des serveurs de monde n'est donc pas remise en cause.
- `iat` est en secondes : la date enregistrée est **arrondie à la seconde supérieure**, et le nouveau jeton remis par
  le changement de mot de passe est émis à cette date ou après → l'ancien est refusé, le nouveau accepté (test dédié).

### 7.3 Course avec un renouvellement (dette 1.3, corrigée ici pour « tout déconnecter »)
Un renouvellement en cours au moment exact du « tout déconnecter » pourrait créer un nouveau jeton de renouvellement
que la révocation ne voit pas. Correction : le renouvellement **verrouille la ligne du compte en lecture**
(`SELECT … FOR SHARE`) et vérifie que la session a été ouverte **après** `tokens_valid_after` ; « tout déconnecter » met
à jour cette ligne (verrou exclusif). Les deux opérations s'attendent l'une l'autre : plus de jeton survivant. Test de
concurrence dédié. (La course « déconnexion d'une seule session » / renouvellement reste une dette, comme en 1.3.)

## 8. Inscription : mêmes requêtes, même durée, journaux propres

### 8.1 Aujourd'hui (1.2)
`contrôle du pseudo` → `hachage` → `INSERT` → si l'e-mail existe : **exception** PostgreSQL (EF Core l'écrit en
`fail:` dans les journaux) → `contrôle du pseudo` de nouveau → réponse « acceptée ». Le chemin « e-mail utilisé » fait
donc une requête de plus et passe par une exception : **~5 ms** de plus, et une fausse erreur dans les journaux.

### 8.2 Proposé
Les deux chemins exécutent **exactement la même suite** :
1. contrôle du pseudo (public : le révéler ne pose pas de problème) → 409 `pseudo_taken` s'il est pris ;
2. hachage Argon2id (dans tous les cas) ;
3. **une seule requête** `INSERT … ON CONFLICT DO NOTHING RETURNING id` : l'unicité de l'e-mail et du pseudo est
   vérifiée **par la base, dans la même requête**, sans exception (rien n'est inséré en cas de doublon) ;
4. contrôle du pseudo de nouveau — **dans tous les cas** (il ne sert qu'aux inscriptions simultanées, mais il est
   fait aussi quand tout va bien, pour que les deux chemins aient la même durée).

Résultat : rien inséré + pseudo pris → 409 (inscription simultanée perdue) ; rien inséré + pseudo libre → e-mail
utilisé → 202 « acceptée » ; ligne insérée → 202 « acceptée ».

- **Vérifier l'e-mail avant l'insertion** (formulation de la ROADMAP) : la vérification se fait *dans* l'insertion
  (`ON CONFLICT`), ce qui est à la fois plus juste (pas de fenêtre entre la vérification et l'insertion) et
  identique pour les deux chemins. Je l'indique comme un écart de formulation, pas de fond (§ 11 décision 7).
- **Courses gardées** : `ON CONFLICT` traite les inscriptions simultanées sans erreur ; le test de concurrence de la
  1.2 (même pseudo / même e-mail en parallèle → mêmes réponses) est conservé tel quel.
- La requête est écrite en SQL **paramétré** via EF Core (`Database.SqlQuery` avec interpolation, qui crée des
  paramètres — aucun risque d'injection SQL), dans le module Identité.
- **Plus d'exception** → plus de `fail:` d'EF Core. Test : aucun message de niveau Erreur/Avertissement dans les
  journaux capturés pour un e-mail déjà utilisé, ni pour une course de pseudos.
- **Mesure** (critère) : 3 séries de 30 inscriptions à chaud, « nouvel e-mail » contre « e-mail utilisé » →
  écart des médianes **< 1 ms et sans direction constante**. Mesure refaite pour la connexion (« e-mail inconnu » /
  « mauvais mot de passe »). Résultats notés au § 14.

## 9. Fichiers créés / modifiés

```
shared/api/core-v1.yaml                         contrat 1.3.0 : 429 + Retry-After sur login/refresh/accounts/password,
                                                 POST /me/password, POST /me/sessions/revoke-all, nouveaux codes d'erreur
core/src/Eudoria.Core.Identity/
├── Accounts/AccountRules.cs                    codes password_too_common, password_unchanged, too_many_attempts
├── Accounts/Account.cs                         TokensValidAfter
├── Passwords/CommonPasswords.cs                (nouveau) chargement de la liste + mots du contexte
├── Passwords/common-passwords.txt              (nouveau) liste filtrée, ressource embarquée
├── Passwords/Argon2idPasswordHasher.cs         limite de hachages simultanés
├── Throttling/LoginThrottle.cs                 (nouveau) compteurs d'échecs en mémoire, blocage progressif
├── Throttling/ThrottlingOptions.cs             (nouveau) seuils, durées, limites par IP
├── Throttling/RateLimitPolicies.cs             (nouveau) politiques du limiteur intégré d'ASP.NET Core
├── Registration/RegistrationService.cs         INSERT … ON CONFLICT, chemins égalisés, liste de mots de passe
├── Sessions/SessionService.cs                  compteurs à la connexion, verrou du compte au renouvellement,
│                                               révocation de toutes les sessions
├── Account/PasswordChangeEndpoint.cs           (nouveau) POST /v1/me/password
├── Account/PasswordChangeService.cs            (nouveau)
├── Account/RevokeAllSessionsEndpoint.cs        (nouveau) POST /v1/me/sessions/revoke-all
├── IdentityModule.cs                           routes, limiteur, contrôle « jetons valables après » (OnTokenValidated)
└── Persistence/Migrations/…_AddTokensValidAfter.cs  (nouvelle migration)
core/src/Eudoria.Core.Api/Program.cs            app.UseRateLimiter()
core/src/Eudoria.Core.Api/appsettings.json      section Identity:Throttling
core/tests/…                                    tests (§ 10)
tools/dev/build-password-blocklist.ps1          (nouveau) fabrication de la liste filtrée
docs/ARCHITECTURE.md, docs/PROGRESS.md, docs/ROADMAP.md (cases cochées)
```

## 10. Tests

| Test | Type | Vérifie |
|---|---|---|
| Blocage progressif : 5 échecs → 6e = 429 + `Retry-After` ; durées 1, 2, 4… 15 min ; remise à zéro après succès ; oubli après 24 h | unitaire (horloge simulée) | § 3.2 |
| Pendant un blocage, **bon** mot de passe → 429 (le mot de passe n'est pas vérifié : hachage non appelé) | intégration | blocage réel |
| E-mail inconnu bloqué comme un e-mail connu, réponse 429 identique (corps + en-têtes) | intégration | non-révélation |
| Une autre IP sur le même compte n'est pas bloquée (avant le seuil « e-mail seul ») ; au-delà de 20 échecs → bloquée | intégration | § 3.2 |
| 50 mauvais essais simultanés → au plus 5 vérifications de mot de passe, le reste 429, aucun 500 | intégration | atomicité |
| Débit par IP : 31e connexion dans la minute → 429 ; 11e inscription dans l'heure → 429 | intégration | limiteur |
| Nombre maximal d'entrées du compteur respecté (e-mails inventés en masse) | unitaire | mémoire bornée |
| Liste : mot de passe courant (et variantes de casse / forme Unicode) refusé à l'inscription ; mot de passe long et rare accepté | unitaire + intégration | § 4 |
| Mot de passe contenant le pseudo / la partie avant `@` / `eudoria` → refusé ; mot de passe contenant un pseudo de 3 lettres → accepté | unitaire | § 4 |
| Changement : succès → 200, nouvelle paire ; ancien mot de passe refusé, nouveau accepté à la connexion | intégration | § 5 |
| Changement : **toutes** les autres sessions coupées (renouvellement → 401) ; **ancien jeton d'accès → 401 sur `/me`** ; nouveau → 200 | intégration | § 5 + § 7 |
| Changement : mot de passe actuel faux → 401 compté, 6e → 429 ; nouveau trop court / courant / identique → 400 ; sans jeton → 401 | intégration | § 5 |
| « Tout déconnecter » → 204 ; toutes les sessions (celle-ci comprise) refusées ; ancien jeton d'accès refusé ; reconnexion OK | intégration | § 6 |
| « Tout déconnecter » en même temps que 10 renouvellements → aucun jeton de renouvellement ne survit | intégration (concurrence) | § 7.3 |
| Inscription « e-mail utilisé » : mêmes requêtes SQL que « nouvel e-mail » (requêtes capturées et comparées) ; hachage fait dans les deux cas | intégration | § 8 |
| Inscription « e-mail utilisé » et course de pseudos : **aucune** ligne de niveau Erreur ou Avertissement dans les journaux | intégration | § 8 |
| Courses de la 1.2 (même pseudo / même e-mail en parallèle) : réponses inchangées | intégration (existant) | § 8 |
| Mots de passe, e-mails, jetons et IP absents des journaux (nouvelles routes comprises) | intégration | journaux |
| Modèle EF Core sans modification hors migration | intégration (existant) | migration |
| Conformité au contrat 1.3.0 : nouvelles routes, 429 + `Retry-After`, nouveaux codes | contrat | YAML ↔ code |

Les tests existants qui enchaînent beaucoup de connexions ou d'inscriptions recevront des limites relevées dans leur
configuration de test (sauf les tests de limitation eux-mêmes), pour ne pas devenir instables.

**Terminé** : `dotnet build` (0 avertissement), `dotnet test`, `dotnet format --verify-no-changes`, puis **lancement
réel** : `migrate.ps1`, `run-core.ps1`, et par `curl` : 6 mauvais mots de passe → 429 + `Retry-After` ; inscription
avec un mot de passe courant → 400 ; changement de mot de passe → 200, ancien jeton d'accès refusé sur `/me`, autre
session refusée ; « tout déconnecter » → 204 ; journal du noyau relu (aucun `fail:` pour un e-mail utilisé).
Mesures de durée du § 8.2. **Tests de mutation** prévus : vérifier le mot de passe pendant un blocage ; ne pas compter
les e-mails inconnus ; retirer le contrôle `tokens_valid_after` ; retirer le verrou du renouvellement ; retirer le
2e contrôle du pseudo dans le chemin « succès » → chacune doit faire échouer au moins un test.

## 11. Points qui demandent ton accord

1. **Seuils** (§ 3.2) — **recommandé** : 5 échecs gratuits puis blocage progressif 1 → 15 min (e-mail + IP) ; 20 échecs
   en 15 min pour l'e-mail seul ; par IP : 30 connexions/min, 10 inscriptions/heure ; 4 hachages simultanés.
   Alternative : blocage fixe (ex. 15 min après 5 échecs), plus simple mais plus pénible pour une vraie personne qui se trompe.
2. **Où garder les compteurs** — **recommandé : en mémoire** du noyau (bornée). Simple, rapide, aucune donnée stockée ;
   le noyau est une seule instance jusqu'à l'hébergement. Inconvénients : un redémarrage du noyau remet les compteurs
   à zéro (l'attaquant ne le contrôle pas) ; il faudra un stockage partagé quand il y aura plusieurs instances (dette
   notée pour l'étape 6). Alternative : **table PostgreSQL** (survit aux redémarrages, prête pour plusieurs instances),
   mais une écriture en base à chaque échec et une clé secrète de plus dans `.env` pour ne pas stocker les e-mails.
3. **Liste de mots de passe** — **recommandé : fichier embarqué, hors ligne**, filtré à ≥ 12 caractères, + mots du
   contexte (pseudo, partie avant `@`, `eudoria`, s'ils font ≥ 4 caractères). **Je te présenterai la source exacte
   (fichier, taille, licence) avant de la télécharger.** Alternative : interroger le service « Have I Been Pwned »
   (des centaines de millions de mots de passe divulgués, envoie seulement 5 caractères d'une empreinte — anonymat
   « k-anonymity ») : plus complet, mais dépend d'un service extérieur et du réseau → proposé comme dette avant
   l'ouverture publique.
4. **Couper les jetons d'accès après « tout déconnecter » / changement de mot de passe** (§ 7) — **recommandé : oui**,
   colonne `tokens_valid_after` lue à chaque appel protégé du noyau (une lecture rapide en base). Cela complète la
   décision 4 de la 1.3 sans la contredire : une **déconnexion simple** laisse toujours vivre le jeton d'accès ≤ 15 min.
   Alternative : rien de plus, le jeton d'accès volé reste utilisable ≤ 15 min après le changement de mot de passe.
5. **Nouveau mot de passe identique à l'actuel** — **recommandé : refusé** (`password_unchanged`), sinon « changer »
   son mot de passe ne changerait rien tout en déconnectant tous les appareils. Alternative : accepté.
6. **Après un changement de mot de passe** — **recommandé : tous les autres appareils sont déconnectés, celui-ci reste
   connecté** (nouvelle paire de jetons dans la réponse). Alternative : tout le monde est déconnecté, y compris
   l'appareil courant (il faut se reconnecter avec le nouveau mot de passe).
7. **Inscription** (§ 8.2) — **recommandé : `INSERT … ON CONFLICT DO NOTHING`** en une requête (vérification de l'e-mail
   faite par la base au moment de l'insertion, pas de requête séparée avant). Alternative, plus proche du texte de la
   ROADMAP : une requête « cet e-mail existe-t-il ? » avant l'insertion, puis l'insertion — mais il faudrait alors
   faire une fausse écriture dans le cas « e-mail utilisé » pour garder la même durée, et la capture d'exception pour
   les courses resterait (avec son `fail:` dans les journaux).
8. **Noms des routes** — **recommandé : `POST /v1/me/password` et `POST /v1/me/sessions/revoke-all`** (sous `/me` :
   tout ce qui concerne son propre compte). Alternative : `PUT /v1/me/password` et `DELETE /v1/me/sessions`.

## 12. Risques

| Risque | Parade |
|---|---|
| **Blocage d'un compte par un tiers** : quelqu'un qui connaît ton e-mail le bloque exprès (20 échecs) | Blocage temporaire (15 min), jamais définitif ; seul le compteur « e-mail seul » le permet, avec un seuil haut. À revoir avec la double authentification (après l'étape 1) |
| Plusieurs personnes derrière la même IP (famille, école) bloquées ensemble par le débit par IP | Limites par IP volontairement larges ; ajustables dans `appsettings.json` |
| Pas de proxy aujourd'hui : derrière un proxy, toutes les requêtes auraient la même IP | Noté pour l'hébergement : configurer les en-têtes transmis (`ForwardedHeaders`) avec une liste de proxys de confiance |
| Compteurs perdus au redémarrage (si décision 2 = mémoire) | Accepté en prototype ; l'attaquant ne contrôle pas les redémarrages |
| Lecture en base à chaque appel protégé (si décision 4 = oui) | ~0,2 ms par clé primaire ; seulement le noyau ; mesurée au compte rendu |
| La liste de mots de passe ne contient pas tout | Liste + mots du contexte + 12 caractères minimum + Argon2id ; « Have I Been Pwned » en dette |
| Tests existants rendus instables par les nouvelles limites | Limites de test relevées dans la configuration de test, sauf pour les tests de limitation |
| `ON CONFLICT` en SQL écrit à la main : écart possible avec le modèle EF Core (noms de colonnes) | Noms tirés du modèle EF Core ; tests d'intégration sur une vraie base ; test « modèle sans modification hors migration » |
| Limite de hachages simultanés trop basse → lenteur sous charge | Valeur ajustable ; mesurée en local |

## 13. Dépendances

**Aucun nouveau paquet NuGet** : le limiteur de débit (`Microsoft.AspNetCore.RateLimiting`), `FrozenSet`,
`SemaphoreSlim` et HMAC-SHA-256 sont fournis par .NET / ASP.NET Core.
**Un fichier de données externe** : la liste de mots de passe courants (§ 4), téléchargée **seulement après ton accord**
sur la source exacte (nom du fichier, taille, licence).

## 14. Commits prévus (petits)

1. `docs(api): add password change, revoke-all and 429 responses to core public v1 contract`
2. `refactor(identity): register accounts with a single insert on conflict`
3. `feat(identity): reject common and context-specific passwords`
4. `chore(tools): add password blocklist builder`
5. `feat(identity): throttle failed logins per e-mail and IP`
6. `feat(core): rate-limit authentication routes per IP and cap concurrent hashing`
7. `feat(identity): add tokens-valid-after check and migration`
8. `feat(identity): add password change and revoke-all sessions`
9. `test(core): add throttling, password change, revoke-all and timing tests`
10. `docs(architecture): describe throttling and password change` (+ `PROGRESS.md`, `ROADMAP.md`)

Puis envoi de la branche sur GitHub. Rien ne commence avant ton « OK » sur ce plan (et sur les 8 points du § 11).
