---
id: E05/F04/US01/T01
type: task
story: us-01-final-integration
wave: R4
status: live
target_repo: raffa-backend
---

# task-01-r4-integration — 01 R4 Integration

## Coding objective
Prove R4 Day-1 path: quote->assess->strategy->outcome.

## Parent story AC covered
- See parent story `us-01-final-integration` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `r4-integration` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-001, ADR-002, ADR-003, ADR-009, ADR-016.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `r4-integration`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | r4-integration behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E05/F04/US01/T01
  prompt: reports/workitems/epic-05-quote-check/feature-04-r4-integration/us-01-final-integration/tasks/task-01-r4-integration.md
  produces: [r4-integration]
  depends_on: [quote-normalization, sku-recalculate, target-saving, strategy-evidence, outcome-propagation]
  effort: L
  layer: backend
  status: live
```
