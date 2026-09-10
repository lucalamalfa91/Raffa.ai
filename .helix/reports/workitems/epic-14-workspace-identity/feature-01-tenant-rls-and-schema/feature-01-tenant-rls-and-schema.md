---
id: feature-01
type: feature
parent: epic-14
wave: w14
status: active
extends: epic-01 F04 (tenancy / RLS), epic-09 (schema apply)
---

# feature-01-tenant-rls-and-schema — the identity GUC and the w14 migrations

## Slice

Extends **epic-01 F04**'s tenancy plumbing and **epic-09**'s idempotent schema
apply with the two mechanisms every other w14 feature reads: a second,
`SELECT`-only RLS axis (`app.identity_subject` on `workspace_user`) that lets a
caller with **no tenant claim** discover which tenants they belong to, and the
three migrations that add the workspace profile columns and the
`workspace_invitation` table with its policy. Nothing user-visible ships here;
everything user-visible in w14 fails without it.

Both tasks are `deps: []` in phase 1 and own their files for the whole wave:
`TenantRlsConnectionInterceptor.cs` (ADR-026 implication 1, "lands first and
lands alone") and `Migrations/Scripts/identity-workspace.sql` (three migrations
regenerate one file; hand-editing it fails
`IdentityWorkspaceMigrationScriptStaleCheckTests`).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Identity-scoped RLS discovery and the w14 schema | w14 |

## Architecture decisions in force

- ADR-009 (w14 footer, clauses 1–5) — exactly one new policy, `identity_self`, `FOR SELECT` only, fail-closed; the GUC must be parameter-bound
- ADR-025 §F.1 / §F.2 / §F.4 — the policy, the injection sink, the new table
- ADR-026 §D4, implications 1, 2, 4 — token-shape-independent schema; `set_config`; three migrations, one script
- ADR-003 (w14 footer, clauses 1–5) — `industry` / `country` / `currency`, all nullable
- ADR-021 — checked-in idempotent SQL applied by CI

## Target repo

`raffa-backend` (this monorepo: `backend/`)
