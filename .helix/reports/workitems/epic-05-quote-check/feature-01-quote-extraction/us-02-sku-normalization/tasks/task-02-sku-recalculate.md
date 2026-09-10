---
id: E05/F01/US02/T02
type: task
story: us-02-sku-normalization
wave: R4
status: live
target_repo: raffa-backend
---

# task-02-sku-recalculate — 02 Sku Recalculate

## Coding objective
Manual product mapping + recalculate trigger.

## Parent story AC covered
- See parent story `us-02-sku-normalization` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `sku-recalculate` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002.
- **Recovery**: this task was skipped on e05 (`resume_completed` treated a leftover branch as done). It is re-queued on slice `e07` (phase-5). `sku-normalization` is already on `main`.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `sku-recalculate`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | sku-recalculate behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E05/F01/US02/T02
  prompt: reports/workitems/epic-05-quote-check/feature-01-quote-extraction/us-02-sku-normalization/tasks/task-02-sku-recalculate.md
  produces: [sku-recalculate]
  depends_on: [sku-normalization]
  effort: M
  layer: backend
  status: live
```
