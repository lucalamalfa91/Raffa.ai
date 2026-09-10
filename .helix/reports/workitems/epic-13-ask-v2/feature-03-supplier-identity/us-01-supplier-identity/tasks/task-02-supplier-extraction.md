---
id: E13/F03/US01/T02
type: task
story: us-01-supplier-identity
wave: 13
status: live
target_repo: raffa-backend
---

# task-02-supplier-extraction — `supplier` critical fact, resolver call in the pipeline, back-fill via reprocess

## Coding objective

Make extraction produce the supplier and link it (`inputs/requirements.md`
R-SUP-01, R-SUP-03, R-SUP-04). In `StagedExtractionJsonSchemas` add the
`supplier` field to the basic-metadata stage (value = legal name as
written, `sourcePage`, `sourceSpan`, `confidence`); in
`StagedExtractionService` map the `supplier` fact into `ExtractionEvidence`
as a **critical field** (spec §7.3: < 0.8 → the document lands in
`needs_review` and the field appears in the review list with its
evidence). In `DocumentProcessingPipeline` (this task is its phase-3
writer), after extraction and when the supplier fact is present and
accepted (≥ 0.8 or corrected), call `ISupplierResolver.ResolveAsync` (an
optional dependency: `GetService`, so unit tests without the Suppliers
module keep passing) and set `Contract.SupplierId`. Make
`ContractCorrectionService` re-run the resolver when a user corrects the
supplier field. Reprocess (`DocumentReprocessService`, phase 2) already
re-runs extraction, so re-processing existing documents back-fills
`SupplierId` — add a test proving it. Expose the name in read models:
`PortfolioListItem`, `Contract360Header`, `DocumentListItem`,
`RenewalPipelineItem`-based API responses get `supplierName` through
`ISupplierNameLookup` in the composition root (`Raffa.Api`
`PortfolioEndpointExtensions`, `ContractsEndpointExtensions`,
`RenewalsEndpointExtensions`, `DocumentsEndpointExtensions` — add the
field to each response; the phase-4 web task documents it in the OpenAPI
contract). Fixture gateway: the scripted extraction used by R1
integration fixtures must emit a `supplier` fact for the sample MSA
("Salesforce, Inc.") so the end-to-end test shows the name.

## Parent story AC covered
- AC-3, AC-4, AC-5

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/src/Raffa.Documents.Contracts/Application/Extraction/StagedExtractionJsonSchemas.cs`, `StagedExtractionService.cs`, `ExtractionPayloads.cs` | `supplier` critical fact |
| `backend/src/Raffa.Documents.Contracts/Application/Extraction/DocumentProcessingPipeline.cs` | resolver call after extraction (phase-3 writer) |
| `backend/src/Raffa.Documents.Contracts/Application/ContractCorrectionService.cs` | re-resolve on correction |
| `backend/src/Raffa.Api/PortfolioEndpointExtensions.cs`, `ContractsEndpointExtensions.cs`, `RenewalsEndpointExtensions.cs`, `DocumentsEndpointExtensions.cs` | `supplierName` in responses via `ISupplierNameLookup` |
| `backend/tests/Raffa.Documents.Contracts.Tests/StagedExtractionServiceTests.cs`, `ContractCorrectionServiceTests.cs` | supplier fact + critical threshold + re-resolve |
| `backend/tests/Raffa.IntegrationTests/R1ExtractionFixtures.cs`, `ScriptedR1AiGateway.cs`, `R1EndToEndTests.cs` | scripted supplier fact; back-fill via reprocess |
| `backend/tests/Raffa.Api.Tests/PortfolioEndpointTests.cs`, `Contract360EndpointTests.cs`, `RenewalsEndpointTests.cs` | `supplierName` present |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 (names everywhere), ADR-002 (Documents never references Suppliers; ports from SharedKernel; composition in Api), spec §7.3 (critical fields), ADR-011 (audit corrections).
- Gap G-SUPPLIER. Ports from F03/T01: `ISupplierResolver`, `ISupplierNameLookup`, `SupplierRef` in `Raffa.SharedKernel/Suppliers/`.
- **Do not touch**: `DocumentAdmissionGate` (F04/T01), reprocess / list / preview services (F04/T02 — consume only), `ChatEndpointExtensions.cs` / `Program.cs` / `ConversationsEndpointExtensions.cs` (F06/T01 this phase), `Raffa.Market` (F02/T02), `web/`, OpenAPI json.

## Definition of done
- [ ] `dotnet test backend/tests/Raffa.Documents.Contracts.Tests` exit 0 — `supplier` fact at 0.64 → `needs_review` with the field in the review list; at 0.98 → `Contract.SupplierId` set through the fake resolver; correction re-resolves
- [ ] `dotnet test backend/tests/Raffa.IntegrationTests --filter R1` exit 0 — sample MSA shows `supplierName == "Salesforce"` in Portfolio; reprocess of a pre-existing contract without supplier back-fills it
- [ ] `dotnet test backend/Raffa.slnx` exit 0

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | fact mapping, threshold, re-resolve | `Raffa.Documents.Contracts.Tests/*` |
| API | `supplierName` in read models | `Raffa.Api.Tests/*` |
| integration | end-to-end name + back-fill | `Raffa.IntegrationTests/R1EndToEndTests.cs` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E13/F03/US01/T02
  prompt: reports/workitems/epic-13-ask-v2/feature-03-supplier-identity/us-01-supplier-identity/tasks/task-02-supplier-extraction.md
  produces: [supplier-extraction]
  depends_on: [supplier-entity, documents-v2-api]
  effort: L
  layer: backend
  status: live
```
