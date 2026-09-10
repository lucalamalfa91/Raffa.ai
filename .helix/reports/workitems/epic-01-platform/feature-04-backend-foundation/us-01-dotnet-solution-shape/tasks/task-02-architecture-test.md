---
id: E01/F04/US01/T02
type: task
story: us-01-dotnet-solution-shape
wave: R0
status: live
target_repo: raffa-backend
---

# task-02-architecture-test — 02 Architecture Test

## Coding objective
Add an architecture test blocking domain->provider/domain-internals references.

## Parent story AC covered
- See parent story `us-01-dotnet-solution-shape` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `dotnet-architecture-test` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `dotnet-architecture-test`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | dotnet-architecture-test behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F04/US01/T02
  prompt: reports/workitems/epic-01-platform/feature-04-backend-foundation/us-01-dotnet-solution-shape/tasks/task-02-architecture-test.md
  produces: [dotnet-architecture-test]
  depends_on: [dotnet-solution]
  effort: S
  layer: backend
  status: live
```
