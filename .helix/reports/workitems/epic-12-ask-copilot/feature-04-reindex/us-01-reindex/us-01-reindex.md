---
id: us-01
type: user-story
parent: feature-04
wave: 12
status: active
---

# us-01-reindex — Readable evidence for Ask

## Story

As **procurement**, I want citations to quote the contract text, so I am
not shown raw PDF headers in the copilot.

## Acceptance criteria

- [ ] AC-1 `IDocumentStorage` can load a previously saved tenant object.
- [ ] AC-2 A reprocess path re-OCR / re-embeds (or at least re-embeds from
      extracted text) existing documents for the current tenant.
- [ ] AC-3 Ask retrieval after reprocess does not return `%PDF-1.4` as
      `chunk_text` for those fixtures.

## Definition of done

- [ ] AC verified by named tests
- [ ] honours ADR-017 / ADR-023

## Dependencies

| Depends on | Why |
|------------|-----|
| F01 | live OCR/embed roles |

## Architecture decisions in force

- ADR-017 — OCR in V1
- ADR-023 — no PDF dump in answers

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | LoadAsync + reprocess | L | phase-2 |

## Open questions

- none
