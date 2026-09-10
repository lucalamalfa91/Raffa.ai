# Software-architect (web) — thin API gaps for locked screens

Lane: `software-architect` (web delta). Scope is **narrow** per the council protocol:
list only the thin API gaps a locked screen cannot render. No module redesign, no
application code, no new backend architecture.

Sources read (not invented):

- `inputs/design/prototypes/screens.md` — 10 locked screens + §20 coverage table.
- `inputs/design/prototypes/ia.md` — route map + object→screen cross-links.
- `reports/architecture/draft/software-architect/module-map.md` — Appendix A
  endpoint ownership per bounded context.
- E01–E05 workitem ACs — the only place endpoint **fields** are named:
  `epic-01-platform/**`, `epic-02-contract-intelligence/**`,
  `epic-03-renewal-intelligence/**`, `epic-04-savings-intelligence/**`,
  `epic-05-quote-check/**`.
- `ADR-012-web-stack.md`, `ADR-002-dotnet-solution.md` (locked; cited, not re-opened).
- `inputs/product-spec.md` §6 (data model) and Appendix A (API catalogue).

---

## Method

For each locked screen, I asked: *does an endpoint AC promise to return every field
the screen renders?* If a field appears in `screens.md` but is not named in any
endpoint AC (either as a column/field or a "returns …" clause), it is a **gap**.
It is a **thin** gap, not a redesign: the owning bounded context and endpoint
already exist (module-map.md); the write is a named field/handler addition, or a
named filter/clause on an existing endpoint.

---

## Confirmed thin gaps

### GAP-1 — Review/correction screen: model/prompt version on a fact/correction

- **Screen**: `screens.md` §6 — right pane shows *"evidence page with highlighted
  passage, correction form, **model/prompt version**"*.
- **API tree**: `PATCH /api/contracts/{id}` (correction-history AC-1) records a
  versioned correction; `task-02-schema-evidence` adds evidence/source-span/
  confidence/version columns. But **no endpoint AC returns `model_id` /
  `prompt_version`** on a fact or its correction history.
- **Why it blocks rendering**: the model/prompt-version line is a literal element
  of the locked screen; it is logged by the AI Gateway internally
  (module-map.md "AI Gateway … logs model/version/prompt-version/timestamp/
  input-hash", ADR-011) but is not exposed on the read path the screen consumes.
- **Thin fix (named, no module redesign)**: expose `model_id` + `prompt_version`
  on the extracted fact / correction-history read (Documents/Contracts module,
  already owns this entity). One field pair surfaced from AI-Gateway usage-log
  onto the contract fact DTO.

### GAP-2 — Contract 360 › Activity tab: contract-scoped audit stream

- **Screen**: `screens.md` §5 (Activity tab) + `ia.md` route `/contracts/:id`
  ("tabs … Activity").
- **API tree**: `GET /api/audit` (audit-baseline AC-2) returns *"authorized,
  tenant-scoped events"* — workspace-wide, no entity filter. Contract 360
  aggregate AC-1 (`GET /api/contracts/{id}`) returns "header + tab data" but the
  Activity tab data is not an audit projection in any AC.
- **Why it blocks rendering**: the Activity tab needs the correction/access
  events **for this one contract**, which no endpoint promises a contract-scoped
  filter (`?contractId=`) or projection for.
- **Thin fix**: add a contract-scoped filter/projection to the existing audit
  read (`GET /api/audit?contractId={id}`). Audit is a cross-cutting capability
  (module-map.md); no module redesign — a query clause + optional projection.

### GAP-3 — Home Savings KPI cells: single KPI aggregation (inc. cross-domain counts)

- **Screen**: `screens.md` §9 — 6 KPI cells: annual spend analyzed · savings
  identified · savings realized · savings in progress · **contracts analyzed** ·
  **upcoming renewals**.
- **API tree**: `GET /api/savings` (savings-kpis AC-1/AC-2) names the KPIs but the
  AC-2 return is only *"the opportunity list"*. `contracts analyzed` and
  `upcoming renewals` are **not savings-domain facts** — they derive from the
  Documents/Contracts and Renewals modules, which no savings endpoint AC promises
  to include. The renewal pipeline endpoint (`GET /api/renewals`) returns rows,
  not a "count of upcoming renewals" KPI.
- **Why it blocks rendering**: the two cross-domain KPI cells have no endpoint
  AC that returns them on the page's single data call.
- **Thin fix**: a KPI aggregation payload on the savings dashboard read (Savings
  module composes the two cross-domain counts from Renewals and Contracts —
  already referenced through interfaces per module-map). One named read handler;
  no module restructuring.

### GAP-4 — Portfolio attention strip / Contract 360 fact row: priority + drivers

- **Screen**: `screens.md` §4 (Attention strip: "High risk", "Deadlines < 45 d",
  severity/score sort) and §5 (6-cell fact row includes **risk + priority**;
  Overview "recommended-action block … with **3 driver numbers**").
- **API tree**: `GET /api/contracts` AC-1 returns spec §8.1 columns
  (supplier, contract, annual spend, start/end, renewal, cancellation deadline,
  auto-renewal, risk, status) — **no priority-score field**. The priority score
  and its component breakdown live in Renewals (`GET /api/renewals`,
  priority-score AC-1/AC-2: spend+urgency+benchmark+uplift+contract-risk
  components, persisted separately). The Contract 360 aggregate AC-1 "header +
  tab data" does **not** promise the priority score or the 3 driver numbers.
- **Why it blocks rendering**: the attention strip sorts by "score" and the
  Overview renders a recommended action with 3 driver numbers; neither the
  portfolio list nor the 360 aggregate is promised to return the priority score /
  driver decomposition.
- **Thin fix**: include `priority_score` (+ driver components) on the portfolio
  row and/or 360 aggregate. The score already exists and is persisted in the
  Renewals module (priority-score AC-2 "components persisted, not computed
  inline"); this is a read-side projection/join, not a recompute.

### GAP-5 — Workspace picker: currency/region + contract count on workspace list

- **Screen**: `screens.md` §1 — the workspace list rows show *"name, **contract
  count**, **currency/region**, role tag"* (and `ia.md` route `/workspaces`).
- **API tree**: `GET /api/workspaces` (workspace-roles AC-2) enforces tenant
  scoping and returns workspace/user/role/membership, but **no AC names
  `currency`, `region`, or a per-workspace `contract_count`**. Spec §6 lists no
  Workspace entity field set at all (it enumerates Supplier/Product/Contract/…
  only), so there is no data-model field the screen can assume is present.
- **Why it blocks rendering**: the locked picker renders a currency/region label
  and a contract count per workspace; neither is a documented return field or a
  documented entity column.
- **Thin fix**: add `currency` + `region` columns to the Workspace entity
  (Identity/Workspace module) and return a `contract_count` aggregate on the
  workspace-list read (count via the Contracts module interface). Two named
  fields + one read-side count; no module redesign.

### GAP-6 — Document status: per-stage processing-pipeline progress

- **Screen**: `screens.md` §3 — after upload the view renders a *"Processing
  pipeline list (6 stages, current pulsing)"* (classify → parse/OCR → section/
  table → extract → validate → normalize/index per spec §7.1). This is a
  **per-stage progress bar**, not a single status badge.
- **API tree**: `GET /api/documents/{id}` (document-upload AC-2/AC-3) persists and
  returns *"metadata + status"* — one `processing_status` per the §6 Document
  entity (uploaded/processing/needs_review/completed/failed). **No AC promises
  per-stage (sub-step) progress** for the 6-stage pipeline.
- **Why it blocks rendering**: the locked screen draws a 6-step pipeline with a
  "current" stage; a single status field cannot distinguish "classifying" from
  "extracting" from "indexing".
- **Thin fix**: expose a stage-level progress projection on the document read
  (Documents/Contracts module) — e.g. `processing_stages: [{stage, status}]`
  derived from the worker's processing-job state, alongside the existing
  aggregate `processing_status`. One named read field on an existing entity;
  the pipeline stages already exist as worker steps (spec §7.1).

---

## Fields verified as *covered* (no gap — documented for the table, not ADR-worthy)

- **Benchmark P25/P50/P75 + confidence/provenance/updated** — covered by
  benchmark-interface AC-1 (`getBenchmark(...) → P25/P50/P75 + metric/currency/
  confidence/source/updated/comparison`) and fixture-adapter AC-1/AC-3
  (incl. "insufficient market data" abstain).
- **Quote line-level assessment + target range + potential saving + provenance/
  confidence** — covered by market-assessment AC-1/AC-2/AC-3
  (`GET /api/quotes/{id}/assessment` returns confidence/provenance).
- **Negotiation levers + per-lever evidence + opening/acceptable/walk-away** —
  covered by negotiation-strategy AC-1/AC-2.
- **Outcome capture + realized-propagation → Home Savings Realized** — covered by
  outcome-capture AC-2 (`POST /api/negotiations/outcomes` → realized savings
  surface on savings dashboard).
- **SKU unmatched + manual mapping + recalculate** — covered by
  sku-normalization AC-1/AC-2/AC-3 + quote-line-extraction AC-2.
- **Document aggregate status incl. failed/needs_review** — covered by
  document-upload AC-2/AC-3 (metadata + status persisted and returned). (The
  *per-stage* progress is GAP-6; the *aggregate* status is not a gap.)
- **Workspace/member invite + roles** — covered by workspace-roles AC-1/AC-2/AC-3
  (the "non-tenant domain" invite-validation error state in `screens.md` §2 is a
  UX-state concern owned by ux-ui-designer, not an API field gap: the backend
  already returns 4xx on invalid invite; the client maps it to the error state —
  client-architect residual, not software-architect). The **currency/region/
  contract-count** list cells are the separate GAP-5.
- **Ask Raffa citations (doc + page/section) + abstain + route line** — covered
  by rag-citations AC-2 (citations or "cannot determine") + query-router AC-1/AC-2.
- **Renewal threshold strip (0–30 … 270–365 d counts)** — not a gap: `GET
  /api/renewals` returns the pipeline rows, and the client can group rows into
  the threshold buckets (a pure client-side aggregation over an existing field).
- **Contract 360 Overview "Needs attention" (fields < 95% confidence)** — the
  confidence column is added by task-02-schema-evidence; the aggregate AC-2
  ("commercials/products … clauses/obligations/risks from extracted facts")
  implies confidence is present on those facts. No separate gap.

---

## Boundary note (what I am *not* doing)

- No new endpoints beyond named field/handler/filter additions on existing ones.
- No module boundary change (`module-map.md` dependency direction stands).
- No backend DTO redesign; the single OpenAPI contract (ADR-012) regen picks up
  whatever thin fields are added.
- The six gaps are **candidate thin backend-gap tasks** for the decomposer
  (`layer: backend` thin writes), each citing the owning module + endpoint above —
  not a new epic and not a redesign. Final decision whether to open the ADR is
  the council-close's call; this lane only records the gaps on disk.

## Assumption in force

- The cross-domain KPI counts (GAP-3), the priority/driver projection (GAP-4),
  and the workspace contract-count (GAP-5) read through the domain modules'
  **interfaces** (not provider SDKs), consistent with module-map dependency
  direction. No change to that rule is requested.
