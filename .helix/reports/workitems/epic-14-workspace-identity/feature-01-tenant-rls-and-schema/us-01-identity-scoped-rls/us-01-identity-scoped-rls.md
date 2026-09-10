---
id: us-01
type: user-story
parent: feature-01
wave: w14
status: active
---

# us-01-identity-scoped-rls — a caller with no tenant claim can discover their tenants, and only those

## Story

As the **platform**, I want a signed-in identity to be able to read *only its
own* `workspace_user` rows when no tenant claim is set, so that
`GET /api/workspaces` can find the caller's tenants without `BYPASSRLS`, a
cross-tenant directory table, or a client-supplied tenant header.

## Acceptance criteria

- [ ] AC-1 With `app.identity_subject` set to identity `A` and **no**
      `app.tenant_id`, a direct `SELECT` on `workspace_user` returns only `A`'s
      own rows; the same connection reading `workspace`, `workspace_role` and
      `workspace_membership` returns **zero** rows (ADR-025 §H T1c/T1d).
- [ ] AC-2 With `app.identity_subject` unset or empty, `workspace_user` returns
      zero rows — the widening is fail-closed, never a widening by default.
- [ ] AC-3 An identity containing `'`, `;` and `--` sets no other GUC and does
      **not** repoint `app.tenant_id` on that connection (T13).
- [ ] AC-4 Outside any tenant scope, `workspace_invitation` reads return zero
      rows and an insert is rejected by `WITH CHECK` (T5).
- [ ] AC-5 `workspace` carries nullable `industry`, `country` and `currency`
      columns; existing rows are untouched and render as absent, never as an
      invented `"CHF"`.
- [ ] AC-6 `dotnet test backend/Raffa.slnx` is green, including
      `IdentityWorkspaceMigrationScriptStaleCheckTests` (the checked-in
      `identity-workspace.sql` byte-matches an in-process regeneration) and
      `TenantRlsDeployableScriptCheckTests` (every `TenantScopedEntity`
      subclass has its policy).

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| none | Both tasks are `deps: []`, phase 1. Phase 1 is the wave's single point of failure, so both are deliberately small |

## Architecture decisions in force

- **ADR-009 (w14 footer)** — exactly **one** new policy: `identity_self` on
  `workspace_user`, **`FOR SELECT` only** (no `WITH CHECK`, so it can never
  authorize a write), keyed on `app.identity_subject`, fail-closed via
  `nullif(current_setting(...),'') IS NOT NULL`. The other three tables keep
  exactly one policy each, unchanged. `BYPASSRLS` is forbidden verbatim
  (ADR-009 `:57`).
- **ADR-025 §F.2** — the GUC is a SQL-injection sink and **must** be
  parameter-bound.
- **ADR-026 §D4** — `workspace_invitation` is an ordinary tenant-scoped table
  with `ENABLE` + `FORCE ROW LEVEL SECURITY` and its `tenant_isolation` policy
  **in the same migration**. It gets **no** `identity_self` policy.
- **ADR-003 (w14 footer)** — three nullable columns; a NOT NULL column on a
  populated table would need a fabricated business default (trap documented at
  `QuoteLineConfiguration.cs:45-49`).

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Identity GUC on the tenant RLS interceptor | M | phase-1 |
| task-02 | Three migrations: identity_self policy, workspace profile columns, workspace_invitation | M | phase-1 |

## Council decisions carried into this story

- `SELECT set_config('app.identity_subject', @identity, false)` with a **real
  `DbParameter`** — never the interpolating `BuildSetCommandText` path
  (`TenantRlsConnectionInterceptor.cs:103-107`), whose own comment justifies
  interpolation *solely* because `TenantId` wraps a Guid. The third argument is
  `false` (session-scoped); `true` would be transaction-local and would
  silently vanish for the discovery read. `RESET app.identity_subject` on
  connection close, alongside the tenant reset. The asymmetry between the two
  GUCs **must be commented** so a later reader does not "tidy" it back into
  `SET`. There is no existing `set_config` call in `backend/src`.
- `workspace_invitation` columns: `id uuid` PK (`ValueGeneratedNever`),
  `tenant_id uuid`, `email varchar(320)` (stored lower-cased),
  `workspace_role_id uuid` FK, `token_hash varchar(128)`,
  `invited_by varchar(320)`, `created_at`, `expires_at` timestamptz NOT NULL,
  `accepted_at`, `revoked_at` timestamptz **nullable**.
- Indexes: unique `(tenant_id, token_hash)`; plain `tenant_id`; partial unique
  `(tenant_id, lower(email)) WHERE accepted_at IS NULL AND revoked_at IS NULL`.
- `industry varchar(120)`, `country varchar(2)` (ISO 3166-1 alpha-2),
  `currency varchar(3)` (ISO 4217) — all nullable, on `workspace`.
- Known and accepted: the `identity_self` predicate compares `lower(email)`,
  which cannot use `ix_workspace_user_tenant_id_email`. Discovery is a
  sequential scan of `workspace_user`; at pilot scale that is tens of rows.
  **The fix, if ever needed, is an expression index — not a change to the
  policy** (ADR-026 Consequences).

## Open questions

- **OQ-w14-001** — which identity keys `workspace_membership` in w14.
  **Assumption in force**: the interim `X-User-Id` (MSAL account username),
  normalised to lower case, behind one `ICallerIdentity` seam. W15 (NW-05)
  swaps in the token `sub`/`oid` without touching callers.
