---
id: feature-02
type: feature
parent: epic-22
wave: w17
status: active
extends: epic-16 F01, epic-02 F01
---

# feature-02-document-page-rendering — the Worker rasterises real pages and the route can ask for one

## Slice

`PlaceholderDocumentPreviewRenderer` is the **only** `IDocumentPreviewRenderer`
on this checkout (port `Application/Preview/IDocumentPreviewRenderer.cs:19`,
implementation `Preview/PlaceholderDocumentPreviewRenderer.cs:26`, sole
registration `Infrastructure/ServiceCollectionExtensions.cs:127`). It passes
bytes through for an already-PNG document and otherwise paints a 480×640 card
with the literal caption `PREVIEW NOT RENDERED` (`:66`); the raster engine says
so itself (`Preview/PngImage.cs:22-24`).

This feature registers a **real** renderer ahead of the placeholder — which
stays the honest fallback (`Preview/DocumentPreviewService.cs:53-54`) — runs it
as a **Worker pipeline stage** at
`Extraction/DocumentProcessingPipeline.cs:239-244`, adds
`DocumentStoragePath.BuildPreviewPage(tenantId, documentId, page)` under the same
tenant prefix and the same `EnsureWithinTenant` guard, gives
`GET /api/documents/{id}/preview` an optional 1-based `?page=n` bounded by the
already-persisted `document.page_count` (`documents-contracts.sql:834`), and puts
`pageCount` on the document read model. Without both of those last two, NW-63's
viewer has exactly one page and its own acceptance is unreachable.

**No migration**: `page_count` already exists. That is what lets
`documents-contracts.sql` have a single writer this wave (ADR-021 w17 clause 1).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | rasterised-pages | w17 |

## Architecture decisions in force

- **ADR-029** (new) — clauses 1–7: rasterise in the Worker, never on the request
  path; after admission and independent of extraction success; a real renderer
  registers ahead of the placeholder; per-page storage; `?page=n` with a **404**
  out of range; `pageCount` on the read model; the page budget is a **stated
  cap, never a silent truncation**.
- **ADR-029 round-3 clauses 1–2** — the per-page key is **deterministic and
  replaced in place** (no suffix, no timestamp, no render id, no attempt
  counter); a re-render yielding fewer pages **deletes `page-{n}.png` for
  `n > pageCount`** in the same stage that writes 1…N.
- **ADR-009** w17 clause 6 — `BuildPreviewPage` validates `page >= 1` and
  **throws itself**, exactly as `DocumentStoragePath.Build` does for
  `versionNumber` (`:42-46`). `page` is an **`int`, never a string**.
- **ADR-009** w17 clause 8 — the reap is bounded to
  `{TenantPrefix}documents/{documentId}/preview/` only, and runs only on a
  **confirmed positive** `pageCount`: null, zero or defaulted deletes **nothing**.
- **ADR-005** w17 §19–§21, §23–§25 — render and store **page by page, disposing
  each bitmap**; the Container Apps pair stays `0.25 vCPU / 0.5 GiB` with a
  named, pre-authorised contingency; ACR stays `Basic`.
- **ADR-027** w17 clause 1 — the stage is part of the pipeline a reprocess
  re-runs.
- **ADR-017** w17 footer — `prebuilt-layout` **stays uncalled**; this feature
  needs no AI-gateway change.

## Target repo

`raffa-backend` (the renderer, the Worker stage, the storage path, the route and
the read model; conditionally `backend/src/Raffa.Worker/Dockerfile`).
