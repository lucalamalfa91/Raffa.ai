---
id: E05/F03/US02/T01
type: task
story: us-02-outcome-capture
wave: R4
status: live
target_repo: raffa-backend
---

# task-01-outcome-capture — 01 Outcome Capture

## Coding objective
NegotiationOutcome entity + POST /api/negotiations/outcomes.

## Parent story AC covered
- See parent story `us-02-outcome-capture` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `negotiation-outcome` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002, ADR-003.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `negotiation-outcome`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | negotiation-outcome behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E05/F03/US02/T01
  prompt: reports/workitems/epic-05-quote-check/feature-03-negotiation-strategy/us-02-outcome-capture/tasks/task-01-outcome-capture.md
  produces: [negotiation-outcome]
  depends_on: [negotiation-strategy]
  effort: M
  layer: backend
  status: live
```
