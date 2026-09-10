---
id: E04/F01/US01/T02
type: task
story: us-01-benchmark-interface
wave: R3
status: live
target_repo: raffa-backend
---

# task-02-adapter-registry — 02 Adapter Registry

## Coding objective
Adapter registry; no provider SDK in domain code.

## Parent story AC covered
- See parent story `us-01-benchmark-interface` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `benchmark-registry` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-001.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `benchmark-registry`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | benchmark-registry behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E04/F01/US01/T02
  prompt: reports/workitems/epic-04-savings-intelligence/feature-01-benchmark-service/us-01-benchmark-interface/tasks/task-02-adapter-registry.md
  produces: [benchmark-registry]
  depends_on: [benchmark-interface]
  effort: M
  layer: backend
  status: live
```
