---
id: us-01
type: user-story
parent: feature-03
wave: w15
status: active
---

# us-01-documents-states-and-counts — Documents, the rail and the downstream screens read the server

## Story

As a **procurement user**, I want the counts beside my documents to match the
rows I can see, a refused file to stay visible with its reason after a reload,
and Ask, Portfolio and Contract 360 to tell me when something is still
processing, so that I am never shown "0" beside fifteen rows and never shown an
empty contract as if it were finished.

## Acceptance criteria

- [ ] AC-1 Dropping fifteen files shows fifteen rows within 2 s, and the chips agree with them: "All documents" and "Needs your attention" read the server's `counts`, never `documents.length` over the fetched page. A reload and a second browser show the same fifteen.
- [ ] AC-2 The optimistic local row is dropped **on evidence** — when a server row with the 201's id is present — not on a timer and not before `load()` succeeds. If `load()` fails the row stays and the list's error state shows. No flicker on any file of a fifteen-file batch.
- [ ] AC-3 A content-refused document renders as a **row** labelled **"Not added"** with `.tag-outline`, its reason in the existing hint slot, no action cell, and it survives a reload. `UploadResultCard.tsx` is deleted.
- [ ] AC-4 The two **pre-storage** refusals — the browser-side oversize check and 413/415 — render as a **local row** in the same grid, session-only by construction (no id, never in `documents`, never in `counts`). Neither is a silent drop.
- [ ] AC-5 A third filter chip **`Not added · K`** reads `counts.rejected` and renders **only while K > 0**; selecting it fetches `listDocuments({ status: … })` from the server rather than bucketing the page.
- [ ] AC-6 A server row with a null stage reads **"Queued…"**; the local row's "Uploading…" is a different fact and stays.
- [ ] AC-7 The rail Documents badge reads the server. `documentStore.ts` is deleted outright — its writer `rememberDocument` has **zero production call sites**, so the badge is not wrong today, it is *always absent*.
- [ ] AC-8 The upload request carries one client-owned deadline, configured in **one** place and never per call site; an abort maps to the existing `"failed"` branch so the user gets Raffa.ai's retryable message, not an opaque platform 502.
- [ ] AC-9 Ask, Portfolio and Contract 360 each distinguish **still processing** from **nothing here**, from a server field. Ask re-reads while it is off, on the same 2 s cadence, so a user sitting on `/ask` sees the gate flip.
- [ ] AC-10 When nothing changes for five minutes the list stops polling and says so, with an explicit resume. It never re-labels a row `Failed`, never drives the progress bar from a client timer, and never introduces a second definition of terminal. The same budget, sentence and label bind all five polling surfaces.
- [ ] AC-11 No number on screen 3 is derived from the fetched page: `buildKbSummary` becomes a function of `counts`, and its "M askable" segment is **removed**.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-03-upload-returns-when-stored | every AC here reads a server field — `counts`, `readiness`, the rejection reason code, the `status` filter value — that does not exist until that task ships it |

## Architecture decisions in force

- **ADR-012** (w15 §4–§6, §10, §13.4–§13.8, §17–§18) — a client must not infer a server state it can be told; a count has one definition and it is the server's.
- **ADR-018** (w15 clauses 1–3, 6) — the two new IA states, their two binding rules, the five surfaces they bind, and the one stopped-updates budget.
- **ADR-020** (w15 §1, §2, §6, §8) — every string on every state named here.
- **ADR-019** (w15 clauses 1–2, 4–6) — one semantic row; no token, no component.
- **ADR-024** — Ask never answers from a document that has not passed validation; *validated* is contract-level.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Server-read counts, the refusal row, the stopped poll, the rail badge and three gated screens | L | phase-4 |

## Council decisions carried into this story

**An empty state must never tell a user to do a thing they have already done.**
That is why Portfolio's third variant is the shipped sentence *minus* its "Upload
one to start." clause, and why a tenant holding only refused files gets the
**first** Ask variant ("Upload a contract first") — true for them, because the
counting rule excludes `Rejected` from what Raffa.ai holds and they were already
told per row why each file was refused.

**Ask's third off-state must not be collapsed into the "still processing"
sentence.** Doing so re-ships today's fabricated fact under a server number and
fails A15-3 while appearing to satisfy it.

**A server field may *select* the sentence a user reads; it may never *supply*
it.** `uploadPipeline.ts:123` currently renders `result.error ?? "Not added: this
file could not be added."` — a user-facing sentence the API authors, with no
oracle behind it.

## Open questions

- **OQ-w15-008** — resolved: NW-10 rides this task. It shares `useDocumentsList.ts` and the shell files with the counts work, so splitting it buys a single-writer conflict for no benefit.
- **OQ-w15-ca-01** — **assumption in force: a client-owned deadline of 120 s**, with its derivation recorded in the task. ADR-005 clause 13 pins no ingress ceiling and gives the reason: NW-27 makes the API's longest synchronous request *shorter*.
- **OQ-w15-ca-05** — answered: the stopped-poll notice's copy and resume label are in ADR-020 w15 §8, widened to five surfaces by ADR-018 w15 clause 6.
- **OQ-w15-D2** — **assumption in force: acceptable.** A `Rejected` row accumulates with no next step for a Procurement user (only an Admin may `DELETE`). Bounded by the chip rendering only while `counts.rejected > 0` and by refusals having their own filter. Revisit only if a pilot user complains.
