---
id: E03/F01/US01/T01
type: task
story: us-01-deterministic-dates
wave: R2
status: live
target_repo: raffa-backend
---

# task-01-deterministic-dates — 01 Deterministic Dates

## Coding objective
Compute renewal date + cancellation deadline deterministically.

## Parent story AC covered
- See parent story `us-01-deterministic-dates` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `renewal-engine` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `renewal-engine`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | renewal-engine behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E03/F01/US01/T01
  prompt: reports/workitems/epic-03-renewal-intelligence/feature-01-renewal-engine/us-01-deterministic-dates/tasks/task-01-deterministic-dates.md
  produces: [renewal-engine]
  depends_on: [contract-schema]
  effort: M
  layer: backend
  status: live
```
