---
id: E02/F02/US02/T02
type: task
story: us-02-embedding-search-index
wave: R1
status: live
target_repo: raffa-backend
---

# task-02-tenant-retrieval — 02 Tenant Retrieval

## Coding objective
Tenant-scoped similarity search + embed via IAiGateway.

## Parent story AC covered
- See parent story `us-02-embedding-search-index` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `tenant-retrieval` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-009, ADR-004.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `tenant-retrieval`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | tenant-retrieval behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E02/F02/US02/T02
  prompt: reports/workitems/epic-02-contract-intelligence/feature-02-contract-schema/us-02-embedding-search-index/tasks/task-02-tenant-retrieval.md
  produces: [tenant-retrieval]
  depends_on: [embedding-entity]
  effort: M
  layer: backend
  status: live
```
