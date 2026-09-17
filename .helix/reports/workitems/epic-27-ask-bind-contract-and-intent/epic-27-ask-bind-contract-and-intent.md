---
id: epic-27
type: epic
wave: w19
status: active
extends: [epic-13, epic-25]
---

# epic-27-ask-bind-contract-and-intent — Bind the open contract + intent lexicon + supplier resolution

## Business capability
The Ask engine finally consumes the open contract's `scopeContractId` so deixis
("this supplier", "questo contratto") resolves to it; the 360 global bar passes
the origin contract's id; a persistent binding chip names the contract in the
thread; the planner lexicon covers the demo phrasings; and supplier/contract
resolution is no longer exact-capitalized-only. These are the shared primitives
(A) every wow flow (Q1/Q2/Q3) depends on.

## Product coverage
| Source | Item |
|--------|------|
| inputs/next/2026-09-16-ask-raffa-wow.md §2 | NW-76, NW-77, NW-78, NW-79, NW-80 |
| ADR-024 w19 | clauses 12 (scope), 13 (lexicon), 14 (resolution) |
| ADR-012 | bar scope + binding chip (cl. 49) |

## Features
| ID | Title | Wave |
|----|-------|------|
| feature-01 | intent-lexicon | w19 |
| feature-02 | engine-scope | w19 |
| feature-03 | bar-scope | w19 |
| feature-04 | binding-chip | w19 |
| feature-05 | supplier-resolution | w19 |

## Success looks like
The three screenshot sentences route to their target intents; a scoped chat
names its contract and answers deixis without "which supplier"; "AsterCloud
GmbH" resolves to the soonest AsterCloud contract, never `NeedsDocument`.

## Architecture decisions in force
- ADR-024 w19 (cl. 12–14) — scope consumed in the engine; lexicon; resolution host-side.
- ADR-012 (cl. 49) — the bar passes scope as `?scope=` (reusing w18 parse), never a new nav-state field.

## Out of scope
- No new `cancellationNoticeDays` column; no tools/grounding on `answer`.
- Q1 (portfolio mal-position) still ranks the workspace portfolio even scoped (lock 4).
