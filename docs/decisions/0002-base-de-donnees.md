# ADR-0002 — Base de données principale

- **Statut :** Proposé
- **Date :** 2026-11-24
- **Liée à :** ADR-0001 (backend)

## Contexte

Le noyau doit stocker durablement : comptes, avatars, inventaires, transactions économiques, registre des mondes,
puis relations sociales. Plusieurs de ces données exigent une **cohérence stricte** :
une monnaie ne doit jamais être dupliquée ni perdue, un objet ne doit jamais appartenir à deux joueurs à la fois.

Critères retenus :
- **Fiabilité et cohérence** avant tout (économie, inventaire).
- Capacité à grandir sur le long terme.
- Simple à faire tourner en local pour une personne seule.
- Schéma et migrations en **fichiers texte**, versionnés dans git.
- Open source, sans dépendance forte à un fournisseur.

## Options étudiées

### Option A — PostgreSQL
**Avantages**
- Base relationnelle de référence : **transactions ACID** (une opération se fait entièrement ou pas du tout), contraintes d'unicité, clés étrangères. Idéal pour l'économie et l'inventaire.
- Très flexible : gère aussi des données semi-structurées (colonnes JSONB), pratique pour l'apparence des avatars ou des données propres à un monde.
- Extrêmement mature, documenté, gratuit, open source ; très bien supporté par tous les langages (en .NET via Entity Framework Core).
- Disponible chez tous les hébergeurs (offres gérées), facile à lancer en local avec Docker.
- Chemins de croissance connus : réplicas en lecture, partitionnement, puis solutions distribuées compatibles PostgreSQL si nécessaire.

**Inconvénients**
- Demande de concevoir un schéma (tables, relations) et de gérer des migrations.
- La montée en charge horizontale « massive » demande du travail d'architecture (mais très lointaine pour nous).

### Option B — MongoDB (base orientée documents)
**Avantages**
- Pas de schéma imposé : on stocke des documents JSON, rapide pour prototyper.
- Montée en charge horizontale native (sharding).

**Inconvénients**
- Les transactions sur plusieurs documents existent mais sont moins naturelles : risque plus élevé d'incohérence pour l'économie.
- L'absence de schéma imposé déplace la rigueur dans le code : plus d'erreurs possibles à long terme.
- Licence SSPL (pas une licence open source au sens strict), ce qui limite certaines options d'hébergement.

### Option C — CockroachDB (base SQL distribuée, compatible PostgreSQL)
**Avantages**
- Conçue dès le départ pour être répartie sur plusieurs serveurs et régions, avec une forte cohérence.
- Compatible en grande partie avec le langage de PostgreSQL.

**Inconvénients**
- Beaucoup plus complexe à faire tourner et à comprendre ; surdimensionnée pour un prototype.
- Licence devenue propriétaire (gratuite sous conditions), ce qui crée une dépendance au fournisseur.
- Compatibilité PostgreSQL incomplète (certaines fonctionnalités manquent).

## Recommandation

**Option A — PostgreSQL** (version stable actuelle, 18).

C'est le choix le plus sûr pour les données qui ne doivent jamais être fausses (identité, économie, inventaire),
tout en restant simple à utiliser seul. Sa compatibilité avec des solutions distribuées laisse une porte de sortie
si EUDORIA devient très grand un jour, sans avoir à payer cette complexité aujourd'hui.

Note : d'autres stockages viendront probablement **en complément** plus tard (cache type Redis/Valkey pour les
sessions et la présence, stockage de fichiers pour les contenus créés). Ils feront chacun l'objet d'un ADR au moment voulu.

## Conséquences si cette option est validée
- PostgreSQL tournera en local via **Docker** (Docker Desktop à installer) et un fichier `docker-compose.yml` dans le dépôt.
- Le schéma sera géré par des **migrations versionnées** (texte, dans git).
- Les identifiants de connexion seront dans `.env` (jamais commités), avec un `.env.example`.
- Les tests d'intégration utiliseront une vraie base PostgreSQL jetable.
