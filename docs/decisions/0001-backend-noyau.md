# ADR-0001 — Langage et framework du backend (le noyau)

- **Statut :** Proposé
- **Date :** 2026-11-24
- **Liée à :** ADR-0002 (base de données), ADR-0003 (moteur client), ADR-0004 (réseau)

## Contexte

Le noyau gère l'identité, les avatars, la persistance, l'inventaire, l'économie et le registre des mondes
(voir `docs/ARCHITECTURE.md`). C'est la partie la plus critique pour la **sécurité** et la **cohérence des données**,
et celle qui vivra le plus longtemps.

Critères retenus :
- Porteur de projet **seul**, niveau **intermédiaire**, qui doit pouvoir **lire et comprendre** le code.
- Claude est le développeur principal : le langage doit être très bien connu, bien outillé, avec des erreurs détectées tôt.
- **Vision long terme** : stabilité, performance, sécurité, maintenabilité sur des années.
- Tout en **fichiers texte**.
- Bonus important : pouvoir **partager du code** (contrats d'API, protocole, règles de validation) avec le client et les serveurs de monde.

## Options étudiées

### Option A — C# avec ASP.NET Core (.NET 10, version LTS)
**Avantages**
- Typage statique fort : beaucoup d'erreurs sont détectées dès la compilation, ce qui sécurise le code écrit par l'IA et sa relecture par toi.
- Très performant (parmi les frameworks web les plus rapides), stable, version LTS supportée sur plusieurs années.
- Écosystème complet et officiel : authentification, validation, tests (xUnit), accès aux données avec migrations (Entity Framework Core), OpenAPI.
- **Même langage que le client si l'on choisit Godot en C# ou Unity** (ADR-0003) : les contrats et le protocole réseau peuvent être écrits une fois, dans `shared/`, et utilisés partout.
- Open source, multi-plateforme (Windows, Linux, conteneurs).

**Inconvénients**
- Un peu plus verbeux que TypeScript ou Python.
- Écosystème moins « à la mode » dans les startups web, donc un peu moins d'exemples récents pour certains sujets.
- Nécessite d'installer le SDK .NET.

### Option B — TypeScript avec Node.js (framework NestJS ou Fastify)
**Avantages**
- Langage très répandu, énormément de ressources et de librairies.
- Démarrage rapide, itérations très courtes.
- TypeScript apporte un typage (mais seulement à la compilation, pas à l'exécution).

**Inconvénients**
- Aucun partage de code avec un client Godot/Unity/Unreal (sauf client web).
- Typage moins strict : les données externes doivent être revalidées partout (librairies supplémentaires).
- Écosystème npm très fragmenté : beaucoup de dépendances, mises à jour fréquentes, risques sur la chaîne d'approvisionnement.
- Performances correctes, mais en dessous de .NET ou Go pour les traitements lourds.

### Option C — Go
**Avantages**
- Très simple, très performant, excellent pour les services réseau et la montée en charge.
- Compile en un seul exécutable : déploiement très facile.
- Langage stable, peu de « magie ».

**Inconvénients**
- Aucun partage de code avec les moteurs de jeu envisagés.
- Plus de code à écrire soi-même (moins de framework « tout compris ») pour l'authentification, la validation, l'accès aux données.
- Gestion des erreurs répétitive, ce qui allonge le code à relire.

## Recommandation

**Option A — C# / ASP.NET Core sur .NET 10 (LTS).**

La raison principale : avec l'option recommandée dans l'ADR-0003 (Godot en C#), **tout EUDORIA est écrit dans un
seul langage** (noyau, serveurs de monde, client). Pour une personne seule, c'est un gros avantage : un seul
langage à apprendre et à lire, des contrats écrits une fois dans `shared/`, un seul outillage de tests.
Le typage fort de C# protège aussi l'économie et l'identité, où une erreur coûte cher.

Si l'ADR-0003 aboutit à un autre choix qu'un moteur en C#, cette recommandation doit être réexaminée.

## Conséquences si cette option est validée
- Installer le SDK .NET 10 sur le poste de développement.
- Le noyau sera un **monolithe modulaire** ASP.NET Core (un module par service : Identité, Avatars, etc.).
- Tests : xUnit ; formatage et analyse : `dotnet format` et les analyseurs intégrés.
- Le `.gitignore` recevra les règles .NET (`bin/`, `obj/`, etc.).
- Mise à jour de la section « Stack technique » de `CLAUDE.md`.
