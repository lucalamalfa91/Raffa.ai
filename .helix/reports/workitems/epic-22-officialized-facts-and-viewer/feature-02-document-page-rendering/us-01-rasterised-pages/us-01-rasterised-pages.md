---
id: us-01
type: user-story
parent: feature-02
wave: w17
status: active
---

# us-01-rasterised-pages — the preview is the document's own page

## Story

As a **procurement reviewer**, I want the preview to show **the page of the file
I uploaded**, so that **I can see where a clause was read from instead of a card
that says `PREVIEW NOT RENDERED`**.

## Acceptance criteria

- [ ] AC-1 Opening the preview of a validated PDF shows **that file's first
      page**, not a placeholder card. (N17)
- [ ] AC-2 `GET /api/documents/{id}/preview?page=2` on a multi-page document
      returns **page 2**.
- [ ] AC-3 `?page=0`, `?page=-1` and a page above the document's
      `page_count` return **404** — never a silent fallback to page 1.
- [ ] AC-4 The document read model carries **`pageCount`**, so a client can page
      at all.
- [ ] AC-5 A document whose **extraction failed** is still viewable: the
      rasterisation stage runs after admission and independently of extraction
      success, and a renderer that throws degrades to "no preview" rather than
      failing the document.
- [ ] AC-6 Re-processing the same document **overwrites** its page objects at the
      same keys — no suffix, no timestamp, no accumulation — and a re-render
      that yields **fewer** pages deletes the surplus `page-{n}.png` for
      `n > pageCount`.
- [ ] AC-7 Pages beyond the stated per-document budget are **not rendered and
      the product says so** — never a silent truncation.
- [ ] AC-8 Rendering a **20-file batch on `dev`** completes with **zero Worker
      replica restarts**.
- [ ] AC-9 A path built for another tenant's prefix still throws, and
      `BuildPreviewPage(…, 0)` throws rather than building a path.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| none in this wave | the placeholder seam, `page_count`, the pipeline stage hook and the storage guard are all already on `main` |

## Architecture decisions in force

- **ADR-029** clauses 1–7 and round-3 clauses 1–2 (see the feature file).
- **ADR-009** w17 clauses 6 and 8 — the builder validates and throws; the reap
  is prefix-bounded and gated on a confirmed positive page count.
- **ADR-005** w17 §19 — both apps run at **0.25 vCPU / 0.5 GiB** from a
  **single shared pair** (`containerapps/variables.tf:70-80`); an A4 page at
  150 DPI is ≈ **8.4 MB per bitmap** and the Worker runs
  **`MaxConcurrentCalls = 4`** per replica. Render and store **page by page,
  disposing each bitmap** — multi-page then multiplies the *work*, not the
  *peak*.
- **ADR-005** w17 §20 — if the renderer's Linux native-dependency list is
  non-empty, the `RUN` layer lands **in this same task**, **above
  `USER $APP_UID`** (`Raffa.Worker/Dockerfile:34`) and **above
  `COPY --from=build`** (`:31`).
- **ADR-005** w17 §24 — w17 ships this renderer **and** NW-73's whole-tenant
  re-render in the same wave, and `infra/modules/storage` has **no
  `management_policy`, no lifecycle rule, no `delete_retention` and no
  versioning**. A non-deterministic key would make one bulk reprocess a
  **permanent doubling** of the container, reclaimable only by hand.
- **ADR-017** w17 footer — no AI-gateway change; `prebuilt-layout` stays
  uncalled.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | rasterised-pages | L | phase-1 |

## Council decisions carried into this story

- **The renderer package is declared, not discovered**: the task names the
  package, its **licence** and its **Linux native-dependency list** in its
  Definition of done, *before* it is implemented (OQ-w17-sa-04). Splitting the
  renderer from its image layer across two tasks is **forbidden either way** —
  the second task would be the one that discovers the first is broken.
- **This task writes no contract.** `?page=n` and `pageCount` reach
  `web/openapi/raffa-api.v1.json` through `E22/F01/US01/T01` in phase 2, which
  is that file's single writer for that phase.
- Where `document.page_count` is **null**, the viewer shows page 1 and **says
  the count is unknown** — never a guessed total (that half is NW-63's).

## Open questions

- **none blocking.** OQ-w17-sa-04 is ruled as a **binding DoD rule** rather than
  a package name. OQ-w17-ca-02's memory contingency (split `worker_cpu` /
  `worker_memory` and raise the **Worker only** to 0.5 vCPU / 1.0 GiB) is
  **pre-authorised with a named ceiling** and needs no new council round — but
  it is an **operator/infra act**, not this task's, and AC-8 is the measurement
  that would trigger it.
