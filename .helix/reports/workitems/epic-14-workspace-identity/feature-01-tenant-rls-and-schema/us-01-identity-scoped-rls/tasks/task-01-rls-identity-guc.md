---
id: E14/F01/US01/T01
type: task
story: us-01-identity-scoped-rls
wave: w14
status: live
target_repo: raffa-backend
---

# task-01-rls-identity-guc — set and reset `app.identity_subject` on the tenant RLS connection, with a bind parameter

## Context

**Closes: NW-01** (the RLS mechanism its two-phase discovery stands on).
Decision row: `reports/architecture/waves/w14.md`, row **NW-01**
(security-architect half). ADRs in force: **ADR-009 w14 footer clauses 1–5**,
**ADR-025 §F.1 / §F.2 / §F.3**, **ADR-026 implication 1 and 2**.

`GET /api/workspaces` must answer "which tenants does this caller belong to?"
for a caller whose tenant is the very unknown being discovered. Today the
interceptor leaves `app.tenant_id` unset when there is no claim
(`TenantRlsConnectionInterceptor.cs:53-57,66-70`, fail closed), so **every**
policy in `identity-workspace.sql:143-182` denies every row. This task adds the
second, identity-keyed GUC that `E14/F01/US01/T02`'s `identity_self` policy
reads. The two tasks are siblings in phase 1 and touch **disjoint files**; the
end-to-end proof that the policy and the GUC agree is `E14/F03/US01/T01`'s
(phase 2).

**This file is this task's for the whole wave.** ADR-026 implication 1:
`TenantRlsConnectionInterceptor.cs` is wired into every module's DbContext
options (`Raffa.Identity.Workspace/Infrastructure/ServiceCollectionExtensions.cs:33-35`,
`Raffa.Documents.Contracts/.../DocumentsContractsDbContextOptions.cs:38-41`), so
it is the wave's highest single-writer risk. **No other w14 task may touch it.**

- **Architecture decisions in force**: ADR-009 (RLS, `BYPASSRLS` forbidden at
  `:57`), ADR-025 §F.2 (the injection sink), ADR-026 implication 2.
- **Do not touch**: `Migrations/Scripts/identity-workspace.sql` and anything
  under `Migrations/` (that is `E14/F01/US01/T02`);
  `WorkspaceProvisioningService.cs` or any endpoint file (phase-1 siblings);
  `backend/src/Raffa.Identity.Workspace/Domain/WorkspacePrincipalAuthorization.cs`
  (ADR-025 §I — **no w14 task edits it**).

## Coding objective

Extend `backend/src/Raffa.SharedKernel/Tenancy/TenantRlsConnectionInterceptor.cs`
with a second GUC, `app.identity_subject`, alongside the existing
`app.tenant_id`, and expose the seam that sets it.

1. Add an identity accessor to the tenancy abstraction next to
   `ITenantContext` (`backend/src/Raffa.SharedKernel/Tenancy/ITenantContext.cs`):
   a `ICallerIdentityContext` with a nullable current identity string and a
   `BeginIdentityScope(string identity)` returning `IDisposable`, plus an
   ambient default implementation registered by the same DI path that registers
   the tenant context today. Keep it in `Raffa.SharedKernel/Tenancy/` — every
   module's DbContext already depends on that namespace.
2. On connection open, when an identity is present, execute
   **`SELECT set_config('app.identity_subject', @identity, false)`** with a real
   `DbParameter` (`NpgsqlParameter`). **Never** route it through
   `BuildSetCommandText` (`:103-107`): that method interpolates, and its own
   comment justifies interpolation *solely* because `TenantId` wraps a Guid. An
   identity is caller-controlled text from `X-User-Id`; interpolating it into a
   statement on the connection that also carries `app.tenant_id` would let
   `X-User-Id: x'; SET app.tenant_id = '<victim>'; --` repoint the RLS claim for
   the rest of the request — the exact cross-tenant path ADR-009 `:12-13`
   forbids. The third argument is `false` (session-scoped); `true` would be
   transaction-local and would silently vanish for the discovery read.
3. `RESET app.identity_subject` on connection close, in the same place and the
   same order as the existing tenant reset. Normalise the identity to lower case
   and trim it before it reaches the parameter, matching
   `WorkspaceMembershipFactory.CreateInvitedUser`'s existing trim (`:30`) — the
   `identity_self` policy compares `lower(email)`.
4. **Comment the asymmetry**: `SET` for the tenant (Guid-typed, cannot be bound
   as a GUC), `set_config` with a bind parameter for the identity
   (caller-controlled text). Say in the comment that "tidying" the identity path
   back into `SET` re-opens the injection sink. This is the first `set_config`
   call in `backend/src` — it is a deliberate new pattern, not an oversight.
5. Setting the identity GUC must be **independent** of the tenant GUC: both may
   be set on one connection, either may be absent, and an absent identity must
   leave `app.identity_subject` unset (not empty-string) so the fail-closed
   `nullif(current_setting(...),'') IS NOT NULL` guard in the policy denies.

## Parent story AC covered

- AC-2 (fail-closed when the identity is unset or empty)
- AC-3 (the GUC is not an injection sink)

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.SharedKernel/Tenancy/TenantRlsConnectionInterceptor.cs` | set `app.identity_subject` via `SELECT set_config(…, @p, false)`; `RESET` on close; comment the asymmetry |
| `backend/src/Raffa.SharedKernel/Tenancy/ITenantContext.cs` | add the `ICallerIdentityContext` seam next to `ITenantContext` (do not change `BeginScope(TenantId)`; **no string-taking overload may be added** — ADR-025 Rule C4) |
| `backend/src/Raffa.SharedKernel/Tenancy/CallerIdentityContext.cs` | new — the ambient implementation (`AsyncLocal`), same shape as the existing tenant context |
| `backend/tests/Raffa.Tenancy/TenantRlsIdentitySubjectTests.cs` | new — the T13 negative test and the fail-closed test |

## Context the implementer needs

- **The GUC name is normative**: `app.identity_subject`. The
  `identity_self` policy in `E14/F01/US01/T02` reads exactly this name.
- `backend/tests/Raffa.Tenancy/` already holds
  `TenantRlsCrossTenantIsolationTests.cs`,
  `TenantRlsDeployableScriptCheckTests.cs` and `TenantRlsMigrationCheckTests.cs`
  — follow their fixture style. These are Postgres Testcontainers suites: they
  run in CI, **not** on the operator's machine (Docker Desktop does not start
  there). Do not weaken a test so it runs locally.
- **Do not touch**: `Migrations/**`, any `*EndpointExtensions.cs`,
  `WorkspaceProvisioningService.cs`, `WorkspaceMembershipService.cs`,
  `WorkspacePrincipalAuthorization.cs`.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Tenancy/Raffa.Tenancy.csproj` exits 0
- [ ] `TenantRlsIdentitySubjectTests` proves: (a) an identity containing `'`,
      `;` and `--` sets no other GUC and leaves `app.tenant_id` unchanged on
      that connection; (b) with no identity, `current_setting('app.identity_subject', true)`
      is null or empty on the open connection; (c) the identity survives the
      whole session (third argument `false`), not just the transaction
- [ ] `rg -n "BuildSetCommandText" backend/src/Raffa.SharedKernel` shows the
      identity path does **not** call it

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| integration (Testcontainers) | T13 — the GUC is not an injection sink | `backend/tests/Raffa.Tenancy/TenantRlsIdentitySubjectTests.cs` |
| integration (Testcontainers) | fail-closed: absent identity leaves the GUC unset | `backend/tests/Raffa.Tenancy/TenantRlsIdentitySubjectTests.cs` |
| unit | the identity is trimmed and lower-cased before it reaches the parameter | `backend/tests/Raffa.Tenancy/TenantRlsIdentitySubjectTests.cs` |

## Open questions blocking this task

- **OQ-w14-001** — which identity keys membership in w14. **Assumption in
  force**: the interim `X-User-Id` (MSAL account username), lower-cased. This
  task only carries the value; it does not decide where it comes from.

## Wave-spec entry
```yaml
- id: E14/F01/US01/T01
  prompt: reports/workitems/epic-14-workspace-identity/feature-01-tenant-rls-and-schema/us-01-identity-scoped-rls/tasks/task-01-rls-identity-guc.md
  produces: [rls-identity-guc]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
