---
id: E23/F02/US01/T01
type: task
story: us-01-evidence-geometry-schema
wave: w18
status: live
target_repo: raffa-backend
---

# task-01-evidence-geometry-and-override — Persist geometry + override on evidence and widen the box read

## Coding objective
Add nullable geometry columns (`BoxX`/`BoxY`/`BoxWidth`/`BoxHeight` or the
normalized `words`/`polygon` geometry, per ADR-003 w18 clause 2) and a nullable
phrase-edit override slot beside the existing `Value` on
`ExtractionEvidence` (`backend/src/Raffa.Documents.Contracts/Domain/ExtractionEvidence.cs`),
configured in `ExtractionEvidenceConfiguration.cs`, in **one** EF Core migration
that regenerates `Migrations/Scripts/documents-contracts.sql` (byte-compared —
never hand-edited). Widen the contract read model so
`ContractEvidenceQueryService`/`ContractEvidenceResult` carry the box, and
widen `GET /api/contracts/{id}/evidence`
(`ContractsEndpointExtensions.cs` `GetContractEvidenceAsync`, the anonymous
projection) to emit `box` (null-able) beside `sourcePage`/`sourceSpan`/`passage`.
The box is read from the overridden phrase's evidence the same way as the
original (ADR-029 clause 4). A null box is the honest w17 state, never an
error. Update `ContractEvidenceSchemaTests` (the column-by-column proof) with
the new columns.

## Parent story AC covered
- AC-1 `extraction_evidence` gains nullable geometry + override columns.
- AC-2 one migration regenerating `documents-contracts.sql`.
- AC-3 `GET /api/contracts/{id}/evidence` returns the box beside page/span/passage.
- AC-4 a null box = text-level highlight only.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Documents.Contracts/Domain/ExtractionEvidence.cs | add nullable geometry + override slot |
| backend/src/Raffa.Documents.Contracts/Infrastructure/Configurations/ExtractionEvidenceConfiguration.cs | configure the new columns |
| backend/src/Raffa.Documents.Contracts/Application/ContractEvidenceQueryService.cs | carry the box on the read model |
| backend/src/Raffa.Documents.Contracts/Application/ContractEvidenceResult.cs | add box member(s) |
| backend/src/Raffa.Api/ContractsEndpointExtensions.cs | `GetContractEvidenceAsync` emits `box` |
| backend/src/Raffa.Documents.Contracts/Migrations/<new>:AddEvidenceGeometryAndOverride.cs (+ Designer) | one migration |
| backend/src/Raffa.Documents.Contracts/Migrations/Scripts/documents-contracts.sql | regenerate (byte-compared) |
| web/openapi/raffa-api.v1.json | `getContractEvidence` response gains `box` |
| web/src/api/generated/schema.ts | regenerate the evidence schema |
| backend/tests/Raffa.Documents.Contracts.Tests/ContractEvidenceSchemaTests.cs | add the new columns to the proof |

## Context the implementer needs

**Closes: NW-63r**

- **Architecture decisions in force**: ADR-003 w18 (one migration, geometry + override on `extraction_evidence`); ADR-029 (override beside proposal; null box stays reachable); ADR-021 (regenerate byte-compared script, no CI array moves).
- **Do not touch**: the phrase-edit write endpoint (feature-03 later); `web/src/api/client.ts` (the evidence shape is `schema.ts`, not a hand-written wrapper).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Documents.Contracts.Tests` exits 0 (schema test gains columns)
- [ ] `git diff --exit-code` on `documents-contracts.sql` after `dotnet ef migrations script --idempotent` (regeneration is byte-identical)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | `ContractEvidenceSchemaTests` proves the new columns exist | `backend/tests/Raffa.Documents.Contracts.Tests` |
| unit | a null-geometry evidence row reads back with a null box | `backend/tests/Raffa.Api.Tests` (evidence endpoint) |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E23/F02/US01/T01
  prompt: reports/workitems/epic-23-viewer-bounding-boxes-and-phrase-edit/feature-02-evidence-geometry-and-override/us-01-evidence-geometry-schema/tasks/task-01-evidence-geometry-and-override.md
  produces: [evidence-geometry-schema]
  depends_on: [ocr-geometry-wire]
  effort: L
  layer: backend
  status: live
```
