---
id: E05/F01/US01/T01
type: task
story: us-01-quote-line-extraction
wave: R4
status: live
target_repo: raffa-backend
---

# task-01-quote-extraction — 01 Quote Extraction

## Coding objective
POST /api/quotes upload + line-item extraction.

## Parent story AC covered
- See parent story `us-01-quote-line-extraction` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `quote-extraction` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002, ADR-004, ADR-003.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `quote-extraction`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | quote-extraction behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E05/F01/US01/T01
  prompt: reports/workitems/epic-05-quote-check/feature-01-quote-extraction/us-01-quote-line-extraction/tasks/task-01-quote-extraction.md
  produces: [quote-extraction]
  depends_on: [extraction-pipeline]
  effort: L
  layer: backend
  status: live
```
