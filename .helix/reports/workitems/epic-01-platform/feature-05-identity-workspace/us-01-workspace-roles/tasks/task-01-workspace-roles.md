---
id: E01/F05/US01/T01
type: task
story: us-01-workspace-roles
wave: R0
status: live
target_repo: raffa-backend
---

# task-01-workspace-roles — 01 Workspace Roles

## Coding objective
Implement Workspace/User/Role/Membership with tenant_id and RLS.

## Parent story AC covered
- See parent story `us-01-workspace-roles` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `workspace-roles` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-009, ADR-003.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `workspace-roles`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | workspace-roles behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F05/US01/T01
  prompt: reports/workitems/epic-01-platform/feature-05-identity-workspace/us-01-workspace-roles/tasks/task-01-workspace-roles.md
  produces: [workspace-roles]
  depends_on: [tenant-rls]
  effort: M
  layer: backend
  status: live
```
