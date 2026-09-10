---
id: E01/F04/US03/T02
type: task
story: us-03-tenant-rls
wave: R0
status: live
target_repo: raffa-backend
---

# task-02-rls-migration-check — 02 Rls Migration Check

## Coding objective
Add CI migration check rejecting tenant tables lacking an RLS policy.

## Parent story AC covered
- See parent story `us-03-tenant-rls` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `rls-migration-check` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-009.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `rls-migration-check`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | rls-migration-check behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F04/US03/T02
  prompt: reports/workitems/epic-01-platform/feature-04-backend-foundation/us-03-tenant-rls/tasks/task-02-rls-migration-check.md
  produces: [rls-migration-check]
  depends_on: [tenant-rls]
  effort: S
  layer: backend
  status: live
```
