# ADR-024 — Ask Raffa V2: Documents-only intake with admission gate, conversations, market-intelligence feed, capability catalog

- **Status**: accepted (supersedes ADR-023)
- **Date**: 2026-09-08
- **Deciders**: product-owner (HITL decisions D1–D8, personas, non-goals) + software-architect (module map, engine pipeline, guards) + security-architect (three-source isolation, RLS on conversations, no-tools model calls) + ux-ui-designer (V2 prototype as pixel reference)
- **Locked citations**: `inputs/requirements.md` §0 (D1–D8), §2, §5–§8; `inputs/design/prototypes/Raffa V2 Prototype.html` (unpacked: `inputs/design/prototypes/raffa-v2/ia-v2.md`, `screens-v2.md`, `app.jsx`, `markup.html`); ADR-001 (fixture market adapter, no paid API on first `demo`); ADR-002 (modular monolith); ADR-004 (five Foundry roles behind `IAiGateway`); ADR-009 / ADR-011 (RLS, authz-before-retrieval, no-training, hash logging); ADR-017 (OCR in V1, images); ADR-018 / ADR-020 (IA and screens); ADR-019 (design system); ADR-021 (schema apply by CI); ADR-022 (Day-1 identity posture); spec §4.1, §7.1, §7.3, §8.3–§8.4, §10.2–§10.4, §12.1, §14.2, §15.3.
- **Diagram**: `docs/architecture/ask-raffa-v2-data-flow.md` (component / data flow + one Ask turn in sequence; Mermaid)

## Context and problem statement

ADR-023 made Ask Raffa a savings copilot over two corpora but was never
executed (epic-12 / e12 planned, worktrees clean). The HITL of 2026-09-08
replaced it with a V2 that covers the whole pilot path, not only the reply:
one or more contracts are dropped in **Documents**, non-contracts are
refused, and Ask — now the **home** of the product — answers about a
contract against a **market-intelligence feed** ("how companies close
contracts": bands, discounts, uplift caps, notice, clauses), proposes a
**renewal strategy** for that contract, and, in a **new chat**, a
**portfolio strategy** (most critical contracts, where to save, what to
improve). Every answer must come from Raffa's own sources, never the web,
and must route the user to the right screen with the right citation.

The audited baseline (`integration` @ `4a1bddf`, `inputs/requirements.md`
§1 P1–P17) has none of this: structured questions return "not wired",
semantic answers echo `%PDF-1.4` chunks, a recipe classified `Other` is
stored and indexed, supplier names do not exist, there are no conversations,
no market index, no capability catalog, and the web IA is still Day-1.

## Decision drivers

- HITL decisions D1–D8 (uploads only in Documents; the "knowledge base" is
  a third-party market feed mocked now; reject and never store non-contracts;
  V2 replaces e12; conversations server-side per user; deterministic
  criticality; PDF/DOCX/XLSX + PNG/JPG and all spec §4.1 types with
  multi-file; Admin and Procurement upload).
- "No evidence, no claim" (spec §8.4) extended to numbers and actions: the
  model narrates deterministic outputs and cites pack items only.
- Tenant content never leaves the tenant (ADR-009 / ADR-011); market
  knowledge is Raffa-owned and shared, never another tenant's PDF.
- No paid provider as a `demo` dependency (ADR-001); the seam must survive
  the provider swap unchanged (spec §10.2).
- The V2 prototype is the pixel and behaviour reference (ADR-019 tokens
  unchanged); requirements win over the prototype where they differ.
- Cheapest Foundry models per role (ADR-004); no tools / grounding on any
  role (spec §14.2, requirements R-AI-03).

## Considered options

1. **Layer V2 on e12** — run the five e12 features first, add V2 on top.
2. **Generic chat with attachments and web grounding** — let the model
   browse and accept files in the chat.
3. **V2 as one epic that absorbs e12: Documents-only intake with an
   admission gate, server-side conversations, a market-intelligence feed
   with its own index, deterministic strategy calculators, a capability
   catalog, structured no-tools answer role, V2 IA** — chosen.

## Decision outcome

**Chosen: Option 3**, because the V2 pilot path is one flow (drop → refuse
non-contracts → review → ask → strategy → new chat → citation landing) and
splitting it across two waves would ship an intermediate Ask nobody uses,
while Option 2 violates D1, D3, ADR-011 and "never invent".

The decisions, by area (ids from `inputs/requirements.md`):

| Area | Decision |
|------|----------|
| **Intake (D1, D3, D7, D8)** | Uploads only in Documents (`POST /api/documents`, one file per request; the client batches ≤ 20). Parse (native or OCR, PNG/JPG included) and `classify` run **before** any blob or row is written; admitted types are the spec §4.1 set (MSA, Order Form, SOW, Amendment, Renewal letter, Quote, Invoice, Price list, NDA, DPA) at confidence ≥ `Documents:AdmissionThreshold` (0.6). Rejected files get HTTP 422 `{ rejected, detectedType, confidence, reason, hint }`, one audit row `document.rejected` (hash, type, confidence — never content), no storage. Wrong format → 415 before any model call. Ask never accepts attachments; it returns an action to `/documents`. |
| **Three sources, one rule (§2)** | (1) validated contracts: tenant tables + tenant `embedding` (RLS; rows gain `page`, `section`); (2) market intelligence: `IMarketIntelligenceProvider` → `MarketDeal` records, projected into `IBenchmarkService` rows **and** a shared read-only **`market_embedding`** index (no `tenant_id`, written only by the ingestion job, never joined with tenant tables), both persisted in Raffa's database by the ingestion job (`market_record` for the numbers, `market_embedding` for the notes) — the provider is called **only** by that job; at question time Ask reads Raffa's own store and nothing else; (3) the versioned capability catalog. Every claim cites one of them or Raffa abstains; no web, no model memory. |
| **Conversations (D5)** | `conversation` / `conversation_message` in `Raffa.Chat` under RLS, keyed by tenant + user; API `GET/POST /api/conversations`, `GET /api/conversations/{id}`, `POST /api/conversations/{id}/messages`. User identity = token subject under ADR-010; `X-User-Id` (MSAL username) under the ADR-022 posture, non-authoritative, replaced in the same task that lands the API JWT. The global Ask bar always opens a new chat. |
| **Engine (R-ASK-01…10)** | Authorization → domain gate (`greeting`, `off_domain`, `legal`, `capability`, `needs_document`, `in_domain`; deterministic first, `classify` role on ambiguity) → planner (fixed intents) → context pack assembled in `Raffa.Api` from the three sources with `citationKey` / `corpus` / provenance → Foundry `answer` role with a versioned persona prompt and **structured JSON output**, temperature ≤ 0.2, **no tools, no grounding** → guards: every `[n]` / `citationKey` in the pack, every amount / percentage / date equal to a pack value (regenerate once, then abstain with the pack's facts), every action from the catalog → reply `{ kind, answerMarkdown, citations[], actions[], provenance, followUps[] }` with kinds `answer` / `abstain` / `redirect` / `refusal`. Off-domain never retrieves. Audit per turn; AI log via `LoggingAiGateway`, never raw text. |
| **Strategies (D6)** | Deterministic: contract vs market per priced line (P25–P75, sample, confidence, provenance "representative · mock feed · updated"), renewal strategy pack (renewal engine + priority + insight + contract-level levers generalized from `NegotiationStrategyCalculator` + benchmark + market notes + weak facts), portfolio **criticality score** 0–100 with explained components (renewal urgency, risk severity, spend weight, savings potential, open critical facts; configurable weights). The model narrates; numbers are the calculators'. |
| **Supplier identity (R-SUP)** | `supplier` becomes a critical extracted fact; `Supplier` entity in `Raffa.Suppliers.Products` (RLS); `ISupplierResolver` port in SharedKernel called from the pipeline orchestration; back-fill through reprocess. Names, never guids, everywhere. |
| **Capability catalog (R-SYS)** | Versioned catalog in `Raffa.Chat` exposed at `GET /api/capabilities`; the planner routes intents to catalog hrefs only (`/contracts/{id}`, `/renewals`, `/savings`, `/documents?…`, `/quotes`); feature citation cards (`corpus: raffa`); availability-aware (greyed modules → upload action). |
| **Foundry (R-AI)** | `FoundryAiGateway` for `ocr` (Document Intelligence), `classify`, `extract`, `embed`, `answer`; registered when `AiGateway:Endpoint` is set (Container Apps inject it), fixture otherwise; always wrapped by `LoggingAiGateway`; Azure SDKs only in `Raffa.AiGateway`; per-tenant daily token / OCR-page budgets fail visibly. |
| **Web V2 IA (R-WEB)** | Pixel and behaviour reference: `Raffa V2 Prototype.html` (unpacked `raffa-v2/`). `/` → `/ask`; `/ask/:conversationId`; two-tier rail (Ask Raffa with recent chats + "+ New chat", Documents; "From your contracts": Portfolio, Renewals, Quote check greyed until the first validated contract; no Home item; Savings at `/savings` from actions); Documents V2 (onboarding empty state, multi-file, per-file outcome incl. **Not added**, attention filter, real stages, review as a state at `/documents?review=`); rich reply; citation landing on the highlighted clause in Contract 360 with "Ask about it" → `/ask?scope=`. Divergences from the prototype are the requirements' (listed in `ia-v2.md`). |
| **Module map (ADR-002 delta)** | New `Raffa.Market` → `[SharedKernel, AiGateway, Benchmark]` (feed, mock, ingestion, index, retrieval, benchmark projection); new `Raffa.Insights` → `[SharedKernel, Benchmark]` (pure calculators fed by DTOs); `Raffa.Suppliers.Products` → `[SharedKernel]` (entity, resolver); `Raffa.Chat` keeps `[SharedKernel, AiGateway]` and gains its own DbContext, gate, planner, guards, prompts, catalog, pack DTOs; composition stays in `Raffa.Api`. Schema lands as checked-in idempotent SQL applied by CI (ADR-021): `chat.sql`, `suppliers.sql`, `market.sql`, regenerated `documents-contracts.sql`. |

### Consequences

- **Good**: one wave delivers the whole pilot path; non-contracts never
  pollute tenant RAG; every number in a reply is reproducible; the market
  provider swap is a one-adapter change; conversations survive devices; the
  prototype can be cited file-by-file by implementers and reviewers.
- **Bad**: twenty tasks across five phases with strict single-writer rules
  on `Program.cs`, `Raffa.slnx`, the OpenAPI contract and the TS client;
  a mock feed carries the risk of looking real — every market number must
  carry the *representative · mock feed* label until the live provider
  lands; per-user conversations under the ADR-022 header posture are only
  as trustworthy as `X-Tenant-Id` already is.
- **Neutral**: ADR-023's persona, two-corpus principle and refusal of legal
  advice survive unchanged; e12's five features are absorbed (F01, F02,
  F03→F06, F04, F05→F09), not lost.

## Pros and cons of the options

### Option 1 — layer V2 on e12
- Good: smaller first wave.
- Bad: ships chunk-echo-free replies without conversations, gate, market
  feed or V2 IA; two HITLs; the e12 UI would be rewritten weeks later.

### Option 2 — generic chat with attachments and web grounding
- Good: familiar chatbot shape.
- Bad: violates D1 and D3, cannot guarantee "verified from Raffa's data",
  breaks ADR-011's authz-before-retrieval for uploaded bytes, makes numbers
  unreproducible (spec §15.3).

### Option 3 — V2 as one epic (chosen)
- Good: satisfies D1–D8 and the acceptance table A1–A14 on `demo`; keeps
  every locked decision; makes the design the oracle.
- Bad: bigger wave; strict file ownership needed.

## Implications for the decomposition

- epic-13 `ask-v2` replaces epic-12 (superseded banner; e12 never launched).
  Slice `e13`, `previous: e1011`, five phases, twenty tasks
  (`skills/decompose-ask-workitems.md`).
- Every **web** task cites `inputs/design/prototypes/Raffa V2 Prototype.html`
  and an unpacked anchor (`raffa-v2/markup.html` string, `app.jsx`
  symbol, `screens-v2.md` section). Copy is verbatim from the prototype
  unless the requirements override it.
- Single writer per phase for `backend/src/Raffa.Api/Program.cs`,
  `backend/Raffa.slnx`, `DependencyDirectionTests.cs`,
  `.github/workflows/backend.yml` (schema list), `web/openapi/raffa-api.v1.json`,
  `web/src/api/client.ts`. Backend tasks after phase 1 do not edit the
  OpenAPI contract; the next phase's web task documents landed endpoints
  (shapes fixed by `inputs/requirements.md` §6) and regenerates the client.
- The admission gate runs before `IDocumentStorage.SaveAsync`; a task that
  stores first and classifies later is incomplete.
- Market rows never enter the tenant `embedding` table; a task that does is
  a defect. Market numbers without a provenance label are a defect (ADR-001).
- The `answer` role request must carry no `tools` / grounding payload; a
  compliance test asserts it on the fake HTTP handler.
- Tests: golden set (≥ 40 questions, 0 numeric-guard interventions), RLS on
  conversations and the shared market index, reprocess removes `%PDF`,
  V2 e2e path (`web/e2e/v2.spec.ts`).

## Assumptions

`inputs/requirements.md` §13 A1–A9, recorded as OQ-askv2-001…009 in
`reports/open-questions.md` with the assumption in force: mock record shape
is Raffa's own; thresholds are configuration; `/savings` out of the rail;
unlimited retention; `X-User-Id` until ADR-010; answer language follows the
question; synchronous upload; a Quote admitted in Documents is routed to
Quote check (no automatic `Quote` record); live Foundry on `dev` / `demo`
for acceptance A2–A8 while the fixture proves the paths in CI.

## Amendment (2026-09-13, wave w15 — the admission gate moves to the Worker; one definition of "validated")

Serves **NW-27, NW-61**. The **Decision outcome above is unchanged and still in
force**. Ask Raffa V2's three sources, the one-rule citation contract, the
no-tools / grounding / numeric guards, the conversations model, the deterministic
strategies, the capability catalog and the V2 IA are **all untouched**. This
footer supersedes **two clauses about *when* intake decides**, and settles one
number Ask already reports wrongly. The mechanism lives in **ADR-027**; scope is
ADR-001's w15 footer.

### 1. Superseded: "classify before you store"

Two places in this ADR say intake refuses **before** anything is written:

- the **Implications** line — *"The admission gate runs before
  `IDocumentStorage.SaveAsync`; a task that stores first and classifies later is
  incomplete"*;
- the **Intake (D1, D3, D7, D8)** row's clauses *"Parse … and `classify` run
  **before** any blob or row is written"*, *"Rejected files get HTTP 422 … no
  storage"*.

**Both are superseded, and only in their ordering half.** From w15 the gate
**splits** (ADR-001 w15 footer clause 2, resolving OQ-w15-004):

- **format and size stay in the request** — `DocumentFormatSniffer` → **415**,
  `MaxFileBytes` → **413**, both before any blob is written, so **nothing is
  stored for a non-document** and D1's 415 clause stands **verbatim**;
- **content classification moves to the Worker**, because it needs OCR and a live
  Foundry call and cannot be promised inside NW-27's 2 s budget — and, a second
  and independent reason, because `AdmissionDecision.Pages` carries the parsed
  text in memory (`AdmissionDecision.cs:67`) and a queue message cannot: a gate
  left in the request would make the worker **parse every document a second
  time**, paying Document Intelligence twice per upload forever.

**HTTP 422 leaves `POST /api/documents`.** After the split the only synchronous
refusals are 413 and 415; `not_a_contract` and `no_readable_text` both need a
parse. A refusal becomes a **terminal `Rejected` document row** whose blob is
deleted (ADR-027 §D6).

### 2. What "never store" meant, and why it survives

D3's rule — *reject and never store non-contracts* — is about a non-contract's
**content** living in the tenant's corpus. That property is **fully preserved**
and is restated here as the binding form:

> On a refusal the worker extracts nothing, creates no `contract` row, and puts
> **nothing** in the tenant `embedding` index. The blob is deleted. What persists
> is a content-free record *of the refusal* — the same thing the
> `document.rejected` audit row (`DocumentAdmissionGate.cs:222-251`, hash, type,
> confidence, never content) already persisted before this wave.

The three-source isolation of §2, the market-index separation and ADR-011's
authz-before-retrieval are therefore **untouched by this footer**. A task that
lets a rejected document's text reach the tenant `embedding` table is still a
defect, exactly as before.

**The refusal record is never askable and never counted** in "All documents" or
the review queue (ADR-001 w15 footer clause 2; `requirements.md` R-DOC-04's
*never counted* clause stands unchanged). `Rejected` is a code-only enum value:
`processing_status` is `character varying(30)` with no CHECK and no enum type
(`documents-contracts.sql:110`), so **ADR-021 needs no amendment**.

### 3. Superseded requirements, recorded here because `inputs/**` is never edited

| Source | Clause | Status from w15 |
|---|---|---|
| `inputs/requirements.md` §13 **A7** / `OQ-askv2-007` | "upload stays synchronous" | **`assumed-wrong`** (OQ-w15-003, ratified by product-owner). NW-27 makes upload asynchronous by design. |
| `inputs/requirements.md` **R-DOC-05 AC-1** | "`DocumentProcessingStatus` gains no `Rejected` value (rejected files do not exist server-side)" | **Superseded on the record** (OQ-w15-D3, ratified by product-owner; raised by ux-ui-designer, who was right to refuse to supersede an accepted HITL requirement from its own seat). |
| `inputs/requirements.md` **R-DOC-04** | *session-only / not stored* half | Superseded. **The *never counted* half stands.** |

`inputs/**` is read-only for this process, so these rows **are** the
supersession. The authority must not survive in code either: the comment at
`DocumentProcessingPipeline.cs:65-77` citing synchronous upload is retired by the
NW-27 task, and the two work-item files named in `waves/w15.md`'s "Work-item
instructions" get their partial banners.

### 4. Ask counts contracts that are not validated — a defect, not a preference

The **Engine** row's authorization → gate → planner → pack → `answer` → guards
pipeline is unchanged. What changes is **what is allowed into the pack**, and it
is a live break of this ADR's own "never invent" promise:

`RoutingContext.ValidatedContractCount` is documented as the switch that decides
whether Ask answers or offers the upload action (`RoutingContext.cs:29-31`), and
`AskCopilotService` feeds it `portfolio.Items.Count` / `TotalCount` —
**unfiltered** (`:159, 178, 191, 200, 259`). Worse, `:294` composes the abstain
reply as `$"Nothing in the {portfolio.Items.Count} validated contract(s) supports
a reliable answer."`, so the product **states to the user** that N contracts are
*validated* when N includes every bootstrap shell a still-processing document
created (`Contract.Status`'s bootstrap value is the literal `"processing"` —
`StagedExtractionService.cs:157`).

**Binding from w15: "validated" has exactly one definition — ADR-026 §D2's** (a
contract is validated iff at least one linked document is
`ProcessingStatus.Completed`), obtained from `CountValidatedContractsAsync`. The
free-text `Contract.Status != "completed"` test (`AskCopilotService.cs:790-792`)
is deleted. One swap fixes the routing gate and the user-facing sentence
together.

**Not changed**: `AnswerPromptV2.cs:33-35` and `GroundingGuard.cs:40-70` stay
exactly as they are. The `answer` role still carries no `tools` and no grounding
payload, and the compliance test on the fake HTTP handler still asserts it.

### 5. What a decomposer must carry out of this footer

1. The admission gate is **called from the worker's message handler**, not from
   the upload endpoint; the 422 block
   (`DocumentsEndpointExtensions.cs:242-252`) and its contract entry are removed.
2. `processingStatus` gains `Rejected` **in eight contract places** — the eighth
   is `listDocuments`' `status` **query parameter** (`raffa-api.v1.json:864-878`),
   which the client generator never parses, so no build failure will reveal a
   miss (client-architect's §8.1).
3. Ask's count comes from `CountValidatedContractsAsync`; no surface may
   re-derive "validated" from `Contract.Status`.
4. Every other ADR-024 instruction — market rows never in the tenant index, the
   provenance label on market numbers, the golden set, the no-tools compliance
   test, the V2 e2e path — is **unchanged and still required**.

## Amendment (2026-09-14, wave w16 — `OQ-askv2-005` retires with the conversation key, and the capability catalog is served whole)

Written by software-architect at the w16 council table. Serves **NW-07,
NW-31**. The **Decision outcome above is unchanged and still in force**: the
three sources, the one-rule citation contract, the no-tools `answer` role with
its grounding and numeric guards, the conversations model, the deterministic
strategies and the V2 IA are all untouched. Two clauses.

### 1. `OQ-askv2-005` retires with NW-07

The conversation key **is** the token subject (`oid`) on this tree:
`TryResolveUserId` exists nowhere under `backend/src`, all five
conversation/chat handlers resolve through `ICallerContext`
(`ConversationsEndpointExtensions.cs:100→111`, `:142→153`, `:188→199`,
`:237→248`; `ChatEndpointExtensions.cs:56→67`), absent identity is **401**
(`CallerContext.cs:117-120`, never the 400 the old prose promises), and
`X-User-Id` is read nowhere. `inputs/requirements.md` R-CONV-03 (`:252-256`) is
satisfied in substance.

Two things the NW-07 task must still do, and one it must not:

- **Delete the stale paragraph at `ConversationsEndpointExtensions.cs:23-37`**,
  which still asserts an `X-User-Id` fallback, a 400, and a
  `<see cref="TryResolveUserId"/>` that no longer resolves. A doc comment that
  contradicts the code beneath it is what makes a grep-based audit return a
  false verdict — the same class NW-31 and NW-32 are sweeping.
- **Record the pre-w15 rows as retired; never re-key them** (product-owner,
  ADR-001 w16 clause 1). Security-architect **S16-3** binds the mechanism: a
  backfill that re-points `conversation.user_id` by matching
  `workspace_user.Email` would move ownership through the weakest key in the
  system. Orphaned history is a smaller harm than mis-attributed history.
- **Do not implement the promoted task's DoD line `:69`** (product-owner,
  ADR-001 w16 clause 1): normalizing the subject re-introduces exactly what
  ADR-010 rejected — `oid` is opaque and case-sensitive
  (`CallerIdentity.cs:60-68,80-81`).

The one-comparison-rule ruling for `ExternalSubjectId` is **ADR-010's**
(security-architect S16-1/S16-2), cited here and **not** decided here.

### 2. The capability catalog is served whole; the role filter is deleted with `X-Role` (NW-31)

`GET /api/capabilities` stops reading a client-asserted role **and stops
filtering**: it returns every catalog row, each carrying its own `roleGate`.

Evidence, gathered because the alternative was the tempting one:

- the endpoint takes **no tenant and no caller** (`CapabilitiesEndpointExtensions.cs:62`),
  and its own doc `:44-48` records that it deliberately never 401s "since the
  catalog itself is not sensitive, tenant data";
- the catalog is **static** (`Raffa.Chat/Application/Capabilities/CapabilityCatalog.cs:31`)
  and contains no tenant fact;
- **Ask's own routing already reads it in-process with no gate at all**
  (`AskCopilotService.cs:181-206`, `:900`) — so the HTTP filter never governed
  the answer a user actually receives;
- **nothing in `web/src` reads `roleGate`**: the SPA consumes the catalog only
  to pick suggestion chips (`askSuggestions.ts:85-94`, `askViewModel.ts:384-390`).

This adopts **security-architect S16-6's named-acceptable alternative** rather
than its primary form. The primary form — membership-derived admin rows —
requires "no token ⇒ 200 non-admin catalog, never 401", which `ICallerContext`'s
ladder cannot express; it would need a bespoke soft-auth path on a route that
takes no tenant, and because the client sends no `X-Tenant-Id` on this call
(`client.ts:2760-2765`) a tenant-scoped version returns **400 on every request**
from a shell component rendered on every screen. `roleGate` **stays on the
wire** as the presentation signal ADR-012's w14 footer makes the client's
business.

**Consequence, recorded rather than discovered**: an admin-gated capability's
example questions may now surface as a suggestion chip for a non-Admin. That is
a **label, never an authorization** — every admin action behind it is enforced
server-side by membership (NW-08's ladder, ADR-025), and the catalog discloses
only *which admin features exist*. Hiding them is client presentation work, not
a server gate: **OQ-w16-sa-02**, W17.

`waves/w16.md` records this under NW-07 and NW-31.

## Amendment (2026-09-15, wave w17 — the review bar is not the admission gate, and the two shapes a market claim may take)

Serves **NW-71**, **NW-20**, **NW-22** and **NW-62**. Nothing above is rewritten;
the two-corpus rule, the admission gate in the Worker and the capability catalog
all stand. This ADR is the home of the market corpus because **ADR-023 is
superseded by this one** and its `IBenchmarkService` principle survives here
(`ADR-023:83-92`).

### A. One review bar, decided by the server (NW-71)

**1. The server decides on the raw stored double: `confidence >= 0.90`, no
rounding** (OQ-w17-002, software-architect half). Rounding *before* comparing is
the one variant that must not ship: it makes `0.895` accept in the payload and
review in the audit row — the record disagreeing with itself. Presentation
rounding is a separate, later step and is floored, never rounded up across the
bar (ADR-001 w17 clause 2).

**2. One policy type, consumed by both deciders.** `ExtractionConfidencePolicy`
lives in `Raffa.Documents.Contracts` and is consumed by **both**
`StagedExtractionService.DetermineDocumentStatus` (`:863`, applied `:236`) **and**
`DocumentQueryService.IsWeak` (`:46-52`). Today these are **duplicated pairs**
(`StagedExtractionService.cs:78,88` vs `DocumentQueryService.cs:33,42`) — which is
raw-file correction 4 on the wave record — so changing one **desyncs the badge
from `needs_review`**. `StagedExtractionService.cs:76-77` already calls its bar
"a config knob, not a hard-coded business rule"; this discharges that note.

**3. Which sites retire and which explicitly stay** (OQ-w17-003,
software-architect half — the scope fence):

| Site | Ruling |
|---|---|
| `StagedExtractionService.cs:78,88` + `DocumentQueryService.cs:33,42` | **retire** into the one policy |
| `DocumentAdmissionOptions.AdmissionThreshold` (`:33`) | **stays** — "is this file admitted at all" is a different decision (this ADR's w15 footer, ADR-027) |
| `Raffa.Quotes`' own bar | **stays** — separate domain (spec §11) |
| `SavingsProvenanceClassifier.cs:37,46` (0.7 / 0.4) | **stays** — provenance banding, not a review decision |
| `CriticalityScoreCalculator.cs:35` (0.8) | **stays this wave**, but see clause 4 |

**4. ⚠ The collision no lane owned: NW-71 × NW-62 meet inside
`StrategyPack.OpenWeakFacts`.** That collection is filtered at **0.8**
(`CriticalityScoreCalculator.cs:35`, `StrategyPack.cs:23-25`) and feeds the very
payload NW-62 is told to consume — while Review moves to **0.90**. If the 360
renders `OpenWeakFacts` as a review statement, two screens carry two definitions
of "weak" on the same contract. **Ruling: `OpenWeakFacts` is a *criticality*
input, not a review decision, and NW-62 renders the strategy sections but not
`OpenWeakFacts` as a review statement this wave.** The two bars reconcile in W18
(**OQ-w17-sa-01**).

**5. On the wire.** Each evidence row carries `decision` and `decidedAt`
(ADR-003 w17 clause 1), and **the threshold is exposed once** so the web never
hardcodes 90 and the review legend is server-fed — the legend at
`ReviewHeader.tsx:87-97` goes from three items to two. Two binding shapes:

- **The decision is read-only to the client.** A client-supplied `decision` is
  rejected **400** and never persisted. Letting a displayed band become an
  authorization-relevant input is the error §3 of this ADR's lineage forbids, and
  security-architect requires it.
- **It is not a boolean.** The contract must express **three** field states —
  `auto_accepted`, `human_accepted`, `review_required` (the derivation table is
  in the **ADR-003 w17 footer clause 1**) — because ADR-001 w17 clause 8
  requires them to stay distinguishable; a boolean collapses two of them. This is
  the wire shape product-owner asked this seat to name.

### B. A market claim has one wire shape and one resolution (NW-22, NW-62, NW-20)

**6. Two honest shapes, one payload.** ADR-001 w17 clause 4 gives a market claim
exactly two forms — a **representative position** carrying adapter, sample size
and as-of date, or an explicit **"insufficient market data"**. On the wire that is
one nullable structure `{position, adapter, sampleSize, asOf}` plus an explicit
absent state. Never a percentile alone, never "market" unqualified, never "Not
determined". No band → the abstain path, never a fabricated number.

**7. One resolution per screen, not two.** NW-20's 360 `benchmark` and NW-62's
"where you can save" must use the **same** `IBenchmarkService` call against the
**same** resolved `(supplier name, geography)` key. Resolving twice and getting
two answers on one screen is the defect this clause exists to prevent. The name
half is already solved (`ISupplierNameLookup`, registered
`ServiceCollectionExtensions.cs:41`); **geography is the workspace country,
resolved in the host** and passed into `StrategyInputs` (OQ-w17-004), because
`Raffa.Insights` is fenced to `[SharedKernel, Benchmark]` and cannot read the
workspace itself. With both resolved, `ToPricedLines`' hardcoded
`Benchmark: null` (`:47-48`) becomes a real call — that file names this method as
the place (`:50-51`).

**8. No per-contract geography column this wave.** It is a new capability plus a
migration, and the cap does not afford it; the workspace country is the honest
default, labelled representative. A paid feed stays an ADR-001 §1.2 non-goal
(NW-52 DEFERRED).

**9. The 360 consumes `GET /api/contracts/{id}/strategy`** (OQ-w17-005,
software-architect half). It **already** returns both `WhenYouMustMove` and
`WhereYouCanPush` (`StrategyPack.cs:26-32,51-56`) and is **already in the
generated client** (`schema.ts:724`) with no consumer. Computing a second answer
in the view model would be exactly the divergence ADR-012 forbids. "When you must
move" is additionally answerable from persisted columns today
(`Contract360QueryService.cs:94,96`) — raw-file correction 2.

**10. Both empty 360 members gain real shapes** (NW-20). `benchmark` and
`activity` are memberless records reserved for R3/R4
(`Contract360Result.cs:209-223`) and are filled by **host composition in
`Raffa.Api`**, not by `Contract360QueryService` (ADR-002 w17 clause 3). For
`activity` this seat will not invent a domain concept: the only honest existing
source is the **audit trail**, projected read-only through the host with an
action whitelist — and that is a **security question before it is an
architecture one**, since a 360 tab is a wider audience than ADR-011's w16 reader
ruling contemplated (**OQ-w17-sa-03**). If security refuses, the member is
removed and the record type deleted, per ADR-001 w17 clause 3 — **an
unconditional `[]` with no decision is not acceptable**. `benchmark` has no such
option: NW-62 consumes it.

`waves/w17.md` records this under NW-71, NW-20, NW-22 and NW-62.

**11. OQ-w17-sa-03 is ruled, so clause 10's conditional resolves: both members
land, and their shapes are named here** (NW-20, round 2). Security permitted
`activity` as a **contract-scoped provenance projection** and refused it as an
**audit reader**, under five conditions (ADR-011 w17 clause 22). Clause 10 said
"if security refuses, the member is removed"; security did not refuse, so the
member lands — and the two memberless record types (`Contract360Result.cs:216`,
`:223`) gain exactly the members below. A permitted member with no named shape is
still a guess, and this seat owns the payload.

**11a. `Contract360ActivityEntry(OccurredAt, Action, ActorLabel)` — three
members, each from a bounded column.** `OccurredAt` is `AuditEvent.Timestamp`;
`Action` is the audit action constant admitted only from the **closed allow-list**
(condition 2, default-deny), `varchar(100)` (`AuditEventConfiguration.cs:21`);
`ActorLabel` is `Actor` (`varchar(200)`, `:20`) **resolved to a display label** —
`system:<component>` verbatim, a human event as the display name the member list
already discloses, an unresolvable subject as "removed member", **never a raw
GUID** (condition 4). This is exactly ADR-001 w17 clause 3's *when · actor · what
changed*: that clause's own list — upload, processing completed, validated,
reprocessed, correction, renewal action, negotiation tick, savings outcome — **is
a list of action names**, so the action constant *is* the "what changed" at the
granularity V1 promises.

**11b. `AuditEvent.Detail` is never projected — this is the clause that keeps
condition 3 true.** It is the trail's only human-readable column, so it is what an
implementer reaches for, and it is `text` with **deliberately no `HasMaxLength`**
— *"unbounded free-form context"* (`AuditEventConfiguration.cs:24`): no schema, no
vocabulary, no length bound. **Verified rather than feared**: writers already
interpolate **contract-derived dates** into it —
`Detail: "milestone=…; thresholdDays=…; milestoneDate={…:yyyy-MM-dd}"`
(`RenewalAlertService.cs:254-255`, again `RenewalThresholdScheduler.cs:148-149`)
— on a `Contract` resource. Projecting `Detail` would break security's *names,
never values* **on day one**, not hypothetically, and the table is **append-only**,
so the leak would be permanent and uncorrectable. **A per-action exception is
refused too**: `document.validated` happens to write only field names there
(`DocumentValidationService.cs:124-135`), but whitelisting a free-text column
breaks the first time a writer adds context, in a task that never mentions
security. If an entry ever needs more than *when · who · what*, it gets a
**structured** source — a column or a typed detail contract — in a later wave,
never by reading this one.

**11c. `Contract360BenchmarkEntry(Metric, Status, Position?, AdapterName?,
SampleSize?, AsOf?)`, and an empty array is refused.** ADR-001 w17 clause 4 gives a
market claim exactly two honest shapes, so the entry carries either a
**representative** position with its **adapter, sample size and as-of date**, or
`Status = insufficient_data` with the band fields null. **The abstain is an entry,
not an empty list**: `[]` is precisely today's defect
(`Contract360QueryService.cs:211`) and a client cannot tell "the adapter
abstained" from "nobody ever wired this" — the unconditional `[]` ADR-001 w17
clause 3 refuses. Same resolved `(supplier name, geography)` key and the **same
single call** as NW-62 (clause 7): one resolution, two consumers.

**11d. Contract delta.** Both records gain members ⇒ `raffa-api.v1.json` moves in
the **epic-21 contract task** (single-writer constraint 2), and the five
conditions travel into that task's DoD together with the pinning test ADR-011 w17
clause 22 names.

## Amendment (2026-09-21 — a document needs review only when a human has something to decide)

Product-owner ruling on the first real `demo` uploads through the Foundry
gateway: a document whose every found fact cleared the auto-accept bar still
parked in `needs_review` with "Review 0 fields" and required a manual "Mark
as validated". The cause was the w17 rule that an empty scalar stage
(metadata, commercial terms, dates) is "nothing where something was
expected" and routes the document to review, while `weakFactCount` and the
review screen only count facts a human can decide on. Two numbers, one state.

**Rule.** `StagedExtractionService` now marks a stage `NeedsReview` exactly
when it produced a fact below `ExtractionConfidencePolicy`; a stage that
found nothing — scalar or list — completes. `DetermineDocumentStatus` is
unchanged in shape: the document is `needs_review` iff a stage needs review,
a stage failed, or the classification sits below the bar; otherwise it
completes on its own. An absent scalar field keeps surfacing on the review
screen under "Not found in the document" (NW-64), where the user may type
it, and never blocks validation — as before.

**What still parks a document with "Review 0 fields".** A failed stage (a
"missing signal": gateway error, malformed payload). That case is now
explainable: `GET /api/documents` carries the failed job's `errorDetail` on
a `NeedsReview` row too, and the Documents table shows it under the file
name ("One extraction step failed: …"). Reprocessing is the resolution, not
a sign-off.

Consequences: `backend/README.md`'s pipeline-rules paragraph, the
`StagedExtractionServiceTests` case for a text with no cues (now: every
stage completes, document completes), `DocumentListItem.ErrorDetail`'s
contract. The admission gate (w15/w17) is untouched.

## Amendment (2026-09-22, wave w20 — a fifth reply kind, `draft`, and a capability-gap gate)

ADR-030 records the decision; this footer records what changes in the engine
this ADR owns. **Clause 1.** `DomainGate.Classify` gains a seventh label,
`CapabilityGap`, checked after the legal lexicon and before the capability
lexicon: a request for an operation Raffa cannot perform (send an email, set a
reminder, export a file, raise a purchase order) never reaches the planner and
never becomes a retrieval-then-abstain. **Clause 2.** The reply contract gains
a fifth kind, `draft`, and a required, nullable `payload` on every reply and
every stored message (`conversation_message.payload_json`); "cites or
abstains" reads "cites, drafts from cited facts, or abstains". A `draft`
carries the honest preface in `answerMarkdown`, the email in `payload.draft`,
the pack items it was written from as `citations`, and never an inline `[n]`.
**Clause 3.** The draft is guarded by `DraftGuard` (the `[n]` rule inverted,
`NumericGuard` unchanged, keys in the pack), retried once, and falls back to a
deterministic template — the draft path cannot abstain; a fallback is audited
as a guard intervention under a new audit action, `chat.drafted`. **Clause
4.** A capability-gap `redirect` may carry `followUps` (one validated supplier
per chip) and `payload.feedbackOffer`; `actions[].kind` gains `external`,
server-authored only. **Clause 5.** The feedback loop (`POST
/api/conversations/{id}/feedback`, `feature_request`, the GitHub publisher) is
ADR-030 D5's; this ADR's conversation store only gains the column and the
table. The body above and every earlier footer are untouched.
