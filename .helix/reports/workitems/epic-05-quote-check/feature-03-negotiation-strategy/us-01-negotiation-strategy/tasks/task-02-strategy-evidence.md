---
id: E05/F03/US01/T02
type: task
story: us-01-negotiation-strategy
wave: R4
status: live
target_repo: raffa-backend
---

# task-02-strategy-evidence — 02 Strategy Evidence

## Coding objective
Cite evidence per lever.

## Parent story AC covered
- See parent story `us-01-negotiation-strategy` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `strategy-evidence` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `strategy-evidence`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | strategy-evidence behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E05/F03/US01/T02
  prompt: reports/workitems/epic-05-quote-check/feature-03-negotiation-strategy/us-01-negotiation-strategy/tasks/task-02-strategy-evidence.md
  produces: [strategy-evidence]
  depends_on: [negotiation-strategy]
  effort: M
  layer: backend
  status: live
```
