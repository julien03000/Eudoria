# EUDORIA — Instructions pour Claude Code

## Le projet
EUDORIA est un univers numérique persistant composé de nombreux mondes interconnectés
(inspiration : l'OASIS de *Ready Player One*). Vision complète : `docs/VISION.md`.
Toute décision doit servir cette vision. En cas de doute sur l'interprétation, demande.

## Contexte du porteur de projet
- Je porte le projet **seul**, avec toi comme développeur principal.
- Niveau **intermédiaire** : je lis et comprends du code, mais je ne suis pas expert.
  Explique tes choix simplement, en français, sans jargon inutile.
- Plateforme cible du premier prototype : **PC**. VR et mobile viendront plus tard,
  mais l'architecture doit les prévoir dès le départ.

## Documents de référence
Au début de chaque session, lis **toujours** :
- `docs/PROGRESS.md` — où on en est, et la prochaine étape exacte
- `docs/ROADMAP.md` — les phases du projet et l'étape en cours

Lis selon le besoin :
- `docs/VISION.md` — la vision (ne jamais la modifier sans demande explicite)
- `docs/ARCHITECTURE.md` — l'architecture technique actuelle
- `docs/decisions/` — les décisions techniques (ADR), un fichier par décision

## Règles de travail
1. **Une tâche à la fois.** Si une demande est trop grosse, découpe-la et propose le découpage avant de coder.
2. **Plan avant code.** Pour toute tâche non triviale : présente un plan (fichiers touchés, approche, risques) et attends mon « OK ».
3. **Pas de changement d'architecture ou de techno sans ADR validé.** Si tu penses qu'un choix existant est mauvais, dis-le et propose un ADR — ne le change pas de toi-même.
4. **Rester dans l'étape en cours.** Pas de fonctionnalités « au cas où » ni d'anticipation sur les étapes suivantes.
5. **Chaque étape doit produire quelque chose de réellement fonctionnel et testé.**
6. **Dépendances :** n'ajoute une librairie que si elle est justifiée ; dis-moi laquelle et pourquoi.
7. **Secrets :** jamais de mot de passe, clé ou token dans le code. Utilise `.env` (jamais commité) et un `.env.example`.
8. **Si tu es bloqué ou hésites entre deux options : arrête-toi et demande.** N'improvise pas, ne contourne pas un problème en silence.
9. **Ne supprime ni ne réécris massivement du code existant** sans me l'avoir annoncé.

## Définition de « terminé »
Une tâche n'est terminée que si :
- le code a été lancé et fonctionne ;
- des tests existent et passent ;
- il n'y a pas d'erreur de compilation ni de lint ;
- `docs/ARCHITECTURE.md` est à jour si la structure a changé ;
- `docs/PROGRESS.md` est à jour ;
- un commit git a été fait.

## Git
- Une branche par étape de la roadmap (ex. `etape-01-identite`), fusionnée dans `main` quand l'étape est validée par moi.
- Commits petits et fréquents, format : `type(zone): description`
  (types : `feat`, `fix`, `docs`, `refactor`, `test`, `chore`).
- Jamais de `push --force`, `rebase` ou suppression de branche sans ma demande.

## Fin de session
Avant de terminer une session, mets à jour `docs/PROGRESS.md` :
ce qui est fait, ce qui est en cours, les problèmes connus, et **la prochaine étape exacte**.

## Principes d'architecture (issus de la vision)
- Le **noyau** (identité, comptes, avatars, persistance, inventaire, économie, registre des mondes)
  est séparé des **mondes**.
- Les mondes sont des modules indépendants qui communiquent avec le noyau via des **API versionnées**.
- L'identité et l'avatar sont uniques et suivent l'utilisateur dans tous les mondes.
- Le **serveur fait autorité** : le client n'est jamais cru sur parole (anti-triche, économie).
- Sécurité, stabilité, performance et maintenabilité passent avant la vitesse du prototype.
- Penser multi-plateforme (PC, VR, mobile) dès la conception, même si on ne développe que PC au début.

## Stack technique
À définir — voir `docs/decisions/`. Mets à jour cette section quand un ADR est validé.

## Structure du dépôt
À définir en Phase 0. Mets à jour cette section quand elle est validée.

## Langue
- Échanges avec moi et documentation : **français**.
- Code, noms de variables, fonctions et commentaires techniques : **anglais**.
