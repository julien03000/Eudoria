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

**Statut : ✅ liste validée par le porteur le 2026-11-27, licence « Professionnelle » ; les 13 assets importés le
2026-11-27 (4 300,9 Mo dans `unreal/Content/Fab/`, dont 1,6 Mo de matériaux partagés créés par le plugin).**
Vérifié par `setup-render-test.ps1 -VerifyReferences` : aucune référence obligatoire manquante, **Nanite activé sur les
24 maillages** (le fond, la falaise, la fougère en 9 variantes, les débris en 6 pièces). Qualité **mélangée** : 4 assets
en « High » (textures 4K, maillage « tier 1 »), 9 en « Raw » (textures **8K**, maillage « tier 0 ») — voir § 3.

Thème : **clairière de forêt nordique** (une seule famille de matériaux, mousse et granit), adossée à une falaise qui fait
surplomb (rebonds de lumière pour Lumen). Tous les assets sont de **Quixel Megascans**, **gratuits** (prix 0 pour les deux
niveaux de licence, vérifié sur chaque fiche le 2026-11-27), sous la **licence standard de Fab**.

**Licence (texte lu sur fab.com/eula le 2026-11-27)** : usage commercial permis, hors Unreal aussi ; **revente ou
redistribution du contenu seul interdite** ; partage permis avec des collaborateurs du projet, y compris par un
**référentiel privé**. Deux niveaux aux **mêmes droits** : « Personnelle » (seulement si moins de 100 000 USD de revenus
bruts sur les 12 derniers mois au moment de l'ajout) et « Professionnelle » (sans condition). Les deux sont à 0 € ici →
**recommandation : « Professionnelle »** (aucune condition à justifier plus tard).

**Tailles** : Fab n'affiche pas la taille du téléchargement sur les fiches ; elle dépend du niveau de qualité choisi dans
la fenêtre Fab de l'éditeur. Elle sera **mesurée à l'import** et notée ici. Budget : **5 Go au plus**. Si le premier asset
importé dépasse ~400 Mo, on s'arrête et on choisit un niveau plus bas.

| # | Rôle | Nom sur Fab | Lien | Taille réelle (l × L × h) | Licence (lue le) | Qualité | Taille sur disque | Dossier dans le projet |
|---|---|---|---|---|---|---|---|---|
| 1 | Sol (matériau répété) | Mossy Forest Floor | [fiche](https://www.fab.com/listings/31b154e8-dea5-49c8-9b29-6362fe1a4a8d) | matériau, 8192 px/m | Standard (2026-11-27) | High (4K) | 137,7 Mo | `Fab/Megascans/Surfaces/Mossy_Forest_Floor_vfylbge` |
| 2 | Sol (relief, plaque) | Nordic Mossy Ground | [fiche](https://www.fab.com/listings/ec83e70d-1c43-4530-a541-cff3e621b96a) | 6,95 × 9,3 × 0,12 m | Standard (2026-11-27) | Raw (8K) | 479,1 Mo | `Fab/Megascans/3D/Nordic_Mossy_Ground_vi2ldcomw` |
| 3 | Falaise en surplomb (fond, Lumen) | Granite Cliff | [fiche](https://www.fab.com/listings/9253e224-5522-4052-82e2-2447e62ff80f) | 8,04 × 29,49 × 16,27 m | Standard (2026-11-27) | Raw (8K) | 457,5 Mo | `Fab/Megascans/3D/Granite_Cliff_rivav` |
| 4 | Paroi rocheuse | Mossy Rock Face | [fiche](https://www.fab.com/listings/6b4525bd-afe0-47d9-84e7-cdf4f5739f71) | 1,81 × 3,52 × 1,86 m | Standard (2026-11-27) | Raw (8K) | 671,9 Mo | `Fab/Megascans/3D/Mossy_Rock_Face_ti2udfqda` |
| 5 | Rocher | Mossy Rock Formation | [fiche](https://www.fab.com/listings/3acf7891-dfe0-45da-b210-813fdc3d1f77) | 1,63 × 3,28 × 1,41 m | Standard (2026-11-27) | High (4K) | 104,8 Mo | `Fab/Megascans/3D/Mossy_Rock_Formation_plFhY` |
| 6 | Rocher (maillage fermé) | Mossy Forest Boulder | [fiche](https://www.fab.com/listings/8cf6b5fa-fc74-4e00-aa37-8cb239bf9782) | 1,98 × 1,98 × 1,29 m | Standard (2026-11-27) | High (4K) | 98,0 Mo | `Fab/Megascans/3D/Mossy_Forest_Boulder_wfstcdnaw` |
| 7 | Corniche rocheuse | Nordic Forest Ledge Rock Medium | [fiche](https://www.fab.com/listings/034fcd08-2e37-407f-9e32-26291a8d18a3) | 1,39 × 3,39 × 0,93 m | Standard (2026-11-27) | Raw (8K) | 369,4 Mo | `Fab/Megascans/3D/Nordic_Forest_Ledge_Rock_Medium_xislecsqx` |
| 8 | Groupe de pierres (instancié en nombre, Nanite) | Nordic Forest Cluster Medium | [fiche](https://www.fab.com/listings/b10a5b93-0ef7-4eb0-acef-4d5353f0220b) | 1,63 × 2,15 × 1,1 m | Standard (2026-11-27) | High (4K) | 100,2 Mo | `Fab/Megascans/3D/Nordic_Forest_Cluster_Medium_xisgcic` |
| 9 | Racines sur corniche | Nordic Forest Ledge Root Large | [fiche](https://www.fab.com/listings/65aeab37-9ed4-43d4-9665-471c5d6be77a) | 2,49 × 7,87 × 1,66 m | Standard (2026-11-27) | Raw (8K) | 381,3 Mo | `Fab/Megascans/3D/Nordic_Forest_Ledge_Root_Large_xj1odjt` |
| 10 | Tronc couché | Nordic Forest Tree Log Large | [fiche](https://www.fab.com/listings/c11bff3d-4576-432e-a7cf-e4da029ffd8b) | 0,71 m (diamètre) | Standard (2026-11-27) | Raw (8K) | 369,5 Mo | `Fab/Megascans/3D/Nordic_Forest_Tree_Log_Large_xiclaj1r` |
| 11 | Souche | Spruce Tree Stump | [fiche](https://www.fab.com/listings/ed8af4af-861e-44b8-8c47-e442ee2d33e1) | 1,23 m | Standard (2026-11-27) | Raw (8K) | 454,2 Mo | `Fab/Megascans/3D/Spruce_Tree_Stump_tjuxbgfda` |
| 12 | Fougère (feuillage, transparence) | Lady Fern | [fiche](https://www.fab.com/listings/3eead001-af7f-4b65-afcc-89c368f089bc) | — | Standard (2026-11-27) | Raw (8K) | 130,0 Mo | `Fab/Megascans/Plants/Lady_Fern_wdvlditia` |
| 13 | Débris (branches, petits éléments) | Tree Debris Pack | [fiche](https://www.fab.com/listings/807c5914-bf82-4199-a5f6-ac6618395b39) | petits éléments | Standard (2026-11-27) | Raw (8K) | 537,7 Mo | `Fab/Megascans/3D/Tree_Debris_Pack_vdqjfjhiw` |

Écartés : un **arbre** (Megaplants, UE 5.7+ : option du plan, plus lourd et statut gratuit non vérifié → pas pour ce
premier test) ; les falaises côtières (autre thème).

Formats proposés par les fiches : FBX, glTF/GLB, USDZ, ZBrush (pas de format « Unreal » direct) : c'est la fenêtre Fab de
l'éditeur qui les importe (avec Nanite). Les fiches marquées « UEFN (référence uniquement) » ont aussi ce niveau : il ne
concerne pas notre usage.

## 3. Mesures de l'import (2026-11-27)

| Mesure | Valeur |
|---|---|
| Contenu Fab dans le projet (`unreal/Content/Fab/`) | **4 300,9 Mo**, 169 fichiers (budget du plan : 5 Go) |
| Mannequin (`unreal/Content/Characters/`) | 52,5 Mo, 35 fichiers |
| Cache du plugin Fab (`E:\Dev\FabCache`, copie des téléchargements) | **6 732 Mo**, 107 fichiers — sur E: comme voulu ; vidable (`Fab.ClearCache` dans l'éditeur) |
| `%LOCALAPPDATA%\Temp\FabLibrary` (C:) | **absent** : rien n'y a été écrit |
| Cache « Vault » du lanceur (`E:\Dev\EpicVaultCache`) | 0,1 Mo |
| Fichier d'échange (sur E:) | pic de 14,7 Go pendant la session : la RAM de 16 Go a été très sollicitée |

Les tailles affichées par la vérification sont celles des fichiers `.uasset` (textures compressées au format source) ;
la mémoire vidéo réelle sera mesurée en partie C.
