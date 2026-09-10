# ADR-021 — Apply EF Core migrations to Azure `dev`/`demo` Postgres

- **Status**: accepted
- **Date**: 2026-09-05
- **Deciders**: software-architect (owner); cloud-architect (HCP / CA env);
  delivery-manager (CI); security-architect (apply role / RLS)
- **Locked citations**: ADR-003 (EF Core + Flexible Server), ADR-007 (HCP
  owns apply), ADR-009 (RLS in migrations), ADR-011 (Key Vault), ADR-014
  (per-folder CI), E01/F04/US02/T02 (deployable idempotent SQL)

## Context and problem statement

R0 required “DB schema+migrations” on `dev` → `demo`. The waves generated
EF migrations and proved them in Testcontainers. Terraform created
`psql-raffa-dev` + empty `raffa_dev`. **No task applied those
migrations to Azure.** `backend.yml` only builds images and rolls Container
Apps. `/health` 200 does not mean tables exist.

E01/F04/US02/T02 already named the operator/CI artifact: a checked-in
idempotent script (`dotnet ef migrations script --idempotent`) applied with
`psql` against a bare server — not `MigrateAsync` inside the API host.

A second gap: live `ca-raffa-dev-api` lacks `ConnectionStrings__Savings`
and `ConnectionStrings__Quotes` (Terraform git has Renewals only).

## Considered options

1. **`Database.MigrateAsync()` at API startup** — simple; couples schema
   apply to every replica boot; contradicts the thin-host / T02 “no EF
   runtime migrator in the deploy path” proof.
2. **One-shot operator `psql`** — recovers `dev` once; no repeatable path
   for `demo` or later modules.
3. **CI apply of checked-in idempotent SQL after container update** —
   matches T02’s documented artifact; same job for `dev` and `demo`
   (`workflow_call`); no laptop `terraform apply`.

## Decision outcome

**Chosen: Option 3.**

- Each module that owns a DbContext checks in
  `Migrations/Scripts/<module>.sql` (Documents already has
  `documents-contracts.sql`). CI fails if a script is missing or stale
  versus `Migrations/`.
- `.github/workflows/backend.yml` apply order, after both
  `az containerapp update` steps, using Key Vault `postgres-connection`:

  1. Identity.Workspace
  2. Documents.Contracts
  3. Audit
  4. Renewals
  5. Savings
  6. Quotes

- `Raffa.Api` / `Raffa.Worker` do **not** call `MigrateAsync()`.
- Terraform adds `ConnectionStrings__Savings` and
  `ConnectionStrings__Quotes` on the same `pg-cs` secret. HCP VCS apply
  on `raffa-dev` / `raffa-demo`.
- Scripts run as the Flexible Server administrator (provisioning). RLS
  policies stay in the scripts. The API identity keeps `app.tenant_id`
  and is not the bypass role.

### Consequences

- **Good**: Azure `dev`/`demo` get the same schema the tests already
  proved; repeatable; no host-time migrator; ADR-003/009 unchanged.
- **Bad**: first successful apply needs HCP to have the new env vars and
  a CI identity that can reach Postgres (AllowAzureServices already
  covers GitHub-hosted Azure CLI from this subscription’s runners if they
  use Azure; otherwise the job must run from a network Azure allows —
  delivery-manager owns the concrete `az postgres` / `psql` invocation).
- **Neutral**: Swagger UI stays out of scope (ADR-012 OpenAPI file, not
  a hosted UI).

## Implications for decomposition

Epic-09 / slice e09 only. Do not inject into a running e05. Do not rewrite
e01–e08 or `wave-spec.execution.yaml`.
