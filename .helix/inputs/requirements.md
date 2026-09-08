# Contigo V2 — Ask Contigo requirements

Status: **binding input** for the V2 Ask process (oracle for Passata 1 and for
fan-out prompts). **Supersedes** `inputs/ask-copilot-brief.md`, `epic-12`
and slice `e12` (planned, never launched: worktrees `E12-F01-US01-T01` /
`E12-F02-US01-T01` are clean at `9332c6f`; no `gates/ask-copilot.hitl-ok`).
Amends how `inputs/product-spec.md` §4.1, §7, §8.3–§8.4 and §10.2–§10.4 show
up to the buyer. Does not re-open `reports/context/locked-decisions.md`.

| Oracle | Path |
|--------|------|
| Design (V2) | `inputs/design/prototypes/Contigo V2 Prototype.html` + `inputs/design/_claude-design-brief.md` ("V2 principles") |
| Product spec | `inputs/product-spec.md` |
| Code baseline audited | branch `integration` @ `4a1bddf`, 2026-09-08 |
| ADRs in force | ADR-001…022 (ADR-023 is superseded by this document, see §8) |

Audience: procurement (Workspace Admin, Procurement). Language: the user's
language (Italian and English at minimum); citations quote the document in its
own language. Tone: a specialist who helps you **save money on contracts** —
not a lawyer, not a generic chatbot, **never a web search**.

---

## 0. Decisions taken at HITL (2026-09-08)

| # | Question | Decision |
|---|----------|----------|
| D1 | Where do uploads happen | **Documents only.** Ask never accepts attachments. When a document is needed, Ask answers with an action to `/documents`. |
| D2 | What is the "knowledge base" | A **third-party market-intelligence API**: a worldwide benchmark of how companies close contracts (price bands, discounts, uplift caps, notice periods, negotiated clauses). Not available yet → a **mock feed** now, which populates Contigo's RAG (a *market* index) and the benchmark numbers. The live API later plugs into the same adapter. |
| D3 | Non-contract documents | **Rejected immediately** by an admission gate: not stored, audit event only, warm message with the reason. |
| D4 | Relation to epic-12 | **V2 replaces e12.** One epic absorbs and rewrites the five e12 features. ADR-023 is superseded by a new ADR. |
| D5 | Conversations | **Server-side, per user and per workspace**, under RLS; resumable from any device. |
| D6 | "Most critical contracts" | A **deterministic composite criticality score**; the model narrates it, never ranks on its own. |
| D7 | Formats and types | PDF/DOCX/XLSX **plus PNG/JPG** (scanned contracts via OCR). All spec §4.1 types are admitted (MSA, Order Form, SOW, Amendment, Quote, Invoice, Price list, NDA, DPA, Renewal letter). **Multi-file upload** with a per-file outcome. |
| D8 | Who uploads | **Admin and Procurement.** Only Admin deletes documents and manages members. |

---

## 1. Problem — what `integration` does today

Audited point by point (file paths are the evidence). Every row below is
either closed by a requirement in §5 or listed as a non-goal in §3.

| # | Today | Evidence |
|---|-------|----------|
| P1 | Structured questions ("which contracts renew in 120 days?", spend) return "not wired to an endpoint". | `backend/src/Contigo.Api/ChatEndpointExtensions.cs` → `ToStructuredNotWiredResponse` |
| P2 | Semantic questions echo retrieved chunk text (including `%PDF-1.4` headers) as the answer. | `backend/src/Contigo.AiGateway/Fixtures/FixtureAiGateway.cs` → `AnswerAsync` concatenates evidence |
| P3 | No Foundry implementation: only the fixture is registered; `LoggingAiGateway` exists but is never wrapped. | `backend/src/Contigo.AiGateway/ServiceCollectionExtensions.cs` (`TryAddSingleton<IAiGateway, FixtureAiGateway>()`) |
| P4 | No domain gate: "ciao" / "carbonara" / "can I sue?" go straight to RAG. | `backend/src/Contigo.Chat/Application/AskContigoQueryRouter.cs` (keyword router, two intents only) |
| P5 | Single-turn chat: no conversations, no history, no "new chat / resume". | `POST /api/chat/query` only; `web/src/routes/ask/index.tsx` keeps messages in component state |
| P6 | Citations are `Document:<guid>` chips; `Page` is always null; the engineer route line ("Structured query…") is shown; abstain is only the red block. | `ChatEndpointExtensions.ToEvidenceSnippet` (`Page: null`, `Section: "chunk n"`); `web/src/routes/ask/ChatMessage.tsx`, `askViewModel.ts` |
| P7 | No in-app actions / deep links in replies. | reply shape `{question,intent,canDetermine,answer,citations,message}` |
| P8 | Upload accepts only `.pdf/.docx/.xlsx`; **a non-contract is never rejected** — a document classified `Other` is still extracted and indexed into tenant RAG. | `web/src/routes/documents/UploadDropzone.tsx` (`ACCEPTED_EXTENSIONS`); `backend/src/Contigo.Documents.Contracts/Application/Extraction/DocumentProcessingPipeline.cs` (`MapDocumentType → Other`, then `IndexForRetrievalAsync`) |
| P9 | Upload is synchronous inside the HTTP request; bytes are saved to blob **before** classification. | `backend/src/Contigo.Api/Program.cs` `MapPost("/api/documents")` |
| P10 | No document list endpoint; the Documents screen remembers uploads in `sessionStorage`. | API has only `POST /api/documents`, `GET /api/documents/{id}`; `web/src/routes/documents/documentStore.ts` |
| P11 | `IDocumentStorage` cannot load a saved object; no re-OCR / re-embed; no first-page preview. | `backend/src/Contigo.SharedKernel/Storage/IDocumentStorage.cs` (`SaveAsync` only); no `/preview` anywhere |
| P12 | **Supplier identity does not exist**: no `Supplier` entity, no `supplier` fact extracted, `Contract.SupplierId` is never set. Ask cannot say "your Allianz contract". | `backend/src/Contigo.Suppliers.Products/` contains only the `.csproj`; `StagedExtractionService` fact switch has no `supplier` case |
| P13 | Market corpus is a thin fixture (8 rows: AWS, Salesforce, Slack, Snowflake, Zoom, Notion) with no narrative and no insurance / facilities rows. | `backend/src/Contigo.Benchmark/Fixtures/FixtureBenchmarkAdapter.cs` |
| P14 | Deterministic calculators exist but are not reachable from Ask: renewal engine + priority score, negotiation levers (quote lines only), savings opportunities. Renewal insight card has `MarketPosition = null`. | `Contigo.Renewals/Application/{RenewalEngine,PriorityScoreCalculator,RenewalPipelineBuilder}.cs`; `Contigo.Quotes/Application/Strategy/NegotiationStrategyCalculator.cs`; `Contigo.Savings/Application/SavingsOpportunityService.cs` |
| P15 | No capability catalog: Ask does not know Contigo's own screens, so it cannot route the user. | suggestions are static strings in `web/src/routes/ask/askViewModel.ts` / `components/ask-bar/askSuggestions.ts` |
| P16 | Web IA is still Day-1: Home first, flat rail, Ask is one item, Contract 360 with tabs, Review as its own route. V2 says Ask is home, two-tier nav, recent chats, review is a state of Documents. | `web/src/components/shell/navItems.ts`, `WorkspaceShellApp.tsx`, `routes/contracts/contract360/*` |
| P17 | Chat module may only reference `SharedKernel` + `AiGateway`; benchmark / calculators cannot be composed inside it. | `backend/tests/Contigo.ArchitectureTests/DependencyDirectionTests.cs` allow-list |

---

## 2. Goal

Ask Contigo V2 is the **home** of Contigo (sign-in lands on `/ask`). It is a
**savings and negotiation copilot** that reasons only over three sources of
truth and always says which one it used:

| Source | What it holds | Where it lives | Isolation |
|--------|---------------|----------------|-----------|
| **Your validated contracts** | structured facts (dates, spend, notice, uplift, liability…), clauses, pages | tenant tables + tenant `embedding` index (RLS) | ADR-009 / ADR-011: never another tenant |
| **Market intelligence** | how companies close contracts: P25–P75 price bands, discounts achieved, uplift caps, notice periods, clauses obtained, by category / supplier / geography / company size / term | `IMarketIntelligenceProvider` (mock now, third-party API later) → benchmark rows **and** a shared, read-only **market index** | never tenant data, never another tenant's PDFs; written only by the ingestion job |
| **Contigo itself** | capability catalog: what each screen does, its route, how to use it | static, versioned in the API | — |

The one rule: **every claim traces to one of the three sources, or Contigo
abstains.** No web search, no model memory, no invented price, saving, date or
clause text. Numbers come from Contigo's deterministic calculators
(renewal engine, priority score, criticality score, benchmark bands,
negotiation levers); the model **narrates** them.

The V2 buyer journey ("pilot path", design brief):
Sign in → Documents (drop one or more contracts; non-contracts are refused) →
review weak facts → Ask (contract vs market, renewal strategy) → new chat
(portfolio strategy) → follow a citation into Contract 360.

---

## 3. Scope and non-goals

**In scope**

- Documents: multi-file upload, admission gate (contract-related only),
  PNG/JPG via OCR, server-side document list, attention filter, per-file
  outcome, re-OCR / re-embed of existing documents, first-page preview.
- Ask: conversations (new / resume / recent), domain gate, intent planner,
  context pack, Foundry `answer` role with structured output, grounding and
  numeric guards, rich reply (markdown, human citation cards with preview,
  in-app actions), abstain / redirect / refusal layouts.
- Contract vs market comparison, renewal strategy per contract, portfolio
  strategy (criticality ranking, where to save, what to improve).
- Market-intelligence feed: adapter seam, mock dataset, benchmark projection,
  market index, provenance labels ("representative / mock" until live).
- Supplier identity: extraction, entity, normalization, back-fill.
- Capability catalog and system-aware routing with feature citations.
- Web V2 information architecture for the pilot path: Ask home, two-tier
  nav, Documents V2, citation landing in Contract 360.
- Foundry gateway for the five ADR-004 roles, always wrapped by
  `LoggingAiGateway`; fixture kept for tests and local.

**Out of scope (spec §1.2 + this document)**

- Legal advice, "would this hold in court", redlining, authoring, e-sign.
- Attachments inside the chat (D1). Ask routes to Documents instead.
- Cross-tenant RAG of any kind; another tenant's contract as "market".
- The paid third-party market API as a hard dependency of the first V2
  `demo` (ADR-001). The seam and the mock are in scope; the live client is a
  later adapter.
- Web browsing / search grounding in any model role.
- Autonomous supplier communication, approval orchestration.
- Mobile.

---

## 4. Personas and roles

| Role | Can | Cannot |
|------|-----|--------|
| Workspace Admin | upload, review, ask, act on renewals, **delete documents**, invite / manage members | — |
| Procurement | upload, review, ask, act on renewals | delete documents, manage members (sees "request access") |

Both roles share the same Ask. Conversations are private to the user who
opened them (D5); an Admin does not read another member's chats in V2.
Legal / Finance / read-only stay model-level (ADR-018), not nav variants.

---

## 5. Functional requirements

Requirement ids are stable (`R-<AREA>-nn`). Every requirement lists
acceptance criteria (AC) that a test or a `demo` walkthrough can verify.

### 5.1 Documents — upload, admission gate, statuses

**R-DOC-01 Multi-file upload.** Documents accepts one or more files per
drop / pick (batch ≤ 20 files, ≤ 50 MB per file). Each file is uploaded and
processed independently and shows its own row and outcome.
- AC-1 Dropping 3 files creates 3 rows immediately (`processing`) and each
  reaches a terminal outcome without blocking the others.
- AC-2 A failed or rejected file never hides the outcome of the others.

**R-DOC-02 Accepted formats.** `application/pdf`, DOCX, XLSX, `image/png`,
`image/jpeg`. Format is checked by extension **and** magic bytes before any
model call. Other formats are refused client-side and server-side (HTTP 415)
with a plain message ("Contigo reads PDF, Word, Excel and scanned images").
- AC-1 A `.zip` renamed `.pdf` is refused (415) without touching the AI
  gateway.
- AC-2 A PNG of a scanned order form goes through OCR (ADR-017) and is
  admitted when the gate says so.

**R-DOC-03 Admission gate (contract-related only).** After parse (native
text or OCR) and before any persistence to blob or database, the document is
classified (ADR-004 `classify` role). It is **admitted** only if the type is
one of MSA, Order Form, SOW, Amendment, Renewal letter, Quote, Invoice, Price
list, NDA, DPA **and** confidence ≥ `Documents:AdmissionThreshold` (default
0.6). Otherwise it is **rejected**: no blob, no `document` row, no embedding;
one audit event `document.rejected` (file-name hash, detected type,
confidence, reason — never content); HTTP 422 with `{ rejected: true,
detectedType, confidence, reason, hint }`.
- AC-1 A recipe (text PDF) → 422, reason "not a contract document", nothing
  persisted (blob list and `document` table unchanged), one audit row.
- AC-2 A photo without readable text → OCR yields < `MinReadableChars`
  (default 200) → 422 reason "no readable contract text" (not "not a
  contract").
- AC-3 A scanned MSA (image PDF) → admitted, type `Msa`.
- AC-4 A Quote → admitted; the result card and Ask suggest **Quote check**.
- AC-5 Gate decisions are logged through `LoggingAiGateway` like any AI call
  (model, version, prompt version, input hash).
- AC-6 The fixture gateway (tests / local) rejects a recipe and admits a
  document containing "MASTER SERVICES AGREEMENT" — the gate is testable
  without Foundry.

**R-DOC-04 Rejection copy.** The Documents result card for a rejected file
reads warm and specific, e.g. *"Not added: this looks like a recipe, not a
contract. Contigo only keeps contracts, order forms, quotes and the documents
around them. Drop the signed agreement or the supplier's proposal."* Rejected
files are shown for the current session only (they are not stored) and are
never counted in "documents" or "askable".

**R-DOC-05 Statuses.** `uploaded → processing → needs_review | completed |
failed` (spec §7.1) plus the transient client-side outcome `rejected`. Only
`completed` documents feed Ask, Portfolio and Renewals (V2 principle
"validated contracts"; wording never says "knowledge base").
- AC-1 `DocumentProcessingStatus` gains no `Rejected` value (rejected files
  do not exist server-side); the API response distinguishes the outcome.

**R-DOC-06 Server-side document list.** `GET /api/documents?status=&page=`
returns the tenant's documents (id, contractId, supplier name when
resolved, fileName, documentType, processingStatus, pageCount, createdAt,
weak-fact count). Documents V2 default filter is **"Needs your attention"**
(processing / needs_review / failed); "All documents" shows everything.
`sessionStorage` tracking is removed.
- AC-1 Reloading the browser shows the same list as before.
- AC-2 Empty attention state reads "Nothing needs you right now."

**R-DOC-07 Load, re-OCR, re-embed.** `IDocumentStorage.LoadAsync` (tenant-
prefixed path, same rules as `SaveAsync`). A reprocess path (API
`POST /api/documents/{id}/reprocess`, Admin, plus an operator job for the
whole tenant) re-runs hybrid parse and embedding so `chunk_text` is readable
text with **page numbers**. Existing `dev` / `demo` documents are reprocessed
before V2 acceptance.
- AC-1 After reprocess, no embedding row for the tenant starts with
  `%PDF` or contains the fixture OCR placeholder.
- AC-2 Every new embedding row carries `Page` (1-based) and, when known,
  `Section`.

**R-DOC-08 First-page preview.** `GET /api/documents/{id}/preview` returns a
PNG of page 1 (rendered at upload for PDF / images; DOCX / XLSX get an honest
placeholder). Tenant-scoped, never a raw blob URL (ADR-009).
- AC-1 A citation card in Ask shows the preview or the placeholder within one
  round-trip.

**R-DOC-09 Processing stages surfaced.** Rows show the real stage (Uploading,
Classifying, OCR / text, Sections & tables, Extracting facts, Validating
schema) from the API, not a client-side timer.

**R-DOC-10 Deletion (Admin).** `DELETE /api/documents/{id}` removes blob,
document, embeddings and detaches the contract's document link; audit
`document.deleted`. Procurement gets 403.

### 5.2 Conversations

**R-CONV-01 Server-side conversations.** Tables `conversation` and
`conversation_message` (tenant-scoped, RLS, ADR-009) keyed by tenant + user.
A conversation has a title (first question, ≤ 48 chars), an optional
**scope** (contract id when opened from Contract 360 "Ask about it"), timestamps.
Messages store role, markdown, citations, actions, kind, AI metadata (model,
prompt version, input hash — never the raw pack).
- AC-1 Another user of the same workspace cannot list or read the
  conversation (403 / not found).
- AC-2 Another tenant cannot read it even with a guessed id (RLS test).

**R-CONV-02 New chat / resume.** The rail shows the user's last 5
conversations nested under **Ask Contigo**, resume by click, "+ New chat".
Asking from the global Ask bar on any screen **always opens a new chat**
(V2 principle). ⌘K / Ctrl+K focuses the bar.
- AC-1 Resuming a conversation renders past turns with citation cards and
  actions still clickable.

**R-CONV-03 User identity.** With ADR-010 in force, user = API token subject.
Under the Day-1 posture (ADR-022, `X-Tenant-Id` header) the SPA sends
`X-User-Id` = MSAL account username; it is **non-authoritative** and must be
replaced by the token claim in the same task that lands the API JWT. The
requirement, not the header, is the contract.

### 5.3 Ask engine

**R-ASK-01 Authorization before anything.** Tenant + user + role are resolved
first; only the resulting scope is passed to retrieval and calculators
(ADR-011). Off-domain turns never retrieve.

**R-ASK-02 Domain gate.** Every turn is classified, deterministically first
(keyword / pattern) and, when ambiguous, with the `classify` role over a
fixed label set: `greeting`, `off_domain`, `legal`, `capability`,
`needs_document`, `in_domain`. Behaviour:

| Label | Behaviour |
|-------|-----------|
| greeting / off_domain | warm decline + **portfolio hook** naming a real contract or saving of *this* tenant (or an upload invite if empty); **no retrieval** |
| legal | refuse the legal reading, offer the commercial analogue with a citation |
| capability | answer from the capability catalog (§5.7) |
| needs_document | explain what is missing and return the action **Upload in Documents** (D1) |
| in_domain | planner (R-ASK-03) |

- AC-1 "ciao" → decline + hook, zero retrieval calls (asserted on the
  recording retrieval fakes).
- AC-2 "ricetta della carbonara" / "foto di mia nonna" → same as AC-1.
- AC-3 "posso fare causa?" → no legal advice; commercial analogue ("I can tell
  you whether the liability cap sits above the market band…") + action to
  Contract 360.

**R-ASK-03 Intent planner.** In-domain turns map to one or more intents:
`structured_fact` (dates, spend, notice, uplift), `clause` (liability,
termination…), `market_compare`, `renewal_strategy`, `portfolio_strategy`,
`savings`, `document_status`, `quote_route`, `navigate`. Supplier names in
the question are resolved against the tenant's `Supplier` table (§5.9);
unknown supplier → `needs_document` with the upload action (prototype:
"No Databricks contract has been uploaded and validated…"). The planner is
deterministic and unit-tested; a model call may only pick among the fixed
intents, never free-form.

**R-ASK-04 Context pack.** Assembled in the composition root from the three
sources, **after** authorization, only for the intents planned. Every item
carries a `citationKey`, a `corpus` (`tenant` | `market` | `contigo`), a
human title / subtitle, page / section, snippet, `href` and provenance.
Items:
- tenant facts (contracts, renewals, risks, obligations, savings
  opportunities, weak facts), tenant clause chunks (top-k, page-aware);
- market: benchmark band for the contract's product(s), market notes (top-k
  from the market index), provenance label and `updatedAt`;
- calculators: renewal engine, priority, criticality, negotiation levers,
  savings ranges — with their component explanations;
- capability entries relevant to the intent (for actions).
Pack size is bounded (token budget per role, configurable). The pack, not the
model, decides what is citable.

**R-ASK-05 Answer role.** Foundry `answer` role is called with a versioned
persona prompt ("savings specialist, never a lawyer, never invent"), the
pack and the conversation's last N turns. Structured output (JSON schema):
`{ canDetermine, answerMarkdown, citationKeys[], actionKeys[],
abstainReason?, followUps[] }`. **No tools, no web grounding, no browsing**
are attached to the deployment or the request; temperature ≤ 0.2.
- AC-1 A request to the Foundry endpoint is built without any `tools` /
  grounding payload (asserted on the fake HTTP handler).

**R-ASK-06 Grounding guards.** Before persisting or returning:
1. every `[n]` and every `citationKey` must exist in the pack (existing
   `AbstainGuard`, extended to keys and to market / contigo corpora);
2. every monetary amount, percentage and date in `answerMarkdown` must equal
   a value present in the pack (normalized; currency-aware) — otherwise the
   reply is regenerated once with the violation named, then downgraded to an
   abstain that shows the pack's own facts;
3. every `actionKey` must resolve to a catalog route; hrefs are never
   model-authored.
- AC-1 A fake model answering "P50 is CHF 140/user" when the pack holds 132 →
  the reply is abstained / regenerated, audit `abstainGuardIntervened=true`.
- AC-2 A fake model citing a document not in the pack → abstain.

**R-ASK-07 Reply contract.** See §6. Kinds: `answer`, `abstain`, `redirect`
(greeting / off-domain / needs_document), `refusal` (legal). Abstain uses spec
§10.4 wording ("insufficient market data" / "cannot determine reliably")
**only** when there is truly no hook; redirects use warm prose + one CTA.

**R-ASK-08 Every consequential answer cites page and section** of the
tenant document (spec §8.4) or the market record id + `updatedAt`, and
offers at least one in-app action. Engineer chrome ("Structured query…",
"Clause retrieval…", chunk ids, guids) is never rendered.

**R-ASK-09 Audit and AI log.** One audit row per turn (`chat.answered`,
`chat.redirected`, `chat.refused`, `chat.abstained`) with counts and the pack
hash; AI calls logged by `LoggingAiGateway` (model, version, prompt version,
timestamp, input hash, page count for OCR) — never raw prompt, pack or
answer (ADR-011).

**R-ASK-10 Askable scope line.** Each conversation shows the scope:
*"Answers only from N validated contracts (Salesforce, Microsoft…) · cites or
abstains"*. With zero validated contracts Ask is **off** with the prototype
copy ("Ask needs at least one validated contract." / "Upload a contract
first…") and one CTA to Documents.

### 5.4 Contract vs market comparison

**R-CMP-01** "Is my Allianz contract in line / above market?" → Contigo
resolves the supplier and the validated contract(s), queries the benchmark
per priced line (unit price, term, geography, currency, quantity tier —
spec §10.4 dimensions) and answers **below / in line / above the P25–P75
band** per line, with the band, sample size, confidence and provenance
("representative market data · mock feed · updated 2026-09-01"). Thin or
unmatched samples → "insufficient market data" for that line, never a bare
number.
- AC-1 With the mock feed, an insurance contract row (e.g. an Allianz-class
  liability policy) has a band; the answer names the position and cites the
  contract page for the unit price and the market record for the band.
- AC-2 A supplier not in the feed → insufficient data + the levers that do
  not need a benchmark (term, volume, payment terms), each cited to market
  notes.

**R-CMP-02 Market notes in the story.** The reply may quote what companies
typically obtain (e.g. "companies of your size usually cap uplift at 3–5%")
only when a market-index record says so; the record is a citation card
(`corpus: market`) with title, category, geography, `updatedAt`.

**R-CMP-03 Not-validated contracts.** If the contract is `needs_review`,
Ask says which weak facts block the comparison and links to the review.

### 5.5 Renewal strategy for a specific contract

**R-STR-01** "How should I approach the Salesforce renewal?" (or from
Contract 360 "Ask about it") → a narrated strategy built from:
renewal engine (end date, notice deadline, days left), priority score
components, insight-card recommendation, contract-level negotiation levers
(R-STR-02), benchmark position (R-CMP-01), market notes on what companies
achieved for that category, open weak facts to validate first.
Output structure: **When you must move** (dates) → **Where you can push**
(levers with evidence) → **Targets** (opening / range / walk-away when a
benchmark exists) → **Next steps** (4-step checklist mirroring the Contract
360 tracker) → actions `Open Contract 360`, `Track it in Renewals`.
- AC-1 Opening / range / walk-away equal the calculator output to the cent.
- AC-2 Dates equal the renewal engine output; a passed deadline is stated as
  passed, not hidden.
- AC-3 Without a benchmark match: no targets, levers only, "insufficient
  market data" stated.

**R-STR-02 Contract-level negotiation levers.** `NegotiationStrategyCalculator`
(today quote lines only) is generalized to a *priced line* input so contract
line items are assessed the same way (opening target, acceptable range,
walk-away, levers: volume, term, utilization, alternatives, quarter-end,
bundle, payment terms). Lever rationale cites the contract fact (page) and,
where used, the market record.

**R-STR-03 Status-aware.** If a renewal action already exists (In
negotiation / Assigned), the strategy acknowledges it and links to the
tracker instead of proposing to start.

### 5.6 Portfolio strategy (new chat)

**R-PORT-01 Criticality score.** A deterministic, explainable score 0–100 per
validated contract: renewal urgency (existing priority score, normalized),
risk severity (max of `Risk.Severity`), spend weight (annual spend share of
the portfolio), savings potential (benchmark position × spend, or savings
opportunity range), open critical facts (weak fields on critical fields
raise criticality because action is blocked). Weights are configuration;
each component has a score and an explanation, like
`PriorityScoreComponent`.
- AC-1 Same inputs → same ranking; components sum to the total.
- AC-2 A contract with all critical facts weak is flagged "validate first"
  in the explanation.

**R-PORT-02 Portfolio questions.** "Which contracts are most critical?",
"Where can we save?", "What should we improve first?" → ranked lists (top 5)
narrated with the component that drives each rank, each row citing the facts
(page) and offering the action to Contract 360 / Renewals / Savings.
"Where can we save" aggregates savings opportunities + above-band lines;
totals are calculator sums with currency, never model arithmetic.
- AC-1 Numbers in the answer equal the calculator totals (numeric guard).
- AC-2 An empty portfolio → upload invite, no ranking.

**R-PORT-03 Improvement hints.** "What should we improve" may propose
clause-level improvements (e.g. add an uplift cap) only when a market note
supports it; otherwise it limits itself to Contigo facts (missing notice
period, unlimited liability, auto-renewal without cap).

### 5.7 System-aware routing and capability catalog

**R-SYS-01 Capability catalog.** A versioned, machine-readable catalog in
the API (`GET /api/capabilities`): for each capability — key, title, route
pattern, one-paragraph description, example questions, role gate,
availability condition (e.g. "needs one validated contract"), how-to steps.
Initial entries: Ask Contigo, Documents (upload / attention / review),
Portfolio, Contract 360 (answers band, clauses, details, tracker),
Renewals (priority list, insight, action, tracker), Savings (KPIs,
opportunities), Quote check (extract → assessment → target → negotiation),
Workspace & members.

**R-SYS-02 Routing.** Intents map to actions with real hrefs from the
catalog only: benchmark / competitor / "in linea" → `Quote check` (and,
when the contract is validated, the inline comparison of R-CMP-01);
unknown supplier → `Upload in Documents`; deadlines → `Renewals`;
"what can you do" / "come faccio a…" → the module list with one action per
module. Every reply carries ≥ 1 action when a capability applies.

**R-SYS-03 Feature citations.** When Ask explains a Contigo capability, the
citation card is a **feature card** (`corpus: contigo`): title = capability,
snippet = what it does, `href` = route. Deep links carry the object id when
known (`/contracts/{id}`, `/quotes/{id}`, `/documents?review={id}`).
- AC-1 "How do I review weak facts?" → answer from the catalog + feature
  card "Documents › Review" + action to `/documents?filter=attention`.

**R-SYS-04 Availability-aware.** Actions to greyed modules (no validated
contract yet) are replaced by the upload action with the prototype's empty-
state copy ("The portfolio lights up from validated contracts.").

### 5.8 Market-intelligence feed (mock now, API later)

**R-MKT-01 Adapter seam.** `IMarketIntelligenceProvider` returns
`MarketDeal` records: provider, record id, supplier, category, product /
SKU, geography, currency, company-size band, term months, annual value band,
unit price P25 / P50 / P75, discount achieved (%), uplift cap (%), notice
days, payment terms, negotiated clauses (liability cap, termination for
convenience, price protection…), closing period, sample size, source,
`updatedAt`, licence restrictions. No business module (Chat, Renewals,
Savings, Quotes) references the provider schema (spec §10.2).

**R-MKT-02 Mock dataset.** A checked-in JSON fixture
(`backend/fixtures/market-intelligence.mock.json`) with **≥ 60 records**
across enterprise software (Salesforce, Microsoft, AWS, Snowflake,
ServiceNow…), insurance (Allianz, AXA, Zurich, Swiss Re…), facilities, telco,
logistics, professional services; geographies EU / CH / US; currencies
CHF / EUR / USD; thin rows on purpose so abstain paths are exercised. Every
record is labelled `source = "mock"`, `representative = true`. It replaces
the eight `FixtureBenchmarkAdapter` rows as the default provider.

**R-MKT-03 Two projections, one ingestion job.**
1. **Benchmark rows** → `IBenchmarkService` (existing contract, P25–P75,
   sample size, confidence, provenance) — deterministic comparison,
   served from the persisted `market_record` rows, never from the provider
   at question time.
2. **Market notes** → one narrative per record ("Companies of 500–2 000
   employees closing Salesforce Sales Cloud in CH in 2026 paid P50 CHF 132
   per user/month, obtained 3–5 % uplift caps…") embedded (ADR-004 `embed`
   role) into the **market index** (`market_embedding`: no tenant id, read by
   every tenant, written only by the ingestion job, own table — never rows in
   the tenant `embedding` table).
   The ingestion job (`seed-market-intelligence` CI job, same identity model
   as `seed-demo-fixture.yml`) is idempotent and versioned by feed version.
- AC-1 Re-running the job with the same file changes nothing.
- AC-2 A market-index search never returns tenant chunks and a tenant search
  never returns market notes (separate tables, separate services).

**R-MKT-04 Provenance in the UX.** Every market number or note shown in Ask,
Renewals or Quote check carries the label *representative (mock feed)* and
`updatedAt` until the live API is wired; then the provider name. A precise-
looking number without provenance is a defect (ADR-001).

**R-MKT-05 Live API later.** The third-party client is a new
`IMarketIntelligenceProvider` implementation behind the same ingestion job;
no Ask, Chat or UI change. The provider feeds Contigo's store and is never
queried live: at question time Ask reads only Contigo's database and indexes
(tenant tables + `embedding`; `market_record` + `market_embedding`). Licence
restrictions from the provider are stored and respected (spec §10.3).

### 5.9 Supplier identity

**R-SUP-01 Extraction.** Staged extraction gains the `supplier` fact
(legal name, page, span, confidence) as a **critical field** (spec §7.3).

**R-SUP-02 Entity.** `Contigo.Suppliers.Products` gets `Supplier`
(tenant-scoped, RLS): name, normalized name, aliases, category, country,
created/updated. `Contract.SupplierId` is set through a resolver port
(`ISupplierResolver` in SharedKernel, implemented by Suppliers.Products,
called from the pipeline orchestration in the composition root): match by
normalized name / alias, else create.

**R-SUP-03 Back-fill.** A one-off job resolves suppliers for existing
contracts (from extracted facts or, when absent, from the review UI where a
user names the supplier as a correction). Weak supplier facts go through the
review like any critical field.

**R-SUP-04 Everywhere a supplier is shown**, the name is used: Documents
list, Portfolio, Renewals, Ask citations ("Salesforce · MSA 2024 · p.12
§8.4"), never a bare `SupplierId` guid.

### 5.10 Foundry gateway, no web, logging

**R-AI-01 FoundryAiGateway** implements the five ADR-004 roles (`ocr` via
Document Intelligence `prebuilt-read` / `prebuilt-layout`, `classify`,
`extract`, `embed`, `answer`) with Azure SDKs **only** in
`Contigo.AiGateway`. Registered when `AiGateway:Endpoint` is set (Container
Apps already inject `AiGateway__Endpoint` / `ProjectName` /
`DocumentIntelligenceConnection`, `infra/modules/containerapps/main.tf`);
fixture otherwise. Always wrapped by `LoggingAiGateway`.
- AC-1 DI tests: endpoint set → Foundry inside Logging; unset → fixture
  inside Logging.
- AC-2 Architecture test: no Azure AI SDK reference outside
  `Contigo.AiGateway`.

**R-AI-02 Structured output.** `classify` returns a label from a fixed set +
confidence; `answer` returns the JSON of R-ASK-05. Prompts are versioned
files (`prompts/<role>/<version>.md`), the version is logged.

**R-AI-03 No web, no training.** Deployments are no-training endpoints
(ADR-011); requests carry no tool / grounding configuration; a compliance
test asserts the request body shape. Any question that would need the web
("today's list price of…") is answered from the market feed with its
`updatedAt` or abstains.

**R-AI-04 Cost guard.** Per-tenant daily token and OCR-page budgets
(configuration); over budget → visible failure, never silent truncation
(ADR-017).

### 5.11 Evidence quality

**R-EVD-01 Page-aware chunks.** Embedding rows store page number and
section label; citations resolve to `Clause.SourcePage` / `SourceSpan` when
the hit is a clause, else to the page.

**R-EVD-02 Citation landing.** Clicking a tenant citation opens Contract 360
with the clause highlighted ("Why" band in the V2 design, or the Clauses
tab until R-WEB-06 lands); a market citation opens a side panel with the
record; a feature citation navigates to the route.

**R-EVD-03 AI evaluation set (spec §15.3).** A golden set of ≥ 40
questions × 3 tenants' fixtures with expected kind (answer / abstain /
redirect / refusal), expected citation corpora and expected numbers; run in
CI against the fixture gateway and on demand against Foundry; hallucination
(numeric guard interventions) must be 0 on the golden set.

### 5.12 Web V2 (pilot path)

**R-WEB-01 Ask is home.** `/` redirects to `/ask`; sign-in → workspace
picker → `/ask`. Route `/ask/:conversationId` resumes a chat.

**R-WEB-02 Two-tier navigation** (prototype): primary **Ask Contigo**
(⌘K badge, recent conversations nested, "+ New chat") and **Documents**
("N to review" badge); secondary **From your contracts**: Portfolio,
Renewals, Quote check — greyed with reroute empty states until the first
validated contract; Workspace & members (Admin) in the footer. No Home item.
Savings KPIs / opportunities stay reachable at `/savings` from actions and
from Renewals / Contract 360 (assumption A3).

**R-WEB-03 Global Ask bar** on every screen (square mark, full-width input,
two quiet suggestion links from the capability catalog for the current
screen); Enter opens a **new chat** on `/ask` with the question.

**R-WEB-04 Rich reply.** Markdown body with inline `[n]`; citation cards
(human title, page / section, snippet, first-page preview or placeholder,
corpus badge *validated contract* / *market · representative* / *Contigo*);
actions as buttons; redirect and refusal layouts (warm prose + one CTA);
abstain block only for true insufficiency. No route line, no guids.

**R-WEB-05 Documents V2.** Onboarding empty state ("First your contracts.
Then your questions."), multi-file dropzone (PDF · DOCX · XLSX · PNG · JPG),
real stages, attention filter default, per-file result cards including
**Not added** with the rejection reason, "X is now askable — Ask: when does
it expire?" hook after validation. Review reachable from the row
(`/documents?review={id}` state; the existing review components are reused).

**R-WEB-06 Contract 360 citation landing (P2).** The V2 answers band /
"Why" clauses / details / negotiation tracker layout is a separate visual
task; V2 acceptance requires only that citations land on the clause with
the original wording visible.

**R-WEB-07 Roles.** Procurement sees upload enabled (D8), delete disabled,
members read-only with "request access".

---

## 6. API contract (additions and changes)

All endpoints tenant-scoped (`X-Tenant-Id` under ADR-022, token later) and
described in `web/openapi/contigo-api.v1.json`; the TS client is regenerated
(`npm run generate:api`).

| Method & path | Purpose |
|---------------|---------|
| `GET /api/conversations` | last N conversations of the caller (id, title, scope, updatedAt) |
| `POST /api/conversations` | new conversation `{ scopeContractId? }` |
| `GET /api/conversations/{id}` | conversation with messages |
| `POST /api/conversations/{id}/messages` | ask `{ question }` → reply (below) |
| `POST /api/chat/query` | kept one release as a thin alias that creates a conversation; then removed |
| `GET /api/documents` | list (filter `status`, paging) |
| `POST /api/documents` | multipart, **one file**, returns 201 admitted / 422 rejected / 415 format |
| `GET /api/documents/{id}/preview` | PNG page 1 or placeholder |
| `POST /api/documents/{id}/reprocess` | Admin; re-OCR / re-embed |
| `DELETE /api/documents/{id}` | Admin |
| `GET /api/capabilities` | capability catalog |
| `GET /api/market/records/{id}` | one market record (for the citation panel) |
| `GET /api/insights/criticality` | ranked criticality with components |
| `GET /api/contracts/{id}/strategy` | renewal strategy pack (facts + levers + targets), same numbers Ask narrates |

Reply of a message (`kind` decides the layout):

```json
{
  "conversationId": "…", "messageId": "…",
  "kind": "answer | abstain | redirect | refusal",
  "answerMarkdown": "Salesforce ends on **15 January 2027** [1] …",
  "citations": [
    { "n": 1, "corpus": "tenant", "title": "Salesforce · MSA 2024", "subtitle": "p.12 §8.4",
      "snippet": "automatically renew for successive twelve (12) month periods",
      "documentId": "…", "contractId": "…", "page": 12, "section": "8.4",
      "previewUrl": "/api/documents/…/preview", "href": "/contracts/…?clause=…" },
    { "n": 2, "corpus": "market", "title": "Sales Cloud Enterprise · CH · 500–2 000 employees",
      "subtitle": "representative market data · mock feed · updated 2026-09-01",
      "snippet": "P25 118 · P50 132 · P75 149 CHF/user/month · n = 214", "recordId": "…" },
    { "n": 3, "corpus": "contigo", "title": "Renewals", "subtitle": "/renewals",
      "snippet": "Priority list, insight card, action and tracker." }
  ],
  "actions": [ { "label": "Open Contract 360 →", "href": "/contracts/…", "kind": "primary" },
               { "label": "Track it in Renewals", "href": "/renewals?select=…", "kind": "secondary" } ],
  "provenance": { "sources": ["tenant", "market"], "modelId": "…", "promptVersion": "answer-v2.1", "inputHash": "…" },
  "followUps": [ "Where can I push on the Salesforce renewal?" ]
}
```

Rejected upload (422):

```json
{ "rejected": true, "detectedType": "Other", "confidence": 0.93,
  "reason": "not_a_contract | no_readable_text",
  "hint": "Contigo only keeps contracts, order forms, quotes and the documents around them." }
```

---

## 7. Data model deltas

| Table | Change | Isolation |
|-------|--------|-----------|
| `conversation`, `conversation_message` | new; `tenant_id`, `user_id`, RLS policy like every tenant table (ADR-009); message stores citations / actions JSON | tenant + app filter on user |
| `supplier` | new in Suppliers.Products; `Contract.supplier_id` populated | tenant |
| `embedding` | add `page`, `section` | tenant |
| `market_embedding`, `market_record` | new; **no** `tenant_id`; readable by every tenant, writable only by the ingestion role; never joined with tenant tables | shared, read-only |
| `document` | add `page_count`, `preview_path` | tenant |
| audit actions | `document.rejected`, `document.deleted`, `document.reprocessed`, `chat.*` | tenant |

Schema lands as checked-in idempotent SQL applied by CI (ADR-021); no
`MigrateAsync` in the API.

---

## 8. Architecture and ADR implications

- **ADR-023 is superseded** by a new ADR ("Ask Contigo V2: conversations,
  admission gate, market-intelligence feed, capability catalog"). The council
  writes it; the decisions it must contain are D1–D8 and §2's three sources.
- **ADR-011 amendment.** Two corpora stay: tenant RAG (unchanged) and market.
  The market corpus now also has its **own vector index** (`market_embedding`),
  shared across tenants, written only by ingestion, never containing tenant
  content. Off-domain turns retrieve from neither.
- **ADR-001 amendment.** "Internal Dataset" = the mock market-intelligence
  feed, labelled representative; the paid API is a later provider behind the
  same seam.
- **ADR-004 amendment.** `answer` returns structured JSON, no tools;
  `classify` is reused for admission and for the domain gate with fixed
  label sets.
- **ADR-017.** PNG / JPG are first-class inputs to the `ocr` role.
- **ADR-018 / ADR-020 amendment.** V2 IA replaces the Day-1 sitemap: Ask
  home, two-tier nav, no Home item, review as a state of Documents,
  `Contigo V2 Prototype.html` as the pixel reference.
- **ADR-002 (module map).** New modules and allow-list changes, proposed for
  the council (`DependencyDirectionTests.AllowedReferences`):
  - `Contigo.Chat` → `[SharedKernel, AiGateway]` unchanged; it gains its own
    DbContext (conversations), the domain gate, planner, guards, prompt
    versions and pack DTOs. Composition (facts, benchmark, calculators)
    stays in `Contigo.Api`.
  - `Contigo.Market` (new) → `[SharedKernel, AiGateway, Benchmark]`: feed
    adapter, mock, ingestion, `market_embedding`, market retrieval,
    benchmark projection registered into `BenchmarkAdapterRegistry`.
  - `Contigo.Insights` (new) → `[SharedKernel, Benchmark]`: pure
    calculators (criticality, contract-level negotiation levers via a
    shared *priced line* input, strategy pack builder) fed by DTOs.
  - `Contigo.Suppliers.Products` → `[SharedKernel]`: `Supplier`, resolver.
  - `Contigo.Documents.Contracts` → unchanged allow-list; admission gate,
    page-aware index, `LoadAsync` consumer, preview.
- **Locked decisions** untouched: Azure, Foundry via the gateway, Key Vault,
  API-first, trunk-based flow.

---

## 9. Gap analysis — point by point

| # | Requirement (user's words) | Today in the codebase | Gap | Closed by |
|---|----------------------------|-----------------------|-----|-----------|
| 1 | Upload one or more documents | `POST /api/documents` single file; dropzone `multiple` but client loops; list in `sessionStorage` | no server list, no batch outcome, no images | R-DOC-01/02/06, R-WEB-05 |
| 2 | Only contract-related; reject recipes / photos | classification runs, `Other` is still stored and indexed | **no admission gate**, blob saved before classification | R-DOC-03/04/05 |
| 3 | Ask about the uploaded contract vs the knowledge base | tenant RAG exists (page-less chunks, `%PDF` text); market = 8 fixture rows, no narrative; no supplier names | market index + mock feed + supplier identity + page-aware chunks missing | R-MKT-01…05, R-SUP-01…04, R-DOC-07, R-EVD-01, R-CMP-01…03 |
| 4 | Renewal strategy for that contract | renewal engine, priority score, insight card, quote-line levers exist; none reachable from Ask; `MarketPosition = null` | strategy pack, contract-level levers, narration | R-STR-01…03, `/api/contracts/{id}/strategy` |
| 5 | New chat: portfolio strategy (most critical, improve, save) | no conversations; no criticality score; savings opportunities exist | conversations, criticality calculator, portfolio intents | R-CONV-01…03, R-PORT-01…03 |
| 6 | Verified info from Contigo's data only, never the web | fixture only; no Foundry; no numeric guard; abstain guard on document ids only | Foundry gateway with no tools, guards on keys / numbers / actions, AI eval set | R-AI-01…03, R-ASK-06, R-EVD-03 |
| 7 | Ask knows every Contigo feature and routes with the right citation | static suggestion strings; no catalog; no actions in replies | capability catalog, routing, feature citations | R-SYS-01…04 |
| 8 | Structured questions answered (not "not wired") | planner + handler exist, never fed with real contracts | composition of `ContractFact` from tenant store | R-ASK-03/04 |
| 9 | Off-domain / greeting / legal handled warmly | no gate | domain gate | R-ASK-02 |
| 10 | Rich reply (prose, cards, preview, deep links) | raw text, guid chips, route line | reply contract + UI | R-ASK-07/08, R-DOC-08, R-WEB-04 |
| 11 | Ask as home, recent chats, two-tier nav | Day-1 IA | IA V2 | R-WEB-01…03 |
| 12 | Live model on `demo` | env vars injected by Terraform, no client | `FoundryAiGateway` | R-AI-01 |
| 13 | Readable evidence (no `%PDF-1.4`) | no `LoadAsync`, no reprocess | load + reprocess job | R-DOC-07 |
| 14 | Provenance "representative" on every market number | `Source = fixture` in results, UI label partial | provenance labels everywhere | R-MKT-04 |

---

## 10. Acceptance on `demo` (observable)

| # | Check |
|---|-------|
| A1 | Drop `carbonara.pdf`, `nonna.jpg`, `Salesforce_MSA_2024.pdf` together → first two **Not added** with reasons, nothing stored for them (blob + DB + no embeddings); the MSA reaches `needs_review` or `completed`. |
| A2 | Drop a PNG scan of an order form → OCR → admitted → shows supplier name in Documents. |
| A3 | "ciao" / "carbonara" → warm decline + a real contract hook of this tenant; no retrieval. |
| A4 | "Posso fare causa a Salesforce?" → refusal + commercial analogue + Contract 360 action. |
| A5 | "Is my Allianz contract above market?" → below / in line / above from P25–P75 **or** insufficient data; provenance *representative · mock feed · updated …*; citation to the contract page and to the market record. |
| A6 | "Come dovrei affrontare il rinnovo Salesforce?" → dates = renewal engine, targets = calculator, levers cited, actions to Contract 360 and Renewals. |
| A7 | New chat: "Quali sono i contratti più critici e dove posso risparmiare?" → top-5 with component explanations, totals = calculator sums, actions per row. |
| A8 | "Cosa sai fare?" / "Come faccio a rivedere i campi deboli?" → module list / how-to with feature cards and working links. |
| A9 | Every reply: markdown + ≥ 1 citation card with page/section or record id + ≥ 1 action; no guid, no route line. |
| A10 | Resume a conversation from another browser → same turns, cards and actions work. |
| A11 | Another tenant's contracts never appear (existing isolation tests extended to conversations and to the market index). |
| A12 | Foundry request bodies carry no tools / grounding; AI log rows exist for every call with prompt version. |
| A13 | Golden set: 0 numeric-guard interventions; expected kinds match. |
| A14 | `/` lands on `/ask`; rail is two-tier; Portfolio / Renewals / Quote check are greyed until the first validated contract. |

---

## 11. Test requirements

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | admission gate (types, thresholds, min readable text, fixture classifier), format sniffing | `Contigo.Documents.Contracts.Tests` |
| unit | domain gate labels, planner intents, supplier resolution in questions | `Contigo.Chat.Tests` |
| unit | grounding guards (keys, numbers, actions), reply builder, prompt version pinning | `Contigo.Chat.Tests` |
| unit | criticality components, contract-level levers, strategy pack | `Contigo.Insights.Tests` (new) |
| unit | feed → benchmark rows + market notes; idempotent ingestion; provenance | `Contigo.Market.Tests` (new) |
| unit | Foundry DI swap + logging wrap; no-tools request shape; SDK allow-list | `Contigo.AiGateway.Tests`, `Contigo.ArchitectureTests` |
| API | 415 / 422 / 201 upload outcomes; list; preview; reprocess; conversations CRUD; capabilities | `Contigo.Api.Tests` |
| integration | RLS on conversations; market index shared but never tenant; reprocess removes `%PDF`; supplier back-fill | `Contigo.IntegrationTests` |
| web | rail V2, Ask home, rich reply, redirect layout, Documents multi + Not added, resume | `web/tests` (vitest) |
| e2e | V2 pilot path (A1 → A8) against `dev` | `web/e2e/v2.spec.ts` (playwright) |
| AI eval | golden set (R-EVD-03) | `backend/tests/Contigo.AiEval` (new), CI on fixture, manual on Foundry |

---

## 12. Decomposition proposal — epic-13 `ask-v2` (replaces epic-12)

Features, with the e12 feature each one absorbs:

| ID | Title | Absorbs | Layer | Depends on |
|----|-------|---------|-------|------------|
| F01 | Foundry 5-role gateway, structured output, no-tools, logging wrap | e12 F01 | backend | — |
| F02 | Market-intelligence seam, mock feed, ingestion, benchmark projection, market index | e12 F02 | backend | F01 (embed role) |
| F03 | Supplier identity: fact, entity, resolver, back-fill | — | backend | F01 |
| F04 | Documents V2 API: admission gate, formats, list, load/reprocess, preview, delete | e12 F04 | backend | F01, F03 |
| F05 | Conversations: schema, RLS, API | — | backend | — |
| F06 | Ask engine V2: gate, planner, pack, answer contract, guards, audit | e12 F03 | backend | F01, F02, F03, F05 |
| F07 | Insights: criticality, contract levers, strategy pack, `/strategy`, `/criticality` | — | backend | F02, F03 |
| F08 | Capability catalog + routing + feature citations | — | backend | F06 |
| F09 | Web V2: IA / nav, Ask home + conversations + rich reply, Documents V2, citation landing | e12 F05 | web | F04, F05, F06, F08 |
| F10 | Web V2 (P2): Contract 360 answers band / tracker, review as state | — | web | F09 |
| F11 | Integration: reprocess `dev`/`demo`, seed market feed, golden set, e2e V2 path, ADR footers | — | all | all |

Phases (fan-out order): **1** F01, F03, F05 · **2** F02, F04 · **3** F06,
F07 · **4** F08, F09 · **5** F10, F11. Estimated effort per feature L except
F03 / F05 / F08 (M).

---

## 13. Assumptions and open points

| # | Assumption (in force unless overridden at HITL) |
|---|--------------------------------------------------|
| A1 | The mock feed's record shape (R-MKT-01) is Contigo's own normalized contract; the third-party API will be mapped onto it, not the reverse. |
| A2 | Admission threshold 0.6 and min readable text 200 chars are configuration, tuned on the golden set. |
| A3 | Savings KPIs / opportunities live at `/savings` (renamed from Home), not in the rail; reachable from Ask actions, Renewals and Contract 360. |
| A4 | Conversations retention: unlimited in V2; deletion by the owner only. |
| A5 | User identity for conversations uses `X-User-Id` only until the API JWT (ADR-010) lands; the same task replaces it. |
| A6 | Answer language follows the question's language; fixtures and golden set cover Italian and English. |
| A7 | Upload stays synchronous in the request for V2 (bounded by 50 MB / file and the OCR page budget); moving it to the worker queue is a later task. |
| A8 | Processing a Quote through Documents creates a `Quote` record too, so Quote check can start from it (ties R-DOC-03 AC-4 to `POST /api/quotes`); the council may instead keep the two pipelines separate and only route. |
| A9 | Live Foundry on `dev` / `demo` is required for acceptance A2–A8; the fixture gateway proves the same paths in CI. |

Open for the council: ADR-024 text; final module placement of criticality
and levers (`Contigo.Insights` vs existing modules); whether `market_record`
lives in Postgres or only in the feed file.

---

## 14. Traceability

| This document | Existing decision / spec |
|---------------|--------------------------|
| Documents only, admission gate | spec §4.1 types, §7.1 statuses; ADR-017 OCR; D1, D3, D7 |
| Two corpora + Contigo catalog | ADR-009, ADR-011 (amended), ADR-023 (superseded) |
| Market feed mock → API | ADR-001, spec §10.2–§10.4; D2 |
| Foundry roles, no tools, logging | ADR-004, ADR-008, ADR-011, spec §14.2 |
| Deterministic numbers narrated | spec §9, §10, §12.1, Appendix C rules 6 and 10; D6 |
| Conversations | D5, ADR-009 (RLS), ADR-022 (identity posture) |
| V2 IA | ADR-018 / ADR-020 (amended), design brief V2 principles, `Contigo V2 Prototype.html` |
| Schema changes | ADR-021 |
| Evidence and citations | spec §8.3–§8.4, §15.3 |

---

## 15. Housekeeping once this document is accepted

1. Add a "superseded by `inputs/requirements.md`" banner to
   `inputs/ask-copilot-brief.md`; mark `epic-12` and `e12.yaml` as
   superseded; remove the two clean `E12-*` worktrees; do not launch
   `slice.current.yaml` (it still points at e12).
2. Run the Ask process Passata 1 on this oracle: ADR-024 + amendment footers
   (001, 004, 011, 018, 020) + epic-13 + `e13.yaml`; then HITL gate
   `reports/plan/gates/ask-v2.hitl-ok`; then fan-out, not in parallel with
   another running wave.
3. Update `reports/workitems/BACKLOG.md` and `reports/plan/slices/INDEX-ask.md`.
