# ADR-0004 — Solution réseau multijoueur

- **Statut :** Accepté (validé par le porteur de projet le 2026-11-24)
- **Date :** 2026-11-24 (révision 2 — adaptée à Unreal Engine, voir ADR-0003)
- **Liée à :** ADR-0001 (backend), ADR-0003 (moteur client), ADR-0005 (fichiers binaires)

## Contexte

Il faut faire communiquer en **temps réel** les clients et les **serveurs de monde** : déplacements, actions,
état du monde, discussion locale. La communication client → noyau (compte, inventaire, économie) passe,
elle, par l'API HTTPS du noyau (ADR-0001) et n'est pas concernée ici.

Exigences :
- **Serveur qui fait autorité** : le client envoie des intentions, le serveur simule et tranche.
- **Serveurs dédiés** (aucun joueur n'héberge), car les mondes sont persistants.
- **Indépendance des mondes** : un monde doit pouvoir être développé, mis à jour et redémarré sans toucher les autres.
- **Versionnement** : un client et un serveur doivent savoir s'ils sont compatibles.
- PC, VR et mobile dans les mêmes mondes quand c'est compatible.

## Options étudiées

### Option A — Réplication native d'Unreal + serveurs dédiés Unreal (retenue)
Chaque monde tourne comme un **serveur dédié Unreal** (programme sans affichage). Le moteur synchronise
automatiquement l'état des objets (« réplication »), les clients envoient leurs actions par des appels au
serveur (RPC), et le serveur décide.

**Avantages**
- Système éprouvé sur des jeux en ligne à très grande échelle, intégré au moteur, documenté.
- Le serveur fait autorité **par conception** dans le modèle Unreal (propriété des objets, validation des RPC).
- Prédiction côté client et correction serveur des déplacements déjà fournies (composant de mouvement des personnages).
- Gestion de la pertinence (on n'envoie à chaque joueur que ce qui le concerne) et des priorités intégrée.
- Même physique, mêmes niveaux, même code côté client et serveur.

**Inconvénients**
- Client et serveur doivent être construits à partir **de la même version du moteur et du même code** pour se comprendre : le versionnement est strict (voir plus bas).
- La construction de serveurs dédiés **exige le moteur compilé depuis les sources** (voir plus bas).
- Un serveur Unreal est plus lourd en mémoire qu'un serveur écrit sur mesure ; le nombre de joueurs par instance est limité (des solutions existent pour aller au-delà, mais plus tard).

### Option B — Serveurs de monde sur mesure (hors moteur) + client Unreal
Serveurs écrits en C#/Go/C++ sans Unreal, avec un protocole maison.

**Avantages :** serveurs légers, indépendants du moteur, protocole totalement maîtrisé.
**Inconvénients :** il faudrait réécrire la physique, les collisions, la navigation et la prédiction de mouvement côté serveur, avec un risque d'écarts avec ce que voit le client. Beaucoup trop de travail pour une personne seule.

### Option C — Services tiers (Photon, Nakama, etc.)
**Avantages :** démarrage rapide, hébergement parfois inclus.
**Inconvénients :** dépendance à un fournisseur, coût à l'échelle, et pour Nakama recouvrement avec le noyau que nous voulons maîtriser. Écarté.

## Décision proposée

**Option A — réplication native d'Unreal avec serveurs dédiés**, en respectant les règles ci-dessous pour
préserver l'indépendance des mondes et le versionnement.

## Préserver l'indépendance des mondes

Unreal est conçu pour un « jeu » unique. Pour garder des mondes indépendants dans ce cadre, on structure le
projet ainsi :

1. **Un seul projet Unreal** (`unreal/`), qui sert à construire le client et les serveurs.
2. **Un plugin socle** (`EudoriaCore`) en C++ : identité, connexion au noyau, avatar, protocole de voyage entre
   mondes, classes de base communes. C'est le seul code que tous les mondes partagent. Il évolue lentement et
   avec prudence.
3. **Chaque monde est un plugin « Game Feature »** (`Plugins/GameFeatures/World_<Nom>/`), avec son code C++,
   ses niveaux et ses assets. Les Game Features sont conçues pour créer des fonctionnalités autonomes que l'on
   active ou désactive, en évitant les dépendances accidentelles entre fonctionnalités sans rapport
   ([documentation Game Features](https://dev.epicgames.com/documentation/en-us/unreal-engine/game-features-and-modular-gameplay-in-unreal-engine)).
   Règle stricte : **un monde ne dépend que du socle, jamais d'un autre monde.**
4. **Un serveur dédié par monde** : le même programme serveur, lancé avec uniquement le plugin du monde concerné
   activé. Chaque monde peut ainsi être redémarré ou mis à jour seul.
5. **Le contenu des mondes est empaqueté séparément** (paquets de contenu par plugin), pour qu'un client puisse
   à terme ne télécharger que les mondes qu'il visite. Le détail sera défini à l'étape 6.
6. **Tout ce qui est commun à tous les mondes** (compte, inventaire, monnaie) reste dans le **noyau**, jamais
   dans un serveur de monde. Les mondes y accèdent via l'API interne versionnée du noyau.

Limite à connaître : tous les mondes partagent la même version du moteur et du socle. Une modification du
socle ou une montée de version du moteur oblige à reconstruire et redéployer **tous** les mondes. C'est
pourquoi le socle doit rester petit et stable.

## Versionnement

Trois niveaux de version, contrôlés automatiquement :

| Niveau | Ce qui est versionné | Contrôle |
|---|---|---|
| **Moteur + socle** | Version du moteur (5.8.x) et du plugin socle | Unreal refuse la connexion si client et serveur ne sont pas compatibles au niveau réseau (vérification intégrée à la poignée de main, basée sur la version du projet et du moteur). On fixe nous-mêmes la version réseau du projet. |
| **Monde** | Version de chaque plugin de monde | Le serveur de monde annonce sa version au **registre des mondes** du noyau ; le client vérifie qu'il possède le contenu compatible avant de s'y connecter. |
| **API du noyau** | `v1`, `v2`… (ADR-0001) | Contrat OpenAPI ; plusieurs versions peuvent coexister pendant une transition. |

## Compilation du moteur depuis les sources

**Pourquoi :** la documentation d'Epic sur les serveurs dédiés indique : « You must use a source build of
Unreal Engine. » ([serveurs dédiés](https://dev.epicgames.com/documentation/en-us/unreal-engine/setting-up-dedicated-servers-in-unreal-engine)).
La version installée par le lanceur ne suffit donc pas pour construire un serveur dédié.

**À partir de quand : l'étape 3** (premier environnement multijoueur), qui est la première à exiger un serveur
dédié. Les étapes 1 et 2 se font avec la version du lanceur (5.8.4, déjà installée).

**Prérequis :**
- Lier ton compte Epic à un compte GitHub, puis accepter l'invitation à l'organisation EpicGames reçue par e-mail
  (à accepter **sous 7 jours**) ([accès aux sources](https://dev.epicgames.com/documentation/en-us/unreal-engine/downloading-source-code-in-unreal-engine)).
- Télécharger la **même version** (branche 5.8, même correctif que celui utilisé jusque-là), puis lancer `Setup.bat`, `GenerateProjectFiles.bat` et compiler.
- Le projet devra alors être associé à cette version du moteur plutôt qu'à celle du lanceur.

**Espace disque :** Epic ne publie pas de chiffre officiel. Les témoignages sur les forums Epic donnent
**environ 150 à 350 Go** une fois le moteur compilé, avec un pic temporaire pendant la compilation
([forum 1](https://forums.unrealengine.com/t/required-disk-space-to-install-ue5-from-source/2742506),
[forum 2](https://forums.unrealengine.com/t/after-building-unreal-engine-from-source-the-folder-is-now-168gb-what-can-i-delete/639560)).
Prévoir **350 Go libres sur E:** (870 Go disponibles le 2026-11-24). La version du lanceur (30 Go) pourra
alors être désinstallée si elle n'est plus utile.

**Temps :** la compilation du moteur prend de quelques dizaines de minutes à plusieurs heures selon la machine
(Epic cite 12 à 16 cœurs comme base pratique pour compiler en local).

## Conséquences si cette option est validée
- Étape 3 : compilation du moteur depuis les sources, création de la cible « Server » du projet, premier serveur dédié (Windows, en local).
- Étape 4 : utilisation de la prédiction de mouvement d'Unreal ; tests de triche (vitesse, téléportation) refusée par le serveur.
- Étape 6 : un plugin Game Feature par monde, registre des mondes, voyage entre serveurs.
- Avant tout hébergement : serveurs Linux (chaîne de compilation croisée), et ADR dédié à l'hébergement.
