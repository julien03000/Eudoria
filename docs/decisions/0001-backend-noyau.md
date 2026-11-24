# ADR-0001 — Langage et framework du backend (le noyau)

- **Statut :** Proposé
- **Date :** 2026-11-24 (révision 2 — réévaluée après le passage à Unreal Engine, ADR-0003)
- **Liée à :** ADR-0002 (base de données), ADR-0003 (moteur client), ADR-0004 (réseau)

## Contexte

Le noyau gère l'identité, les avatars, la persistance, l'inventaire, l'économie et le registre des mondes
(voir `docs/ARCHITECTURE.md`). C'est la partie la plus critique pour la **sécurité** et la **cohérence des
données**, et celle qui vivra le plus longtemps.

**Ce qui a changé :** la première version recommandait C#/.NET surtout pour **partager du code** avec un client
Godot en C#. Avec Unreal (C++), ce partage disparaît : client et serveurs de monde sont en C++, le noyau sera
dans un autre langage quoi qu'il arrive. Le lien entre eux devient un **contrat d'API** décrit en **OpenAPI**.

### Contrats d'API en OpenAPI (quel que soit le langage)
- Le contrat (`shared/api/core-v1.yaml`, texte) est écrit **avant** le code : routes, données, erreurs, version.
- Il sert de source unique pour : la documentation, des tests qui vérifient que le noyau respecte le contrat,
  et, si possible, la génération du code client.
- Côté Unreal, le code d'appel HTTP en C++ peut être généré (OpenAPI Generator propose un générateur
  `cpp-ue4`, communautaire, à évaluer à l'étape 1) ou écrit à la main sur une petite surface.
- Deux contrats distincts : **API publique** (clients) et **API interne** (serveurs de monde).

Critères :
- Porteur seul, niveau intermédiaire, qui doit pouvoir **lire et comprendre** le code.
- Claude, développeur principal : langage bien connu, bien outillé, erreurs détectées tôt.
- Sécurité, fiabilité, maintenabilité sur des années.
- Bon support d'OpenAPI et de PostgreSQL (ADR-0002).
- Pas de partage de code avec le client : ce critère ne compte plus.

## Options étudiées

### Option A — C# avec ASP.NET Core (.NET 10, version LTS)
**Avantages**
- Framework « tout compris » et officiel : authentification et jetons, hachage de mots de passe, limitation de débit, validation, injection de dépendances, tests (xUnit).
- **Migrations de base de données** intégrées (Entity Framework Core), schéma versionné en texte.
- Génération du document OpenAPI intégrée ; outils de validation de contrat disponibles.
- Typage statique fort, très performant, version LTS supportée plusieurs années.
- Syntaxe orientée objet **proche du C++** (classes, interfaces) : un seul style de lecture pour toi entre le client et le noyau.
- Le SDK .NET 10 est de toute façon requis par Visual Studio 2026 pour Unreal 5.8 ([configuration Visual Studio](https://dev.epicgames.com/documentation/en-us/unreal-engine/setting-up-visual-studio-development-environment-for-cplusplus-projects-in-unreal-engine)) : **aucun outil supplémentaire à installer**.

**Inconvénients**
- Beaucoup de fonctionnalités « magiques » (injection de dépendances, conventions) qu'il faut connaître pour lire le code.
- Programmes un peu plus gourmands en mémoire que Go.

### Option B — Go
**Avantages**
- Langage très simple, volontairement limité : le code se lit de haut en bas, peu de « magie ».
- Très performant, faible consommation mémoire, démarrage instantané.
- Un seul exécutable à déployer ; excellent pour multiplier les services plus tard.
- Bon outillage OpenAPI (génération de serveur à partir du contrat, ex. `oapi-codegen`), ce qui colle bien à une approche « contrat d'abord ».

**Inconvénients**
- Moins de choses fournies : authentification, migrations, validation reposent sur plusieurs petites librairies à choisir et assembler (plus de décisions, plus de code).
- Gestion des erreurs répétitive (`if err != nil`) : code plus long à relire.
- Un langage de plus à installer et à apprendre, sans lien avec le reste du projet.

### Option C — TypeScript / Node.js (pour mémoire)
Très répandu, rapide à démarrer, bon support OpenAPI. Écarté : typage uniquement à la compilation,
écosystème de dépendances très fragmenté (risques de sécurité sur la chaîne d'approvisionnement),
moins adapté à un noyau critique sur le long terme.

## Comparaison synthétique

| Critère | C# / .NET 10 | Go |
|---|---|---|
| Sécurité « clé en main » (auth, hachage, limitation) | ★★★ intégré | ★★ à assembler |
| Migrations de base | ★★★ intégré (EF Core) | ★★ librairie externe |
| OpenAPI | ★★★ génération du document intégrée | ★★★ génération du code depuis le contrat |
| Lisibilité pour un niveau intermédiaire | ★★ (conventions à connaître) | ★★★ (très explicite) |
| Performance / mémoire | ★★★ / ★★ | ★★★ / ★★★ |
| Outils à installer | Aucun en plus (déjà requis par VS) | Go en plus |
| Maturité long terme | ★★★ (LTS, Microsoft) | ★★★ (Google, très stable) |

## Recommandation

**Option A — C# / ASP.NET Core sur .NET 10 (LTS)**, avec une approche **« contrat OpenAPI d'abord »**.

Le partage de code n'est plus un argument, mais .NET garde l'avantage sur ce qui compte le plus pour le noyau
d'une personne seule : la sécurité (authentification, hachage, limitation) et les migrations sont fournies et
maintenues par Microsoft au lieu d'être assemblées à partir de librairies tierces. Le SDK est déjà requis
par la chaîne Unreal, donc aucun outil en plus. Go est une **alternative sérieuse** et resterait un bon choix
si tu privilégies la simplicité de lecture du code.

Pour limiter la « magie » de .NET : style explicite (Minimal APIs, pas d'abstractions inutiles), et le contrat
OpenAPI comme référence lisible de ce que fait le noyau.

## Conséquences si cette option est validée
- Le noyau vivra dans `core/` : un **monolithe modulaire** ASP.NET Core, un module par service.
- Contrats dans `shared/api/` (OpenAPI, YAML) ; tests de conformité au contrat.
- Tests : xUnit ; formatage et analyse : `dotnet format` + analyseurs intégrés.
- Règles .NET (`bin/`, `obj/`) ajoutées au `.gitignore` au démarrage de l'étape 1.
- Mise à jour de la section « Stack technique » de `CLAUDE.md` après validation.
