---
id: E03/F02/US01/T01
type: task
story: us-01-threshold-scheduler
wave: R2
status: live
target_repo: raffa-backend
---

# task-01-threshold-scheduler — 01 Threshold Scheduler

## Coding objective
Daily scheduler + threshold windows + renewal.approaching event.

## Parent story AC covered
- See parent story `us-01-threshold-scheduler` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `threshold-scheduler` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002, ADR-003.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `threshold-scheduler`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | threshold-scheduler behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E03/F02/US01/T01
  prompt: reports/workitems/epic-03-renewal-intelligence/feature-02-cancellation-alerts/us-01-threshold-scheduler/tasks/task-01-threshold-scheduler.md
  produces: [threshold-scheduler]
  depends_on: [renewal-engine]
  effort: M
  layer: backend
  status: live
```
