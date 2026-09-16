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

## Amendment (2026-09-15, wave w17 — two columns on an existing table, and the geometry this wave refuses)

Serves **NW-71** (auto-accept a field at ≥ 90 %) and **NW-63** (document viewer).
Nothing above is rewritten.

**1. The per-field decision lands on `extraction_evidence` — no new table.** The
table is already **one row per (contract, field) carrying `Confidence`**
(`ExtractionEvidence.cs:38,48`) and is explicitly the extraction-time sibling of
`CorrectionHistory` (`:14-19`). It gains two nullable columns:

| Column | Type | Meaning |
|---|---|---|
| `decision` | `text`, nullable | `auto_accepted` \| `review_required`; **NULL = not yet decided** |
| `decided_at` | `timestamptz`, nullable | when the server decided |

- **No SQL enum.** `FieldName` is deliberately not an enum (`:36-37`) and
  `decision` follows it: a text column with the vocabulary checked in code.
- **Three seats reached this table independently and that is why it is the right
  one.** Isolation: it is already `ENABLE` + `FORCE ROW LEVEL SECURITY`
  (`documents-contracts.sql:798-800`), so the wave adds **zero new isolation
  surface** (security-architect S17-3). Read-back: `useReviewSession.ts:124`
  already calls `getContractEvidence` on every load, so the flagship read-back
  needs **no new endpoint and no new fetch** (client-architect). Shape: it is
  already keyed exactly per (contract, field).
- **If it had been a new table instead**, it would have had to derive from
  `TenantScopedEntity`, carry `tenant_id`, and get `ENABLE` + `FORCE` + policy in
  the same migration — because `TenantRlsMigrationCheckTests.cs:37-44` discovers
  its table list **from the EF model**, so a non-deriving entity is never checked
  and the suite stays green. Recorded because it is the trap avoided, not a
  hypothetical.
- **Two columns, three states.** The stored `decision` is the **server's**
  decision; human acceptance remains the existing correction/accept path. The
  wire therefore composes **three** field states, each derived rather than stored
  a second time:

  | Wire state | Derived from |
  |---|---|
  | `auto_accepted` | `decision = 'auto_accepted'` and no human correction on this field |
  | `human_accepted` | a correction/acceptance exists for this field (the existing path) |
  | `review_required` | `decision = 'review_required'`, or `decision IS NULL` (not yet decided) |

  This is what ADR-001 w17 clause 8 requires to stay distinguishable, and
  **`review_required` is that clause's "pending"** — named for what it needs
  rather than for what it lacks, reusing the stored vocabulary instead of
  inventing a fourth word. **A boolean would collapse two of them** and make that
  clause unimplementable. The rule that keeps states 1 and 2 apart across a
  reprocess is in the **ADR-027 w17 footer clause 2**.
- `ContractEvidenceSchemaTests.cs:49-76` is a deliberate column-by-column proof
  ("complete proof, not just a diff", `:47-48`) and **must gain both columns**.

**2. The geometry columns NW-63 would need are refused this wave.** There is
zero `bbox` / `bounding` / `polygon` in any `*.sql` on `d3d2d24`, and none is
added here. Page-level anchoring is already derivable from the existing
`SourcePage` + `SourceSpan` (`ExtractionEvidence.cs:46-47`) over Document
Intelligence's utf16 spans — see the **ADR-017 w17 footer** and **ADR-029**.
Bounding boxes, and the schema to hold them, are W18. This refusal is what makes
clause 3 possible.

**3. Migration mechanics.** **One** migration this wave, in
`Raffa.Documents.Contracts`, regenerating the single byte-compared script
`Migrations/Scripts/documents-contracts.sql`. Because clause 2 refuses NW-63's
geometry, **NW-71 is the only writer of that script this wave** — the wave
record's single-writer constraint 5 (NW-71 vs NW-63) therefore **dissolves**
rather than needing two phases. Why no CI workflow edit is owed, and one
citation the wave record compresses wrongly, are in the **ADR-021 w17 footer**.

`waves/w17.md` records this under NW-71 and NW-63.

## Amendment (2026-09-16, wave w18 — the geometry this wave refused lands, in the shape ADR-029 pre-decided)

Serves **NW-63r** (bounding-box overlay + phrase-edit — the W18 remainder). **The
w17 footer's clause 2 refusal is ended, not reversed**: it refused geometry *for
w17* because w17 shipped text-level `SourceSpan` highlighting over real pages and
needed no migration; w18 ships the box overlay, which needs the geometry the
refusal named. Nothing above is rewritten; `decision` / `decided_at` and the
three-state derivation are untouched.

**1. Geometry lands as columns on the existing evidence tables, not as a new
table.** The box a phrase occupies is a property of that phrase's evidence row,
so it joins `extraction_evidence` (and, where an OCR-derived structure carries
its own box, the matching wire shape named in ADR-017 w18) as **nullable geometry
columns** — every existing row predates the columns and must stay renderable with
a `SourceSpan` highlight, not break on a null box.

**2. What is stored, and how to read it.** The columns carry the `words` /
`polygon` geometry the widened wire supplies (`DocumentIntelligenceContracts.cs`,
ADR-017 w18), normalized so a box overlays the same page the `SourcePage` names.
A **null** box means "text-level highlight only" — which is exactly w17's shipped
state and must stay reachable for any document rasterized before this lands, not
silently upgraded or errored.

**3. The phrase-edit write path touches the same table, and it obeys the
proposal-vs-override rule of ADR-029.** An edited phrase writes an **override**
beside the proposal (`ExtractionEvidence.cs:14-19`), never rewrites the proposal
in place; the geometry columns are read from the overridden phrase's evidence the
same way they are from the original. This footer records that the geometry and the
override live on the same evidence row family; the write path's exact shape and
the provenance fence are ADR-029's and ADR-027's.

**4. Migration mechanics.** **One** migration, in `Raffa.Documents.Contracts`,
regenerating the single byte-compared `Migrations/Scripts/documents-contracts.sql`
(`dotnet ef migrations script --idempotent`). It is never hand-edited and has
**one writer** in the decomposition — the geometry columns and the phrase-edit
write land in the same module, so the contract file stays single-owner. No CI
workflow edit (ADR-021).

`waves/w18.md` records this under NW-63r.
