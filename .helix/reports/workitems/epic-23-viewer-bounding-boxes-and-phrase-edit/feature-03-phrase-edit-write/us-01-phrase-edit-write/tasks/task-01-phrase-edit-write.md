---
id: E23/F03/US01/T01
type: task
story: us-01-phrase-edit-write
wave: w18
status: live
target_repo: raffa-backend
---

# task-01-phrase-edit-write — Phrase-edit write: override beside the proposal

## Coding objective
Add a phrase-edit write endpoint that persists an override beside the proposal
on the `extraction_evidence` row (the override slot feature-02 added), never
rewriting `Value`. Implement a
`ContractPhraseEditService` (or extend `ContractCorrectionService` following its
write-then-audit shape) that, given a contract id + field name + corrected
phrase text, writes the override (proposal untouched), writes one audit row with
the caller's resolved actor, and returns the updated evidence. Map it in
`ContractsEndpointExtensions.MapContractsEndpoints` as
`PATCH /api/contracts/{id}/evidence/{fieldName}` (or the closest existing-route
convention), under the same `ICallerContext.ResolveTenantAsync` guard-clause
shape as `CorrectContractAsync` (401 → 400 → 404 → 200). The override is the
value `GET /api/contracts/{id}/evidence` returns for that field, so a second
browser reads it back. Declare the new route in
`web/openapi/raffa-api.v1.json` and regenerate `web/src/api/generated/schema.ts`
and the hand-written `web/src/api/client.ts` wrapper (single-writer: feature-02
already wrote these in the previous phase; this task is their writer in this
phase).

## Parent story AC covered
- AC-1 override beside proposal, proposal untouched.
- AC-2 authenticated tenant-scoped endpoint, guard-clause shape.
- AC-3 second browser reads the override back from Postgres.
- AC-4 a reprocess never silently reverts the override.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Documents.Contracts/Application/ContractPhraseEditService.cs | new: write override beside proposal + audit |
| backend/src/Raffa.Documents.Contracts/Domain/ExtractionEvidence.cs | read/write the override slot (added in feature-02) |
| backend/src/Raffa.Api/ContractsEndpointExtensions.cs | `MapPatch("/api/contracts/{id}/evidence/{fieldName}")` + handler |
| backend/src/Raffa.Documents.Contracts/Infrastructure/ServiceCollectionExtensions.cs | register the phrase-edit service |
| web/openapi/raffa-api.v1.json | declare the phrase-edit PATCH route |
| web/src/api/generated/schema.ts | regenerate |
| web/src/api/client.ts | hand-written `editEvidencePhrase` wrapper |

## Context the implementer needs

**Closes: NW-63r**

- **Architecture decisions in force**: ADR-029 clause 1 (override, never rewrite); ADR-027 (re-derivation never overrides a human correction); ADR-003 w18 (the override slot is on the evidence row family).
- **Do not touch**: `DocumentsEndpointExtensions.cs` (the phrase is per contract+field evidence, not a document blob); the migration (feature-02 owns the columns; this task only writes them).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0, with a test proving an edit writes an override while `Value` (proposal) is unchanged, and a second read returns the override
- [ ] a test proves a reprocess of the same evidence leaves the override intact (ADR-027 fence)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | override written, proposal untouched | `backend/tests/Raffa.Documents.Contracts.Tests` |
| integration | PATCH returns override; second read agrees; reprocess preserves | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E23/F03/US01/T01
  prompt: reports/workitems/epic-23-viewer-bounding-boxes-and-phrase-edit/feature-03-phrase-edit-write/us-01-phrase-edit-write/tasks/task-01-phrase-edit-write.md
  produces: [phrase-edit-endpoint]
  depends_on: [evidence-geometry-schema]
  effort: M
  layer: backend
  status: live
```
