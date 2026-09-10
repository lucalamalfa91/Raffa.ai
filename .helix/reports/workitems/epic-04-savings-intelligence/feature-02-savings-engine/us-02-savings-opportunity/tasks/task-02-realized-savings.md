---
id: E04/F02/US02/T02
type: task
story: us-02-savings-opportunity
wave: R3
status: live
target_repo: raffa-backend
---

# task-02-realized-savings — 02 Realized Savings

## Coding objective
Record realized value + audit event.

## Parent story AC covered
- See parent story `us-02-savings-opportunity` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `realized-savings` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-009.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `realized-savings`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | realized-savings behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E04/F02/US02/T02
  prompt: reports/workitems/epic-04-savings-intelligence/feature-02-savings-engine/us-02-savings-opportunity/tasks/task-02-realized-savings.md
  produces: [realized-savings]
  depends_on: [savings-opportunity, audit-abstraction]
  effort: M
  layer: backend
  status: live
```
