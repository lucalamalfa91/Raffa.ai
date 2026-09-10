---
id: E01/F04/US04/T02
type: task
story: us-04-deployable-api
wave: R0
status: live
target_repo: raffa-backend
---

# task-02-deployable-worker — 02 Deployable Worker

## Coding objective
Create the thin worker host consuming the queue and shared app services.

## Parent story AC covered
- See parent story `us-04-deployable-api` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `deployable-worker` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `deployable-worker`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | deployable-worker behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F04/US04/T02
  prompt: reports/workitems/epic-01-platform/feature-04-backend-foundation/us-04-deployable-api/tasks/task-02-deployable-worker.md
  produces: [deployable-worker]
  depends_on: [deployable-api]
  effort: M
  layer: backend
  status: live
```
