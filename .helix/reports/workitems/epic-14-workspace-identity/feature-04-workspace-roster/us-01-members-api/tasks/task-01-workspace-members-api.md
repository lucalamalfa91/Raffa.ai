---
id: E14/F04/US01/T01
type: task
story: us-01-members-api
wave: w14
status: live
target_repo: raffa-backend
---

# task-01-workspace-members-api — `GET /api/workspaces/{tenantId}/members`, the roster as live memberships ∪ live invitations

## Context

**Closes: NW-04.** Decision row: `reports/architecture/waves/w14.md`, row
**NW-04** (software-architect + security-architect + client-architect). ADRs in
force: **ADR-026 §D3**, **ADR-025 §B / §D.4 / §H T1b**, **ADR-009 w14 footer
clause 7**, **ADR-022 w14 footer** (`X-Tenant-Id` is not an input to a
membership route).

`WorkspaceEndpointExtensions.cs:31-32` had no member-list route, so
`web/src/routes/workspace/members/memberStore.ts:57,77` reads and writes
`sessionStorage` and `memberViewModel.ts:39` renders
`INVITATION_SENT_MESSAGE` after the POST: the Members table is this session's
optimistic echo of invites made in this tab.

**This task owns `WorkspaceMembersEndpointExtensions.cs` in phase 2** — the file
`E14/F02/US01/T01` created in phase 1 when it split the route groups. It also
owns `WorkspaceMembershipService.cs` in phase 2. **It writes no contract file**:
its OpenAPI entry and its `getWorkspaceMembers(tenantId)` client method are
written by the phase-2 contract owner, `E14/F03/US01/T01`, from ADR-026 §D3.
The contract is reproduced below; **implement exactly it**.

- **Architecture decisions in force**: ADR-026 §D3, ADR-025 §D.4 (any live
  member reads; Admin writes), §B (401 / 404, never 403 for a non-member),
  ADR-009 w14 footer clause 7 (verify-then-scope).
- **Do not touch**: `web/openapi/raffa-api.v1.json`,
  `web/src/api/generated/schema.ts`, `web/src/api/client.ts`
  (`E14/F03/US01/T01` is the phase-2 contract owner);
  `WorkspaceEndpointExtensions.cs` and `WorkspaceProvisioningService.cs`
  (`E14/F03/US01/T01`, same phase); `WorkspaceRoleResolver.cs`
  (`E14/F02/US02/T01`, same phase); `WorkspaceInvitesEndpointExtensions.cs`
  (`E15/F01/US01/T01`, phase 3); `Migrations/**` and `identity-workspace.sql`
  (**no migration here**); `Program.cs`; anything under `web/src`.

## Coding objective

Implement the roster handler and its query.

```
GET /api/workspaces/{tenantId}/members
  headers: X-User-Id            // through ICallerIdentity; NOT X-Tenant-Id
  200 -> { members: [ { id, email, name?, role, status } ] }
```

1. **Verify, then scope, then read** (ADR-009 w14 footer clause 7). Resolve the
   caller identity through `ICallerIdentity`
   (`backend/src/Raffa.Api/Infrastructure/CallerIdentity.cs`); absent → **401**.
   Verify the caller holds a **live `workspace_membership`** in the **route**
   tenant *before* entering a scope for it; not a member → **404**, with a body
   that carries **no tenant name**. Never 403 (a tenant-existence oracle) and
   never an empty 200 (also an oracle: "that tenant exists and is empty").
2. Add `ListMembersAsync(TenantId tenantId, CancellationToken ct)` to
   `backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceMembershipService.cs`
   (**not** a new service — a new service would need a DI registration in
   `ServiceCollectionExtensions.cs`, which `E14/F03/US01/T01` owns this phase).
   The roster is **live memberships ∪ live invitations**:
   - live `workspace_membership` → `status: "Active"`
   - no membership **and** a live `workspace_invitation` (`accepted_at IS NULL`,
     `revoked_at IS NULL`, `expires_at` in the future) → `status: "Invited"`
   - **never a scan of `workspace_user`.** A removed member keeps their user row
     by design (audit continuity, and `WorkspaceSignIn` needs it), so a
     user-driven roster would list removed people and — because their
     `ExternalSubjectId` is still bound — would render them **`Active`**. That
     defect is the reason this clause exists.
3. `status` is **derived, never stored**. A stored status column would be a
   second source of truth every write path would have to keep in step.
4. `name` maps to the existing nullable `WorkspaceUser.DisplayName`
   (`WorkspaceUser.cs:28`) and is emitted only when a real stored value exists.
   **Never derive it from the email address** — `MembersTable.tsx:13-16`
   deliberately refuses to invent one.
5. A person holding two memberships appears **once**, at the highest role,
   reusing the precedence `WorkspaceRoleResolver.cs:101-103,116-118` already
   applies. **No second ordering is invented.**
6. `role` and `status` are **non-nullable strings** on the wire (ADR-026
   implication 7: the generator checks `enum` before the nullable branch, so a
   nullable enum silently loses its `null`). Drop `invitedAt` — no consumer.
7. Register the route inside `MapWorkspaceMemberEndpoints()` in that same file.
   Its group is already wired into the host by the phase-1 route-group split, so
   **add no `Program.cs` line and change no other file to reach it.**

## Parent story AC covered

- AC-1, AC-2, AC-3, AC-4, AC-5, AC-6, AC-7

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Api/WorkspaceMembersEndpointExtensions.cs` | map `GET /api/workspaces/{tenantId}/members`; verify-then-scope; 401 / 404 |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceMembershipService.cs` | add `ListMembersAsync` — memberships ∪ live invitations, derived status, highest role once |
| `backend/tests/Raffa.Api.Tests/WorkspaceMembersEndpointTests.cs` | new — T1b, the 401, the Active/Invited derivation, the two-membership case |
| `backend/tests/Raffa.Identity.Workspace.Tests/WorkspaceRosterTests.cs` | new — a removed member is absent, not `Active`; an expired invitation is absent |

## Context the implementer needs

- **`WorkspaceMembershipService.InviteAsync` still writes a live membership at
  invite time in phase 2.** `E15/F01/US01/T01` (phase 3) moves membership to
  accept time and adds the invitation write. Write `ListMembersAsync` so it is
  **already correct for both**: it reads `workspace_membership` and
  `workspace_invitation` and derives; it does not care which write path created
  them. Do **not** change `InviteAsync` here.
- Build any fixture that needs a Procurement member **directly through the
  `DbContext`**, not through `InviteAsync`, for the same reason.
- The design oracle is `inputs/design/prototypes/raffa-v2/screens-v2.md` §10
  (`:147-154`). Note the two anchors the w14 record explicitly **supersedes**:
  the table is **Member · Role · Status** (three columns), **not**
  `name · email · role · status` — the backend stores no member name — and the
  Admin summary is `memberViewModel.ts:30-33`'s D8 / R-WEB-07 wording, **not**
  `markup.html:395`'s "Also uploads, deletes, manages members".
- Docker Desktop does not start on the operator's machine; Postgres
  Testcontainers suites run in CI only.
- **Do not touch**: the three contract files under `web/`, any other
  `*EndpointExtensions.cs`, `WorkspaceProvisioningService.cs`,
  `WorkspaceRoleResolver.cs`, `Migrations/**`, `Program.cs`,
  `WorkspacePrincipalAuthorization.cs`.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests/Raffa.Api.Tests.csproj` exits 0
- [ ] `dotnet test backend/tests/Raffa.Identity.Workspace.Tests/Raffa.Identity.Workspace.Tests.csproj` exits 0
- [ ] `rg -n "X-Tenant-Id" backend/src/Raffa.Api/WorkspaceMembersEndpointExtensions.cs`
      returns nothing
- [ ] `rg -n "WorkspaceUsers" backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceMembershipService.cs`
      shows `ListMembersAsync` does not drive the roster from that set

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| integration | **T1b** — a non-member gets 404 and the body carries no tenant name | `backend/tests/Raffa.Api.Tests/WorkspaceMembersEndpointTests.cs` |
| integration | a live member gets 200; absent identity → 401; `X-Tenant-Id` is ignored | `backend/tests/Raffa.Api.Tests/WorkspaceMembersEndpointTests.cs` |
| unit | membership → `Active`; invitation only → `Invited`; a **removed** member (user row kept, membership gone) is **absent**, not `Active` | `backend/tests/Raffa.Identity.Workspace.Tests/WorkspaceRosterTests.cs` |
| unit | an expired / revoked / accepted invitation does not render `Invited`; two memberships render once at the highest role; `name` is null unless `DisplayName` is stored | `backend/tests/Raffa.Identity.Workspace.Tests/WorkspaceRosterTests.cs` |

## Open questions blocking this task

- **OQ-w14-005** — answered at the table: any live member reads, only an Admin
  writes, a non-member gets 404.

## Wave-spec entry
```yaml
- id: E14/F04/US01/T01
  prompt: reports/workitems/epic-14-workspace-identity/feature-04-workspace-roster/us-01-members-api/tasks/task-01-workspace-members-api.md
  produces: [workspace-roster-api]
  depends_on: [workspace-schema, workspace-bootstrap]
  effort: M
  layer: backend
  status: live
```
