# Étape 1.7 — Test de rendu : scène après connexion, Lumen + Nanite, mesures sur le poste

> Statut : **✅ plan validé par le porteur le 2026-11-27** (D1 à D8 = recommandations, avec les deux compléments du
> § 11.1). Partie A en cours.

## 1. Vérifications faites avant d'écrire ce plan (2026-11-27)

| Point | Constat |
|---|---|
| Plugin Fab | Le lanceur l'enregistre (« Fab UE Plugin », `FabPlugin_5.8`, 109 Mo) dans **`C:\Program Files\Epic Games\UE_5.8`, dossier inexistant**. Aucun dossier `Fab` dans `E:\Epic Games\UE_5.8\Engine\Plugins` → **le plugin n'est pas réellement installé**. |
| Bug connu Fab + 5.8.4 | Rapport sur le forum d'Epic (29/10/2026, sans réponse) : avec **Fab 0.0.22** installé sur **UE 5.8.4 du lanceur**, la compilation d'un projet **C++** échoue avant notre code (`RulesError` : type `FabToolset` introuvable). Désinstaller Fab → la compilation repasse. **C'est notre cas exact** (projet C++, `Build.bat`) → risque n° 1 du plan (§ 4.3). |
| Cache de Fab | Par défaut `%LOCALAPPDATA%\Temp\FabLibrary` (sur C:, celui qu'on a supprimé le 2026-11-24, 6,1 Go). Réglage annoncé par des utilisateurs : commande `Fab.ShowSettings` dans l'éditeur (chemin du cache) ; **non confirmé par Epic**, à vérifier sur le plugin réel. |
| Cache du lanceur (« Vault ») | `C:\ProgramData\Epic\EpicGamesLauncher\VaultCache` : vide (0 Mo). |
| Megascans | Selon deux articles (5 et 13/11/2026), **toute la bibliothèque Megascans est redevenue gratuite sur Fab** depuis le 05/11/2026, sous la **licence standard de Fab** (usage commercial permis ; **redistribution des fichiers seuls interdite**, dépôt public interdit ; dépôt privé : non traité). Sources secondaires : **la ligne « licence » de chaque fiche sera vérifiée** avant tout ajout (§ 5.2). |
| Personnage | Le moteur fournit le mannequin (Manny/Quinn) dans `Templates\TemplateResources\High\Characters` (**125 Mo**, 128 fichiers) : maillage, matériaux, animations, et un espace de mélange **`BS_Idle_Walk_Run`**. Il contient aussi **`ABP_Unarmed`** (Animation Blueprint) et des **Control Rig** : ce sont des Blueprints **avec logique**, que notre test `Eudoria.Editor.NoBlueprintLogic` refuserait (il scanne tout `/Game`). |
| Carte graphique | **AMD Radeon RX 9060 XT, 16 Go** de mémoire vidéo (lu dans le registre), pilote du 18/11/2026. Écran **1920 × 1080 à 240 Hz**. |
| Disques | C: **66,7 Go** libres ; E: **833 Go** libres. |
| Projet | DirectX 12, SM6, ombres virtuelles activées, éclairage statique désactivé ; Lumen **non écrit explicitement** dans `DefaultEngine.ini`. Carte actuelle : `/Engine/Maps/Entry` (vide). La session (`UEudoriaSessionSubsystem`) est un sous-système de `GameInstance` → **elle survit au changement de carte**. |
| Outils de mesure | Unreal Insights présent (`Engine\Binaries\Win64\UnrealInsights.exe`). |

## 2. Objectif et résultat visible

Après « Bienvenue, <pseudo> », un bouton **« Entrer dans la scène de test »** charge une petite scène photoréaliste
(une clairière rocheuse d'environ 100 m × 100 m, éclairage de jour, Lumen + Nanite). Le joueur y marche et court à la
troisième personne. Une **mesure automatique** (une touche) parcourt des points de vue fixes à plusieurs niveaux de
qualité et écrit les résultats ; ils sont recopiés dans `docs/perf/`. Conclusion : **l'ADR-0003 (Unreal) est-il validé
sur ton poste ?**

Ce que la 1.7 **ne fait pas** : pas de serveur de monde, pas de ticket d'entrée (la scène est locale, l'étape 3 s'en
chargera), pas de multijoueur, pas d'avatar personnalisé (étape 2), pas de VR/mobile (fin d'étape 4).

## 3. Découpage proposé (une tâche à la fois)

- **Partie A — Préparatifs** : plugin Fab réinstallé sur E: **et compilation vérifiée**, cache sur E:, liste des assets
  (licence, taille) **validée par toi**, contenu en place hors du dépôt. → **point de contrôle avec toi.**
- **Partie B — La scène et le personnage** (C++ + script Python de construction de la carte) + tests.
- **Partie C — Mesures** : outil de mesure, version empaquetée, campagne de mesures, `docs/perf/`, conclusion ADR-0003.

## 4. Plugin Fab : réinstallation sur E: et cache sur E: (point 1)

### 4.1 Réinstallation
Le lanceur croit le plugin installé dans un dossier qui n'existe pas. Procédure (à la main, § 10, M1) :
1. Lanceur → Bibliothèque → carte « 5.8 » → **Plugins installés** : retirer « Fab » s'il y figure.
2. Réinstaller le plugin Fab (« Installer sur le moteur » → 5.8). Le lanceur connaît désormais le moteur sur E:
   (`LauncherInstalled.dat` le confirme) → destination attendue `E:\Epic Games\UE_5.8\Engine\Plugins\Fab`.
3. Je vérifie : dossier présent, version du plugin (`Fab.uplugin`), manifeste du lanceur pointant vers E:.
4. *Si le lanceur refuse* (il croit le plugin déjà là) : je te proposerai de **renommer** (pas supprimer) le manifeste
   périmé `FabPlugin_5.8` dans `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests`, lanceur fermé, avec ton accord.

### 4.2 Cache sur E:
- Dans l'éditeur : `Fab.ShowSettings` → chemin du cache = **`E:\Dev\FabCache`** (et chemin de bibliothèque s'il existe).
  Si ce réglage est stocké dans un `.ini` de l'utilisateur, je le lirai pour le documenter dans `docs/SETUP.md`.
- Lanceur → Paramètres → **emplacement du cache « Vault »** → `E:\Dev\EpicVaultCache` (s'il existe encore).
- **Preuve** : après le premier téléchargement, `%LOCALAPPDATA%\Temp\FabLibrary` ne doit pas exister ou rester vide,
  et `E:\Dev\FabCache` doit avoir grossi. Si le réglage ne marche pas (des utilisateurs le signalent) : on **vide le
  cache après chaque import** (ce n'est qu'une copie du téléchargement ; l'asset vit ensuite dans le projet).
- Je ne propose **pas** de déplacer la variable `TEMP` de Windows (effet sur tous les programmes).

### 4.3 Risque : le bug `FabToolset` (compilation C++ cassée)
Juste après l'installation, **avant tout le reste**, je relance `tools/dev/build-unreal.ps1` :
- **compile** → on continue avec le plugin dans le projet Eudoria ;
- **échoue** comme sur le forum → j'essaie `{"Name": "Fab", "Enabled": false}` dans `Eudoria.uproject` ; si ça ne
  suffit pas, **plan de repli (décision D4)** : un **projet séparé sans C++** `E:\Dev\FabImport` (Blueprint seul,
  donc jamais compilé par UBT) où le plugin Fab importe les assets, puis **migration** (« Migrer… » de l'éditeur) vers
  le contenu d'Eudoria ; et on désinstalle Fab du moteur si la compilation d'Eudoria reste cassée.
  Autre repli : téléchargement depuis fab.com (FBX/glTF) et **import par script Python** avec Nanite activé.

## 5. Les assets : choix, licences, tailles, et surtout où ils vivent (point 2)

### 5.1 Ce dont la scène a besoin
| Rôle | Source | Licence | Taille |
|---|---|---|---|
| Personnage (Manny) + animations de base | Moteur (`TemplateResources`), **sélection** de fichiers | Contenu du moteur, licence d'Unreal Engine | ~40–60 Mo estimés (sans `ABP_Unarmed` ni Control Rig) |
| Sol (surface scannée) | Megascans (Fab) | Licence standard Fab (à vérifier par fiche) | à mesurer |
| 4 à 6 rochers / falaises (Nanite) | Megascans | idem | à mesurer |
| 3 à 5 petits éléments (débris, souches, plantes basses) | Megascans | idem | à mesurer |
| *Optionnel* 1 arbre | Megaplants (UE 5.7+) | à vérifier (statut gratuit non confirmé) | à mesurer |
| Ciel, soleil, nuages, brouillard | Acteurs du moteur | — | 0 |

**Budget proposé : 5 Go au plus sur E:** pour les assets, plus le cache de données dérivées (Nanite, textures
compressées) qui grossira de quelques Go dans `E:\Dev\UnrealCache`. Les tailles des Megascans dépendent du niveau de
qualité choisi au téléchargement (je ne les connais pas avant de voir les fiches) : **on remplit le tableau ensemble
(nom, lien, licence lue sur la fiche, niveau de qualité, taille) et tu le valides avant tout téléchargement.** Ce
tableau devient `docs/render-test/assets.md` (commité : c'est la « recette » pour tout retrouver).

### 5.2 Vérification des licences
Pour chaque asset : licence lue **sur sa fiche Fab** le jour de l'ajout, notée avec la date. On écarte tout asset dont la
licence n'est pas la licence standard (ou CC0), et tout asset qui apporte un **Blueprint avec logique** (il ferait
échouer notre test de règle, et il n'a rien à faire dans une scène de rendu).

### 5.3 Où ils vivent : **recommandation — hors du dépôt, re-téléchargeables** (décision D1)
| | Commités (Git LFS) | **Hors du dépôt (recommandé)** |
|---|---|---|
| Quota GitHub (10 Gio de stockage, 10 Gio de téléchargement / mois) | Consommé **définitivement** : l'historique garde chaque version, et supprimer un fichier LFS ne rend pas le quota (il faut supprimer le dépôt). Chaque clonage complet consomme aussi le téléchargement. | 0 |
| Licence | « Redistribution seule interdite » ; **le texte de la licence (fab.com/eula, lu le 2026-11-27) autorise le partage avec des collaborateurs « par le biais d'un référentiel privé »** → permis, contrairement à ce que laissaient croire les articles | Aucune question |
| Reproductibilité | Immédiate | Par la recette `docs/render-test/assets.md` + un script qui vérifie la présence et la taille |
| Usage | Scène de **test**, jetable | Adapté |

Concrètement :
- dossiers ignorés par git : le dossier d'import de Fab (`unreal/Content/Fab/`, chemin exact confirmé en partie A),
  `unreal/Content/Characters/` (copie du moteur) et `unreal/Content/RenderTest/` (la carte, § 6.1) ;
- `tools/dev/setup-render-test.ps1` : copie la sélection du mannequin depuis le moteur (aucun téléchargement), vérifie
  la présence des assets Fab listés, et dit clairement ce qui manque ;
- **réserve de quota** : on garde le LFS pour le contenu qui nous appartient (étape 2 et suivantes).

## 6. La scène (point 3)

### 6.1 La carte : construite par un script Python (décision D2)
`unreal/Scripts/build_render_test_map.py` (lancé par l'éditeur en ligne de commande) crée
`/Game/RenderTest/L_RenderTest` : sol, rochers et débris placés de façon **déterministe** (graine fixe, quelques
centaines d'instances pour solliciter Nanite), une arche ou un surplomb rocheux (rebonds de lumière pour Lumen),
`DirectionalLight` (soleil) + `SkyAtmosphere` + `SkyLight` (capture en temps réel) + `VolumetricCloud` +
`ExponentialHeightFog` + un `PostProcessVolume` global, un `PlayerStart`, et **5 points de vue de mesure** (acteurs
`TargetPoint` étiquetés `Eudoria.Benchmark.1` à `.5`). Le mode de jeu de la carte pointe vers notre classe C++.
Recommandation : carte **non commitée** (elle dépend d'assets hors dépôt, et le script la refait à l'identique) ;
alternative : la commiter en LFS (verrouillée), quelques centaines de Ko.

### 6.2 Réglages de rendu (`Config/DefaultEngine.ini`, en texte)
Écrits explicitement : `r.DynamicGlobalIlluminationMethod=1` (Lumen), `r.ReflectionMethod=1` (reflets Lumen),
`r.GenerateMeshDistanceFields=True` (Lumen logiciel), `r.Shadow.Virtual.Enable=1` (déjà là), Nanite activé sur les
maillages au moment de l'import. Lancé en **Lumen logiciel** ; le **Lumen matériel** (lancer de rayons de la RX 9060 XT)
est mesuré en plus si tu le veux (décision D6), par une option de ligne de commande, sans changer le réglage commité.

### 6.3 Le personnage à la troisième personne, en C++ (sans aucun Blueprint)
Dans `Source/Eudoria/…/RenderTest/` (le module du client ; l'avatar partagé entre mondes viendra dans `EudoriaCore` à
l'étape 2 ou 3 — pas d'anticipation ici) :
- `AEudoriaRenderTestGameMode`, `AEudoriaRenderTestPlayerController`, `AEudoriaThirdPersonCharacter` :
  bras de caméra (`SpringArm`) + caméra, déplacement relatif à la caméra, marche / course (Maj), saut (Espace).
- **Entrées créées en C++** (actions et contexte d'Enhanced Input construits au démarrage) → **aucun `.uasset`
  d'entrée**. Touches : **ZQSD** (clavier français) **et flèches**, souris pour la caméra, manette (décision D8).
- **Animation sans graphe (décision D3)** : mode « nœud unique » d'Unreal piloté par le C++ : l'espace de mélange
  `BS_Idle_Walk_Run` reçoit la vitesse à chaque image (`SetBlendSpacePosition`) ; en l'air, le C++ passe aux animations
  de saut / chute, puis revient. On ne copie **ni `ABP_Unarmed` ni les Control Rig** → le test « aucune logique
  Blueprint » reste vrai sur tout `/Game`. Perte acceptée : pas d'ajustement des pieds au sol (IK).
- Chemins des assets (maillage, espace de mélange, animations, carte) dans un réglage texte
  (`Config/DefaultGame.ini`, classe de réglages C++), comme `CoreBaseUrl`.

### 6.4 Ce qui se passe après « Bienvenue »
- Écran « Bienvenue » : nouveau bouton **« Entrer dans la scène de test »**. Si la carte ou les assets manquent, le
  bouton est grisé avec le message « Scène de test absente : lancer `tools/dev/setup-render-test.ps1` ».
- Clic → chargement de la carte ; la session reste ouverte (sous-système de `GameInstance`).
- **Échap** → petit menu en C++ (UMG) : « Reprendre », « Retour à l'accueil » (revient à « Bienvenue, <pseudo> »),
  « Se déconnecter » (déconnexion au noyau puis écran « Se connecter »).
- Si le jeton expire pendant la visite : rien ne se passe dans la scène (aucun appel au noyau) ; le renouvellement
  existant agira au prochain appel (retour à l'accueil, déconnexion).

## 7. Méthode de mesure (point 4)

### 7.1 Conditions
- **Version empaquetée « Development »** (cuisinée, hors éditeur) dans `E:\Dev\Builds\Eudoria` (décision D7) :
  l'éditeur fausse la RAM et le processeur. Les commandes de mesure restent disponibles en « Development ».
- Plein écran **1920 × 1080** (ton écran), **synchro verticale coupée**, **pas de limite d'images/s**, TSR (anti-crénelage
  par défaut d'Unreal) au pourcentage de résolution de chaque niveau.
- Poste au calme : rien d'autre d'ouvert (ni jeu, ni navigateur lourd) ; **noyau et Docker lancés** (il faut se connecter)
  et comptés dans la RAM du système ; mesure de la RAM « au repos » avant de lancer le client.
- Un **premier passage de chauffe** est jeté (compilation des shaders et des états du GPU) ; les **saccades** de ce
  premier passage sont notées à part : c'est ce que vivra un joueur la première fois.

### 7.2 Le parcours de mesure (automatique, reproductible)
Touche **F8** dans la scène (ou option `-EudoriaBenchmark` au lancement, après connexion) : pour chaque niveau de
qualité, la caméra va sur chacun des 5 points de vue (10 s de stabilisation, 20 s de mesure), puis fait un trajet
continu entre eux (streaming, Nanite en mouvement). Pendant ce temps :
- **dans le jeu (C++)** : temps de chaque image, et par image les temps **Game** (logique), **Draw** (préparation du
  rendu, processeur) et **GPU** (mêmes chiffres que `stat unit`) ; résumé par segment : images/s moyennes et médianes,
  **1 % les plus lentes**, 99e centile du temps d'image, nombre d'images > 33 ms et > 100 ms. Écrit en JSON dans
  `Saved/Benchmarks/`. Le **profileur CSV** d'Unreal tourne en parallèle (contrôle croisé, fichiers bruts) ;
- **hors du jeu (`tools/dev/measure-render.ps1`)**, une mesure par seconde : RAM du processus (privée et « working
  set »), **RAM libre du système**, **mémoire vidéo dédiée** utilisée par le processus et par la carte (compteurs GPU de
  Windows, indépendants d'Unreal), utilisation du fichier d'échange ;
- **une capture `ProfileGPU` par niveau** : répartition du temps GPU (Lumen, Nanite, ombres virtuelles, TSR, brouillard),
  pour savoir *quoi* alléger si besoin. Unreal Insights seulement pour comprendre une anomalie.

### 7.3 Niveaux de qualité testés
| Passage | Niveau (scalabilité d'Unreal) | Pourquoi |
|---|---|---|
| 1 | **Haute** (2) | **Critère de la ROADMAP** |
| 2 | **Épique** (3) | Marge restante, cible « photoréaliste » de l'ADR-0003 |
| 3 | **Moyenne** (1) | Lumen y est désactivé par Unreal : coût réel de Lumen par différence, et piste pour les configurations modestes |
| 4 | Cinématique (4) | Une seule fois, pour information |
| 5 | Haute + **Lumen matériel** | Optionnel (D6), relancement avec le lancer de rayons activé |

Chaque passage est fait **2 fois** (on garde les deux pour juger la stabilité).

### 7.4 « Fluide en Haute » : seuils proposés (décision D5)
- images/s **médianes ≥ 60** et **1 % les plus lentes ≥ 45 i/s** ; 99e centile ≤ 25 ms ;
- après la chauffe, **aucune image > 100 ms** ;
- **mémoire vidéo ≤ 12 Go** (marge sur 16) ; **RAM libre du système ≥ 2 Go** et pas d'emballement du fichier
  d'échange (tes 16 Go sont la vraie limite probable).

### 7.5 Résultats
`docs/perf/2026-xx-xx-1.7-rendu.md` : conditions (pilote, version d'Unreal, réglages), tableau par niveau, captures
d'écran de la scène, répartition GPU, conclusion. Les fichiers bruts restent hors du dépôt (`E:\Dev\Perf`). Puis
**ADR-0003** : ajout d'une section « Validation sur le poste du porteur » avec les chiffres (ou, si ce n'est pas fluide,
un point avec toi sur la suite — l'ADR n'est pas changé de moi-même).

## 8. Tests automatisés
- **Unitaires C++** : choix de l'animation selon vitesse / état en l'air ; construction des entrées (ZQSD + flèches
  présentes) ; lecture et validation des réglages (chemins vides refusés) ; statistiques du parcours de mesure
  (moyenne, médiane, centiles, comptage des longues images) sur des séries connues.
- **Intégration Unreal** (contenu présent) : la carte se charge, le personnage apparaît et est contrôlé, la caméra est
  attachée, une entrée de déplacement le fait avancer, Échap ouvre le menu, « Retour à l'accueil » rouvre « Bienvenue ».
  Contenu absent → test **marqué « ignoré » avec le message clair**, pas « réussi » en silence.
- **Règle Blueprint** : `Eudoria.Editor.NoBlueprintLogic` repasse avec **tout** le contenu importé.
- **Script** : `setup-render-test.ps1` testé à la main (contenu absent, partiel, complet), comme `start-db.ps1`.
- Les 38 tests Unreal et 364 tests .NET existants repassent ; 0 avertissement dans notre code ; quelques
  **mutations** (touche retirée, centile faussé, garde « contenu absent » retirée) doivent faire échouer un test.

## 9. Fichiers créés / modifiés (prévision)
- `unreal/Source/Eudoria/{Public,Private}/RenderTest/` : mode de jeu, contrôleur, personnage, animation, menu Échap,
  parcours de mesure, réglages ; `Private/Tests/` correspondants ; `UI/EudoriaWelcomeScreen.*` (bouton).
- `unreal/Scripts/build_render_test_map.py` ; `unreal/Config/DefaultEngine.ini`, `DefaultGame.ini`.
- `unreal/Eudoria.uproject` (plugin Fab : selon § 4.3).
- `tools/dev/setup-render-test.ps1`, `tools/dev/measure-render.ps1`, `tools/dev/package-client.ps1`.
- `.gitignore` (dossiers du § 5.3) ; `docs/render-test/assets.md` ; `docs/perf/…` ; `docs/SETUP.md` (Fab, cache,
  contenu de test) ; `docs/ARCHITECTURE.md` ; `docs/decisions/0003-…` (validation) ; `docs/PROGRESS.md` ; `docs/ROADMAP.md`.

## 10. Ce que tu feras à la main (point 5)
| # | Où | Action | Quand |
|---|---|---|---|
| M1 | Lanceur Epic | Retirer puis réinstaller le plugin Fab sur le moteur 5.8 (§ 4.1) — je te guide pas à pas | Début de A |
| M2 | Lanceur Epic | Paramètres → cache « Vault » vers `E:\Dev\EpicVaultCache` (si l'option existe) | Début de A |
| M3 | Éditeur | `Fab.ShowSettings` → cache vers `E:\Dev\FabCache` (si je ne peux pas l'écrire moi-même dans un `.ini`) | Début de A |
| M4 | fab.com (ton compte) | Lire la licence et **ajouter à ta bibliothèque** chaque asset du tableau validé (accepter des conditions, c'est à toi) | A, après validation du tableau |
| M5 | Éditeur, fenêtre Fab | Importer chaque asset dans le projet au niveau de qualité indiqué (ou dans `E:\Dev\FabImport` si repli) | A |
| M6 | Client | Te connecter, entrer dans la scène, **regarder** (rendu, défauts visibles), marcher/courir/sauter, puis lancer la mesure (F8) **sans toucher le PC** pendant ~15 min | C |
| M7 | — | Me dire ton ressenti (fluidité, saccades, beauté), puis valider (ou non) la 1.7 | Fin |

Je fais tout le reste : scripts, code, carte, empaquetage, analyse des mesures, documents.

## 11. Décisions à prendre
| # | Question | Ma recommandation |
|---|---|---|
| D1 | Où vivent les assets Fab et le mannequin ? | **Hors du dépôt**, re-téléchargeables (recette + script) |
| D2 | La carte de test : générée par script et non commitée, ou commitée en LFS ? | **Générée, non commitée** |
| D3 | Animation : C++ en « nœud unique » (sans IK), ou `ABP_Unarmed` du moteur avec une exception dans la règle Blueprint ? | **C++ nœud unique** (la règle reste sans exception) |
| D4 | Si le bug `FabToolset` casse notre compilation : projet d'import séparé sans C++, ou téléchargement fab.com + import Python ? | **Projet d'import séparé** d'abord, Python en second repli |
| D5 | Seuils de « fluide en Haute » (§ 7.4) | Ceux du § 7.4 |
| D6 | Mesurer aussi le Lumen matériel ? | **Oui**, en passage supplémentaire |
| D7 | Mesures dans une version empaquetée Development (sur E:) ? | **Oui** |
| D8 | Touches : ZQSD + flèches + manette ? | **Oui** |

### 11.1 Décisions du porteur (2026-11-27)
D1 à D8 : **recommandations retenues**. Deux compléments :
1. **D3** : animation en C++ « nœud unique » acceptée **pour ce test** ; point ouvert ajouté à l'ADR-0003 et à la
   ROADMAP (étape 2, évaluation de MetaHuman) : les Animation Blueprints et Control Rig sont l'outil standard d'un
   avatar réaliste ; la règle « aucune logique Blueprint » sera précisée pour eux, avec une décision du porteur à
   l'étape 2.
2. **Mesures** : campagne 1 avec **16 Go** comme prévu ; **campagne 2 identique** (même scène, même parcours, mêmes
   niveaux) **après le passage à 32 Go**, pour comparer. Les deux sont décrites dans `docs/perf/README.md` ; le
   parcours et les scripts de mesure doivent donc rester **rejouables tels quels** (version de la carte et du parcours
   notée dans chaque résultat).

## 12. Risques
- **Bug Fab / compilation C++** (§ 4.3) : vérifié en tout premier, replis prévus.
- **16 Go de RAM** : la première compilation des shaders (Lumen, Nanite, SM6) lance beaucoup de processus de
  compilation ; risque de manque de mémoire. Parade : limiter leur nombre dans un réglage **utilisateur** (pas commité),
  comme on l'a fait pour UBT ; éditeur seul ouvert pendant ce temps. L'empaquetage repasse par là (long : à lancer en
  arrière-plan).
- **Dépendances cachées du mannequin** : le maillage peut référencer des assets non copiés (ex. Control Rig). Le
  script vérifie les références après copie et s'arrête avec la liste des manquants.
- **Réglage du cache Fab inefficace** : parade du § 4.2 (vider après import), contrôle de C: avant/après.
- **Tailles Megascans inconnues** : rien n'est téléchargé avant ton accord sur le tableau.
- **Statut « gratuit » des Megascans** : sources secondaires ; vérifié sur chaque fiche.
- **Mesures bruitées** (comme en 1.4) : 2 passages, PC au calme, RAM de base notée.

## 13. Commits prévus (petits)
1. `chore(unreal): ignore local render-test content` (+ `docs/render-test/assets.md` vide, à remplir) — partie A
2. `feat(tools): add setup-render-test.ps1 (mannequin copy, content check)`
3. `docs(setup): document the Fab plugin, its cache and the test content` → **point de contrôle A**
4. `feat(unreal): third-person character, input and animation in C++` (+ tests)
5. `feat(unreal): build the render test map with a Python script` (+ réglages Lumen)
6. `feat(unreal): enter the render test from the welcome screen; Escape menu` (+ tests)
7. `feat(unreal): add the render benchmark` (+ tests des statistiques)
8. `feat(tools): add measure-render.ps1 and package-client.ps1`
9. `docs(perf): render test results on the owner's PC` + `docs(adr): ADR-0003 validation` + PROGRESS / ROADMAP / ARCHITECTURE

## 14. Dépendances
Aucune nouvelle librairie. Outils et contenus : plugin **Fab** d'Epic (déjà prévu par la ROADMAP), **assets Megascans**
gratuits (licence par fiche), **contenu du moteur** (mannequin). Rien d'installé sans ton accord (M1).

## Sources consultées
- Forum Epic, bug `FabToolset` avec Fab 0.0.22 / UE 5.8.4 :
  https://forums.unrealengine.com/t/ue-5-8-4-launcher-install-c-build-fails-with-ruleserror-fabtoolset-after-fab-plugin-0-0-22-is-installed/2846815
- Megascans de nouveau gratuits : https://digitalproduction.com/2026/11/13/quixel-megascans-free-again/ et
  https://indietooling.com/posts/megascans-free-fab-again (licence Fab : https://www.fab.com/eula)
- Cache du plugin Fab (retours d'utilisateurs) : https://forums.unrealengine.com/t/can-i-change-where-the-plugin-saves-downloads/2146686
