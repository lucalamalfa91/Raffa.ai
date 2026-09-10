---
id: E03/F01/US01/T02
type: task
story: us-01-deterministic-dates
wave: R2
status: live
target_repo: raffa-backend
---

# task-02-renewal-opportunity — 02 Renewal Opportunity

## Coding objective
Generate renewal opportunities; abstain cannot-determine when missing.

## Parent story AC covered
- See parent story `us-01-deterministic-dates` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `renewal-opportunity` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `renewal-opportunity`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | renewal-opportunity behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E03/F01/US01/T02
  prompt: reports/workitems/epic-03-renewal-intelligence/feature-01-renewal-engine/us-01-deterministic-dates/tasks/task-02-renewal-opportunity.md
  produces: [renewal-opportunity]
  depends_on: [renewal-engine]
  effort: M
  layer: backend
  status: live
```
