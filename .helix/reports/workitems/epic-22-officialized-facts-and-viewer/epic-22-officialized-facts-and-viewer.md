---
id: epic-22
type: epic
wave: w17
status: active
extends: [epic-02, epic-07, epic-16]
---

# epic-22-officialized-facts-and-viewer — the product officializes what it knows and shows the page it read it from

## Business capability

One bar decides whether an extracted fact is accepted: **90 %, every field, the
five critical ones included**. The **server** decides on the raw stored double
and **persists** the decision per field; the web **renders** that decision and
never recomputes it — retiring `acceptedThisSession`, the third kind of
"accepted" that vanished on reload.

With the decision durable, the screens stop asserting things the system does not
hold: Contract 360's Details drops the "facts you still need to decide" block and
the sentence that claimed facts were "signed off by you" with no human-decision
state behind it; the Why row drops the original quote and the confidence
percentage and speaks **leverage** instead of a raw risk enum; and the Review
screen stops **hiding** fields OCR did not recover, offering them as fillable
rows instead.

Underneath, the preview stops being a 480×640 card reading `PREVIEW NOT
RENDERED`: the Worker rasterises **real pages** at pipeline time, the route
gains `?page=n` bounded by the already-persisted `document.page_count`, and a new
`/documents/:documentId/viewer` route shows the page a clause was read from —
reached from Why clauses by deep link, with the wording highlighted **below** the
page.

## Product coverage

| Source | Item |
|--------|------|
| `inputs/next/w17-todo.md` §3 | NW-71 — a field extracted at ≥ 90 % is accepted automatically |
| `inputs/next/w17-todo.md` §2 | NW-26 — preview is a placeholder PNG |
| `inputs/next/w17-todo.md` §2 | NW-63 — document viewer over uploaded pages |
| `inputs/next/w17-todo.md` §2 | NW-64 — unrecovered fields get a fillable section |
| `inputs/next/w17-todo.md` §2 | NW-65 — Details: only officialized facts |
| `inputs/next/w17-todo.md` §2 | NW-66 — Why-clauses: no quote, leverage not confidence |
| `inputs/product-spec.md` `:333-335` | §7.3's three confidence bands — **superseded on the record by NW-71** |
| `inputs/percorso-pilota-v1.md` `:46` | "Soglia HITL" — **superseded on the record by NW-71**, including the "critical stricter" clause |
| `inputs/requirements.md` R-EVD-02, R-DOC-07 | evidence and document surfaces |

## Features

| ID | Title | Wave |
|----|-------|------|
| feature-01 | auto-accept-decision | w17 |
| feature-02 | document-page-rendering | w17 |
| feature-03 | document-viewer | w17 |
| feature-04 | officialized-facts | w17 |
| feature-05 | unrecovered-fields | w17 |
| feature-06 | w17-integration | w17 |

## Extends

- **epic-02** (`contract-intelligence`) — the staged extraction pipeline, its
  evidence rows and the confidence bars NW-71 consolidates into one policy.
- **epic-07 F01/F02** (`web-contract-intelligence`) — Contract 360 and the
  Review screen, the two surfaces NW-64/65/66/71 change.
- **epic-16** (`async-document-processing`) — the Worker pipeline NW-26 adds a
  rasterisation stage to, and the reprocess path NW-73 drives.

## Success looks like

- A field extracted at 0.92 shows **"Accepted automatically · 92 %"** on Review
  after a reload **and in a second browser**; a field at 0.895 shows
  **"Review · 89 %"** — floored, never rounded across the bar.
- The badge on the Documents row and `needs_review` on the document agree,
  because one `ExtractionConfidencePolicy` decides both.
- Opening the preview of a validated Northwind PDF shows **that file's page**,
  not a `PREVIEW NOT RENDERED` card; `?page=2` shows page 2; `?page=99` on a
  3-page document is a **404**, never page 1.
- A Why clause links to `/documents/:documentId/viewer?page=<n>&clause=<id>` and
  the link **lands**; the row shows a leverage word and **no quote and no
  percentage**.
- A field OCR missed appears on Review as an empty fillable row under **"Not
  found in the document"**, and the value typed there survives a reload.
- Contract 360 renders **no confidence tag anywhere** and the Details column
  carries one trailing count line instead of a list of undecided facts.

## Architecture decisions in force

- **ADR-029** (new) — document page rendering and the preview contract: clauses
  1–7 plus the round-3 footer (deterministic key replaced in place; a shorter
  re-render reaps `n > pageCount`).
- **ADR-003** — w17 clause 1: `decision` + `decided_at` on the **existing**
  `extraction_evidence` table, two nullable columns, **no SQL enum, no new
  table**. w17 clause 2: geometry columns are **refused** this wave.
- **ADR-024** — w17 §A: the wire carries **three** states
  (`auto_accepted` / `human_accepted` / `review_required`) and the threshold
  **once**, so the web never hardcodes 90.
- **ADR-021** — w17 clause 1: `documents-contracts.sql` has a **single writer**
  this wave; `backend.yml` is **not** opened (the module is already in both
  arrays, `backend.yml:289` and `:321`).
- **ADR-027** — w17 clauses 1–2: rasterisation is a Worker pipeline stage; a
  reprocess re-derives pages and the decision.
- **ADR-009** — w17 clauses 6, 8: `BuildPreviewPage` validates `page >= 1` and
  throws **itself**; the reap is bounded to
  `{TenantPrefix}documents/{documentId}/preview/` and runs only on a **confirmed
  positive** `pageCount`.
- **ADR-011** — w17 clause 21: one audit row per document per extraction run,
  actor `system:extraction`, **field names and confidence numbers, never a
  value**.
- **ADR-012** — w17 clauses 32–41 and §46–§48: the client renders the server's
  decision; `acceptedThisSession` retires; `client.ts` is one-writer-per-phase
  and NW-63 owns it; every page fetch is revoked in the `useEffect` that owns it;
  **no page cache across a page navigation**; the 404 branch is discriminated
  from `pageCount` with **no fetch**.
- **ADR-018** — w17 clauses 9–15: **one route added**, the first since w14;
  `navItems.ts` gains **no row**; the URL stays on the page that was asked for —
  no clamp, no `onError` swap to page 1.
- **ADR-019** — w17 clauses 7–11: two treatments over three decisions;
  `.tag-accent` is **released from confidence entirely**; **floor, never round**;
  no confidence tag renders on Contract 360 at all.
- **ADR-020** — w17 §13–§18 and §24: the Review title stops naming a threshold;
  the viewer chrome is composed from the locked catalogue; the not-found state is
  **one state, two causes, two sentences**, taking the *empty* treatment and
  never *error*'s Retry.
- **ADR-001** — w17 clauses 2, 5, 6, 8: one bar, no always-review list; the five
  critical fields are always shown, recovered or not; confidence lives in Review
  and Contract 360 never renders it; the three acceptance states stay
  distinguishable.
- **ADR-002** — w17 clause 2: `RenewalPipelineBuilder` stays pure (not this
  epic's file — recorded so no task "fixes" it by injection).

## Out of scope

- **Bounding boxes and editable OCR phrases.** OQ-w17-001's split is ratified:
  w17 ships the viewer over **real pages** with **text-level** highlighting
  anchored on the existing `SourcePage`/`SourceSpan`. `prebuilt-layout`, a
  widened `DocumentIntelligencePage`, geometry columns and a phrase-edit write
  path are the **head of W18**, ahead of this run's overflow items.
- **A new web runtime dependency.** `web/package.json:19-25` carries exactly
  five and must still carry five. Because NW-26 rasterises server-side the
  viewer renders **PNG pages** — `react-pdf`, `pdfjs` and `pdf-lib` are all
  refused, this wave and the W18 remainder.
- **Retiring every confidence threshold.** Exactly **two** sites retire
  (`StagedExtractionService.cs:78,88` and `DocumentQueryService.cs:33,42`).
  `DocumentAdmissionOptions.AdmissionThreshold` (`:33`),
  `Raffa.Quotes`' bar, `SavingsProvenanceClassifier.cs:37,46` and
  `CriticalityScoreCalculator.cs:35` all **stay**, each for its own reason
  (ADR-024 w17 clause A3).
- **Softening the 90 % bar to keep a demo interesting.** The **fixture**
  changes, never the threshold (OQ-w17-po-01): at least two of the pilot's
  critical fields stay strictly below 0.90, and not adjacent to the bar.
- **Restoring the design export.** No task may re-add the confidence tag to the
  Why row, re-add "facts still to decide" to Details, or re-introduce the
  three-band confidence tip. The prototype is stale from the moment these tasks
  land and stays stale until the operator re-exports.
- **A "re-indexing" or "catching up" banner** on Ask or the 360. Nothing
  requeues after ADR-011 w17 clause 26, so it would be a *not ready yet* claim
  that can never resolve (ADR-018 w15 clause 6).
- **An Activity surface.** NW-20 ships `activity` and `benchmark` **on the wire
  with no web consumer** (ADR-012 w17 §45); W18 designs the surface.
