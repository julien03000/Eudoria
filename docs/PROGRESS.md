# EUDORIA — Suivi d'avancement

> À mettre à jour à la fin de chaque session.

**Dernière mise à jour :** 2026-11-24
**Étape en cours :** Phase 0 — Fondations du projet (branche `main`)

## ✅ Fait
- Git installé sur le poste (2.56.1), dépôt initialisé sur `main`.
- `.gitignore` générique (secrets, OS, éditeurs, builds, logs) et `.gitattributes` (fins de ligne LF).
- `docs/ROADMAP.md` : étapes 0 à 10, étape 1 (identité numérique) détaillée.
- `docs/ARCHITECTURE.md` : architecture globale noyau / mondes / clients, communications, schéma Mermaid, proposition de structure du dépôt.
- ADR rédigés (statut « Proposé ») dans `docs/decisions/` :
  - ADR-0001 — Backend du noyau → recommandation : C# / ASP.NET Core (.NET 10 LTS)
  - ADR-0002 — Base de données → recommandation : PostgreSQL
  - ADR-0003 — Moteur client → recommandation : Godot 4 avec C#
  - ADR-0004 — Réseau multijoueur → recommandation : serveurs de monde Godot headless + protocole EUDORIA versionné sur ENet

## 🟡 En cours
- Validation des 4 ADR par le porteur de projet.

## ⚠️ Problèmes connus / points d'attention
- Les 4 ADR sont liés : refuser l'ADR-0001 ou l'ADR-0003 oblige à revoir les autres.
- C# sur mobile avec Godot : support plus récent que GDScript → test d'export Android à prévoir tôt (étape 2 ou 3).
- Le `.gitignore` est générique : règles .NET / Godot à ajouter une fois la stack validée.
- Outils restant à installer après validation : SDK .NET 10, Godot 4 édition .NET, Docker Desktop.

## ⏭️ Prochaine étape exacte
1. Le porteur relit et valide (ou amende) les ADR 0001 à 0004.
2. Passer les ADR validés au statut « Accepté », mettre à jour `CLAUDE.md` (sections « Stack technique » et « Structure du dépôt ») et compléter le `.gitignore`.
3. Installer les outils de la stack validée.
4. Clôturer la Phase 0, puis créer la branche `etape-01-identite` et proposer le plan de la sous-étape 1.1
   (squelette du noyau + environnement local + route « santé »).
