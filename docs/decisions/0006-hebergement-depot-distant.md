# ADR-0006 — Hébergement du dépôt distant

- **Statut :** Accepté (validé par le porteur de projet le 2026-11-24 : GitHub, dépôt privé)
- **Date :** 2026-11-24
- **Liée à :** ADR-0005 (fichiers binaires, Git LFS)

## Contexte

Le dépôt n'existe aujourd'hui **que sur le PC du porteur de projet** (`E:`). Une panne de disque, un vol ou
une erreur de manipulation ferait tout perdre. Il faut un **dépôt distant privé** qui serve à la fois de
**sauvegarde hors site** et de référence pour le travail.

Exigences :
- Dépôt **privé**.
- **Git LFS** avec un quota et un prix raisonnables : avec des assets photoréalistes (MetaHuman, textures 4K,
  Nanite), le volume LFS atteindra probablement **plusieurs dizaines puis centaines de Go**. Rappel : chaque
  nouvelle version d'un fichier LFS compte en entier.
- **Verrouillage LFS** (`git lfs lock`, ADR-0005) supporté par le serveur.
- **Simplicité** pour une personne seule, et bonne intégration avec les outils de Claude (ligne de commande, revues, intégration continue plus tard).

## Options étudiées

Chiffres relevés le 2026-11-24 sur les pages officielles (liens en bas). Les prix évoluent : à revérifier au moment de souscrire.

| | **GitHub** | **GitLab.com** | **Azure DevOps** | **Bitbucket Cloud** | **Auto-hébergé (Gitea / Forgejo)** |
|---|---|---|---|---|---|
| Dépôts privés gratuits | Illimités | Oui | Illimités | Illimités | Illimités |
| Utilisateurs gratuits | Illimités | 5 par groupe privé | 5 | 5 | Illimités |
| LFS gratuit | 10 Gio stockage + 10 Gio de téléchargement / mois | 10 Gio **par projet, dépôt + LFS confondus** | **Gratuit, aucun quota LFS indiqué** (dépôt conseillé < 250 Go) | 1 Go | Limité par ton disque |
| Prix au-delà | 5 $/mois pour 50 Go de stockage + 50 Go de téléchargement (facturation à l'usage) | 5 $/mois par 10 Gio (page contradictoire sur les offres concernées) | Aucun prix LFS indiqué | 10 $/mois par 100 Go | Matériel ou serveur loué + électricité |
| Verrouillage LFS | **Supporté — testé avec succès le 2026-11-24** (lock, locks, verify, unlock) | **Supporté sur l'offre gratuite** (confirmé par la doc) | **Incertain** : la doc évoque le verrouillage, mais un utilisateur rapporte « ne supporte pas l'API de verrouillage » — **à tester** | **Non supporté** (confirmé par la doc) | Supporté (API présente dans le code de Gitea/Forgejo, à activer côté serveur) |
| Taille max. d'un fichier | 2 Go (offre gratuite) | non relevé | non relevé | 5 Go | configurable |
| Autres limites | — | Projet en lecture seule si quota dépassé | **Pas de SSH** pour les dépôts avec LFS (HTTPS uniquement) | Pas de transfert de dépôt LFS | Sauvegarde, mises à jour et sécurité à ta charge |
| Simplicité / outils | ★★★ (le plus répandu ; outils `gh`, Actions, intégration Claude Code) | ★★ | ★★ (interface plus « entreprise ») | ★★ | ★ |

### Estimation de coût LFS (ordre de grandeur, hors taxes)
| Volume LFS | GitHub | GitLab.com | Azure DevOps | Bitbucket |
|---|---|---|---|---|
| 50 Go | ~5 $/mois | ~20 $/mois | 0 $ (si confirmé) | 10 $/mois |
| 200 Go | ~20 $/mois | ~95 $/mois | 0 $ (si confirmé) | 20 $/mois |

(Calculs à partir des tarifs ci-dessus ; GitHub compte aussi le téléchargement, faible pour une personne seule.)

### Ce que chaque option implique
- **GitHub** — le plus simple et le mieux outillé ; payant dès que les assets dépassent 10 Gio, à un prix modéré. Point à vérifier : le verrouillage.
- **GitLab.com** — verrouillage confirmé, mais le quota gratuit inclut le dépôt et le LFS ensemble, et le stockage supplémentaire est le plus cher.
- **Azure DevOps** — LFS gratuit sans quota annoncé, ce qui est très avantageux pour des assets lourds ; mais verrouillage incertain, HTTPS obligatoire, interface moins simple.
- **Bitbucket** — **écarté** : pas de verrouillage LFS.
- **Auto-hébergé** — maîtrise totale et pas de quota, mais un serveur à administrer et sécuriser ; et s'il est **chez toi**, il ne protège pas d'un sinistre (ce n'est plus une sauvegarde hors site). Pertinent plus tard sur un serveur loué, pas maintenant.

## Recommandation

**GitHub (dépôt privé)**, avec un **test préalable du verrouillage LFS** (dépôt de test, `git lfs lock` sur un fichier).
C'est la solution la plus simple, la mieux intégrée aux outils que j'utilise, avec un coût prévisible
(~5 $ par tranche de 50 Go) quand les assets grossiront.

**Alternative :** **Azure DevOps** si tu veux éviter tout coût LFS — à condition que le test de verrouillage
réussisse. Les deux tests (GitHub et Azure DevOps) peuvent être faits sur des dépôts de test vides avant de décider.

**Dans tous les cas, garder une deuxième sauvegarde indépendante** (règle « 3-2-1 » : 3 copies, 2 supports,
1 hors site) : par exemple une copie complète du dépôt (avec les fichiers LFS) sur un disque externe, mise à
jour régulièrement. Un hébergeur n'est pas une sauvegarde à lui seul (erreur de manipulation, compte bloqué).

## Conséquences si cette option est validée
- **Toi** : créer le compte et le dépôt privé (je ne crée aucun compte), puis m'autoriser à y envoyer le dépôt.
- Configurer l'authentification (HTTPS avec le gestionnaire d'identifiants de Git ; jamais de jeton dans le code).
- Activer Git LFS dans le dépôt (`git lfs install`), envoyer `main`, tester le verrouillage.
- Surveiller le quota LFS ; définir un budget mensuel maximum chez l'hébergeur pour éviter les surprises.
- Ajouter une procédure de sauvegarde secondaire dans `docs/SETUP.md`.

## Sources
- GitHub : [facturation Git LFS](https://docs.github.com/en/billing/managing-billing-for-your-products/managing-billing-for-git-large-file-storage/about-billing-for-git-large-file-storage), [tarifs](https://github.com/pricing), [à propos de Git LFS](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-git-large-file-storage)
- GitLab : [quotas de stockage](https://docs.gitlab.com/user/storage_usage_quotas/), [tarifs](https://about.gitlab.com/pricing/), [verrouillage de fichiers](https://docs.gitlab.com/user/project/file_lock/)
- Azure DevOps : [gros fichiers / LFS](https://learn.microsoft.com/en-us/azure/devops/repos/git/manage-large-files), [limites](https://learn.microsoft.com/en-us/azure/devops/repos/git/limits), [tarifs](https://azure.microsoft.com/en-us/pricing/details/devops/azure-devops-services/), [témoignage sur le verrouillage](https://gist.github.com/joeskeen/f27c22e5ec179c8773237049ac1a6aac)
- Bitbucket : [tarifs](https://www.atlassian.com/software/bitbucket/pricing), [limites LFS](https://support.atlassian.com/bitbucket-cloud/docs/current-limitations-for-git-lfs-with-bitbucket/)
- Git LFS : [verrouillage](https://github.com/git-lfs/git-lfs/wiki/File-Locking)
