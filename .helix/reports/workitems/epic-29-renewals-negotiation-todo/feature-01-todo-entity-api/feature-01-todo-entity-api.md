---
id: feature-01
type: feature
parent: epic-29
wave: w19
status: active
---

# feature-01-todo-entity-api — Renewals negotiation TODO entity + RLS + API (NW-85)

## Slice
New `RenewalNegotiationTodo` entity keyed `(tenant_id, contract_id, point_key)`
with topic/rank/current/target/rationale/citation_keys/source=ask/status/
timestamps, in `Raffa.Renewals`; an RLS migration; `GET
/api/renewals/{id}/negotiation-todos` + a tick PUT; audit
`renewal.negotiation_todos_written`; actor = token subject.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | todo-entity-api | w19 |

## Architecture decisions in force
- ADR-003/009/011/028 w19 — the table, RLS, actor, idempotent upsert semantics.

## Target repo
`raffa-backend`
