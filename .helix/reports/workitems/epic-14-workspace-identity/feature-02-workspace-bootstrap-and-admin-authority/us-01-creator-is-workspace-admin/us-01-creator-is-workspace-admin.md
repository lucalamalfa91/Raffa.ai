---
id: us-01
type: user-story
parent: feature-02
wave: w14
status: active
---

# us-01-creator-is-workspace-admin — the person who creates a workspace is its Admin, and only an Admin can invite

## Story

As the **person who creates a workspace**, I want to be its Workspace Admin the
moment it exists, so that I can upload, delete and manage members without
anybody granting me anything — and so that nobody outside the workspace can
grant themselves the same.

## Acceptance criteria

- [ ] AC-1 (**N1**) `POST /api/workspaces` with an identity → rows in
      `workspace`, `workspace_user` and `workspace_membership` (Admin, that
      identity), observable on `dev`. The 201 body carries `role: "Admin"`.
- [ ] AC-2 `POST /api/workspaces` with **no** identity → **401**, and no row is
      written. (Not 400: absence of identity is an authentication failure.) A
      malformed body is still 400.
- [ ] AC-3 A `role` field in the create body is **ignored**, never honoured.
      There is no self-assignable role anywhere in this wave.
- [ ] AC-4 Creating a tenant grants **no** read of any existing tenant.
- [ ] AC-5 (**T3, the invite-hole regression**)
      `POST /api/workspaces/{T1}/invites` with **no** identity → **401** (today:
      201 Created with a real Admin membership row). Then: a non-member of `T1`
      → **404**; a `T1` Procurement member → **403**; a `T1` Admin → **201**.
- [ ] AC-6 An Admin may invite another Admin; what is refused is a non-Admin
      reaching the endpoint, and any **self**-assignment.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| none | `deps: []`, phase 1. **No migration** — `workspace_user` and `workspace_membership` already exist and `InviteAsync` writes a membership row today |

## Architecture decisions in force

- **ADR-025 §B** — the rejection contract. **401** for absent identity, **404**
  (never 403) for a non-member, **403** for a member who lacks the role. A 403
  for a non-member is a tenant-existence oracle.
- **ADR-025 §D.2** — the creator is Admin *by virtue of creating the tenant*.
- **ADR-009 (w14 footer, clause 6)** — the four writes land in **one**
  `BeginScope(workspace.TenantId)` and **one** `SaveChangesAsync` under the new
  tenant's own claim: the workspace row *is* the first row of its own tenant
  (`WorkspaceProvisioningService.cs:17-21`; `UNIQUE INDEX ix_workspace_tenant_id`,
  `identity-workspace.sql:69`; `WITH CHECK` `:145-147`). **A partial bootstrap
  must not be reachable by a failure path either.** This path stays strictly
  one-scope-per-request — the discovery exception is not available to it, and
  **no policy is relaxed to make the bootstrap fit**.
- **ADR-025 §A** — every new or changed endpoint consumes the **one** identity
  seam. No endpoint added in this wave reads `HttpRequest.Headers` directly.
- **ADR-026 §D5** — the 201 carries `role: "Admin"` so the SPA enters without a
  second call.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Creator membership on create, the Admin gate on invite, and the route-group split | L | phase-1 |

## Council decisions carried into this story

- `CreateWorkspaceAsync(name, callerIdentity, profile, ct)` reuses
  `WorkspaceFactory.CreateWorkspaceWithDefaultRoles` and
  `WorkspaceMembershipFactory.CreateInvitedUser` / `CreateMembership` rather
  than new construction logic. **No migration — no schema change.**
- **Backfill is out of band.** A "claim this workspace" endpoint is a
  **tenant-takeover primitive** (whoever calls first becomes Admin of a tenant
  holding another user's uploads) and is **refused** (ADR-025 §2.3). Existing
  `dev` workspaces are backfilled by an operator job over
  `(tenant id, admin email)` pairs supplied at HITL — `E14/F05/US01/T01`.
- `WorkspaceEndpointExtensions.cs` is contended by five w14 items and is 98
  lines today. ADR-026 implication 6: **split it by route group up front**, in
  this task, keeping `MapWorkspaceEndpoints()` as the single entry point so
  `Program.cs:225` is untouched.

## Open questions

- **OQ-w14-001** — which identity keys membership. **Assumption in force**: the
  interim `X-User-Id`, behind one `ICallerIdentity` seam, so W15 (NW-05)
  replaces the header with the token `sub`/`oid` without touching any caller.
- **OQ-w14-006** — how existing `dev` workspaces get a membership row.
  **Answered**: an operator script, never an endpoint (`E14/F05/US01/T01`).
