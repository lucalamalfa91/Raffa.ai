---
id: E04/F02/US02/T01
type: task
story: us-02-savings-opportunity
wave: R3
status: live
target_repo: raffa-backend
---

# task-01-savings-opportunity — 01 Savings Opportunity

## Coding objective
SavingsOpportunity entity + GET/PATCH /api/savings.

## Parent story AC covered
- See parent story `us-02-savings-opportunity` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `savings-opportunity` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002, ADR-003.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `savings-opportunity`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | savings-opportunity behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E04/F02/US02/T01
  prompt: reports/workitems/epic-04-savings-intelligence/feature-02-savings-engine/us-02-savings-opportunity/tasks/task-01-savings-opportunity.md
  produces: [savings-opportunity]
  depends_on: [savings-normalization]
  effort: M
  layer: backend
  status: live
```
