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

## Amendment (2026-09-14, wave w16 — one regenerated script, and why the CI arrays do not move)

Written by software-architect (owner) at the w16 council table. Serves
**NW-13**. The **Decision outcome above is unchanged**: CI applies the
checked-in idempotent EF SQL after the container update, and the API never calls
`MigrateAsync`. Four clauses, all mechanical.

1. **w16's only schema change is `contract_negotiation_step`** (ADR-003 w16
   footer clause 1). It regenerates **one** existing script,
   `backend/src/Raffa.Documents.Contracts/Migrations/Scripts/documents-contracts.sql`.
2. **No workflow edit is owed, and one would be a defect.** The two script
   arrays in `.github/workflows/backend.yml` (`:277-285` and `:309-317`) list
   **modules**, and `Raffa.Documents.Contracts` is already in both. A new
   migration inside an already-listed module therefore moves no CI YAML — this
   wave's CI-YAML set stays exactly the NW-31 workflow pair delivery-manager
   owns (their D7), and any `backend.yml` diff in this wave fails the
   final-integration check.
3. **The script is byte-compared** against an in-process regeneration by its
   stale-check test. Hand-editing it, or letting two tasks regenerate it inside
   one phase, are both failures: **one writer**.
4. Nothing about the apply order, the `--idempotent` flag, the psql invocation
   or the Terraform-injected connection strings changes.

`waves/w16.md` records this under NW-13.

## Amendment (2026-09-15, wave w17 — one writer again, a compressed citation corrected, and two stale CI claims)

Serves **NW-71**. Nothing above is rewritten; clauses 1–4 of the w16 footer stand
verbatim and this wave changes nothing about the apply order or the mechanism.

**1. `documents-contracts.sql` has exactly one writer this wave, and the wave
record's constraint 5 dissolves.** That constraint lists **NW-71** (per-field
decision columns) and **NW-63** (geometry columns) contending for one
byte-compared script, resolved by "one writer per phase, or one task owns both".
**ADR-003 w17 clause 2 refuses NW-63's geometry this wave**, so the contention
does not exist: NW-71 is the only migration, and the decomposer does **not** need
to spend a phase boundary separating them. The w16 rule stands unchanged —
hand-editing the script, or two tasks regenerating it inside one phase, are both
failures.

**2. No `backend.yml` edit is owed, and one would still be a defect** —
`Raffa.Documents.Contracts` is already in both arrays, so a new migration inside
an already-listed module moves no CI YAML.

**⚠ A citation the wave record compresses wrongly.** This footer's clause 2 above
cites the arrays as **ranges** — `:277-285` and `:309-317` — and those ranges are
correct: each lists **nine** module scripts. `w17-requirements.md` §5 and the w17
decision record compress them to the point citations "`:277`, `:309`", which is
where the error enters: **`:277` and `:309` are `identity-workspace.sql`**.
`documents-contracts.sql` is **`:278` and `:310`** (verified on `d3d2d24`). A task
told to "check line 277" reads the wrong module's row and may edit it. Cite the
range, or cite `:278`/`:310`.

**3. Two stale prose claims inside the same step, assigned rather than left.**
Both are in `.github/workflows/backend.yml` and both miscount the arrays they
describe:

- `:289` — the failure message reads *"ADR-021 requires all **eight** module
  scripts to be checked in"*, but each array lists **nine** (identity-workspace,
  documents-contracts, audit, renewals, savings, quotes, chat, suppliers,
  market).
- `:297-304` — the explanatory comment above "Verify schema applied" says the
  check reads the expected ids *"straight out of the same **six** checked-in
  files"*. Also nine.

These are **not NW-71's to fix**. `.github/workflows/**` is **NW-73's alone**
(wave-record constraint 6, ADR-014 w16 clause 4), so they are recorded here for
the NW-73 CI sweep to pick up — and recorded so that a migration task does **not**
"helpfully" correct them, which would produce exactly the `backend.yml` diff
clause 2 calls a defect. Neither claim affects behaviour: the loop iterates the
array, not the number in the message.

`waves/w17.md` records this under NW-71 and NW-73.
