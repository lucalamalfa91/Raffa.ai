---
id: us-01
type: user-story
parent: feature-01
wave: w19
status: active
---

# us-01-todo-entity-api — The TODO entity + GET/tick PUT persist negotiation points

## Story
As the **operator**, I want negotiation TODOs persisted in Postgres (RLS) and
readable/tickable over HTTP, so a second browser reads back what Ask wrote.

## Acceptance criteria
- [ ] AC-1 `renewal_negotiation_todo` keyed `(tenant_id, contract_id, point_key)` with the full field set, `ENABLE`+`FORCE` RLS with `USING`+`WITH CHECK` in the migration.
- [ ] AC-2 `GET /api/renewals/{id}/negotiation-todos` returns the list; tick PUT mirrors `POST /api/renewals/{id}/action` roles (Procurement/Admin).
- [ ] AC-3 the write is audited `renewal.negotiation_todos_written` with actor = token subject.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-003/009/011/028 w19
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| — | new entity + service in `Raffa.Renewals` (does not reuse `RenewalAction`/`ContractNegotiationStep`) |

## Architecture decisions in force
- ADR-028 — idempotent upsert (same key updates, never un-ticks Done, vanished → Superseded); ADR-009/011 — RLS + actor.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | todo-entity-api | L | phase-1 |

## Council decisions carried into this story
- do not reuse `RenewalAction` or `ContractNegotiationStep`; point_key stable; actor token subject; host upserts in-process.

## Open questions
- none
