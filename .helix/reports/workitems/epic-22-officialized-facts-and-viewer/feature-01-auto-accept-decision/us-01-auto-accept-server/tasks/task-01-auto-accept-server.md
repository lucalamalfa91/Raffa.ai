---
id: E22/F01/US01/T01
type: task
story: us-01-auto-accept-server
wave: w17
status: live
target_repo: raffa-backend
---

# task-01-auto-accept-server — one confidence policy, two persisted columns, and the wave's phase-2 contract

## Context

**Closes: NW-71** (server half). Also carries the **phase-2 contract edit** for
**NW-20** and **NW-26**, whose backend halves land in phase 1 and whose
`web/openapi/raffa-api.v1.json` delta is deliberately deferred to this task —
ADR-012 §3 gives that file **one writer per phase**, and the council ruled
**"two contract tasks, not five"** (`w17-requirements.md` §5 constraint 6).

Decision row: `reports/architecture/waves/w17.md` — the **NW-71** row (all five
seat halves) and the rulings on **OQ-w17-002**, **OQ-w17-003**, **OQ-w17-po-01**
and **OQ-w17-sec-03**.

ADRs in force: **ADR-003** w17 clause 1; **ADR-024** w17 §A (clauses A1–A4);
**ADR-027** w17 clause 2; **ADR-021** w17 clause 1; **ADR-009** w17 clause 2;
**ADR-011** w17 clause 21; **ADR-022** w17 clause 5; **ADR-001** w17 clauses 2
and 8.

⚠ **This task is the phase-2 owner of `web/openapi/raffa-api.v1.json` and of the
regenerated `web/src/api/generated/schema.ts`.** `E20/F01/US01/T01` owns them in
**phase 4** (contract-B) and regenerates on top of this merged contract.

Already on `main`, so **not** a dependency to build: `extraction_evidence` is
already one row per (contract, field) carrying `Confidence`
(`ExtractionEvidence.cs:38`, `:48`), already `ENABLE` + `FORCE` +
`tenant_isolation` (`documents-contracts.sql:798-800`) and already indexed on
`(contract_id, field_name)` (`:770`). `document.page_count` already exists
(`:834`). `documents-contracts.sql` is already in **both** `backend.yml` script
arrays (`:289` and `:321`) — **a `backend.yml` diff from this migration is a
defect** (ADR-014 w17 clause 6).

## Coding objective

In `raffa-backend`, make the **server** decide which extracted fields are
accepted, persist that decision per field, and publish it — then land the
wave's phase-2 contract edit.

1. **One policy, replacing two duplicated pairs.** Add
   `ExtractionConfidencePolicy` to
   `backend/src/Raffa.Documents.Contracts/Application/` exposing the single bar
   (`0.90`) and a decision function over a nullable `double`. Consume it from
   **both** `Extraction/StagedExtractionService.cs` — retiring
   `LowConfidenceThreshold` (`:78`) and `CriticalConfidenceThreshold` (`:88`)
   and rewriting `DetermineDocumentStatus` (`:889`, applied at `:236`) — **and**
   `DocumentQueryService.cs`, retiring `WeakFactThreshold` (`:33`) and
   `CriticalWeakFactThreshold` (`:42`) and rewriting `IsWeak` (`:46-52`,
   consumed at `:317`). Both files document their duplication as deliberate
   (`DocumentProcessingPipeline.cs:107-113`, `DocumentQueryService.cs:28-31`);
   those comments are rewritten with the code. **Changing only one pair
   desyncs the Documents badge from `needs_review`** — that is the defect this
   step exists to remove.
2. **Compare raw.** `confidence >= 0.90` on the stored `double`, **no rounding
   before the compare** (ADR-024 w17 clause A1). A null confidence is
   `review_required`, never accepted.
3. **Persist the decision.** Add two nullable properties to
   `backend/src/Raffa.Documents.Contracts/Domain/ExtractionEvidence.cs` beside
   `Confidence` (`:48`): `Decision` (`string?`) and `DecidedAt`
   (`DateTimeOffset?`). Map them in
   `Infrastructure/Configurations/ExtractionEvidenceConfiguration.cs` as
   `character varying` and `timestamp with time zone`. **No SQL enum** —
   `FieldName` is deliberately not one (`ExtractionEvidence.cs:36-37`) — and
   **no new table**, so no new RLS policy, guard or migration ordering is owed
   (ADR-009 w17 clause 2).
4. **Migrate.** `dotnet ef migrations add AddExtractionEvidenceDecision` from
   `backend/src/Raffa.Documents.Contracts`, then regenerate
   `Migrations/Scripts/documents-contracts.sql` with
   `dotnet ef migrations script --idempotent`. **Do not touch
   `.github/workflows/backend.yml`.**
5. **Write the decision.** In `Extraction/StagedExtractionService.cs`, set
   `Decision` and `DecidedAt` on each evidence row as it is written, from the
   policy. A **reprocess re-derives** them (ADR-027 w17 clause 2) — a stale
   decision from a previous run must not survive a re-extraction.
6. **Record it once, in the trail.** Write **one** audit row per document per
   extraction run, actor the fixed literal **`system:extraction`**, carrying
   **field names and confidence numbers and no field value** (ADR-011 w17
   clause 21). Precedent for the shape:
   `Application/DocumentValidationService.cs:124-135`. The table is
   append-only: a value written once cannot be removed.
7. **Publish it.** In
   `backend/src/Raffa.Documents.Contracts/Application/ContractEvidenceQueryService.cs`
   project the two columns, and in
   `backend/src/Raffa.Api/ContractsEndpointExtensions.cs`'s
   `GetContractEvidenceAsync` (route `:80`, projection `:127`) emit `decision`
   (one of `auto_accepted` | `human_accepted` | `review_required`) **and** the
   `autoAcceptThreshold` the server used, **once per response**, so the web
   never hardcodes `90` and the legend is server-fed.
8. **Reject a client-asserted decision.** Any write path that receives a
   `decision` returns **400** and persists nothing (ADR-022 w17 clause 5). A
   human acceptance is `human_accepted`, attributed to the caller's resolved
   subject by the existing validate/PATCH path; the automatic one is
   `system:extraction`. **No new role, no Admin-gating of review.**
9. **Land the phase-2 contract edit**, in `web/openapi/raffa-api.v1.json`:
   - this task's own `decision` + `autoAcceptThreshold` on the evidence
     response;
   - **NW-20's** members on the 360 payload —
     `Contract360BenchmarkEntry(Metric, Status, Position?, AdapterName?,
     SampleSize?, AsOf?)` and `Contract360ActivityEntry(OccurredAt, Action,
     ActorLabel)` — exactly as `E21/F01/US01/T01` shipped them in phase 1
     (`weakFactCount` at `:1132-1135` is untouched);
   - **NW-26's** optional 1-based `page` query parameter on
     `GET /api/documents/{id}/preview` and `pageCount` on the document read
     model, exactly as `E22/F02/US01/T01` shipped them in phase 1.

   Then run `npm run generate:api` in `web/` and commit the regenerated
   `web/src/api/generated/schema.ts`. **Add no `client.ts` wrapper** —
   `E21/F03/US01/T01` is that file's phase-2 writer.
10. **Move the fixture, not the bar** (OQ-w17-po-01). In
    `backend/src/Raffa.AiGateway/Fixtures/FixtureContractFactExtractor.cs`,
    `GoodConfidence = 0.9` (`:49`) renders *Flagged* today and flips to
    *Accepted* at the new bar, which would leave the pilot's HITL act
    (`percorso-pilota-v1.md:122`) nothing to review **while every suite stays
    green**. Keep **at least two** of the five critical fields **strictly below
    0.90**, at a value **not adjacent to the bar** (0.85, not 0.899). The
    fixture prose at `:57-58` moves with the values. **No task may soften the
    90 % rule to keep a seed interesting.**

## Parent story AC covered

- AC-1 A field whose stored extraction confidence is `0.90` or above is recorded as **accepted automatically** without any human action. (A17-1)
- AC-2 The bar is **90 % for every field**, the five critical ones included. There is **no always-review list**. (A17-2)
- AC-3 A field at `0.895` is **not** accepted. The comparison is on the raw stored value.
- AC-4 `GET /api/contracts/{id}/evidence` returns one of exactly three decisions per field and also carries the **threshold** the server used. (A17-3)
- AC-5 A decision supplied by a caller on a write path is **rejected with 400** and nothing is persisted.
- AC-6 The Documents-row badge and the document's `needs_review` status never disagree.
- AC-7 One audit row per document per extraction run, actor `system:extraction`, naming **field names and confidence numbers and no field value**. (A17-4)
- AC-8 Another tenant never sees this tenant's decision state.

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Documents.Contracts/Application/ExtractionConfidencePolicy.cs` | **new** — the single bar (`0.90`) and the decision function over a nullable confidence |
| `backend/src/Raffa.Documents.Contracts/Application/Extraction/StagedExtractionService.cs` | modify — retire `:78` and `:88`; `DetermineDocumentStatus` (`:889`) consumes the policy; write `Decision`/`DecidedAt`; write the audit row; rewrite the duplication comment |
| `backend/src/Raffa.Documents.Contracts/Application/Extraction/DocumentProcessingPipeline.cs` | modify — rewrite the `:107-113` comment that documents the duplication as deliberate; the `:114` constant follows the policy |
| `backend/src/Raffa.Documents.Contracts/Application/DocumentQueryService.cs` | modify — retire `:33` and `:42`; `IsWeak` (`:46-52`) consumes the policy; rewrite the `:28-31` comment |
| `backend/src/Raffa.Documents.Contracts/Domain/ExtractionEvidence.cs` | modify — `Decision` and `DecidedAt`, nullable, beside `Confidence` (`:48`) |
| `backend/src/Raffa.Documents.Contracts/Infrastructure/Configurations/ExtractionEvidenceConfiguration.cs` | modify — map both columns; `character varying` + `timestamp with time zone`, no enum |
| `backend/src/Raffa.Documents.Contracts/Migrations/` | **new migration** — `AddExtractionEvidenceDecision` (+ its `.Designer.cs` and the updated `DocumentsContractsDbContextModelSnapshot.cs`) |
| `backend/src/Raffa.Documents.Contracts/Migrations/Scripts/documents-contracts.sql` | modify — regenerated by `dotnet ef migrations script --idempotent` |
| `backend/src/Raffa.Documents.Contracts/Application/ContractEvidenceQueryService.cs` | modify — project `Decision` and `DecidedAt` |
| `backend/src/Raffa.Api/ContractsEndpointExtensions.cs` | modify — `GetContractEvidenceAsync` (`:127`) emits `decision` per field and `autoAcceptThreshold` once; a caller-supplied decision is 400 |
| `backend/src/Raffa.AiGateway/Fixtures/FixtureContractFactExtractor.cs` | modify — at least two critical fields strictly below `0.90`, not adjacent to the bar; the `:57-58` prose moves with them |
| `web/openapi/raffa-api.v1.json` | modify — the evidence decision + threshold, **plus** NW-20's two 360 member shapes and NW-26's `page` parameter and `pageCount` |
| `web/src/api/generated/schema.ts` | modify — regenerated by `npm run generate:api` |
| `backend/tests/Raffa.Documents.Contracts.Tests/ContractEvidenceSchemaTests.cs` | modify — the column-by-column table (`:46-76`) gains `("extraction_evidence", "decision", ...)` and `("extraction_evidence", "decided_at", ...)` |
| `backend/tests/Raffa.Documents.Contracts.Tests/DocumentsContractsMigrationScriptTests.cs` | modify — the regenerated script applies cleanly to a bare Postgres |
| `backend/tests/Raffa.Documents.Contracts.Tests/DocumentsContractsMigrationTests.cs` | modify — `migrations add` + `database update` succeed with the new columns |
| `backend/tests/Raffa.Documents.Contracts.Tests/ExtractionConfidencePolicyTests.cs` | **new** — the boundary table: `0.90` accepts, `0.895` does not, `null` is `review_required`, and the five critical fields use the same bar |
| `backend/tests/Raffa.Api.Tests/DocumentsV2EndpointTests.cs` | modify — three decisions on the wire, the threshold once, a client-asserted decision is 400, cross-tenant isolation |
| `backend/tests/Raffa.AiGateway.Tests/FixtureContractFactExtractorTests.cs` | modify — at least two critical fields stay strictly below the bar |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root — not
`workspace/<repo>/` (PROCESS.md D1).

## Context the implementer needs

- **Architecture decisions in force**:
  - **ADR-024 w17 clause A1 / OQ-w17-002** — compare the **raw stored double**.
    Rounding before the compare makes `0.895` accept in the payload and review
    in the audit row: the record disagreeing with itself. Presentation rounding
    is a separate, later, **floored** step and belongs to `us-02`.
  - **ADR-024 w17 clause A2** — **three** states, not a boolean. With two, a
    human acceptance has nowhere to live on the wire and would have to be
    painted as `auto_accepted` (ADR-001 w17 clause 8 forbids it) or left in
    `acceptedThisSession`, the store this wave retires. A two-value enum does
    not simplify the design; it **forces the session store to survive**.
  - **ADR-024 w17 clause A3 / OQ-w17-003** — retire **exactly two** sites.
    **Do not touch**: `Admission/DocumentAdmissionOptions.cs:33`
    (`AdmissionThreshold` — "is the file admitted at all", a different decision,
    ADR-024 w15 / ADR-027), `Raffa.Quotes`' own bar (separate domain, spec §11),
    `SavingsProvenanceClassifier.cs:37,46` (provenance banding) and
    `CriticalityScoreCalculator.cs:35` (a criticality input — and see
    OQ-w17-sa-01: it must not be rendered as a review statement this wave).
  - **ADR-003 w17 clause 1 / ADR-009 w17 clause 2** — the column lands on a
    table that is **already** `ENABLE` + `FORCE` + `tenant_isolation` and
    already per-field keyed. Three seats reached that table independently
    (isolation, the wire, the read-back), which is why it is recorded rather
    than assumed. Had a **new table** been chosen, ADR-009 w16 clause 2's four
    requirements would bind — and `TenantRlsMigrationCheckTests.cs:37-44`
    discovers its table list from the EF model by `TenantScopedEntity` subclass
    while the "guards the guard" assert at `:46-49` catches only an **empty**
    list, never a **missing member**, so a non-deriving entity would never be
    checked and the suite would stay green.
  - **ADR-011 w17 clause 21** — actor `system:extraction` (there is no HTTP
    caller). **Field names and confidence numbers, never a field value.** The
    audit table is append-only, so a value written once **cannot be removed**.
  - **ADR-022 w17 clause 5** — the decision is **server-computed and read-only
    on the wire**. Letting a display band become an authorization decision is
    the error ADR-022 §3 forbids on the other screen.
  - **ADR-021 w17 clause 1 / ADR-014 w17 clause 6** — `documents-contracts.sql`
    has a single writer this wave and **`.github/workflows/backend.yml` is not
    opened**. The module is already in both arrays (`backend.yml:289`, `:321`);
    a `backend.yml` diff here is a defect. ⚠ `w17-requirements.md` §5
    constraint 9 cites `:277`/`:309` for this — those lines are
    `identity-workspace.sql`; the verified lines are **`:289`** and **`:321`**.
  - **ADR-020 w17 §17** — `screens-v2.md:89-91`'s confidence tip quotes the
    **old** bands verbatim and `:83` the "N facts below 80%" title. Both become
    **stale copy** the moment this lands. **Do not "restore" the export**, and
    do not re-derive the old bands from it.
- **Do not touch**: `web/src/api/client.ts` (`E21/F03/US01/T01` is its phase-2
  writer); anything under `web/src/routes/` or `web/src/styles/` (that is
  `us-02`, phase 3); `backend/src/Raffa.Api/DocumentsEndpointExtensions.cs` and
  `backend/src/Raffa.SharedKernel/Storage/DocumentStoragePath.cs`
  (`E22/F02/US01/T01`'s, phase 1); `backend/src/Raffa.Api/RenewalsEndpointExtensions.cs`
  and `backend/src/Raffa.Api/InsightsEndpointExtensions.cs` (`E21/F02/US01/T01`
  and `E21/F03/US01/T01`, **this same phase** — do not open them);
  `backend/src/Raffa.Documents.Contracts/Domain/Contract.cs`; the AI-gateway wire
  contract `Foundry/Wire/DocumentIntelligenceContracts.cs` (ADR-017 w17 footer:
  `prebuilt-layout` stays uncalled).

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Documents.Contracts.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.AiGateway.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Tenancy --configuration Release` exits 0
- [ ] `cd web && npm run generate:api` exits 0 and the regenerated `web/src/api/generated/schema.ts` contains `auto_accepted`, `human_accepted`, `review_required`, `autoAcceptThreshold`, `pageCount` and the two 360 member shapes
- [ ] `cd web && npx tsc --noEmit` exits 0
- [ ] `git diff --stat -- .github/workflows/backend.yml` is **empty** for this task's commits
- [ ] `grep -n "0\.8\|0\.6" backend/src/Raffa.Documents.Contracts/Application/DocumentQueryService.cs backend/src/Raffa.Documents.Contracts/Application/Extraction/StagedExtractionService.cs` shows **no** surviving review-bar constant (admission and criticality bars live in their own files and are untouched)
- [ ] `grep -rn "0\.90\|0\.9" backend/src/Raffa.Documents.Contracts/Application/ExtractionConfidencePolicy.cs` shows the bar declared **exactly once** in the module
- [ ] `grep -c "GoodConfidence" backend/src/Raffa.AiGateway/Fixtures/FixtureContractFactExtractor.cs` returns ≥ 1 and at least two critical fields are seeded strictly below `0.90`
- [ ] a reviewer reading the new audit row's construction finds **no** field value interpolated into it — field names and numbers only

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | `0.90` → `auto_accepted`; `0.895` → `review_required`; `null` → `review_required`; the five critical fields use the same bar as every other field | `backend/tests/Raffa.Documents.Contracts.Tests/ExtractionConfidencePolicyTests.cs` |
| unit | `DetermineDocumentStatus` and `IsWeak` read the **same** policy — a document whose badge says "needs review" has `needs_review` status, and vice versa, across the boundary | `backend/tests/Raffa.Documents.Contracts.Tests/ContractEvidenceSchemaTests.cs` + the extraction tests in the same project |
| integration | the migration applies to a bare Postgres and `extraction_evidence` has `decision` (`character varying`, nullable) and `decided_at` (`timestamp with time zone`, nullable) | `backend/tests/Raffa.Documents.Contracts.Tests/ContractEvidenceSchemaTests.cs`, `DocumentsContractsMigrationScriptTests.cs` |
| integration | `GET /api/contracts/{id}/evidence` carries one of the three decisions per field plus `autoAcceptThreshold`; a caller-supplied `decision` on a write path returns 400 and changes nothing; a second tenant sees none of it | `backend/tests/Raffa.Api.Tests/DocumentsV2EndpointTests.cs` |
| unit | the audit row names fields and confidence numbers and contains **no** field value string | `backend/tests/Raffa.Documents.Contracts.Tests/` (the extraction audit test) |
| unit | at least two of the pilot's critical fixture fields are strictly below `0.90` | `backend/tests/Raffa.AiGateway.Tests/FixtureContractFactExtractorTests.cs` |

## Open questions blocking this task

- **none blocking.** OQ-w17-002 (raw `>= 0.90`), OQ-w17-003 (two sites retire),
  OQ-w17-po-01 (the fixture moves) and OQ-w17-sec-03 (closed by the column
  ruling) are all ruled at the w17 table.

## Wave-spec entry

```yaml
- id: E22/F01/US01/T01
  prompt: reports/workitems/epic-22-officialized-facts-and-viewer/feature-01-auto-accept-decision/us-01-auto-accept-server/tasks/task-01-auto-accept-server.md
  produces: [auto-accept-server, api-contract-a]
  depends_on: [contract360-benchmark-activity, document-page-render]
  effort: L
  layer: backend
  status: live
```
