---
id: E02/F02/US02/T01
type: task
story: us-02-embedding-search-index
wave: R1
status: live
target_repo: raffa-backend
---

# task-01-embedding-entity — 01 Embedding Entity

## Coding objective
Add Embedding entity with pgvector vector column + fixed dimension.

## Parent story AC covered
- See parent story `us-02-embedding-search-index` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `embedding-entity` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-003, ADR-004.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `embedding-entity`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | embedding-entity behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F02/US02/T01
  prompt: reports/workitems/epic-02-contract-intelligence/feature-02-contract-schema/us-02-embedding-search-index/tasks/task-01-embedding-entity.md
  produces: [embedding-entity]
  depends_on: [contract-schema]
  effort: M
  layer: backend
  status: live
```
