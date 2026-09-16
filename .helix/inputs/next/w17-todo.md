# Raffa — next-waves input · W17

Status: **binding input** for the W17 requirements document. Written 2026-09-15
after the operator confirmed wave **w16 closed** (merged to `origin/main`) and
confirmed the scope: carry-over residuals + the recorded W17 queue, including
the bulk-reprocess console and the two `could` filters. IDs continue from
`next-waves-todo.md` / `w16-todo.md` / `helix/w17-input`; carried-over items
keep their id; the four W16-table residuals that had no `NW-*` id are
**NW-72…NW-75**.

| | |
|---|---|
| Compared | `origin/main` @ `d3d2d24` (2026-09-15, merge of PR #129 `integration`) — w16 closed by that PR; acceptance `docs/waves/w16-acceptance.md` is on `main` |
| Previous wave | w16 "nothing the product knows lives only in a browser tab" — `reports/context/waves/w16-requirements.md`, `reports/architecture/waves/w16.md`, ADR-028, epics 18–19 |
| Product oracles | `inputs/product-spec.md`, `inputs/requirements.md`, `inputs/percorso-pilota-v1.md` |
| Architecture | ADR-001 (V1 scope), ADR-012 (web), ADR-016 (promotion; w16 clause 31 bulk console), ADR-017 (extraction / evidence), ADR-019 / ADR-020 (design system / screen inventory), ADR-024 (Ask V2), ADR-027 (async processing), ADR-028 (server-side state) |
| Design | `inputs/design/prototypes/raffa-v2/screens-v2.md` §4 Review, §5 Contract 360, §6 Portfolio, §8 Savings |

## 0. Binding instructions (unchanged from w15 §0; stakeholder 2026-09-13)

1. **Every wave the previous intake queued is executed, in order, in full.**
   This run **is W17**. W18 keeps the queue in `w16-requirements.md` §5
   (restated in §5 below). Nothing is dropped and nothing is pushed to a
   later wave than the one it is already queued for; an item that does not
   fit this wave's cap becomes the **head** of W18, never its tail.
2. **An item this file marks `must` or "no longer deferred" is never queued
   beyond the next wave** and is never demoted without a written reason.
   The three `must` items of this wave are **NW-71, NW-62, NW-63**.
3. **Carry-over first.** The four residuals the w16 table ruled into W17
   (BACKLOG.md w16 "Queued for the next wave", `w16-hitl.md` §5,
   `docs/waves/w16-acceptance.md` known-gap #1) are the **head** of this
   wave. The W17 intake picks them up without re-auditing the *intent*; it
   **does** re-audit *status today* against this checkout
   (`origin/main` @ `d3d2d24`).
4. **NW-71 is `must` and joins the review-screen group (NW-63 / NW-64 /
   NW-65 / NW-66).** It is the rule those four items render; decide it
   once, at the council, before any of them is decomposed. Ingested from
   `helix/w17-input` `w17-todo.md` (stakeholder 2026-09-13); that branch
   is otherwise out of this run.
5. **Do not skip ahead to W18.** Ask citation cards, Contract 360 "Ask
   about it", Quote-check product UX, Ask abstain recovery, the global Ask
   bar, OpenAPI residuals, Foundry ops and `day1.spec.ts` stay W18.

## 1. Head of W17 — residuals the w16 table ruled in

None of these four was a live w16 task. They are not decomposed. Assign
stable `NW-*` ids here so the intake does not mint `W17-NN` duplicates on
a re-run.

### NW-72 — Savings KPI realized amount (OQ-w16-po-01)

- **Status:** OPEN — **should**. Head of this wave (ADR-001 w16 clause 4 /
  OQ-w16-po-01). Unmet AC of the still-`active` story
  `E04/F03/US01` AC-1, **not** a new capability and not an ADR-001 scope
  change. w16 closed A16-8 on the **status** move and fenced every w16
  task from rendering `SavingsKpiSummary.Realized` as money.
- **Today:** `SavingsKpiCalculator` buckets `Realized` from the
  opportunity's **own estimated range**
  (`backend/src/Raffa.Savings/Application/SavingsKpiCalculator.cs:46-54`)
  — the calculator's own comment names `Domain.RealizedSavings`
  (E04/F02/US02/T02) as the missing verified-value record.
  `web/src/routes/savings/savingsViewModel.ts:92,110` renders the
  realized KPI as `countOf(kpis.savingsRealized)` in a meta line beside
  identified / in-progress. No surface shows a realized **amount**.
- **Must:** after a negotiation outcome that moved an opportunity to
  `Realized` (w16 NW-21), Savings shows the **realized money** (the
  verified amount, not the pre-negotiation estimate) in the KPI band,
  grouped by currency, with provenance. A second browser agrees. An
  unlinked outcome (`savingsPropagated: null`) still must not enter any
  total (ADR-028 §D5 fence 2, already structural).
- **Acceptance:** A17-S1 — record an outcome that realizes an opportunity
  → Savings KPI shows a money figure for realized, not only a count;
  reload / second browser agrees.
- **Seats (hint):** product-owner (AC wording; this is the unmet AC, not
  a new promise), software-architect (which amount is "verified"),
  client-architect (KPI cell), ux-ui-designer (the cell currently has no
  money slot — screens-v2 §8 KPI triple).

### NW-73 — Bulk whole-tenant reprocess (ADR-016 w16 clause 31)

- **Status:** OPEN — **should**. Shape **already designed** at the w16
  table so W17 does not re-derive it. Recorded as known-gap #1 in
  `docs/waves/w16-acceptance.md`: w16 deleted
  `reprocess-tenant-documents.yml` and added `verify-tenant-corpus.yml`,
  which reports and does not mutate. A `demo` walker must not read the
  missing bulk job as a regression.
- **Today:** the in-product path is one document at a time
  (`POST /api/documents/{id}/reprocess` → 202 → Worker). The
  verification workflow enumerates `processing_status`; it does not
  enqueue. No operator console exists.
- **Must (shape from ADR-016 w16 clause 31, binding unless the council
  writes a better one):** an operator console (`backend/scripts` or a
  `Raffa.Tools` project) referencing `Raffa.Documents.Contracts` +
  `Raffa.Messaging` and calling `DocumentReprocessService.ReprocessAsync`
  per document — so the `extraction_job` row, the `document.reprocessed`
  audit row and ADR-027 §D5's replace step stay **the product's own
  code, never re-implemented in bash**. Postgres from the Key Vault
  secret CI already reads. Publish under `DefaultAzureCredential` with
  **one** topic-scoped `Azure Service Bus Data Sender` for
  `raffa-sp-<env>`.
- **Owed at the table (not pre-decided):** security-architect — may a CI
  principal hold a send right at all (inclination recorded: *yes — Send
  only, topic-scoped, never `Manage`, never a SAS key*) and what actor
  the console writes (NW-32 / ADR-011 w16 clause 16, `system:<component>`);
  cloud-architect — the module `azurerm_role_assignment`. Three shortcuts
  remain refused: Service Bus SAS key, any `X-*` identity header, inserting
  `extraction_job` rows via psql (nothing sweeps them).
- **Acceptance:** A17-S2 — operator runs the console against a `dev`
  tenant with N documents; each is re-enqueued through the product path
  and reaches a terminal state; `verify-tenant-corpus.yml` still reports
  `%PDF` gone. A16-3's in-product single-document path stays green.
- **Seats (hint):** delivery-manager (owns the console and CI-YAML set),
  software-architect, security-architect, cloud-architect.

### NW-74 — Hide admin-gated Ask chips from a non-Admin (OQ-w16-sa-02)

- **Status:** OPEN — **should**. Presentation, **never a security fix**
  (ADR-022 w16 S16-11; ADR-012 w16 clause; ADR-024 w16 clause 2). The
  catalog is served whole to an unauthenticated caller; every action
  behind an admin-gated entry is already enforced server-side by
  membership.
- **Today:** `roleGate` is on the wire
  (`CapabilitiesEndpointExtensions.cs` / generated `schema.ts`) and
  **nothing in `web/src` reads it** (grep: generated type only). A
  non-Admin can see an admin-gated suggestion chip.
- **Must:** the Ask / capability UI hides entries whose `roleGate` is
  `admin` when the signed-in membership is not Admin. The API still
  returns the full catalog. Do not re-introduce a client-asserted role
  and do not membership-gate `GET /api/capabilities` (w16 took the
  named-acceptable alternative because that route is tenant-free).
- **Acceptance:** A17-S3 — Procurement member does not see admin-gated
  chips; Admin does; `GET /api/capabilities` is identical for both
  (and for no token).
- **Seats (hint):** client-architect, ux-ui-designer (chip copy/visibility);
  security-architect only to confirm it is still not a control.

### NW-75 — Designed Savings section for tracked renewal actions (OQ-w16-ca-01)

- **Status:** OPEN — **could**. Product half of OQ-w16-ca-01. w16 retired
  `buildTrackedOpportunityRow` so Savings no longer floods with
  estimate-less pseudo-rows of every tracked action. Whether Savings
  should show tracked renewal actions **at all** is product-owner's; if
  yes, it is a **designed section** of that screen, which seats
  ux-ui-designer.
- **Today:** Savings renders only real `SavingsOpportunity` rows
  (`savingsViewModel.ts`). Renewal actions are on Renewals and Contract
  360 via the embedded `savedAction` (NW-11).
- **Must (if IN):** a designed Savings section for tracked actions — not
  an estimate-less pseudo-opportunity row. If OUT, record the ruling and
  do not mint a task.
- **Acceptance:** A17-S4 — only if the council takes it IN; otherwise
  skip. A `could` that does not fit the cap overflows to the head of
  W18's `could` tier and must not displace a queued W18 item.
- **Seats (hint):** product-owner (IN/OUT), ux-ui-designer (only if IN),
  client-architect (only if IN).

## 2. Rest of W17 — domain completeness (Contract 360)

From `w14-requirements.md` §5 / `w16-requirements.md` §5, after the
overflow head. Persistence rule (unchanged): **if a human can create or
change it, another browser / device / session must read it back from
Postgres (RLS).** Browser storage is not a system of record.

Theme: empty tabs / honest-but-useless gaps on Contract 360 become
answers, a viewer, and officialized facts. Success on `dev`: N16–N20
plus A17-1…A17-4 (NW-71).

### NW-20 — Contract 360 `benchmark` and `activity` are always `[]`

- **Status:** OPEN — **should**. Feeds NW-62.
- **Today:** `ContractsEndpointExtensions.cs:353,362` emits
  `benchmark = Array.Empty<object>()` and
  `activity = Array.Empty<object>()` unconditionally.
- **Must:** the 360 payload carries the market / similar-contract
  benchmark and the activity that NW-62's answers band needs, with
  provenance; first-of-type is an honest seed, not a silent empty array.
- **Acceptance:** N16 (with NW-22 / NW-62).
- **Seats (hint):** software-architect, product-owner (what "activity"
  means for V1).

### NW-22 — Renewal insight `MarketPosition` stays null on real contracts

- **Status:** OPEN — **should**. Feeds NW-62.
- **Today:** `RenewalPipelineBuilder.cs:92` hardcodes
  `MarketPosition: null` (with `AnnualUpliftPercent` and
  `PotentialSavingsRange`) for every item.
  `contract360ViewModel.ts` `buildAnswers` therefore shows
  `SAVINGS_NOT_YET_AVAILABLE` / `LEVER_NOT_YET_AVAILABLE`.
- **Must:** a real contract with spend/fees and a market (or honest
  first-of-type) band gets a market position; 360 and Renewals agree.
- **Acceptance:** N16.
- **Seats (hint):** software-architect, product-owner.

### NW-23 — Portfolio has no `category` filter

- **Status:** OPEN — **could**.
- **Today:** `PortfolioFilter.cs:11-18` has no `Category`;
  `PortfolioEndpointExtensions` parses supplier/status/risk/autoRenewal/
  spend/renewal dates only. The filter's own comment defers Category to
  a follow-up once Suppliers/Products defines it.
- **Must:** filter the portfolio by category, or the council records why
  V1 still cannot (and this item overflows rather than shipping a fake
  enum). screens-v2 §6 table is Supplier · contract · Type · Annual
  spend · Ends · Notice by · Risk — **Type** is the product word if
  Category is not a domain concept yet.
- **Acceptance:** operator can restrict the portfolio list by type /
  category on `dev`.
- **Seats (hint):** software-architect, product-owner (is Type enough?),
  client-architect, ux-ui-designer (control).

### NW-25 — Savings list has no filters

- **Status:** OPEN — **could**.
- **Today:** `web/src/routes/savings/index.tsx` renders header + KPI band
  + a flat `OpportunitiesTable`; no filter, search or sort control
  exists in that folder.
- **Must:** the operator can restrict the opportunities list (supplier /
  status / currency — council picks the set). Do not invent a category
  the domain does not have.
- **Acceptance:** Savings list is filterable on `dev`.
- **Seats (hint):** client-architect, ux-ui-designer, product-owner.

### NW-26 — Preview / evidence quality residuals

- **Status:** OPEN — **should**. Distinct from Ask citation UX (NW-55,
  W18). The full page viewer + editable OCR highlights is NW-63; this
  row is the **real page render vs placeholder PNG**.
- **Today:** `PlaceholderDocumentPreviewRenderer` is the only
  `IDocumentPreviewRenderer` (`ServiceCollectionExtensions.cs:125-127`);
  `DocumentPreviewService.cs:54` falls back to `RenderPlaceholder("FILE")`.
  `GET /api/documents/{id}/preview` is a first-page PNG.
- **Must:** preview of the uploaded file's pages (not the placeholder)
  so NW-63 has something to overlay. Bounding boxes / per-page original
  are NW-63's.
- **Acceptance:** N17 (with NW-63) — opening preview on a validated
  Northwind PDF shows that file's page, not a FILE placeholder.
- **Seats (hint):** software-architect, cloud-architect only if a new
  renderer / SKU is required (Document Intelligence already in the
  wave's Foundry path).

### NW-62 — Contract 360 must answer "where you can save" and "when you must move"

- **Status:** OPEN — **must**. Job-to-be-done of 360.
- **Today:** `buildAnswers` (`contract360ViewModel.ts:150-175`) reads
  `insightCard.recommendations.potentialSavingsRange` / `marketPosition`
  (null — NW-20/NW-22) and `header.cancellationDeadline` / `endDate`
  (null even when the clause text already has 36-month term, 90-day
  notice, auto-renewal, EUR 48k). `AnswersBand` prints
  `SAVINGS_NOT_YET_AVAILABLE` / `LEVER_NOT_YET_AVAILABLE`. "Why" is an
  extraction dump (`WhyClauses.tsx`), not proof of the two answers.
- **Must:**
  1. **Where you can save:** a real figure or band from market / similar
     contracts (RAG + this tenant's corpus) vs this contract's
     spend/fees, with provenance. First-of-type: honest seed, then
     update when peers land (same idea as NW-57, which stays W18).
  2. **When you must move:** notice deadline / term end **from the
     document**. If a structured field is missing, recover it from RAG
     on that contract — do not show "Not determined" while the clause
     is on the same page.
  3. Clauses under the band are **evidence for those two answers**, not
     a raw field dump. Token salad stays in Review.
  4. "What to do" follows from (1) and (2). Start negotiation only when
     there is a dated move and a save target.
- **Related, do not merge:** NW-20, NW-22 (wires), NW-61 (don't open 360
  on half-ready docs — closed in w15), NW-57 (quote market compare,
  W18).
- **Acceptance:** N16.
- **Design:** `screens-v2.md` §5 Answers band.
- **Seats (hint):** product-owner, software-architect, client-architect,
  ux-ui-designer.

### NW-63 — Document viewer: uploaded pages with OCR phrases highlighted and editable

- **Status:** OPEN — **must**.
- **Today:** no document viewer in `web/src` (no pdfjs / canvas /
  react-pdf iframe). `getDocumentPreviewUrl` has zero component callers.
  Highlighting is text-level `<mark>` (`ClauseHighlight.tsx`,
  `EvidencePane.tsx`). Persisted model keeps only `SourceSpan` (string)
  + `SourcePage` (int) — `ExtractionEvidence.cs:46-48` — no bounding
  boxes.
- **Must:**
  1. From 360 (and Review): a control opens a **document viewer** (the
     real uploaded file, page by page — not a placeholder PNG).
  2. OCR / extraction spans are **already highlighted** on those pages.
     Clicking a 360 clause or a Review field jumps to that page and
     highlight.
  3. From the highlight, the user can **edit the recovered text** (and
     the structured field it feeds). Save goes through the existing
     correction write (`correctContract` / review), not a silent local
     tweak.
  4. Viewer is read-only chrome around the file; edits are on the
     extracted phrases, with provenance kept.
- **Related:** NW-26 (real page render), NW-55 (Ask citation deep-link
  — W18, do not pull it in), NW-62 (viewer is where save/move proof
  lives), NW-71 (auto-accepted phrases are officialized and still
  correctable here).
- **Acceptance:** N17.
- **Design:** `screens-v2.md` §5 Why / citation landing.
- **Seats (hint):** client-architect, ux-ui-designer, software-architect
  (boxes / preview API).

### NW-64 — Fields OCR did not recover still appear in a section the user can fill

- **Status:** OPEN — **should**.
- **Today:** `reviewViewModel.ts:245` `continue`s when both the contract
  value and the evidence proposal are empty, so end date / notice vanish
  from Review. 360 `computeNeedsAttention` ignores `confidence: null`.
  "What to do" complains about missing `endDate` but offers Start
  negotiation, not a form.
- **Must:**
  1. Canonical fields with **no OCR span / no extracted value** show in
     a dedicated **"Not found in the document"** (or equivalent) section
     — next to the viewer (NW-63), not buried in Details.
  2. Each row is an **empty input** the user can complete; save is the
     same correction write as Review.
  3. Do **not** invent a value. Do **not** hide the hole. Recovered
     phrases stay on the page highlights (NW-63); unrecovered stay here.
- **Related:** NW-62 (recover first; if still empty, this section),
  NW-71 (auto-accept does not apply to a missing field).
- **Acceptance:** N18.
- **Design:** `screens-v2.md` §4 Review.
- **Seats (hint):** ux-ui-designer, client-architect, product-owner
  (required vs optional).

### NW-65 — Details: only officialized facts; drop the "still need to decide" list

- **Status:** OPEN — **should**. ADR-019 w14 footer §6 deferred the ≥90 %
  band reconciliation to this item; **NW-71 now owns the threshold**.
  This item owns the Details layout.
- **Today:** `DetailsSection.tsx` two-column grid: Key terms (all header
  fields, including "—") vs Documents + `computeNeedsAttention` (every
  product/clause/obligation **not** in the >95 % band) + "Review all →".
  Flagged/Review rows duplicate Review and the Why list.
  `NO_ATTENTION_MESSAGE` still says "above 95 %" while the (never
  committed) stash used ≥90 %.
- **Must:**
  1. Details (and Key terms / Products / Obligations / Risks on this
     page) show **only officialized** values: auto-accepted (NW-71) or
     human Accept / Correct. No Flagged / Review · N% here.
  2. **Delete** the "Facts you still need to decide" block. Keep
     **Review all →** as the only path to pending fields.
  3. No sparse two-column hole. Unrecovered empties belong in NW-64.
- **Acceptance:** N19.
- **Design:** `screens-v2.md` §5 Details ▾.
- **Seats (hint):** ux-ui-designer, client-architect.

### NW-66 — Why-clauses: no original quote on the row; click = specchietto; viewer link; leverage, not confidence

- **Status:** OPEN — **should**.
- **Today:** `WhyClauses.tsx:16-19,43-64` row = type · normalized ·
  `source` (page + span, often the long quote) · raw `riskLevel` ·
  `getConfidenceTag` (Flagged / Accepted · %). Click already selects and
  shows `ClauseHighlight`. No viewer link.
- **Must:**
  1. Row: type + officialized value only. **No** original wording / page
     quote in the right column.
  2. Click → specchietto with original text (keep `ClauseHighlight`).
  3. Every row (and the specchietto) has **Open in document viewer** to
     that page/span (NW-63).
  4. **No confidence %** on this list.
  5. Replace bare Medium/Low/High with a **plain-language negotiation
     tag** (Leverage / Weak for you / Protect / Watch) plus a one-line
     why and a short legend — never colour-only (ADR-019).
  6. Only officialized clauses here (same gate as NW-65). Pending stay
     in Review.
- **Acceptance:** N20.
- **Design:** `screens-v2.md` §5 Why.
- **Seats (hint):** ux-ui-designer, client-architect, product-owner
  (the leverage vocabulary).

## 3. Review: automatic acceptance of high-confidence fields

Ingested from `helix/w17-input` `w17-todo.md`. Stakeholder ruling
2026-09-13.

### NW-71 — A field extracted with confidence ≥ 90 % is accepted automatically (server rule + web)

- **Status:** OPEN — **must**. The rule is **server + web**, and **90 %
  applies to every field, the five critical ones included** (value,
  cancellation, termination, renewal, uplift) — no stricter band, no
  "always review" list. This supersedes `inputs/product-spec.md:335`
  (">95 % automatically accept unless configured as always-review") and
  the pilot's "<80 % HITL, critical stricter"
  (`inputs/percorso-pilota-v1.md:46`), and it is the same HITL decision
  of 2026-09-10 that ADR-019's w14 footer §6 says the ADR body is stale
  against and deferred to NW-65. **NW-71 owns the threshold; NW-65 owns
  the Details layout that consumes it.**
- **Today:**
  - Web: `web/src/styles/semantics.ts:29-49` still implements the old
    spec bands `>95 % Accepted / 80–95 % Flagged / <80 % Review`.
    "Accepted" on the review screen is a **client-side, per-session**
    acknowledgement (`reviewViewModel.ts` `acceptedThisSession`) that
    becomes durable only when "Mark as validated" posts `acceptedFields`
    to `POST /api/documents/{id}/validate`.
  - Backend: **no auto-accept exists**. `DocumentQueryService.cs:33,42`
    holds the review bars (`WeakFactThreshold = 0.6`,
    `CriticalWeakFactThreshold = 0.8`) that only decide `needs_review`
    via `WeakFactCount`; there is no persisted per-field "accepted"
    state other than the validation's `acceptedFields` audit.
  - The stakeholder's 2026-09-10 web edits for the ≥90 % rule were
    **never committed** (operator stash on the main checkout). Seed the
    web half from there if the stash still exists; do not re-derive it.
- **Must:**
  - **Server rule, applied at extraction time** (and on reprocess):
    every extracted field whose evidence confidence is ≥ 0.90 is
    **officialized automatically** — persisted as accepted (who:
    `system:<component>`, why: `auto-accept ≥ 0.90`, when), with one
    audit row per document naming the auto-accepted fields (never the
    values); a field below 0.90 stays pending.
  - `needs_review` is decided **only** by pending (sub-threshold)
    fields: a document whose every extracted field is ≥ 0.90 goes
    straight to **Completed** with no human step; `WeakFactCount` and
    the Documents badge count only pending fields. The 0.6 / 0.8 bars
    are retired (one threshold, one definition, every caller).
  - **Web:** `semantics.ts` bands become `≥ 90 % Accepted automatically /
    < 90 % Review`; the review screen shows auto-accepted fields as
    **"Accepted automatically (NN %)"**, still **correctable**; "Mark as
    validated" sends only the fields the human touched; Details /
    Contract 360 show **only officialized facts** (NW-65).
  - Reprocess re-applies the rule but never overwrites a human-assigned
    value (ADR-003: corrections are versioned; the human wins).
  - OpenAPI: the review / evidence read exposes the per-field decision
    (`pending` / `auto_accepted` / `human`) and the threshold in force,
    so the web renders the **server's** fact, never its own computation.
- **Acceptance:**
  - A17-1 — upload a clean born-digital MSA: every field the extractor
    reports ≥ 0.90 lands **Accepted automatically**; if none is below,
    the document is **Completed** with no review, and Ask can use it at
    once.
  - A17-2 — a scan with one field at 0.85: the document is **Needs
    review** with exactly that field pending; accepting or correcting
    it completes the document.
  - A17-3 — correct an auto-accepted field: the correction wins, is
    audited, and survives a reprocess.
  - A17-4 — `curl` the evidence / review read: each field carries its
    decision and confidence; the badge count equals the number of
    `pending` fields.
- **Seats (hint):** product-owner (supersedes spec §7.3 / pilot HITL
  bands), software-architect, security-architect (audit actor
  `system:<extraction>`), client-architect, ux-ui-designer (ADR-019
  semantic rows).

## 4. Out of this wave

| ID | Why |
|---|---|
| NW-07, NW-08, NW-11, NW-12, NW-13, NW-21, NW-31, NW-32, W16-01 | **w16** — operator confirmed the wave terminated (PR #129); intake may record CLOSED-ON-MAIN / PARTIAL with evidence, but must **not** reopen them as live W17 tasks |
| NW-10 | CLOSED-ON-MAIN in w15; kept out |
| NW-30, NW-40, NW-41, NW-50, NW-55, NW-56, NW-57, NW-59, NW-60 | **W18** |
| NW-51 | design-alignment residual; not in the recorded W18 queue — do not pull it in |
| NW-52, NW-53, NW-54 | **DEFERRED** (paid market API / mobile beyond scaffold / extra roles in nav) |

## 5. Full remaining schedule (restated so nothing is dropped)

| Wave | Queue (head first) |
|---|---|
| **W17 (this run)** | NW-72, NW-73, NW-74, NW-75 (W16 residuals, head), then NW-20, NW-22, NW-23, NW-25, NW-26, NW-62, NW-63, NW-64, NW-65, NW-66, **NW-71** |
| **W18** | NW-30, NW-40, NW-41, NW-50, NW-55, NW-56, NW-57, NW-59, NW-60 |

**NW-75 is `could`.** If the council takes it OUT, it leaves the live
set and is recorded, not queued. If the cap binds, overflow order (never
a `must`): NW-75, then NW-23, then NW-25, then NW-74, then NW-73 — each
becomes the **head** of W18, never its tail, and never displaces a
queued W18 item.

## 6. Suggested grouping and seats

| Theme | Items | Seats |
|---|---|---|
| A · W16 residuals | NW-72, NW-73, NW-74, NW-75 | product-owner, software-architect, client-architect, ux-ui-designer; + delivery-manager, security-architect, cloud-architect on NW-73; security-architect confirms NW-74 is not a control |
| B · 360 answers (save / move) | NW-20, NW-22, NW-62 | product-owner, software-architect, client-architect, ux-ui-designer |
| C · viewer + OCR + missing fields + Details + Why | NW-26, NW-63, NW-64, NW-65, NW-66, **NW-71** | product-owner, software-architect, client-architect, ux-ui-designer; security-architect on NW-71's audit actor; cloud-architect only if NW-26 needs a renderer SKU |
| D · list filters | NW-23, NW-25 | product-owner, software-architect (NW-23), client-architect, ux-ui-designer |

Order: **A before C** where they share files (Savings KPI vs NW-25;
`semantics.ts` is NW-71 + NW-65 + NW-66). **NW-71 before NW-65 / NW-64 /
NW-66** (they render the officialized gate). **NW-26 before NW-63**
(viewer overlays a real page). **NW-20 / NW-22 before NW-62** (answers
consume those wires). Cap 20 tasks / 5 phases; overflow → **head of
W18** in the order in §5.

`web/openapi/raffa-api.v1.json` — one task owns it per phase (ADR-012
§3). Likely writers: NW-72 (if the KPI shape changes), NW-20/NW-22/NW-62
(360 / renewals payload), NW-26/NW-63 (preview / evidence), NW-71
(per-field decision), NW-23 (portfolio query). Combine contract edits
inside a theme rather than one task per item.

## 7. Acceptance seeds (on deployed `dev`)

| # | Check | Expected |
|---|---|---|
| A17-S1 (NW-72) | record an outcome that realizes an opportunity | Savings KPI shows realized **money**, not only a count; second browser agrees |
| A17-S2 (NW-73) | operator console against a `dev` tenant | N documents re-enqueued through the product path; each reaches a terminal state; verify-tenant-corpus still reports |
| A17-S3 (NW-74) | Procurement vs Admin on Ask chips | non-Admin does not see `roleGate=admin` chips; catalog GET unchanged |
| A17-S4 (NW-75) | only if IN | designed Savings section for tracked actions, not a pseudo-row |
| N16 (NW-20, NW-22, NW-62) | open a validated Northwind MSA | **Where you can save** and **When you must move** are concrete, with citations; no "Not yet available" / "Not determined" while those facts are in the file |
| N17 (NW-26, NW-63) | from that 360, open the document viewer | uploaded pages, OCR phrases highlighted; click a clause → that page/span; edit a highlight → correction persists |
| N18 (NW-64) | a contract missing `endDate` / notice | those fields in a **separate empty section** the user can fill; not omitted from Review |
| N19 (NW-65) | Details | only officialized key terms; **no** "Facts you still need to decide"; **Review all →** still there |
| N20 (NW-66) | Why-rows | type + officialized value; **no** original quote on the right; click → specchietto; **Open in document viewer**; tag is leverage, not Flagged · N% |
| A17-1…4 (NW-71) | clean MSA vs a 0.85 field vs a correction vs curl | auto-accept ≥ 0.90; Completed with no review when nothing is pending; human correction survives reprocess; badge = pending count |

Inherited, still true, not reopened: N6 (session is not SoT — w16), N15
(upload feels instant — w15), N3b (invite mail — w15, `dev` only).

## 8. Traceability

W16 residuals ← `reports/workitems/BACKLOG.md` w16 "Queued for the next
wave" · `reports/audit/w16-hitl.md` §5 · `docs/waves/w16-acceptance.md`
known-gap #1 · ADR-001 w16 clause 4 · ADR-016 w16 clause 31 · ADR-022
w16 S16-11 · OQ-w16-po-01 / OQ-w16-sa-02 / OQ-w16-ca-01.

Domain completeness ← `w14-requirements.md` §5 "W17 — Domain
completeness" · `w16-requirements.md` §5 · `inputs/next/next-waves-todo.md`
§6 NW-20…NW-26, NW-62…NW-66 · N16–N20.

NW-71 ← `helix/w17-input` `w17-todo.md` · stakeholder 2026-09-13 · HITL
2026-09-10 · ADR-019 w14 footer §6.

Operator: w16 terminated (PR #129), 2026-09-15; scope confirmed in chat
(bulk reprocess and the two `could` filters stay in the W17 queue).
