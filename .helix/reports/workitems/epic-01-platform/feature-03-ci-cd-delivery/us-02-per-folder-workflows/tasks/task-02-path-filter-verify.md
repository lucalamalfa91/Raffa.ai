---
id: E01/F03/US02/T02
type: task
story: us-02-per-folder-workflows
wave: R0
status: live
target_repo: raffa-infra
---

# task-02-path-filter-verify — 02 Path Filter Verify

## Coding objective
Verify four workflows have correct path filters and mobile non-blocking.

## Parent story AC covered
- See parent story `us-02-per-folder-workflows` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-infra/src/ | implementation for `ci-path-filters` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-014.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `ci-path-filters`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | ci-path-filters behaviour | workspace/raffa-infra/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F03/US02/T02
  prompt: reports/workitems/epic-01-platform/feature-03-ci-cd-delivery/us-02-per-folder-workflows/tasks/task-02-path-filter-verify.md
  produces: [ci-path-filters]
  depends_on: [ci-cd-workflows]
  effort: S
  layer: backend
  status: live
```
