---
id: E01/F05/US01/T02
type: task
story: us-01-workspace-roles
wave: R0
status: superseded
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

## Superseded (2026-09-11, wave w14)

Ruled at the w14 council table on 2026-09-10 by the security-architect
(`reports/architecture/waves/w14.md`, §"Work items this wave supersedes"), and
applied here by `next-decomposer`. **The body above is unchanged**; only the
frontmatter `status:` line and this footer were added.

**Replaced by**: `E15/F01/US01/T01` — `reports/workitems/epic-15-workspace-invitations/
feature-01-invitation-lifecycle/us-01-invite-accept-remove/tasks/task-01-invitation-lifecycle-api.md`
(item **NW-58**, `reports/context/waves/w14-requirements.md`).

**Why this is a replacement, not an extension.** This task delivered
"invite ⇒ membership row **immediately**"
(`WorkspaceMembershipService.InviteAsync`, which writes `workspace_user` and a
live `workspace_membership` in one call). NW-58 must #2 / #4 makes membership
happen **on accept**: invite writes `workspace_user` + a `workspace_invitation`
row and **no membership**. Same word, two different product rules. Leaving this
task `live` would tell a future intake that the old rule is still wanted.

**Nothing is silently dropped.** The never-delivered "**with OIDC claims**" half
of this task's objective moves to **W15 / NW-05** (the API JWT, ADR-010), which
is recorded as queued in `reports/workitems/BACKLOG.md` §"Wave w14" and in
`reports/context/waves/w14-requirements.md` §"Queued items".

**Note for the operator**: `reports/plan/slices/e01.yaml:52` and
`reports/plan/wave-spec.execution.yaml:87` still list this task as
`status: live`. Those are historical wave files and the next-wave process never
edits them (`skills/kb-contract-next.md`, "Never write"). See
`reports/audit/w14-hitl.md` §"Superseded items".
