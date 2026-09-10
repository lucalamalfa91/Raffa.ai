---
id: E02/F03/US01/T01
type: task
story: us-01-portfolio-list-filters
wave: R1
status: live
target_repo: raffa-backend
---

# task-01-portfolio-list — 01 Portfolio List

## Coding objective
GET /api/contracts spec 8.1 columns + server-side tenant filter.

## Parent story AC covered
- See parent story `us-01-portfolio-list-filters` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `portfolio-list` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002, ADR-009.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `portfolio-list`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | portfolio-list behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F03/US01/T01
  prompt: reports/workitems/epic-02-contract-intelligence/feature-03-portfolio-contract-360/us-01-portfolio-list-filters/tasks/task-01-portfolio-list.md
  produces: [portfolio-list]
  depends_on: [contract-schema]
  effort: M
  layer: backend
  status: live
```
