---
id: E02/F03/US02/T01
type: task
story: us-02-contract-360-aggregate
wave: R1
status: live
target_repo: raffa-backend
---

# task-01-contract-360 — 01 Contract 360

## Coding objective
GET /api/contracts/{id} 360 aggregate (header + tabs).

## Parent story AC covered
- See parent story `us-02-contract-360-aggregate` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `contract-360` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `contract-360`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | contract-360 behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F03/US02/T01
  prompt: reports/workitems/epic-02-contract-intelligence/feature-03-portfolio-contract-360/us-02-contract-360-aggregate/tasks/task-01-contract-360.md
  produces: [contract-360]
  depends_on: [portfolio-list]
  effort: M
  layer: backend
  status: live
```
