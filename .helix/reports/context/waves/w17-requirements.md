---
wave: w17
source: inputs/next/w17-todo.md
source_sha256: 8afa1243587461327dfec6e0eafa3003d676345c0b8c9b9aed5edaa8b8de9275
design_sources: [inputs/design/prototypes/raffa-v2/screens-v2.md, inputs/design/prototypes/raffa-v2/ia-v2.md]
baseline: d3d2d24 (helix/w17)
generated: 2026-09-15T11:40Z
previous_wave: w16
caps: { max_tasks: 20, max_phases: 5 }
focus: "none"
---

# Wave w17 — normalized requirements

Written by `next-intake` from the raw file above. The raw file stays the
human's; this file is the process oracle for the council and the decomposer.
Items keep the source ids (`NW-NN`). Every claim about "today" cites a file in
`../`, re-audited on this checkout — **not copied from the raw file**. Where the
raw file's own "Today" paragraph is wrong against `d3d2d24`, the correction is
marked **⚠ raw-file correction** and the code is the authority.

> **Parameter note.** `reports/plan/next-run.json` (written 2026-09-15T11:14:55Z)
> resolves this run as `wave=w17`, `todo=inputs/next/w17-todo.md`,
> `previous=w16`. The workflow input string carried a stale `wave=w15`;
> `kb-contract-next` puts `next-run.json` first, and the disk agrees —
> `w15-requirements.md`, `w16-requirements.md`, `w15.yaml` and `w16.yaml` all
> exist, and the raw file's §0.1 states "This run **is W17**".

## 1. Oracles in force

- **Wave base**: `d3d2d24` on `helix/w17`; `git rev-list --left-right --count
  origin/main...HEAD` = `0 0`. The wave base **is** `origin/main` — no base
  row, no "merge main first" open question (ADR-014 w14 clause 1).
- Product: `inputs/product-spec.md` §7.3 (confidence bands — **superseded by
  NW-71**, see §6), §8.1/§8.2 (portfolio, 360), §10.1 (savings);
  `inputs/requirements.md` R-WEB-05/R-EVD-02/R-DOC-07;
  `inputs/percorso-pilota-v1.md` §"Soglia HITL" (**superseded by NW-71**).
- Locked: `reports/context/locked-decisions.md` + `inputs/engineering-brief.md`
  §1. **Nothing in this wave touches a locked row** — no cloud, environment,
  IaC, backend-stack, source-control, auth or API-first decision moves.
  Confidence thresholds are **not** locked; NW-71 is council-owned.
- ADRs touched by this wave: ADR-001 (scope / non-goals), ADR-002 (module
  boundaries), ADR-003 (schema — NW-71 and NW-63 both need a migration),
  ADR-012 (web stack; §3 one-writer, w16 clause 25 `client.ts`), ADR-016
  (w16 clause 31 — the bulk console shape), ADR-017 (OCR models — NW-63's
  bounding boxes), ADR-019 (confidence rows, stale by its own w14 footer §6),
  ADR-020 (screen inventory), ADR-021 (schema apply), ADR-022 (w16 S16-11 —
  `roleGate` is presentation), ADR-024 (Ask V2), ADR-027 (async processing),
  ADR-028 (server-side state).
- Design: `inputs/design/prototypes/raffa-v2/screens-v2.md` §4 Review
  (`:81-93`), §5 Contract 360 (`:95-112`), §6 Portfolio (`:114-121`), §8
  Savings (`:132-137`); `ia-v2.md` route map (`:42-58`). All four anchors
  verified present on disk.
- Last wave: **no `wave-close-w16.md` exists**. `reports/execution/wave-close.md`
  is the **stale w15 file** that ADR-014 w16 clause 3 names as a defect — do
  not read it as current. The authoritative w16 record is
  `reports/workitems/BACKLOG.md` §"Wave w16" (`:309-389`) and
  `reports/audit/w16-hitl.md`.
- Carry-over: **w16 queued nothing** (`BACKLOG.md:343-348`, `w16-hitl.md:153-159`
  — "13/20 tasks, 5/5 phases, no item demoted"). The four head items of this
  wave are the residuals **ruled into W17 at the w16 table**, not decomposed
  tasks: `BACKLOG.md:350-356`, `w16-hitl.md:156-159`,
  `../docs/waves/w16-acceptance.md` known-gap #1, ADR-016 w16 clause 31
  (`:770-784`), ADR-022 w16 S16-11 (`:222-231`), OQ-w16-po-01 / OQ-w16-sa-02 /
  OQ-w16-ca-01.

## 2. Items

| ID | Title | Kind | Priority | Area | Status today | Seats | ADR touchpoints | Design refs | Acceptance |
|---|---|---|---|---|---|---|---|---|---|
| NW-72 | Savings KPI realized amount | change | should | backend / web | **PARTIAL** | product-owner, software-architect, client-architect, ux-ui-designer | ADR-001, ADR-028 | screens-v2 §8 | A17-S1 |
| NW-73 | Bulk whole-tenant reprocess console | ops | should | backend / infra / ci | **OPEN** | delivery-manager, software-architect, security-architect, cloud-architect | ADR-016, ADR-011, ADR-005 | none | A17-S2 |
| NW-74 | Hide admin-gated Ask chips from a non-Admin | change | should | web | **OPEN** | client-architect, ux-ui-designer, security-architect | ADR-022, ADR-024, ADR-012 | ia-v2 §Ask intents | A17-S3 |
| NW-75 | Designed Savings section for tracked renewal actions | design | could | web | **OPEN** | product-owner, ux-ui-designer, client-architect | ADR-001, ADR-020 | screens-v2 §8 | A17-S4 |
| NW-20 | 360 `benchmark` / `activity` always `[]` | change | should | backend | **OPEN** | software-architect, product-owner | ADR-002, ADR-024 | screens-v2 §5 | N16 |
| NW-22 | Renewal insight `MarketPosition` null | change | should | backend | **OPEN** | software-architect, product-owner | ADR-002 | screens-v2 §5/§7 | N16 |
| NW-23 | Portfolio has no category filter | feature | could | backend / web | **OPEN** | software-architect, product-owner, client-architect, ux-ui-designer | ADR-002, ADR-020 | screens-v2 §6 | A17 portfolio filter |
| NW-25 | Savings list has no filters | feature | could | backend / web | **OPEN** | client-architect, ux-ui-designer, product-owner | ADR-020 | screens-v2 §8 | A17 savings filter |
| NW-26 | Preview is a placeholder PNG | feature | should | backend | **OPEN** | software-architect, cloud-architect *(conditional)* | ADR-017, ADR-005 | screens-v2 §5 | N17 |
| NW-62 | 360 answers "save" / "move" | feature | **must** | backend / web | **PARTIAL** | product-owner, software-architect, client-architect, ux-ui-designer | ADR-001, ADR-024, ADR-002 | screens-v2 §5 Answers | N16 |
| NW-63 | Document viewer, highlighted + editable OCR | feature | **must** | web / backend | **OPEN** | client-architect, ux-ui-designer, software-architect | ADR-012, ADR-017, ADR-003, ADR-027 | screens-v2 §5 Why | N17 |
| NW-64 | Unrecovered fields get a fillable section | feature | should | web | **OPEN** | ux-ui-designer, client-architect, product-owner | ADR-020, ADR-019 | screens-v2 §4 | N18 |
| NW-65 | Details: only officialized facts | design | should | web | **OPEN** | ux-ui-designer, client-architect | ADR-019, ADR-020 | screens-v2 §5 Details | N19 |
| NW-66 | Why-clauses: no quote, leverage not confidence | design | should | web | **OPEN** | ux-ui-designer, client-architect, product-owner | ADR-019, ADR-020 | screens-v2 §5 Why | N20 |
| NW-71 | Auto-accept a field at ≥ 90 % (server + web) | feature | **must** | backend / web | **OPEN** | product-owner, software-architect, security-architect, client-architect, ux-ui-designer | ADR-019, ADR-003, ADR-011, ADR-024, ADR-012 | screens-v2 §4 | A17-1…A17-4 |

---

### NW-72 — Savings KPI realized amount

- **Source**: `inputs/next/w17-todo.md` §1 (head of wave); OQ-w16-po-01, ADR-001
  w16 clause 4. Unmet AC-1 of the still-`active`
  `reports/workitems/epic-04-savings-intelligence/.../us-01-savings-kpis.md:19`.
- **Raw**: "Savings shows the **realized money** (the verified amount, not the
  pre-negotiation estimate) in the KPI band, grouped by currency, with provenance."
- **Today (evidence)**: `../backend/src/Raffa.Savings/Application/SavingsKpiCalculator.cs:86`
  buckets `Realized` and `:103-104` sums `EstimatedSavingsLow/High` — the
  opportunity's **own estimated range**, as `:46-54` admits.
  `../backend/src/Raffa.Savings/Application/SavingsKpiQueryService.cs:32-36`
  projects only `SavingsOpportunities`. Web renders a **count**:
  `../web/src/routes/savings/savingsViewModel.ts:110`
  (`${countOf(kpis.savingsRealized)} realized`), and `buildKpiCells` returns
  exactly three cells (`:61`, `:93-112`) painted by `KpiRow.tsx:45-58` — there
  is no realized-money slot.
- **⚠ raw-file correction**: the raw file says the calculator's comment "names
  `Domain.RealizedSavings` (E04/F02/US02/T02) as the **missing** verified-value
  record". **That record now exists and is written.**
  `../backend/src/Raffa.Savings/Domain/RealizedSavings.cs:26` (with `Amount` `:45`,
  `Currency` `:51`), persisted at `Infrastructure/SavingsDbContext.cs:29`, written
  at `Application/SavingsOpportunityService.cs:325`. It is **write-only**: one
  `.Add` and no reader anywhere. The comment at `SavingsKpiCalculator.cs:50-52`
  and `Domain/SavingsOpportunityStatus.cs:37-44` are **stale** and the task must
  sweep them. Status is therefore **PARTIAL**, not OPEN — the record is there,
  the read path is not.
- **Also**: `SavingsEndpointExtensions.cs:169-171` already emits `realizedAmount`,
  but it is non-null only on the PATCH that just recorded it
  (`SavingsOpportunityResult.cs:10-19`); it is on the wire
  (`schema.ts:614`) with **zero consumers** in `web/src`.
- **Gap**: the verified amount is recorded and never read back; the KPI still
  reports the pre-negotiation estimate, and no surface shows realized money.
- **Seats**: product-owner (this is an unmet AC, **not** a new promise — ADR-001
  §1.2 is not touched), software-architect (which amount is "verified": the
  `RealizedSavings` rows vs the PATCH-only `RealizedAmount`), client-architect
  (the KPI cell and its read), ux-ui-designer (screens-v2 §8's KPI triple is
  *Contracts analyzed · Upcoming renewals · Savings identified* — **the oracle
  carries no realized-money KPI at all**, so the slot must be designed, not
  assumed).
- **ADR touchpoints**: ADR-001 (w16 clause 4's money fence is lifted by this
  item — record it), ADR-028 §D5 fence 2 (an unlinked outcome still enters no
  total) — likely `amend` both, or `none — the fence already binds`.
- **Design refs**: `screens-v2.md:132-137` (KPI triple; no money slot exists).
- **Acceptance**: A17-S1 — record an outcome that realizes an opportunity →
  Savings KPI shows a money figure for realized, grouped by currency, not only a
  count; reload and a second browser agree; `savingsPropagated: null` still
  enters no total.
- **Proposed epic**: epic-20 (extends epic-04 F02/F03).

### NW-73 — Bulk whole-tenant reprocess console

- **Source**: `inputs/next/w17-todo.md` §1; ADR-016 w16 clause 31 (`:770-784`),
  `../docs/waves/w16-acceptance.md` known-gap #1.
- **Raw**: an operator console "calling `DocumentReprocessService.ReprocessAsync`
  per document — so the `extraction_job` row, the `document.reprocessed` audit
  row and ADR-027 §D5's replace step stay **the product's own code, never
  re-implemented in bash**."
- **Today (evidence)**: the workflow set is **ten files**;
  `reprocess-tenant-documents.yml` is **gone** and
  `../.github/workflows/verify-tenant-corpus.yml` is present — both changes in
  the single commit `21eee60` (w16 / NW-31). It is **report-only**, verified
  across all 225 lines: every `psql` call is `SET app.tenant_id …; SELECT …`,
  with no `INSERT`/`UPDATE`/`DELETE`, no API call and no Service Bus step
  (`:143` enumerates `processing_status`, `:151-160` builds the worklist,
  `:167` and `:222` say so in the job's own output).
- The in-product path is one document at a time:
  `../backend/src/Raffa.Api/DocumentsEndpointExtensions.cs:94` (route),
  handler `:479`, Admin gate `:506-509`, **202** at `:530` →
  `../backend/src/Raffa.Documents.Contracts/Application/DocumentReprocessService.cs:62`
  (`ReprocessAsync`), declared at `:37`. The route takes a single `{id}`; there
  is **no list, array or batch variant**.
- **No console exists**: there is **no `Raffa.Tools` project** (`backend/Raffa.slnx`
  lists 17 `src/` + 18 `tests/` projects, none named Tools) and
  `../backend/scripts/` holds only `backfill-workspace-membership.sql`,
  `demo-fixture-seed.sql`, `live-smoke.sh`, `make-smoke-pdf.py`. A repo-wide grep
  for `bulk|batch.*reprocess|whole.tenant|BulkReprocess` finds no entry point in
  API, Worker or CLI.
- **The two projects the shape names both exist**: `backend/Raffa.slnx:8`
  (`Raffa.Documents.Contracts`) and `:12` (`Raffa.Messaging`). `Raffa.Messaging`
  already references `Raffa.SharedKernel` **and** `Raffa.Documents.Contracts`, so
  a console referencing `Raffa.Messaging` alone picks up both halves.
  **⚠ Mechanical note for the decomposer**: the solution file is **`.slnx`**
  (XML), not `.sln` — a glob for `*.sln` finds nothing, and a new console project
  must be registered there.
- **⚠ Refinement to ADR-016 clause 31's shape** — `ReprocessAsync` usually writes
  **no new `extraction_job` row**: `RequeueClassificationJobAsync` (`:138-176`)
  **reuses and resets** the latest `Classification` job (`:161-173` — `Status =
  Queued`, `ClaimedAt = null`, `AttemptCount` deliberately preserved per
  `:134-136`), inserting only when none exists (`:149-160`). The
  `ClaimedAt = null` reset is what lets the Worker's compare-and-swap claim
  (`claimed_at IS NULL`) re-deliver at all. **A console must call this method,
  never replicate it** — which is exactly why clause 31 forbids re-implementing
  the path in bash.
- It also publishes **before** the commit (`:110-113`, reason at `:107-109`,
  ADR-027) and writes the `document.reprocessed` audit row at `:117-126`
  (constant `:51`).
- **The RBAC gap is real, and larger than "one module change"**:
  `../infra/modules/servicebus/main.tf:71-87` grants `Azure Service Bus Data
  Sender` (topic-scoped, `:72`) — but to `var.workload_principal_id`, which is
  the **Container Apps managed identity** (`infra/modules/identity/main.tf:36-37`,
  wired `environments/dev/main.tf:115`). The CI principal is a **different**
  principal, looked up by literal client id
  (`infra/environments/dev/main.tf:223-225`, with the ADR-015 "SPs are out of
  band" note at `:215-222`), and its **only** data-plane grant today is Key Vault
  Secrets User (`infra/modules/keyvault/variables.tf:35`). **No
  `azurerm_role_assignment` anywhere in `infra/` gives `raffa-sp-<env>` any
  Service Bus role** — that assignment is the cloud-architect module change
  ADR-016 clause 31 says is owed.
- **How the app publishes today** (the console must match it): port
  `IExtractionQueuePublisher`
  (`../backend/src/Raffa.Documents.Contracts/Application/Extraction/IExtractionQueuePublisher.cs:19`),
  adapter `../backend/src/Raffa.Messaging/ServiceBusExtractionQueuePublisher.cs:24`
  (sender `:38`, send `:57`, `MessageId` `:50` for duplicate collapse), auth
  `DefaultAzureCredential` with **no connection string and no SAS**
  (`MessagingServiceCollectionExtensions.cs:84-87`, comment `:81-83`).
- **Stale prose residual the wave should sweep**: the deleted workflow is still
  named in **executable test text** — `../web/e2e/v2.spec.ts:117` and `:431` —
  plus `backend/README.md`, `infra/README.md`, `docs/ask-v2-acceptance.md` and
  `.github/workflows/backfill-workspace-membership.yml`.
- **Gap**: the bulk capability R-DOC-07 names is unavailable; w16 bounded the
  loss deliberately and scheduled the shape here.
- **Seats**: delivery-manager (owns the console and the CI-YAML set — ADR-016
  clause 31 assigns it), software-architect (the console references product code
  and must not re-implement the pipeline), security-architect (**owed**: may a CI
  principal hold a Send right at all — inclination already recorded as *yes,
  Send only, topic-scoped, never `Manage`, never a SAS key*, ADR-022 `:245-251`;
  and what actor the console writes, `system:<component>` per ADR-011 w16
  clause 16 / NW-32), cloud-architect (the module `azurerm_role_assignment`).
- **ADR touchpoints**: amend ADR-016 (the console lands), ADR-011 (the actor),
  ADR-005/ADR-007 (one role assignment in `modules/servicebus`).
- **Design refs**: none (operator surface).
- **Acceptance**: A17-S2 — operator runs the console against a `dev` tenant with
  N documents; each is re-enqueued **through the product path** and reaches a
  terminal state; `verify-tenant-corpus.yml` still reports `%PDF` gone; A16-3's
  single-document path stays green.
- **Three shortcuts stay refused** (ADR-016 clause 31): Service Bus SAS key, any
  `X-*` identity header, inserting `extraction_job` rows via psql.
- **Proposed epic**: epic-20.

### NW-74 — Hide admin-gated Ask chips from a non-Admin

- **Source**: `inputs/next/w17-todo.md` §1; OQ-w16-sa-02, ADR-022 w16 S16-11.
- **Raw**: "the Ask / capability UI hides entries whose `roleGate` is `admin`
  when the signed-in membership is not Admin. The API still returns the full
  catalog." **Presentation, never a security fix** — S16-11 is the reason that
  is safe and it "must never be re-described as a security fix", in the item or
  in a PR.
- **Today (evidence)**: `roleGate` is on the wire —
  `../backend/src/Raffa.Api/CapabilitiesEndpointExtensions.cs:49`
  (`roleGate = capability.RoleGate.ToApiValue()`). **Nothing in `web/src` reads
  it**: the single occurrence under `web/src` is the generated type
  `../web/src/api/generated/schema.ts:692`. `GET /api/capabilities` is **fully
  anonymous and tenant-free** — `CapabilitiesEndpointExtensions.cs:31`
  (`GetCapabilities()` takes no parameters at all) and `:33`
  (`CapabilityCatalog.All`, no filter), proven by
  `../backend/tests/Raffa.Api.Tests/CapabilitiesEndpointTests.cs:57-65`.
- **The concrete leak**: `../web/src/components/ask-bar/askSuggestions.ts:51`
  maps screen `workspace → "workspace-members"`, which is the **one**
  `CapabilityRoleGate.Admin` row
  (`../backend/src/Raffa.Chat/Application/Capabilities/CapabilityCatalog.cs:244`).
  A Procurement user on `/workspace*` gets that capability's example questions
  as chips.
- **Chips render in two places**:
  `../web/src/components/ask-bar/GlobalAskBar.tsx:84-95` (selector
  `askSuggestions.ts:68-82`, `:89-97`) and `../web/src/routes/ask/index.tsx:351-357`
  (selector `askViewModel.ts:383-395`). Both selectors read only `key` +
  `exampleQuestions`.
- **The role is already in scope and simply not passed**:
  `../web/src/components/shell/AppShell.tsx:40` holds `role` and passes it to
  `RailNav` at `:50`, but `:59` renders `<GlobalAskBar …/>` **without it**;
  `GlobalAskBarProps` (`GlobalAskBar.tsx:7-19`) has no `role` member. Parser:
  `../web/src/components/shell/workspaceRole.ts:37-39` (least privilege).
- **Gap**: both chip renderers are structurally role-blind.
- **Seats**: client-architect (pass the server-derived role into both renderers;
  no client-asserted role — ADR-012 w14), ux-ui-designer (which entries
  disappear and whether the bar needs a different empty state),
  security-architect (**one line only** — confirm this is still not a control
  and the catalog stays un-gated).
- **ADR touchpoints**: `none` expected — ADR-022 S16-11 and ADR-024 w16 clause 2
  already rule it; the seats confirm rather than decide. Do **not**
  membership-gate `GET /api/capabilities` (w16 took that alternative
  deliberately; the route is tenant-free).
- **Design refs**: `ia-v2.md:92-115` (Ask intents / chips).
- **Acceptance**: A17-S3 — Procurement member does not see admin-gated chips;
  Admin does; `GET /api/capabilities` is byte-identical for both **and for no
  token**.
- **Proposed epic**: epic-20.

### NW-75 — Designed Savings section for tracked renewal actions

- **Source**: `inputs/next/w17-todo.md` §1; OQ-w16-ca-01's **product half**.
- **Raw**: "a designed Savings section for tracked actions — not an
  estimate-less pseudo-opportunity row. If OUT, record the ruling and do not
  mint a task."
- **Today (evidence)**: the precondition is **closed** — `buildTrackedOpportunityRow`
  is gone (repo-wide grep hits only `reports/**` and the raw file). Savings
  renders only real opportunities:
  `../web/src/routes/savings/savingsViewModel.ts:209-214` is a plain
  `opportunities.map(buildRealOpportunityRow)`;
  `../web/src/routes/savings/index.tsx:91-92` sources rows solely from
  `getSavingsOpportunities`. Renewal actions live on Renewals and Contract 360
  via the embedded `savedAction`
  (`../backend/src/Raffa.Api/RenewalsEndpointExtensions.cs:254,269`;
  `../web/src/routes/renewals/renewalPipelineViewModel.ts:46`;
  `../web/src/routes/contracts/contract360/index.tsx:122`).
  **The designed section does not exist** — status **OPEN**, precondition met.
- **Gap**: an open product question, not a defect. Whether Savings should show
  tracked renewal actions at all is product-owner's.
- **Seats**: product-owner (IN/OUT — decide first), ux-ui-designer (only if IN),
  client-architect (only if IN).
- **ADR touchpoints**: ADR-001 (scope), ADR-020 (a new section on screen 8) — or
  `none` if OUT.
- **Design refs**: `screens-v2.md:132-137` — the oracle's opportunities columns
  are Supplier · Action · **Estimate** · Status, so an estimate-less row is a row
  the oracle does not define (this is why the pseudo-row was retired).
- **Acceptance**: A17-S4 — only if IN; otherwise the ruling is recorded and no
  task is minted.
- **Proposed epic**: epic-20 (only if IN).

### NW-20 — Contract 360 `benchmark` and `activity` are always `[]`

- **Source**: `inputs/next/w17-todo.md` §2. Feeds NW-62.
- **Today (evidence)**: `../backend/src/Raffa.Api/ContractsEndpointExtensions.cs:353`
  (`benchmark = Array.Empty<object>(),`) and `:362`
  (`activity = Array.Empty<object>(),`) — unconditional literals; the comment at
  `:349-352` admits they are "R3/R4 placeholders". The API only re-serializes a
  domain result that is **also** hardcoded empty at its one construction site:
  `../backend/src/Raffa.Documents.Contracts/Application/Contract360QueryService.cs:211`
  and `:213` (one `new Contract360Result(` in the repo, `:201`). The row types are
  **memberless**: `Contract360Result.cs:216` (`public sealed record
  Contract360BenchmarkEntry;`) and `:223` — even if populated they could carry no
  data.
- **Gap**: two arrays that can never be non-empty, and two record types with no
  fields. The shape must be designed before it can be filled.
- **Seats**: software-architect (the payload and where the benchmark comes from),
  product-owner (what "activity" **means** for V1 — the raw file leaves it vague
  and there is no domain concept behind it).
- **ADR touchpoints**: amend ADR-024 or ADR-002 (the 360 payload); possibly
  `none` if the shape lands inside NW-62's decision.
- **Design refs**: `screens-v2.md:95-112`.
- **Acceptance**: N16 (with NW-22 / NW-62).
- **Proposed epic**: epic-21.

### NW-22 — Renewal insight `MarketPosition` stays null

- **Source**: `inputs/next/w17-todo.md` §2. Feeds NW-62.
- **Today (evidence)**: `../backend/src/Raffa.Renewals/Application/RenewalPipelineBuilder.cs:91-92`
  — `new RenewalInsightRecommendations(action, explanation, AnnualUpliftPercent:
  null, MarketPosition: null, PotentialSavingsRange: null)`, with the comment at
  `:89-90` ("Benchmark/Savings fields are always null this wave"). **All three**
  are hardcoded null; only `action`/`explanation` are computed (`:79`).
  Structurally the builder cannot do better: its constructor is
  `RenewalPipelineBuilder(RenewalEngine, IClock)` (`:22`) — no benchmark
  dependency — although the module's allow-list **does** permit `Benchmark`
  (`:16-20`). Contract side: `RenewalPipelineItem.cs:64-73`.
- **Gap**: a dependency that is architecturally legal but not injected.
- **Seats**: software-architect (inject and wire), product-owner (what an honest
  first-of-type band says).
- **ADR touchpoints**: `none` expected — ADR-002's allow-list already permits it;
  the seat records why.
- **Design refs**: `screens-v2.md:95-112`, `:123-130`.
- **Acceptance**: N16.
- **Proposed epic**: epic-21.

### NW-62 — Contract 360 must answer "where you can save" and "when you must move"

- **Source**: `inputs/next/w17-todo.md` §2. **`must`**.
- **Today (evidence)**: `buildAnswers` is
  `../web/src/routes/contracts/contract360/contract360ViewModel.ts:160-199`.
  `:173-176` reads `recommendations?.potentialSavingsRange ??
  SAVINGS_NOT_YET_AVAILABLE` and `recommendations?.marketPosition ?? upliftLever
  ?? LEVER_NOT_YET_AVAILABLE` — with NW-22's three nulls, **both are constants
  every time**, and the `upliftLever` fallback (`:169-172`) is dead for the same
  reason. Constants at `:150` and `:151-152`. Rendered by
  `AnswersBand.tsx:55-57`; the component even has an `is-prose` branch
  (`:33-35`) for the long gap sentence — the gap is a designed-for steady state.
- **⚠ raw-file correction 1** — "*When you must move*" is **not** broken the way
  the raw file states. It says the deadline/end date are "null even when the
  clause text already has 36-month term, 90-day notice". On this checkout those
  are **real persisted columns fed by extraction**:
  `Contract360QueryService.cs:86-98` reads `contract.EndDate` (`:94`) and
  `contract.CancellationDeadline` (`:96`);
  `Extraction/StagedExtractionService.cs:608-612` and `:622-626` set them from
  the `endDate` / `cancellationDeadline` facts; `Domain/Contract.cs:28` is a
  persisted `DateOnly?`. They are null **only when extraction missed the fact** —
  a data-quality gap, not a hardcoded null. So "when you must move" is
  **answerable today**, and item status is **PARTIAL**, not OPEN.
- **⚠ raw-file correction 2** — the raw file calls `WhyClauses.tsx` "an
  extraction dump". It is not: `WhyClauses.tsx:21-67` is a **curated clause
  list** (type, normalized value, source, risk, confidence) with a real empty
  state at `:32-36`. The actual field dump is a **different** component,
  `DetailsSection.tsx:35-41`/`:115-117` — which is **NW-65's** file. The two
  items must not be merged on a false premise.
- **⚠ raw-file gap — the answer already half-exists and nothing reads it**:
  `GET /api/contracts/{id}/strategy`
  (`../backend/src/Raffa.Api/InsightsEndpointExtensions.cs:64`) returns a
  `StrategyPack` with **`WhenYouMustMove` and `WhereYouCanPush`** sections
  (`../backend/src/Raffa.Insights/Contracts/StrategyPack.cs:26-31`, `:51-54`).
  It is already in the generated client (`schema.ts:928-929`, `getContractStrategy`
  at `:724`) and **no Contract 360 component imports it**.
- **The real blocker is a join key, not a missing service.** `IBenchmarkService`
  is real and registered (`../backend/src/Raffa.Benchmark/IBenchmarkService.cs:28`,
  `ServiceCollectionExtensions.cs:60`) with `MarketFeedBenchmarkAdapter` as the
  **default active** adapter (`../backend/src/Raffa.Market/Benchmark/MarketFeedBenchmarkAdapter.cs:61`,
  `:108`), mock-fed but with real matching and abstain paths, and a DB-backed
  path for persisted `market_record` rows (`:98-102`). `InsightsEndpointExtensions.cs:40-53`
  states the blocker verbatim: `BenchmarkQuery` needs a non-null supplier **name**
  and **geography**, and `Contract` has neither. **Half of that is now solved** —
  `ISupplierNameLookup` exists (`../backend/src/Raffa.SharedKernel/Suppliers/ISupplierNameLookup.cs:10`,
  implemented `../backend/src/Raffa.Suppliers.Products/Application/SupplierNameLookup.cs:15`,
  registered `Infrastructure/ServiceCollectionExtensions.cs:41`). **Geography is
  the remaining gap** → OQ-w17-004.
- **Gap**: "where you can save" is a permanent constant; the benchmark service
  and a strategy endpoint that answers both questions already exist and are
  unwired.
- **Seats**: product-owner (what an honest first-of-type band may claim; ADR-001
  §1.2 forbids a **paid** market API, and R3/R4 are gated on the interface +
  fixture adapter — so the band must be labelled representative),
  software-architect (the join key and whether 360 consumes `/strategy` or a new
  composition), client-architect (consume the server's answer, never recompute),
  ux-ui-designer (the answers band's real states — screens-v2 §5).
- **ADR touchpoints**: amend ADR-024 or ADR-001 (what the band may assert),
  ADR-002 (composition in the host).
- **Design refs**: `screens-v2.md:95-112` (Answers band).
- **Acceptance**: N16 — **Where you can save** and **When you must move** are
  concrete with citations; no "Not yet available" / "Not determined" while those
  facts are in the file.
- **Related, do not merge**: NW-20, NW-22 (wires); NW-57 (quote market compare) stays W18.
- **Proposed epic**: epic-21.

### NW-26 — Preview / evidence quality residuals (real page render)

- **Source**: `inputs/next/w17-todo.md` §2.
- **Today (evidence)**: `PlaceholderDocumentPreviewRenderer` is the **only**
  `IDocumentPreviewRenderer` — port at
  `../backend/src/Raffa.Documents.Contracts/Application/Preview/IDocumentPreviewRenderer.cs:19`,
  the single implementation at `Preview/PlaceholderDocumentPreviewRenderer.cs:26`,
  the single DI registration at
  `Infrastructure/ServiceCollectionExtensions.cs:127`. It passes bytes through
  only for an already-PNG document (`:41-45`) and otherwise paints a 480×640 card
  with the literal caption `"PREVIEW NOT RENDERED"` (`:66`). The fallback the raw
  file cites is `Preview/DocumentPreviewService.cs:53-54`
  (`?? PlaceholderDocumentPreviewRenderer.RenderPlaceholder("FILE")`). The raster
  engine is hand-rolled and says so: `Preview/PngImage.cs:22-24` — "A real
  first-page raster of a PDF needs a PDF rasteriser and is therefore NOT done
  here."
- **`GET /api/documents/{id}/preview` is one page, with no page parameter**:
  route `../backend/src/Raffa.Api/DocumentsEndpointExtensions.cs:93`, handler
  `:443-472` returning `image/png` (`:471`); storage is one hard-coded object per
  document — `../backend/src/Raffa.SharedKernel/Storage/DocumentStoragePath.cs:20-21`
  (`…/documents/{id}/preview/page-1.png`).
- **Gap**: a real rasteriser **and** a per-page path/route; NW-63 has nothing to
  overlay until both exist.
- **Seats**: software-architect (the renderer seam — `TryAdd` is already the
  seam — and the multi-page contract), cloud-architect **conditional** (only if
  the chosen renderer needs a new dependency, SKU or Document Intelligence call
  pattern; Document Intelligence is already on the account, ADR-017).
- **ADR touchpoints**: amend ADR-017 (if page rendering moves to
  `prebuilt-layout`), else `none — the seam already exists`.
- **Design refs**: `screens-v2.md:95-112`.
- **Acceptance**: N17 (with NW-63) — opening preview on a validated Northwind PDF
  shows **that file's page**, not a FILE placeholder.
- **Proposed epic**: epic-22.

### NW-63 — Document viewer: uploaded pages, OCR phrases highlighted and editable

- **Source**: `inputs/next/w17-todo.md` §2. **`must`**. **This is the largest
  item in the wave — see OQ-w17-001.**
- **Today (evidence)**: **no viewer exists.** `../web/package.json:19-25` lists
  exactly five runtime dependencies (`@azure/msal-browser`, `@azure/msal-react`,
  `react`, `react-dom`, `react-router-dom`) — **no PDF or viewer library**; a
  grep of `web/src` for `pdfjs|react-pdf|pdf-lib|<canvas|<iframe|getContext(`
  returns zero. `getDocumentPreviewUrl` has **zero component callers** — its only
  `web/src` hits are `../web/src/api/client.ts:418`, `:1275`, `:2082-2113` and the
  generated types.
- Highlighting is text-level `<mark>`:
  `../web/src/routes/contracts/contract360/ClauseHighlight.tsx:28-36` and
  `../web/src/routes/contracts/review/EvidencePane.tsx:141-160`.
- **Bounding boxes do not exist anywhere** — persisted model is
  `../backend/src/Raffa.Documents.Contracts/Domain/ExtractionEvidence.cs:46-48`
  (`SourceSpan` string, `SourcePage` int, `Confidence`); SQL is
  `Migrations/Scripts/documents-contracts.sql:747-763` (`source_span
  character varying(500)`, `source_page integer`), and a repo-wide grep of all
  `*.sql` for `bbox|bounding|polygon` returns **zero**. **A migration is required.**
- **And the geometry is dropped upstream, before it reaches us**: the AI-gateway
  wire contract keeps only page numbers and offsets —
  `../backend/src/Raffa.AiGateway/Foundry/Wire/DocumentIntelligenceContracts.cs:24-26`
  (`DocumentIntelligencePage(int PageNumber, IReadOnlyList<DocumentIntelligenceSpan>?
  Spans)`, no `words`, no `polygon`), and the domain type is
  `Contracts/AiOcrPage.cs:13` (`AiOcrPage(int PageNumber, string Text)`). Real
  boxes need `prebuilt-layout` **and** a widened gateway contract (ADR-017 `:103`
  records that `prebuilt-layout` "stays available on the account and is **not
  called in V1**").
- **"Editable OCR phrases" has no write target.** The existing correction write is
  field-name → scalar only: `PATCH /api/contracts/{id}`
  (`../backend/src/Raffa.Api/ContractsEndpointExtensions.cs:78`, handler `:367`),
  client `../web/src/api/client.ts:2403-2411`, caller
  `../web/src/routes/contracts/review/useReviewSession.ts:159-162`. There is **no
  `MapPatch`/`MapPut` on documents at all** (`DocumentsEndpointExtensions.cs:90-96`).
- **Gap**: four distinct missing pieces — a real page render (NW-26), page
  geometry through the gateway + schema, a viewer component with a new web
  dependency, and a write path for phrase edits.
- **Seats**: client-architect (the viewer, a new runtime dependency under
  ADR-012, and `client.ts` as a one-writer file), ux-ui-designer (viewer chrome,
  highlight affordance, the edit interaction), software-architect (gateway
  contract, evidence schema + migration, the phrase-edit endpoint).
- **ADR touchpoints**: amend ADR-012 (first new runtime dependency since the
  stack was set), ADR-017 (`prebuilt-layout` becomes called), ADR-003 (new
  evidence columns), ADR-027 (reprocess must re-derive boxes).
- **Design refs**: `screens-v2.md:95-112` (Why / citation landing).
- **Acceptance**: N17.
- **Related**: NW-26 **before** this; NW-55 (Ask citation deep-link) stays W18 —
  do not pull it in; NW-71 (auto-accepted phrases still correctable here).
- **Proposed epic**: epic-22.

### NW-64 — Fields OCR did not recover still appear in a section the user can fill

- **Source**: `inputs/next/w17-todo.md` §2.
- **Today (evidence)**: the row is skipped at
  `../web/src/routes/contracts/review/reviewViewModel.ts:244` —
  `if (currentValue === null && (proposedValue === null || proposedValue.trim()
  === "")) continue;` inside `buildReviewFields` (`:231-269`). `endDate` (`:82`)
  and `cancellationDeadline` (`:84`) are `required: false`, so an OCR miss makes
  them **vanish**. It is deliberate (`:189-191`) and **test-locked** —
  `../web/tests/routes/contracts/review/reviewViewModel.test.ts:347` — so the task
  must rewrite that test, not just the code. No recovery affordance exists
  downstream (`review/index.tsx:116-127`, `ReviewFieldList.tsx:38`).
- **⚠ raw-file sharpening**: the raw file says "360 `computeNeedsAttention`
  ignores `confidence: null`". True —
  `contract360ViewModel.ts:571-576` (`if (confidence === null) return;`, doc at
  `:564-566`) — but the **stronger** fact is that it never looks at header fields
  at all: `:578-580` iterate only products, clauses and obligations. A missing
  end date can therefore **never** surface there, at any confidence.
- **⚠ raw-file correction**: the "complains about missing `endDate`" text is in
  the **"When you must move"** cell, not "What to do" —
  `contract360ViewModel.ts:179`, `:187`, `:192`, rendered `AnswersBand.tsx:60-75`.
  "What to do" (`AnswersBand.tsx:77-98`) carries only the two buttons
  ("Start negotiation" / "Assign to me",
  `../web/src/routes/renewals/renewalPipelineViewModel.ts:165-166`). The
  substance of the complaint holds; the cell attribution does not.
- **Gap**: a hole the product knows about and hides.
- **Seats**: ux-ui-designer (the section and its copy — screens-v2 §4),
  client-architect (the rows and the write), product-owner (which canonical
  fields are required vs optional).
- **ADR touchpoints**: amend ADR-020 (a new section on the Review screen), else
  `none`.
- **Design refs**: `screens-v2.md:81-93`.
- **Acceptance**: N18.
- **Proposed epic**: epic-22.

### NW-65 — Details: only officialized facts; drop the "still need to decide" list

- **Source**: `inputs/next/w17-todo.md` §2. ADR-019 w14 footer §6 deferred the
  band reconciliation here; **NW-71 now owns the threshold**, this item owns the
  layout.
- **Today (evidence)**: `../web/src/routes/contracts/contract360/DetailsSection.tsx:51`
  opens `contract360-details-grid` (two columns —
  `contract360.css:391-394`, plus a full-width third child at `:96`/`:400-402`).
  Column 1 "Key terms" `:52-61`; column 2 "Documents" `:63-94`. The block to
  delete is **`DetailsSection.tsx:78`** — `<h6>Facts you still need to decide</h6>`
  — with "Review all →" at `:79-81` (keep) and rows at `:86-92`.
  Em-dash placeholders come from `contract360ViewModel.ts:375-400` (`:387`,
  `:391`, `:392`).
- **⚠ raw-file correction**: `NO_ATTENTION_MESSAGE` is at
  `contract360ViewModel.ts:586` and reads **"None — every fact is above 95% or
  signed off by you."** — `above 95%`, no space. And the **"or signed off by
  you" half is backed by no code at all**: `computeNeedsAttention` reads no
  human-decision state (acceptances live in `useReviewSession` /
  `document.validated`, never in `Contract360Body.tabs`). The screen asserts a
  fact the system does not hold — exactly what ADR-001 w14 forbids.
- **Threshold logic**: `contract360ViewModel.ts:571-576` pushes every candidate
  whose tag is not `neutral`, and `neutral` is issued only for `> 95`
  (`../web/src/styles/semantics.ts:34-35`) — so both the 80–95 and <80 bands are
  listed. Applied over products, clauses and obligations (`:578-580`).
- **Gap**: non-officialized facts on a page that should carry only officialized
  ones, plus a sentence that is untrue.
- **Seats**: ux-ui-designer (layout, the deletion, the no-sparse-hole rule),
  client-architect (the gate and the view model).
- **ADR touchpoints**: amend ADR-019 (its w14 footer §6 explicitly defers here)
  and ADR-020 (screen 5's Details).
- **Design refs**: `screens-v2.md:95-112` (Details ▾).
- **Acceptance**: N19.
- **Proposed epic**: epic-22.

### NW-66 — Why-clauses: no original quote on the row; specchietto; viewer link; leverage, not confidence

- **Source**: `inputs/next/w17-todo.md` §2.
- **Today (evidence)**: `../web/src/routes/contracts/contract360/WhyClauses.tsx`
  — row is type `:51`, normalized `:52`, source `:54`, risk tag `:55`,
  confidence tag `:56` (computed `:40`); click selects `:49` and renders
  `ClauseHighlight` at `:64`. The **source really can be a long quote and is
  rendered untruncated**: `contract360ViewModel.ts:361-367` joins `p.{page}` with
  the raw `row.sourceSpan` (`:365`, no cap) against a 500-char column
  (`Infrastructure/Configurations/ClauseConfiguration.cs:26`) — whereas the
  Review screen truncates the same kind of field at 60 chars
  (`../web/src/routes/contracts/review/ReviewFieldList.tsx:84`, `:92-96`).
  `riskLevel` is shown **raw**: `contract360ViewModel.ts:255-259` returns
  `label: riskLevel` (the backend enum string), unlike every other tag in
  `semantics.ts`.
- **No viewer link exists**: all seven `Link`/`href` in the `contract360/` folder
  go to `/renewals`, `/ask`, `/contracts/:id/review` or `/documents`;
  `ClauseHighlight.tsx` has no anchor at all.
- **Gap**: the row leaks the quote, shows a confidence %, and uses raw enum
  vocabulary instead of negotiation language.
- **Seats**: ux-ui-designer (the row, the specchietto, the legend, and the
  leverage vocabulary — never colour-only, ADR-019), client-architect (the
  render and the viewer deep-link), product-owner (the leverage words are
  product vocabulary).
- **ADR touchpoints**: amend ADR-019 (risk → plain-language tag mapping) and
  ADR-020.
- **Design refs**: `screens-v2.md:95-112` (Why).
- **Acceptance**: N20.
- **Proposed epic**: epic-22.

### NW-71 — A field extracted with confidence ≥ 90 % is accepted automatically

- **Source**: `inputs/next/w17-todo.md` §3; stakeholder 2026-09-13; HITL
  2026-09-10; ADR-019 w14 footer §6 (`:224-230`). **`must`.**
- **Raw**: the rule is **server + web**, and **90 % applies to every field, the
  five critical ones included** — no stricter band, no "always-review" list.
- **Today (evidence) — WEB**: `../web/src/styles/semantics.ts:32-41` still ships
  the old bands (`:34` `> 95` → Accepted, `:37` `>= 80` → Flagged, `:40` →
  Review), with `isConfidenceBlocking` at `:48-49` (`< 80`) and the stale rule
  restated in the module header `:11-12`. "Accepted" today is a **per-session
  React state**: `../web/src/routes/contracts/review/useReviewSession.ts:92`
  (`useState<ReadonlySet<…>>`) and `:184` — no network call; consumed at
  `reviewViewModel.ts:251`. Durability only via "Mark as validated":
  `reviewViewModel.ts:326-328` → `useReviewSession.ts:197-199` →
  `../web/src/api/client.ts:2481-2489` → `DocumentsEndpointExtensions.cs:96`
  (handler `:116-170`) → `DocumentValidationService.cs:108-135`.
  Stale legend copy also ships at `ReviewHeader.tsx:89,92,95` (asserted by
  `../web/tests/routes/contracts/review/ReviewRoute.test.tsx:492`).
- **⚠ raw-file correction (path)**: `acceptedThisSession` lives in
  `../web/src/routes/contracts/review/useReviewSession.ts`, not
  `web/src/routes/review/reviewViewModel.ts` — the review screen is under
  `routes/contracts/review/`.
- **Today (evidence) — BACKEND**: **no auto-accept exists at any threshold.**
  Greps for `autoAccept|auto_accept|AutoAccept` return no production hit (only a
  doc comment at `../backend/src/Raffa.AiGateway/Fixtures/FixtureContractFactExtractor.cs:44`).
  The nearest thing is a **local variable**, not a rule:
  `Extraction/StagedExtractionService.cs:509-510`.
- **⚠ raw-file correction (important)**: the raw file says
  `DocumentQueryService.cs:33,42` holds the bars "that only decide `needs_review`
  via `WeakFactCount`". The constants are at exactly those lines
  (`WeakFactThreshold = 0.6`, `CriticalWeakFactThreshold = 0.8`, selector
  `:46-52`, computation `:295-318`, consumed `:183`→`:201`→
  `DocumentsEndpointExtensions.cs:414`) — but they are **display-only and never
  touch `DocumentProcessingStatus`**. `needs_review` is decided by a **separate,
  duplicated** pair in `Extraction/StagedExtractionService.cs:863-884`
  (`DetermineDocumentStatus`; per-stage at `:419-422`; applied `:236`) and
  `Extraction/DocumentProcessingPipeline.cs:195`, `:378`. **Changing one pair
  silently desyncs the badge from the status** — that is the defect to avoid,
  and it is not in the raw file.
- **Threshold sprawl** — a single-threshold change touches **≥ 5 independently
  duplicated constants**, and both files document the duplication as deliberate
  (`DocumentProcessingPipeline.cs:107-113`, `DocumentQueryService.cs:28-31`):
  `StagedExtractionService.cs:78`, `:88`, `:509-510`, `:664`, `:720`, `:772`,
  `:826`, `:878`; `DocumentProcessingPipeline.cs:114`, `:195`, `:378`;
  `DocumentQueryService.cs:33`, `:42`, `:46-52`;
  `Admission/DocumentAdmissionOptions.cs:33` (**a different decision —
  admission**; see OQ-w17-003); `Raffa.Quotes/.../QuoteLineExtractionService.cs:48,87`
  (separate domain).
- **No persisted per-field decision state exists.** `ExtractionEvidence.cs:23-50`
  has no decision column; `documents-contracts.sql:747-763` confirms; the
  `document` table's only later added columns are `page_count` (`:834`),
  `preview_path` (`:841`), `rejection_reason`/`rejection_confidence` (`:880`).
  The sole record today is a comma-joined string in one `document.validated`
  audit row (`DocumentValidationService.cs:124-135`, design intent stated at
  `:36-39`). **A migration is required.**
- **OpenAPI exposes neither a decision nor a threshold**:
  `../web/openapi/raffa-api.v1.json:1132-1135` (`weakFactCount` is a bare
  integer), `:2334-2340` (raw `confidence`), `:1548-1553`/`:1603-1608`
  (`acceptedFields` free-form names).
- **✔ The operator stash exists and is usable.** `git -C .. stash list` →
  **`stash@{2}`** *"wip: review auto-accept and next-waves-todo (not w14)"*
  carries exactly the web half: `web/src/styles/semantics.ts`,
  `ReviewHeader.tsx`, `ReviewFieldList.tsx`, `reviewViewModel.ts`,
  `web/README.md` + three test files. Its diff introduces
  `AUTO_ACCEPT_MIN_PCT = 90` and collapses the bands to
  *≥ 90 → Accepted / below → Review*. (`stash@{3}` holds the same file set.)
  **Seed the web half from it; do not re-derive.**
- **Two sharp edges the council must rule on**:
  1. The stash compares the **rounded** percentage (`rounded >=
     AUTO_ACCEPT_MIN_PCT`), so `0.895` auto-accepts on the web while the raw
     file's server rule says `≥ 0.90`. Web and server would disagree at the
     boundary → **OQ-w17-002**.
  2. `FixtureContractFactExtractor.cs:49` `GoodConfidence = 0.9` renders
     **Flagged** today and flips to **Accepted** under the new band — demo
     fixtures change behaviour the moment the threshold moves.
- **Gap**: four distinct pieces — the server rule, the persisted per-field state
  (+ migration), the contract, and the web bands.
- **Seats**: product-owner (this supersedes spec §7.3 **and** the pilot's HITL
  band — see §6), software-architect (the rule, the schema, the retirement of the
  duplicated bars, and the badge/status resync), security-architect (the audit
  actor `system:<extraction>` under ADR-011 w16 clause 16, one row per document
  naming fields **never values**), client-architect (render the **server's**
  decision, never a client computation; `semantics.ts` is a one-writer file),
  ux-ui-designer (ADR-019's semantic rows, which its own w14 footer §6 declares
  stale and defers here).
- **ADR touchpoints**: amend ADR-019 (the confidence rows — deferred to this
  wave by name), ADR-003 (the new per-field decision column), ADR-011 (the audit
  row and actor), ADR-024 / ADR-012 (the contract exposes the decision).
- **Design refs**: `screens-v2.md:81-93` (Review; its confidence tip quotes the
  **old** bands verbatim and becomes stale copy this wave).
- **Acceptance**: A17-1 … A17-4 (raw §3).
- **Proposed epic**: epic-22.

---

### Items named by the raw file and deliberately **not** items here

- **NW-07, NW-08, NW-11, NW-12, NW-13, NW-21, NW-31, NW-32, W16-01** — delivered
  by w16 (PR #129, merged as `d3d2d24`). Raw §4 forbids reopening them.
- **NW-10** — CLOSED-ON-MAIN in w15; kept out.
- **NW-51** — design-alignment residual, not in the recorded W18 queue; not pulled in.

## 3. Seat roster for this wave

Derived from §2. **All seven seats are involved** — this is the first wave since
w14 where that is true, because the wave spans product vocabulary, a schema
change, an identity-adjacent CI right, a new web dependency and five screens.

| Seat | Involved | Items | Why |
|---|---|---|---|
| product-owner | **yes** | NW-72, NW-75, NW-20, NW-22, NW-23, NW-25, NW-62, NW-64, NW-66, NW-71 | NW-71 supersedes two product oracles (spec §7.3, pilot HITL band) — the largest product ruling of the wave. NW-75 is a pure IN/OUT. NW-20's "activity" and NW-62's claimable band have no definition yet, and ADR-001 §1.2 bounds what a market band may assert. |
| software-architect | **yes** | NW-72, NW-73, NW-20, NW-22, NW-23, NW-26, NW-62, NW-63, NW-71 | Two migrations (NW-71's per-field decision, NW-63's geometry), a widened AI-gateway contract, the benchmark join key, the renderer seam, and the retirement of ≥ 5 duplicated thresholds across three services. |
| cloud-architect | **yes (conditional)** | NW-73, NW-26 | NW-73 needs **one** new `azurerm_role_assignment` — today's Send grant is workload-identity-only (`infra/modules/servicebus/main.tf:71-73`), so the CI principal has none. NW-26 only if the chosen renderer needs a new dependency or SKU. If the council takes a renderer already on the account, this seat is NW-73-only. |
| security-architect | **yes** | NW-73, NW-71, NW-74 | NW-73 is the **owed** ruling from ADR-016 clause 31 (may a CI principal hold a topic-scoped Send right, and what actor the console writes). NW-71 adds an audit row with a `system:` actor. NW-74 is **one line** — confirm it is still not a control (S16-11). |
| client-architect | **yes** | NW-72, NW-74, NW-23, NW-25, NW-62, NW-63, NW-64, NW-65, NW-66, NW-71 | Ten items touch the SPA. NW-63 introduces the **first new runtime dependency** since ADR-012 was set. Three one-writer-per-phase files collide across items (§5). |
| ux-ui-designer | **yes** | NW-72, NW-75, NW-23, NW-25, NW-62, NW-63, NW-64, NW-65, NW-66, NW-71 | Five screens change. ADR-019's confidence rows are stale **by its own w14 footer §6**, which names NW-65 and now NW-71. screens-v2 §8 has no realized-money slot to put NW-72 in. |
| delivery-manager | **yes** | NW-73 | Owns the operator console and the CI-YAML set (ADR-016 clause 31 assigns it by name). Also owns the wave order: three `must` items and a binding cap. |

## 4. Proposed epics (append-only, next free numbers)

`Glob reports/workitems/epic-*` → 01…19 used; next free is **epic-20**. Epics
follow the raw file's own grouping (§6 themes A–D).

| Epic | Slug | Theme | Items | Extends |
|---|---|---|---|---|
| **epic-20** | w16-residuals | A · the four residuals the w16 table ruled into W17 | NW-72, NW-73, NW-74, NW-75 | epic-04 F02/F03 (savings), epic-13 F03 (capabilities), epic-18 F03 (the workflow set) |
| **epic-21** | contract-360-answers | B · 360 answers "save" and "move" | NW-20, NW-22, NW-62 | epic-02 (extraction/360), epic-03 (renewals), epic-04 F01 (benchmark), epic-07 F02 |
| **epic-22** | officialized-facts-and-viewer | C · viewer, OCR, missing fields, Details, Why, auto-accept | NW-26, NW-63, NW-64, NW-65, NW-66, NW-71 | epic-02 (extraction/evidence), epic-07 F01/F02 (360, review), epic-16 (the Worker/reprocess) |
| **epic-23** | portfolio-and-savings-filters | D · list filters | NW-23, NW-25 | epic-04 F03, epic-07 F03 |

**epic-23 exists only if its items survive the cap** (see §5) — both are `could`
and both are first to overflow. The decomposer should not mint it if neither
item lands.

## 5. Selection for this wave (cap 20 tasks / 5 phases)

Raw §0 is binding and this section applies it literally: every queued wave runs
in order; nothing moves to a later wave than the one it is already queued for; a
`must` is never queued beyond the next wave nor demoted without a written
reason; anything that does not fit becomes the **head** of W18, never its tail.

**The cap binds this wave.** Honest estimate for all fifteen items is **≈ 28–30
tasks** against a cap of 20 — NW-63 alone is ≈ 5–6 (a new web dependency, a
widened gateway contract, a schema migration, the viewer, the highlight, and a
phrase-edit write path) and NW-71 ≈ 4 (server rule, migration, contract, web).
The raw file's own overflow order is therefore applied.

- **In wave** (priority order — the four residuals are the head per raw §0.3,
  then the ordering constraints below):
  1. **NW-72** — Savings realized amount (`should`, head) — epic-20
  2. **NW-73** — bulk reprocess console (`should`, head) — epic-20
  3. **NW-71** — auto-accept ≥ 90 % (**`must`**) — epic-22 — *before NW-64/65/66*
  4. **NW-20** — 360 benchmark/activity payload (`should`) — epic-21
  5. **NW-22** — renewal market position (`should`) — epic-21
  6. **NW-62** — 360 answers (**`must`**) — epic-21 — *after NW-20/NW-22*
  7. **NW-26** — real page render (`should`) — epic-22 — *before NW-63*
  8. **NW-63** — document viewer (**`must`**) — epic-22
  9. **NW-64** — unrecovered fields section (`should`) — epic-22
  10. **NW-65** — Details: officialized only (`should`) — epic-22
  11. **NW-66** — Why-clauses (`should`) — epic-22

- **Queued** — **head of W18, in the raw file's own overflow order** (raw §5:
  "NW-75, then NW-23, then NW-25, then NW-74, then NW-73 — each becomes the
  **head** of W18, never its tail, and never displaces a queued W18 item"):
  1. **NW-75** (`could`) — and if the council rules it **OUT**, it leaves the
     live set entirely and is recorded, not queued.
  2. **NW-23** (`could`)
  3. **NW-25** (`could`)
  4. **NW-74** (`should`) — *reason for queuing, as raw §0.2 requires for a
     non-`must`*: it is the last `should` in the overflow order and the smallest
     item in the wave (one prop threaded into two components), so it is the
     cheapest thing to move and the only one whose deferral costs no other item.
     **It is not demoted** — it stays `should` and heads W18 behind the three
     `could`s only because the raw file fixes that sequence.

  **NW-73 is deliberately NOT cut**, even though it is expensive, because it is
  **last** in the raw file's overflow order and the four residuals are the
  wave's head (raw §0.3).

- **Out**:
  - **Delivered by w16, not reopened**: NW-07, NW-08, NW-11, NW-12, NW-13,
    NW-21, NW-31, NW-32, W16-01 (`d3d2d24`, PR #129).
  - **CLOSED-ON-MAIN earlier**: NW-10 (w15).
  - **W18 (unchanged)**: NW-30, NW-40, NW-41, NW-50, NW-55, NW-56, NW-57,
    NW-59, NW-60.
  - **Not in any queue — do not pull in**: NW-51.
  - **DEFERRED**: NW-52 (paid market API — ADR-001 §1.2 and the R3/R4
    fixture-adapter gate), NW-53 (mobile beyond the scaffold — ADR-013
    non-gating), NW-54 (extra roles in nav).

- **Everything in "In wave" is closable in this product.** No paid market API
  (NW-62's band comes from `IBenchmarkService`'s existing fixture/market-feed
  adapter and this tenant's corpus, labelled representative — ADR-001's R3/R4
  gate), nothing beyond the mobile scaffold, nothing in ADR-001 §1.2 non-goals.
  **Two Terraform-adjacent facts**: NW-73 needs **one** new role assignment, and
  nothing else in the wave opens `infra/`.

### Full remaining schedule (restated so nothing is dropped between runs)

Every wave below is executed, in order, and is the next run after the one before
it. W18 is `w16-requirements.md` §5 verbatim, plus this run's overflow at its
**head**.

| Wave | Queue (head first) | Change vs `w16-requirements.md` §5 |
|---|---|---|
| **W17 (this run)** | NW-72, NW-73, NW-71, NW-20, NW-22, NW-62, NW-26, NW-63, NW-64, NW-65, NW-66 | NW-75, NW-23, NW-25, NW-74 **overflow to the head of W18**. |
| **W18** | **NW-75, NW-23, NW-25, NW-74** (this run's overflow, head), then NW-30, NW-40, NW-41, NW-50, NW-55, NW-56, NW-57, NW-59, NW-60 | **+ four overflow items at the head.** The nine recorded W18 items are **unchanged and not displaced**. |

If the council splits NW-63 (OQ-w17-001), the **deferred half becomes the head of
W18 ahead of the four overflow items** — a `must`'s remainder outranks a
`should`/`could` overflow.

### Order constraints

1. **NW-71 before NW-64 / NW-65 / NW-66 — and it is a single-writer constraint,
   not a preference.** `getConfidenceTag` has **five** consumers:
   `../web/src/routes/contracts/contract360/WhyClauses.tsx:40` (NW-66),
   `.../contract360/FactTable.tsx:35` (Details), `.../contract360/contract360ViewModel.ts:574`
   (NW-65's `computeNeedsAttention`), `.../review/reviewViewModel.ts:282`, plus
   `isConfidenceBlocking` at `.../review/reviewViewModel.ts:294`. NW-71 rewrites
   the bands those five render.
2. **`web/src/styles/semantics.ts` is a one-writer-per-phase file — NW-71 owns
   it.** No other task may open it.
3. **`web/src/routes/contracts/contract360/contract360ViewModel.ts` is a
   one-writer-per-phase file.** **This collision is not in the raw file.** Three
   items edit it: NW-62 (`buildAnswers` `:160-199`), NW-65
   (`computeNeedsAttention` `:571-580`, `NO_ATTENTION_MESSAGE` `:586`) and NW-66
   (`formatSource` `:361-367`, `getClauseRiskTag` `:255-259`). **One task, or
   three different phases** — otherwise `check_single_writer.py` rejects the slice.
4. **NW-26 before NW-63** — the viewer overlays a real page.
5. **NW-20 / NW-22 before NW-62** — the answers consume those wires.
6. **`web/openapi/raffa-api.v1.json` — one task owns it per phase** (ADR-012 §3;
   `schema.ts` is regenerated wholesale). **Five items want it**: NW-71
   (per-field decision + threshold), NW-20/NW-22/NW-62 (360 / renewals payload),
   NW-26/NW-63 (preview pages / evidence geometry), NW-72 (only if the KPI shape
   changes), NW-23 (portfolio query — queued out). Five contract edits cannot
   each own a phase inside a 5-phase cap. **Recommended resolution, the same one
   w16 used: two contract tasks, not five** — one for epic-21 (NW-20/22/62) and
   one for epic-22 (NW-71 + NW-26/63), in different phases.
7. **`web/src/api/client.ts` is a one-writer-per-phase file** (ADR-012 w16
   clause 25 — hand-written glue, not generated). Writers this wave: NW-63
   (viewer/preview wrappers) and NW-72 (the realized-amount read, if it needs
   one). Same phase ⇒ one task.
8. **`DetailsSection.tsx` is NW-65's**; NW-64's new section is adjacent and must
   not be folded into it without a single-writer check.
9. **Two migrations, and they are independent** — NW-71's per-field decision
   column and NW-63's geometry columns both regenerate
   `documents-contracts.sql`. **One writer per phase on that script**, or one
   task owns both. ADR-021's `backend.yml` arrays already list
   `documents-contracts` (`:277`, `:309`), so **no CI YAML moves** — a
   `backend.yml` diff from a migration is a defect.
10. **NW-73 owns the CI-YAML set** — no other task may open `.github/workflows/**`
    (ADR-014 w16 clause 4: a feature wave does not edit CI YAML its plan did not
    name).

## 6. Superseded work items

| Existing item | Superseded by | Why |
|---|---|---|
| none | — | The raw file contains **no "cancels / replaces" statement about a work item**. No status banner is written this wave and no `status:` line becomes `superseded`. |

**What *is* superseded is two product oracles, on the record only** (`inputs/**`
is never edited — the same treatment ADR-001's w15 footer used for A7 and
R-DOC-05):

| Oracle | Line | Superseded by | Note |
|---|---|---|---|
| `inputs/product-spec.md` §7.3 confidence table | `:333-335` (`> 95%` auto-accept unless always-review; `80–95%` flag; `< 80%` human review) | **NW-71** | One threshold, 90 %, every field, no always-review list. |
| `inputs/percorso-pilota-v1.md` "Soglia HITL" | `:46` (`<80%`, critical stricter: value, cancellation, termination, renewal, uplift) | **NW-71** | The "critical stricter" clause is explicitly removed — 90 % applies to the five critical fields too. |

**One AC is *completed*, not superseded**: `E04/F03/US01` AC-1
(`reports/workitems/epic-04-savings-intelligence/.../us-01-savings-kpis.md:19`)
stays `active` and NW-72 discharges its realized half. **No banner, no
`superseded:` line.** ADR-001's w16 clause 4 money fence is **lifted** by NW-72
rather than violated — the council records that transition.

**Design-oracle staleness created by this wave** (record, do not edit the
export): `screens-v2.md:87-89`'s confidence tip quotes the old bands verbatim and
`:81` the "N facts below 80%" title — both become stale the moment NW-71 lands.
ux-ui-designer owns the disposition.

## 7. Open questions and assumptions in force

Also appended to `reports/open-questions.md`. **OQ-w17-001 is the only one that
can change the wave's shape.**

- **OQ-w17-001 — NW-63: does the viewer ship whole this wave?** Four pieces are
  missing and none exists today: a real page render (NW-26), page **geometry**
  through the AI gateway (`DocumentIntelligenceContracts.cs:24-26` drops
  `words`/`polygon`; ADR-017 `:103` records `prebuilt-layout` as "not called in
  V1"), evidence **columns** for boxes (zero `bbox|bounding|polygon` in any
  `*.sql`; migration required), a **viewer library** (`web/package.json:19-25`
  has none — the first new runtime dependency since ADR-012), and a **write
  target for phrase edits** (`PATCH /api/contracts/{id}` is field→scalar; there
  is no document PATCH at all). **Assumption in force**: NW-63 **splits** — W17
  ships the viewer over **real pages** (NW-26) with jump-to-page anchored on the
  **existing** `SourcePage` + text-level `SourceSpan`, and clause→page
  navigation from 360 and Review; **true bounding-box overlays and editable OCR
  phrases become the head of W18**, ahead of this run's four overflow items
  because a `must`'s remainder outranks a `should` overflow. If the council
  rejects the split, it must cut two `should` items instead and say which.
  client-architect + software-architect + ux-ui-designer + product-owner.
- **OQ-w17-002 — NW-71: raw `≥ 0.90` or rounded `≥ 90 %`?** The operator stash
  (`stash@{2}`) compares the **rounded** percentage, so `0.895` auto-accepts on
  the web; the raw file's server rule says `≥ 0.90`. Unreconciled, web and
  server disagree at the boundary and the screen contradicts the audit row.
  **Assumption in force**: the **server** decides with raw `>= 0.90` and
  persists the decision; the web **renders the server's decision and never
  recomputes it** (the raw file's own OpenAPI clause requires exactly this), so
  the stash's band function becomes presentation-only for the confidence
  *label*. software-architect + client-architect.
- **OQ-w17-003 — NW-71: which thresholds retire?** `0.6`/`0.8` are duplicated
  across **≥ 5** sites in three services, deliberately
  (`DocumentProcessingPipeline.cs:107-113`, `DocumentQueryService.cs:28-31`), and
  the `DocumentQueryService` pair is **display-only** while
  `StagedExtractionService.DetermineDocumentStatus` decides the actual status —
  so a partial change desyncs the badge from `needs_review`. **Assumption in
  force**: NW-71 retires the review bars as **one** definition consumed by both
  the badge and the status decision; `DocumentAdmissionOptions.AdmissionThreshold`
  (`:33`) is a **different decision** (admission — ADR-027 / ADR-024) and is
  **out of scope**; `Raffa.Quotes`' own bar is out of scope. software-architect +
  product-owner.
- **OQ-w17-004 — NW-62: where does benchmark *geography* come from?**
  `BenchmarkQuery` needs supplier **name** + **geography**
  (`InsightsEndpointExtensions.cs:40-53`). The name half is now solved
  (`ISupplierNameLookup`, registered). `Contract` has no geography.
  **Assumption in force**: geography is derived from the **workspace country**
  (the w14 profile field), used as the contract's market and **labelled
  representative**, never invented per contract and never a paid lookup
  (ADR-001 §1.2). If the council prefers a per-contract field, that is a schema
  change and NW-62 grows a migration. software-architect + product-owner.
- **OQ-w17-005 — NW-62: consume `/strategy` or build a parallel answer?**
  `GET /api/contracts/{id}/strategy` already returns `WhenYouMustMove` and
  `WhereYouCanPush` (`StrategyPack.cs:26-31`, `:51-54`) and is already in the
  generated client (`schema.ts:724`), with **no component consuming it**.
  **Assumption in force**: NW-62 **consumes the existing endpoint** rather than
  computing a second answer in the view model — two answer sources on one screen
  is the divergence ADR-012 forbids. software-architect + client-architect.
- **OQ-w17-006 — NW-72: which amount is "verified"?** `RealizedSavings.Amount`
  exists and is written (`SavingsOpportunityService.cs:325`) but never read;
  `SavingsOpportunityResult.RealizedAmount` is PATCH-only. **Assumption in
  force**: the KPI reads **`RealizedSavings` rows grouped by currency** (the
  audit-tracked record), `RealizedAmount` stays PATCH-only, and the stale
  comments at `SavingsKpiCalculator.cs:50-52` and `SavingsOpportunityStatus.cs:37-44`
  are swept by the same task. software-architect + product-owner.
- **OQ-w17-007 — NW-23 (queued): supplier category or contract type?**
  `Supplier.Category` **exists** (`Supplier.cs:36`, persisted
  `SupplierConfiguration.cs:32`), so `PortfolioFilter.cs:11-18`'s deferral
  comment ("Suppliers/Products … is still an empty scaffold") is **stale**;
  `Contract` has no category, only `ContractDocumentType` (`Contract.cs:21`).
  The architecture boundary still holds (`DependencyDirectionTests.cs:63`), so
  any join happens in `Raffa.Api` as `supplierName` already does
  (`PortfolioEndpointExtensions.cs:133-146`). **Assumption in force**: if NW-23
  lands, it filters by **supplier category** joined in the host, and
  screens-v2 §6's "Type" column stays the document type. **This OQ travels with
  the item to W18.** product-owner + software-architect.
- **OQ-w17-008 — NW-73: may a CI principal hold a Send right?** Pre-answered at
  the w16 table with an inclination, not a ruling (ADR-016 clause 31, ADR-022
  `:245-251`): *yes — Send only, topic-scoped, never `Manage`, never a SAS key*.
  Today's grant is workload-identity-only
  (`infra/modules/servicebus/main.tf:71-73`), so the assignment is genuinely new.
  **Assumption in force**: the inclination is adopted and the console writes
  actor `system:<component>` (ADR-011 w16 clause 16 / NW-32).
  security-architect + cloud-architect + delivery-manager.

### Operator prerequisite — not a task, and it will block the fan-out

**`reports/plan/gates/w16.hitl-ok` does not exist** (`reports/plan/gates/` holds
`e01…e13`, `readiness-gaps`, `w14`, `w15`), and
`scripts/check_slice_prereqs.py:329-338` requires `<previous>.hitl-ok`. With
`previous: w16`, **`run.ps1 -Slice w17` fails its prerequisites before fan-out**.
This is the **exact recurrence** of OQ-w16-008, which was raised for w15 and
fixed by an operator stamp rather than a rule. Stamp it at the w17 HITL gate:

```
python scripts/check_slice_prereqs.py --record-hitl w16
```

Accurate rather than a rubber stamp — w16 merged as PR #129 and every w16 area
was re-verified against this checkout (§1, §2). **Never a wave task.**
