# ADR-029 — document page rendering and the preview contract

- **Status**: accepted
- **Date**: 2026-09-15
- **Deciders**: software-architect (owner) + cloud-architect (Worker image and its native layer) + client-architect (viewer consumption, generated client) + ux-ui-designer (viewer surface) + product-owner (the NW-63 split, ADR-001 w17 clause 7)
- **Locked citations**: ADR-002 (module boundaries, hosts vs modules); ADR-009 (tenant storage prefix, `EnsureWithinTenant`); ADR-017 (`prebuilt-read`, page map from spans; `prebuilt-layout` not called in V1); ADR-027 (the Worker owns every heavy per-document step); ADR-012 (generated client); ADR-001 §1.2 (non-goals)
- **Items**: **NW-26** (preview is a placeholder PNG), **NW-63** (document viewer, highlighted + editable OCR — server half)
- **Wave**: w17 (`reports/architecture/waves/w17.md`), baseline `d3d2d24`

## Context and problem statement

`GET /api/documents/{id}/preview` returns a **placeholder PNG**. The port is
already correct — `IDocumentPreviewRenderer.cs:19`, one implementation, one
`TryAdd` registration (`ServiceCollectionExtensions.cs:127`) and an honest
fallback (`DocumentPreviewService.cs:53-54`) — so this ADR does not invent a
seam, it fills one.

Two items depend on the filling. **NW-26** wants the real page. **NW-63** wants a
viewer that overlays extraction evidence on that page, and its remainder was
split at this table (OQ-w17-001): a `must` reduced on the record, not silently.
Both raise the same three questions — *where does rasterisation run*, *where do
pages live*, and *what does the contract expose* — and answering them in two
separate amendments would produce two half-answers.

The decision is genuinely new. ADR-017 chose an **OCR model**; it says nothing
about producing images. ADR-027 owns the async pipeline but not what a page is.
Nothing on disk defines a per-page storage key: `DocumentStoragePath.BuildPreview`
hardcodes `page-1.png` (`:20-21`).

## Decision drivers

- A rasteriser is **unbounded CPU per request**; the API request path is the one
  place it must never run (ADR-027's whole premise).
- A human looking at a **failed** extraction needs the page *most* — so the
  preview must not depend on extraction succeeding.
- Tenant isolation is a storage-path property here, not an RLS property
  (ADR-009): a page key that escapes the tenant prefix is a cross-tenant read.
- The viewer must degrade, never break: a missing page is a stated absence.
- A native rasteriser changes the **Worker image**, which this seat does not own.

## Considered options

1. **Rasterise on demand inside `GET .../preview`** — simplest to write; puts
   unbounded CPU on the API request path and re-renders on every page turn.
2. **Rasterise in the Worker at pipeline time, persist per-page artifacts** —
   one render per document, served as a blob read.
3. **Client-side rendering (PDF.js-class library in the SPA)** — no server work;
   breaks ADR-012's dependency fence, ships the raw document to the browser, and
   makes the overlay the client's problem twice over.

## Decision outcome

**Chosen: Option 2 — rasterise in the Worker at pipeline time**, because the
Worker already owns every heavy per-document step (ADR-027) and a preview that is
rendered once and read as a blob is the only shape that survives a 40-page
document being paged through by a reviewer.

### The seven clauses

1. **Rasterisation is a Worker pipeline stage, never an API request-path
   operation.** A synchronous rasterise inside `GET /api/documents/{id}/preview`
   is forbidden by this ADR.
2. **The stage runs after admission and is independent of extraction success.**
   A document whose extraction failed must still be viewable — that is precisely
   the document a human needs to look at, and it is what makes NW-73's bulk
   reprocess reviewable. Rasterisation failing does not fail the document.
3. **A real renderer registers ahead of the placeholder**, which stays as the
   honest fallback (`DocumentPreviewService.cs:53-54`). The port and the `TryAdd`
   do not change.
4. **Per-page storage.** `DocumentStoragePath` gains
   `BuildPreviewPage(tenantId, documentId, page)` under the **same tenant
   prefix** and the **same `EnsureWithinTenant` guard** (`:28-38`, ADR-009).
   `BuildPreview` **stays page 1** so nothing that exists today breaks.
5. **The route gains an optional, 1-based `?page=n`**, bounded by the
   **already-persisted** `document.page_count`
   (`documents-contracts.sql:834` — verified on `d3d2d24`). Out of range is a
   **404**, never a silent fallback to page 1: silently serving the wrong page to
   a citation deep-link is how a viewer lies about where a clause came from.
6. **`pageCount` is exposed on the document read model.** Without it the client
   cannot page at all and the viewer renders exactly one page — the failure mode
   that would strand NW-63's own acceptance. This is the endpoint-one-phase-early
   dependency client-architect asked for.
7. **The page budget is a stated cap, never a silent truncation.** Rasterisation
   mirrors the existing per-document page budget (`AiGateway:Ocr:MaxPagesPerDocument`,
   ADR-017 `:107-108`); pages beyond it are **not rendered and the viewer says
   so**. A viewer that stops at page N with no explanation reads as a broken
   document.

### The NW-63 split, recorded so W18 does not re-litigate it

**w17's server half is clauses 1–7 and nothing more.** The viewer anchors on the
**existing** `SourcePage` + `SourceSpan` (`ExtractionEvidence.cs:46-47`): no AI
gateway change, no migration, no new write endpoint. The warrant is in ADR-017's
own amendment — `DocumentIntelligencePage` carries `Spans` as utf16 offsets into
the concatenated `content` (`DocumentIntelligenceContracts.cs:15-26,28-33`), so
**page-level anchoring is derivable today**. Bounding boxes are the only missing
piece, and only boxes.

**Deferred to W18, shape pre-decided**: (a) `prebuilt-layout` plus a widened wire
contract (`words` / `polygon`) and a widened `AiOcrPage` (`:13` carries page and
text only); (b) geometry columns on the evidence tables (zero
`bbox|bounding|polygon` in any `*.sql` today — ADR-003 w17 clause 2 refuses them
this wave); (c) the phrase-edit write path.

**No phrase-edit endpoint this wave.** There is no `MapPatch`/`MapPut` on
documents at all (`DocumentsEndpointExtensions.cs:90-96`) and the existing
correction path is field-name → scalar. Editing an OCR *phrase* is a different
provenance question — does it rewrite the extracted text, the evidence, or both?
— and improvising it blurs the proposal-vs-override distinction that
`ExtractionEvidence.cs:14-19` exists to keep. It is its own decision, in W18.

### The renderer implementation is declared, not discovered

This ADR fixes the **seam and the behaviour**; it does not name a package,
because naming one without verifying its licence and its Linux native-asset set
would be a claim this council cannot back. The binding rule instead:

- The NW-26 task **names the renderer package, its licence, and its Linux native
  dependency list (possibly empty) in its Definition of Done**, before it is
  implemented.
- If that list is **non-empty**, cloud-architect's image clause fires in the
  **same task**: the `RUN` layer goes **above `USER $APP_UID`**
  (`Raffa.Worker/Dockerfile:34`) and **above `COPY --from=build`** (`:31`) so it
  lands in a layer shared by every `:<sha>` tag. The Worker base is
  `mcr.microsoft.com/dotnet/runtime:10.0` (`:29`) — slim Debian, no fontconfig,
  no freetype. A missing native layer fails at **runtime** with `Unable to load
  shared library`, not at build.
- If the list is **empty** (a managed-only rasteriser), **no Dockerfile change is
  owed at all** and NW-26's image cost is zero.

Splitting the renderer from its image layer across two tasks is forbidden either
way: the second task would be the one that discovers the first is broken.

### Consequences

- **Good**: one render per document instead of one per page view; the preview
  survives a failed extraction; the citation deep-link cannot land on the wrong
  page; ADR-012's dependency fence is **not** breached, because the client
  renders PNGs and the viewer overlay is absolutely-positioned DOM over an
  `<img>` — NW-63 is **not** the first new runtime dependency, contrary to how
  OQ-w17-001 scoped it.
- **Bad**: storage grows per page per document; a re-render is needed if the
  rasteriser or its settings ever change; the Worker image may gain a native
  layer, which is a real per-build cost even when shared.
- **Neutral**: the placeholder renderer stays in the tree as the fallback, so the
  fixture path and CI remain provider-free.

## Implications for the decomposition

- NW-26 lands **before** NW-63 (wave-record ordering; the viewer overlays a real
  page). NW-63's client work must not start against the placeholder.
- Files: the renderer implementation, `DocumentStoragePath`,
  `DocumentsEndpointExtensions.cs:443-472`, a Worker pipeline stage, the Worker
  `.csproj`, and — conditionally — `Raffa.Worker/Dockerfile`.
- **No migration**: `page_count` already exists (`documents-contracts.sql:834`).
  This is what lets ADR-021 w17 clause 1 give `documents-contracts.sql` a single
  writer this wave.
- Contract delta: `?page=n` on the preview route and `pageCount` on the document
  read model, then a regenerated client.
- `GET .../preview` returns a **blob under an authenticated fetch**
  (`client.ts:2082-2113`); the client contract for revoking object URLs is
  client-architect's (ADR-012), and the viewer is its first consumer.

## Assumptions

- A per-page PNG is sufficient for the overlay; no vector layer is needed in V1.
- `document.page_count` is populated for every document that reaches the viewer;
  where it is null the viewer shows page 1 only and says the page count is
  unknown, rather than guessing. *(Recorded as OQ-w17-sa-04.)*
- The rasteriser can be driven from the stored blob without re-fetching the
  original upload through any provider API.

`waves/w17.md` records this under NW-26 and NW-63.

## Amendment (2026-09-15, wave w17 round 3 — the page key is deterministic, and a shorter re-render reaps its surplus)

Serves **NW-26** (and NW-63, which reads the same pages). Nothing above is
rewritten; the decision, the options and the consequences stand. This footer adds
the **key rule** the body left implicit — written because **two seats reached it
independently after this seat last spoke, and neither could write it**:
cloud-architect (ADR-005 w17 §24) and security-architect (ADR-009 w17 clause 7),
both of whom explicitly declined to edit this ADR because it is this seat's.

**1. The per-page key is deterministic and replaced in place — no suffix.**
`BuildPreviewPage(tenantId, documentId, page)` (`:71`) resolves to
`{tenantPrefix}documents/{documentId}/preview/page-{n}.png`, 1-based, derived
from **(tenantId, documentId, page) and nothing else**: no render id, no
timestamp, no content hash, no attempt counter. Two renders of the same page of
the same document produce the **same key**, and the second **overwrites** the
first.

- **The rule is already written in the file the method is added to**, which is
  why this is a ratification rather than a new invention:
  `DocumentStoragePath.cs:16-18` states a preview is "a derived rendering,
  **replaced in place** whenever the document is reprocessed, and must never be
  served as if it were the document". `BuildPreview` (`:20-21`) obeys it with a
  fixed `page-1.png`; `BuildPreviewPage` inherits it verbatim.
- **Why it could not be left implicit.** w17 ships a page renderer **and** a bulk
  whole-tenant re-render trigger (NW-73) **in the same wave** — the one
  combination that turns a key-scheme choice into a permanent cost. Cloud
  verified that `infra/modules/storage` has no `management_policy`, no lifecycle
  rule, no `delete_retention` and no versioning, so nothing downstream would
  reclaim a suffixed key: one bulk reprocess would **permanently double** the
  container. Security reached the same rule from the other side — a suffixed key
  leaves a prior rendering of a **since-corrected** document readable where
  nothing reaps it. **Cost and correctness land on one rule**, which is the
  strongest evidence it belongs in the key scheme and not in a runbook.
- **Corrects this ADR's own `:142`.** "Storage grows per page per document" is
  right per document and wrong per render: under this clause it grows **once**,
  not on every reprocess. The *Bad* consequence stands in size; its growth rate
  does not.

**2. A re-render that yields fewer pages deletes the surplus.** Determinism
overwrites pages 1…N but **reaps nothing beyond N**.

- **The gap is reachable, not hypothetical**: `BuildPreview` and
  `BuildPreviewPage` carry **no version segment**, while `Build` takes an
  explicit `versionNumber` (`DocumentStoragePath.cs:40`) — so one `documentId`
  holds successive document **versions**, and a v2 with fewer pages leaves v1's
  surplus pages at exactly the keys this route serves. The route's `page_count`
  bound (404 out of range) hides them from the API but leaves them **in the
  container**, which is precisely security's "prior rendering of a
  since-corrected document that nothing reaps".
- **Rule**: the render stage deletes `page-{n}.png` for `n > pageCount` for that
  document as part of the same stage that writes pages 1…N. Precedent is the
  product's own path — `DocumentReprocessService` already deletes derived chunks
  before re-deriving (`:99-101`); this is that shape applied to pages.
- **It is NW-26's own task**, not a follow-up: the body already forbids splitting
  the renderer from its image layer because "the second task would be the one
  that discovers the first is broken" (`:131-132`), and the same reasoning binds
  the reap.

**Scope.** No migration, no contract change, no new file beyond the body's list
(`:152-154`): `DocumentStoragePath` gains the method **and** the docstring rule,
and NW-26's DoD gains both clauses as the task's own words. **ADR-005 and ADR-009
are not edited** — they are cloud-architect's and security-architect's, and both
findings are adopted here rather than argued back.

`waves/w17.md` records this under NW-26 and NW-63.

## Amendment (2026-09-16, wave w18 — the phrase-edit provenance, and what the box overlay owes the screen)

Serves **NW-63r** (the W18 remainder). Nothing above is rewritten; the seven
clauses, the deterministic key and the reap all stand. This footer answers the
one question the body deliberately left open at `:130-133` ("its own decision, in
W18") — **OQ-w18-002** — and states the screen-level promise the box overlay must
keep. The mechanism (endpoint, columns, gateway model) is software-architect's,
cloud-architect's and client-architect's; this seat owns **what the product
promises**, which is exactly the provenance and the screen wording.

**1. A phrase edit is an override, never an in-place rewrite.** Editing an OCR
phrase writes an **override beside the proposal** — both persisted, the proposal
untouched — and the screen reads the override when one exists. This is the
provenance distinction `ExtractionEvidence.cs:14-19` exists to keep, restated as
the product rule: **the record must always be able to answer "what did the model
propose, and what did a human change", for every phrase.** An in-place rewrite
blurs that, which is Appendix C's "never destructively overwrite … human
corrections" inverted into a promise about the model's own words. A correction
that a second browser reads back as the override — and that a reprocess cannot
silently revert (ADR-027's re-derivation-never-overrides-a-human) — is the
acceptance.

**2. The box overlay is the screen promise this wave adds, and it must not be
made before it is real.** The w17 viewer shipped text-level highlighting with
copy reading *"Page N — the wording is highlighted below"* (fenced in
`w17-hitl.md` §2). W18 draws the **box on the page**; the copy that promised a
box in w17 stays fenced **until the box lands**, then the overlay and its wording
ship in the same task — never copy first, box later, which is the one failure
mode the split makes invisible to a user.

**3. Acceptance wording (N17r), this seat's:** open a cited field on a validated
Northwind PDF → the viewer draws a box on the page **and** shows the text; edit an
OCR phrase → it persists (reload **and** a second browser agree) **and** the
proposal is still distinguishable from the override.

**4. What this footer does not own.** The geometry columns (ADR-003), the widened
wire and `AiOcrPage` (`ADR-017`), the endpoint and its single-writer migration
(software-architect), and the overlay/affordance (client-architect /
ux-ui-designer). This footer rules only the provenance and the screen promise.

`waves/w18.md` records this under NW-63r.
