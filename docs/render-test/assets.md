# Contenu de la scène de test de rendu (sous-étape 1.7)

Ce contenu **n'est pas dans le dépôt** (décision D1 du plan `docs/plans/etape-01-7.md`) : quota Git LFS préservé, et
la licence de Fab interdit de redistribuer les fichiers seuls. Cette page est la **recette** pour le retrouver ;
`tools/dev/setup-render-test.ps1` le remet en place ou dit ce qui manque (liste exacte :
`tools/dev/render-test-content.json`).

## 1. Mannequin (contenu du moteur, copié automatiquement)

Source : `E:\Epic Games\UE_5.8\Templates\TemplateResources\High\Characters\Content` (fourni avec Unreal Engine 5.8,
licence d'Unreal Engine). Destination : `unreal/Content/Characters/` (`/Game/Characters/…`, le même chemin que le
modèle « Third Person » d'Epic, pour que les références internes restent valides).

On copie **une sélection** : maillage de Manny, squelette, physique, matériaux, textures, espace de mélange
`BS_Idle_Walk_Run` et ses animations, saut / chute / réception. On **ne copie pas** `ABP_Unarmed` (Animation Blueprint)
ni les Control Rig : ce sont des Blueprints avec logique (décision D3 ; point ouvert de l'ADR-0003).

## 2. Assets Fab (ajoutés à la main par le porteur)

*À remplir au point de contrôle A, puis validé par le porteur avant tout téléchargement.* Pour chaque asset : licence lue
**sur sa fiche** le jour de l'ajout.

| Rôle | Nom sur Fab | Lien | Licence (lue le) | Qualité téléchargée | Taille | Dossier dans le projet |
|---|---|---|---|---|---|---|
| — | — | — | — | — | — | — |
