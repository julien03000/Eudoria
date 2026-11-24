# EUDORIA — Architecture globale

> Statut : **proposition (Phase 0, révision 2)**. Vue d'ensemble à haut niveau, sans détail d'implémentation.
> Les choix de technologies sont traités dans `docs/decisions/` (ADR, tous au statut « Proposé »).
> Hypothèses actuelles : client et serveurs de monde sous **Unreal Engine 5** (ADR-0003, ADR-0004),
> noyau en **C#/.NET** (ADR-0001), base **PostgreSQL** (ADR-0002).

## 1. Principes directeurs

1. **Noyau séparé des mondes.** Le noyau gère ce qui est commun à tout l'univers ; les mondes gèrent leur propre simulation.
2. **Le serveur fait autorité.** Le client affiche et envoie des *intentions* (« je veux avancer », « je veux acheter »). Le serveur décide. Le client n'est jamais cru sur parole.
3. **Contrats versionnés.** Tout échange entre composants passe par un contrat versionné : API du noyau en OpenAPI (`v1`, `v2`…), compatibilité réseau vérifiée entre client et serveur de monde.
4. **Une seule identité.** Un compte = un identifiant stable, un avatar, valable dans tous les mondes.
5. **Isolation.** Un monde qui plante, est mis à jour ou est malveillant ne doit pas compromettre le noyau ni les autres mondes.
6. **Multi-plateforme dès la conception.** La logique de jeu ne dépend jamais du type d'appareil ; seules les entrées, l'interface et le niveau de qualité visuelle changent entre PC, VR et mobile.
7. **Croissance progressive.** On démarre simple (peu de services, tout en local), mais les frontières sont tracées dès maintenant.

## 2. Les trois grands blocs

### 2.1 Le noyau (backend central)
Ensemble de services qui détiennent la **vérité commune** de l'univers :

| Service | Responsabilité |
|---|---|
| **Identité** | Comptes, authentification, émission et vérification des jetons. |
| **Avatars** | Apparence et identité visuelle de chaque habitant (paramètres d'apparence, pas les fichiers 3D eux-mêmes). |
| **Persistance** | Données durables des joueurs (état, progression, réputation). |
| **Inventaire** | Objets possédés, transferts entre joueurs et entre mondes. |
| **Économie** | Monnaie commune, transactions, journal d'audit. |
| **Registre des mondes** | Liste des mondes, adresses des serveurs, versions, orientation des joueurs vers une instance. |
| **Social** (plus tard) | Amis, groupes, messagerie, présence. |

Au début, ces services vivent dans **un seul programme découpé en modules** (« monolithe modulaire »).
Ils pourront devenir des programmes séparés si la charge l'exige, sans changer leurs contrats.
Le noyau s'appuie sur une **base de données principale** (ADR-0002) et, plus tard, sur un cache et un
système de messages.

Le noyau n'utilise **pas** Unreal : c'est un service web classique, indépendant du moteur.

### 2.2 Les mondes (serveurs dédiés Unreal)
- Chaque monde est un **plugin « Game Feature »** du projet Unreal, qui ne dépend que du **plugin socle** `EudoriaCore` (jamais d'un autre monde).
- Chaque monde tourne sur un ou plusieurs **serveurs dédiés** Unreal (instances), qui **simulent** le monde et en font autorité (réplication native d'Unreal, ADR-0004).
- Un serveur de monde ne gère **pas** l'identité, la monnaie ni l'inventaire commun : il les **demande au noyau** via l'API interne.
- Il peut avoir ses propres données locales (état d'une partie), à condition qu'elles ne contredisent pas le noyau.
- Contrainte Unreal : client et serveurs doivent être construits avec la même version du moteur et du socle (voir ADR-0004).

### 2.3 Les clients (Unreal)
- **Client PC** (premier prototype, rendu photoréaliste), puis **VR** et **mobile** (rendus allégés).
- Un même projet Unreal produit tous les clients ; la différence se fait par **profils de qualité** par plateforme (Device Profiles, Scalability) et par une couche d'entrées et d'interface propre à chaque plateforme (ADR-0003).
- Un client ne contient **aucune logique qui fait foi** : il affiche, prédit pour la fluidité, et envoie des intentions.
- Toute la logique est en **C++** ; les Blueprints ne servent qu'à assigner assets et réglages (ADR-0003).

## 3. Comment les blocs communiquent

| Liaison | Nature | Usage |
|---|---|---|
| Client → Noyau | **HTTPS**, API publique versionnée (contrat OpenAPI) | Connexion, profil, avatar, inventaire, liste des mondes, achats. |
| Client ↔ Monde | **Réseau Unreal** (UDP, réplication + appels au serveur) | Déplacements, actions, état du monde, discussion locale. |
| Monde → Noyau | **HTTPS**, API interne versionnée (contrat OpenAPI), authentifiée par serveur | Vérifier un joueur, lire/écrire l'inventaire, transactions, rapport d'état au registre. |
| Noyau → Monde | Notifications (plus tard, via système de messages) | Ex. : un objet a été donné au joueur depuis un autre monde. |

### Parcours type d'un joueur
1. Le client se connecte au **noyau** (Identité) et reçoit un **jeton**.
2. Il demande au **registre des mondes** où aller ; le registre renvoie l'adresse d'un serveur de monde compatible avec la version du client.
3. Le client se connecte au **serveur dédié** du monde en présentant son jeton.
4. Le serveur de monde **vérifie le jeton** (signature ou appel au noyau) et charge l'avatar depuis le noyau.
5. Pendant la session, le monde envoie au noyau tout ce qui doit être persisté ou qui touche l'économie.
6. Pour changer de monde (portail), le client redemande une destination au registre et recommence à l'étape 3.

## 4. Schéma d'ensemble

```mermaid
flowchart LR
    subgraph Clients["Clients Unreal (même projet)"]
        PC["Client PC<br/>photoréaliste"]
        VR["Client VR<br/>allégé (plus tard)"]
        MOB["Client mobile<br/>allégé (plus tard)"]
    end

    subgraph Noyau["Noyau (service web C#/.NET)"]
        API["API publique v1<br/>(HTTPS, OpenAPI)"]
        ID["Identité"]
        AV["Avatars"]
        PERS["Persistance"]
        INV["Inventaire"]
        ECO["Économie"]
        REG["Registre des mondes"]
        INT["API interne v1<br/>(HTTPS, OpenAPI)"]
        DB[("PostgreSQL")]
    end

    subgraph Mondes["Mondes (serveurs dédiés Unreal)"]
        W1["Monde A<br/>plugin Game Feature<br/>instances 1..n"]
        W2["Monde B<br/>plugin Game Feature<br/>instances 1..n"]
        WN["Monde ...<br/>(communauté, plus tard)"]
    end

    PC -- "HTTPS : compte, avatar, liste des mondes" --> API
    VR -.-> API
    MOB -.-> API

    PC <-- "réseau Unreal : actions / réplication" --> W1
    VR <-.-> W1
    MOB <-.-> W2

    API --- ID & AV & INV & ECO & REG
    ID & AV & PERS & INV & ECO & REG --- DB

    W1 -- "vérif. joueur, inventaire, transactions" --> INT
    W2 --> INT
    WN -.-> INT
    INT --- ID & PERS & INV & ECO & REG
```

Les flèches en pointillés représentent ce qui viendra plus tard.

### Organisation interne du projet Unreal

```mermaid
flowchart TB
    subgraph Projet["Projet Unreal (unreal/)"]
        GAME["Module principal<br/>(démarrage, menus, connexion au noyau)"]
        CORE["Plugin socle EudoriaCore<br/>identité, avatar, voyage entre mondes"]
        WA["Game Feature World_A"]
        WB["Game Feature World_B"]
    end
    GAME --> CORE
    WA --> CORE
    WB --> CORE
    WA -. "dépendance interdite" .- WB
```

## 5. Sécurité (vue d'ensemble)
- Tout le trafic externe vers le noyau est chiffré (HTTPS).
- Les jetons d'identité sont signés et expirent ; le noyau peut les révoquer.
- Les serveurs de monde ont leur **propre identité** auprès du noyau, avec des droits limités (un monde ne peut pas créer de monnaie à volonté).
- Les appels des clients aux serveurs de monde sont **validés côté serveur** (aucune confiance dans les valeurs envoyées).
- Les opérations économiques sont atomiques et journalisées.
- Les secrets ne sont jamais dans le code (variables d'environnement).

## 6. Structure du dépôt (proposition, à valider avec les ADR)

Un seul dépôt git (« monorepo ») :

```
eudoria/
├── docs/                    # vision, roadmap, architecture, ADR, progrès, installation
├── core/                    # le noyau (service web C#/.NET)
├── shared/
│   └── api/                 # contrats OpenAPI du noyau (public + interne), en YAML
├── unreal/                  # le projet Unreal (client + serveurs de monde)
│   ├── Source/              # code C++ du module principal
│   ├── Plugins/
│   │   ├── EudoriaCore/     # plugin socle (C++)
│   │   └── GameFeatures/    # un plugin par monde (World_<Nom>)
│   ├── Config/              # réglages en texte (.ini), profils de qualité par plateforme
│   ├── Content/             # assets binaires (Git LFS, ADR-0005)
│   └── Scripts/             # scripts Python d'automatisation de l'éditeur
└── tools/                   # scripts de dev, environnement local (Docker Compose…)
```

Le détail interne de chaque dossier sera défini à l'étape qui le crée.

## 7. Ce qui n'est pas encore décidé
- Validation des ADR 0001 à 0005.
- Hébergeur du dépôt distant (quotas Git LFS) → ADR futur (voir ADR-0005).
- Hébergement des serveurs (Linux), déploiement, observabilité → avant toute mise en ligne publique.
- Mécanisme de distribution du contenu des mondes (paquets par monde) → étape 6.
