---
id: E01/F04/US02/T02
type: task
story: us-02-relational-store
wave: R0
status: live
target_repo: raffa-backend
---

# task-02-migrations-apply — 02 Migrations Apply

## Coding objective
Apply initial EF Core migrations; prove pgvector vector column usable.

## Parent story AC covered
- See parent story `us-02-relational-store` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `postgres-migrations` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-003.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `postgres-migrations`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | postgres-migrations behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F04/US02/T02
  prompt: reports/workitems/epic-01-platform/feature-04-backend-foundation/us-02-relational-store/tasks/task-02-migrations-apply.md
  produces: [postgres-migrations]
  depends_on: [postgres-schema]
  effort: S
  layer: backend
  status: live
```
