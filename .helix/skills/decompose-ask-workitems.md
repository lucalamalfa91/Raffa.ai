# Decomposition — Ask Raffa V2 (epic-13 / e13, replaces epic-12 / e12)

Ids start at **E13/F01/US01/T01**. Templates: `templates/*.md`.

Oracles: `inputs/requirements.md` (§5 requirements, §6 API, §7 data, §12
decomposition) + ADR-024 + `inputs/design/prototypes/raffa-v2/ia-v2.md`
and `screens-v2.md` (authored from `Raffa V2 Prototype.html`).

## Design-citation rule (checker fails without it)

Every **web** task cites, in `## Coding objective` or `## Context`:

1. `inputs/design/prototypes/Raffa V2 Prototype.html` (the bundled export), and
2. the unpacked anchor it implements: a `raffa-v2/markup.html` string
   (e.g. *"From your contracts"*, *"Nothing needs you right now."*), an
   `app.jsx` symbol (e.g. `primaryNav`, `askScope`, `ask()`), and the
   `screens-v2.md` section.

Backend tasks that shape user-visible copy (rejection reasons, redirect
prose, intents) cite `app.jsx` → `ask()` and `screens-v2.md` too.

## Features and tasks (all rows from the gap report)

| Task | Title | produces | depends_on | Phase | Layer | Effort |
|------|-------|----------|------------|-------|-------|--------|
| E13/F01/US01/T01 | V2 solution scaffold: `Raffa.Market`, `Raffa.Insights` (+ `.Tests`), `Raffa.Suppliers.Products.Tests`, `Raffa.AiEval`; `backend/Raffa.slnx`; `DependencyDirectionTests` allow-list | v2-scaffold | — | 1 | backend | M |
| E13/F01/US01/T02 | `FoundryAiGateway` five roles, DI swap on `AiGateway:Endpoint`, `LoggingAiGateway` wrap, structured `classify`/`answer` output, no-tools compliance | foundry-gateway | — | 1 | backend | L |
| E13/F05/US01/T01 | Conversations store: `Conversation` / `ConversationMessage`, `ChatDbContext`, RLS migration + `chat.sql`, `ConversationService`, `AddChatModule` overload | conversations-store | — | 1 | backend | M |
| E13/F04/US01/T01 | Documents admission gate before persistence, magic-byte formats (PDF/DOCX/XLSX/PNG/JPG → OCR), `DocumentsEndpointExtensions.cs` (moved out of `Program.cs`), 415 / 422 replies, OpenAPI update | documents-admission | — | 1 | backend | L |
| E13/F09/US01/T01 | Web shell V2: two-tier rail, `/` → `/ask`, `/ask/:conversationId`, `/savings`, `/documents?review=`, greyed modules, Ask bar opens a new chat | web-shell-v2 | — | 1 | web | L |
| E13/F02/US01/T01 | Market-intelligence seam: `IMarketIntelligenceProvider`, `MarketDeal`, mock feed (≥ 60 records), benchmark projection adapter, `IMarketKnowledgeRetrieval` (in-memory), provenance | market-feed-mock | v2-scaffold | 2 | backend | L |
| E13/F03/US01/T01 | Supplier entity, `ISupplierResolver` port, migration + `suppliers.sql`, RLS, normalization | supplier-entity | v2-scaffold | 2 | backend | M |
| E13/F04/US01/T02 | Documents V2 API: `IDocumentStorage.LoadAsync`, list, preview, reprocess (re-OCR / re-embed), delete, page-aware embeddings | documents-v2-api | documents-admission, foundry-gateway | 2 | backend | L |
| E13/F05/US01/T02 | Conversations API (`/api/conversations`), `Program.cs` registration of the Chat DbContext, RLS integration tests | conversations-api | conversations-store | 2 | backend | M |
| E13/F07/US01/T01 | Insights: criticality score, priced-line negotiation levers (generalized `NegotiationStrategyCalculator`), strategy pack builder, `InsightsEndpointExtensions.cs` (unmapped) | insights-calculators | v2-scaffold | 2 | backend | L |
| E13/F08/US01/T01 | Capability catalog in `Raffa.Chat`, routing table, `CapabilitiesEndpointExtensions.cs` (unmapped) | capability-catalog | — | 2 | backend | M |
| E13/F09/US01/T02 | Rich reply components: markdown, citation cards with corpus badge + preview, actions, redirect / refusal / abstain layouts | web-rich-reply | web-shell-v2 | 2 | web | L |
| E13/F06/US01/T01 | Ask engine V2: domain gate, planner, context pack, answer contract, guards, messages endpoint, `Program.cs` wiring (Suppliers, Market, Insights, endpoint maps) | ask-engine | foundry-gateway, market-feed-mock, supplier-entity, documents-v2-api, conversations-api, insights-calculators, capability-catalog | 3 | backend | L |
| E13/F02/US01/T02 | Market index: `market_embedding` + `market.sql`, ingestion (embed role), DB-backed retrieval (DI swap), `MarketEndpointExtensions.cs` | market-index | market-feed-mock, foundry-gateway | 3 | backend | L |
| E13/F03/US01/T02 | Supplier extraction: `supplier` critical fact, resolver call in the pipeline, weak-supplier review, back-fill through reprocess | supplier-extraction | supplier-entity, documents-v2-api | 3 | backend | L |
| E13/F09/US01/T03 | Web Documents V2: multi-file, per-file outcome, **Not added** cards, attention filter, list from API, real stages, review state; OpenAPI + client regen (documents) | web-documents-v2 | documents-v2-api, web-shell-v2 | 3 | web | L |
| E13/F06/US01/T02 | AI golden set (`Raffa.AiEval`): ≥ 40 questions, expected kinds / citations / numbers, CI on the fixture gateway | ask-golden-set | ask-engine | 4 | backend | M |
| E13/F09/US01/T04 | Web Ask V2: conversations in the rail, resume, new chat, reply-contract wiring, scope line, suggestions from capabilities, `?scope=`; OpenAPI + client regen (conversations, messages, capabilities, market, insights) | web-ask-v2 | ask-engine, market-index, web-rich-reply, web-documents-v2 | 4 | web | L |
| E13/F10/US01/T01 | Contract 360 citation landing (`?clause=` highlight, original wording) + "Ask about it" → `/ask?scope=` | web-contract360-landing | web-shell-v2 | 4 | web | M |
| E13/F11/US01/T01 | Integration: CI jobs (market seed, tenant reprocess), `web/e2e/v2.spec.ts`, demo acceptance checklist, README sweep | v2-integration | ask-golden-set, web-ask-v2, web-contract360-landing, supplier-extraction, market-index | 5 | backend | L |

Stories: one story per feature (`us-01-<slug>`); F01, F02, F03, F04, F05,
F06, F09 carry 2–4 tasks, the others one task (delta process — the generic
2–5 rule is relaxed, as for epic-12).

## Single writer per file, per phase (checker enforces)

| File | Phase 1 | Phase 2 | Phase 3 | Phase 4 | Phase 5 |
|------|---------|---------|---------|---------|---------|
| `backend/src/Raffa.Api/Program.cs` | F04/T01 | F05/T02 | F06/T01 | — | F11/T01 |
| `backend/Raffa.slnx` | F01/T01 | — | — | — | — |
| `backend/tests/Raffa.ArchitectureTests/DependencyDirectionTests.cs` | F01/T01 | — | — | — | — |
| `.github/workflows/backend.yml` (schema SCRIPTS list) | F05/T01 | F03/T01 | F02/T02 | — | F11/T01 |
| `web/openapi/raffa-api.v1.json` | F04/T01 | — | F09/T03 | F09/T04 | — |
| `web/src/api/client.ts`, `generated/schema.ts` | — | — | F09/T03 | F09/T04 | — |
| `backend/src/Raffa.Chat/Infrastructure/ServiceCollectionExtensions.cs` | F05/T01 | F08/T01 | F06/T01 | — | — |
| `backend/src/Raffa.Documents.Contracts/Application/Extraction/DocumentProcessingPipeline.cs` | F04/T01 | F04/T02 | F03/T02 | — | — |
| `backend/src/Raffa.Api/appsettings*.json` | — | F05/T02 | F06/T01 | — | — |

Backend tasks in phases 2–4 do **not** edit the OpenAPI contract; the web
task of the next phase documents the landed endpoints (shapes fixed by
`inputs/requirements.md` §6) and regenerates the client.

**Creation counts as writing (lesson of e13 phase 3).** F02/T02 created
`backend/src/Raffa.Api/MarketEndpointExtensions.cs` and F06/T01 mapped
`MapMarketEndpoints()` in `Program.cs` in the same phase; F06/T01 had to stub
the file to compile, the barrier union-merged the two files, and CI failed on
18 errors. A file created by a task may not be named by any other task of the
same phase; map a new endpoint file one phase later. Before cutting a slice:
`python scripts/check_single_writer.py --slice e13` must exit 0.

After `wave-spec.ask.yaml`:

```
python scripts/cut_ask_slices.py
```

## Checker fail

- missing `ask-v2-gaps.md`, ADR-024, epic-13, `wave-spec.ask.yaml` or `e13.yaml`
- a write to e01–e11 / e1011 / e12 / epic-01…12 / `slice.current.yaml`
- a web task without the design citation (bundled + unpacked)
- two same-phase tasks on one file of the table above
- a task that mixes tenant `embedding` with the market index, persists
  rejected documents, adds attachments to the chat, implements legal
  advice, a paid market API client, or web / tool grounding

## Lesson from the e13 wave — the barrier merge concatenates, it does not reconcile

Two files broke on PR #67 because the "single writer per file, per phase"
table above did not cover them. Both failures have the same shape: the phase
barrier's `merge_auto` union merge **concatenates** two versions of a region
instead of choosing between them. Extend the table with two more rules.

**1. Creation is a write. A file one task creates must not be *required to
exist* by another task of the same phase.**
`backend/src/Raffa.Api/MarketEndpointExtensions.cs` was created by
E13/F02/US01/T02 (`market-index`) and, in the same phase 3, by
E13/F06/US01/T01 (`ask-engine`), which owns the `Program.cs` wiring and had to
map the endpoint. F06/T01's own doc comment records the trap exactly: *"this
endpoint's first writer — the market-ingestion task that owns
`Raffa.Market`'s own persisted `market_record` store … has not landed in
this wave"*. It had not landed because it was running **in the same phase**,
in a sibling worktree F06/T01 could not see. Neither task was wrong on its
own; the union merge then put both handlers back to back (duplicate `deal`,
undefined `endpoints`/`detail`) and `dotnet build Raffa.slnx` failed.
Reconciled by hand in `50b38a7`. When task A creates an endpoint/extension
file and task B must call into it, put B **one phase later** than A, or make
the call site itself A's deliverable — never let two tasks in one phase both
bring the file into existence.

**2. A prose file several tasks append to needs a single writer per phase.**
`backend/README.md` is edited by nearly every backend task (the
`readme-hygiene` skill requires it — ten of the e13 task commits touched it).
Its `## Solution` section is not a list — it *describes the current state* —
so each task rewrote it, and the union merge kept every rewrite. The damage
compounded barrier by barrier: one copy of the "V2 scaffold" paragraph at the
phase-1 barrier, two at the phase-3 barrier, and by `50b38a7`
`Raffa.Documents.Contracts/`, `Raffa.Audit/` and `Raffa.AiGateway/` were
each listed three times in the module tree, with three mutually contradicting
descriptions of the same modules. Rebuilt by hand in `1ca7888`. The same risk
applies to any index or catalog file (`INDEX.md`, an ADR index, an OpenAPI
`paths` object).

Rule to apply when decomposing:

- Append-only regions (a new row, a new bullet, a new section at the end) may
  have several writers per phase — union merge is safe there.
- Any region that states *the current state* — a module tree, a "what exists
  today" paragraph, an index — gets **one nominated writer per phase**, listed
  in the table above like `Program.cs` already is. The other tasks of that
  phase say nothing about it; the nominated writer describes the whole phase's
  result.
- Files to add to the table for a wave of this shape: every
  `*EndpointExtensions.cs` any task creates, `backend/README.md` (the
  `## Solution` section), `web/README.md`, and any `INDEX.md`.
