---
id: E04/F03/US01/T01
type: task
story: us-01-savings-kpis
wave: R3
status: live
target_repo: raffa-backend
---

# task-01-savings-kpis — 01 Savings Kpis

## Coding objective
Procurement homepage KPI aggregation.

## Parent story AC covered
- See parent story `us-01-savings-kpis` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `savings-kpis` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002, ADR-009.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `savings-kpis`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | savings-kpis behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E04/F03/US01/T01
  prompt: reports/workitems/epic-04-savings-intelligence/feature-03-savings-dashboard/us-01-savings-kpis/tasks/task-01-savings-kpis.md
  produces: [savings-kpis]
  depends_on: [savings-opportunity]
  effort: M
  layer: backend
  status: live
```
