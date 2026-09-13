---
id: E14/F02/US01/T01
type: task
story: us-01-creator-is-workspace-admin
wave: w14
status: live
target_repo: raffa-backend
---

# task-01-creator-membership-and-invite-guard — the creator becomes Admin, the invite endpoint is guarded, and the route file is split by group

## Context

**Closes: NW-02, NW-58** (the ADR-025 §D.1a authorization guard half).
Decision rows: `reports/architecture/waves/w14.md`, row **NW-02**
(software-architect + security-architect) and row **NW-58** (security-architect:
"the invite endpoint has no authorization at all today and this wave is what
makes that exploitable"; delivery-manager: "the guard folds into the phase-1
task that already owns `WorkspaceEndpointExtensions.cs`"). ADRs in force:
**ADR-025 §A / §B / §D.1 / §D.2**, **ADR-026 §D5 and implications 5 and 6**,
**ADR-009 w14 footer clause 6**, **ADR-022 w14 footer**.

Today `WorkspaceProvisioningService.cs:40-46` adds `db.Workspaces` +
`db.WorkspaceRoles` only — no `WorkspaceMembership`, no `WorkspaceUser` — and
`CreateWorkspaceAsync` (`WorkspaceEndpointExtensions.cs:36-57`) takes only a
name, so no caller identity ever reaches the service. The creator of a workspace
is a member of nothing, and every membership-backed read (NW-01, NW-04) and
every Admin-only write (NW-14) fails for the one person who just created the
tenant.

And `InviteAsync` (`WorkspaceEndpointExtensions.cs:59-97`) takes a route
`tenantId`, a body, the service and a `CancellationToken` — **no `HttpContext`,
no claim, no membership check**; `MapPost` `:32` attaches no authorization
metadata, the role comes from the request body (`:75`) with `Admin` accepted
(`:78`), and `WorkspaceMembershipService.InviteAsync` writes a **live**
membership (`:98-105`). Anyone who can reach the API and guesses a tenant GUID
can grant themselves Admin over another customer's contracts. It has been
survivable only because discovery is a `localStorage` array in one browser —
**`E14/F03/US01/T01` is exactly what makes it live**, which is why the guard
ships here, one phase earlier.

**This task owns `WorkspaceEndpointExtensions.cs` in phase 1** and is the task
that splits it. `E14/F03/US01/T01` (phase 2) and `E15/F01/US01/T01` (phase 3)
own the resulting files in their own phases.

- **Architecture decisions in force**: ADR-025 (§A one identity seam, §B 401 /
  404 / 403, §D.1a the guard, §D.2 the bootstrap), ADR-026 (§D5 the 201 body,
  implication 5 `Program.cs`, implication 6 the file split), ADR-009 w14 footer
  clause 6, ADR-022 w14 footer.
- **Do not touch**: `Migrations/**` and `identity-workspace.sql`
  (`E14/F01/US01/T02` owns them — **no migration is needed here, and none may
  be added**); **all of `backend/src/Raffa.SharedKernel/Tenancy/`** —
  `TenantRlsConnectionInterceptor.cs`, `ITenantContext.cs` and the new
  `CallerIdentityContext.cs` are `E14/F01/US01/T01`'s for the whole wave, and
  the last two **do not exist on this task's base**;
  `WorkspaceRoleResolver.cs` (`E14/F02/US02/T01`, phase 2);
  `backend/src/Raffa.Identity.Workspace/Domain/WorkspacePrincipalAuthorization.cs`
  (ADR-025 §I — **no w14 task edits it**); the `workspace` profile columns and
  `GET /api/workspaces` (`E14/F03/US01/T01`, phase 2).

## Coding objective

**1. The identity seam.** Add `ICallerIdentity` in
`backend/src/Raffa.Api/Infrastructure/CallerIdentity.cs` with a single
`string? Resolve()` that reads the interim `X-User-Id` header off
`IHttpContextAccessor`, trims it and lower-cases it. **Every** endpoint added or
changed in this wave consumes this one seam; **no endpoint reads
`HttpRequest.Headers` directly** (ADR-025 §A2). W15 (NW-05) replaces the header
with the validated token subject in this one file. Register it where
`WorkspaceRoleResolver` is registered today; if that is `Program.cs`, this task
is phase 1's sole writer of `Program.cs` and adds exactly one line.

**Do not name `ICallerIdentityContext` here, and do not open an identity
scope.** That type does not exist on this task's base — `E14/F01/US01/T01`
creates it in **this same phase**
(`backend/src/Raffa.SharedKernel/Tenancy/ITenantContext.cs` +
`CallerIdentityContext.cs`), and the fan-out runs the two tasks concurrently
from the same base, so referencing it would leave this branch uncompilable
until a sibling's branch merged. That is the e13 union-merge defect verbatim
(`MarketEndpointExtensions.cs` created by one task, `MapMarketEndpoints()`
written by a sibling of the same phase → 18 CI errors). **Nothing in phase 1
needs `app.identity_subject`**: the bootstrap writes inside the existing
`BeginScope(workspace.TenantId)` **tenant** scope, and the invite guard resolves
membership in the **route** tenant through `WorkspaceRoleResolver`, which opens
its own tenant scope (`WorkspaceRoleResolver.cs:99`). The request identity scope
is opened by **`E14/F03/US01/T01` in phase 2** — the wave's first and only
reader of `workspace_user` outside a tenant scope — which depends on both this
task (`workspace-bootstrap`) and `E14/F01/US01/T01` (`rls-identity-guc`), so
both halves of the seam exist by then. Registering `ICallerIdentity` is this
task's whole share of the identity wiring.

**2. `POST /api/workspaces` writes the creator's membership.** Change
`WorkspaceProvisioningService.CreateWorkspaceAsync` to
`CreateWorkspaceAsync(string name, string callerIdentity, CancellationToken ct)`
and write `workspace` + the five `workspace_role` rows + one `workspace_user` +
one `workspace_membership` (Admin) in **one `SaveChangesAsync`** inside the
**existing single** `BeginScope(workspace.TenantId)` at `:42`. Reuse
`WorkspaceMembershipFactory.CreateInvitedUser` / `CreateMembership` — do not
write new construction logic. The endpoint returns
`201 -> { id, name, createdAt, role: "Admin" }` so the SPA can enter without a
second call.
- Absent identity → **401** (`Results.Unauthorized()`), **not** 400. This is a
  deliberate change of posture from today's anonymous signup step
  (`WorkspaceEndpointExtensions.cs:17-18`, "nobody has a tenant claim yet, by
  definition") because the endpoint now writes an **identity-keyed grant**. It
  matches `AuditEndpointExtensions.cs:33-34`, which already maps
  `Unauthenticated` → 401. A malformed body stays 400 through the existing
  `Result<T>` failure path (`WorkspaceProvisioningService.cs:35-38`).
- A `role` field in the body is **ignored**, never honoured.
- The four writes must be atomic: a partial bootstrap must not be reachable **by
  a failure path either**. Keep it strictly one-scope-per-request.

**3. The invite guard (ADR-025 §D.1a).** `POST /api/workspaces/{tenantId}/invites`
now resolves, in order: identity present → else **401**; a **live
`workspace_membership`** for that identity in the **route** tenant → else
**404** (never 403 — that is a tenant-existence oracle); that membership's role
is `Admin` → else **403**. Take the tenant from the **route**, never from
`X-Tenant-Id` (ADR-022 w14 footer: "`X-Tenant-Id` is not an input to a
membership route"). An Admin may invite another Admin; a non-Admin reaching the
endpoint and any **self**-assignment are refused. Resolve the role through the
existing membership branch of `WorkspaceRoleResolver` (`:85-119`) — do **not**
edit that file (its header branch is deleted in phase 2 by
`E14/F02/US02/T01`); call it.

**4. Split the route file by group (ADR-026 implication 6).** Keep
`MapWorkspaceEndpoints()` in `WorkspaceEndpointExtensions.cs` as the **single
entry point** — `Program.cs:225` already calls it and must stay untouched by
this half — and have it call two new group mappers:
- `WorkspaceEndpointExtensions.cs` — `MapWorkspaceEndpoints()` +
  `POST /api/workspaces` (and, from phase 2, `GET /api/workspaces`)
- `WorkspaceMembersEndpointExtensions.cs` (new) —
  `MapWorkspaceMemberEndpoints()`, initially empty of routes; phase 2 adds
  `GET /api/workspaces/{tenantId}/members`, phase 3 adds the DELETE
- `WorkspaceInvitesEndpointExtensions.cs` (new) —
  `MapWorkspaceInviteEndpoints()` + `POST /api/workspaces/{tenantId}/invites`
  with the guard above

This is what lets four later tasks write workspace routes without ever sharing a
file inside one phase. **Do not add a `Program.cs` line for the two new files** —
they are mapped from inside `MapWorkspaceEndpoints()`.

## Parent story AC covered

- AC-1, AC-2, AC-3, AC-4, AC-5, AC-6

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Api/Infrastructure/CallerIdentity.cs` | new — `ICallerIdentity` + the `X-User-Id` implementation, the one seam W15 retires |
| `backend/src/Raffa.Api/WorkspaceEndpointExtensions.cs` | `POST /api/workspaces` takes the caller identity (401 when absent), returns `role: "Admin"`; `MapWorkspaceEndpoints()` calls the two new group mappers; the invite handler moves out |
| `backend/src/Raffa.Api/WorkspaceMembersEndpointExtensions.cs` | new — `MapWorkspaceMemberEndpoints()`, the members route group (no routes yet) |
| `backend/src/Raffa.Api/WorkspaceInvitesEndpointExtensions.cs` | new — `MapWorkspaceInviteEndpoints()` + the guarded `POST …/{tenantId}/invites` |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceProvisioningService.cs` | `CreateWorkspaceAsync(name, callerIdentity, ct)`; four writes, one `SaveChangesAsync`, one `BeginScope` |
| `backend/src/Raffa.Api/Program.cs` | **only if** `ICallerIdentity` / `IHttpContextAccessor` registration cannot go where `WorkspaceRoleResolver` is registered — then exactly one line, and this task is phase 1's sole writer of the file |
| `backend/tests/Raffa.Api.Tests/WorkspaceBootstrapEndpointTests.cs` | new — T4 (create → three rows, 401, ignored `role`) |
| `backend/tests/Raffa.Api.Tests/WorkspaceInviteAuthorizationTests.cs` | new — T3 (the invite-hole regression: 401 → 404 → 403 → 201) |
| `backend/tests/Raffa.Identity.Workspace.Tests/WorkspaceProvisioningServiceTests.cs` | new or extended — the bootstrap writes all four rows in one scope |

## Context the implementer needs

- `WorkspaceSignIn.ResolveSignedInUser` (`WorkspaceSignIn.cs:23-25,36-41`) holds
  the invariant "sign-in never provisions a new workspace user". Nothing here
  may break it.
- `WorkspaceMembershipService.InviteAsync` still writes a live membership at
  invite time in phase 1 — that is deliberate and is changed in phase 3 by
  `E15/F01/US01/T01`. **Do not change it here.**
- **Write test fixtures that need a Procurement member directly through the
  `DbContext`, not through `InviteAsync`.** Phase 3 moves membership to accept
  time; a fixture built on `InviteAsync` will break then. This is a recorded
  ordering correction from the w14 table (NW-14 row, client-architect).
- `WorkspaceRoleClaimResolver` already accepts Admin / Procurement / Legal /
  Finance / ReadOnly (`WorkspaceEndpointExtensions.cs:75-79`). Keep that
  vocabulary; do not narrow it.
- **`ICallerIdentityContext` / `BeginIdentityScope` belong to `E14/F01/US01/T01`
  and are consumed for the first time by `E14/F03/US01/T01` in phase 2.** If a
  handler here looks like it needs the identity GUC, it does not — re-read the
  Coding objective §1 note and use the tenant scope. **HALT and name
  `E14/F03/US01/T01`** rather than adding the type yourself.
- **Do not touch**: `Migrations/**`, `identity-workspace.sql`,
  `backend/src/Raffa.SharedKernel/Tenancy/**` (interceptor, `ITenantContext.cs`,
  `CallerIdentityContext.cs`), `WorkspaceRoleResolver.cs`,
  `WorkspacePrincipalAuthorization.cs`, `web/**`.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests/Raffa.Api.Tests.csproj` exits 0
- [ ] `dotnet test backend/tests/Raffa.Identity.Workspace.Tests/Raffa.Identity.Workspace.Tests.csproj` exits 0
- [ ] `dotnet test backend/tests/Raffa.ArchitectureTests/Raffa.ArchitectureTests.csproj` exits 0
      (`DependencyDirectionTests` — `Raffa.Identity.Workspace` may reference only
      `Raffa.SharedKernel`)
- [ ] `rg -n "Headers\[\"X-User-Id\"\]|Headers\.\[?\"X-User-Id" backend/src/Raffa.Api/Workspace*.cs`
      returns nothing — every handler goes through `ICallerIdentity`
- [ ] `rg -n "MapWorkspaceEndpoints" backend/src/Raffa.Api/Program.cs` still
      shows exactly one call and it is unchanged
- [ ] `rg -n "ICallerIdentityContext|BeginIdentityScope" backend/src/Raffa.Api backend/src/Raffa.Identity.Workspace backend/tests/Raffa.Api.Tests backend/tests/Raffa.Identity.Workspace.Tests`
      returns **nothing** — this task never names its phase-1 sibling's type, so
      the branch compiles against the wave's base alone (the e13 union-merge rule)
- [ ] `dotnet build backend/Raffa.slnx` passes **on this task's branch with no
      phase-1 sibling merged in** — that is the real check behind the grep above

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| integration | T4 — create → `workspace` + `workspace_user` + `workspace_membership`(Admin); no identity → 401; a `role` in the body is ignored | `backend/tests/Raffa.Api.Tests/WorkspaceBootstrapEndpointTests.cs` |
| integration | **T3 (the regression that must exist)** — invite with no identity → 401; non-member → 404; Procurement → 403; Admin → 201 | `backend/tests/Raffa.Api.Tests/WorkspaceInviteAuthorizationTests.cs` |
| unit | the four bootstrap writes land in one scope and one `SaveChangesAsync`; a failure leaves no partial tenant | `backend/tests/Raffa.Identity.Workspace.Tests/WorkspaceProvisioningServiceTests.cs` |

## Open questions blocking this task

- **OQ-w14-001** — which identity keys membership. **Assumption in force**: the
  interim `X-User-Id`, lower-cased, behind `ICallerIdentity`.

## Wave-spec entry
```yaml
- id: E14/F02/US01/T01
  prompt: reports/workitems/epic-14-workspace-identity/feature-02-workspace-bootstrap-and-admin-authority/us-01-creator-is-workspace-admin/tasks/task-01-creator-membership-and-invite-guard.md
  produces: [workspace-bootstrap]
  depends_on: []
  effort: L
  layer: backend
  status: live
```
