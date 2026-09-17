# Ask Raffa — next-waves input · W19 wow answers

Status: **binding input** for the wave after W18. Written 2026-09-16 from a
live `dev` walk of three Ask Raffa questions on workspace **LUCA TEST SRL**
(37 contracts). Wave 18 is treated as **already landed**. This file does not
re-derive W18 bodies. Product-owner locks of **2026-09-16** (§0 table +
§7) override earlier “recommended defaults” in this file; they are
binding, not HITL guesses. Later the same day the product owner locked
**option 3** for the 2026 listing (two honest buckets) and a
**communication principle** for every Ask wow answer; those refine
lock 2 and add lock 9.

| | |
|---|---|
| Previous wave | **w18** — citation cards, scoped `/ask?scope=`, abstain recovery, quote-check benchmark-first, viewer overlay. Treat as **done**. |
| This work | Ask Raffa must become the product feature: evidence-based savings, a bound notice date, a real negotiation strategy that writes Renewals. |
| Product oracles | `inputs/product-spec.md`, `inputs/requirements.md`, ADR-024, this file |
| Demo oracles | the three questions in §1 (Italian + English, as asked) |

Ask Raffa is the heart of the web app. A correct abstain on the wrong pack is
still a failed product. The buyer must be able to say: **it works, it saves
me money**.

---

## 0. Binding instructions

1. **W18 is done.** Do not re-queue or re-litigate NW-55 (citation cards),
   NW-56 (360 “Ask about it” `/ask?scope=`), NW-57 (Quote check as market
   assessment), NW-59 (abstain has a next step), NW-60 (hide bar on `/ask`),
   NW-63 remainder (viewer box), NW-23 / NW-25 (list filters). **Reuse**
   those primitives. Two W18 *decisions* that are still unwired are in
   scope here and must be **finished, not redesigned**: the engine must
   consume `Conversation.ScopeContractId`; Renewals must honour
   `/renewals?select=`.
2. **ADR-024 stands.** The `answer` role has **no tools and no grounding**.
   Retrieval, ranking, date math, and TODO writes are **host-side** in
   `AskCopilotService` / `Raffa.Api`. The model **narrates a pack** the
   calculators already ranked. Do not add function-calling for these
   flows.
3. **Deterministic numbers.** Mal-position % is vs the **representative
   average of our market corpus** (“media dei nostri dati di mercato”),
   never Renewals P75. Notice-by date, days left, opening / range /
   walk-away come from calculators and `PackValue`s. NumericGuard must
   still pass. Never let the model subtract dates or invent a
   percentile. Insufficient sample → **omit** the %, never invent one.
   Honesty of classified vs unclassifiable claims is lock 9.
4. **Three corpora stay isolated** (ADR-024 / ADR-011). Tenant RAG, market
   notes (`IMarketKnowledgeRetrieval`), numeric bands (`IBenchmarkService`)
   are packed as separate items. Never join tenant chunks with
   `market_embedding`.
5. **Persistence.** If Ask writes a negotiation TODO, another browser must
   read it back from Postgres (RLS). Browser storage is not a system of
   record.
6. **This wave is Ask wow.** Portfolio-wide mal-position, scoped notice,
   supplier-scoped renewal strategy. Overflow becomes the **head of W20**,
   never a silent drop. Items marked `must` in §2–§5 are never demoted
   without a written reason.
7. **Do not** change the 360 four-step tracker (`Notify → … → Sign`). That
   is the process checklist. Negotiation TODOs live on **Renewals**.
8. **Quote check vs Ask (LOCKED).** Quote check is for a **new market
   proposal**: the user uploads a quote/doc and asks “is this proposal
   in line with the market?” — then comparisons to see if it is in line
   or can be improved. Q1-style “which of MY contracts are poorly
   positioned / where can I save in 2026” is **answered in Ask**, never
   routed to Quote check. Quote check does **not** disappear; it is not
   the handler for portfolio mal-position (R-SYS-02, narrowed in NW-86).
9. **Honesty of deductions (LOCKED, all Ask wow answers).** Ask Raffa
   must **always** be clear and honest about what is a **stored fact**,
   what is a **calculator deduction**, and what **cannot be claimed**.
   When a situation has materially different cases (actionable vs
   locked, price-ok vs conditions-worse, date known vs days only if
   cited, insufficient market data, several contracts for one
   supplier), the **answer must spell out the distinction**, not
   collapse it into one optimistic sentence. The same standard applies
   to **this requirements file**: do not leave case splits as unnamed
   defaults; write the cases with the same concreteness as the 2026
   table. Do not let the model imply savings, notice dates, or
   negotiation wins that the pack did not classify. Persona /
   `Prompts/answer` narration follows this lock.

### Locked 2026-09-16

Product owner locked the nine rows below. They **override** earlier
recommended defaults in this file. Implementation must follow them;
do not re-open them as HITL. Later the same day, lock 2 was refined
to **option 3** (2026 listing with two honest buckets) and lock 9
(communication principle) was added; do not revert lock 2 to an
undifferentiated “all active that impact 2026” list.

| # | Decision | Locked |
|---|---|---|
| 1 | What is “average” / mal-position % | vs the **representative average of our market corpus** — today the fixture / mocked `IBenchmarkService` bands, tomorrow the same interface backed by third-party APIs. Product language is “media dei nostri dati di mercato”. Implementation may map “media” to the adapter’s central statistic (typically P50 / median of the band). **Do not use Renewals P75 “above market” as the Q1 gate.** Insufficient sample → omit, never invent a %. |
| 2 | Anno 2026 candidate set | **Option 3 (BINDING).** Show **all Active (+validated) contracts that impact 2026 costs** (you are paying them in 2026 / they drive 2026 spend) — not “renewal date ∈ 2026 only”, not a mere date-overlap story. Expired / inactive / ended-in-2025 are **out**. Chat **always distinguishes two buckets**, honest wording, never a single undifferentiated list: **(a) Actionable in 2026** — a lever this year is still open (notice window still open, renewal/end in 2026, or mid-term renegotiation is a **stored fact**). These are “su quali posso lavorare per risparmiare nel 2026”. **(b) Impacts 2026 spend but 2026 is already locked** — still listed if above-market or prezzo-ok / condizioni-migliorabili, but Raffa must say the 2026 cash is already committed (notice passed + auto-renew, or next negotiation is after 2026). Do **not** imply the buyer can still save 2026 money on those rows. Price-in-line + worse conditions stays listed (lock 3), narrated “prezzo ok, condizioni migliorabili”. Cases: running since 2024, renewal Jan 2026, notice still open → list, **actionable**; same but notice passed + auto-renew → list if above-market, **locked for 2026**, say so; 2025–2028 term, next renewal 2028, still paying 2026 → list if above-market, **locked for 2026** unless mid-term renegotiation is a stored fact; ends 2025-12-31 still Status=Active → **out** (does not impact 2026 costs); starts 2026-06-01 → in, actionable if not already closed. Pack: `actionableIn2026: true / false` plus a short why (“notice by 18 Oct 2026” vs “auto-renew already triggered; next window 2027”). TODOs / Renewals link for **actionable** rows; locked rows may link to 360, must not promise a 2026 save. |
| 3 | Price in-line, conditions worse | **List it.** Narrate: the price is in line, but some conditions can be improved (cite which). **No invented condition %.** Do not hide these behind a price-only gate. |
| 4 | Q1 asked from an open contract 360 | Always the **workspace portfolio**. “quali contratti” is a portfolio question even if the chat was opened from one contract. |
| 5 | Q2 notice jump from chat | Card in chat with **two actions**: open 360 **and** open the viewer **already at the right place**, without searching the document. Not viewer-only, not 360-only (NW-83 / NW-93). |
| 6 | Quote check vs Ask | Quote check = **new market proposal** (upload + “is this proposal in line with the market?”). Portfolio mal-position / 2026 savings is **Ask**, never Quote check. Quote check does **not** disappear. |
| 7 | How many negotiation points in chat | **Top 3.** Max 3 grounded points in the chat answer (the strongest). Full/remaining points still persist to the Renewals TODO list (do not cap the TODO list at 3 unless the ranker only produced 3). |
| 8 | Notice days vs date | **Date + “N days” only if N is in the cited clause.** Do not invent days. Do not require a new `cancellationNoticeDays` column for V1 wow. R5a persist-days is **could / overflow**, not should-blocking. NW-92 must-floor = date + days-if-in-span. |
| 9 | Honesty of deductions (all Ask wow answers) | **Always** be clear and honest: what is a **stored fact**, what is a **calculator deduction**, what **cannot be claimed**. Materially different cases must be **spelled out** in the answer (actionable vs locked, price-ok vs conditions-worse, date known vs days only if cited, insufficient market data, several contracts for one supplier) — never collapsed into one optimistic sentence. Same standard for **this requirements file**. Do not let the model imply savings, notice dates, or negotiation wins the pack did not classify. Applies to Q1, Q2, and Q3. Persona / pack narration (`Prompts/answer`) follows this lock. |

---

## 1. The three questions (what you see today)

Live answers on `dev`, workspace LUCA TEST SRL. These sentences are the
acceptance oracles. The product must answer **these phrasings**, not only
the golden-set paraphrases.

### Q1 — portfolio mal-position / 2026 savings

**Asked:** *“quali contratti sono mal posizionati sul mercato? su quali
posso lavorare per risparmiare un po di soldi sull'anno 2026?”*

**Where:** Ask Raffa, unscoped (Ask home / global composer). Same
portfolio question if asked from an open contract 360 — **always the
workspace portfolio** (lock 4).

**What you see:** no ranking. RAIFFA replies with the hard-coded Quote
check routing copy (“That's a job for Quote check…”) and an orange
**Quote check** button. No contract list, no %, no Portfolio links, no
savings. **No model ran.**

**Root cause (as of this checkout):** `IntentPlanner` matches `mercato`
first → unscoped `AskIntent.QuoteRoute` (R-SYS-02). `risparm*` never
runs. Even if routing were fixed, `BuildPortfolioStrategyPackAsync`
ranks **criticality top-5**, not above-market %, and Ask never calls
`IBenchmarkService` / `BenchmarkKeyResolution` for a portfolio list.

**What it should be:** recover **all Active (+ validated) contracts that
impact 2026 costs** in this workspace — **option 3 (LOCKED):** you are
paying them in 2026 / they drive 2026 spend (not “renewal date ∈ 2026
only”, not a mere date-overlap story). Ends 2025-12-31 still
Status=Active → **out**. Starts 2026-06-01 → **in**. Then price and
conditions vs the representative average of our market corpus
(`IBenchmarkService`; today fixture / mocked bands) → list contracts
**above that average**, each with a mal-position **%**, **and**
contracts whose price is in line but conditions can be improved (cite
which; no invented condition %; narrate “prezzo ok, condizioni
migliorabili”). Insufficient sample → omit that contract’s %, never
invent one. Link each listed contract in Portfolio / 360.

In the chat answer, **always distinguish two buckets**, honest wording,
not a single undifferentiated list (lock 2 / lock 9):
- **Actionable in 2026** — there is still a lever this year (notice
  window still open, renewal/end in 2026, or mid-term renegotiation is
  a **stored fact**). These are “su quali posso lavorare per
  risparmiare nel 2026”. Follow-up: *“su quali punti posso contrattare
  sul supplier X”* → **top 3** grounded points in chat, **write all**
  ranked points onto the Renewals negotiation TODO for that contract,
  link to `/renewals?select={id}`.
- **Impacts 2026 spend but 2026 is already locked** — still listed if
  above-market or prezzo-ok / condizioni-migliorabili, but Raffa must
  say clearly that the 2026 cash is already committed (notice passed +
  auto-renew, or next negotiation is after 2026). 360 link is fine; do
  **not** promise a 2026 save, and do **not** attach a 2026-savings
  Renewals TODO.

Cases the spec keeps: running since 2024, renewal Jan 2026, notice
still open → list, **actionable**; same but notice already passed,
auto-renew → list if above-market, **locked for 2026**, say so;
2025–2028 term, next renewal 2028, still paying 2026 → list if
above-market, **locked for 2026** unless mid-term renegotiation is a
real stored fact; starts 2026-06-01 → in, **actionable** if not already
closed.

### Q2 — notice on the open contract

**Asked:** *“When must we give notice to this supplier?”*

**Where:** started from the **open contract** screen (Contract 360). Chip
text is the hard-coded c360 suggestion.

**What you see:** pink **Cannot determine reliably**. The model says
notice depends on which supplier/contract, and that the pack does not
state which one “this supplier” is.

**Root cause:** the 360 global bar navigates to `/ask` with `{ query }`
and **no `scope`**. `AskCopilotService.AskAsync` does not take
`conversation.ScopeContractId` even when it is stored. `"this supplier"`
is lowercase → DomainGate extracts no name. `"notice"` is not a
structured keyword → `AskIntent.Clause` → tenant-wide RAG over all 37
contracts → GroundingGuard abstains on an ambiguous pack. `EndDate` /
`CancellationDeadline` / `AutoRenewal` already exist on the contract and
were never packed.

**What it should be:** chat is bound to **that** contract (chip + link).
Raffa reads expiry + notice metadata, answers a **clear calendar date**
(and “N days” **only if N is in the cited clause**; miss consequence).
Honesty (lock 9): the date is pack-backed; days are not invented; miss
vs passed vs no auto-renew is **spelled out**, not collapsed. In chat,
a **two-CTA card**: open 360 **and** open the viewer already at the
notice span, without searching the document.

### Q3 — AsterCloud next-renewal negotiation points

**Asked:** *“sul contratto di AsterCloud GmbH quali sono i maggiori punti
su cui posso contrattare nel prossimo rinnovo?”*

**Where:** Ask Raffa, supplier named in the question.

**What you see:** a plausible-looking abstain. It found the Software
Subscription Agreement / CT-01, then refused to name major points
because the pack has no “prossimo rinnovo” section. No TODOs. No
Renewals link.

**Root cause:** Italian `contrattare` / `rinnovo` miss
`RenewalStrategyPattern` (`approach|negozia*|strategia|…`). Router has
English `renew` only. Fallback = Semantic → `AskIntent.Clause` →
unfiltered tenant RAG. `GET /api/contracts/{id}/strategy` and
`StrategyPackBuilder` already exist and **were not used**. Abstain is
honest on the wrong evidence.

**What it should be:** resolve AsterCloud → load **stored metadata** →
RAG this contract + similar types + market → ranked negotiation
**strategy** on critical points → chat narrates the **top 3** grounded
points → **upsert all** ranked points onto Renewals TODOs for that
contract (do not cap the TODO list at 3) → chat link to open that
Renewals row. Honesty (lock 9): only pack-ranked grounded points;
insufficient market data stays named; do not imply a negotiation win
the pack did not classify. If several AsterCloud contracts, spell out
which one (NW-80), never silently merge.

---

## 2. Shared primitives (must) — unblock all three flows

These are not optional scaffolding. Q1, Q2, and Q3 all fail without them.

### NW-76 — Ask engine consumes `scopeContractId` on every turn (must)

- **Status:** OPEN. W18 stored the column and the create path; the engine
  still ignores it.
- **Where:** `AskCopilotService.AskAsync`,
  `ConversationsEndpointExtensions.AskAndAppendAsync` (~294),
  `RoutingContext.ContractId`.
- **Today:** `POST /api/conversations/{id}/messages` body is `{ question }`
  only. Host loads `conversation.ScopeContractId` and drops it.
  `AskAsync(tenantId, question, recentTurns, actor)` has no scope
  parameter. `RoutingContext.ContractId` is filled only from a **named**
  supplier.
- **Must:** pass `conversation.ScopeContractId` into `AskAsync`. When set
  and visible to the caller: skip unnamed-supplier portfolio packing
  for deixis / this-contract questions; treat deixis (`this supplier`
  / `this contract` / `it` / `questo contratto`) as that id;
  `RoutingContext.ContractId` = that id. Follow-ups stay scoped. Two
  contracts, same supplier: **scoped id wins**, never `FirstOrDefault`
  by name. Unseen id → 404 / W18 N11 refusal, no pack leak.
  `CreateAsync` must authorize the contract in the host.
  **Exception (lock 4):** a `PortfolioMarketPosition` question
  (“quali contratti” / workspace mal-position / 2026 savings) still
  ranks the **workspace portfolio** even if the chat was opened from
  one contract. Scope remains for deixis and for Q2/Q3; it does not
  shrink Q1.
- **Acceptance:** with scope set, Q2 never abstains with “which
  supplier/contract”. Unscoped Q2 on Ask home may still abstain, with a
  Portfolio / “open a contract” recovery — never a fake named supplier.
- **Seats:** software-architect, client-architect.

### NW-77 — Global Ask bar on Contract 360 passes origin scope (must)

- **Status:** OPEN. This is the Q2 screenshot path. W18 covered the
  header “Ask about it” button, **not** this chip.
- **Where:** `GlobalAskBar.tsx`, `askSuggestions.ts` (`c360Chips`),
  `askViewModel.createConversationAndAsk`.
- **Today:** chip “When must we give notice to this supplier?” is
  hard-coded `"this supplier"` because the bar does not fetch the open
  contract. Submit is `navigate("/ask", { state: { query, newChat: true } })`
  — **no `scope`**.
- **Must:** from `/contracts/:contractId` (header **and** global bar
  chips **and** typed query), the new conversation is created with
  `scopeContractId` equal to that id. Documents “Ask about it” already
  sends `?scope=`; keep it. Portfolio / Ask-home chips remain unscoped.
- **Acceptance:** on 360, clicking the notice chip creates a conversation
  whose GET payload has `scopeContractId` = the open contract. Typing
  the same sentence in the bar on that screen does the same.
- **Seats:** client-architect, ux-ui-designer.

### NW-78 — Chat shows a persistent contract-binding chip (must)

- **Status:** OPEN.
- **Where:** `web/src/routes/ask/index.tsx`, conversation detail wire
  (`scopeContractId` already on GET).
- **Today:** after the first turn the URL is `/ask/:id` with **no**
  visible contract. Heading still uses the portfolio line
  (`buildScopeLine(validatedContractCount, [])`).
- **Must:** while `scopeContractId` is set, the thread shows a chip:
  `{supplierName} · {type}` linking to `/contracts/{id}`. Survives
  resume (rebuild from conversation detail + 360 header). Closing the
  chip is out of scope (“+ New chat” starts unscoped).
- **Acceptance:** above the Q2 thread the open contract is named and
  clickable, even if the conversation title stays the question.
- **Seats:** client-architect, ux-ui-designer.

### NW-79 — Intent lexicon covers the demo phrasings (must)

- **Status:** OPEN.
- **Where:** `IntentPlanner.cs`, `AskRaffaQueryRouter.cs`,
  `IntentPlannerTests`.
- **Today:**
  - Q1: `mercato` → QuoteRoute before `risparm*` → PortfolioStrategy.
  - Q2: `"notice"` / `"preavviso"` / `"disdetta"` are not structured
    keywords → Clause RAG.
  - Q3: `contrattare`, `punti`, `rinnovo` miss `RenewalStrategyPattern`
    (`approach|affrontare|negotiat*|negozia*|strategia`). Router has
    English `renew` only. Golden-set Italian that *does* hit strategy is
    a different sentence.
- **Must:** planner test table (IT+EN) for the three screenshot
  sentences and close paraphrases. See NW-86 / NW-91 / NW-95 for the
  target intents. Do not rely on the model to recover a missed intent.
- **Acceptance:** `IntentPlannerTests` rows for the three oracle
  sentences. No screenshot phrasing falls through to unscoped Clause
  RAG when the needed facts exist.
- **Seats:** software-architect.

### NW-80 — Supplier / contract resolution is not exact-capitalized-only (must)

- **Status:** OPEN.
- **Where:** `DomainGate.ExtractSupplierCandidate`,
  `AskCopilotService.BuildInDomainReplyAsync` (`FirstOrDefault` by
  supplier name), `SupplierNameNormalizer`.
- **Today:** gate requires a capitalized run **equal** to `Supplier.Name`.
  “AsterCloud” vs “AsterCloud GmbH” → `NeedsDocument`. Follow-up “su
  salesforce” (lowercase) may miss. Multi-contract same supplier picks
  the first portfolio row, not the soonest renewal, and not the scoped
  id.
- **Must:** exact, then normalized / contains match. Multi-contract:
  scoped id wins; else soonest cancellation deadline / renewal (same
  sort as `RenewalPipelineBuilder`) **and** a named pack item
  (“Using {Type} CT-01 (renews …). Ask if you meant another.”). Never
  silently merge. Follow-up chips insert the **catalog** capitalized
  name.
- **Acceptance:** “sul contratto di AsterCloud GmbH …” resolves CT-01
  (or the soonest AsterCloud contract) without `NeedsDocument`. Unknown
  supplier stays `NeedsDocument` + upload.
- **Seats:** software-architect.

### NW-81 — Tenant RAG can filter to a contract (must)

- **Status:** OPEN.
- **Where:** `EmbeddingRetrievalService.SearchAsync` (tenant + top-K
  only). `SourceType` / `SourceId` exist on `Embedding` and are unused
  at query time.
- **Today:** Clause pack is cosine top-K over **all** tenant embeddings.
  Q2 and Q3 therefore mix 37 contracts.
- **Must:** overload or `EmbeddingSearchQuery` with `contractId` /
  source ids. “This contract” slice = only that contract’s chunks.
  “Similar types” slice = other validated contracts with same
  `ContractDocumentType` and/or `Supplier.Category`, lower top-K,
  labelled peer. Market notes stay on `IMarketKnowledgeRetrieval` with
  category / geography filters — not mixed into tenant pgvector.
- **Acceptance:** unit tests: this-contract search does not return
  another supplier’s MSA unless explicitly in the peer slice and
  labelled peer.
- **Seats:** software-architect.

### NW-82 — Ask uses the same benchmarked priced-lines as `/strategy` (must)

- **Status:** OPEN. Host wiring exists; Ask drifts.
- **Where:** `InsightsEndpointExtensions.ToPricedLines` (async, with
  `IBenchmarkService` + `BenchmarkKeyResolution`),
  `AskCopilotService.BuildRenewalStrategyPackAsync` and
  `BuildMarketComparePackAsync`.
- **Today:** GET `/api/contracts/{id}/strategy` is benchmarked. Ask
  strategy path uses **sync** `ToPricedLines` → bands always null →
  targets abstain, seven generic levers still emitted. MarketCompare
  uses `GoverningLaw` as geography, max 2 lines, not workspace country.
- **Must:** Ask composition calls **async** `ToPricedLines` with
  `BenchmarkKeyResolution` (supplier name, workspace country). Same
  numbers as the 360 strategy GET. Missing band → “insufficient market
  data”, never an invented percentile. Provenance on every market
  number (representative · adapter · n · as of).
- **Acceptance:** named-supplier strategy / market-compare numbers in
  the Ask pack match `GET /api/contracts/{id}/strategy` opening / range
  / walk-away when bands exist.
- **Seats:** software-architect.

### NW-83 — Citations carry real `contractId` / `documentId` / page / span (must)

- **Status:** OPEN. Blocks Q2 jump-to-clause and weakens Q1/Q3 cards.
- **Where:** `CopilotReplyBuilder` (`DocumentId` = citation key,
  `ContractId` always null), `AskCopilotService.BuildContractFactItem`
  (`Page`/`PreviewUrl` null, `Href` = `/contracts/{id}` only),
  `askViewModel.buildTenantCitationHref`.
- **Today:** W18 viewer route `/documents/:id/viewer?page=&clause=`
  cannot be built from a fact citation. Client only appends `?page=`
  onto a 360 href.
- **Must:** notice / clause / evidence citations include real
  `contractId`, `documentId` (evidence `SourceDocumentId` or clause),
  `page` + span / clause id so the viewer can open **already at the
  span**. `href` = W18 viewer with page + clause when a clause id
  exists; else viewer `?page=` + evidence highlight; else 360
  `?clause=` / `?page=` as the 360 half of the pair. Calc citations
  for Q1 set `href` = `/contracts/{id}` and `contractId`.
- **Q2 primary UX (LOCKED):** a **two-CTA card** — open 360 **and**
  open the viewer already at the notice span, without searching the
  document. Not viewer-only, not 360-only. Citation ids must be
  sufficient for both CTAs (NW-93).
- **Acceptance:** Q2 card offers both CTAs; the viewer CTA opens the
  source page with the notice wording highlighted (W18 box); the 360
  CTA opens that contract. Q1 cards open the listed contract. No
  `"No page preview available"` as the only treatment.
- **Seats:** software-architect, client-architect.

### NW-84 — Renewals honours `?select=` (must)

- **Status:** OPEN. W18 / catalog already **emit** the href.
- **Where:** `CapabilityRouting.BuildHref` → `/renewals?select={id}`;
  `web/src/routes/renewals/index.tsx` keeps selection in `useState` and
  **never reads** the query param.
- **Must:** `useSearchParams().get("select")` selects that row and
  shows its insight pane (and TODOs once NW-85 lands). Invalid /
  other-tenant id: no leak, fall back to current default (top row)
  without a 500.
- **Acceptance:** opening `/renewals?select={guid}` from chat selects
  that contract. Second browser not required for the query itself;
  TODOs in NW-85 are.
- **Seats:** client-architect.

### NW-85 — Renewals negotiation TODO list (must)

- **Status:** OPEN. **Does not exist.**
- **Where:** new `RenewalNegotiationTodo` in `Raffa.Renewals`;
  `RenewalNegotiationTodoService`; RLS migration; GET (and tick PUT);
  `InsightCard.tsx` / `NegotiationTodoList.tsx`.
- **Today:** `RenewalAction` is one owner/status/action string per
  contract. `ContractNegotiationStep` is the closed 4-step 360
  checklist — **do not reuse either**. Insight pane = recommended
  action + Start negotiation / Assign.
- **Must:**
  - Entity keyed `(tenant_id, contract_id, point_key)` with
    `topic`, `rank`, `current`, `target`, `rationale`, `citation_keys`,
    `source = ask`, `status` Open / Done / Superseded, timestamps.
  - Host upsert **after ranking, before the answer call** (so the
    deep-link is true). Not a model tool.
  - Idempotency: same `point_key` updates current/target/rationale/rank;
    **does not** un-tick a human Done. New keys insert. Keys that
    disappeared become `Superseded`, not deleted.
  - `GET /api/renewals/{id}/negotiation-todos` (web needs a GET; Ask
    writes in-process). Tick PUT mirrors `POST /api/renewals/{id}/action`
    roles (Procurement / Admin).
  - Audit e.g. `renewal.negotiation_todos_written`. Actor = token
    subject. RLS.
  - Renewals pane renders the list. Chat confirms they were saved;
    repeat ask says they are already on the list.
- **Acceptance:** after Q3 (and Q1 follow-up), GET returns the written
  todos; a second browser agrees. Done ticks survive a second ask.
- **Cancels / does not cancel:** does **not** cancel NW-75 (designed
  Savings section for tracked renewal actions — still OUT per ADR-001
  w17 clause 7). These TODOs are negotiation points, not a realized
  savings estimate.
- **Seats:** software-architect, client-architect, ux-ui-designer,
  security-architect (RLS + write actor).

---

## 3. Flow Q1 — portfolio mal-position / 2026 savings (must)

### NW-86 — Unscoped portfolio market questions are not Quote check (must)

- **Status:** OPEN. Contradicts current R-SYS-02 as encoded in
  `IntentPlanner` + tests. **R-SYS-02 narrowed LOCKED 2026-09-16.**
- **Where:** `IntentPlanner`, `AskIntent` (new
  `PortfolioMarketPosition`), `AskCopilotService.BuildInDomainReplyAsync`
  QuoteRoute short-circuit. ADR-024 amendment, not a silent hack.
- **Today:** unscoped `market|mercato` → QuoteRoute. Tests lock it:
  `A_benchmark_question_with_no_named_supplier_routes_to_quote_check_instead_of_narrating`.
- **Must:**
  - Screenshot Q1 and close paraphrases (IT+EN: “quali contratti” /
    “which contracts” + mal posizionat* / above market / too expensive
    + optional risparm* / year) → **`PortfolioMarketPosition`**,
    **answered in Ask**. Never QuoteRoute. This is a **workspace
    portfolio** question even if the chat was opened from one contract
    360 (lock 4).
  - Single-supplier “is X above market?” stays `MarketCompare`
    (R-CMP-01).
  - **Narrowed R-SYS-02 (LOCKED):** Quote check is **only** for a
    **new market proposal**: the user uploads a quote/doc and asks
    “is this proposal in line with the market?” — then comparisons to
    see if it is in line or can be improved. Q1-style “which of MY
    contracts are poorly positioned / where can I save in 2026” is
    **never** routed to Quote check. Quote check does **not**
    disappear; it is not the handler for portfolio mal-position.
  - Empty-above-market (data existed) is an honest sentence + Portfolio
    action, **not** Quote check. Empty/invalid portfolio → upload
    invite (R-ASK-10), not a fake ranking.
- **Acceptance:** screenshot string → `PortfolioMarketPosition` (also
  when asked from a scoped 360). Planner table locks the narrowed
  R-SYS-02 remainder (new-proposal Quote check only). Golden: fixture
  workspace with ≥2 active contracts that impact 2026 costs, one above
  the corpus average and **actionable in 2026**, one above-average but
  **locked for 2026**, one below → Italian answer lists **the above**
  (and any price-in-line / worse-conditions rows per NW-88), **split
  into the two buckets** (NW-89), with that contract’s % when a band
  exists, citation href `/contracts/{id}`. Does not send the user to
  Quote check. Does not narrate the locked row as a 2026 save.
- **Seats:** software-architect, product-owner (R-SYS-02 amendment
  already locked).

### NW-87 — Candidate set = Active (+ validated) that impact 2026 costs (must)

- **Status:** OPEN. **Candidate set LOCKED 2026-09-16 — option 3.**
- **Where:** `AskCopilotService.AskAsync` uses
  `PortfolioQueryService.GetPortfolioAsync(..., PortfolioFilter.None,
  pageSize=100)`. `CountValidatedContractsAsync` unused here.
  `PortfolioFilter.Status` already exists. `RenewalFrom`/`RenewalTo`
  only auto-renewing rows.
- **Must:** Ask does **not** rank processing shells (ADR-027). Filter
  validated (same definition as `CountValidatedContractsAsync`) **and**
  `Status = "Active"`. **Anno 2026 option 3 (LOCKED):** include **all
  Active (+ validated) contracts that impact 2026 costs** (you are
  paying them in 2026 / they drive 2026 spend) — not “renewal date ∈
  2026 only”, not a mere date-overlap story. Expired / inactive are
  out. Do not silently drop non-auto-renewing active contracts that
  still cost in 2026. **Impact-2026 cases (write them, do not leave as
  unnamed defaults):**
  - Running since 2024, renewal Jan 2026 → **in** the candidate set
    (notice still open or already passed does not change inclusion).
  - 2025–2028 term, next renewal 2028, still paying in 2026 → **in**.
  - Starts 2026-06-01 → **in**; **actionable** if not already closed.
  - Ends 2025-12-31 still `Status=Active` in DB → **out** (does not
    impact 2026 costs).
  Listing / mal-position gate is NW-88. How the answer **narrates**
  the set as two honest buckets (`actionableIn2026` vs locked-for-2026)
  is NW-89 — never collapse that split here into one undifferentiated
  “all active that impact 2026” list. Document or raise the 100-row
  cap; bound N × lines (raise `MaxPricedLinesForBenchmark` with a
  documented cap, or fail visibly). Batch; do not N+1 360+benchmark
  unbounded on the Ask turn.
- **Acceptance:** a processing shell is never listed. An expired /
  inactive contract is never listed. A 2025-12-31 end still marked
  Active is never listed. An active contract that still drives 2026
  spend can be listed if above the corpus average (or price-in-line /
  worse-conditions per NW-88), even when its renewal date is not in
  2026 — including a 2028 next-renewal row that is **locked for 2026**.
  Timeout / cap is visible, not a hang.
- **Seats:** software-architect.

### NW-88 — Mal-position % is a pure calculator (must)

- **Status:** OPEN. Closest unused field:
  `RenewalPriorityInputs.BenchmarkMarketPositionPercent` (always null
  at composition). `AboveBandLineFraction` is hard-null in
  `ToCriticalityInputs`. Do **not** reuse Renewals
  `DetermineMarketPosition` as-is (it compares **AnnualSpend** to a
  **unit-price** band). **Average + list-gate LOCKED 2026-09-16.**
- **Must:** named helper, Appendix C rule 6. Product language is
  “media dei nostri dati di mercato”: the % is vs the
  **representative average of our market corpus** — today the fixture
  / mocked `IBenchmarkService` bands, tomorrow the same interface
  backed by third-party APIs. Implementation may map “media” to the
  adapter’s central statistic (typically P50 / median of the band).
  **Do not use Renewals P75 “above market” as the Q1 gate.** Renewals
  P75 labels stay on Renewals.
  1. Per priced line with sufficient band:
     `linePct = (unitPrice - media) / media * 100` where `media` is
     that adapter central statistic.
  2. Contract is **listed** iff at least one line has `linePct > 0`
     (strictly above the corpus average) **or** a condition is worse
     than market notes when both sides have a comparable fact (notice,
     uplift cap, payment terms, liability). **Price-in-line + worse
     conditions: list it (LOCKED).** Narrate that the price is in
     line and that some conditions can be improved (cite which).
     Inventing a condition % is forbidden. Do not hide these behind a
     price-only gate.
  3. Contract-level % = spend-weighted average of **positive**
     linePcts. One number in the pack (`PackValueKind.Percentage`).
     Price-in-line / worse-conditions rows still list; they do **not**
     get an invented condition %.
  4. `HasSufficientData == false` → **omit** the % (and omit from the
     above-average list if there is also no comparable condition
     fact); never invent a %. Optional trailing “could not score N
     contracts” + Review action (NW-59 pattern).
  5. Conditions contribute to the **why** snippet when a market note
     has a comparable fact.
  6. **Honesty flag (LOCKED option 3 / lock 9).** Each **listed**
     contract carries `actionableIn2026: true / false` plus a short
     why from stored facts or calculator dates, never model invention
     (“notice by 18 Oct 2026” vs “auto-renew already triggered; next
     window 2027”). `true` iff a lever this year is still open: notice
     window still open, renewal/end in 2026, **or** mid-term
     renegotiation is a **real stored fact**. `false` when 2026 cash
     is already committed (notice passed + auto-renew, or next
     negotiation after 2026) and no stored mid-term-renegotiation
     fact. Do **not** invent mid-term renegotiation. Price-in-line +
     worse-conditions rows still list (lock 3) and still get this
     flag. The model must not imply a 2026 save on `false` rows.
- **Acceptance:** unit tests vs known corpus-average/unitPrice; thin
  sample → omit; NumericGuard: answer containing `18%` fails if pack
  has no `18` percentage. A price-in-line / worse-conditions fixture
  is listed with a condition citation and **no** invented %. Currency
  is real ISO, not `"n/a"` (`GAP-ASK-SPEND-CURRENCY-NA` on this path).
  Fixtures classify `actionableIn2026`: notice-open Jan-2026 renewal
  → true; notice-passed auto-renew → false; 2025–2028 term next
  renewal 2028 → false unless a stored mid-term-renegotiation fact.
- **Seats:** software-architect.

### NW-89 — Chat lists above-average contracts and worse-condition contracts, with Portfolio links (must)

- **Status:** OPEN. **List-gate LOCKED 2026-09-16** (locks 1 and 3).
  **Two-bucket 2026 narration LOCKED** (lock 2 option 3 / lock 9).
- **Where:** pack composition + `ReplyBody` / W18 `CitationCard` /
  `ActionRow`. No new chat widget required for V1 if calc citations
  carry `href` + % . Pack/UI hint: each listed contract already has
  `actionableIn2026` + why (NW-88); chat **groups or labels** the two
  buckets. Do not invent a new NW id.
- **Must:** Italian/EN follows the question. **Never** a single
  undifferentiated list of “all active that impact 2026”. Chat
  distinguishes two buckets with honest wording:
  1. **Actionable in 2026** — “su quali posso lavorare per risparmiare
     nel 2026”. Follow-up chips and the Renewals TODO / Renewals link
     are for **these** rows.
  2. **Impacts 2026 spend but 2026 is already locked** — still listed
     if above-market or prezzo-ok / condizioni-migliorabili; Raffa
     says the 2026 cash is already committed (notice passed +
     auto-renew, or next window after 2026). 360 / Portfolio link is
     fine; do **not** promise a 2026 save; do **not** attach a
     2026-savings Renewals CTA.
  Each row: supplier · type, **X% above the media dei nostri dati di
  mercato** when a band exists (never a Renewals P75 label), one-line
  why from pack snippets (include the actionability why), citation
  card → `/contracts/{id}` with `state.from = "ask"`. **Price in-line,
  conditions worse: still list it** — narrate “prezzo ok, condizioni
  migliorabili” and cite which; **no invented condition %**; do not
  hide these behind a price-only gate. Include raffa pack items
  (`contract-360`, `portfolio`) so `actionKeys` are legal. Follow-up
  chips, one per **actionable** listed supplier, posting in the
  **same** conversation with the catalog capitalized name. Primary
  action may include Portfolio; **not** Quote check. No engineer
  chrome, no unrounded 27-digit scores.
- **Acceptance:** golden/eval on the screenshot question. Cards are
  clickable. A price-in-line / worse-conditions fixture appears with
  the condition narrative. When the fixture has both buckets, the
  answer **labels both**; a locked row is not narrated as “puoi
  lavorare per risparmiare nel 2026”. Resume may drop follow-up chips
  (existing gap, `ConversationMessage` has no `followUps` column) —
  **do not** block V1 on persisting chips; chips on the live turn are
  required for **actionable** rows.
- **Seats:** software-architect, client-architect, ux-ui-designer.

### NW-90 — Follow-up “contrattare sul supplier X” writes Renewals TODOs (must)

- **Status:** OPEN. Shares the ranker with Q3 (`persistTodos=true` on
  this follow-up — product owner was explicit).
- **Where:** `AskIntent.RenewalStrategy` (keep), pack must be the
  **benchmarked** strategy (NW-82) + `NegotiationPointRanker` (NW-96) +
  NW-85 upsert + NW-84 link.
- **Today:** RenewalStrategy pack is calculator snippets only, no
  tenant clause evidence, no raffa item → prompt forbids `actionKeys`.
  Ask does not read `RenewalActionService` (R-STR-03 unimplemented).
- **Must:** resolve X → active contract(s) (NW-80). If several, all
  active X contracts or one disambiguation turn; never mix tenants.
  Spell out the multi-contract case (lock 9); never silently merge.
  Unknown X → `NeedsDocument`. **Q1 follow-up TODOs / Renewals link
  are for `actionableIn2026` rows (lock 2).** Narrate the **top 3**
  grounded points in chat (lock 7); persist **all** ranked points to
  the Renewals TODO list (do not cap TODOs at 3 unless the ranker
  only produced 3). Action **“Open in Renewals →”**
  `/renewals?select={contractId}`. If the named contract is
  **locked for 2026**, do **not** promise a 2026 save and do **not**
  write a 2026-savings TODO; 360 link is fine; a next-renewal
  strategy may still be narrated honestly as post-2026. If
  `savedAction.Status == InProgress`, acknowledge tracker; don’t
  propose “Start negotiation” again (R-STR-03).
- **Acceptance:** after the follow-up, chat names at most **3**
  points; GET todos + Renewals select show the **full** ranked list
  (may be longer than 3). Strategy numbers match `/strategy` when
  bands exist.
- **Seats:** software-architect, client-architect.

---

## 4. Flow Q2 — notice on the open contract (must)

### NW-91 — Notice questions use a structured pack, not generic RAG (must)

- **Status:** OPEN.
- **Where:** `AskRaffaQueryRouter`, `IntentPlanner`,
  `BuildClausePackAsync` vs `BuildContractFactItem` /
  `BuildStructuredFactPackAsync`, `StrategyPackBuilder.BuildWhenYouMustMove`.
- **Today:** `"notice"` is not structured. Default Semantic → Clause.
  `BuildContractFactItem` **already** knows `"notice by {yyyy-MM-dd}"`
  plus auto-renew and is never called for this question. Unscoped
  structured fallback would dump the soonest **5** contracts.
- **Must:** phrasings “when must we give notice”, “notice period”,
  “notice by”, “cancellation deadline”, “non-renewal”, IT “preavviso”,
  “disdetta” → structured notice (StructuredFact + dedicated pack, or
  a dedicated deterministic kind). Pack order: scoped fact (`endDate`,
  `cancellationDeadline`, `autoRenewal`, `renewalTermMonths`,
  `daysUntilNotice` computed in host / `RenewalEngine`) →
  `WhenYouMustMove` explanation (miss / passed / no auto-renew) →
  evidence item for `cancellationDeadline` / matching clause.
  RAG only as fallback, **filtered** to that contract (NW-81); otherwise
  skip RAG and abstain honestly.
- **Acceptance:** scoped screenshot question never becomes tenant-wide
  Clause RAG. Fixture with `endDate=2027-01-31`,
  `cancellationDeadline=2026-10-18`, `autoRenewal=true`,
  `renewalTermMonths=12`: answer states **18 Oct 2026**, remaining days
  vs `IClock`, and “if we miss it the term renews for 12 months”.
  `autoRenewal=false`: no notice window / contract ends on `endDate`.
  Deadline in the past: “deadline passed N days ago”. Every calendar
  date in markdown equals a `PackValueKind.Date`. Remaining-until-
  deadline day counts are pack values vs `IClock`, not model
  arithmetic. Notice-period “N days” only if N is in the cited clause
  (NW-92 / lock 8) — do not invent a period.
- **Seats:** software-architect.

### NW-92 — Notice days: date + days-if-in-span (must); persist-days is overflow

- **Status:** OPEN. **LOCKED 2026-09-16:** must-floor only for this
  wave. R5a persist-days is **could / overflow**, not should-blocking.
- **Where:** `Contract` has `CancellationDeadline` but **no**
  `CancellationNoticeDays`. Host compositions pass
  `CancellationNoticeDays: null`. Fixture extractor regex captures N
  days then **discards N** (`FixtureContractFactExtractor`).
- **Must floor (R5b — V1 wow):** answer with the **date**. Say “N
  days” **only if N is in the cited clause / span**. Do not invent
  days. Do not silently parse `Clause.NormalizedValue` into a
  deadline. Do not let the model subtract `EndDate − deadline`. Do
  **not** require a new `cancellationNoticeDays` column for this wave.
- **Could / overflow (R5a — not this wave):** extract and persist
  `cancellationNoticeDays` (int) with evidence; `RenewalEngine`
  computes deadline from `EndDate − days`; stop treating the
  LLM-written date as the only source. Head of W20 if it ever lands;
  **do not block** the Q2 wow on it.
- **V1 notice regime:** answer **non-renewal / auto-renew notice
  only**. If the PDF mixes breach / convenience periods, quote those
  clauses and do not pick a second calendar date without a second
  column.
- **Acceptance:** the product never claims a day count that is not in
  the cited clause. Date is always pack-backed. NumericGuard still
  holds. Missing persist-days column is **not** a fail.
- **Seats:** software-architect.

### NW-93 — Jump to the notice phrase from chat (must)

- **Status:** OPEN. Depends on NW-83 + W18 viewer (done).
  **Two-CTA card LOCKED 2026-09-16.**
- **Must:** the notice answer includes a chat **card with two
  actions**: (1) open 360, (2) open the viewer **already at the
  notice span**, without searching the document. Primary UX is this
  **two-CTA card** — not viewer-only, not 360-only. The viewer CTA
  uses the W18 viewer deep-link (page + clause / span). 360
  `?clause=` is **not** a substitute for the viewer CTA when a span
  exists; it is the 360 half of the pair (and the fallback when there
  is no page image, together with an honest “no page” treatment — still
  keep the 360 CTA). Citation snippet is the evidence `sourceSpan` /
  clause `rawText`, not the calculator sentence alone.
- **Acceptance:** the Q2 card shows both CTAs. Viewer CTA opens the
  contract’s source page with the notice wording highlighted. 360 CTA
  opens that contract. Neither CTA is omitted on the happy path.
- **Seats:** client-architect, ux-ui-designer, software-architect.

### NW-94 — Notice fallbacks never ask “which supplier?” when scoped (must)

- **Status:** OPEN.
- **Acceptance:**
  1. Deadline + evidence → answer + deep-link (happy path).
  2. Deadline, no span → answer the date; CTA to 360 Review; no fake
     page.
  3. No deadline, clause text only → no invented calendar date; quote
     clause; recovery Review.
  4. Neither → abstain **naming this contract**; recovery 360 Review
     (NW-59). Never “which supplier”.
  5. Unscoped deictic on Ask home → abstain + Portfolio / “open a
     contract” recovery. Do **not** mix in a portfolio notice listing
     (that is a different question).
- **Seats:** software-architect, client-architect.

---

## 5. Flow Q3 — AsterCloud next-renewal strategy (must)

### NW-95 — Screenshot Q3 plans `RenewalStrategy` (must)

- **Status:** OPEN. Covered in part by NW-79; this item is the Q3
  routing + pack entry, not just the regex.
- **Must:** given the screenshot question (and EN “what can I negotiate
  on the AsterCloud renewal?”), planner returns `RenewalStrategy` with
  named supplier AsterCloud. Pack corpora include `calc` + `tenant`
  (and `market` when notes/bands exist) + one `raffa` Renewals item.
  If 360 exists, do **not** abstain for “no renewal section in the
  PDF”. Missing deadline is a named point (“end date unknown”), not a
  full abstain.
- **Acceptance:** golden
  `seeded-renewal_strategy-contrattare-astercloud-it`: `kind=answer`,
  ranked points, not `ReplyKind.Abstain`. Existing compliance test:
  `answer` request still has **no tools** payload.
- **Seats:** software-architect.

### NW-96 — `NegotiationPointRanker` — grounded points only (must)

- **Status:** OPEN. New pure calculator in `Raffa.Insights`.
  **Chat cap LOCKED 2026-09-16: top 3.**
- **Today:** `WhereYouCanPush` always emits **7 levers** per line,
  including ungrounded generics (“no utilization data yet”).
  `StrategyInputs` has dates + priced lines + weak facts — **no**
  clause / SLA / liability ranking. SLA is at best a free-text
  `ClauseType`.
- **Must:** each point has `topic`, `rank`, `current` (stored),
  `target` (band or “insufficient market data”), `whyItMatters`
  (renewal urgency + peer/market evidence), `citationKeys`, `strength`
  (grounded vs generic). **Include a point only if grounded** (stored
  fact, clause, band, or peer chunk). Drop generic
  utilization / alternatives / payment-terms unless data exists.
  Suggested order: (1) priced lines above band, (2) uncapped / high
  liability, (3) auto-renewal + short notice, (4) SLA/credits clauses,
  (5) term/volume with recorded qty/term, (6) payment terms if
  extracted. **Chat cap LOCKED: top 3** grounded points in the chat
  answer (the strongest). Full / remaining points still persist to the
  Renewals TODO list — **do not cap the TODO list at 3** unless the
  ranker only produced 3. The model narrates this chat list; it does
  not invent rank (lock 9: stored fact vs calculator vs cannot-claim).
- **Shared helper:**
  `BuildNegotiationPointsPackAsync(contractId, includeRenewalUrgency,
  persistTodos)`. Q1 follow-up and Q3 both persist. Q3 always injects
  the Renewals action; Q1 follow-up does too.
- **Insights still `[SharedKernel, Benchmark]`:** host maps
  clause/risk/commercial snapshots into `StrategyInputs` DTOs. Do not
  take a Documents project reference inside Insights.
- **Acceptance:** `NegotiationPointRankerTests`: above-band price ranks
  above ungrounded payment-terms. Generic 7-lever dump is not an
  answer. Chat markdown names at most **3** points; GET todos may
  return more than 3 when the ranker produced more. Pack contains
  typed values for price, term, auto-renewal, cancellation deadline,
  payment terms, liability/SLA summaries when present.
- **Seats:** software-architect.

### NW-97 — Q3 persist + Renewals deep-link (must)

- **Status:** OPEN. Composition of NW-85 + NW-84 + pack raffa item.
  **Chat top-3 / persist-all LOCKED 2026-09-16.**
- **Must:** after rank, upsert **all** ranked todos (do not cap the
  TODO list at 3 unless the ranker only produced 3). Successful answer
  is `kind=answer` markdown of the **top 3** grounded points (topic ·
  current → target · why) + citations (tenant pages, calc points,
  market notes, raffa Renewals card) + **server-injected** navigate
  action `/renewals?select={guid}` via existing
  `CapabilityRouting.BuildHref`. Do not wait for the model to emit
  `actionKeys`. Opening it selects that row and shows TODOs (full
  list, which may be longer than the chat answer).
  `CopilotReplyBuilder` today **strips actions on abstain** — Q3
  success path is `answer`; do not rely on abstain to carry the CTA.
- **Acceptance:** ask → chat answer with **at most 3** points → click
  Renewals → same contract selected with the **full** TODO list (3 or
  more if the ranker produced more). Second identical question: no
  duplicate Open rows; Done preserved.
- **Seats:** software-architect, client-architect, ux-ui-designer.

---

## 6. Out of this wave / do not touch

| Item | Why |
|---|---|
| NW-55, NW-56, NW-57, NW-59, NW-60, NW-63 remainder | **W18 — done.** Reuse. Finish only the unwired leftovers named in NW-76 and NW-84. |
| NW-23, NW-25, NW-74, NW-30, NW-40, NW-41, NW-50 | W18 queue. Do not pull them into this wow wave unless a `dev` walk proves they never landed. |
| Function-calling / tools on `answer` | ADR-024. Persistence and retrieval stay in the host. |
| 360 4-step tracker extra step | Product change. TODOs live on Renewals. |
| Paid market API (NW-52) | Still deferred. Fixture / representative bands today; same `IBenchmarkService` later backed by third-party APIs (lock 1). |
| Invented % / invented notice date / invented 2026 save | NumericGuard + persona rule 6 + **lock 9**. Insufficient data is a valid wow (honest + next step). Never invent a mal-position %, a notice-day count, or a 2026 saving the pack did not classify. Spell out case splits (actionable vs locked, price-ok vs conditions-worse). |
| New chat “list widget” React type | V1 = markdown + W18 citation cards + ActionRow. Optional later. |
| Persisting follow-up chips on resume | Existing schema gap; do not block V1. |
| Quote check disappearing from Ask | Quote check **remains** for a **new market proposal** (user uploads a quote/doc and asks “is this proposal in line with the market?”). It is **not** the handler for Q1 portfolio mal-position / 2026 savings — that is Ask (narrowed R-SYS-02, NW-86). |
| Persist `cancellationNoticeDays` (R5a) | **Overflow / W20.** V1 wow = date + “N days” only if N is in the cited clause (NW-92). Not should-blocking. |
| Viewer overlay / phrase-edit | W18. Q2 two-CTA card **fills** citation ids so the overlay can fire at the span. |
| NW-75 | Still OUT (ADR-001 w17 clause 7). |

---

## 7. LOCKED by product owner 2026-09-16

These nine decisions are **binding**. They override earlier
“recommended defaults” in this file. They are no longer optional HITL
guesses. Later the same day, lock 2 was refined to **option 3** (two
honest 2026 buckets) and lock 9 (communication principle) was added.

| # | Question | LOCKED 2026-09-16 |
|---|---|---|
| 1 | What is “average” / mal-position % | vs the **representative average of our market corpus** (“media dei nostri dati di mercato”). Today: fixture / mocked `IBenchmarkService` bands; tomorrow: the same interface backed by third-party APIs. Implementation may map “media” to the adapter’s central statistic (typically P50 / median of the band). **Do not use Renewals P75 “above market” as the Q1 gate.** Insufficient sample → omit, never invent a %. |
| 2 | Anno 2026 candidate set | **Option 3 (BINDING).** Show **all Active (+validated) contracts that impact 2026 costs** (you are paying them in 2026 / they drive 2026 spend) — not “renewal date ∈ 2026 only”, not a mere date-overlap story. Expired / inactive / ended-in-2025 are **out**. Chat **always distinguishes two buckets**, honest wording, never a single undifferentiated list: **(a) Actionable in 2026** — a lever this year is still open (notice window still open, renewal/end in 2026, or mid-term renegotiation is a **stored fact**). These are “su quali posso lavorare per risparmiare nel 2026”. **(b) Impacts 2026 spend but 2026 is already locked** — still listed if above-market or prezzo-ok / condizioni-migliorabili, but Raffa must say the 2026 cash is already committed (notice passed + auto-renew, or next negotiation is after 2026). Do **not** imply the buyer can still save 2026 money on those rows. Price-in-line + worse conditions stays listed (lock 3), narrated “prezzo ok, condizioni migliorabili”. Cases: running since 2024, renewal Jan 2026, notice still open → list, **actionable**; same but notice passed + auto-renew → list if above-market, **locked for 2026**, say so; 2025–2028 term, next renewal 2028, still paying 2026 → list if above-market, **locked for 2026** unless mid-term renegotiation is a stored fact; ends 2025-12-31 still Status=Active → **out** (does not impact 2026 costs); starts 2026-06-01 → in, actionable if not already closed. Pack: `actionableIn2026: true / false` plus a short why (“notice by 18 Oct 2026” vs “auto-renew already triggered; next window 2027”). TODOs / Renewals link for **actionable** rows; locked rows may link to 360, must not promise a 2026 save. |
| 3 | Price in-line, conditions worse | **List it.** Narrate: the price is in line, but some conditions can be improved (cite which). **No invented condition %.** Do not hide these behind a price-only gate. |
| 4 | Q1 asked from an open contract 360 | Always the **workspace portfolio**. “quali contratti” is a portfolio question even if the chat was opened from one contract. |
| 5 | Q2 notice jump from chat | Card in chat with **two actions**: open 360 **and** open the viewer **already at the right place**, without searching the document. Not viewer-only, not 360-only (NW-83 / NW-93). |
| 6 | Quote check vs Ask | Quote check is for a **new market proposal** (upload a quote/doc: “is this proposal in line with the market?”). Q1-style “which of MY contracts are poorly positioned / where can I save in 2026” is **answered in Ask**, never routed to Quote check. Quote check does **not** disappear. |
| 7 | How many negotiation points in chat | **Top 3.** Max 3 grounded points in the chat answer (the strongest). Full/remaining points still persist to the Renewals TODO list (do not cap the TODO list at 3 unless the ranker only produced 3). |
| 8 | Notice days vs date | **Date + “N days” only if N is in the cited clause.** Do not invent days. Do not require a new `cancellationNoticeDays` column for V1 wow. R5a persist-days is **could / overflow**, not should-blocking. NW-92 must-floor = date + days-if-in-span. |
| 9 | Honesty of deductions (all Ask wow answers) | **Always** be clear and honest: what is a **stored fact**, what is a **calculator deduction**, what **cannot be claimed**. Materially different cases must be **spelled out** in the answer (actionable vs locked, price-ok vs conditions-worse, date known vs days only if cited, insufficient market data, several contracts for one supplier) — never collapsed into one optimistic sentence. Same standard for **this requirements file**. Do not let the model imply savings, notice dates, or negotiation wins the pack did not classify. Applies to Q1, Q2, and Q3. Persona / pack narration (`Prompts/answer`) follows this lock. |

### Still as written (not reopened by HITL)

These rows were not overridden on 2026-09-16. They stay as this file
already bound them; they are not “open questions” for the nine locks
above.

| # | Question | Binding in this file |
|---|---|---|
| A | Contract-level % when lines disagree | Spend-weighted average of **positive** linePcts. |
| B | Several contracts per supplier | **One row per contract** (link each in Portfolio). Spell out the case (lock 9); never silently merge. |
| C | TODO merge | Upsert by `point_key`; preserve Done; supersede vanished keys. Owner blank / system; human ticks on Renewals. |
| D | Multiple notice regimes | V1 = **non-renewal / auto-renew** only. |
| E | Q1 follow-up persist | **Yes** (`persistTodos=true`) for **actionable in 2026** rows. Write Renewals TODOs. Chat still narrates only the top 3 (lock 7). Locked-for-2026 Q1 rows must not get a 2026-save TODO. |

---

## 8. Suggested grouping, order, and seats

Cap 20 tasks / 5 phases; overflow → **head of W20**. Order is binding
inside the wave: shared primitives before the three flows; Q1/Q2/Q3
can fan out after NW-76…NW-85.

| Theme | Items | Seats |
|---|---|---|
| A · bind the open contract | NW-76, NW-77, NW-78, NW-80 | software-architect, client-architect, ux-ui-designer |
| B · planner + retrieval + numbers | NW-79, NW-81, NW-82, NW-83, NW-88 | software-architect |
| C · Renewals write-back | NW-84, NW-85 | software-architect, client-architect, ux-ui-designer, security-architect |
| D · Q2 notice wow | NW-91, NW-92, NW-93, NW-94 | software-architect, client-architect, ux-ui-designer |
| E · Q1 portfolio wow | NW-86, NW-87, NW-89, NW-90 | software-architect, product-owner, client-architect, ux-ui-designer |
| F · Q3 strategy wow | NW-95, NW-96, NW-97 | software-architect, client-architect, ux-ui-designer |

**A + B + C before D/E/F.** Q2 is the smallest end-to-end wow once
scope binds. Q3 reuses C + ranker. Q1 is the heaviest (planner
amendment, portfolio × benchmark, latency).

### Build sequence (hint for execution, not a wave-spec)

1. Lexicon + planner tests (screenshot sentences become routable).
2. Scope + supplier resolution (NW-76/77/78/80).
3. Async priced-lines parity (NW-82) + embedding filter (NW-81).
4. Citation ids (NW-83).
5. TODO schema/API + `?select=` (NW-85, NW-84).
6. Notice pack + fallbacks + jump (D).
7. Ranker + Q3 composition + persist + action (F).
8. Portfolio mal-position intent + % + list + follow-up (E).
9. Goldens: the three oracle questions on the LUCA TEST / fixture
   workspace.

---

## 9. Essential files (do not re-derive from a blank sheet)

**Chat / Ask:** `AskCopilotService.cs`, `ConversationsEndpointExtensions.cs`,
`IntentPlanner.cs`, `AskIntent.cs`, `DomainGate.cs`, `AskRaffaQueryRouter.cs`,
`AnswerComposer.cs`, `NumericGuard.cs`, `CopilotReplyBuilder.cs`,
`CapabilityCatalog.cs`, `CapabilityRouting.cs`, `PackItem.cs`, `PackValue.cs`,
`Prompts/answer/v2.1.md` (persona / pack narration: lock 9 — stored fact vs
calculator vs cannot-claim; never collapse case splits)

**Contracts / 360 / evidence:** `PortfolioQueryService.cs`, `PortfolioFilter.cs`,
`Contract.cs`, `Contract360QueryService`, `ContractsEndpointExtensions.cs`
(360 + `/evidence`), `ExtractionEvidence.cs`, `StagedExtractionJsonSchemas.cs`

**Benchmark / insights / strategy:** `BenchmarkKeyResolution.cs`,
`InsightsEndpointExtensions.cs`, `IBenchmarkService.cs`, `PricedLine.cs`,
`StrategyPackBuilder.cs`, `StrategyInputs.cs`, `PricedLineNegotiationCalculator.cs`,
`RenewalEngine.cs`, `ContractRenewalTerms.cs`

**Market RAG:** `IMarketKnowledgeRetrieval.cs`, `EmbeddingRetrievalService.cs`

**Renewals:** `RenewalsEndpointExtensions.cs`, `RenewalPipelineBuilder.cs`,
`RenewalAction.cs` (do not overload), `web/src/routes/renewals/index.tsx`,
`InsightCard.tsx`

**Web Ask:** `web/src/routes/ask/index.tsx`, `askViewModel.ts`,
`GlobalAskBar.tsx`, `askSuggestions.ts`, `reply/ReplyBody.tsx`,
`CitationCard.tsx`, `ActionRow.tsx`, `Contract360Header.tsx`

**Policy / eval:** ADR-024, `inputs/requirements.md` R-SYS-02 / R-CMP-01 /
R-PORT-01..03 / R-STR-01..03 / R-ASK-10, `IntentPlannerTests.cs`,
`GoldenSet.cs`

**W18 primitives to reuse:** `Conversation.ScopeContractId`,
`parseScopeContractId` / `createConversationAndAsk`, `CitationCard.tsx`,
`documentViewerViewModel.ts`, `ClauseHighlight.tsx`
