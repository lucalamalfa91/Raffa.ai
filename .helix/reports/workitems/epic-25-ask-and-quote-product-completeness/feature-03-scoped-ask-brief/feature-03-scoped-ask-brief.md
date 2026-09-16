---
id: feature-03
type: feature
parent: epic-25
wave: w18
status: active
---

# feature-03-scoped-ask-brief — 360 "Ask about it" briefs the contract (NW-56)

## Slice
A scoped entry (`/ask?scope=<contractId>`) must brief the contract in the
heading and resolve the scope before the R-ASK-10 gate decides — never landing
on a generic gate/hello. The backend threads the conversation's persisted
`ScopeContractId` into the planner so the turn is scoped, and the web renders
the supplier kicker + scope line + scoped suggestions.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | scoped-ask-backend | w18 |
| us-02 | scoped-ask-web | w18 |

## Architecture decisions in force
- ADR-024 — one engine, scoped entry; the gate resolves the scope id before the R-ASK-10 check.
- ADR-020 — heading copy (supplier kicker + scope line).

## Target repo
`raffa-backend` + `raffa-web` (mixed)
