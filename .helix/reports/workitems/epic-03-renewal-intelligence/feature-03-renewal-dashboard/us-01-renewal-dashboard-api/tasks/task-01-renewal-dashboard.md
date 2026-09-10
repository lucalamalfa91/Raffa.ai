---
id: E03/F03/US01/T01
type: task
story: us-01-renewal-dashboard-api
wave: R2
status: live
target_repo: raffa-backend
---

# task-01-renewal-dashboard — 01 Renewal Dashboard

## Coding objective
GET /api/renewals pipeline + insight card.

## Parent story AC covered
- See parent story `us-01-renewal-dashboard-api` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `renewal-dashboard` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002, ADR-009.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `renewal-dashboard`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | renewal-dashboard behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E03/F03/US01/T01
  prompt: reports/workitems/epic-03-renewal-intelligence/feature-03-renewal-dashboard/us-01-renewal-dashboard-api/tasks/task-01-renewal-dashboard.md
  produces: [renewal-dashboard]
  depends_on: [renewal-engine]
  effort: M
  layer: backend
  status: live
```
