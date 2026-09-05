# Schema-apply brief — land EF migrations on Azure `dev`/`demo`

Operator brief for the **schema-apply Helix delta** (`contigo-schema-process.yaml`).
This is not a product feature. It closes the R0 gap: Azure Flexible Server exists,
the API is healthy, **tables were never applied**.

Do **not** re-open ADR-003 (Postgres + EF Core) or ADR-009 (RLS). Do **not** add
Swagger UI.

## Locked facts (do not re-litigate)

- Terraform provisions `psql-contigo-<env>` + empty database `contigo_<env>` +
  `vector`. Schema is **not** Terraform (`infra/modules/postgres`).
- E01/F04/US02/T02 already named the deployable artifact: checked-in
  **idempotent SQL** (`dotnet ef migrations script --idempotent`), applied with
  `psql -f` (or equivalent) against a bare `dev`/`demo` server. Today only
  `Contigo.Documents.Contracts/Migrations/Scripts/documents-contracts.sql`
  exists.
- `Contigo.Api` must **not** call `Database.MigrateAsync()` at startup (thin
  host, us-04 / T02).
- Live `ca-contigo-dev-api` injects
  `IdentityWorkspace` / `DocumentsContracts` / `Audit` / `Storage` only.
  Git Terraform adds `Renewals`. **`Savings` and `Quotes` are missing.**
  HCP workspace `contigo-dev` may be behind git.
- e05 (quotes) may be running on the **live** artifact. This delta must never
  write `wave-spec.execution.yaml`, `slices/e01.yaml`–`e05.yaml`, or
  `slice.current.yaml`.

## Decision to lock (ADR-021)

**Mechanism:** after `az containerapp update` in `.github/workflows/backend.yml`,
CI applies each module's idempotent script **in order** against the env Postgres,
using Key Vault secret `postgres-connection` (same string already on the apps):

1. Identity.Workspace
2. Documents.Contracts
3. Audit
4. Renewals
5. Savings
6. Quotes

Same job on `demo` via existing `workflow_call`. Scripts must be stale-checked
in CI (regenerate or fail if they drift from `Migrations/`).

**Terraform:** add `ConnectionStrings__Savings` and `ConnectionStrings__Quotes`
on API (and worker if it already takes Renewals) pointing at secret `pg-cs`.
HCP VCS apply on `contigo-dev` / `contigo-demo` — **no** laptop `terraform apply`.

**Apply role:** provisioning admin (Flexible Server administrator) runs the
scripts once per deploy. RLS policies stay in the scripts. The app continues
to connect as the app role with `app.tenant_id` set. Do not leave a session
that bypasses RLS as the API identity.

## Definition of done (slice e09)

On `psql-contigo-dev` database `contigo_dev`:

- App tables exist (`workspace`, `document`, `contract`, `audit_event`,
  `renewal_action`, `savings_opportunity`, `quote`, …).
- `__EFMigrationsHistory` has a row set per DbContext.
- A CI check fails if any module lacks a script or the script is stale.

## Out of scope

- Swagger / OpenAPI self-publish
- Rewriting RLS policies
- `--fresh` on the live Helix process
- Applying schema by editing e05 while Studio is running

## Wave placement

New work is **epic-09 / slice e09**, append-only. Passata 2 (after e05 HITL):

```
./run.ps1 -Max -Slice e09 -o execution-fanout
```
