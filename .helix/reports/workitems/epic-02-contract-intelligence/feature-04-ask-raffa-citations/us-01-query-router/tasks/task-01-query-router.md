---
id: E02/F04/US01/T01
type: task
story: us-01-query-router
wave: R1
status: live
target_repo: raffa-backend
---

# task-01-query-router — 01 Query Router

## Coding objective
Structured-vs-semantic query intent router (spec 8.3).

## Parent story AC covered
- See parent story `us-01-query-router` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `query-router` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `query-router`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | query-router behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F04/US01/T01
  prompt: reports/workitems/epic-02-contract-intelligence/feature-04-ask-raffa-citations/us-01-query-router/tasks/task-01-query-router.md
  produces: [query-router]
  depends_on: [contract-schema]
  effort: M
  layer: backend
  status: live
```
