---
id: E05/F03/US02/T02
type: task
story: us-02-outcome-capture
wave: R4
status: live
target_repo: raffa-backend
---

# task-02-realized-propagation — 02 Realized Propagation

## Coding objective
Realized-savings propagation + audit.

## Parent story AC covered
- See parent story `us-02-outcome-capture` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `outcome-propagation` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-003, ADR-009.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `outcome-propagation`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | outcome-propagation behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E05/F03/US02/T02
  prompt: reports/workitems/epic-05-quote-check/feature-03-negotiation-strategy/us-02-outcome-capture/tasks/task-02-realized-propagation.md
  produces: [outcome-propagation]
  depends_on: [negotiation-outcome, realized-savings]
  effort: M
  layer: backend
  status: live
```
