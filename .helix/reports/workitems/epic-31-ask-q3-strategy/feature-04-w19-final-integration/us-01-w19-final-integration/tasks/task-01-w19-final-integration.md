---
id: E31/F04/US01/T01
type: task
story: us-01-w19-final-integration
wave: w19
status: live
target_repo: raffa-backend
---

# task-01-w19-final-integration — Wave w19 integration + three-question acceptance runbook

## Coding objective
Run the whole wave green: `dotnet build backend/Raffa.slnx`, per-project
`dotnet test` (AiGateway, Chat, Documents.Contracts, Insights, Renewals, Api);
`npm run typecheck`, `npm run lint`, `npm test` in `web/`; run the non-live
golden cases for Q2/Q3 (Q2 notice answer is `answer` with the date; Q3 is
`kind=answer` ranked with ≤3 points + injected Renewals action). Sweep
`backend/README.md` and `web/README.md` for the changed surfaces (scoped engine,
planner lexicon, binding chip, `?select=`, TODO list, two-CTA card, notice pack).
Write `docs/waves/w19-acceptance.md` with the three wow questions' manual `dev`
checks.

## Parent story AC covered
- AC-1 backend build+test.
- AC-2 web typecheck/lint/test.
- AC-3 acceptance doc with the three questions.

## Files to create or modify
| Path | Change |
|------|--------|
| docs/waves/w19-acceptance.md | new: Q1/Q2/Q3 manual `dev` checks + goldens |

## Context the implementer needs
- **Architecture decisions in force**: ADR-016 (recorded, never dispatched); Q1 overflows to W20.
- **Do not touch**: `slice.current.yaml`, `wave-spec.*.yaml`, `slices/w19.yaml`.

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` (and each touched project) exit 0
- [ ] `npm run typecheck` / `npm run lint` / `npm test` (in `web/`) exit 0
- [ ] `docs/waves/w19-acceptance.md` exists with the three questions

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | whole wave green | backend + web suites |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E31/F04/US01/T01
  prompt: reports/workitems/epic-31-ask-q3-strategy/feature-04-w19-final-integration/us-01-w19-final-integration/tasks/task-01-w19-final-integration.md
  produces: [w19-integration]
  depends_on: [planner-lexicon, engine-scope, bar-scope, binding-chip, supplier-resolution, priced-lines-parity, rag-contract-filter, citation-ids, citation-two-cta, negotiation-todo-api, todo-host-upsert, renewals-select, todo-web, notice-pack, notice-fallbacks, q3-route, point-ranker, q3-persist]
  effort: L
  layer: backend
  status: live
```
