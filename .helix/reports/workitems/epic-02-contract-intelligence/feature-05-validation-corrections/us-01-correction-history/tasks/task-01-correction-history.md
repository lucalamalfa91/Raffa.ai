---
id: E02/F05/US01/T01
type: task
story: us-01-correction-history
wave: R1
status: live
target_repo: raffa-backend
---

# task-01-correction-history — 01 Correction History

## Coding objective
PATCH /api/contracts/{id} versioned correction + history.

## Parent story AC covered
- See parent story `us-01-correction-history` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `correction-history` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-003, ADR-009.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `correction-history`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | correction-history behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F05/US01/T01
  prompt: reports/workitems/epic-02-contract-intelligence/feature-05-validation-corrections/us-01-correction-history/tasks/task-01-correction-history.md
  produces: [correction-history]
  depends_on: [contract-schema]
  effort: M
  layer: backend
  status: live
```
