# ADR-003 — Relational store: PostgreSQL + pgvector with EF Core + npgsql

- **Status**: accepted
- **Date**: 2026-09-02
- **Deciders**: software-architect (owner); cloud-architect (SKU/host), security-architect (RLS tenancy) reconcile at council-close
- **Locked citations**: Cloud — Azure; Cost — cheapest SKU that satisfies the spec; Backend — C#/ASP.NET Core LTS (locked-decisions.md). Database guideline — SQLite acceptable on dev laptop only; on Azure use cheapest managed relational store satisfying tenant isolation, shared API+worker, embeddings/search, durable shared storage; non-relational store must not be the system of record (brief §5).

## Context and problem statement

The brief forbids SQLite on Azure and requires the **cheapest managed relational store** that satisfies: tenant isolation at the DB level, a shared API + worker writing to the same store, embeddings/semantic search, and durable shared storage (brief §5). The spec's deployable topology explicitly names **PostgreSQL + pgvector** (spec §5.1) and requires P0 "PostgreSQL domain schema + migrations" (spec priority list).

The question is the concrete engine, access library, and how vectors/search and tenancy are satisfied without exceeding cost or coupling modules.

## Decision drivers

- PostgreSQL + pgvector is named in the spec topology and requires no second search engine in V1.
- Must satisfy tenancy isolation at DB level and RAG auth-before-retrieval (brief §10, Appendix C rule 4).
- C# / .NET ecosystem: EF Core is the natural ORM; npgsql is the ADO.NET/EF Core provider for PostgreSQL.
- One system of record; no non-relational store as the source of truth.

## Considered options

1. **Azure Database for PostgreSQL Flexible Server (pgvector) + EF Core + npgsql** — single managed Postgres with the `vector` extension; EF Core migrations; RLS for tenancy.
2. **Citus / Hyperscale (Citus)** — distributed Postgres for scale.
3. **Cosmos DB (NoSQL) as system of record + separate Postgres** — non-relational primary.

## Decision outcome

**Chosen: Option 1** — Azure Database for PostgreSQL Flexible Server with the `pgvector` extension, accessed through EF Core (npgsql provider) with a code-first migration pipeline and Postgres Row-Level Security for tenant isolation. This is because it is the spec-named engine, satisfies embeddings/search and tenancy in one managed store without a second system of record, and keeps ownership costs at the cheapest tier that the storage/vector features allow (exact SKU owned by cloud-architect).

### Consequences

- **Good**: One system of record; vectors live next to relational facts (no sync between two stores); RLS enforces tenant isolation at the DB level; EF Core migrations give a repeatable P0 schema pipeline; npgsql supports `pgvector`.
- **Bad**: Flexible Server is not serverless/free — it has a base cost that must be minimized (burstable/compute size) and may not scale-to-zero, so it must be sized/stoppable by cloud-architect under the cost guideline. RLS adds a design obligation on every table (a policy per table) that the decomposer must encode.
- **Neutral**: `pgvector` is an extension, not a separate service; index choice (HNSW vs IVFFlat) is a later tuning decision, not an architecture fork.

## Pros and cons of the options

### Option 1 — Azure Database for PostgreSQL Flexible Server + pgvector + EF Core/npgsql
- Good: spec-named; single store; vectors + relational + RLS in one place; mature .NET access path.
- Bad: non-zero base cost; RLS discipline required; `pgvector` feature availability must be confirmed for the chosen server type/region.

### Option 2 — Citus/Hyperscale
- Good: horizontal scale path.
- Bad: overkill for two non-production environments; higher cost; against "no production HA/multi-region" framing; not needed for V1 volume.

### Option 3 — Cosmos DB primary + Postgres secondary
- Good: flexible schema.
- Bad: violates "non-relational store must not be the system of record"; introduces a second store and sync; unnecessary complexity.

## Implications for the decomposition

Every table carrying business data MUST have a `tenant_id` column and a Postgres RLS policy that restricts rows to the current tenant (security-architect owns the exact RLS mechanics). Database changes MUST be expressed as EF Core migrations (no hand-edited DDL drift). The `pgvector` extension MUST be enabled in the Terraform/managed-server provisioning so the `vector` column type and similarity search are available. Deterministic calculations stay in domain code and only persist *results*, never store derived truth that an LLM computed. Embeddings are stored in the Documents/Contracts context and query-time RAG MUST apply RLS so unauthorized documents are never retrieved (Appendix C rule 4).

## Assumptions

- Exact SKU/tier (burstable compute size, storage, backup retention) is owned by cloud-architect; the store is a Flexible Server, not Single Server (Single Server is being retired).
- `pgvector` is available on the chosen Postgres version/region (confirmed at implementation time).
- EF Core Core version aligns with the .NET LTS chosen in ADR-dotnet-solution.

## Amendment (2026-09-10, wave w14)

Written by software-architect (owner of this ADR) at the w14 council table.
Serves **NW-24** (workspace profile) and records the schema deltas of
**NW-01/NW-02/NW-04/NW-58**. The Decision outcome above is unchanged:
PostgreSQL Flexible Server + pgvector via EF Core/npgsql, RLS tenancy, one
system of record. Nothing here relaxes the rule at line 53 that database
changes MUST be expressed as EF Core migrations with no hand-edited DDL drift
— this amendment tightens how that rule is discharged.

**1. Three columns are added to `workspace` (NW-24).**

| Column | Type | Null | Note |
|---|---|---|---|
| `industry` | varchar(120) | yes | closed list at the UI, stored as text |
| `country` | varchar(2) | yes | ISO 3166-1 alpha-2, closed list |
| `currency` | varchar(3) | yes | ISO 4217 alpha-3, **derived from `country`, never typed** |

**All three are nullable, deliberately.** Existing `workspace` rows predate
the columns, and a NOT NULL column on a populated table requires a default
that would be a fabricated business fact. The nullable precedent is
`quotes.sql:163,170,195,202`; the trap being avoided is documented at
`QuoteLineConfiguration.cs:45-49`, where EF backfills a new NOT NULL
enum-as-string with `""` that the converter then cannot parse. The API renders
a missing value as **absent**, never as an invented `"CHF"`.

Per the product-owner's w14 ruling (ADR-001 w14 footer, clauses 5–6) the
create form asks `name` + `industry` + `country` only; `currency` and the
business region are **derived from country and stored**, never entered. A
stored workspace `currency` is a **display default that never overrides a
contract's own extracted currency** — converting a validated fact would breach
spec §2 ("AI is not the database") and is forbidden here as a data rule, not
only as a UI rule.

"Region" in the prototype's `"CHF · eu-west"` is a **business** region string
and has no relationship to ADR-006's Azure region; `northeurope` is untouched.

**2. One new table, `workspace_invitation`, in `Raffa.Identity.Workspace`.**
Ordinary tenant-scoped table: `tenant_id` column, `ENABLE` + `FORCE ROW LEVEL
SECURITY` and its `tenant_isolation` policy shipped **in the same migration as
the table**. Shape, indexes and rationale are in **ADR-026 §D4**; the
authorization, token strength and audit rows are in **ADR-025**. This ADR
records only that the store gains one table and that it obeys the
policy-per-tenant-table rule at line 53 with no exception.

**3. `workspace_user` gains a second, `SELECT`-only RLS policy.** The
`identity_self` policy (owner: security-architect; text in the ADR-009 w14
footer, consumed by ADR-026 §D1) is the single widening in this wave. It adds
**no column and no table**, leaves `WITH CHECK` untouched everywhere, and is
confined to one table. Recorded here because it changes what a reader of the
schema will see, not because this ADR decides it.

**4. Migration mechanics the decomposition must honour.** Three migrations
(`AddWorkspaceUserIdentitySelfReadPolicy`, `AddWorkspaceProfileColumns`,
`AddWorkspaceInvitation`) regenerate one file,
`Migrations/Scripts/identity-workspace.sql`. That file is byte-compared
against an in-process regeneration by
`IdentityWorkspaceMigrationScriptStaleCheckTests`, so it must **never** be
hand-edited, and two tasks regenerating it concurrently will conflict — the
decomposer orders them. Regenerate with `dotnet ef migrations script
--idempotent` from `backend/src/Raffa.Identity.Workspace`. The script is
already listed in both CI arrays (`.github/workflows/backend.yml:276-286` and
`:308-318`), so appended migrations need no workflow edit (ADR-021 unchanged).

**5. A known cost, recorded rather than discovered.** The `identity_self`
predicate compares `lower(email)` and therefore cannot use the existing
`ix_workspace_user_tenant_id_email` index (plain `email`,
`identity-workspace.sql:118`), so workspace discovery is a sequential scan of
`workspace_user`. At pilot scale that is tens of rows. If the table grows the
remedy is an expression index on `lower(email)` — not a change to the policy
or to this ADR. Identities are normalised to lower-case on write
(`WorkspaceMembershipFactory.CreateInvitedUser`, which already trims, `:30`);
`citext` is rejected as a column-type change under a live unique index for no
benefit once writes are normalised.

## Amendment (2026-09-14, wave w16 — one new table, and the three schema changes this wave refuses)

Written by software-architect (owner) at the w16 council table. Serves
**NW-13**, and records the negative for **NW-11, NW-12 and NW-21**. The
**Decision outcome above is unchanged** (PostgreSQL Flexible Server + pgvector
via EF Core/npgsql, RLS tenancy, one system of record), and the w14 footer's
five clauses stand. Nothing here relaxes line 53's rule that schema changes are
EF Core migrations with no hand-edited DDL drift.

**1. One new tenant table — `contract_negotiation_step`**, in
`Raffa.Documents.Contracts` (ownership: ADR-002 w16 clause 1; routes and wire
shape: ADR-028 §D3).

| Column | Type | Null | Note |
|---|---|---|---|
| `id` | uuid | no | entity id, as every table in this module |
| `tenant_id` | uuid | no | the RLS column |
| `contract_id` | uuid | no | the contract the steps belong to; no FK across a module boundary, the `RenewalAction.ContractId` treatment |
| `step` | varchar(60) | no | the step's **name** — a closed enum serialized as a string, the `NegotiationOutcome.LeversUsed:112-116` convention |
| `ticked_at` | timestamptz | no | when it was ticked |

- **Unique `(tenant_id, contract_id, step)`**, and **the row's presence is the
  tick** — there is no `ticked` boolean. An untick deletes the row, so the whole-set
  `PUT` is idempotent with no nullable third state and no "false" rows to
  interpret. Per **contract**, not per renewal cycle (ADR-001 w16 clause 3).
- **Never keyed by array index and never storing the rendered label.** The
  client's store is a positional `boolean[4]` holding no names
  (`negotiationStepsStore.ts:11,18,24,31`) and two of the four labels are
  parameterized (`contract360ViewModel.ts:211-218`): an index key re-points
  every tick the day a step is inserted, and a stored label would freeze a fact
  that later changes.
- **`ENABLE` + `FORCE ROW LEVEL SECURITY` and its `tenant_isolation` policy ship
  in the same migration as the table** — the w14 footer's clause-2 rule for
  `workspace_invitation`, applied unchanged (ADR-009).

**2. Three schema changes this wave refuses**, each recorded with its reason so
no task adds one "while it is in there":

- **no `savings_opportunity.quote_id`** — it would invent the quote-originated
  opportunity `SavingsOpportunity.cs:23-27` explicitly parks in R4, and nothing
  in this wave would populate it (ADR-028 §D5);
- **no widening of `renewal_action`** with `supplier_id` / `annual_spend` — the
  client already holds both from fetches it has made (ADR-028 §D1,
  client-architect C9);
- **no new column on `negotiation_outcome`** — the link field already exists and
  is already persisted (`NegotiationOutcomeService.cs:173`).

**3. Migration mechanics.** **One** migration this wave, in
`Raffa.Documents.Contracts`, regenerating the single byte-compared script
`Migrations/Scripts/documents-contracts.sql` (`dotnet ef migrations script
--idempotent`). It is never hand-edited and has **one writer** in the
decomposition. Why no CI workflow edit is owed is in the **ADR-021 w16 footer**.

`waves/w16.md` records this under NW-13.
