---
id: E01/F02/US03/T02
type: task
story: us-03-demo-environment
wave: R0
status: live
target_repo: raffa-infra
---

# task-02-demo-isolation-check — 02 Demo Isolation Check

## Coding objective
Assert demo uses distinct RG/store and no shared state with dev.

## Parent story AC covered
- See parent story `us-03-demo-environment` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-infra/src/ | implementation for `demo-isolation-verified` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-005, ADR-016.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `demo-isolation-verified`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | demo-isolation-verified behaviour | workspace/raffa-infra/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F02/US03/T02
  prompt: reports/workitems/epic-01-platform/feature-02-azure-infrastructure/us-03-demo-environment/tasks/task-02-demo-isolation-check.md
  produces: [demo-isolation-verified]
  depends_on: [azure-demo-environment]
  effort: S
  layer: backend
  status: live
```
