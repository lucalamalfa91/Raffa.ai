---
id: E13/F04/US01/T01
type: task
story: us-01-documents-v2
wave: 13
status: live
target_repo: contigo-backend
---

# task-01-documents-admission — Admission gate before persistence, formats, endpoints out of `Program.cs`

## Coding objective

Make "only contracts get in" true at the API (HITL D3, D7;
`inputs/requirements.md` R-DOC-02…05). Move the two document endpoints
from `backend/src/Contigo.Api/Program.cs` into a new
`DocumentsEndpointExtensions.cs` (`MapDocumentsEndpoints()`), keeping
their behaviour, and reorder `POST /api/documents` to: (1) format check by
extension **and magic bytes** (`%PDF`, OOXML zip signature + content type,
PNG, JPEG) → HTTP 415 with "Contigo reads PDF, Word, Excel and scanned
images" before any AI call; size ≤ `Documents:MaxFileBytes` (50 MB) → 413;
(2) parse in memory through `HybridDocumentParsingService` (PNG / JPG take
the OCR path: `NativeDocumentTextExtractor.CanHandle` stays false for
images so they reach `IAiGateway.OcrAsync`); (3) the new
`DocumentAdmissionGate` (`Contigo.Documents.Contracts/Application/Admission/`):
readable text ≥ `Documents:MinReadableChars` (200) else
`reason: no_readable_text`; `IAiGateway.ClassifyAsync` → admitted when the
type is one of MSA, Order Form, SOW, Amendment, Renewal letter, Quote,
Invoice, Price list, NDA, DPA **and** confidence ≥
`Documents:AdmissionThreshold` (0.6), else `reason: not_a_contract`;
(4) only when admitted: `DocumentUploadService.UploadAsync` (blob + rows)
then `DocumentProcessingPipeline.ProcessAsync` **reusing the pages and
classification already computed** (extend `ProcessAsync` with an overload
that takes the parsed pages + classification so the model is not called
twice). A rejection returns 422 `{ rejected: true, detectedType,
confidence, reason, hint }` with the hint "Contigo only keeps contracts,
order forms, quotes and the documents around them.", writes one audit row
`document.rejected` (`ResourceId` = SHA-256 of the bytes; detail = type,
confidence, reason — never file name content or text) and persists
**nothing**. Extend `AiDocumentType` → `ContractDocumentType` mapping so
Quote / Invoice / PriceList / Nda / Dpa are kept as their own types
(add the enum members; `Other` is never stored). The fixture gateway
must make this testable: a recipe → `Other` → rejected; a text containing
"MASTER SERVICES AGREEMENT" → `Msa` → admitted. Update
`web/openapi/contigo-api.v1.json` for the new 413 / 415 / 422 responses
and the widened `documentType` enum (this task is the phase-1 writer of
that file; do not regenerate the TS client).

## Parent story AC covered
- AC-1, AC-2, AC-6

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/src/Contigo.Api/DocumentsEndpointExtensions.cs` | new (moved + reordered upload, existing GET by id) |
| `backend/src/Contigo.Api/Program.cs` | remove the inline document endpoints; call `MapDocumentsEndpoints()` (phase-1 writer of this file) |
| `backend/src/Contigo.Documents.Contracts/Application/Admission/DocumentAdmissionGate.cs`, `AdmissionDecision.cs`, `DocumentFormatSniffer.cs`, `DocumentAdmissionOptions.cs` | new |
| `backend/src/Contigo.Documents.Contracts/Application/Extraction/DocumentProcessingPipeline.cs` | overload taking parsed pages + classification |
| `backend/src/Contigo.Documents.Contracts/Application/Extraction/HybridDocumentParsingService.cs` | image mime types routed to OCR (verify; adjust only if needed) |
| `backend/src/Contigo.Documents.Contracts/Domain/ContractDocumentType.cs`, `Contigo.AiGateway/Contracts/AiDocumentType.cs` mapping site | new members Quote, Invoice, PriceList, Nda, Dpa |
| `backend/src/Contigo.Documents.Contracts/Infrastructure/ServiceCollectionExtensions.cs` | register gate + options |
| `backend/tests/Contigo.Documents.Contracts.Tests/Admission/*` | gate + sniffer tests |
| `backend/tests/Contigo.Api.Tests/DocumentUploadEndpointTests.cs` | 415 / 413 / 422 / 201 paths; nothing persisted on 422 |
| `web/openapi/contigo-api.v1.json` | responses + enum |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 (gate before persistence; rejected never stored), ADR-017 (images via Document Intelligence; page budget), ADR-011 (audit without content), ADR-009 (tenant scope), ADR-004 (`classify` role).
- Gap G-ADMISSION. Prototype copy for the web card (not this task): `inputs/design/prototypes/contigo-v2/screens-v2.md` §3 "Not added".
- **Do not touch**: `IDocumentStorage` / `LoadAsync`, list / preview / reprocess / delete (T02, phase 2), `StagedExtractionService` (F03/T02), `Contigo.AiGateway` internals (F01/T02), `web/src` (F09).
- The enum widening changes the `documentType` values exposed by `GET /api/documents/{id}`; keep the existing values' names unchanged.

## Definition of done
- [ ] `dotnet test backend/tests/Contigo.Api.Tests` exit 0 — `.zip` renamed `.pdf` → 415 with zero gateway calls (recording gateway); recipe text PDF → 422 `not_a_contract`, `RecordingDocumentStorage` has no save, no `document` row, one audit row `document.rejected`; image with < 200 readable chars → 422 `no_readable_text`; MSA → 201 `processingStatus` as before
- [ ] `dotnet test backend/tests/Contigo.Documents.Contracts.Tests` exit 0 — gate decisions per type / threshold; sniffer per signature
- [ ] `dotnet test backend/Contigo.slnx` exit 0 (pipeline overload keeps R1 integration tests green)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | gate + sniffer | `Contigo.Documents.Contracts.Tests/Admission/*` |
| API | 415 / 413 / 422 / 201 and nothing persisted on rejection | `Contigo.Api.Tests/DocumentUploadEndpointTests.cs` |

## Open questions blocking this task
- OQ-askv2-002 — thresholds are configuration (assumed)
- OQ-askv2-007 — synchronous upload (assumed)

## Wave-spec entry
```yaml
- id: E13/F04/US01/T01
  prompt: reports/workitems/epic-13-ask-v2/feature-04-documents-v2/us-01-documents-v2/tasks/task-01-documents-admission.md
  produces: [documents-admission]
  depends_on: []
  effort: L
  layer: backend
  status: live
```
