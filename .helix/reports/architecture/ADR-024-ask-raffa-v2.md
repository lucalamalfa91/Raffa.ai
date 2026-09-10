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
