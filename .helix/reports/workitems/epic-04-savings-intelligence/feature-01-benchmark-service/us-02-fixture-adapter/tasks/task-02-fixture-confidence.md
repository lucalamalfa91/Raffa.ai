---
id: E04/F01/US02/T02
type: task
story: us-02-fixture-adapter
wave: R3
status: live
target_repo: raffa-backend
---

# task-02-fixture-confidence — 02 Fixture Confidence

## Coding objective
Weak-comparable abstain; no paid API.

## Parent story AC covered
- See parent story `us-02-fixture-adapter` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `fixture-confidence` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-001.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `fixture-confidence`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | fixture-confidence behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E04/F01/US02/T02
  prompt: reports/workitems/epic-04-savings-intelligence/feature-01-benchmark-service/us-02-fixture-adapter/tasks/task-02-fixture-confidence.md
  produces: [fixture-confidence]
  depends_on: [fixture-adapter]
  effort: M
  layer: backend
  status: live
```
