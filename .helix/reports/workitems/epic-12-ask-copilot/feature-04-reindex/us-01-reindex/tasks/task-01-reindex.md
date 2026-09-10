---
id: E12/F04/US01/T01
type: task
story: us-01-reindex
wave: 12
status: live
target_repo: raffa-backend
---

# task-01-reindex — LoadAsync + re-OCR / re-embed

## Coding objective

Extend `IDocumentStorage` with `LoadAsync` (tenant-prefixed path only, same
rules as `SaveAsync`). Implement it on the Azure blob adapter and the test
fake (`RecordingDocumentStorage`).

Add a worker/API reprocess path that, for this tenant, re-runs hybrid parse
(ADR-017) and embedding (ADR-004 `embed` role) so `chunk_text` is readable
text. If full OCR of historical blobs is too heavy for one task, the
minimum is: load bytes → native/OCR extract → replace garbage chunks →
re-embed. Do not skip tenant checks.

## Parent story AC covered

- AC-1, AC-2, AC-3

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.SharedKernel/Storage/IDocumentStorage.cs` | `LoadAsync` |
| blob adapter + test fakes | implement load |
| Documents / worker reprocess | re-OCR + re-embed |
| tests | load + no `%PDF-1.4` after reprocess |

## Context the implementer needs

- **Architecture**: ADR-017, ADR-023. Gap G-PDF-CHUNKS.
- **Do not touch**: Chat JSON shape (F03), `web/`, Foundry SDK files owned by F01.
- Same-phase as F03: do not edit `ChatEndpointExtensions.cs`.

## Definition of done

- [ ] Unit tests: save then load round-trip; reprocess fixture no longer
      stores `%PDF-1.4` as chunk text.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | LoadAsync + re-embed | Documents / storage tests |

## Open questions blocking this task

- none

## Wave-spec entry

```yaml
- id: E12/F04/US01/T01
  produces: [docs-reindexed]
  depends_on: [foundry-gateway]
```
