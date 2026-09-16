---
wave: w18
source: inputs/next/w18-todo.md
source_sha256: "unavailable — no sha256sum/python in this harness at intake"
design_sources: [inputs/design/prototypes/raffa-v2/screens-v2.md, inputs/design/prototypes/raffa-v2/ia-v2.md]
baseline: "unavailable — no git in this harness at intake; read at the gate (ADR-014)"
generated: 2026-09-15T23:59Z
previous_wave: w17
caps: { max_tasks: 20, max_phases: 5 }
focus: "none"
---

# Wave w18 — normalized requirements

Written by `next-intake`. Items keep source ids (`NW-NN`). Every claim about
"today" cites a file in `../`, re-audited on this checkout — not copied from the
raw file. Raw-file corrections marked **⚠**.

> **SHA fields.** `bash` here has no `python`/`git`/`sha256sum`, so `source_sha256`
> and `baseline` can't be computed. ADR-014: the **base SHA is read at the gate,
> never quoted from a wave document** — leaving `baseline` unset is correct; the
> operator stamps it at HITL (as the w17 gate did).

## 1. Oracles in force

- **Wave base**: read at the gate (ADR-014). w17 closed on disk (`w17.yaml`,
  `w17-hitl.md`, ADR-029, epics 20–22). W17-HITL §8.1: `origin/main` moved past the
  council baseline (`d3d2d24` → `7fc831f`) — W18-A1 is that merge.
- Product: `product-spec.md` §8.1/8.2, §10.1; `requirements.md` R-WEB-01/04/05,
  R-DOC-08, R-EVD-01/02, R-ASK-07/10; `percorso-pilota-v1.md`.
- Locked: `locked-decisions.md` + `engineering-brief.md` §1. **Nothing touches a
  locked row.**
- ADRs touched: **ADR-029** (NW-63 split — pre-decided), **ADR-017**
  (`prebuilt-layout` called; widened `words`/`polygon` wire), **ADR-003** (geometry
  columns — refused in w17 clause 2, owed here), **ADR-027** (reprocess re-derives
  boxes), **ADR-024** (Ask scoped chat), **ADR-012** (generated client; citation
  deep-link), **ADR-018** (viewer route), **ADR-019/020** (box affordance, filters,
  Ask UX), **ADR-002** (portfolio join in host), **ADR-014/016** (base, promotion).
- Design: `screens-v2.md` §2 (Ask abstain), §5 (360/Why/citation), §6 (Portfolio
  "Type"), §8 (Savings), §10 (Quote check); `ia-v2.md` route map.
- Last wave: w17 closed **on disk** (record `w17-hitl.md` + `BACKLOG.md`). **No
  `salvage/*` tags carry in.**
- Carry-over: `w17-hitl.md` §5 — head of W18 is **NW-63r** (`must`), then **NW-23**
  (`could`), **NW-25** (`could`), **NW-74** (`should`). **NW-75 OUT** (ADR-001 w17
  clause 7).

## 2. Items

| ID | Title | Kind | Pri | Area | Status | Seats | ADR | Design | Acc |
|---|---|---|---|---|---|---|---|---|---|
| NW-63r | Bounding-box overlay + phrase-edit (remainder) | feature | must | backend/web | OPEN | sw, cloud, client, ux, po | 029,017,003,027 | §5 | N17r |
| NW-23 | Portfolio category filter | feature | could | backend/web | OPEN | sw, po, client, ux | 002,020 | §6 | A18-p |
| NW-25 | Savings list filters | feature | could | web | OPEN | client, ux, po | 020 | §8 | A18-s |
| NW-74 | Hide admin-gated Ask chips | change | should | web | OPEN | client, ux, sec | 022,012 | ia | A17-S3 |
| NW-30 | Hand-authored OpenAPI gaps | change | should | backend/ci | PARTIAL | sw, client | 012,026 | — | A18-o |
| NW-40 | Confirm HCP apply CA env + Foundry RBAC | ops | should | infra | OPEN | cloud, dm | 005,016 | — | A18-ops |
| NW-41 | Prove deployed API serves market_record rows | ops | should | backend | PARTIAL | dm, sw, cloud | 024,005 | — | A18-ops |
| NW-50 | day1.spec.ts red vs V2 shell | ops | should | web/ci | OPEN | dm, client | 016,012 | — | A18-e2e |
| NW-55 | Ask citation cards: preview or section link | feature | should | web | OPEN | client, ux, sw | 024,012,018 | §2 | N10 |
| NW-56 | 360 "Ask about it" briefs that contract | feature | should | web/backend | OPEN | po, client, ux, sw | 024,012 | §5 | N11 |
| NW-57 | Quote check: market benchmark, not worksheet | feature | should | web/backend | OPEN | po, client, ux, sw | 024,001,028 | §10 | N12 |
| NW-59 | Ask never dead-ends (abstain recovery) | change | should | web | OPEN | client, ux, po | 024,020 | §2 | N13 |
| NW-60 | Hide global Ask bar on Ask screens | change | should | web | OPEN | client, ux | 018,020 | ia | N14 |

(Seat keys abbreviated: sw software-architect · cloud cloud-architect · sec
security-architect · client client-architect · ux ux-ui-designer · po product-owner ·
dm delivery-manager.)

### NW-63r — bounding-box overlay + phrase-edit (W18 remainder)

- **Source**: NW-63 W18 remainder, `w18-todo.md` §1; split pre-decided ADR-029
  (OQ-w17-001). Do not re-litigate; do not pull the w17 half back.
- **Raw**: viewer draws a box; OCR phrase editable via a write that does not blur
  proposal vs override; w17's fenced "box" copy honoured until this lands.
- **Today**: (a) `prebuilt-layout` not called (doc comment `AiGatewayModelOptions.cs:52`
  only; ADR-017 "not called in V1"). (b) gateway wire drops geometry —
  `DocumentIntelligenceContracts.cs` `DocumentIntelligencePage(PageNumber, Spans)`,
  no `words`/`polygon`; `AiOcrPage.cs:13` = `(PageNumber, Text)`. (c) no geometry
  columns — grep `bbox|bounding|polygon` = only `InvitationOptions.cs:16`; ADR-003
  w17 clause 2 refused them. (d) no phrase-edit target — `DocumentsEndpointExtensions.cs`
  maps `MapPost(.../reprocess)` `:94` + DELETE; **no `MapPatch`/`MapPut` on documents**
  `:90-96`; correction is `PATCH /api/contracts/{id}` field→scalar
  (`ExtractionEvidence.cs:14-19`).
- **Gap**: four pieces (pre-scoped ADR-029): `prebuilt-layout` + widened
  `words`/`polygon` + widened `AiOcrPage`; geometry columns (migration); viewer box
  overlay; phrase-edit write preserving proposal-vs-override.
- **Seats**: sw (contract, geometry+migration, endpoint+provenance); cloud (gateway
  model, no new SKU); client (box overlay over w17 `<img>`, edit); ux (affordances,
  stated absence); po (acceptance wording).
- **ADR**: ADR-029 governs (no new ADR); amend ADR-017, ADR-003, ADR-027.
- **Design**: `screens-v2.md:95-112`. **Acceptance**: N17 remainder — box over the
  cited phrase; edit persists (second browser) without blurring proposal vs override.
- **Epic**: epic-23 (extends epic-22).

### NW-23 — Portfolio has no `category` filter

- **Source**: NW-23, `w18-todo.md` §1 (carry-over). OQ-w17-007 travels.
- **Raw**: filter by category/Type, or record why V1 cannot; §6 word is **Type**.
- **Today**: `PortfolioFilter.cs:11` "Category deliberately not a member… No Category
  concept"; `:17` "follow-up adds the Category filter". **⚠** `Supplier.Category`
  exists (`Supplier.cs:36`, `SupplierConfiguration.cs:32`) — comment stale; join legal
  (`DependencyDirectionTests.cs:63` allows the supplier-name join at
  `PortfolioEndpointExtensions.cs:133-146`). `Contract` has only `ContractDocumentType`.
- **Gap**: supplier-category filter (host join) or documented "V1 cannot".
- **Seats**: sw (host join); po (Type enough?); client (control); ux (vocabulary).
- **ADR**: ADR-002 (host composition), ADR-020 (control) — or `none`.
- **Design**: `screens-v2.md:114-121`. **Acceptance**: A18 portfolio — restrict by
  type/category on `dev`. **Epic**: epic-24.

### NW-25 — Savings list has no filters

- **Source**: NW-25, `w18-todo.md` §1 (carry-over).
- **Raw**: restrict opportunities (supplier/status/currency); don't invent a category.
- **Today**: no filter/search/sort in savings web folder (`grep` over
  `web/src/**/*savings*` = zero); `savings/index.tsx` = header + KPI + flat
  `OpportunitiesTable.tsx:26-31`.
- **Gap**: flat list, no restriction; the set is the council's pick.
- **Seats**: client (control+read); ux (control); po (filter vs sort — Estimate is a
  sort).
- **ADR**: ADR-020 — or `none`. **Design**: `screens-v2.md:132-137`.
- **Acceptance**: A18 savings — filterable on `dev`. **Epic**: epic-24.

### NW-74 — Hide admin-gated Ask chips from a non-Admin

- **Source**: NW-74, `w18-todo.md` §1 (carry-over). Not demoted. Shape ADR-012 w17
  clause 40.
- **Raw**: Procurement no admin chips; Admin yes; API returns full catalog.
  Presentation, never a security fix.
- **Today**: `roleGate` on wire (`CapabilitiesEndpointExtensions.cs:49`); nothing in
  `web/src` reads it (only generated `schema.ts:692`). Leak: `askSuggestions.ts:51`
  workspace→`workspace-members` (the one Admin row `CapabilityCatalog.cs:244`). Two
  selectors (`GlobalAskBar.tsx:84-95`, `ask/index.tsx:351-357`). Role unpassed:
  `AppShell.tsx:40` holds `role`→`RailNav` (`:50`), not `GlobalAskBar` (`:59`). ADR-022
  S16-11 stands (un-gated, tenant-free).
- **Gap**: two role-blind renderers; one server-derived `role` prop.
- **Seats**: client; ux; sec (confirm only). **ADR**: `none` expected (S16-11 + clause
  40 rule). **Design**: `ia-v2.md:92-115`. **Acceptance**: A17-S3. **Epic**: epic-25.

### NW-30 — Hand-authored OpenAPI gaps

- **Source**: NW-30, `w18-todo.md` §2.
- **Raw**: conversations `requestBody`, no `GET /api/audit`.
- **Today** — **⚠ both halves moved**: (a) `GET /api/audit` **CLOSED-ON-MAIN** (w16
  NW-08, `raffa-api.v1.json:6486`, `Program.cs:377`). (b) conversations `requestBody`
  is a documented generator limitation (parses only `responses`; bodies hand-written
  in `client.ts` — `raffa-api.v1.json:6`; conversations `:5339`, `:5680`).
- **Gap**: both essentially closed; residual = confirm hand-written bodies + sweep
  stale prose. **PARTIAL.**
- **Seats**: sw (contract doc); client (request types) — possibly `none`.
- **ADR**: ADR-012, ADR-026 — likely `none`. **Acceptance**: A18 OpenAPI. **Epic**: epic-26.

### NW-40 / NW-41 — ops walks

- **NW-40** (ops, should, OPEN): confirm HCP apply landed CA env vars
  (`ConnectionStrings__Storage`, `AiGateway__*`, `AzureAd__*` from w15) + Foundry
  RBAC on `dev`+`demo`. Seats cloud + dm (ADR-005, ADR-016). Runbook step.
- **NW-41** (ops, should, PARTIAL): code seeded — `market_record`/`market_embedding`
  tables (`market.sql:44`), `MarketRecordQueryService` (registered
  `ServiceCollectionExtensions.cs:131`), `GET /api/market/records/{id}`
  (`MarketEndpointExtensions.cs:40`); residual = dev walk proving a seeded row is
  served. Seats dm + sw/cloud. Runbook step.

### NW-50 — day1.spec.ts red vs V2 shell

- **Source**: NW-50, `w18-todo.md` §2. ops, should, OPEN.
- **Today**: `web/e2e/day1.spec.ts` exists, authored pre-V2 (Ask is home). No workflow
  runs Playwright (`web.yml` = vitest). Runbook spec, never CI.
- **Gap**: reconcile (or re-scope), no unexplained skip.
- **Seats**: dm; client. **ADR**: ADR-016, ADR-012 — likely `none`. **Acceptance**: A18
  e2e. **Epic**: epic-26.

### NW-55 — Ask citation cards

- **Source**: NW-55, `w18-todo.md` §2. feature, should, OPEN.
- **Today**: `CitationCard.tsx:42` still \"No page preview available\"; `previewUrl`
  almost never set. w17 viewer `/documents/:id/viewer` now exists → tenant citation
  can deep-link. Product citations have no page → CTA card.
- **Gap**: placeholder shipped; deep-link unwired. Two corpora, two treatments.
- **Seats**: client; ux; sw (`PackItem.PreviewUrl`). **ADR**: ADR-024/012/018 (route
  exists). **Design**: `screens-v2.md` §2. **Acceptance**: N10. **Epic**: epic-25.

### NW-56 — 360 \"Ask about it\" briefs that contract

- **Source**: NW-56, `w18-todo.md` §2. feature, should, OPEN.
- **Today**: `Contract360Header.tsx:49` links `/ask?scope=${contractId}`; `AskRoute`
  applies R-ASK-10 gate first (`ask/index.tsx:94`), ignores `?scope=`; `?scope=` handler
  (`askViewModel.ts:428-431`) = empty scoped chat; off-state fires from a live 360.
- **Gap**: scoped entry lands on generic gate/hello.
- **Seats**: po; client; ux; sw. **ADR**: ADR-024, ADR-012 (amend scoped entry path).
  **Design**: `screens-v2.md:95-112`. **Acceptance**: N11. **Epic**: epic-25.

### NW-57 — Quote check: market benchmark

- **Source**: NW-57, `w18-todo.md` §2. feature, should, OPEN.
- **Today**: `web/src/routes/quotes/` drop zone + optional fields (`UploadQuoteForm`);
  `TargetStep` types an editable savings target; assessment `InsufficientBenchmarkData`
  on no match; CTA \"See it in Savings →\". `GET /api/quotes` documented (w16 NW-12),
  history+benchmark-first UX remain the gap.
- **Gap**: job-to-be-done (market benchmark), first-of-type cold start, history.
- **Seats**: po; client; ux; sw. **ADR**: ADR-024/001/028. **Design**: `screens-v2.md`
  §10. **Acceptance**: N12. **Epic**: epic-25.

### NW-59 — Ask never dead-ends

- **Source**: NW-59, `w18-todo.md` §2. change, should, OPEN.
- **Today**: `ReplyBody.tsx:75` abstain = `.abstain-block` only; `replyTypes.ts:50`
  \"abstain/error never carry actions\". R-ASK-07 forbade the block as the *only* UX —
  the UI still does.
- **Gap**: no recovery control; prefer the action that unblocks *this* failure.
- **Seats**: client; ux; po. **ADR**: ADR-024/020. **Design**: `screens-v2.md` §2.
  **Acceptance**: N13. **Epic**: epic-25.

### NW-60 — Hide the global Ask bar on Ask screens

- **Source**: NW-60, `w18-todo.md` §2. change, should, OPEN.
- **Today**: `AppShell.tsx:82` always renders `GlobalAskBar` (AC-3 \"every screen\");
  submit navigates `/ask?…` and no-ops on an open chat. Duplicate of the composer.
- **Gap**: two bars fight; remove on `/ask`+`/ask/:id`, keep elsewhere, ⌘K → composer.
- **Seats**: client; ux. **ADR**: ADR-018/020. **Acceptance**: N14. **Epic**: epic-25.

## 3. Seat roster

| Seat | Involved | Items | Why |
|---|---|---|---|
| product-owner | yes | 63r,23,25,56,57,59 | Scoped-chat follow-ups, quote job-to-be-done, abstain actions are vocabulary; NW-23/25 scope. |
| software-architect | yes | 63r,23,30,41,55,56,57 | Gateway wire + geometry + phrase-edit, host join, OpenAPI residual, Ask scoped-turn, quote seeding. |
| cloud-architect | yes | 63r,40,41 | `prebuilt-layout` gateway model (no new SKU); NW-40/41 environments/Foundry. |
| security-architect | confirm only | 74 | One line — chip hide still presentation (S16-11), catalog un-gated. |
| client-architect | yes | 63r,23,25,74,30,50,55,56,57,59,60 | Ten SPA items. |
| ux-ui-designer | yes | 63r,23,25,74,55,56,57,59,60 | Nine surfaces/controls. |
| delivery-manager | yes | 40,41,50 | HCP walk, market dev-walk, e2e set. |
| council-gate | yes | all | Verifies and closes. |

## 4. Proposed epics (next free: 23–26)

| Epic | Slug | Theme | Items |
|---|---|---|---|
| epic-23 | viewer-bounding-boxes-and-phrase-edit | NW-63 remainder | NW-63r |
| epic-24 | portfolio-and-savings-filters | list filters | NW-23, NW-25 |
| epic-25 | ask-and-quote-product-completeness | Ask/Quote UX + chip | NW-74, NW-55, NW-56, NW-57, NW-59, NW-60 |
| epic-26 | contract-and-ops-residuals | OpenAPI + ops + e2e | NW-30, NW-40, NW-41, NW-50 |

## 5. Selection for this wave (cap 20 tasks / 5 phases)

NW-63r is the only `must` — never overflows. Carry-over head enters first (§0.3),
then the recorded queue in order. Only `should`/`could` overflow → **head of W19**.

- **In wave** (priority order): NW-63r (must, ~5 tasks) → NW-23 (~2) → NW-25 (~1–2)
  → NW-74 (~1) → NW-55 (~2) → NW-56 (~2) → NW-57 (~2–3) → NW-59 (~1–2) → NW-60 (~1)
  → NW-50 (~1) → NW-30 (~1) → NW-40/NW-41 (runbook steps).

**Budget note.** Honest estimate ≈ 20–22 vs cap 20; the decomposer applies the cap.
The `must` and the carry-over head cannot overflow. If the cap binds, draw the
overflow from the **tail** (NW-40/NW-41 walks, NW-30/NW-50 reconciliations), to the
**head of W19** in raw-file order, never the tail. Do not cut NW-63r's durability or
phrase-edit provenance.

### Remaining schedule

| Wave | Queue (head first) |
|---|---|
| W18 (this run) | NW-63r, NW-23, NW-25, NW-74, NW-30, NW-40, NW-41, NW-50, NW-55, NW-56, NW-57, NW-59, NW-60 |
| W19 | whatever does not fit the cap, head first, never tail |

### Order constraints

1. **NW-63r before NW-55** (deep-link lands on the box; both use the w17 viewer route).
2. **NW-56/57/59 share the Ask surface** (`AskRoute`, reply types, scoped gate) —
   phase-separate or co-locate (single-writer-per-phase, ADR-012 §3).
3. **NW-40/41 are runbook walks, not code** — no `depends_on`, ride final integration.

## 6. Superseded work items

**None.** No \"cancels / replaces\" statement in the raw file; no status banner; no
`superseded` line. **NW-75 is OUT** (ADR-001 w17 clause 7) — recorded, not queued.
Two prior facts recorded so they are not re-opened: NW-30's `GET /api/audit` half is
**CLOSED-ON-MAIN** (w16 NW-08); NW-41's code side is **landed** (residual = the walk).

## 7. Open questions and assumptions

- **OQ-w18-001 baseline SHA** — no git/python here. **Assumption**: operator reads
  the base at the gate and merges `origin/main` (moved past `7fc831f`) before fan-out
  (W18-A1, as the w17 gate did).
- **OQ-w18-002 NW-63r phrase-edit provenance** — does an edit rewrite the text, the
  evidence row, or both (ADR-029 left it open)? **Assumption**: the council settles it;
  the write keeps proposal vs override (`ExtractionEvidence.cs:14-19`), never silently
  overwrites a human correction (ADR-003/027). No decomposition without a ruling.
- **OQ-w18-003 NW-23 Type vs Category** (OQ-w17-007) — `Supplier.Category` exists but
  `Contract` has only `ContractDocumentType`. **Assumption**: filter by supplier
  category joined in the host (matching `supplierName`), §6's \"Type\" stays the doc
  type; else record \"V1 still cannot\".
- **OQ-w18-004 NW-30 residual** — is the requestBody closure a decision or a sweep?
  **Assumption**: a sweep (ADR-012 already decided the shape); seats record `none`.
- **OQ-w18-005 cloud NW-63r gate** — does `prebuilt-layout` need a new role or is it
  on the account (ADR-017:103, ADR-008)? **Assumption**: already on the account — no
  new SKU/role; cloud records `none — ADR-008/017`. If a role is owed, it is an
  NW-40-adjacent find, not a new item.
