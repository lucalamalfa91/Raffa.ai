---
id: E05/F02/US01/T02
type: task
story: us-01-market-assessment
wave: R4
status: live
target_repo: raffa-backend
---

# task-02-target-saving — 02 Target Saving

## Coding objective
Target range + potential saving (deterministic).

## Parent story AC covered
- See parent story `us-01-market-assessment` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `target-saving` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `target-saving`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | target-saving behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E05/F02/US01/T02
  prompt: reports/workitems/epic-05-quote-check/feature-02-quote-assessment/us-01-market-assessment/tasks/task-02-target-saving.md
  produces: [target-saving]
  depends_on: [market-assessment]
  effort: M
  layer: backend
  status: live
```
