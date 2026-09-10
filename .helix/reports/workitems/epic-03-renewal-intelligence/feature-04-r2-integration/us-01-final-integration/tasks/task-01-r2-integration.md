---
id: E03/F04/US01/T01
type: task
story: us-01-final-integration
wave: R2
status: live
target_repo: raffa-backend
---

# task-01-r2-integration — 01 R2 Integration

## Coding objective
Prove R2 end-to-end: dates + alerts + prioritized pipeline.

## Parent story AC covered
- See parent story `us-01-final-integration` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `r2-integration` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002, ADR-003, ADR-009, ADR-016.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `r2-integration`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | r2-integration behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E03/F04/US01/T01
  prompt: reports/workitems/epic-03-renewal-intelligence/feature-04-r2-integration/us-01-final-integration/tasks/task-01-r2-integration.md
  produces: [r2-integration]
  depends_on: [renewal-opportunity, renewal-priority-explain, renewal-alerts, renewal-action]
  effort: L
  layer: backend
  status: live
```
