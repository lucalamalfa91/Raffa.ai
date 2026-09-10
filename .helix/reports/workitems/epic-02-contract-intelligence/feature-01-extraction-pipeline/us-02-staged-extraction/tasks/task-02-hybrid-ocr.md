---
id: E02/F01/US02/T02
type: task
story: us-02-staged-extraction
wave: R1
status: live
target_repo: raffa-backend
---

# task-02-hybrid-ocr — 02 Hybrid Ocr

## Coding objective
Add hybrid OCR pre-pass behind gateway (full doc, no 2-page cap).

## Parent story AC covered
- See parent story `us-02-staged-extraction` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `hybrid-ocr` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-004, ADR-011.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `hybrid-ocr`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | hybrid-ocr behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F01/US02/T02
  prompt: reports/workitems/epic-02-contract-intelligence/feature-01-extraction-pipeline/us-02-staged-extraction/tasks/task-02-hybrid-ocr.md
  produces: [hybrid-ocr]
  depends_on: [extraction-pipeline]
  effort: M
  layer: backend
  status: live
```
