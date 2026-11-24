# ADR-0005 — Gestion des fichiers binaires (Git LFS, verrouillage)

- **Statut :** Accepté (validé par le porteur de projet le 2026-11-24)
- **Date :** 2026-11-24
- **Liée à :** ADR-0003 (moteur client)

## Contexte

Avec Unreal (ADR-0003), une grande partie du projet est **binaire** : niveaux (`.umap`), assets (`.uasset`),
textures, modèles 3D, sons, vidéos. Ces fichiers posent trois problèmes avec git :
1. **Taille** : git garde chaque version de chaque fichier ; un dépôt avec des assets binaires grossit très vite et devient lent.
2. **Fusion impossible** : deux modifications d'un même `.uasset` ne peuvent pas être fusionnées ; l'une écrase l'autre.
3. **Fichiers générés** : Unreal produit beaucoup de fichiers temporaires (compilation, caches) qui ne doivent jamais être versionnés.

Même seul, le problème 2 existe : entre toi dans l'éditeur et moi via des scripts, ou entre deux machines.

## Options étudiées

### Option A — Git + Git LFS avec verrouillage (retenue)
Git LFS remplace les gros fichiers dans git par de petits fichiers « pointeurs » texte et stocke leur contenu
sur un serveur à part ([git-lfs.com](https://git-lfs.com/)). Le **verrouillage** marque certains types de
fichiers comme « verrouillables » : ils sont en lecture seule localement tant qu'on n'a pas pris le verrou
(`git lfs lock <fichier>`), ce qui empêche deux personnes de modifier le même asset en parallèle
([wiki File Locking](https://github.com/git-lfs/git-lfs/wiki/File-Locking)).

**Avantages**
- Garde git, déjà en place, gratuit, que je maîtrise parfaitement ; le code reste dans un dépôt normal.
- Git LFS est déjà fourni avec Git sur ce PC (version 3.8.1) : rien à installer.
- L'éditeur Unreal propose une intégration Git (fournisseur de contrôle de source), avec des plugins communautaires gérant le verrouillage LFS.

**Inconvénients**
- Le verrouillage n'est efficace que si **le serveur distant implémente l'API de verrouillage** ; sinon, Git LFS n'empêche pas l'envoi des modifications (le wiki le précise). À vérifier sur l'hébergeur choisi.
- **Quotas d'hébergement** : sur GitHub, les comptes gratuits ont **10 Gio de stockage et 10 Gio de bande passante par mois** pour LFS ; chaque nouvelle version d'un fichier compte en entier ([facturation GitHub LFS](https://docs.github.com/en/billing/managing-billing-for-your-products/managing-billing-for-git-large-file-storage/about-billing-for-git-large-file-storage)). Avec des assets photoréalistes (MetaHuman, textures 4K), ce quota sera atteint vite → hébergement payant ou alternatif à prévoir.
- Fichiers en lecture seule tant qu'ils ne sont pas verrouillés : il faut prendre l'habitude.

### Option B — Perforce (Helix Core)
Le standard de l'industrie pour Unreal, très bien intégré à l'éditeur, conçu pour les gros binaires et le verrouillage.

**Avantages :** gestion native des gros fichiers et des verrous, intégration Unreal de référence.
**Inconvénients :** un second système de version à apprendre et à administrer (serveur à héberger), moins
adapté au code et à la documentation ; Claude y est moins efficace qu'avec git. Prématuré pour une personne seule.

### Option C — Git sans LFS (assets hors dépôt)
Stocker les assets ailleurs (dossier partagé, stockage cloud) et ne versionner que le code.
**Inconvénient majeur :** plus d'historique ni de cohérence entre le code et les assets qu'il référence. Écarté.

## Décision proposée

**Option A — Git + Git LFS avec verrouillage.** Perforce pourra être réévalué si l'équipe grandit ou si le
volume d'assets devient ingérable.

### Règles
1. **Tous les binaires passent par LFS**, et les assets Unreal (`.uasset`, `.umap`) sont **verrouillables**.
2. Avant de modifier un asset dans l'éditeur : le verrouiller ; après le commit et l'envoi : le déverrouiller.
3. **Jamais** de fichiers générés dans git (voir `.gitignore`).
4. Les sources lourdes (fichiers de travail Blender, Photoshop, etc.) vont dans un dossier dédié, aussi en LFS, uniquement si elles sont utiles à garder.
5. Les packs d'assets téléchargés (Fab, MetaHuman) sont importés **avec parcimonie** : uniquement ce qui est utilisé, pour ne pas épuiser le quota LFS.

## Fichiers mis à jour dans le dépôt (avec cet ADR)
- **`.gitignore`** : ajout des dossiers générés par Unreal (`Binaries/`, `Intermediate/`, `Saved/`, `DerivedDataCache/`, `.vs/`, fichiers de solution générés…), avec une exception pour les binaires de plugins tiers si besoin plus tard.
- **`.gitattributes`** : assets Unreal en LFS **verrouillables** ; textures, modèles, sons, vidéos et archives en LFS.

Ces règles sont sans effet tant qu'aucun fichier Unreal n'existe dans le dépôt ; elles évitent simplement
d'oublier une protection au moment où le projet Unreal sera créé.

## Points ouverts (à décider avant le premier envoi d'assets)
- **Hébergeur distant** du dépôt : GitHub (quota LFS gratuit faible, verrouillage supporté à confirmer), autre hébergeur, ou serveur auto-hébergé. Traité par l'ADR-0006.
- **Activation de Git LFS dans le dépôt** (`git lfs install`, simple configuration) au moment de créer le projet Unreal (étape 1).
- **Intégration du verrouillage dans l'éditeur Unreal** : plugin Git avec support LFS à choisir et tester à l'étape 1.
