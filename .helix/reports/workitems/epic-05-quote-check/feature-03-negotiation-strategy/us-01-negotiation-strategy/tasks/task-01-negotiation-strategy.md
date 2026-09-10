---
id: E05/F03/US01/T01
type: task
story: us-01-negotiation-strategy
wave: R4
status: live
target_repo: raffa-backend
---

# task-01-negotiation-strategy — 01 Negotiation Strategy

## Coding objective
Opening target/range/walk-away/levers with rationale.

## Parent story AC covered
- See parent story `us-01-negotiation-strategy` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `negotiation-strategy` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002, ADR-004.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `negotiation-strategy`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | negotiation-strategy behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E05/F03/US01/T01
  prompt: reports/workitems/epic-05-quote-check/feature-03-negotiation-strategy/us-01-negotiation-strategy/tasks/task-01-negotiation-strategy.md
  produces: [negotiation-strategy]
  depends_on: [target-saving]
  effort: L
  layer: backend
  status: live
```
