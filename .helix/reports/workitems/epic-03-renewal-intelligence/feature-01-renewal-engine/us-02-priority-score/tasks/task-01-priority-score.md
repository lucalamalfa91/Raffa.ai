---
id: E03/F01/US02/T01
type: task
story: us-02-priority-score
wave: R2
status: live
target_repo: raffa-backend
---

# task-01-priority-score — 01 Priority Score

## Coding objective
Priority score + component breakdown.

## Parent story AC covered
- See parent story `us-02-priority-score` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `renewal-priority` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002, ADR-003.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `renewal-priority`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | renewal-priority behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E03/F01/US02/T01
  prompt: reports/workitems/epic-03-renewal-intelligence/feature-01-renewal-engine/us-02-priority-score/tasks/task-01-priority-score.md
  produces: [renewal-priority]
  depends_on: [renewal-engine]
  effort: M
  layer: backend
  status: live
```
