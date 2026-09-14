---
id: us-01
type: user-story
parent: feature-04
wave: 13
status: active
---

# us-01-documents-v2 — Only contracts get in, and every one is listable, readable and re-processable

## Story

As **procurement**, I want to drop one or more files and have Raffa keep
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
recipe, not a contract. Raffa only keeps contracts, order forms, quotes
and the documents around them. Drop the signed agreement or the supplier's
proposal."* API hint: *"Raffa only keeps contracts, order forms, quotes
and the documents around them."*

## Open questions

- OQ-askv2-002 — thresholds are configuration, tuned on the golden set (assumed)
- OQ-askv2-007 — upload stays synchronous in the request (assumed)
- OQ-askv2-008 — a Quote admitted here is routed to Quote check, no automatic Quote record (assumed)

## Superseded in part (2026-09-14, wave w15)

**`status` stays `active`. This story is *not* superseded whole** — AC-2, AC-3,
AC-4 and AC-5 are still exactly what the product wants, and a reviewer should
still hold a task to them.

Two clauses are superseded by **NW-27**, on the ruling of `OQ-w15-004` at the w15
council (`reports/architecture/waves/w15.md`), which splits the admission gate:
format and size stay in the request, content classification moves to the Worker,
and a refused file becomes a **persistent, visible, terminal `Rejected` record**
whose content Raffa does not keep.

| Clause | Status | Replaced by |
|---|---|---|
| **AC-6** (`:44-46`), entirely — *"no `Rejected` status exists server-side"* | **superseded**, negated word for word | `epic-16-async-document-processing/feature-02-durable-document-processing/us-01-async-processing-schema` (the status value) and `us-03-upload-returns-when-stored` (the row) |
| **AC-1** (`:22-26`), its *"no blob, no `document` row"* clause on a **422 content refusal** only | **superseded in part** | `epic-16-…/feature-02-…/us-03-upload-returns-when-stored` |
| **AC-1**'s 415 format clause (a `.zip` renamed `.pdf`) | **stands** — format and size are still refused in-request with nothing stored | — |

`OQ-askv2-007` ("upload stays synchronous in the request"), listed under Open
questions above, is recorded as **`assumed-wrong` from wave w15 on**
(`OQ-w15-003`; ADR-024 w15 footer §3). `inputs/requirements.md` A7 and R-DOC-05
AC-1 are superseded **on the record** in the same way — this process never edits
`inputs/**`.

Authority: `reports/architecture/ADR-001-scope-r0-r4.md` (w15 footer clauses 1–2),
`reports/architecture/ADR-024-ask-raffa-v2.md` (w15 footer §1–§3),
`reports/architecture/ADR-027-async-document-processing.md` (§D1, §D6), and
`reports/architecture/waves/w15.md` §"Work-item instructions", where product-owner
corrects §6 of the intake and requires this banner.
