---
id: us-01
type: user-story
parent: feature-04
wave: w14
status: active
---

# us-01-members-api — the roster the server holds, not the one this tab remembers

## Story

As a **member of a workspace**, I want the Members table to show who is really
in it, so that another Admin, another browser or a reload sees the same roster
Postgres holds — and as the **platform**, I want a non-member to learn nothing
at all about a tenant they name.

## Acceptance criteria

- [ ] AC-1 (**N3**, server half) `GET /api/workspaces/{tenantId}/members` as a
      **live member** → `200 { members: [ { id, email, name?, role, status } ] }`.
- [ ] AC-2 `status` is **derived, never stored**: a live `workspace_membership`
      renders `Active`; no membership plus a live (unaccepted, unrevoked,
      unexpired) invitation renders `Invited`.
- [ ] AC-3 The roster is **live memberships ∪ live invitations** — **never** a
      scan of `workspace_user`. A removed member keeps their user row by design
      (audit continuity, and `WorkspaceSignIn` needs it), so a
      `workspace_user`-driven roster would list removed people and, because
      their `ExternalSubjectId` is still bound, would render them **`Active`**.
- [ ] AC-4 (**T1b**) A **non-member** gets **404** — never 403 (a
      tenant-existence oracle) and never an empty 200 (also an oracle: "that
      tenant exists and is empty"). The body carries no tenant name. Absent
      identity → **401**.
- [ ] AC-5 A person holding two memberships appears **once**, at the highest
      role, reusing the precedence
      `WorkspaceRoleResolver.cs:101-103,116-118` already applies.
- [ ] AC-6 `name` maps to the existing nullable `WorkspaceUser.DisplayName`
      (`WorkspaceUser.cs:28`) and is emitted only when a real stored value
      exists. It is **never derived from the email address**.
- [ ] AC-7 The route takes its tenant from the **route**, never from
      `X-Tenant-Id`.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `workspace-schema` (E14/F01/US01/T02) | the `workspace_invitation` table the `Invited` status is derived from |
| `workspace-bootstrap` (E14/F02/US01/T01) | the membership row `Active` is derived from, the `ICallerIdentity` seam, and `WorkspaceMembersEndpointExtensions.cs`, which that task creates |

## Architecture decisions in force

- **ADR-025 §D.4 (answers OQ-w14-005)** — **any live member may read the
  roster**; only an **Admin** may invite, revoke or remove. Read ≠ write. The
  design shows the roster to Procurement (`screens-v2.md` §10) and a team of
  five cannot operate if only Admins can see who is in the workspace.
- **ADR-009 (w14 footer clause 7)** — **verify, then scope, then read**: the
  route tenant is entered as a scope *after* the caller's membership in it is
  verified. "Scope and see what comes back" converts an authorization question
  into an empty-result question and yields 200-with-nothing where the answer
  must be 404.
- **Privacy** — email addresses are personal data returned **only** to members
  of that tenant. No endpoint in this wave exposes an email to a non-member, and
  the pre-accept read exposes none at all.
- **ADR-022 (w14 footer)** — `X-Tenant-Id` is not an input to a membership
  route; the route already carries `{tenantId}`. Sending both would let one
  caller present two candidate tenants for one operation.
- **No new RLS** — the join is over three already-policied tables
  (`identity-workspace.sql:154-158`, `:165-169`, `:176-180`). The `identity_self`
  policy cannot leak here: it is `FOR SELECT` on `workspace_user` matching only
  the **caller's own** rows, and the 404 prevents a non-member from reaching it.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | `GET /api/workspaces/{tenantId}/members` — the roster as memberships ∪ invitations | M | phase-2 |

## Council decisions carried into this story

- This **corrects the software-architect's own lane draft**, which derived
  `status` from `ExternalSubjectId is null` and would have rendered a
  **removed** member as `Active`.
- `invitedAt` is **dropped** — no consumer.
- The join at `WorkspaceRoleResolver.cs:104-114` is reused. **No migration.**
- The OpenAPI entry and the `getWorkspaceMembers(tenantId)` client method are
  written by the phase-2 contract owner, `E14/F03/US01/T01`, from ADR-026 §D3.
  This task implements the handler and **must match that contract exactly**.

## Open questions

- **OQ-w14-005** — who may list and remove members. **Answered at the table**:
  any live member may read; only an Admin may invite, revoke or remove; an Admin
  may not remove the last Admin; a removed member loses access on the next
  request.
