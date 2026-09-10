---
id: F04
type: feature
parent: epic-13
wave: 13
status: active
---

# feature-04-documents-v2 — Admission gate, formats, list, load, reprocess, preview, delete

## Slice

The Documents intake of HITL decisions D1, D3, D7, D8 as an API: format
sniffing by magic bytes (PDF / DOCX / XLSX / PNG / JPG; PNG / JPG go to the
`ocr` role), an **admission gate before any persistence** (classify → admit
spec §4.1 types at confidence ≥ threshold, else HTTP 422 with a warm reason
and one audit row, nothing stored), the document endpoints moved out of
`Program.cs`, then the V2 surface: server-side list with attention
filter, `IDocumentStorage.LoadAsync`, reprocess (re-OCR / re-embed with
page-aware chunks so Ask never cites `%PDF-1.4`), first-page preview,
Admin delete (`inputs/requirements.md` R-DOC-01…10). Absorbs e12 F04.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Only contracts get in, and every one is listable, readable and re-processable | 13 |

## Architecture decisions in force

- ADR-024 — gate before persistence, rejected never stored, page-aware evidence
- ADR-017 — OCR in V1, images first-class, page budget fails visibly
- ADR-009 / ADR-011 — tenant-prefixed storage, never a raw blob URL, audit without content
- ADR-021 — regenerated `documents-contracts.sql`
- spec §7.1 statuses, §8.4 evidence

## Target repo

`raffa-backend`
