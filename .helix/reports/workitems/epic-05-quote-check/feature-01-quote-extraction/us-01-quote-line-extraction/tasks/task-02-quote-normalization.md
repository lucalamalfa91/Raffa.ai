---
id: E05/F01/US01/T02
type: task
story: us-01-quote-line-extraction
wave: R4
status: live
target_repo: raffa-backend
---

# task-02-quote-normalization — 02 Quote Normalization

## Coding objective
Normalize line-item unit economics.

## Parent story AC covered
- See parent story `us-01-quote-line-extraction` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `quote-normalization` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `quote-normalization`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | quote-normalization behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E05/F01/US01/T02
  prompt: reports/workitems/epic-05-quote-check/feature-01-quote-extraction/us-01-quote-line-extraction/tasks/task-02-quote-normalization.md
  produces: [quote-normalization]
  depends_on: [quote-extraction]
  effort: M
  layer: backend
  status: live
```
