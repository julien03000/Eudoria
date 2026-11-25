# EUDORIA — Feuille de route

Ce document découpe la vision (`docs/VISION.md`, section 13) en étapes concrètes.
Chaque étape doit produire **quelque chose de réellement fonctionnel et testé** avant de passer à la suivante.

Une étape = une branche git (`etape-XX-nom`), fusionnée dans `main` après ta validation.

| # | Étape | Statut |
|---|-------|--------|
| 0 | Fondations du projet | 🟢 Validée (2026-11-24) |
| 1 | Identité numérique | 🟡 En cours |
| 2 | Création et personnalisation des avatars | ⚪ À faire |
| 3 | Premier environnement multijoueur | ⚪ À faire |
| 4 | Déplacements et interactions | ⚪ À faire |
| 5 | Sauvegarde et persistance | ⚪ À faire |
| 6 | Connexion entre plusieurs mondes | ⚪ À faire |
| 7 | Premières activités et expériences | ⚪ À faire |
| 8 | Systèmes sociaux | ⚪ À faire |
| 9 | Première économie virtuelle | ⚪ À faire |
| 10 | Outils de création communautaire | ⚪ À faire |

Légende : ⚪ À faire · 🟡 En cours · 🟢 Validée

---

## Étape 0 — Fondations du projet

**Objectif :** poser le cadre de travail avant d'écrire la moindre ligne de code applicatif.

**Livrable :** dépôt git initialisé, feuille de route, architecture globale, ADR des choix techniques majeurs, suivi d'avancement.

**Critères de validation :**
- Le dépôt git existe sur `main`, avec `.gitignore` et `.gitattributes`.
- `docs/ROADMAP.md`, `docs/ARCHITECTURE.md`, `docs/PROGRESS.md` existent.
- Les 5 ADR (backend, base de données, moteur client, réseau, fichiers binaires) sont **validés** par le porteur de projet. ✅ (2026-11-24)
- L'ADR-0006 (hébergement du dépôt distant) est décidé et le dépôt est sauvegardé hors du PC.
- `CLAUDE.md` est mis à jour (sections « Stack technique » et « Structure du dépôt »).
- Les outils de `docs/SETUP.md` nécessaires à l'étape 1 sont installés (après validation des ADR).

---

## Étape 1 — Identité numérique (détaillée)

### Objectif
Permettre à une personne de **créer une identité EUDORIA unique**, de s'y connecter de façon sécurisée,
et de prouver cette identité auprès de tout autre composant du système (futurs mondes compris).
C'est la première brique du **noyau** : tout le reste (avatar, inventaire, économie, mondes) s'y rattachera.

### Périmètre
**Inclus :**
- Création de compte : e-mail + mot de passe + **pseudo public unique** (le nom affiché dans EUDORIA).
- Connexion / déconnexion.
- Session : un **jeton d'accès** de courte durée + un **jeton de renouvellement** révocable.
- Consultation de son propre profil (pseudo, date de création).
- Changement de mot de passe.
- Un **identifiant interne stable** (UUID) par compte, distinct du pseudo et de l'e-mail : c'est lui que tous les autres systèmes utiliseront.
- Un mécanisme permettant à un autre service (futur serveur de monde) de **vérifier qu'un jeton est valide** sans redemander le mot de passe.
- Un **client Unreal minimal** pour PC (écrans de connexion / création de compte) qui parle au noyau en HTTPS.

**Exclus (étapes ultérieures ou plus tard) :**
- Avatar (étape 2), connexion via Google/Discord/etc., double authentification,
  vérification d'e-mail par envoi réel, mot de passe oublié par e-mail, suppression de compte RGPD complète
  (notés comme dettes à traiter avant toute ouverture publique).

### Exigences de sécurité
- Mots de passe **jamais stockés en clair** : hachage avec un algorithme conçu pour ça (Argon2id ou bcrypt).
- **Limitation des tentatives** de connexion (anti-force brute).
- Messages d'erreur qui ne révèlent pas si un e-mail existe déjà.
- Jetons signés, avec expiration ; jetons de renouvellement révocables côté serveur.
- Toutes les entrées validées côté serveur (longueur, format du pseudo, caractères autorisés).
- Aucun secret dans le code : configuration via `.env`, avec un `.env.example` commité.
- En dehors du poste de développement : HTTPS obligatoire.

### Livrables concrets
1. **Service Identité** du noyau, exposant une API **versionnée** (`/v1/...`) :
   inscription, connexion, renouvellement, déconnexion, profil, changement de mot de passe.
2. **Schéma de base de données** des comptes, géré par des **migrations** versionnées.
3. **Client Unreal minimal** (UE 5.8, version du lanceur) :
   - projet Unreal créé dans `unreal/`, en **C++**, avec le plugin socle `EudoriaCore` ;
   - écrans « Créer un compte » et « Se connecter », puis un écran « Bienvenue, <pseudo> » qui prouve que la session fonctionne ;
   - logique (appels HTTP, gestion du jeton, erreurs) **en C++** ; les Widget Blueprints ne font que la mise en page de classes C++ ;
   - création des assets de base par **scripts Python** de l'éditeur quand c'est possible ;
   - jeton conservé uniquement en mémoire pendant la session (pas de stockage en clair sur disque).
4. **Environnement de développement reproductible** : une commande pour lancer la base de données
   et le noyau en local (probablement via Docker Compose — à confirmer).
5. **Tests automatisés** : unitaires (règles de validation, hachage) et d'intégration (API complète contre une vraie base de test) côté noyau ; tests d'automatisation Unreal pour la partie C++ du client (appel au noyau, gestion des erreurs).
6. **Documentation** : contrat de l'API (OpenAPI, `shared/api/`), mise à jour de `ARCHITECTURE.md` et `PROGRESS.md`.
7. **Mise en place technique** : Git LFS activé dans le dépôt, cache de données dérivées d'Unreal déplacé sur E:.
8. **Scène de test de rendu** (sous-étape 1.7) : après la connexion, le joueur arrive dans une petite scène
   où il se déplace à la troisième personne (personnage de base d'Unreal, logique en **C++**), avec **Lumen** et
   **Nanite** activés et quelques assets photoréalistes **gratuits de Fab** (licence vérifiée pour chaque asset,
   cache de Fab redirigé vers E:, taille ajoutée à Git LFS surveillée). But : mesurer les performances réelles
   sur le poste du porteur (images/s, mémoire vidéo, RAM) et **valider l'ADR-0003**.

### Critères de validation
- [x] Je peux créer un compte depuis le client Unreal sur PC, puis me connecter avec. *(1.5, test réel T4 du porteur le 2026-11-25)*
- [x] Le client Unreal ne contient aucune logique dans les graphes Blueprint (vérifié par script). *(1.5 : test d'automatisation C++ éditeur `Eudoria.Editor.NoBlueprintLogic`, preuve sur un Blueprint fautif ; Python ne peut pas lire les nœuds, plan 1.5 § 14)*
- [x] Deux comptes ne peuvent pas avoir le même pseudo ni le même e-mail. *(1.2, garanti par la base, testé en concurrence)*
- [x] Un mauvais mot de passe est refusé ; après N échecs, les tentatives sont temporairement bloquées. *(1.3 + 1.4)*
- [x] Le mot de passe n'apparaît en clair ni en base, ni dans les logs. *(1.2 à 1.4, tests des journaux + lancement réel)*
- [x] Un jeton expiré ou falsifié est refusé par l'API. *(1.3)*
- [x] Après déconnexion, le jeton de renouvellement ne fonctionne plus. *(1.3 ; et « tout déconnecter » coupe aussi les jetons d'accès, 1.4)*
- [ ] Un composant tiers (simulé dans les tests) peut vérifier un jeton et obtenir l'identifiant du compte.
- [ ] Après connexion, la scène de test (Lumen + Nanite, assets Fab) tourne de façon **fluide en qualité Haute** sur le poste du porteur, et les mesures (images/s, mémoire vidéo, RAM) sont notées dans `docs/`.
- [ ] Les tests passent, sans erreur de compilation ni de lint. *(noyau : 229 tests ; client Unreal : 22 tests, 0 avertissement dans notre code (1.5) ; à revérifier à la fin de l'étape, après 1.6 et 1.7)*
- [x] Tout se lance en local en suivant une procédure écrite de quelques commandes. *(noyau et client : `docs/SETUP.md` + scripts `tools/dev/`, 1.5)*

### Découpage prévisionnel (à confirmer au démarrage de l'étape)
1. Squelette du noyau + environnement local (base de données, configuration, une route « santé »).
2. Modèle de compte + migrations + inscription.
3. Connexion, jetons, renouvellement, déconnexion.
4. Protections (limitation des tentatives, validations), dont les reports de la 1.2 (décision du porteur, 2026-11-24) :
   - inscription : **vérifier l'e-mail avant l'insertion** au lieu d'attendre le refus de la base ;
   - **égaliser le chemin** « e-mail déjà utilisé » et « inscription réussie » (mêmes requêtes, même durée ; écart de ~5 ms mesuré en 1.2) ;
   - **garder la capture des courses** (refus d'unicité de PostgreSQL lors d'inscriptions simultanées → mêmes réponses) ;
   - **supprimer l'erreur EF Core des journaux** pour un e-mail déjà utilisé (cas normal, pas une panne).
5. Création du projet Unreal (C++, plugin socle, Git LFS) et client minimal. ✅ *(validée le 2026-11-25)*
6. Vérification de jeton par un service tiers.
   Les serveurs de monde ne reçoivent jamais le jeton d'accès du noyau : le noyau délivre un ticket d'entrée de
   courte durée, avec une audience propre à chaque monde. Il faut concevoir ce mécanisme en 1.6.
   (Décision du porteur, 2026-11-24 : le jeton d'accès porte l'audience `eudoria-core` et n'est accepté que par le noyau.)
7. Test de rendu : scène de test après connexion (troisième personne en C++, Lumen, Nanite, assets Fab gratuits), mesures de performances notées dans `docs/`, validation de l'ADR-0003.

---

## Étape 2 — Création et personnalisation des avatars

**Objectif :** chaque identité possède **un avatar unique**, photoréaliste, qu'elle peut personnaliser.
**Première tâche — évaluation de MetaHuman** (courte étude avec prototype, conclue par un ADR) :
personnalisation possible **par le joueur, dans le jeu** (et pas seulement dans l'outil de création),
coût en performances avec plusieurs avatars à l'écran, déclinaison VR/mobile (niveaux de détail bas),
taille des assets (impact sur le quota Git LFS, ADR-0005), conditions de licence ; et, si MetaHuman ne
convient pas, alternatives.
**Livrable :** éditeur d'avatar dans le client Unreal (apparence de base, couleurs, quelques vêtements) ; les paramètres de l'avatar sont stockés par le noyau et rattachés au compte.
**Validation :** l'ADR avatar est validé ; je personnalise mon avatar, je me reconnecte depuis un autre poste, je retrouve exactement le même avatar.
**Idée future (à décider au démarrage de l'étape, choix du porteur du 2026-11-24) :** un **« nom d'affichage » libre**
(accents, autres alphabets), distinct du **pseudo unique** (ASCII, sert d'identifiant public, étape 1.2). Il faudra alors
traiter les noms « sosies » (caractères qui se ressemblent d'un alphabet à l'autre) et la modération.

## Étape 3 — Premier environnement multijoueur

**Objectif :** plusieurs personnes connectées se voient dans un même lieu.
**Prérequis :**
- **Passage du poste à 32 Go de RAM** (16 Go actuellement) : indispensable pour compiler le moteur dans de bonnes conditions (Epic recommande 32 Go).
- **Compilation d'Unreal 5.8 depuis les sources** (obligatoire pour les serveurs dédiés, ADR-0004 ; ~150 à 350 Go sur E:, liaison des comptes Epic et GitHub).
**Livrable :** un premier serveur dédié Unreal (une place simple, Windows en local) qui accepte les joueurs authentifiés par le noyau et affiche leurs avatars.
**Validation :** deux clients PC rejoignent le même monde et voient l'avatar de l'autre ; un client sans jeton valide est refusé.

## Étape 4 — Déplacements et interactions

**Objectif :** se déplacer et interagir de façon fluide, avec le **serveur qui fait autorité**.
**Livrable :** déplacement à pied synchronisé (prédiction côté client, correction serveur), discussion textuelle de proximité, interactions simples (emotes, objets).
**Validation :** déplacements fluides avec latence simulée ; une triche de vitesse côté client est corrigée par le serveur.
**En fin d'étape :** test d'export sur Meta Quest / Android, pour mesurer tôt le coût du rendu sur ces plateformes (ADR-0003).
À ce moment-là : **réactiver le plugin `AndroidFileServer`** dans `unreal/Eudoria.uproject` (désactivé le 2026-11-25,
sous-étape 1.5) et **garder son jeton hors du dépôt** : l'éditeur écrit un `SecurityToken` aléatoire dans
`Config/DefaultEngine.ini` (section `AndroidFileServerRuntimeSettings`) ; le placer dans un fichier de réglages non versionné
(ex. `Saved/Config/`, ou une variable d'environnement), jamais dans un `.ini` commité.

## Étape 5 — Sauvegarde et persistance

**Objectif :** l'univers se souvient de tout ce qui doit l'être.
**Livrable :** persistance de la position, de l'état du joueur et d'un premier inventaire ; sauvegardes et restauration de la base.
**Validation :** je me déconnecte, le serveur redémarre, je me reconnecte et retrouve mon état ; une restauration de sauvegarde est testée.

## Étape 6 — Connexion entre plusieurs mondes

**Objectif :** plusieurs mondes indépendants reliés au même noyau.
**Livrable :** registre des mondes dans le noyau, deux mondes distincts (un plugin Game Feature chacun, un serveur dédié chacun), un portail pour passer de l'un à l'autre en gardant identité et avatar ; serveurs Linux (compilation croisée) prêts pour l'hébergement.
**Validation :** je passe d'un monde à l'autre par un portail ; chaque monde peut être arrêté/mis à jour sans faire tomber l'autre.

## Étape 7 — Premières activités et expériences

**Objectif :** donner des choses à faire.
**Livrable :** une ou deux activités jouables (ex. course, mini-quête) avec récompenses enregistrées par le noyau.
**Validation :** une activité complète est jouable à plusieurs du début à la fin ; les récompenses sont persistées et non falsifiables par le client.

## Étape 8 — Développement des systèmes sociaux

**Objectif :** créer du lien entre habitants.
**Livrable :** amis, groupes, messagerie inter-mondes, présence (« en ligne dans tel monde »), premières guildes.
**Validation :** j'ajoute un ami, je vois dans quel monde il est, je lui écris, nous formons un groupe et voyageons ensemble.

## Étape 9 — Première économie virtuelle

**Objectif :** une monnaie commune et des échanges sécurisés.
**Livrable :** monnaie gérée exclusivement par le noyau (transactions atomiques, journal d'audit), échanges entre joueurs, premier marché.
**Validation :** aucune création/perte de monnaie possible hors règles ; chaque transaction est traçable ; tests de concurrence réussis.

## Étape 10 — Outils de création communautaire

**Objectif :** permettre à d'autres de créer des contenus/mondes.
**Livrable :** premier kit de création (objets ou petits espaces) et processus de publication contrôlé (validation, isolation, modération).
**Validation :** un contenu créé par un utilisateur est publié, visité par d'autres, et ne peut pas compromettre le noyau.

---

## Chantiers transverses (plus tard)

Chantiers qui ne sont pas une étape à eux seuls ; on les ouvre quand leurs prérequis sont remplis.

- **Identité visuelle et ergonomie des interfaces** *(ajouté par le porteur le 2026-11-25)* : définir l'identité
  d'EUDORIA (couleurs, typographie, style), puis refaire les écrans de connexion, d'inscription et de bienvenue
  (contraste insuffisant aujourd'hui, par exemple le texte d'aide gris clair sur fond gris clair).
  - **Prérequis :** identité visuelle définie.
  - **À reconsidérer au plus tard avec l'éditeur d'avatar (étape 2) :** Widget Blueprints pour la mise en page, comme
    prévu par l'ADR-0003 (aujourd'hui, les écrans sont construits en C++ : décision 1 du plan 1.5).
