---
id: E04/F02/US01/T02
type: task
story: us-01-price-normalization
wave: R3
status: live
target_repo: raffa-backend
---

# task-02-savings-provenance — 02 Savings Provenance

## Coding objective
Propagate confidence + provenance.

## Parent story AC covered
- See parent story `us-01-price-normalization` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `savings-provenance` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `savings-provenance`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | savings-provenance behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E04/F02/US01/T02
  prompt: reports/workitems/epic-04-savings-intelligence/feature-02-savings-engine/us-01-price-normalization/tasks/task-02-savings-provenance.md
  produces: [savings-provenance]
  depends_on: [savings-normalization]
  effort: S
  layer: backend
  status: live
```
