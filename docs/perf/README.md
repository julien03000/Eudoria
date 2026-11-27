# Mesures de performances

Résultats de mesures faites sur le poste du porteur. Les fichiers bruts (CSV, JSON, captures `ProfileGPU`) restent hors
du dépôt, dans `E:\Dev\Perf` ; ici, seulement les résumés.

## Test de rendu (sous-étape 1.7)

Scène `L_RenderTest`, parcours de mesure automatique (5 points de vue + trajet), version empaquetée « Development »,
1920 × 1080, synchro verticale coupée. Méthode complète : `docs/plans/etape-01-7.md`, § 7.

Les deux campagnes doivent être **identiques** (même version de la carte, du parcours, des scripts et des niveaux de
qualité) ; seule la mémoire vive change. Toute différence de conditions (pilote graphique, version d'Unreal, Windows)
est notée dans le résultat.

| Campagne | Poste | Statut | Résultat |
|---|---|---|---|
| 1 | Ryzen 9 3900X, **16 Go** de RAM, RX 9060 XT 16 Go | ⚪ À faire (1.7, partie C) | — |
| 2 | Même poste, **32 Go** de RAM | ⚪ À faire après le passage à 32 Go (prérequis de l'étape 3) | — |

Niveaux de qualité mesurés à chaque campagne : Haute, Épique, Moyenne, Cinématique, Haute + Lumen matériel ; chaque
passage 2 fois, après un passage de chauffe.
