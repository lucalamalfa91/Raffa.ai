---
id: E02/F04/US01/T02
type: task
story: us-01-query-router
wave: R1
status: live
target_repo: raffa-backend
---

# task-02-deterministic-queries — 02 Deterministic Queries

## Coding objective
Deterministic query handlers for dates/spend (no LLM).

## Parent story AC covered
- See parent story `us-01-query-router` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `deterministic-queries` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-002.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `deterministic-queries`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | deterministic-queries behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F04/US01/T02
  prompt: reports/workitems/epic-02-contract-intelligence/feature-04-ask-raffa-citations/us-01-query-router/tasks/task-02-deterministic-queries.md
  produces: [deterministic-queries]
  depends_on: [query-router]
  effort: M
  layer: backend
  status: live
```
