---
id: feature-02
type: feature
parent: epic-29
wave: w19
status: active
---

# feature-02-todo-host-upsert — Host upserts TODOs after rank, before answer (NW-85)

## Slice
`AskCopilotService` (and the Q3/Q1-follow-up composition) calls
`RenewalNegotiationTodoService` to upsert all ranked points **after ranking and
before** the answer call, so the deep-link is true, with `persistTodos`
idempotent (re-ask adds no duplicate Open, Done preserved).

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | todo-host-upsert | w19 |

## Architecture decisions in force
- ADR-028 w19 — host upsert before answer; ADR-024 w19 (cl. 21) persist-all.

## Target repo
`raffa-backend`
