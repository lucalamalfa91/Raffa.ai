---
id: epic-29
type: epic
wave: w19
status: active
extends: [epic-03 F02, epic-19, epic-08 F02]
---

# epic-29-renewals-negotiation-todo — Renewals negotiation TODO list + `?select=`

## Business capability
A durable `renewal_negotiation_todo` table (keyed `tenant_id`/`contract_id`/
`point_key`, RLS, auditable) that Ask writes in-process after ranking and
before answering, exposed at `GET /api/renewals/{id}/negotiation-todos` + a
tick PUT; and Renewals honours `/renewals?select=` so a chat deep-link opens
the right row. These are the shared primitives (C) Q1-follow-up and Q3 persist
to.

## Product coverage
| Source | Item |
|--------|------|
| inputs/next/2026-09-16-ask-raffa-wow.md §2 | NW-85, NW-84 |
| ADR-003/009/011/028 w19 | the TODO table, RLS, actor, host upsert |
| ADR-012 (cl. 51–52) | `?select=` + TODO surface |

## Features
| ID | Title | Wave |
|----|-------|------|
| feature-01 | todo-entity-api | w19 |
| feature-02 | todo-host-upsert | w19 |
| feature-03 | renewals-select | w19 |
| feature-04 | todo-web | w19 |

## Success looks like
After Q3 (and a Q1 follow-up), `GET` returns the written todos and a second
browser agrees; Done survives a repeat ask; `/renewals?select={id}` opens that
row's insight + TODO list without a 500.

## Architecture decisions in force
- ADR-028 — idempotent upsert (same key updates, never un-ticks Done, vanished → Superseded); host writes in-process.
- ADR-009 — ENABLE + FORCE RLS, `USING` + `WITH CHECK` in the migration; ADR-011 — actor = token subject.
- ADR-003 — one migration, single writer of `renewals` (module) script.

## Out of scope
- Does not cancel NW-75; does not reuse `RenewalAction` or `ContractNegotiationStep`; TODOs are negotiation points, not realized-savings estimates.
