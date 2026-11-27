# EUDORIA — Suivi d'avancement

> À mettre à jour à la fin de chaque session.

**Dernière mise à jour :** 2026-11-27 (1.7 partie A terminée, 13 assets importés ; ⏸️ point de contrôle A présenté au porteur)
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
- **Sous-étape 1.7 — partie A en cours (2026-11-27)** :
  - fait : contenu local du test ignoré par git (`unreal/Content/Characters/`, `Fab/`, `RenderTest/`) ; recette
    `docs/render-test/assets.md` ; liste exacte `tools/dev/render-test-content.json` ;
  - fait : `tools/dev/setup-render-test.ps1` — copie **35 fichiers du mannequin (52,5 Mo)** depuis le moteur, sans
    `ABP_Unarmed` ni Control Rig ; `-VerifyReferences` demande à l'éditeur (script Python
    `unreal/Scripts/check_render_test_content.py`) les références manquantes. Il a trouvé un vrai manque (l'espace de
    mélange utilise 8 directions : 14 animations ajoutées) → **0 référence obligatoire manquante** ; 18 références
    « souples » sans effet (aperçus vers Quinn, Control Rig non chargé). Testé : copie, deuxième passage (rien à copier),
    fichier supprimé (recopié), moteur introuvable (erreur claire). `Eudoria.Editor.NoBlueprintLogic` passe avec ce contenu ;
  - **plugin Fab 0.0.23 installé sur E:** (`E:\Epic Games\UE_5.8\Engine\Plugins\Fab`, 117 Mo, actif par défaut).
    Le lanceur refusait l'installation (« existing install », ancien enregistrement vers C:) : avec l'accord du porteur,
    lanceur arrêté et enregistrement `…\Manifests\52E3D1DF6B8DABBF4846C85F5A078A29.item` renommé en `.item.bak`, puis
    réinstallation par le porteur. **Compilation C++ réussie aussitôt après** (le bug `FabToolset` de la 0.0.22 ne
    touche pas la 0.0.23 : plus de module `FabToolset`) ; l'éditeur charge le plugin sans erreur ; tests
    `Eudoria.Editor.*` OK ;
  - **cache du plugin Fab sur E:** : réglage `CacheDirectoryPath` (classe `FabSettings`, enregistré par projet et par
    utilisateur dans `Saved/`) → `E:\Dev\FabCache`, écrit par `setup-render-test.ps1` ; **relu par l'éditeur** (valeur
    d'origine : `C:/Users/…/Temp/FabLibrary`). M3 inutile. **Cache « Vault » du lanceur** réglé par le porteur sur
    `E:\Dev\EpicVaultCache` (M2). Preuve finale (rien écrit sur C:) au premier import ;
  - **tableau des assets proposé** (`docs/render-test/assets.md`) : 13 Megascans « forêt nordique » (sol, falaise en
    surplomb de 29 m, rochers, racines, tronc, souche, fougère, débris), tous gratuits, **licence standard de Fab**
    vérifiée sur chaque fiche ; licence lue sur fab.com/eula : le partage par **dépôt privé** est permis (la « zone
    grise » du plan n'en était pas une ; D1 reste justifiée par le quota) ; niveaux « Personnelle » et
    « Professionnelle » aux mêmes droits, tous deux à 0 € → « Professionnelle » recommandée. Tailles inconnues avant
    import (non affichées par Fab) ;
  - liste **validée par le porteur** (licence « Professionnelle ») ; **13 assets importés par le porteur**
    (M4, M5) : **4 300,9 Mo** dans `unreal/Content/Fab/` (budget 5 Go). `setup-render-test.ps1 -VerifyReferences` :
    **contenu complet, 0 référence obligatoire manquante, Nanite activé sur les 24 maillages** (le script vérifie
    désormais Nanite, lu dans le registre des assets sans les charger). `Eudoria.Editor.NoBlueprintLogic` passe sur
    tout le contenu. `render-test-content.json` complété (13 assets + matériaux partagés du plugin) ; tailles, qualité et
    dossiers notés dans `docs/render-test/assets.md` ;
  - **cache Fab resté sur E:** : `E:\Dev\FabCache` = 6 732 Mo ; `%LOCALAPPDATA%\Temp\FabLibrary` **absent** ;
  - **écart** : qualité **mélangée** — 4 assets en « High » (textures 4K), **9 en « Raw » (textures 8K)**, alors que le
    plan visait le plus haut niveau hors « Raw ». À trancher au point de contrôle A (garder, ou réimporter en « High »
    pour une scène homogène) ;
  - C: a perdu ~1,8 Go pendant l'import sans cause trouvée dans nos outils : seuls ~230 Mo écrits aujourd'hui (traces
    du Bureau à distance de Windows dans `%LOCALAPPDATA%\Temp\DiagOutputDir`, 109 Mo ; registre d'assets du moteur
    `%LOCALAPPDATA%\UnrealEngine\5.8\Intermediate\CachedAssetRegistry_*.bin`, 118 Mo). Fichier d'échange (sur E:) : pic
    de 14,7 Go pendant la session ;
  - **⏸️ point de contrôle A présenté au porteur** (2026-11-27).
- **Plan de la sous-étape 1.7 ✅ validé par le porteur le 2026-11-27** (`docs/plans/etape-01-7.md`) : D1 à D8 =
  recommandations, avec deux compléments (§ 11.1) : point ouvert sur les Animation Blueprints / Control Rig ajouté à
  l'ADR-0003 et à la ROADMAP (décision à l'étape 2) ; seconde campagne de mesures identique après le passage à 32 Go
  (`docs/perf/README.md`).
- Plan de la 1.7 rédigé le 2026-11-27 (8 décisions au § 11). Constats : plugin Fab **pas réellement installé** (enregistré vers
  C:, dossier absent) ; **bug signalé** sur le forum d'Epic (Fab 0.0.22 + UE 5.8.4 du lanceur → compilation C++ cassée,
  `FabToolset`) → vérifié en premier, replis prévus ; Megascans redevenus gratuits sur Fab (05/11/2026, sources
  secondaires, licence à lire sur chaque fiche) ; RX 9060 XT **16 Go** ; le mannequin du moteur contient des Blueprints
  avec logique (`ABP_Unarmed`, Control Rig) → animation proposée en C++.
- **`start-db.ps1` corrigé (2026-11-27)** : lancé avec sa sortie redirigée (`*>`), il échouait sur le message normal de
  Docker « Container … Running » (PowerShell 5.1 change chaque ligne de stderr en erreur). La sortie de Docker est
  désormais convertie en texte, et seul le code de retour décide de l'échec. Vérifié à la main (pas de suite de tests
  pour les scripts) : défaut reproduit avec l'ancienne version ; nouvelle version, sortie redirigée ou non → démarrage
  (base arrêtée ou déjà lancée) et arrêt réussis ; Docker injoignable → toujours une erreur claire.
- **Sous-étape 1.6 ✅ validée par le porteur le 2026-11-27** (parties A et B).
- **Sous-étape 1.6 — partie B (C++) réalisée le 2026-11-26, ✅ validée par le porteur le 2026-11-27** (détail et écarts :
  `docs/plans/etape-01-6.md` § 19) : vérificateur de tickets C++ dans `EudoriaCore` (OpenSSL du moteur, code serveur
  seulement), **mêmes verdicts que le .NET sur les 50 jeux d'essai partagés** ; `FEudoriaSession::RequestEntryTicket` ;
  bout en bout Unreal ↔ noyau réussi (même UUID que `/me`) ; **38 tests Unreal** et **364 tests .NET** passent ;
  3 mutations C++ détectées. Deux défauts du vérificateur .NET de la partie A (exceptions non attrapées : base64url non
  canonique, UTF-8 invalide) trouvés, prouvés et corrigés.
- **Partie A ✅ validée par le porteur le 2026-11-26 ; ADR-0007 « Accepté »** (`CLAUDE.md` à jour). Partie A (détail et
  écarts : `docs/plans/etape-01-6.md` § 18) :
  - **ADR-0007** « Ticket d'entrée des mondes » rédigé (« Accepté » le 2026-11-26) ;
  - contrat core-v1 **1.4.0** : `POST /v1/worlds/{worldId}/entry-tickets` (200 / 400 `world_id_invalid` / 401 /
    404 `world_unknown` / 429) et `GET /v1/entry-tickets/keys` (JWK Set public, cache 5 min) ; format du ticket et règles
    de vérification décrits dans le contrat (tag `worlds`) ;
  - jeton d'accès retypé **`eudoria-access+jwt`**, seul type accepté par le noyau (les anciens jetons `JWT` reçoivent
    401 → le client renouvelle) ;
  - module Identité, dossier `EntryTickets/` : clé de signature **distincte** (le noyau refuse de démarrer si elle
    manque ou est identique à celle des jetons d'accès), `kid` RFC 7638, mondes connus `["sandbox"]` derrière
    `IKnownWorlds`, débits 30/min (tickets) et 60/min (clés) ;
  - **clé des tickets générée dans ton `.env`** (`IDENTITY_ENTRY_TICKET_SIGNING_KEY`, jamais affichée) ;
    `new-signing-key.ps1 -EntryTickets` ; `.env.example`, `run-core.ps1`, `docs/SETUP.md` à jour ;
  - **vérificateur tiers .NET** `core/tests/Eudoria.ThirdParty.WorldVerifier` (aucun lien avec le noyau, aucune
    librairie JWT) + ses tests ; **47 jeux d'essai partagés** `shared/test-vectors/entry-tickets/` (clés publiques
    seulement), que le C++ devra passer en partie B ;
  - **357 tests passent** (99 + 98 + 160), build 0 avertissement, format propre ; **5 tests de mutation détectés** ;
  - **lancement réel** vérifié (connexion → ticket → clés → refus ; journal sans `fail`/`warn`, sans ticket ni jeton) ;
    **tests Unreal relancés contre le noyau : 22 réussis** (le client accepte le nouveau type de jeton) ;
  - ROADMAP : critère « un composant tiers peut vérifier un jeton » **coché** (9 sur 11) ;
  - incident : Docker Desktop figé (moteur muet en mode économie de ressources) → arrêté et relancé, sans perte.
- **Plan de la sous-étape 1.6 ✅ validé par le porteur le 2026-11-26** (`docs/plans/etape-01-6.md`). Ticket d'entrée des mondes : JWT ES256 `typ: eudoria-entry+jwt`, 60 s (tolérance 15 s côté
  monde), audience `eudoria-world:<id>`, usage unique (`jti` retenus par le monde), signé par une **clé distincte** publiée
  en JWKS (`GET /v1/entry-tickets/keys`, `kid` RFC 7638) ; jeton d'accès retypé `eudoria-access+jwt` (seul type accepté
  par le noyau) ; premier monde `sandbox` (liste fermée dans `appsettings.json`) ; ADR-0007 à rédiger (« Proposé »).
  Décisions du § 13 : 1 = A (partie A noyau + vérificateur tiers .NET, **contrôle du porteur**, puis partie B C++),
  2 = clé distincte, 3 = oui, 4 = 60 s / 15 s, 5 = ADR-0007, 6 = `sandbox`.
- **Sous-étape 1.5 réalisée et ✅ validée par le porteur le 2026-11-25** (T5), après son test réel **T4 réussi** :
  création de compte, connexion, « Bienvenue », déconnexion, mauvais mot de passe refusé. Journaux de T4 relus : aucune
  erreur, aucun jeton ni mot de passe (seulement des noms de colonnes SQL). Remarque du porteur : contraste insuffisant
  des écrans (texte d'aide gris clair sur fond gris clair) → chantier transverse « Identité visuelle et ergonomie des
  interfaces » ajouté à la ROADMAP. ROADMAP : 4 critères de l'étape 1 cochés (8 sur 11 ; restent la vérification de
  jeton par un tiers (1.6), la scène de rendu (1.7), et « les tests passent » à revérifier en fin d'étape).
- **Plan de la sous-étape 1.5 ✅ validé par le porteur le 2026-11-25** (`docs/plans/etape-01-5.md`) : les 5
  recommandations du § 10 + complément sur le renouvellement (§ 10.1 : **un seul `/auth/refresh` à la fois**, les
  requêtes en 401 attendent son résultat puis sont rejouées ; test « deux 401 simultanés → un seul renouvellement »).
  T1 (5.8.4 visible dans la Bibliothèque du lanceur) et T2 (certificat approuvé) **faits par le porteur le 2026-11-25**.
- **1.5 — noyau en HTTPS fait (2026-11-25)** : `run-core.ps1` écoute sur `https://localhost:5443` seulement et refuse
  de démarrer si le certificat de .NET n'est pas approuvé ; `.env.example` et `.env` : `CORE_HTTPS_PORT=5443` ; contrat
  core-v1 **1.3.1** (adresse de développement) ; `docs/SETUP.md` (procédure du certificat, expiration 2027-11-24).
  Vérifié en réel : `curl` **sans `-k`** → 200 (certificat vérifié) ; HTTP refusé (5443 et 5080) ; inscription →
  connexion → `/me` → renouvellement → déconnexion → renouvellement refusé (401) ; journal sans `fail:`/`warn:`, sans
  mot de passe ni jeton. **229 tests passent**, build 0 avertissement, format propre. Non testé en réel : le message
  d'arrêt de `run-core.ps1` quand le certificat n'est pas approuvé (il aurait fallu retirer l'approbation).
- **1.5 — client Unreal, commits 3 à 5 faits (2026-11-25)**, chacun compilé (`tools/dev/build-unreal.ps1`, éditeur
  fermé) **sans erreur ni avertissement dans notre code** :
  - projet en fichiers texte (`unreal/Eudoria.uproject`, cibles, module `Eudoria`, plugin `EudoriaCore`,
    `Config/*.ini`, carte vide du moteur `/Engine/Maps/Entry`) → **aucun `.uasset`** ;
  - socle : réglages (`CoreBaseUrl=https://localhost:5443/v1`, refus de tout sauf `https://`), lecture pure des
    réponses du contrat, messages en français, transport HTTP remplaçable, session en mémoire avec **renouvellement
    unique** (§ 10.1 du plan) ;
  - front-end : écrans « Se connecter », « Créer un compte » (connexion automatique, message neutre), « Bienvenue »,
    construits en C++ (UMG) ; mode de jeu et contrôleur ;
  - **T3 fait par le porteur (2026-11-25)** : éditeur ouvert en ~3 min (shaders du moteur servis par le cache fourni),
    aucune question posée ; journal : `Found local data cache path=E:\Dev\UnrealCache\DDC` ✅, notre module chargé,
    aucune erreur venant de notre code (des « Condition failed » internes au moteur au démarrage, sans lien) ;
  - **tests Unreal : 20 sur 20 passent** (`tools/dev/test-unreal.ps1`, ~20 s, noyau lancé), dont
    « deux 401 simultanés → un seul renouvellement » et sa variante d'échec ; l'intégration prouve qu'Unreal accepte le
    certificat HTTPS approuvé. **3 tests de mutation détectés** (409 lu comme 400 ; `http://` accepté ; garde du
    renouvellement unique retirée → les deux tests du § 10.1 échouent) ;
  - défauts trouvés et corrigés : adresse du noyau coupée à `https:` (Unreal lit `//` comme un commentaire dans les
    `.ini` → valeur entre guillemets + test) ; conflit de noms entre fichiers de tests en compilation groupée (« unity ») ;
    focus clavier donné à un widget non focalisable (erreur au démarrage du client) ;
  - **client lancé de mon côté** (`run-client.ps1`) : démarre sans erreur, écran « Se connecter » affiché (capture de la
    fenêtre vérifiée). **Parcours à l'écran non fait par moi** : l'outil de contrôle de l'ordinateur ne reconnaît pas la
    fenêtre du client (programme de l'éditeur, absent du menu Démarrer) → ce sera T4 ;
  - **décisions du porteur appliquées (2026-11-25)** : (1) vérification « aucune logique Blueprint » en **C++ éditeur**
    (module `EudoriaEditor`, Python ne peut pas lire les nœuds) : 2 tests `Eudoria.Editor.*`, preuve avec un Blueprint
    temporaire fautif (détecté puis supprimé, rien sur disque), mutation de la règle détectée ; écart noté au § 14 du plan ;
    (2) plugin **AndroidFileServer désactivé** : après un nouveau lancement de l'éditeur, plus de `SecurityToken` dans
    `DefaultEngine.ini` ; réactivation notée dans la ROADMAP (fin d'étape 4, jeton hors du dépôt) ;
  - première compilation : 91 s, 12 actions. Le journal d'UBT n'affiche pas le nombre de tâches parallèles :
    confirmation de `MaxParallelActions` = 6 toujours en attente (à voir sur une compilation plus grosse) ;
  - **cache de l'UBA redirigé vers E:** (règle « tout sur E: ») : il allait dans
    `C:\ProgramData\Epic\UnrealBuildAccelerator` (jusqu'à 40 Go). `BuildConfiguration.xml` :
    `UnrealBuildAccelerator/RootDir = E:\Dev\UnrealCache\UBA` (réglage lu dans le code d'UBT 5.8, fichier validé contre
    le schéma, vérifié à la compilation suivante). Le dossier de C: reste, vide (0 Mo) : supprimable.
  Vérifications faites pour le plan :
  - `LauncherInstalled.dat` liste désormais UE 5.8.4 (`E:\Epic Games\UE_5.8`) → problème résolu ; reste le
    contrôle visuel dans la Bibliothèque du lanceur (action T1 du plan) ;
  - le plugin Fab 5.8 est enregistré vers `C:\Program Files\Epic Games\UE_5.8`, dossier inexistant → à régler en 1.7 ;
  - certificat HTTPS de .NET présent mais non approuvé ; Unreal 5.8 accepte sous Windows les certificats approuvés
    dans le magasin Windows (code du moteur lu) → option recommandée : `dotnet dev-certs https --trust`.
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
- **Plan de la sous-étape 1.4 validé** le 2026-11-24 (8 recommandations + compléments du porteur : IPv6 compté par
  préfixe /64 ; liste de mots de passe SecLists **1 million + liste française**, choix du porteur).
- **Sous-étape 1.4 réalisée et ✅ validée par le porteur le 2026-11-24** (`docs/plans/etape-01-4.md`, § 15) :
  - contrat core-v1 **1.3.0** : `POST /v1/me/password`, `POST /v1/me/sessions/revoke-all`, réponses 429 (`Retry-After`)
    et 503 `server_busy`, codes `password_too_common`, `password_unchanged`, `too_many_attempts` ;
  - **limitation des tentatives** : échecs comptés par e-mail + adresse (5 gratuits, puis 1 → 15 min) et par e-mail seul
    (20 en 15 min), e-mail connu ou non, mot de passe **non vérifié** pendant un blocage ; débit par adresse (IPv4, ou
    **IPv6 /64**) sur connexion, inscription, renouvellement et routes du compte ; au plus 4 hachages simultanés ;
  - **mots de passe courants** refusés (29 961 entrées, 417 Kio, script `tools/dev/build-password-blocklist.ps1`) + mots du
    contexte (pseudo, début de l'e-mail, « eudoria ») ;
  - **changement de mot de passe** et **« tout déconnecter »** : toutes les sessions révoquées et **jetons d'accès coupés
    aussitôt** (colonne `tokens_valid_after`, migration `AddTokensValidAfter`) ; course avec un renouvellement corrigée ;
  - **inscription égalisée** : mêmes requêtes SQL pour « e-mail utilisé » et « inscription réussie » (`INSERT … ON CONFLICT`),
    **plus d'erreur EF Core** dans les journaux ;
  - **229 tests passent**, 7 tests de mutation détectés, build 0 avertissement, format propre ; lancement réel vérifié
    (journal sans `fail:`) ; **mesures de durée non concluantes** (poste chargé pendant la mesure, à refaire).
- **Plan de la sous-étape 1.3 validé** le 2026-11-24 (7 recommandations + compléments du porteur : audience `eudoria-core`,
  `MapInboundClaims = false`, `RequireSignedTokens` / `RequireExpirationTime` explicites, version stricte de la rotation).
- **Sous-étape 1.3 réalisée et ✅ validée par le porteur le 2026-11-24** (`docs/plans/etape-01-3.md`, § 14) :
  - contrat core-v1 **1.2.0** : `POST /v1/auth/login`, `/v1/auth/refresh`, `/v1/auth/logout`, `GET /v1/me` ;
  - jeton d'accès **JWT ES256** 15 min (audience `eudoria-core`) ; jeton de renouvellement aléatoire, **empreinte SHA-256**
    en base (`identity.refresh_tokens`, migration `CreateRefreshTokens`), rotation à chaque usage, 7 jours / session 30 jours,
    réutilisation = session révoquée ; déconnexion = session révoquée ;
  - **ré-hachage à la connexion** des empreintes Argon2id à l'ancien coût (prouvé en réel) ;
  - clé de signature dans `.env`, générée par `tools/dev/new-signing-key.ps1` (fait sur ton poste, jamais affichée) ;
  - **168 tests passent**, 6 tests de mutation détectés, build 0 avertissement, format propre ; lancement réel vérifié ;
  - un défaut de cache de la librairie de Microsoft trouvé et corrigé (§ 14 du plan).
- **Plan de la sous-étape 1.2 validé** le 2026-11-24 (choix du porteur : § 9.1 du plan ; NSec vérifié : MIT, maintenu).
- **Sous-étape 1.2 réalisée et ✅ validée par le porteur le 2026-11-24** :
  - contrat `POST /v1/accounts` (core-v1 1.1.0) ; module `Eudoria.Core.Identity` (règles, NFKC, Argon2id via NSec,
    modèle, migration `CreateAccounts`, route) ; `tools/dev/migrate.ps1` ;
  - **102 tests passent** (unitaires, intégration, concurrence, journaux, contrat), build 0 avertissement, format propre ;
    tests de mutation : NFKC retiré et course au pseudo cassée → détectés ;
  - **vérifié en lancement réel** (migration, noyau, inscriptions par `curl`, table inspectée) — détail : § 12 du plan ;
  - **Smart App Control désactivé par le porteur** le 2026-11-24 (il bloquait les DLL recompilées) ;
  - **Argon2id porté à 64 Mio / 3 passes / p = 1** (RFC 9106, décision du porteur) : médiane **75–87 ms** par hachage sur le
    poste, dans la cible 50–200 ms ; les comptes existants seront ré-hachés à la connexion (1.3) ; 102 tests passent.
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
- **Tâche non bloquante (décision du porteur, 2026-11-24) : refaire plus tard, PC au repos, les mesures de durée de la 1.4**
  (aucun jeu ni gros programme ouvert ; ne bloque pas la 1.5). Mesures : inscription « nouvel e-mail » / « e-mail utilisé »,
  connexion « e-mail inconnu » / « mauvais mot de passe ») : pendant la mesure, un jeu tournait (0,6 Go de RAM libre),
  bruit de ±15 ms. L'égalité des requêtes est prouvée par un test ; seule la mesure chiffrée manque.
- **Dette (1.4)** : compteurs d'échecs **en mémoire** (remis à zéro au redémarrage du noyau, non partagés entre plusieurs
  instances) → stockage partagé à prévoir avec l'hébergement (étape 6). Un attaquant qui connaît un e-mail peut le
  bloquer 15 min (20 échecs) : à revoir avec la double authentification.
- **Dette (1.4)** : derrière un proxy (hébergement), configurer les en-têtes transmis (`ForwardedHeaders`, proxys de
  confiance), sinon tous les clients auraient la même adresse. Vérification en ligne « Have I Been Pwned » : avant
  toute ouverture publique.
- Listes de mots de passe téléchargées : `E:\Dev\_downloads\seclists` (8,9 Mo, jamais commitées, supprimables ; il
  faut les retélécharger pour reconstruire la liste).
- **Dette (décision du porteur, 2026-11-24, plan 1.3)** : renouvellement strict — si la réponse d'un renouvellement est
  perdue (réseau instable) et que le client réessaie, la session est coupée. Prévoir un éventuel **délai de grâce**
  quand les clients mobile et VR arriveront.
- Comptes créés en 1.2 avec l'ancien coût Argon2id (19 Mio / 2 passes) : **ré-hachés automatiquement à leur prochaine
  connexion** (mécanisme livré en 1.3) ; 6 comptes locaux concernés.
- **Dette (1.3)** : les jetons de renouvellement expirés ne sont jamais supprimés de la table (nettoyage à prévoir) ;
  une déconnexion simultanée à un renouvellement de la même session peut laisser vivre le nouveau jeton (cas théorique ;
  le même cas avec « tout déconnecter » est corrigé en 1.4).
- `new-signing-key.ps1` laisse ~9 Mo de compilation dans `%TEMP%\dotnet\runfile` (C:, supprimable).
- **Sauvegarde** : le dépôt est sur GitHub ; la seconde sauvegarde indépendante recommandée par l'ADR-0006 (disque externe) n'est pas encore en place.
- Quota LFS gratuit de GitHub : 10 Gio de stockage et 10 Gio de téléchargement par mois — à surveiller dès l'arrivée des premiers assets.
- **.NET** : Visual Studio 18.12 a installé le SDK **11** (et non 10). Le runtime .NET 10 LTS est présent et le SDK 11 compile pour net10.0 : conforme à l'ADR-0001. Décision (2026-11-24) : SDK 11 épinglé par `global.json`, cible `net10.0`.
- **Tests du noyau** : Docker Desktop doit être démarré (base jetable Testcontainers).
- **Mémoire vive : 16 Go** (32 recommandés). Passage à 32 Go prévu avant l'étape 3. En attendant : `MaxParallelActions` = 6 (fait ; s'applique à l'UBA d'après le code d'UBT 5.8). Retirer la limite une fois à 32 Go.
- **Disque C:** ~71 Go libres ; il descend temporairement pendant les installations.
- ~~`LauncherInstalled.dat` ne liste pas encore UE 5.8~~ : résolu (vérifié le 2026-11-24) ; contrôle visuel dans la
  Bibliothèque du lanceur à faire par le porteur (T1 du plan 1.5).
- **Compilateur MSVC 14.52** « plus récent que la version préférée » d'UE 5.8 (14.50) : UBT l'accepte avec un
  avertissement, et signale un avertissement C4864 dans un en-tête **du moteur** (`Misc/Attribute.h`), pas dans notre
  code. Rien à faire tant que ça compile ; installer MSVC 14.50 serait une installation (accord du porteur requis).
- ~~Plugin Fab enregistré vers un dossier inexistant de C:~~ : résolu le 2026-11-27 (0.0.23 sur E:). L'ancien
  enregistrement renommé `…\Manifests\52E3D1DF6B8DABBF4846C85F5A078A29.item.bak` est supprimable à la fin de la 1.7.
- La limite mémoire WSL (4 Go) s'applique aussi à la distribution Ubuntu déjà présente sur le PC.
- **Trace sur C: (2026-11-27)** : le moteur garde un registre d'assets en cache dans
  `%LOCALAPPDATA%\UnrealEngine\5.8\Intermediate\CachedAssetRegistry_*.bin` (118 Mo). Petit ; à surveiller s'il grossit.
- **Cache du plugin Fab** (`E:\Dev\FabCache`, 6,7 Go) : simple copie des téléchargements, vidable une fois la 1.7 finie
  (`Fab.ClearCache` dans l'éditeur, décision du porteur).
- Unreal 5.9 est en Preview (2026-11-09) ; on reste sur 5.8.x.

## ⏭️ Prochaine étape exacte
- *Non bloquant, quand le PC est au repos :* refaire les mesures de durée de la 1.4 (voir « Problèmes connus »).
- **ROADMAP :** 9 critères de l'étape 1 sur 11 cochés ; sous-étapes 1.1 à 1.6 validées. Restent la scène de rendu (1.7)
  et « les tests passent » (à revérifier en fin d'étape).
- *À surveiller :* Docker Desktop s'est figé une fois en mode « économie de ressources » (2026-11-26) ; s'il se
  reproduit, envisager de désactiver ce mode dans ses réglages (décision du porteur).

1. **Porteur : valider le point de contrôle A**, et trancher la qualité des 9 assets « Raw » (8K) : les garder, ou
   les réimporter en « High » (4K) pour une scène homogène.
2. **Claude, ensuite : partie B** (plan 1.7 § 6 et commits 4 à 6 du § 13) — personnage, entrées et animation en C++,
   carte construite par script Python, entrée depuis « Bienvenue » et menu Échap, avec leurs tests.
