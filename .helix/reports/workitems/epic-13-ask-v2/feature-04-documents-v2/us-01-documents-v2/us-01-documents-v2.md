---
id: us-01
type: user-story
parent: feature-04
wave: 13
status: active
---

# us-01-documents-v2 — Only contracts get in, and every one is listable, readable and re-processable

## Story

As **procurement**, I want to drop one or more files and have Contigo keep
only the contract documents (refusing a recipe or a family photo with a
plain reason, never storing them), then see every kept document in a list
that survives a reload, with its real processing stage, a first-page
preview, and a way to re-process it — so that Ask only ever answers from
readable, validated contract text.

## Acceptance criteria

- [ ] AC-1 A `.zip` renamed `.pdf` → HTTP 415 before any AI call; a
      recipe PDF → 422 `{ rejected: true, detectedType, confidence,
      reason: "not_a_contract", hint }`, no blob, no `document` row, no
      embedding, one audit row `document.rejected` (hash, type,
      confidence, reason — never content).
- [ ] AC-2 A PNG / JPG scan goes through the `ocr` role; unreadable text
      (< `Documents:MinReadableChars`) → 422 `reason: "no_readable_text"`;
      a scanned MSA is admitted with type `Msa`; a Quote is admitted.
- [ ] AC-3 `GET /api/documents?status=&page=` lists the tenant's documents
      (id, contractId, supplierName, fileName, documentType,
      processingStatus, stage, pageCount, createdAt, weakFactCount); the
      web no longer keeps the list in `sessionStorage`.
- [ ] AC-4 `IDocumentStorage.LoadAsync` round-trips a saved object under
      the tenant prefix; `POST /api/documents/{id}/reprocess` (Admin)
      re-parses and re-embeds; afterwards no embedding row of the tenant
      starts with `%PDF` or contains the fixture OCR placeholder, and every
      row carries `page` (and `section` when known).
- [ ] AC-5 `GET /api/documents/{id}/preview` returns a PNG for PDF /
      images and an honest placeholder for DOCX / XLSX, tenant-scoped,
      never a raw blob URL; `DELETE /api/documents/{id}` (Admin) removes
      blob, row and embeddings and audits `document.deleted`; Procurement
      gets 403.
- [ ] AC-6 Processing status transitions stay `uploaded → processing →
      needs_review | completed | failed` (spec §7.1); no `Rejected` status
      exists server-side.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-v2-foundation | live `ocr` / `embed` roles for reprocess (T02) |

## Architecture decisions in force

- ADR-024 — gate before persistence; rejected never stored; page-aware evidence
- ADR-017 — hybrid parse, images via Document Intelligence, page budget fails visibly
- ADR-009 / ADR-011 — tenant-prefixed storage; audit without content
- ADR-021 — regenerated `documents-contracts.sql` (`page`, `section`, `page_count`, `preview_path`)
- spec §4.1 types, §7.1 statuses, §8.4 evidence

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | Admission gate, format sniffing, endpoints moved out of `Program.cs`, 415 / 422, OpenAPI | L | phase-1 |
| T02 | Documents V2 API: `LoadAsync`, list, preview, reprocess, delete, page-aware embeddings | L | phase-2 |

## Council decisions carried into this story

Admitted types: `Msa`, `OrderForm`, `Sow`, `Amendment`, `RenewalLetter`,
`Quote`, `Invoice`, `PriceList`, `Nda`, `Dpa` (extend `AiDocumentType` /
`ContractDocumentType` mapping accordingly; `Other` is rejected).
Configuration keys `Documents:AdmissionThreshold` (0.6),
`Documents:MinReadableChars` (200), `Documents:MaxFileBytes` (50 MB).
Rejection copy (web, `screens-v2.md` §3): *"Not added: this looks like a
recipe, not a contract. Contigo only keeps contracts, order forms, quotes
and the documents around them. Drop the signed agreement or the supplier's
proposal."* API hint: *"Contigo only keeps contracts, order forms, quotes
and the documents around them."*

## Open questions

- OQ-askv2-002 — thresholds are configuration, tuned on the golden set (assumed)
- OQ-askv2-007 — upload stays synchronous in the request (assumed)
- OQ-askv2-008 — a Quote admitted here is routed to Quote check, no automatic Quote record (assumed)
