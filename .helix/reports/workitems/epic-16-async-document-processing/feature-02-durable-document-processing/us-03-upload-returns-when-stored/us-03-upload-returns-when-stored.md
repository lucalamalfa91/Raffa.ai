---
id: us-03
type: user-story
parent: feature-02
wave: w15
status: active
---

# us-03-upload-returns-when-stored — Upload returns when the file is stored, and every aggregate tells the truth

## Story

As a **procurement user**, I want the upload request to come back as soon as my
file is safely stored, and every screen that shows me a contract or a count to
know the difference between "there is nothing here" and "this is not ready
yet", so that I never wait on a spinner for a minute and never read an empty
contract as if it were the truth.

## Acceptance criteria

- [ ] AC-1 `POST /api/documents` returns **201 within 2 s** once the blob and the rows are durable, for every file of a batch. Nothing in the request awaits classify, OCR, extraction or embedding.
- [ ] AC-2 The admission gate **splits**: format and size are refused **in the request** (415 / 413, nothing stored, as today); content classification happens on the Worker. The content-refusal **422 leaves the endpoint** — a refused file is now a row.
- [ ] AC-3 A content-refused document becomes a **terminal `Rejected` row** with its reason **code** persisted and its blob deleted. It is never askable, and the existing `DELETE /api/documents/{id}` removes it with no change.
- [ ] AC-4 `POST /api/documents/{id}/reprocess` takes the **same shape** — it re-enqueues and returns, instead of re-parsing, re-classifying and re-embedding inside the request.
- [ ] AC-5 `GET /api/documents` returns server-computed **`counts { all, needsAttention, needsReview, processing, rejected }`**, tenant-wide and never page-wide, from **one** grouped query. `all` **excludes** `Rejected`; `needsAttention` is *not `Completed` **and** not `Rejected`*.
- [ ] AC-6 `GET /api/contracts/{id}` returns **`readiness { state, stage, documentCount, completedDocumentCount }`** with `state` one of `ready` / `processing` / `unavailable`, so a mid-pipeline contract is distinguishable from one whose extraction genuinely found nothing.
- [ ] AC-7 `GET /api/contracts` returns `processingDocumentCount`.
- [ ] AC-8 `GET /api/documents`'s `items[]` carries the rejection **reason code**, so a refused row is still legible after a reload.
- [ ] AC-9 The `status` query parameter accepts the new value, and **all eight** `processingStatus` sites in `web/openapi/raffa-api.v1.json` are updated together.
- [ ] AC-10 `AskCopilotService`'s *"Nothing in the N validated contract(s)…"* string stops rendering an **unfiltered** count — the product states a fabricated fact in shipped copy today, and A15-3 forbids exactly that.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-02-durable-queue-transport | the endpoint publishes to a transport that must already exist, and the Worker must already be able to do the work the endpoint stops doing |

## Architecture decisions in force

- **ADR-027 §D1, §D6–§D9** and §C1, §C5, §C9 — the 201 contract, the `Rejected` record, `counts`, `readiness`, and the ruling that `counts` is **five overlapping projections, not a partition**.
- **ADR-024** (w15 footer §1–§4) — the "classify before store" ordering is superseded; A7, `OQ-askv2-007` and R-DOC-05 AC-1 are `assumed-wrong`; **one** definition of *validated*, which is contract-level, not document-level.
- **ADR-012** (w15 §9, §10) — land `counts`/`readiness` **inline**, touch no generator, hand-write no DTO; the contract has **eight** `processingStatus` sites and the eighth is a query parameter invisible to `tsc`.
- **ADR-026 §D2** — the single definition of *validated*; `readiness.stage` is nullable, so it **must not** carry an `enum`.
- **ADR-001** (w15 clauses 1–3) — a refusal is persistent, visible and terminal, whose content Raffa does not keep; "still processing" outranks "empty".

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | The endpoint returns when stored; the aggregates stop guessing | L | phase-3 |

## Council decisions carried into this story

**Why the gate splits rather than staying put.** `AdmissionDecision.Pages`
carries the parsed text in memory (`AdmissionDecision.cs:67`) and feeds the
pipeline's pages overload (`DocumentProcessingPipeline.cs:174-182`), so today the
product parses **once**. A 256 KB message cannot carry pages — so a gate left in
the request would make the Worker **OCR every upload a second time**,
permanently more expensive *and* slower. Keeping it synchronous is worse on both
axes.

**`counts` does not gain `askable`.** `buildKbSummary` is wrong twice — it
counts the fetched page and equates askable with `processingStatus ==
"Completed"`, a **document**-level test where ADR-026 §D2's *validated* is
**contract**-level. Putting it on `counts` would mint a **fifth** definition of
ready in the wave whose §D8 collapses four into one. The web segment is
**removed** instead (ADR-018 w15 clause 3, branch two — resolved, not left open).

**`needsAttention` excludes `Rejected` as well as `Completed`.** §D7 originally
read `NeedsReview + Failed`, justified as "the definition the client already
applies" — and that premise is false: `isAttentionStatus` is
`processingStatus !== "Completed"` (`documentTable.ts:141-143`). Since attention
is the **default** filter, shipping the narrow definition would put fifteen
just-dropped rows under a chip reading **"Needs your attention · 0"** — NW-61's
reported defect, re-created on the server where it looks authoritative.

## Open questions

- **OQ-w15-004** — resolved: the gate splits; `Rejected` is terminal, code-only, blob deleted.
- **OQ-w15-003** — resolved: A7 / `OQ-askv2-007` are `assumed-wrong`; the comment citing them as authority is retired in this task.
- **OQ-w15-D3** — resolved: R-DOC-05 AC-1 is superseded **on the record** by product-owner, never edited in place.
