---
id: E05/F02/US01/T01
type: task
story: us-01-market-assessment
wave: R4
status: live
target_repo: raffa-backend
---

# task-01-market-assessment — 01 Market Assessment

## Coding objective
Match line items to benchmark; above/in-line/below.

## Parent story AC covered
- See parent story `us-01-market-assessment` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `market-assessment` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002, ADR-001.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `market-assessment`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | market-assessment behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E05/F02/US01/T01
  prompt: reports/workitems/epic-05-quote-check/feature-02-quote-assessment/us-01-market-assessment/tasks/task-01-market-assessment.md
  produces: [market-assessment]
  depends_on: [sku-normalization, benchmark-interface]
  effort: L
  layer: backend
  status: live
```
