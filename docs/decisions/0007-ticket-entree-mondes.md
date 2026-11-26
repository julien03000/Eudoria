# ADR-0007 — Ticket d'entrée des mondes

- **Statut :** Accepté (validé par le porteur le 2026-11-26, avec la partie A de la sous-étape 1.6)
- **Date :** 2026-11-26
- **Liée à :** ADR-0001 (noyau, contrats OpenAPI), ADR-0004 (mondes = serveurs dédiés Unreal) ;
  plan `docs/plans/etape-01-6.md` (décisions du porteur du 2026-11-26)

## Contexte

Un joueur connecté au noyau possède un **jeton d'accès** (JWT ES256, 15 min, audience `eudoria-core`). Quand il
entrera dans un monde (serveur dédié Unreal, étape 3), ce monde devra savoir **qui il est**.

`docs/ARCHITECTURE.md` (§ 3) prévoyait : « le serveur de monde vérifie le jeton (signature **ou** appel au noyau) ».
Deux questions restaient ouvertes :
1. Le monde reçoit-il le **jeton d'accès** du joueur, ou autre chose ?
2. Vérifie-t-il **seul** (signature), ou en **appelant le noyau** ?

Contraintes :
- Décision du porteur (2026-11-24) : le jeton d'accès **ne doit jamais** être remis à un monde. Un monde (un jour
  communautaire, donc pas forcément de confiance) qui le recevrait pourrait agir au nom du joueur sur le noyau.
- La porte d'entrée d'un serveur Unreal (`PreLogin`) attend une réponse **immédiate** : un appel réseau au noyau
  à chaque connexion la ralentirait et rendrait chaque monde dépendant de la disponibilité du noyau.
- Le serveur fait autorité ; les jetons ne doivent jamais pouvoir être confondus entre eux (RFC 8725).

## Options étudiées

| | **A. Ticket d'entrée signé, vérifié hors ligne** | B. Le monde reçoit le jeton d'accès | C. Ticket opaque, vérifié par appel au noyau |
|---|---|---|---|
| Ce que reçoit le monde | un ticket valable 60 s, pour lui seul, une fois | le jeton d'accès (15 min, tout le noyau) | un code aléatoire |
| Un monde malveillant peut… | rien d'autre qu'entrer une fois dans lui-même | **agir sur le noyau au nom du joueur** | rien |
| Appel au noyau par connexion | **aucun** (clés publiques en cache) | aucun | **un** (latence, dépendance) |
| Identité du monde auprès du noyau | pas nécessaire | pas nécessaire | **nécessaire tout de suite** |
| Anti-rejeu | mémoire des `jti` par le monde | aucun (15 min réutilisables) | simple (côté noyau) |
| Révocation immédiate | non (60 s au plus) | non (15 min) | oui |

B est écarté (décision du porteur). C est plus simple pour l'anti-rejeu mais oblige chaque monde à joindre le noyau
à chaque connexion et à s'authentifier dès maintenant. A ne coûte qu'une mémoire des `jti` de quelques kilo-octets
dans le monde.

## Décision

**Option A : ticket d'entrée signé par le noyau, vérifié par le monde sans appeler le noyau** (« signature
seulement » : le « ou appel au noyau » de `ARCHITECTURE.md` est retiré).

1. **Émission.** Le client demande au noyau, juste avant d'entrer dans un monde,
   `POST /v1/worlds/{worldId}/entry-tickets` avec son jeton d'accès (vérifié, révocation comprise). Le noyau
   refuse un monde qu'il ne connaît pas (404 `world_unknown`). Le client garde le ticket en mémoire, s'en sert une
   fois, ne l'écrit jamais dans un journal.
2. **Format.** JWT (JWS compact) signé **ES256** ; en-tête `{alg: ES256, typ: eudoria-entry+jwt, kid}` ;
   revendications **exactement** `iss` (`eudoria-core`), `sub` (UUID du compte), `aud` (`eudoria-world:<id>`, une
   seule valeur), `iat`, `exp` (= `iat` + **60 s**), `jti` (UUID v4). Rien d'autre (ni pseudo, ni e-mail).
3. **Clé.** Une **clé de signature distincte** de celle des jetons d'accès (secret `Identity__EntryTickets__SigningKey`).
   Seule sa partie publique est publiée, en **JWK Set** (RFC 7517) sur `GET /v1/entry-tickets/keys` (route publique,
   cache 5 min), avec un `kid` = **empreinte RFC 7638** de la clé. La clé des jetons d'accès n'est jamais publiée.
4. **Vérification par le monde** (hors ligne, clés en mémoire, rafraîchies toutes les 5 min ; `kid` inconnu → un
   seul nouveau téléchargement, au plus un par minute) : signature ES256 par `kid`, `typ` exact, `alg` exact,
   `iss`, `aud` = sa propre audience, dates avec **15 s** de tolérance d'horloge, puis **usage unique** : le monde
   retient chaque `jti` accepté jusqu'à `exp` + 15 s (mémoire bornée ; pleine → refus).
5. **Séparation des jetons**, trois verrous indépendants : type explicite (le jeton d'accès devient
   `typ: eudoria-access+jwt`, **seul type accepté par le noyau** ; le ticket est `eudoria-entry+jwt`), audience
   (`eudoria-core` contre `eudoria-world:<id>`), clé de signature (deux clés).
6. **Identifiant de monde** : texte opaque `^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$` (un nom lisible aujourd'hui,
   un UUID demain). En attendant le registre des mondes (étape 6), le noyau lit une **liste fermée** dans sa
   configuration (`Identity:EntryTickets:KnownWorlds`, premier monde : `sandbox`).

## Conséquences

- Un monde peut identifier un joueur **seul**, en quelques microsecondes, même si le noyau est momentanément
  injoignable (avec les clés déjà en mémoire).
- Le jeton d'accès ne quitte jamais la liaison client ↔ noyau ; un ticket volé ne sert qu'une fois, dans un seul monde,
  pendant 75 s au plus.
- Changer le `typ` du jeton d'accès invalide une fois les jetons déjà émis : le client reçoit 401 et renouvelle
  (mécanisme de la 1.5), sans intervention du joueur.
- Un secret de plus dans `.env` (`IDENTITY_ENTRY_TICKET_SIGNING_KEY`, même script de génération).
- **Limites acceptées pour l'instant** (traitées à l'étape 6, avec le registre et plusieurs instances) :
  - un ticket volé pourrait servir une fois **sur chaque instance** d'un même monde, ou de nouveau après un
    redémarrage du serveur (la mémoire des `jti` n'est ni partagée ni conservée) ;
  - pas de révocation immédiate : un ticket délivré reste valable ses 60 s ;
  - une clé compromise reste en cache dans les mondes jusqu'à 5 min ; rotation à plusieurs clés et procédure
    écrite à faire avant l'hébergement public.
- Reporté : identité des serveurs de monde auprès du noyau (étape 3) ; transport du ticket dans la connexion
  Unreal (étape 3, jamais dans l'adresse de connexion, qu'Unreal écrit dans ses journaux) ; identifiant différent par
  monde pour les mondes communautaires (étape 10).
- Un vérificateur tiers indépendant (.NET, sans aucun code du noyau) et des jeux d'essai partagés
  (`shared/test-vectors/entry-tickets/`) prouvent que le format se suffit à lui-même ; le vérificateur C++ du socle
  Unreal (partie B) devra donner exactement les mêmes verdicts.

## Sources

- RFC 7515 (JWS), RFC 7517 (JWK), RFC 7518 (algorithmes, ES256 : signature `r‖s` de 64 octets), RFC 7519 (JWT),
  RFC 7638 (empreinte de clé JWK), RFC 8725 (bonnes pratiques JWT, § 3.11 type explicite et § 3.12 audiences).
