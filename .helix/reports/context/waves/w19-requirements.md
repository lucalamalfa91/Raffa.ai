---
wave: w19
source: inputs/next/2026-09-16-ask-raffa-wow.md
source_sha256: "unavailable — no sha256sum/python in this harness at intake"
design_sources: []
baseline: "unavailable — no git in this harness at intake; read at the gate (ADR-014)"
generated: 2026-09-16T23:59Z
previous_wave: w18
caps: { max_tasks: 20, max_phases: 5 }
focus: "none"
---

# Wave w19 — normalized requirements (Ask Raffa wow answers)

Written by `next-intake`. Items keep source ids (`NW-NN`). Every claim about
"today" is re-audited against this checkout's code — not copied from the raw
file. The raw file's **nine product-owner locks (2026-09-16)** and its "still as
written" table (A–E) are binding and quoted, not re-opened.

> **SHA fields.** `bash` here has no `python`/`git`/`sha256sum`, so
> `source_sha256`/`baseline` cannot be computed. ADR-014: base SHA read at the
> gate; operator stamps at HITL.

## 1. Oracles in force

- **Previous wave**: w18 fully decomposed (`w18.yaml`, `w18-hitl.md`, epics
  23–26, **20 live tasks, 0 queued**). W18 items are **done — reuse, do not
  re-litigate**: NW-55/56/57/59/60, NW-63r, NW-23/25. Two W18 decisions still
  unwired are in scope and must be **finished, not redesigned**: the engine must
  consume `Conversation.ScopeContractId` (NW-76); Renewals must honour
  `/renewals?select=` (NW-84).
- **Binding locks (po 2026-09-16)**: (1) % vs representative market average
  ("media dei nostri dati di mercato"), never Renewals P75; (2) **option 3** —
  all Active(+validated) impacting 2026 costs, chat splits **two honest buckets**
  (actionable-in-2026 vs locked-for-2026); (3) price-in-line + worse-conditions
  listed ("prezzo ok, condizioni migliorabili"), no invented condition %; (4) Q1
  is workspace portfolio even from scoped 360; (5) Q2 = two-CTA card (360 +
  viewer-at-span); (6) Quote check = new market proposal only; (7) top 3 in chat,
  persist all to TODOs; (8) notice = date + "N days" only if N in cited clause,
  no new column for V1; (9) honesty of deductions on every answer.
- Product/ADR: `product-spec.md`; `requirements.md` R-SYS-02 (narrowed),
  R-CMP-01, R-PORT-01..03, R-STR-01..03, R-ASK-10; **ADR-024** (no tools/grounding
  on `answer`; retrieval/ranking/date/TODO host-side); ADR-011 (three corpora
  isolated); ADR-027 (validated-only).
- **Out of scope**: NW-23/25/74/30/40/41/50; function calling on `answer`; 360
  tracker; paid market API (NW-52); list-widget; persist follow-up chips;
  `cancellationNoticeDays` (R5a → W20); NW-75 (still OUT).

## 2. Items

All 22 items `must`; status OPEN (re-audited).

| ID | Title | Area | Seats | Acc |
|---|---|---|---|---|
| NW-76 | Engine consumes `scopeContractId` per turn | backend | sw, client | scope |
| NW-77 | 360 global bar passes origin scope | web | client, ux | chip |
| NW-78 | Persistent contract-binding chip | web | client, ux | chip |
| NW-79 | Intent lexicon covers demo phrasings | backend | sw | planner |
| NW-80 | Supplier/contract resolution not exact-cap-only | backend | sw | resolve |
| NW-81 | Tenant RAG filters to a contract | backend | sw | rag |
| NW-82 | Ask uses benchmarked priced-lines as `/strategy` | backend | sw | parity |
| NW-83 | Citations carry id/documentId/page/span | backend/web | sw, client | cite |
| NW-84 | Renewals honours `?select=` | web | client | select |
| NW-85 | Renewals negotiation TODO list | backend/web | sw, client, ux, sec | todos |
| NW-86 | Unscoped portfolio market → not Quote check | backend | sw, po | Q1 |
| NW-87 | Candidate set = Active+validated impacting 2026 | backend | sw | cand |
| NW-88 | Mal-position % pure calculator | backend | sw | pct |
| NW-89 | Chat lists above-average + worse-conditions, 2 buckets | backend/web | sw, client, ux | list |
| NW-90 | Follow-up "contrattare su X" writes TODOs | backend/web | sw, client | follow |
| NW-91 | Notice questions structured pack, not RAG | backend | sw | notice |
| NW-92 | Notice days = date + days-if-in-span | backend | sw | days |
| NW-93 | Jump to notice phrase (two-CTA card) | web | client, ux, sw | jump |
| NW-94 | Notice fallbacks never ask "which supplier" scoped | backend/web | sw, client | fb |
| NW-95 | Q3 plans `RenewalStrategy` | backend | sw | Q3 |
| NW-96 | `NegotiationPointRanker` grounded points only | backend | sw | rank |
| NW-97 | Q3 persist + Renewals deep-link | backend/web | sw, client, ux | persist |

### NW-76 — engine consumes `scopeContractId`

- **Today**: `AskAsync` at `AskCopilotService.cs:125` = `(tenantId, question,
  recentTurns, userId, cancellationToken)` — no scope; `ConversationsEndpointExtensions.cs:295`
  calls without scope; `CreateAsync` stores it (`:162`) but engine drops it.
- **Must**: pass scope into `AskAsync`; deixis ("this supplier/contract") = that
  id; scoped id wins over same-name; unseen id → 404/N11 refusal. **Lock 4**:
  `PortfolioMarketPosition` still ranks workspace portfolio even scoped.
- **Acc**: scoped Q2 never abstains "which supplier". ADR-024.

### NW-77 — 360 global bar passes origin scope

- **Today**: `GlobalAskBar.tsx` submit = `navigate("/ask", {state:{query,newChat}})` —
  no `scope`; notice chip hard-codes "this supplier".
- **Must**: from `/contracts/:contractId`, create conversation with
  `scopeContractId` = that id. Seats client, ux. ADR-012.

### NW-78 — persistent contract-binding chip

- **Today**: after first turn URL `/ask/:id` with no visible contract;
  `buildScopeLine(validatedContractCount, [])`.
- **Must**: while scoped, show `{supplierName} · {type}` → `/contracts/{id}`,
  survives resume. Seats client, ux. ADR-024.

### NW-79 — intent lexicon covers demo phrasings

- **Today**: `IntentPlanner.cs:43` `market|mercato|…` → `:102` QuoteRoute before
  `risparm*`; "notice"/"preavviso" not structured; `contrattare`/`rinnovo` miss
  `RenewalStrategyPattern`.
- **Must**: planner test table (IT+EN) for the three screenshot sentences; no
  phrasing falls to unscoped Clause RAG when facts exist. Seat sw. ADR-024.

### NW-80 — supplier/contract resolution not exact-capitalized-only

- **Today**: `DomainGate` needs capitalized run == `Supplier.Name`; "AsterCloud
  GmbH" → `NeedsDocument`; multi-contract picks first portfolio row.
- **Must**: exact → normalized/contains; multi-contract scoped id else soonest
  deadline, named pack item, never merge. Seat sw. ADR-024.

### NW-81 — tenant RAG filters to a contract

- **Today**: `EmbeddingRetrievalService.SearchAsync` = cosine top-K over **all**
  tenant embeddings; `SourceType`/`SourceId` unused at query time.
- **Must**: `EmbeddingSearchQuery` with `contractId`; "this contract" slice +
  "similar types" slice (same `ContractDocumentType`/category, lower K, labelled
  peer); market stays on `IMarketKnowledgeRetrieval`. Seat sw. ADR-024/011.

### NW-82 — Ask uses benchmarked priced-lines as `/strategy`

- **Today**: `/strategy` is async benchmarked; Ask strategy path uses sync
  `ToPricedLines` → bands null.
- **Must**: async `ToPricedLines` + `BenchmarkKeyResolution` (supplier, workspace
  country); same numbers as 360 `/strategy`; missing band → "insufficient market
  data". Seat sw. ADR-024.

### NW-83 — citations carry id/documentId/page/span

- **Today**: `CopilotReplyBuilder` `DocumentId` = citation key, `ContractId` null;
  `BuildContractFactItem` `Page`/`PreviewUrl` null, `Href` = `/contracts/{id}`.
- **Must**: real `contractId`+`documentId`+`page`+span/clause so W18 viewer opens
  at span. **Lock 5**: two-CTA card (360 + viewer-at-span). Seats sw, client.
  ADR-024/012.

### NW-84 — Renewals honours `?select=`

- **Today**: `CapabilityRouting.BuildHref` emits `/renewals?select={id}`;
  `renewals/index.tsx:48` uses `useState`, never reads query.
- **Must**: `useSearchParams().get("select")` selects row; invalid → default, no
  500. Seat client. ADR-012.

### NW-85 — Renewals negotiation TODO list

- **Today**: **doesn't exist** — `RenewalNegotiationTodo`/`negotiation-todos`/
  `NegotiationPointRanker` grep zero. Reuse neither `RenewalAction` nor
  `ContractNegotiationStep`.
- **Must**: entity `(tenant_id, contract_id, point_key)` topic/rank/current/
  target/rationale/citation_keys/source=ask/status/timestamps; host upsert after
  rank before answer; idempotent (same key updates, never un-ticks Done, vanished→
  Superseded); `GET /api/renewals/{id}/negotiation-todos` + tick PUT; audit
  `renewal.negotiation_todos_written`, actor = token subject, RLS. Seats sw,
  client, ux, sec. ADR-003/009/011. Does not cancel NW-75 (still OUT).

### NW-86 — unscoped portfolio market → not Quote check

- **Today**: `mercato` → QuoteRoute; tests lock it; QuoteRoute short-circuit in
  `AskCopilotService`.
- **Must**: screenshot Q1 → new `AskIntent.PortfolioMarketPosition` answered in
  Ask; single-supplier "is X above market?" stays `MarketCompare`. **R-SYS-02
  narrowed (LOCKED)**: Quote check = new market proposal only. ADR-024 amendment.
  Seats sw, po.

### NW-87 — candidate set = Active + validated impacting 2026

- **Today**: `GetPortfolioAsync(..., PortfolioFilter.None, pageSize=100)`; ranks
  criticality top-5, not above-market.
- **Must**: validated **and** `Status="Active"`; **option 3 (LOCKED)** — all
  Active impacting 2026 costs (paying in 2026 / driving spend), not "renewal ∈
  2026 only"; ended 2025-12-31 (still Active) out; 2025–2028 term (still paying)
  in; starts 2026-06-01 in. Bound N×lines; batch, no unbounded N+1. Seat sw.
  ADR-027/024.

### NW-88 — mal-position % pure calculator

- **Today**: `BenchmarkMarketPositionPercent` always null; `AboveBandLineFraction`
  hard-null; do not reuse Renewals `DetermineMarketPosition` (unit-price band
  mismatch).
- **Must**: `linePct = (unitPrice − media)/media × 100` (media = P50/median);
  listed iff any line >0 **or** worse condition (comparable fact); contract % =
  spend-weighted avg of positive linePcts; `HasSufficientData=false` → omit %;
  `actionableIn2026` flag + short why from stored facts, never model invention.
  Seat sw. **Acc**: NumericGuard; no `"n/a"` currency.

### NW-89 — chat lists above-average + worse-conditions, two buckets

- **Must**: two buckets — (a) **actionable in 2026** ("su quali posso lavorare
  per risparmiare nel 2026", TODOs/Renewals link); (b) **impacts 2026 but locked**
  (listed if above-market or prezzo-ok/condizioni-migliorabili, says 2026 cash
  committed, 360 link only, no 2026-save TODO). Rows: supplier·type, X% above
  media when band exists, one-line why, `/contracts/{id}`. Price-in-line +
  worse-conditions listed, no invented %. Follow-up chips per actionable supplier.
  Seats sw, client, ux. ADR-024.

### NW-90 — follow-up "contrattare su X" writes TODOs

- **Today**: RenewalStrategy pack = calculator snippets, no tenant evidence; Ask
  never reads `RenewalActionService` (R-STR-03).
- **Must**: resolve X (NW-80); multiple X → spell out, never merge; top-3 in chat
  (lock 7), persist all to TODOs (no cap at 3); "Open in Renewals →"
  `/renewals?select={id}`; locked rows never promise 2026 save / never 2026-save
  TODO. `persistTodos=true`. Seats sw, client. ADR-024/028.

### NW-91 — notice questions structured pack, not RAG

- **Today**: "notice" not structured → Clause RAG over 37 contracts;
  `BuildContractFactItem` knows "notice by {date}" but is never called.
- **Must**: notice/preavviso/disdetta → structured notice pack (endDate/
  cancellationDeadline/autoRenewal/renewalTermMonths + `WhenYouMustMove` +
  evidence); RAG only fallback filtered to contract (NW-81). Seat sw. ADR-024.

### NW-92 — notice days = date + days-if-in-span

- **Today**: `Contract` has `CancellationDeadline`, no `CancellationNoticeDays`;
  fixture regex captures N then discards.
- **Must (floor)**: answer the **date**; "N days" only if N in cited clause; never
  model-subtract. R5a persist-days = could/overflow (W20). Seat sw. ADR-024.

### NW-93 — jump to notice phrase (two-CTA card)

- **Today**: no viewer deep-link from a fact citation (NW-83 blocks).
- **Must**: two-CTA card (open 360 + open viewer at notice span). Seats client,
  ux, sw. ADR-024/012.

### NW-94 — notice fallbacks never ask "which supplier" scoped

- **Must (5 cases)**: deadline+evidence → answer+deep-link; deadline no-span →
  answer date + Review CTA; no deadline, clause text → quote + recovery Review;
  neither → abstain naming this contract, recovery 360 Review; unscoped deictic →
  abstain + Portfolio recovery. Seats sw, client. ADR-024.

### NW-95 — Q3 plans `RenewalStrategy`

- **Today**: `contrattare`/`rinnovo` miss `RenewalStrategyPattern`; fallback
  Clause RAG → abstain "no prossimo rinnovo".
- **Must**: Q3 → `RenewalStrategy` named AsterCloud; corpora calc+tenant(+market
  +raffa Renewals); missing deadline = named point, not abstain. Seat sw. ADR-024.
  **Acc**: `kind=answer` ranked, not abstain.

### NW-96 — `NegotiationPointRanker` grounded points only

- **Today**: `WhereYouCanPush` always emits 7 levers incl. ungrounded generics; no
  clause/SLA/liability ranking.
- **Must**: point = topic/rank/current/target/whyItMatters/citationKeys/strength;
  only grounded points; order (above-band price → uncapped/high liability →
  auto-renew+short notice → SLA/credits → term/volume → payment terms); chat cap
  top 3, persist all. Shared `BuildNegotiationPointsPackAsync(contractId,
  includeRenewalUrgency, persistTodos)`. Insights `[SharedKernel, Benchmark]`.
  Seat sw. ADR-024.

### NW-97 — Q3 persist + Renewals deep-link

- **Today**: `CopilotReplyBuilder` strips actions on abstain; no server-injected
  Renewals navigate action.
- **Must**: after rank, upsert all todos; answer = top-3 markdown + citations +
  server-injected `/renewals?select={guid}` (never model `actionKeys`). Seats sw,
  client, ux. ADR-024/028. **Acc**: ask → top-3 → click Renewals → full TODO list;
  re-ask = no duplicate Open, Done preserved.

## 3. Seat roster

| Seat | Involved | Items | Why |
|---|---|---|---|
| software-architect | yes | 76–83, 85–97 | Scope, planner, RAG, priced-lines, citations, candidate+% calc, notice pack, ranker, TODO API. |
| client-architect | yes | 76,77,78,83,84,85,89,90,93,94,97 | Scope chip, binding chip, `?select=`, citation deep-link, TODO UI, two-CTA, follow-up/persist. |
| ux-ui-designer | yes | 77,78,85,89,93,97 | Binding chip, TODO surface, two-bucket narration/cards, two-CTA, point layout. |
| product-owner | yes | 86 | R-SYS-02 amendment (already locked) — one scope ruling. |
| security-architect | yes | 85 | RLS + write actor for the TODO table. |
| cloud-architect | no | — | No `infra/`/SKU/apply. `PASS`. |
| delivery-manager | no | — | No CI-YAML/promotion. `PASS`. |
| council-gate | yes | all | Verifies and closes. |

## 4. Proposed epics (next free: 27–32)

| Epic | Slug | Theme | Items |
|---|---|---|---|
| epic-27 | ask-bind-contract-and-intent | scope + lexicon + resolution | NW-76,77,78,79,80 |
| epic-28 | ask-retrieval-numbers-citations | RAG + priced-lines + citations | NW-81,82,83 |
| epic-29 | renewals-negotiation-write-back | TODO list + select | NW-84,85 |
| epic-30 | ask-q2-notice-wow | notice flow | NW-91,92,93,94 |
| epic-31 | ask-q3-strategy-wow | strategy flow | NW-95,96,97 |
| epic-32 | ask-q1-portfolio-wow | portfolio mal-position | NW-86,87,88,89,90 |

## 5. Selection for this wave (cap 20 tasks / 5 phases)

All 22 items `must`. Binding order (§8): **shared primitives (A+B+C) before the
three flows; Q2 smallest end-to-end once scope binds; Q3 reuses C; Q1 heaviest.**
Overflow → **head of W20**, never a silent drop, and a `must` is never demoted
without a written reason.

- **In wave** (build sequence): NW-79 (lexicon/planner) → NW-76/77/78/80 (scope +
  resolution) → NW-82/81 (priced-lines + RAG) → NW-83 (citations) → NW-85/84
  (TODO + select) → **Q2** NW-91/92/93/94 → **Q3** NW-95/96/97 → **Q1**
  NW-86/87/88/89/90.

**Budget note.** 22 `must` items, honest estimate ≈ 35–45 tasks (NW-85 ≈ 4,
NW-87/88/89 ≈ 2 each, NW-96 ≈ 2, NW-81/82 ≈ 1–2 each) against a cap of **20**.
The cap physically binds. The raw's own build sequence places **Q1 last and calls
it "the heaviest"**, so the written reason a `must` needs to leave the wave is the
physical cap: **if the cap binds, the Q1 flow (NW-86/87/88/89/90) overflows to the
head of W20**, never the tail, and is not demoted (it stays `must` in W20). Shared
primitives (A+B+C) and Q2 (the smallest end-to-end wow) and Q3 are sequenced first
so they land this wave. The decomposer applies the 20-task cap against this order.

### Remaining schedule

| Wave | Queue (head first) |
|---|---|
| W19 (this run) | A+B+C (NW-76..85), Q2 (NW-91..94), Q3 (NW-95..97), Q1 (NW-86..90) |
| W20 | any `must` overflow (Q1 flow first, if it did not fit), then R5a persist-days if it ever lands |

### Order constraints

1. **A+B+C before D/E/F** (§8 binding) — the flows fan out only after scope/lexicon/
   retrieval/TODO primitives land.
2. **NW-83 before NW-93** (two-CTA needs real citation ids) and before NW-89/NW-97
   (cards/deep-links).
3. **NW-85 before NW-90/NW-97** (TODO write-back is consumed by both flows).
4. **NW-82 before NW-90/NW-96** (benchmarked priced-lines feed strategy/points).
5. **NW-79 before NW-86/91/95** (intent routing must reach the target intents first).

## 6. Superseded work items

**None.** No "cancels / replaces" statement about a work item; no status banner,
no `superseded` line. **R-SYS-02 is narrowed** (NW-86, ADR-024 amendment, locked) —
recorded on the record, `inputs/**` never edited. NW-75 stays OUT. W18 items are
**done/reused**, not superseded.

## 7. Open questions and assumptions

- **OQ-w19-001 — baseline SHA.** No git/python here; `baseline` unstamped.
  **Assumption**: operator reads base at the gate and merges `origin/main` before
  fan-out (W19-A1, as w18/w17 gates did).
- **OQ-w19-002 — cap vs 22 `must` items.** The 20-task cap physically cannot hold
  all 22 `must` items (which are multi-task). **Assumption**: the raw's own build
  sequence is the demotion reason — Q1 (heaviest, last) overflows to the **head of
  W20** and is **not demoted** (stays `must`). The decomposer applies the cap; if it
  lands inside the cap, no overflow. Neither the shared primitives nor Q2/Q3 may
  overflow (they unblock and are the smallest end-to-end wows).
