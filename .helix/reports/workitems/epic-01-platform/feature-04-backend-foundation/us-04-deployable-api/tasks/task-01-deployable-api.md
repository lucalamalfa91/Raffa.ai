---
id: E01/F04/US04/T01
type: task
story: us-04-deployable-api
wave: R0
status: live
target_repo: raffa-backend
---

# task-01-deployable-api — 01 Deployable Api

## Coding objective
Create the thin API host composing modules with /health + Dockerfile.

## Parent story AC covered
- See parent story `us-04-deployable-api` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `deployable-api` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `deployable-api`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | deployable-api behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F04/US04/T01
  prompt: reports/workitems/epic-01-platform/feature-04-backend-foundation/us-04-deployable-api/tasks/task-01-deployable-api.md
  produces: [deployable-api]
  depends_on: [dotnet-solution, tenant-rls]
  effort: M
  layer: backend
  status: live
```
