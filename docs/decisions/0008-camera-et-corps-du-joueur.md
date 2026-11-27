# ADR-0008 — Caméra et corps du joueur

- **Statut :** Accepté (décision du porteur, 2026-11-27)
- **Date :** 2026-11-27
- **Liée à :** ADR-0003 (client Unreal ; point ouvert sur les Animation Blueprints et Control Rig), ADR-0004
  (réseau, serveur qui fait autorité) ; ROADMAP étapes 2 (avatar) et 4 (déplacements)

## Contexte

EUDORIA vise le PC d'abord, puis la VR et le mobile (ADR-0003). En VR, la vue est forcément celle des yeux du
joueur. La question était de savoir si le PC suivrait la même logique, ou garderait une caméra à la troisième
personne (derrière l'épaule), comme dans la scène de test de la sous-étape 1.7.

L'identité et l'avatar sont au cœur de la vision : le joueur doit se sentir **dans** son avatar, le même partout.

## Décision (du porteur)

1. **Caméra à la première personne, sur PC comme en VR.** Une seule manière de voir le monde, quelle que soit la
   plateforme.
2. **Le joueur voit son propre corps** en regardant vers le bas : corps entier visible, **ombre comprise** ; la
   **tête est masquée pour lui seul** (sinon la caméra serait à l'intérieur). Les autres joueurs voient l'avatar
   complet, tête comprise.
3. **La scène de test de la 1.7 reste à la troisième personne** : elle sert à mesurer le rendu, pas à fixer la vue du
   jeu. Le code de la 1.7 n'est pas modifié.

## Conséquences

- **Caméra attachée à la tête de l'avatar** (au niveau des yeux), et non à un bras de caméra derrière le personnage.
  Sur PC, la souris oriente la tête et le corps suit ; en VR, le casque donne directement la position et
  l'orientation de la tête.
- **Masquer la tête pour le seul joueur local** : avec les outils d'Unreal (masquer des os ou une partie du maillage
  pour le propriétaire, « owner no see »), tout en gardant **l'ombre de la tête** (ombre projetée par un maillage non
  visible). À vérifier sur l'avatar choisi à l'étape 2 (MetaHuman ou autre), qui peut être fait de plusieurs
  morceaux (tête, corps, vêtements).
- **VR : IK du corps entier.** Seuls la tête et les mains sont connus (casque et manettes) : le reste du corps
  (bras, buste, jambes) doit être **déduit** par cinématique inverse (IK) pour que le corps vu en baissant les yeux
  soit crédible. C'est un travail d'animation important, et un risque de confort (un corps qui ne suit pas
  « juste » gêne plus en VR que sur écran).
- **Lien avec le point ouvert de l'ADR-0003** (Animation Blueprints et Control Rig) : un corps vu à la première
  personne, avec IK des pieds et du corps entier, **rend ces outils pratiquement indispensables**. La précision de la
  règle « aucune logique Blueprint » (par exemple : logique dans une classe C++ `AnimInstance`, graphe d'animation
  autorisé pour assembler les poses) devient une **condition** de l'étape 2 ; décision du porteur à prendre au début
  de l'étape 2.
- **Étape 2 (avatar)** : l'évaluation de MetaHuman doit vérifier en plus : la vue de son propre corps à la première
  personne (tête masquée, ombre conservée, pas de « trou » au niveau du cou), le coût en performances d'un avatar
  complet toujours rendu pour le joueur local, et la compatibilité avec l'IK du corps entier en VR.
- **Étape 4 (déplacements)** : les déplacements, la caméra et les animations sont conçus pour la première personne
  sur PC et en VR ; la synchronisation réseau (serveur qui fait autorité) porte aussi la pose de la tête et, en VR,
  des mains. La question du **confort en VR** est ouverte (voir ROADMAP, étape 4).
- **Hors de cette décision** : une vue à la troisième personne optionnelle (photo, éditeur d'avatar, spectateur)
  pourra être proposée plus tard par un nouvel ADR ; elle n'est pas prévue.
