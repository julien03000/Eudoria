# Jeux d'essai partagés — tickets d'entrée des mondes

Ces fichiers prouvent que **tous les vérificateurs de tickets** d'EUDORIA lisent le format de la même façon
(ADR-0007, plan `docs/plans/etape-01-6.md` § 8.2) :

- le vérificateur tiers .NET (`core/tests/Eudoria.ThirdParty.WorldVerifier`, test `SharedTestVectorsTests`) ;
- le vérificateur C++ du plugin Unreal `EudoriaCore` (partie B de la sous-étape 1.6).

Chacun doit donner **exactement** le verdict attendu pour chaque jeu d'essai.

| Fichier | Contenu |
|---|---|
| `keys.json` | les clés **publiques** (JWK Set, comme `GET /v1/entry-tickets/keys`) — aucune clé privée |
| `vectors.json` | le monde (`world`), le compte attendu (`accountId`), et la liste des jeux d'essai : `name`, `why`, `now` (heure de la vérification, en secondes Unix), `expected` (`accepted` ou un code de refus), `ticket` |

Codes de refus, **dans l'ordre où les règles sont vérifiées** (un ticket qui enfreint plusieurs règles reçoit le
premier code) : `ticket_malformed` (structure, JSON, membre en double, membre d'en-tête autre que `alg`/`typ`/`kid`,
plus de 4 096 caractères), `alg_not_allowed`, `typ_not_allowed`, (`kid` absent → `ticket_malformed`), `kid_unknown`,
`signature_invalid`, `iss_invalid`, `aud_invalid`, `claims_invalid`, `ticket_expired`, `ticket_from_future`.
Les refus qui demandent une mémoire ou le réseau (`ticket_replayed`, `replay_cache_full`, `keys_unavailable`) sont
testés à part, dans chaque vérificateur.

**Régénérer** (nouvelles clés jetables à chaque fois, donc nouveaux fichiers) : `.\tools\dev\new-entry-ticket-vectors.ps1`,
puis relancer les tests des deux vérificateurs.
