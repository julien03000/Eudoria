# Étape 1.5 — Projet Unreal en C++ et client minimal de connexion

- **Statut :** ✅ **Sous-étape réalisée et validée par le porteur le 2026-11-25** (test réel T4 réussi). Plan validé le
  même jour : les 5 recommandations du § 10, avec un complément sur la décision 3 (renouvellement unique, § 10.1).
  Écarts de réalisation : § 14.
- **Date :** 2026-11-24 (validé le 2026-11-25)
- **Branche :** `etape-01-identite`
- **Périmètre :** sous-étape 1.5 **uniquement** : créer le projet Unreal dans `unreal/` (C++, plugin socle
  `EudoriaCore`) et un client PC minimal avec trois écrans : « Créer un compte », « Se connecter »,
  « Bienvenue, <pseudo> ». Pas de vérification de jeton par un serveur de monde (1.6), pas de scène 3D, ni Lumen,
  ni Nanite, ni assets Fab (1.7), pas de serveur dédié (étape 3).

---

## 1. Vérifications faites avant d'écrire ce plan

| Point | Constat (2026-11-24) | Conséquence |
|---|---|---|
| **Lanceur Epic / UE 5.8** | `C:\ProgramData\Epic\UnrealEngineLauncher\LauncherInstalled.dat` (modifié aujourd'hui à 21 h 21) **liste maintenant `UE_5.8`** → `E:\Epic Games\UE_5.8`, version 5.8.4-58902052. Manifeste du lanceur présent et cohérent. `Build.version` : 5.8.4. `UnrealVersionSelector` est bien associé aux fichiers `.uproject`. | ✅ Le problème noté dans PROGRESS est **résolu**. Un `.uproject` avec `"EngineAssociation": "5.8"` trouvera le moteur. Il te restera un contrôle visuel (§ 7, étape T1). |
| **Plugin Fab pour 5.8** | Le manifeste `FabPlugin_5.8` pointe vers `C:\Program Files\Epic Games\UE_5.8`, **dossier qui n'existe pas** ; le plugin n'est pas non plus dans `E:\Epic Games\UE_5.8\Engine\Plugins\Marketplace`. | Sans effet sur la 1.5 (pas d'asset Fab). **À régler en 1.7** (réinstaller le plugin Fab sur le moteur de E:). Noté dans PROGRESS. |
| **Certificat HTTPS de développement** | Un certificat `CN=localhost` existe (valide jusqu'au 2027-11-24) mais **n'est pas approuvé** (`dotnet dev-certs https --check --trust` → code 7). Le noyau écoute aujourd'hui en **HTTP** sur `localhost:5080`. | Voir § 5. |
| **Comment Unreal vérifie les certificats** | Lu dans le code du moteur 5.8 : sous Windows, le module HTTP (libcurl + OpenSSL) vérifie le certificat du serveur (`bVerifyPeer`), avec le fichier `cacert.pem` du moteur **plus** le magasin « Autorités racines de confiance » de Windows (`WindowsPlatformSslCertificateManager.cpp`, réglage `[SSL] bUsePlatformProvidedCertificates`, vrai par défaut). | Un certificat approuvé dans Windows sera **aussi accepté par Unreal**, sans réglage particulier. |
| **Mémoire** | 16 Go de RAM ; fichier d'échange géré par Windows sur **E:** (22 Go). `BuildConfiguration.xml` : 6 compilations parallèles, 2 Gio par action. Shaders : réglages par défaut du moteur (`bUseVirtualCores=False`, 50 % des cœurs → ~6 processus de compilation de shaders). | Voir § 6. |
| **Disques** | C: 68 Go libres, E: 833 Go libres. | OK. |

---

## 2. Objectif et périmètre

**Ce que tu pourras faire à la fin de la 1.5 :** lancer le client Unreal sur PC, créer un compte, te connecter,
voir « Bienvenue, <ton pseudo> » (pseudo lu sur le noyau par `GET /v1/me`), te déconnecter. Le tout en **HTTPS**.

**Dans la 1.5 :**
- projet Unreal `unreal/Eudoria.uproject` (UE 5.8, version du lanceur), **écrit en texte par moi** (§ 3) ;
- plugin socle `EudoriaCore` : réglages, appels au noyau, session (jetons **en mémoire seulement**) ;
- module principal `Eudoria` : le « front-end » du client (mode de jeu, écrans) ;
- écrans en C++ ; messages d'erreur en français, sans révéler si un e-mail existe ;
- noyau en HTTPS en local (§ 5) ;
- tests d'automatisation Unreal + script Python « aucune logique dans les Blueprints » ;
- scripts `tools/dev/` pour compiler, lancer et tester le client en une commande chacun.

**Hors 1.5 :** changement de mot de passe et « tout déconnecter » dans le client (l'API existe ; aucun écran prévu
par la ROADMAP pour la 1.5), ticket d'entrée pour les mondes (1.6), scène 3D et mesures (1.7), profils de qualité
par plateforme (1.7 et après), empaquetage d'un client « Shipping » distribuable.

---

## 3. Structure du projet Unreal

Je propose de **créer le projet à la main, en fichiers texte**, plutôt que par l'assistant « Nouveau projet » de
l'éditeur. Raison : l'assistant crée toujours un sous-dossier `<Emplacement>/<Nom>/`, donc `unreal/Eudoria/…`,
ce qui ne respecte pas la structure validée (`unreal/Source`, `unreal/Plugins`…). En texte, tout est
reproductible, relu et versionné, et **tu n'as rien à cliquer** pour la création.

```
unreal/
├── Eudoria.uproject                      # EngineAssociation "5.8", modules et plugins activés
├── Source/
│   ├── Eudoria.Target.cs                 # cible Jeu (client)
│   ├── EudoriaEditor.Target.cs           # cible Éditeur
│   └── Eudoria/                          # module principal : front-end du client
│       ├── Eudoria.Build.cs
│       ├── Public/ Private/
│       │   ├── EudoriaFrontEndGameMode   # mode de jeu de l'écran d'accueil
│       │   ├── EudoriaFrontEndPlayerController  # affiche les écrans, souris visible
│       │   ├── UI/EudoriaLoginScreen, EudoriaRegisterScreen, EudoriaWelcomeScreen
│       │   └── Tests/                    # tests d'automatisation du front-end
├── Plugins/
│   └── EudoriaCore/
│       ├── EudoriaCore.uplugin           # module Runtime, ne dépend d'aucun monde
│       └── Source/EudoriaCore/
│           ├── EudoriaCore.Build.cs
│           ├── Public/ Private/
│           │   ├── EudoriaCoreSettings   # adresse du noyau (UDeveloperSettings, lue dans DefaultGame.ini)
│           │   ├── EudoriaCoreApi        # appels HTTP + lecture des réponses JSON (contrat core-v1)
│           │   ├── EudoriaSessionSubsystem  # session : jetons en mémoire, connexion, déconnexion, profil
│           │   └── Tests/                # tests d'automatisation du socle
├── Config/
│   ├── DefaultEngine.ini                 # carte de démarrage, mode de jeu par défaut
│   ├── DefaultGame.ini                   # nom du projet, adresse du noyau
│   └── DefaultEditor.ini                 # (si nécessaire)
└── Scripts/
    └── check_no_blueprint_logic.py       # vérification « aucune logique dans les graphes Blueprint »
```

- **Aucun fichier binaire (`.uasset` / `.umap`) en 1.5** si tu valides la décision 1 (§ 10) : la carte de démarrage
  est celle, vide, fournie par le moteur (`/Engine/Maps/Entry`), et le mode de jeu est choisi dans `DefaultEngine.ini`.
  Pas besoin de verrou LFS dans cette sous-étape.
- Plugins du moteur activés dans le `.uproject` (aucune librairie externe) : `PythonScriptPlugin` et
  `EditorScriptingUtilities` (éditeur seulement, pour les scripts Python).
- Modules du moteur utilisés : `HTTP`, `Json`, `JsonUtilities`, `UMG`, `Slate`, `SlateCore`, `DeveloperSettings`.
- `.gitignore` : les règles Unreal existent déjà (`Binaries/`, `Intermediate/`, `Saved/`, `*.sln` sous `unreal/`) ;
  je vérifierai avec `git status` après la première compilation qu'aucun fichier généré n'est proposé au commit.

---

## 4. Fonctionnement du client

### 4.1 Le socle `EudoriaCore`
- **Réglages** : `CoreBaseUrl` (ex. `https://localhost:5443/v1`) dans `DefaultGame.ini`. Le client **refuse toute
  adresse qui n'est pas en `https://`** (message clair à l'écran, et test automatisé).
- **Appels** (`FEudoriaCoreApi`) : `POST /accounts`, `POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout`,
  `GET /me`. Délai maximal 10 s par requête. La lecture d'une réponse (code HTTP + JSON → résultat ou liste d'erreurs)
  est une **fonction pure**, testable sans réseau. Les codes d'erreur du contrat (`invalid_credentials`,
  `pseudo_taken`, `too_many_attempts` + `Retry-After`, `server_busy`, etc.) deviennent des messages français.
- **Session** (`UEudoriaSessionSubsystem`, un sous-système de l'instance de jeu) : garde le jeton d'accès et le jeton
  de renouvellement **uniquement en mémoire** ; rien sur disque, **jamais** de jeton ni de mot de passe dans les
  journaux (vérifié par un test). Déconnexion = appel `/auth/logout` puis effacement de la mémoire. Fermer le client
  efface aussi la mémoire (la session reste valide côté noyau jusqu'à son expiration, comme prévu en 1.3).
- **Renouvellement** : voir décision 3 (§ 10) et son complément (§ 10.1) : **un seul renouvellement à la fois**.

### 4.2 Le front-end `Eudoria`
- Au lancement : écran « Se connecter » (e-mail, mot de passe masqué, bouton « Se connecter », lien « Créer un compte »).
- « Créer un compte » : e-mail, pseudo, mot de passe (+ confirmation, vérifiée côté client pour le confort seulement).
  Le noyau répond **202 de la même façon** que l'e-mail soit nouveau ou déjà utilisé (choix de la 1.2). Le client
  enchaîne donc sur une **connexion automatique** avec les mêmes identifiants ; si elle échoue, message neutre
  (« Compte créé ou e-mail déjà utilisé : connecte-toi avec ton mot de passe »). Rien n'est révélé de plus que ce que
  l'écran de connexion révèle déjà.
- « Bienvenue, <pseudo> » : pseudo et date de création lus par `GET /v1/me`, bouton « Se déconnecter ».
- Les champs de mot de passe sont vidés après chaque envoi. Validation côté client minimale (champs vides,
  confirmation) : **le noyau reste seul juge** des règles (principe « le serveur fait autorité »).
- Écrans construits en C++ : voir décision 1 (§ 10).

---

## 5. Certificat HTTPS de développement (question en attente depuis la 1.1)

**Le problème :** la ROADMAP exige un client qui parle au noyau **en HTTPS**. En local, le noyau a besoin d'un
certificat ; celui créé par .NET existe mais n'est pas approuvé, donc Unreal (comme `curl`) le refuserait.

| Option | Principe | Avantages | Inconvénients |
|---|---|---|---|
| **A. Approuver le certificat de .NET** *(recommandée)* | Tu lances `dotnet dev-certs https --trust` ; Windows te demande confirmation. Le noyau écoute en `https://localhost:5443`. | Même chemin qu'en production (TLS + vérification du certificat) ; Unreal le reconnaît via le magasin Windows (§ 1) ; aucun réglage dans le projet Unreal ; réversible (`dotnet dev-certs https --clean`). | Ajoute une autorité racine dans **ton** magasin Windows (utilisateur courant). Elle ne vaut que pour `localhost` et sa clé privée reste sur ton PC : risque faible, mais à connaître. |
| B. Certificat déclaré au seul Unreal | Exporter le certificat (partie publique) dans `Saved/` et le déclarer par `[SSL] DebuggingCertificatePath`. | Rien dans le magasin Windows. | Réglage spécial dans le projet ; ce réglage **n'est pas retiré** des versions « Shipping » par le moteur (lu dans le code) → risque de l'oublier ; `curl` et les autres outils ne font toujours pas confiance au noyau. |
| C. HTTP en local, HTTPS ailleurs | Le client accepte `http://localhost` en développement. | Rien à installer. | Le chemin HTTPS n'est jamais testé avant l'hébergement ; une exception « développement » dans le client = une porte à refermer plus tard. Contraire à la ROADMAP. |

**Recommandation : A.** Changements côté noyau (petits) :
- `tools/dev/run-core.ps1` : `ASPNETCORE_URLS = https://localhost:<CORE_HTTPS_PORT>` (HTTPS seulement ; Kestrel
  utilise automatiquement le certificat de développement en environnement `Development`) ; le script **s'arrête avec
  un message clair** si le certificat n'est pas approuvé (`dotnet dev-certs https --check --trust`) ;
- `.env.example` : `CORE_HTTP_PORT=5080` remplacé par `CORE_HTTPS_PORT=5443` (ton `.env` : je modifie seulement
  cette ligne, sans afficher le reste) ;
- `shared/api/core-v1.yaml` : adresse de développement `https://localhost:5443/v1` → version **1.3.1** (documentation
  seulement, aucune route ne change) ;
- les 229 tests du noyau ne sont pas concernés (ils utilisent un serveur en mémoire) ; je les relance quand même.

---

## 6. Compilations avec 16 Go de RAM

- **Compilation du C++** : par la ligne de commande (`Build.bat EudoriaEditor Win64 Development`), **éditeur fermé**,
  via `tools/dev/build-unreal.ps1`. Avec le moteur du lanceur, seuls nos deux modules sont compilés (quelques
  dizaines de fichiers) : charge modérée. Les limites de `BuildConfiguration.xml` (6 actions, 2 Gio) s'appliquent ;
  je relèverai dans le journal d'UBT le nombre de tâches parallèles réellement utilisé (confirmation attendue depuis
  le début de l'étape).
- **Live Coding** (recompilation éditeur ouvert) : je ne l'utiliserai pas ; pour toi, à éviter tant qu'on est à
  16 Go (éditeur + compilateur en même temps = risque de manque de mémoire).
- **Premier lancement de l'éditeur** : compilation des shaders et remplissage du cache (DDC) — **la partie la plus
  lourde** (estimation 15 à 40 min). Ferme jeux, navigateur chargé et Docker pendant ce lancement (Docker n'est pas
  nécessaire pour ouvrir l'éditeur). Réglages par défaut gardés (~6 processus de shaders) ; si la mémoire sature,
  j'ajoute dans `unreal/Config/DefaultEngine.ini` une limite (`[DevOptions.Shaders]`) et je te le signale.
- **Tests Unreal** : lancés sans fenêtre (`UnrealEditor-Cmd.exe … -nullrhi -unattended`), donc sans rendu ni shaders
  d'affichage : ~2 à 4 Go de RAM.
- Le fichier d'échange de Windows est sur E: et géré automatiquement : bon filet de sécurité, rien à changer.

---

## 7. Qui fait quoi

### Ce que **tu** feras (à la main) — dans l'ordre

| # | Quand | Action | Durée |
|---|---|---|---|
| T1 | Avant tout | Ouvrir le lanceur Epic → **Unreal Engine → Bibliothèque** : vérifier que **5.8.4** apparaît sous « Versions du moteur » (et pas « à installer »). Me dire ce que tu vois. | 1 min |
| T2 | Avant la partie noyau | Dans un onglet du **panneau Terminal** : `dotnet dev-certs https --trust`, puis cliquer **Oui** dans la fenêtre de sécurité de Windows. (Je ne peux pas valider cette fenêtre à ta place, et je ne dois pas.) | 1 min |
| T3 | Après ma première compilation | **Premier lancement de l'éditeur** (double-clic sur `unreal/Eudoria.uproject`, ou commande que je te donnerai) ; programmes lourds fermés ; attendre la fin de la compilation des shaders ; me dire si une fenêtre demande quelque chose (ex. « reconstruire les modules ? » → je préfère que tu répondes **Non** et que tu me préviennes). Puis fermer l'éditeur. | 15–40 min |
| T4 | Fin de sous-étape | **Test réel** : `start-db.ps1`, `run-core.ps1`, puis lancer le client ; créer un compte, se connecter, voir « Bienvenue », se déconnecter, essayer un mauvais mot de passe. | 10 min |
| T5 | Fin de sous-étape | Valider (ou non) la sous-étape. | — |

Aucune autre action dans l'éditeur n'est prévue : pas de Blueprint ni de Widget Blueprint à dessiner (si décision 1 = A).

### Ce que **je** ferai

1. Noyau en HTTPS (§ 5) : `run-core.ps1`, `.env.example`, `.env` (une ligne), contrat 1.3.1, `docs/SETUP.md` ;
   vérification par `curl https://localhost:5443/v1/health/live` **sans** option `-k` (certificat vraiment approuvé).
2. Fichiers du projet Unreal (§ 3) : `.uproject`, cibles, `Build.cs`, `.uplugin`, `Config/*.ini`.
3. Génération des fichiers de projet Visual Studio et première compilation (éditeur fermé), lecture du journal d'UBT.
4. Code C++ du socle puis du front-end (§ 4), compilation sans avertissement.
5. Tests d'automatisation (§ 8) + `tools/dev/test-unreal.ps1`.
6. Script Python `check_no_blueprint_logic.py` + son test de mutation.
7. Scripts `tools/dev/build-unreal.ps1` et `run-client.ps1`.
8. Lancement réel du client de mon côté (fenêtre ouverte, scénario de T4) avant de te le confier.
9. `docs/ARCHITECTURE.md` (organisation du projet Unreal), `docs/SETUP.md` (procédure client), `docs/PROGRESS.md`,
   ROADMAP (critères cochés), commits, push.

---

## 8. Tests

**Tests d'automatisation Unreal** (catégorie `Eudoria.*`, lancés sans fenêtre par `tools/dev/test-unreal.ps1`) :
- *Sans réseau* : lecture de chaque réponse du contrat (202, 200, 400 avec plusieurs erreurs, 401, 409, 429 avec
  `Retry-After`, 503, JSON invalide, réponse vide, délai dépassé) ; traduction des codes en messages ; refus d'une
  adresse `http://` ; la session est vide après déconnexion ; aucun jeton ni mot de passe dans le texte journalisé.
- *Renouvellement unique* (§ 10.1, demandé par le porteur ; sans réseau, avec un faux transport HTTP qui retient
  les réponses pour simuler la simultanéité) : **deux requêtes reçoivent 401 en même temps → un seul appel
  `/auth/refresh`**, et les deux sont rejouées avec le nouveau jeton d'accès. Même test, variante d'échec : le
  renouvellement échoue → toujours un seul appel, les deux requêtes échouent, la session est vidée (retour à
  « Se connecter »).
- *Avec le noyau lancé* (catégorie `Eudoria.Integration.*`, ignorée si le noyau ne répond pas, avec un avertissement
  explicite) : inscription → connexion → `GET /me` → déconnexion → le jeton de renouvellement est refusé ; pseudo
  déjà pris → message attendu ; mauvais mot de passe → message neutre.
- **Tests de mutation** (comme en 1.2–1.4) : fausser volontairement la lecture d'une réponse, et laisser passer une
  adresse `http://` → les tests doivent échouer.

**Vérification « aucune logique Blueprint »** (critère de l'étape 1) : `check_no_blueprint_logic.py` parcourt tous les
Blueprints de `/Game` et des plugins du projet, et échoue si un graphe contient un nœud ajouté. Lancé sans fenêtre
par l'éditeur. Comme il n'y aura aucun Blueprint en 1.5, je **prouve qu'il détecte bien** un cas fautif : le script
de test crée un Blueprint temporaire avec un nœud dans un dossier jetable, vérifie l'échec, puis le supprime.

**Noyau** : les 229 tests relancés après le passage en HTTPS (Docker démarré).

**Définition de « terminé »** : compilation sans erreur ni avertissement de nos modules, tous les tests verts,
lancement réel fait (par moi puis par toi), documentation à jour, commit et push.

---

## 9. Fichiers créés / modifiés (prévision)

- **Créés** : tout `unreal/` décrit au § 3 ; `tools/dev/build-unreal.ps1`, `tools/dev/run-client.ps1`,
  `tools/dev/test-unreal.ps1` ; ce plan.
- **Modifiés** : `tools/dev/run-core.ps1`, `.env.example`, `shared/api/core-v1.yaml` (1.3.1), `docs/SETUP.md`,
  `docs/ARCHITECTURE.md`, `docs/PROGRESS.md`, `docs/ROADMAP.md` (critères), `CLAUDE.md` (seulement si une commande
  utile y manque).
- **Hors dépôt** : ton `.env` (une ligne de port), magasin de certificats Windows (par toi, T2).

---

## 10. Points qui demandent ton accord

1. **Écrans construits en C++ (sans Widget Blueprint) pour la 1.5.**
   - **A (recommandé)** : la mise en page des trois écrans est construite en C++ (widgets UMG créés par le code).
     Aucun asset binaire, rien à dessiner pour toi, tout est relu et testable. L'ADR-0003 dit que le Widget Blueprint
     « ne fait que la mise en page » : c'est une limite, pas une obligation, donc A reste conforme.
   - B : classes C++ + Widget Blueprints enfants pour la mise en page. Plus agréable pour soigner l'apparence, mais
     le dessin se fait **à la main dans l'éditeur** (Python ne sait pas construire proprement l'arbre d'un widget),
     plus des `.uasset` à verrouiller. → À reconsidérer quand l'apparence comptera (éditeur d'avatar, étape 2).
2. **Certificat HTTPS : option A du § 5** (approuver le certificat de .NET ; noyau en HTTPS seulement, port 5443).
3. **Renouvellement du jeton d'accès (15 min).**
   - **A (recommandé)** : si le noyau répond 401 à `GET /me`, le client renouvelle **une fois** puis réessaie ; si le
     renouvellement échoue → retour à « Se connecter ». Simple, sans minuterie. Utile dès la 1.7 (on reste dans la scène
     plus de 15 min).
   - B : pas de renouvellement en 1.5 (reporté à la 1.7).
4. **Connexion automatique après « Créer un compte »** (§ 4.2) — recommandé : oui, avec le message neutre.
5. **Projet créé en fichiers texte par moi** (§ 3) plutôt que par l'assistant de l'éditeur — recommandé : oui.

**Décisions du porteur (2026-11-25) :** 1 = A (écrans en C++), 2 = A (certificat .NET approuvé, HTTPS seul sur 5443),
3 = A avec le complément du § 10.1, 4 = oui, 5 = oui.

### 10.1 Complément du porteur sur la décision 3 : un seul renouvellement à la fois

**Pourquoi :** depuis la 1.3, présenter deux fois le même jeton de renouvellement est traité comme un vol
(réutilisation détectée → **toute la session est révoquée**). Si deux requêtes reçoivent 401 en même temps et
lancent chacune leur `/auth/refresh` avec le même jeton, la seconde serait vue comme une réutilisation et
couperait la session du joueur.

**Règle :**
- Le sous-système de session ne lance **qu'un seul** `/auth/refresh` à la fois. Une requête qui reçoit 401 pendant
  qu'un renouvellement est en cours **ne lance rien** : elle attend son résultat.
- Renouvellement réussi → chaque requête en attente est **rejouée une fois** avec le nouveau jeton d'accès.
- Renouvellement échoué → toutes les requêtes en attente échouent, la session est vidée, retour à « Se connecter ».
- Une requête partie avec un jeton d'accès **déjà remplacé** (renouvellement terminé entre son envoi et son 401) est
  simplement rejouée avec le jeton actuel, **sans** nouveau renouvellement.
- Une requête rejouée qui reçoit encore 401 ne déclenche pas de second renouvellement : échec, retour à « Se connecter »
  (pas de boucle).
- Tout se passe sur le fil principal du jeu (les réponses HTTP d'Unreal y arrivent) : pas de verrou à gérer.

**Test ajouté** (§ 8) : deux 401 simultanés provoquent un seul appel de renouvellement.

---

## 11. Risques

| Risque | Probabilité | Parade |
|---|---|---|
| Manque de mémoire au premier lancement de l'éditeur (shaders) | Moyenne | Programmes lourds fermés (T3) ; limite des processus de shaders dans `DefaultEngine.ini` si besoin. |
| UBT 5.8 n'accepte pas d'emblée Visual Studio 2026 / MSVC 14.52 (version « non préférée ») | Faible à moyenne | Lire le message d'UBT ; si besoin, choisir la version du compilateur dans `BuildConfiguration.xml` **après t'en avoir parlé** (jamais d'installation sans ton accord). |
| Smart App Control bloque les DLL compilées | Faible (désactivé) | Si l'erreur 0x800711C7 revient : je m'arrête et te préviens, je ne contourne rien. |
| Un module compilé par l'éditeur (bouton « Reconstruire ») au lieu de mes scripts | Faible | Consigne T3 : répondre « Non » et me prévenir. |
| Le certificat .NET expire le 2027-11-24 | Certaine (dans 1 an) | `run-core.ps1` le détecte ; procédure de renouvellement dans `SETUP.md`. |
| Disque C: | Faible | Le cache DDC/Zen est déjà sur E: ; je vérifierai au premier lancement la ligne `Found local data cache path=` dans le journal. |

---

## 12. Dépendances

**Aucune nouvelle librairie.** Uniquement des modules et plugins **fournis avec Unreal 5.8** (HTTP, Json, UMG,
Python de l'éditeur, utilitaires de script de l'éditeur) et l'outil `dotnet dev-certs` fourni avec le SDK .NET.

---

## 13. Commits prévus (petits)

1. `docs(plans): add sub-step 1.5 plan` (ce fichier, après ton OK)
2. `feat(core): serve the local core over HTTPS with the trusted dev certificate`
3. `feat(unreal): create the C++ project and the EudoriaCore plugin skeleton`
4. `feat(unreal): add the core API client and the in-memory session`
5. `feat(unreal): add login, registration and welcome screens`
6. `test(unreal): add automation tests and the Blueprint-logic check`
7. `chore(tools): add build, run and test scripts for the Unreal client`
8. `docs(architecture): describe the Unreal project; update setup, progress and roadmap`

---

## 14. Écarts par rapport au plan (réalisation, 2026-11-25)

1. **Vérification « aucune logique Blueprint » en C++ au lieu de Python** (décision du porteur, 2026-11-25).
   - *Constat :* en Python (UE 5.8), les nœuds d'un graphe Blueprint sont **protégés** (`EdGraph.nodes` illisible) ; les
     fonctions disponibles (`list_functions`, `list_events`) comptent comme « implémentés » les nœuds fantômes qu'Unreal
     place d'office dans tout Blueprint neuf (BeginPlay, Tick, script de construction). Un script Python ne pouvait donc
     pas distinguer un Blueprint vide d'un Blueprint avec de la logique (essai fait sur un Blueprint jetable).
   - *Réalisé :* nouveau module **éditeur seul** `EudoriaEditor` (`unreal/Source/EudoriaEditor`, jamais livré dans le client
     ni les serveurs) : règle `EudoriaBlueprintRules::FindLogic` (tout nœud est de la logique sauf fantômes, commentaires,
     entrées/sorties de fonction et de macro) et deux tests lancés par `test-unreal.ps1` :
     `Eudoria.Editor.NoBlueprintLogic` (parcourt tous les Blueprints de `/Game` et des plugins du projet) et
     `Eudoria.Editor.NoBlueprintLogicDetectsFaultyBlueprint` (Blueprint temporaire dans un dossier jetable
     `/Game/__EudoriaTmp_<guid>`, jamais enregistré : neuf → 0 nœud de logique ; + un nœud « Print String » → détecté par
     la règle **et** par le parcours du projet ; puis supprimé du registre et de la mémoire, aucun fichier sur disque).
   - *Test de mutation :* règle neutralisée → le test de preuve échoue (« 0 au lieu de 1 »). Code restauré.
   - Le script `unreal/Scripts/check_no_blueprint_logic.py` prévu au § 3 n'existe donc pas.
2. **Plugin `AndroidFileServer` désactivé** dans `Eudoria.uproject` (décision du porteur) : l'éditeur écrivait à chaque
   ouverture un `SecurityToken` aléatoire dans `Config/DefaultEngine.ini` (jamais commité). Vérifié : après un nouveau
   lancement de l'éditeur, plus aucun `SecurityToken` dans le fichier. Réactivation prévue à la fin de l'étape 4 (ROADMAP).
3. **Cache de l'accélérateur de compilation (UBA) redirigé vers E:** (`BuildConfiguration.xml`), règle « tout sur E: ».
4. **Défauts trouvés en cours de route et corrigés :** adresse du noyau coupée à `https:` (Unreal lit `//` comme un
   commentaire dans les `.ini` → valeur entre guillemets + test) ; conflit de noms entre fichiers de tests en compilation
   groupée ; focus clavier donné à un widget non focalisable.
5. **Lancement réel par Claude partiel :** le client démarre sans erreur et l'écran « Se connecter » a été vérifié sur
   une capture de la fenêtre, mais l'outil de contrôle de l'ordinateur ne reconnaît pas la fenêtre du client : le
   parcours à l'écran (clics, saisie) est fait pour la première fois par le porteur en T4. La même logique est couverte
   par les tests d'intégration contre le vrai noyau.
6. Commits plus petits que prévus au § 13 (corrections séparées) ; scripts `build-unreal.ps1` livré avec le commit 3.
