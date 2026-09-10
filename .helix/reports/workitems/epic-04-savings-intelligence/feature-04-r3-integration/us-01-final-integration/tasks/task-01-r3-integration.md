---
id: E04/F04/US01/T01
type: task
story: us-01-final-integration
wave: R3
status: live
target_repo: raffa-backend
---

# task-01-r3-integration — 01 R3 Integration

## Coding objective
Prove R3 end-to-end with fixture benchmark.

## Parent story AC covered
- See parent story `us-01-final-integration` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `r3-integration` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-001, ADR-002, ADR-003, ADR-009, ADR-016.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `r3-integration`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | r3-integration behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E04/F04/US01/T01
  prompt: reports/workitems/epic-04-savings-intelligence/feature-04-r3-integration/us-01-final-integration/tasks/task-01-r3-integration.md
  produces: [r3-integration]
  depends_on: [benchmark-registry, fixture-confidence, savings-provenance, realized-savings, savings-list]
  effort: L
  layer: backend
  status: live
```
