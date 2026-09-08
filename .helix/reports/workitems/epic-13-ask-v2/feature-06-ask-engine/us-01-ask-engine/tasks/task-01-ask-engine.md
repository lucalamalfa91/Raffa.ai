---
id: E13/F06/US01/T01
type: task
story: us-01-ask-engine
wave: 13
status: live
target_repo: contigo-backend
---

# task-01-ask-engine — Domain gate, planner, context pack, answer contract, guards, messages endpoint, host wiring

## Coding objective

Replace the single-turn `POST /api/chat/query` behaviour with the V2
copilot (`inputs/requirements.md` R-ASK-01…10, R-CMP-01…03, R-STR-01/03,
R-PORT-02/03, §6). In `backend/src/Contigo.Chat/Application/`:

1. `Gate/DomainGate` — labels `greeting`, `off_domain`, `legal`,
   `capability`, `needs_document`, `in_domain`; deterministic rules first
   (greeting lexicon it/en, legal lexicon "sue / causa / tribunale / valid
   under law / enforceable", capability lexicon from `CapabilityRouting`,
   supplier-name resolution through `ISupplierNameLookup` /
   `ISupplierResolver` names of the tenant → unknown named supplier =
   `needs_document`), then `IAiGateway.ClassifyAsync` with the fixed label
   set on ambiguity. Off-domain never reaches retrieval.
2. `Planning/IntentPlanner` — fixed intents `structured_fact`, `clause`,
   `market_compare`, `renewal_strategy`, `portfolio_strategy`, `savings`,
   `document_status`, `quote_route`, `navigate`; reuse and extend
   `AskContigoQueryRouter` / `DeterministicQueryPlanner` (they stay pure);
   the prototype outcomes in `inputs/design/prototypes/contigo-v2/app.jsx`
   `ask()` and `contigo-v2/ia-v2.md` "Ask intents" are the oracle.
3. `Pack/ContextPack` DTOs — `PackItem(citationKey, corpus tenant|market|
   contigo|calc, title, subtitle, page?, section?, snippet, href?,
   previewUrl?, recordId?, provenance, values[] numeric facts)`,
   `PackBudget` (`Chat:PackTokenBudget`), `PackBuilder` (composition lives
   in `Contigo.Api`, see below — Chat only owns the DTOs and the budget).
4. `Answering/AnswerPromptV2` — versioned persona prompt file
   `Prompts/answer/v2.1.md` (savings specialist, never a lawyer, never
   invent, cite only pack keys, answer in the question's language, output
   the JSON of `AiAnswerResult` v2), `AnswerComposer` calling
   `IAiGateway.AnswerAsync` with the pack + last N turns.
5. `Guards/GroundingGuard` (every `[n]` / citationKey in the pack; every
   actionKey resolvable), `Guards/NumericGuard` (every currency amount,
   percentage and date in `answerMarkdown` equals a pack value, normalized
   and currency-aware; dates in the pack's formats), `Guards/RegenerateOnce`
   (one retry naming the violation, then downgrade to `abstain` with the
   pack's own facts); extend `AbstainGuard` to keys and corpora.
6. `Reply/CopilotReply` — `{ kind, answerMarkdown, citations[], actions[],
   provenance { sources, modelId, promptVersion, inputHash }, followUps[] }`
   with `citations[]` = `{ n, corpus, title, subtitle, snippet, documentId?,
   contractId?, page?, section?, previewUrl?, href?, recordId? }` and the
   redirect / refusal builders (warm decline + portfolio hook naming a real
   contract or saving of this tenant, or the upload invite; legal refusal +
   commercial analogue + Contract 360 action).
7. Audit per turn (`chat.answered` / `chat.redirected` / `chat.refused` /
   `chat.abstained`, counts + pack hash, never text).

In `Contigo.Api`: `ChatEndpointExtensions.cs` becomes the **pack
composition root** (`AskCopilotService`): authorization scope first;
tenant facts via `PortfolioQueryService`, `Contract360QueryService`,
renewals (`RenewalEngine`, `PriorityScoreCalculator`, `RenewalPipelineBuilder`),
`SavingsOpportunityService`, weak facts; clause chunks via
`EmbeddingRetrievalService.SearchAsync` (page-aware, top-k); market via
`IBenchmarkService` + `IMarketKnowledgeRetrieval`; calculators via
`Contigo.Insights` (`CriticalityScoreCalculator`, `StrategyPackBuilder`);
capability entries via `CapabilityRouting`. Add
`POST /api/conversations/{id}/messages` to `ConversationsEndpointExtensions.cs`
(`{ question }` → runs the pipeline, appends both messages through
`ConversationService`, returns `CopilotReply` with `conversationId` and
`messageId`); keep `POST /api/chat/query` as a thin alias that creates a
conversation and delegates (one release). `Program.cs` (this task is its
phase-3 writer): register `AddSuppliersProductsModule(connectionString)`,
`AddMarketModule()`, `AddInsightsModule()`; map `MapMarketEndpoints()`,
`MapInsightsEndpoints()`, `MapCapabilitiesEndpoints()`; add
`ConnectionStrings:Suppliers` to `appsettings.Development.json`;
`Benchmark:ActiveAdapter` default stays "market-feed". The fixture gateway
must drive all tests: give `FixtureAiGateway.AnswerAsync` a deterministic
v2 behaviour when a pack is supplied (echo a markdown that cites the first
N pack keys and copies their values verbatim — no chunk concatenation),
so the guards and the golden set run without Foundry.

## Parent story AC covered
- AC-1 … AC-8 (AC-9 isolation test extended here; golden set in T02)

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/src/Contigo.Chat/Application/Gate/*`, `Planning/*`, `Pack/*`, `Answering/*`, `Guards/*`, `Reply/*`, `Prompts/answer/v2.1.md` | new |
| `backend/src/Contigo.Chat/Application/AbstainGuard.cs`, `AskContigoQueryRouter.cs`, `DeterministicQueryPlanner.cs`, `RagAnswerService.cs` | extend / reuse |
| `backend/src/Contigo.Chat/Infrastructure/ServiceCollectionExtensions.cs` | register the engine (phase-3 writer) |
| `backend/src/Contigo.AiGateway/Fixtures/FixtureAiGateway.cs` | deterministic v2 answer when a pack is supplied (F01/T02 landed in phase 1; this is the only AiGateway edit this phase) |
| `backend/src/Contigo.Api/ChatEndpointExtensions.cs` | pack composition + alias |
| `backend/src/Contigo.Api/AskCopilotService.cs` | new composition service |
| `backend/src/Contigo.Api/ConversationsEndpointExtensions.cs` | `POST /{id}/messages` |
| `backend/src/Contigo.Api/Program.cs`, `appsettings.Development.json` | module registrations, endpoint maps, connection string (phase-3 writer) |
| `backend/tests/Contigo.Chat.Tests/{Gate,Planning,Guards,Reply}/*` | unit tests incl. ciao / carbonara / legal / Allianz band / 120 days / criticality narration |
| `backend/tests/Contigo.Api.Tests/ChatEndpointTests.cs`, `ConversationsEndpointTests.cs` | reply contract, no guid / route line, alias |
| `backend/tests/Contigo.IntegrationTests/AskContigoRagCrossTenantIsolationTests.cs` | extended to the messages endpoint |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 (pipeline, reply contract, guards, no tools), ADR-004 (amended), ADR-011 (amended: authz before retrieval; off-domain retrieves nothing; hash-only audit), ADR-002 (Chat allow-list `[SharedKernel, AiGateway]` — composition in Api), spec §8.3–§8.4, §10.4, §12.1, Appendix C rules 2, 6, 10.
- **Design oracle**: `inputs/design/prototypes/Contigo V2 Prototype.html`; unpacked `contigo-v2/app.jsx` `ask()` (intent outcomes, abstain copy "Nothing in the N validated contracts supports a reliable answer…", unknown supplier copy), `contigo-v2/screens-v2.md` §2, `contigo-v2/ia-v2.md` "Ask intents" + divergences (inline comparison when validated).
- Gaps G-DOMAIN-GATE, G-NOT-WIRED, G-ANSWER-ROLE, G-COMPARE (pack side), G-STRATEGY / G-CRITICALITY (narration side).
- **Do not touch**: `Contigo.Market` internals (F02/T02 this phase), `StagedExtractionService` / pipeline (F03/T02 this phase), `Contigo.Insights` (phase 2, consumed only), `web/`, OpenAPI json (phase-4 web task documents the endpoints).

## Definition of done
- [ ] `dotnet test backend/tests/Contigo.Chat.Tests` exit 0 — gate labels for "ciao", "ricetta della carbonara", "posso fare causa?", "cosa sai fare?", "quando scade Databricks?" (needs_document), "Is my Allianz contract above market?" (in_domain / market_compare); numeric guard downgrades a fake "P50 CHF 140" vs pack 132; grounding guard rejects a foreign key; actions never contain `{`
- [ ] `dotnet test backend/tests/Contigo.Api.Tests` exit 0 — `POST /api/conversations/{id}/messages` returns the §6 contract for answer / abstain / redirect / refusal; zero retrieval calls on "ciao" (recording retrieval fake); 120-day renewal question answered with per-contract citations; no `Document:` guid and no "Structured query" substring in any reply
- [ ] `dotnet test backend/tests/Contigo.IntegrationTests --filter AskContigo` exit 0 — cross-tenant evidence never appears
- [ ] `dotnet test backend/Contigo.slnx` exit 0

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | gate, planner, guards, reply builders | `Contigo.Chat.Tests/*` |
| API | reply contract, alias, no engineer chrome | `Contigo.Api.Tests/ChatEndpointTests.cs`, `ConversationsEndpointTests.cs` |
| integration | RLS on evidence via the new endpoint | `Contigo.IntegrationTests/AskContigoRagCrossTenantIsolationTests.cs` |

## Open questions blocking this task
- OQ-askv2-006 — answer language follows the question (assumed)

## Wave-spec entry
```yaml
- id: E13/F06/US01/T01
  prompt: reports/workitems/epic-13-ask-v2/feature-06-ask-engine/us-01-ask-engine/tasks/task-01-ask-engine.md
  produces: [ask-engine]
  depends_on: [foundry-gateway, market-feed-mock, supplier-entity, documents-v2-api, conversations-api, insights-calculators, capability-catalog]
  effort: L
  layer: backend
  status: live
```
