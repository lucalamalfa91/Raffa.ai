---
id: E14/F01/US01/T02
type: task
story: us-01-identity-scoped-rls
wave: w14
status: live
target_repo: raffa-backend
---

# task-02-workspace-schema — three migrations: the `identity_self` policy, the workspace profile columns, `workspace_invitation`

## Context

**Closes: NW-01, NW-24, NW-58** (the schema half of each).
Decision rows: `reports/architecture/waves/w14.md`, rows **NW-01**
(security-architect: the `identity_self` policy), **NW-24** (software-architect:
three nullable columns) and **NW-58** (software-architect: the new table). ADRs
in force: **ADR-009 w14 footer clauses 1–5 and 8**, **ADR-025 §F.1 / §F.4**,
**ADR-026 §D4 and implication 4**, **ADR-003 w14 footer clauses 1–5**,
**ADR-021** (checked-in idempotent SQL applied by CI).

**This task owns `Migrations/Scripts/identity-workspace.sql` for the whole
wave.** ADR-026 implication 4: three migrations regenerate one file, which
`IdentityWorkspaceMigrationScriptStaleCheckTests` byte-compares against an
in-process regeneration. **Never hand-edit the `.sql`.** Two tasks regenerating
concurrently *will* conflict — no other w14 task touches `Migrations/**`.

- **Architecture decisions in force**: ADR-009 (one new policy, `FOR SELECT`
  only, fail-closed; `BYPASSRLS` forbidden at `:57`), ADR-026 §D4, ADR-003 w14
  footer, ADR-021.
- **Do not touch**: `TenantRlsConnectionInterceptor.cs` (that is
  `E14/F01/US01/T01`, sole owner for the wave); `WorkspaceProvisioningService.cs`,
  `WorkspaceMembershipService.cs` or any `*EndpointExtensions.cs` (phase-1 and
  later siblings); `.github/workflows/backend.yml` — the script is **already** in
  both CI arrays (`:276-286` apply, `:308-318` verify), so no workflow edit is
  needed for these migrations.

## Coding objective

Add three EF Core migrations to `backend/src/Raffa.Identity.Workspace`, in this
order, then regenerate the deployable script.

**1. `AddWorkspaceUserIdentitySelfReadPolicy`** — exactly **one** new policy,
`identity_self` on `workspace_user`:
- `FOR SELECT` **only**. No `WITH CHECK` clause, so it can never authorize a
  write.
- Predicate: `nullif(current_setting('app.identity_subject', true), '') IS NOT NULL
  AND lower(email) = nullif(current_setting('app.identity_subject', true), '')`
  — the `nullif` guard is what makes it **fail-closed**: an absent or empty
  claim widens nothing.
- The other three tables (`workspace`, `workspace_role`,
  `workspace_membership`) keep **exactly one policy each, unchanged**. The
  widening must stay auditable by reading six lines.
- `BYPASSRLS` is forbidden verbatim by ADR-009 `:57`. A cross-tenant directory
  table and identity-keyed policies on all four tables were both considered and
  rejected (ADR-026 options 3 and the "clever RLS is how holes happen" note).

**2. `AddWorkspaceProfileColumns`** — three columns on `workspace`, **all
nullable**: `industry varchar(120)`, `country varchar(2)` (ISO 3166-1 alpha-2),
`currency varchar(3)` (ISO 4217). Nullable is deliberate: existing rows predate
the columns and a NOT NULL column on a populated table needs a default that
would be a fabricated business fact — precedent `quotes.sql:163,170,195,202`,
trap documented at `QuoteLineConfiguration.cs:45-49` (EF backfills a NOT NULL
enum-as-string with `""` the converter cannot parse). Add the properties to
`WorkspaceTenant` and map them in `WorkspaceTenantConfiguration`. Do **not** add
a `domain` column — the ux-ui-designer withdrew that ask (w14 record, NW-58 row).

**3. `AddWorkspaceInvitation`** — the new `workspace_invitation` table, an
**ordinary tenant-scoped entity** (`TenantScopedEntity` subclass), with its
`tenant_isolation` policy **in the same migration** —
`TenantRlsDeployableScriptCheckTests` discovers `TenantScopedEntity` subclasses
from the EF model and **fails the build** if the policy is missing. `ENABLE` +
`FORCE ROW LEVEL SECURITY`, exactly like the other three tables. It gets **no**
`identity_self` policy: that widening is confined to `workspace_user`.

| Column | Type | Null | Note |
|---|---|---|---|
| `id` | uuid | no | PK, `ValueGeneratedNever` per house style |
| `tenant_id` | uuid | no | RLS axis; plain index |
| `email` | varchar(320) | no | RFC 5321, matching `workspace_user.email`; stored lower-cased |
| `workspace_role_id` | uuid | no | FK to `workspace_role` — the offered role |
| `token_hash` | varchar(128) | no | SHA-256 of the secret half of the token |
| `invited_by` | varchar(320) | no | the inviting identity |
| `created_at` | timestamptz | no | |
| `expires_at` | timestamptz | no | absolute, evaluated against `IClock` |
| `accepted_at` | timestamptz | **yes** | null until accepted |
| `revoked_at` | timestamptz | **yes** | null until revoked |

Indexes, tenant-first per the established convention
(`WorkspaceUserConfiguration.cs:29`, `suppliers.sql:37`, `quotes.sql:233`):
unique `(tenant_id, token_hash)`; a plain `tenant_id` index; and the partial
unique index `(tenant_id, lower(email)) WHERE accepted_at IS NULL AND
revoked_at IS NULL`, which keeps at most **one live invitation per address per
tenant** so re-invites cannot accumulate valid links.

**Then regenerate** with `dotnet ef migrations script --idempotent` from
`backend/src/Raffa.Identity.Workspace` into
`Migrations/Scripts/identity-workspace.sql`.

## Parent story AC covered

- AC-1 (the `identity_self` policy is the only widening)
- AC-4 (`workspace_invitation` is fail-closed outside a scope)
- AC-5 (three nullable profile columns)
- AC-6 (both script-check suites stay green)

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Identity.Workspace/Domain/WorkspaceTenant.cs` | add `Industry`, `Country`, `Currency` (all `string?`) |
| `backend/src/Raffa.Identity.Workspace/Domain/WorkspaceInvitation.cs` | new — `TenantScopedEntity` subclass, the table above |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/Configurations/WorkspaceTenantConfiguration.cs` | map the three columns |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/Configurations/WorkspaceInvitationConfiguration.cs` | new — columns, the three indexes |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/IdentityWorkspaceDbContext.cs` | add the `DbSet<WorkspaceInvitation>` |
| `backend/src/Raffa.Identity.Workspace/Migrations/` | three new migrations + the regenerated `IdentityWorkspaceDbContextModelSnapshot.cs` |
| `backend/src/Raffa.Identity.Workspace/Migrations/Scripts/identity-workspace.sql` | **regenerated**, never hand-edited |
| `backend/tests/Raffa.Tenancy/WorkspaceInvitationRlsTests.cs` | new — T5 |
| `backend/tests/Raffa.Tenancy/WorkspaceUserIdentitySelfPolicyTests.cs` | new — T1c / T1d and the fail-closed case |

## Context the implementer needs

- **The GUC name is fixed by the sibling task**: `app.identity_subject`
  (`E14/F01/US01/T01` sets it). The policy reads it with
  `current_setting('app.identity_subject', true)`.
- `identity-workspace.sql` already carries `ix_workspace_membership_workspace_user_id_workspace_role_id`
  (`:90`) — that unique index is what makes two concurrent accepts produce one
  row and one unique violation (`E15/F01/US01/T01` translates it to 409). Do not
  drop or widen it.
- `UNIQUE INDEX ix_workspace_tenant_id` (`:69`) and the `WITH CHECK` at
  `:145-147` are the reason the four bootstrap writes can land in one scope
  (`E14/F02/US01/T01`). Do not relax them.
- `ix_workspace_user_tenant_id_email` is on plain `email` (SQL `:118`), so the
  `lower(email)` predicate cannot use it. **This is accepted** (ADR-026
  Consequences). Do **not** "fix" it by changing the policy or the column type
  to `citext`.
- Docker Desktop does not start on the operator's machine; the Testcontainers
  suites run in CI. Do not weaken a test so it runs locally.
- **Do not touch**: `TenantRlsConnectionInterceptor.cs`, any
  `*EndpointExtensions.cs`, `WorkspaceProvisioningService.cs`,
  `WorkspaceMembershipService.cs`, `.github/workflows/backend.yml`.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Tenancy/Raffa.Tenancy.csproj` exits 0 —
      including `TenantRlsDeployableScriptCheckTests` and
      `IdentityWorkspaceMigrationScriptStaleCheckTests`
- [ ] `dotnet ef migrations script --idempotent` from
      `backend/src/Raffa.Identity.Workspace` reproduces the checked-in
      `Migrations/Scripts/identity-workspace.sql` byte for byte
- [ ] `rg -n "BYPASSRLS|CREATE POLICY" backend/src/Raffa.Identity.Workspace/Migrations/Scripts/identity-workspace.sql`
      shows no `BYPASSRLS` and exactly **five** policies: four `tenant_isolation`
      (one per tenant table, `workspace_invitation` included) + one
      `identity_self` on `workspace_user`

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| integration (Testcontainers) | T1c / T1d — `identity_self` returns only the caller's own `workspace_user` rows; `workspace`, `workspace_role`, `workspace_membership` return zero under the same claim | `backend/tests/Raffa.Tenancy/WorkspaceUserIdentitySelfPolicyTests.cs` |
| integration (Testcontainers) | fail-closed — absent / empty `app.identity_subject` returns zero rows | `backend/tests/Raffa.Tenancy/WorkspaceUserIdentitySelfPolicyTests.cs` |
| integration (Testcontainers) | T5 — outside any scope, `workspace_invitation` reads return zero and an insert is rejected by `WITH CHECK` | `backend/tests/Raffa.Tenancy/WorkspaceInvitationRlsTests.cs` |
| build-time | the deployable script is not stale and every tenant entity has its policy | `backend/tests/Raffa.Tenancy/TenantRlsDeployableScriptCheckTests.cs` (existing) |

## Open questions blocking this task

- **OQ-w14-004** — which workspace profile fields. **Answered at the table**:
  `industry` + `country` are asked (closed lists); `currency` is **derived from
  country and stored, never typed**; "region" is a business region and does not
  touch ADR-006 (`northeurope` stays). This task adds the columns; the
  derivation lands in `E14/F03/US01/T01`.

## Wave-spec entry
```yaml
- id: E14/F01/US01/T02
  prompt: reports/workitems/epic-14-workspace-identity/feature-01-tenant-rls-and-schema/us-01-identity-scoped-rls/tasks/task-02-workspace-schema.md
  produces: [workspace-schema]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
