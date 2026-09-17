---
id: E29/F02/US01/T01
type: task
story: us-01-todo-host-upsert
wave: w19
status: live
target_repo: raffa-backend
---

# task-01-todo-host-upsert — Host upserts ranked TODOs before the answer (NW-85)

## Coding objective
In `backend/src/Raffa.Api/AskCopilotService.cs`, wire the ranked negotiation
points (produced by the epic-31 `NegotiationPointRanker` via
`BuildNegotiationPointsPackAsync(contractId, includeRenewalUrgency,
persistTodos)`) into `RenewalNegotiationTodoService.UpsertAsync` **after ranking
and before** the `answer` call, so the `/renewals?select={id}` deep-link is true.
`persistTodos=true` upserts **all** points (chat top-3 uncapped in storage); a
repeat ask is idempotent (same `point_key` updates, never un-ticks Done, vanished
→ Superseded). A row that is locked-for-2026 (NW-88/89, epic-32) never receives a
2026-save TODO.

## Parent story AC covered
- AC-1 upsert all ranked points before answer.
- AC-2 re-ask idempotent (no duplicate Open, Done preserved).
- AC-3 locked rows never get a 2026-save TODO.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Api/AskCopilotService.cs | upsert ranked todos before the answer call |

## Context the implementer needs
- **Architecture decisions in force**: ADR-028/ADR-024 w19 (cl. 21).
- **Do not touch**: the ranker (epic-31) or the entity/API (feature-01).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0, with a test that a Q3 (and Q1 follow-up) turn upserts todos before the answer and a repeat ask is idempotent

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | upsert-before-answer + idempotent re-ask | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E29/F02/US01/T01
  prompt: reports/workitems/epic-29-renewals-negotiation-todo/feature-02-todo-host-upsert/us-01-todo-host-upsert/tasks/task-01-todo-host-upsert.md
  produces: [todo-host-upsert]
  depends_on: [negotiation-todo-api, point-ranker]
  effort: M
  layer: backend
  status: live
```
