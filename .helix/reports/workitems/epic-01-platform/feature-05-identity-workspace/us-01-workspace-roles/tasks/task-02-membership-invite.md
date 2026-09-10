---
id: E01/F05/US01/T02
type: task
story: us-01-workspace-roles
wave: R0
status: live
target_repo: raffa-backend
---

# task-02-membership-invite — 02 Membership Invite

## Coding objective
Implement workspace invite + role assignment with OIDC claims.

## Parent story AC covered
- See parent story `us-01-workspace-roles` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `workspace-membership` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-010, ADR-009.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `workspace-membership`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | workspace-membership behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F05/US01/T02
  prompt: reports/workitems/epic-01-platform/feature-05-identity-workspace/us-01-workspace-roles/tasks/task-02-membership-invite.md
  produces: [workspace-membership]
  depends_on: [workspace-roles]
  effort: M
  layer: backend
  status: live
```
