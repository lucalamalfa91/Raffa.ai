---
id: E13/F04/US01/T02
type: task
story: us-01-documents-v2
wave: 13
status: live
target_repo: raffa-backend
---

# task-02-documents-v2-api — `LoadAsync`, list, preview, reprocess, delete, page-aware embeddings

## Coding objective

Complete the Documents V2 API surface (`inputs/requirements.md`
R-DOC-06…10, R-EVD-01, §6). (1) `IDocumentStorage.LoadAsync(tenantId,
storagePath)` in SharedKernel with the same tenant-prefix rules as
`SaveAsync`; implement on `AzureBlobDocumentStorage` and on the test fake
`RecordingDocumentStorage`; a path outside the tenant prefix is refused.
(2) `Embedding` gains `Page` (int?) and `Section` (string?);
`EmbeddingRetrievalService.IndexChunkAsync` takes page + section and
`SearchAsync` returns them; `DocumentProcessingPipeline.IndexForRetrievalAsync`
passes the page number (and the clause section when the chunk maps to a
clause span). (3) `DocumentQueryService.ListAsync(tenant, status?, page,
pageSize)` → items {id, contractId, supplierName (via
`ISupplierNameLookup` when registered; null otherwise), fileName,
documentType, processingStatus, stage, pageCount, createdAt,
weakFactCount}; `Document` gains `PageCount` and `PreviewPath`.
(4) `DocumentReprocessService.ReprocessAsync(tenant, documentId)`: load
bytes, hybrid parse (native / OCR), replace the document's embeddings with
page-aware chunks, re-run staged extraction (so later supplier facts
back-fill), return the summary; exposed as `POST /api/documents/{id}/reprocess`
(Admin, `X-Workspace-Role` / claims as the existing admin checks do).
(5) `DocumentPreviewService`: render page 1 to PNG at upload (PDF via the
existing native parser's raster path or `PDFtoImage`-class library allowed
in `Raffa.Api` infrastructure only; PNG / JPG copied; DOCX / XLSX → a
generated placeholder PNG with the file type), stored under the tenant
prefix; `GET /api/documents/{id}/preview` streams it, tenant-scoped.
(6) `DELETE /api/documents/{id}` (Admin): blob + preview + rows +
embeddings; detach `Contract` document link; audit `document.deleted`;
Procurement → 403. Regenerate `Migrations/Scripts/documents-contracts.sql`
(ADR-021; script test keeps it honest). All endpoints go in
`DocumentsEndpointExtensions.cs` (created by T01); do not edit
`Program.cs` and do not touch `web/openapi/raffa-api.v1.json` (the
phase-3 web task documents these endpoints from `inputs/requirements.md` §6).

## Parent story AC covered
- AC-3, AC-4, AC-5

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/src/Raffa.SharedKernel/Storage/IDocumentStorage.cs` | `LoadAsync` |
| `backend/src/Raffa.Api/Infrastructure/AzureBlobDocumentStorage.cs` | implement load + delete + preview blob helpers |
| `backend/tests/Raffa.IntegrationTests/RecordingDocumentStorage.cs` | implement load / delete |
| `backend/src/Raffa.Documents.Contracts/Domain/Embedding.cs`, `Document.cs` | `Page`, `Section`, `PageCount`, `PreviewPath` |
| `backend/src/Raffa.Documents.Contracts/Application/EmbeddingRetrievalService.cs`, `EmbeddingSearchResult.cs` | page-aware index + search |
| `backend/src/Raffa.Documents.Contracts/Application/Extraction/DocumentProcessingPipeline.cs` | page-aware indexing, page count, preview hook (phase-2 writer of this file) |
| `backend/src/Raffa.Documents.Contracts/Application/DocumentQueryService.cs`, `DocumentListItem.cs`, `DocumentReprocessService.cs`, `DocumentPreviewService.cs`, `DocumentDeleteService.cs` | new / extended |
| `backend/src/Raffa.Documents.Contracts/Infrastructure/Configurations/*`, `Migrations/*`, `Migrations/Scripts/documents-contracts.sql` | migration + regenerated script |
| `backend/src/Raffa.Api/DocumentsEndpointExtensions.cs` | list, preview, reprocess, delete |
| `backend/tests/Raffa.Documents.Contracts.Tests/*`, `backend/tests/Raffa.Api.Tests/Document*EndpointTests.cs` | tests |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 (page-aware evidence, tenant-scoped preview), ADR-009 / ADR-011 (never a raw blob URL; tenant prefix), ADR-017 (hybrid parse on reprocess; page budget), ADR-021 (regenerated idempotent script), ADR-004 (`ocr` / `embed` roles).
- Gap G-DOC-API. `ISupplierNameLookup` exists from F03/T01 (same phase) as a **port in SharedKernel** — resolve it optionally (`GetService`) so this task builds without the Suppliers module registered.
- **Do not touch**: `DocumentAdmissionGate` (T01), `StagedExtractionService` fact mapping (F03/T02), `Program.cs`, OpenAPI json, `web/`.

## Definition of done
- [ ] `dotnet test backend/tests/Raffa.Documents.Contracts.Tests` exit 0 — save then load round-trip; list paging + status filter; reprocess of a `%PDF-1.4` fixture leaves no chunk starting with `%PDF` and every chunk with `Page ≥ 1`; migration script test green
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exit 0 — list, preview (PNG content type; placeholder for DOCX), reprocess (Admin 200 / Procurement 403), delete (Admin 204 and storage delete recorded / Procurement 403)
- [ ] `dotnet test backend/Raffa.slnx` exit 0

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | load / list / reprocess / preview / delete services | `Raffa.Documents.Contracts.Tests/*` |
| API | endpoints, roles, content types | `Raffa.Api.Tests/Document*EndpointTests.cs` |
| integration | reprocess removes `%PDF` on the R1 fixtures | `Raffa.IntegrationTests/R1EndToEndTests.cs` (extend) |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E13/F04/US01/T02
  prompt: reports/workitems/epic-13-ask-v2/feature-04-documents-v2/us-01-documents-v2/tasks/task-02-documents-v2-api.md
  produces: [documents-v2-api]
  depends_on: [documents-admission, foundry-gateway]
  effort: L
  layer: backend
  status: live
```
