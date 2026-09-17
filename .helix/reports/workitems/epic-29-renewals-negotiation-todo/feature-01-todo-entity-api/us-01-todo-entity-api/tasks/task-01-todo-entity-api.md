---
id: E29/F01/US01/T01
type: task
story: us-01-todo-entity-api
wave: w19
status: live
target_repo: raffa-backend
---

# task-01-todo-entity-api — Renewals negotiation TODO entity + RLS + API (NW-85)

## Coding objective
Create `RenewalNegotiationTodo` in `Raffa.Renewals` (key `(tenant_id,
contract_id, point_key)`; fields `topic`, `rank`, `current`, `target`,
`rationale`, `citation_keys`, `source = "ask"`, `status` Open/Done/Superseded,
timestamps) with an `ENABLE`+`FORCE ROW LEVEL SECURITY` + `tenant_isolation`
`USING`+`WITH CHECK` migration (regenerated `renewals` SQL, byte-compared) —
**not** reusing `RenewalAction` or `ContractNegotiationStep`. Add
`RenewalNegotiationTodoService` with idempotent upsert (same `point_key` updates
current/target/rationale/rank; never un-ticks Done; vanished → Superseded), a
`GetAsync`, and a tick `SetDoneAsync`. Map `GET /api/renewals/{id}/negotiation-todos`
+ `PUT /api/renewals/{id}/negotiation-todos` (tick) in
`RenewalsEndpointExtensions` under the standard `ICallerContext` guard + the
`POST /api/renewals/{id}/action` roles; write
`renewal.negotiation_todos_written` audit with actor = the resolved token subject.

## Parent story AC covered
- AC-1 entity + RLS migration.
- AC-2 GET + tick PUT.
- AC-3 audit with token-subject actor.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Renewals/Domain/RenewalNegotiationTodo.cs | new entity |
| backend/src/Raffa.Renewals/Application/RenewalNegotiationTodoService.cs | idempotent upsert + get + tick |
| backend/src/Raffa.Renewals/Migrations/<new> + Scripts/renewals.sql | one migration (regenerate) |
| backend/src/Raffa.Api/RenewalsEndpointExtensions.cs | GET + tick PUT routes |

## Context the implementer needs
- **Architecture decisions in force**: ADR-028 (idempotent upsert), ADR-009 (ENABLE+FORCE, USING+WITH CHECK), ADR-011 (actor token subject), ADR-003 (one migration, single writer).
- **Do not touch**: `RenewalAction` / `ContractNegotiationStep` (reuse is forbidden); `Raffa.Insights`.

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Renewals.Tests` + `Raffa.Api.Tests` exit 0 (upsert idempotency, Done preserved, RLS-covered table, audit actor)
- [ ] `git diff --exit-code` on `renewals.sql` after regenerate (byte-compared)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | idempotent upsert; Done never un-ticked; vanished → Superseded | `backend/tests/Raffa.Renewals.Tests` |
| integration | GET/tick routes + audit actor + RLS | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E29/F01/US01/T01
  prompt: reports/workitems/epic-29-renewals-negotiation-todo/feature-01-todo-entity-api/us-01-todo-entity-api/tasks/task-01-todo-entity-api.md
  produces: [negotiation-todo-api]
  depends_on: []
  effort: L
  layer: backend
  status: live
```
