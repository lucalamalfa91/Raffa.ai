---
id: E02/F01/US02/T01
type: task
story: us-02-staged-extraction
wave: R1
status: live
target_repo: raffa-backend
---

# task-01-staged-extraction — 01 Staged Extraction

## Coding objective
Implement staged schema-constrained extraction with source+confidence.

## Parent story AC covered
- See parent story `us-02-staged-extraction` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `extraction-pipeline` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-004, ADR-002.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `extraction-pipeline`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | extraction-pipeline behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F01/US02/T01
  prompt: reports/workitems/epic-02-contract-intelligence/feature-01-extraction-pipeline/us-02-staged-extraction/tasks/task-01-staged-extraction.md
  produces: [extraction-pipeline]
  depends_on: [ai-gateway-roles, contract-schema]
  effort: L
  layer: backend
  status: live
```
