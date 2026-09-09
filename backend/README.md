# Contigo backend

.NET 10 modular monolith + background worker (ADR-002). One class-library
project per bounded context, a shared kernel, and two thin hosts. Domain
modules never reference a provider SDK or another domain's internals —
`Contigo.ArchitectureTests` fails the build if a project reference points
the wrong way.

Honours ADR-003 (Postgres + pgvector, EF Core), ADR-009 (RLS as the
non-bypassable backstop), and ADR-005 (API + worker as Container Apps).

## Solution

```
backend/
  Contigo.slnx
  Directory.Build.props          # net10.0, nullable, TreatWarningsAsErrors
  src/
    Contigo.Api/                 # thin HTTP composition root (port 8080 in containers)
    Contigo.Worker/              # thin worker composition root
    Contigo.SharedKernel/        # TenantId, EntityId, Result<T>, IClock, IAuditWriter, IDocumentStorage
    Contigo.Identity.Workspace/  # workspace, membership, roles (live)
    Contigo.Documents.Contracts/ # upload + admission gate (task E13/F04/US01/T01), metadata, hybrid OCR pre-pass, staged extraction, portfolio list, Contract 360, contract correction + history (live)
    Contigo.Audit/               # append-only audit events (live)
    Contigo.AiGateway/           # IAiGateway (classify/extract/embed/answer/ocr): FixtureAiGateway + live FoundryAiGateway (Azure OpenAI-compatible + Document Intelligence, task E13/F01/US01/T02), always behind the LoggingAiGateway decorator
    Contigo.Benchmark/           # IBenchmarkService.GetBenchmarkAsync + normalized Contracts DTOs (E04/F01/US01/T01); BenchmarkAdapterRegistry + AddBenchmarkModule (E04/F01/US01/T02); FixtureBenchmarkAdapter registered as the default IBenchmarkProviderAdapter, incl. statistical weak-comparable abstain (E04/F01/US02/T01+T02) — no host calls AddBenchmarkModule yet (R3)
    Contigo.Suppliers.Products/  # Supplier entity, SupplierNameNormalizer, ISupplierResolver/ISupplierNameLookup impls, SuppliersDbContext + RLS (task E13/F03/US01/T01, ADR-024; live) - see "Supplier identity" below
    Contigo.Market/              # R-MKT-01/02/03/04 mock feed + benchmark projection + in-memory notes retrieval (E13/F02/US01/T01); market_record/market_embedding pgvector index + ingestion job + DB-backed retrieval/benchmark + GET /api/market/records/{id} (E13/F02/US01/T02, mapped by E13/F06/US01/T01) - see "Market Intelligence" below
    Contigo.Insights/            # criticality score, priced-line negotiation, strategy pack builder (E13/F07/US01/T01, ADR-024) - pure calculators fed by DTOs; InsightsEndpointExtensions mapped by task E13/F06/US01/T01 (ask-engine) - see "Insights" below
    Contigo.Renewals/            # renewal engine + opportunity + explainable priority score + threshold scheduler + dashboard pipeline + action (R2; live) — see "Renewal Intelligence" below
    Contigo.Savings/             # price normalization + percentile/target/savings-range calculator (R3; task E04/F02/US01/T01) + persisted, trackable SavingsOpportunity + GET/PATCH /api/savings (task E04/F02/US02/T01) — see "Savings Intelligence" below
    Contigo.Quotes/              # quote upload + hybrid-OCR-reused, schema-constrained line-item extraction (evidence + confidence; deterministic pricing) + POST /api/quotes (R4; task E05/F01/US01/T01) + SKU/edition normalization against a per-tenant canonical mapping, unmatched-SKU flagging (task E05/F01/US02/T01) + benchmark matching/above-in-line-below market assessment + GET /api/quotes/{id}/assessment, AddBenchmarkModule now wired (task E05/F02/US01/T01) + deterministic recommended target range/potential saving on that same endpoint (task E05/F02/US01/T02) + deterministic negotiation strategy (opening target/acceptable range/walk-away threshold + seven canonical levers with rationale, NegotiationStrategyService, no HTTP endpoint yet) (task E05/F03/US01/T01) + NegotiationOutcome capture (original/target/final/deterministic saving+discount/duration/levers used) + POST /api/negotiations/outcomes, append-only/audit-tracked (task E05/F03/US02/T01) — see "Quote Check" / "Market Assessment" / "Negotiation Strategy" / "Negotiation Outcome" below
    Contigo.Chat/                # Ask Contigo structured-vs-semantic query router (R1, task E02/F04/US01/T01) + deterministic dates/spend query handlers (task E02/F04/US01/T02) + RagAnswerService (task E02/F04/US02/T01) + AbstainGuard no-fabrication guard (task E02/F04/US02/T02); AddChatModule wired into Contigo.Api by this last task; own ChatDbContext + Conversation/ConversationMessage under RLS + ConversationService (create/list/get/append) (task E13/F05/US01/T01) — see "Ask Contigo — conversations store" below
  tests/                         # per-module + architecture + R0-R4 integration
```

Hosts are composition roots only: they register modules via `AddXxxModule`
and map HTTP / hosted services. Business logic lives in the libraries.

**V2 scaffold (task E13/F01/US01/T01, ADR-024).** `Contigo.Market` and
`Contigo.Insights` entered the solution as scaffolds — a class library, an
`AddMarketModule()` / `AddInsightsModule()` stub that registered nothing, and
a matching test project with one placeholder test — so the epic-13 tasks that
fill them in would not also have to touch `Contigo.slnx` or the architecture
allow-list. Both are now real: `Contigo.Market` by tasks E13/F02/US01/T01+T02
(see "Market Intelligence" below), `Contigo.Insights` by task E13/F07/US01/T01
(see "Insights"). `Contigo.Suppliers.Products.Tests` was that scaffold task's
third new test project and got real coverage from task E13/F03/US01/T01 (see
"Supplier identity"). `Contigo.AiEval` (references `Contigo.Chat`,
`Contigo.AiGateway`, `Contigo.SharedKernel` — the golden-set eval harness of
story us-01-v2-foundation) is the one still-placeholder project, pending task
E13/F06/US01/T02. `Contigo.ArchitectureTests.DependencyDirectionTests`
allow-lists `Contigo.Market` → `[SharedKernel, AiGateway, Benchmark]` and
`Contigo.Insights` → `[SharedKernel, Benchmark]`, and covers both in its
domain-module direction / provider-SDK theories.

*(This section previously carried three interleaved copies of the project tree
and of this paragraph — one truncated mid-sentence — merged in by the epic-13
wave's phase barriers. Reconciled by task E13/F04/US01/T01 against the actual
`Contigo.slnx` and `DependencyDirectionTests` allow-list.)*

## Commands

Requires the .NET 10 SDK. Integration tests pull a `pgvector/pgvector:pg16`
Testcontainer (Docker must be running).

```bash
cd backend
dotnet restore Contigo.slnx
dotnet build Contigo.slnx --configuration Release
dotnet test Contigo.slnx --configuration Release
```

Local API (https://localhost:7109, http://localhost:5029 — matches
`web/public/config.json`):

```bash
dotnet run --project src/Contigo.Api/Contigo.Api.csproj --launch-profile https
```

`appsettings.Development.json` points at `localhost:5432` database
`contigo_dev` (user/password `contigo`) and Azurite
(`UseDevelopmentStorage=true`). There is no docker-compose in this repo
yet — bring your own Postgres (with `VECTOR` enabled) and Azurite, or rely
on Testcontainers inside `dotnet test`.

EF migrations live in each module that owns a DbContext
(`Contigo.Identity.Workspace`, `Contigo.Documents.Contracts`,
`Contigo.Audit`, `Contigo.Renewals`, `Contigo.Savings`, `Contigo.Quotes`,
`Contigo.Chat`, `Contigo.Suppliers.Products`). Apply them against
the same database the hosts use; RLS policies are added in those
migrations, not in Terraform.

**Deployable schema artifact (ADR-021):** every module above also checks in
`Migrations/Scripts/<module>.sql` — `identity-workspace.sql`,
`documents-contracts.sql`, `audit.sql`, `renewals.sql`, `savings.sql`,
`quotes.sql`, `chat.sql`, `suppliers.sql`, `market.sql` — generated with
`dotnet ef migrations script --idempotent` from that module's `src/`
folder. That checked-in script, applied with `psql` (or any plain Npgsql
client), is the actual `dev`/`demo` deploy path: CI applies all nine, in
ADR-021's fixed order (`chat.sql` appended seventh — task E13/F05/US01/T01
postdates ADR-021's own fixed list; `suppliers.sql` appended eighth — task
E13/F03/US01/T01; `market.sql` appended ninth — task E13/F02/US01/T02, same
reasoning as `suppliers.sql`: neither `market_record` nor `market_embedding`
carries a FK to or from any other module's tables, so `market.sql` has no
ordering dependency on the other eight and is simply appended last), after
both `az containerapp
update` steps (task E09/F02/US01/T02) — `Contigo.Api` and `Contigo.Worker`
deliberately never call `Database.MigrateAsync()`, so a replica boot never
mutates schema. Regenerate the script after adding or changing a
migration; `<Module>MigrationScriptStaleCheckTests` (task E09/F01/US01/T01,
not yet retrofitted onto `Contigo.Chat` — same pre-existing gap
`Contigo.Documents.Contracts` also has; `Contigo.Suppliers.Products` has
its own from the start, task E13/F03/US01/T01) fails `dotnet test` if a
script is missing or no longer matches a fresh idempotent generate, and
`<Module>MigrationScriptTests`
proves the checked-in script itself — not `MigrateAsync`, no DbContext —
applies (and re-applies) cleanly to a bare `pgvector/pgvector:pg16` server.
`.github/workflows/backend.yml`'s CI apply step (`scripts/pg_connection_string_env.py`
turns the Key Vault `postgres-connection` secret into `psql`'s `PG*`
environment variables; `scripts/schema_apply_verify.py` then proves every
migration_id all nine scripts declare landed in `contigo_<env>`'s own
`__EFMigrationsHistory`, failing the job by name otherwise) needs the CI
deploy principal to hold `Key Vault Secrets User` on that environment's
vault (`modules/keyvault` `ci_secrets_user`, applied by HCP).

**Demo fixture seed (task E10/F01/US01/T01, ADR-001, ADR-022):** ADR-021's
schema apply above creates empty tables — nothing populates the Day-1
Savings screen on a fresh `contigo_demo`. `backend/scripts/demo-fixture-seed.sql`
is the checked-in, idempotent fix: one demo `workspace` (tenant id
`00000000-0000-0000-0000-000000000001` — hard-code this as `X-Tenant-Id`
to exercise the seeded tenant directly), one supporting `contract` row, and
three `savings_opportunity` rows traceable to real
`Contigo.Benchmark.Fixtures.FixtureBenchmarkAdapter.Catalog` entries (AWS
EC2, Zoom, Snowflake) spanning `Contigo.Savings.Application
.SavingsProvenanceClassifier`'s own documented High/Medium/Low confidence
examples for those exact fixtures — never a fabricated number (ADR-001).
Every `INSERT` is `ON CONFLICT (id) DO NOTHING` against a fixed id, so
re-running is safe, and every RLS-guarded table is written the same way
the application itself writes one — `SET app.tenant_id = '<demo tenant
id>'` before the insert (see `Contigo.SharedKernel.Tenancy
.TenantRlsConnectionInterceptor`) — never a bypass role, never a disabled
policy (AC-4). Applied with `psql` (never `Database.MigrateAsync()`) by
`.github/workflows/seed-demo-fixture.yml` — `workflow_dispatch` for a
manual operator run, `workflow_call` for a future caller (for example a
`demo-v*` promotion runbook) — against either `dev` or `demo`
(`target_environment` input), *after* `backend.yml`'s own schema apply has
run against that environment; it reuses that same job's Key Vault fetch
(`scripts/pg_connection_string_env.py`, secret `postgres-connection`) and
the same per-env deploy service principal, so no new Azure role assignment
is needed. `Contigo.IntegrationTests.DemoFixtureSeedEndToEndTests` (via
`DemoFixtureSeedIntegrationFixture`) proves the checked-in script itself —
read from disk, applied to a real Postgres+RLS Testcontainer, never
re-typed into the test — makes `GET /api/savings` return the three seeded
opportunities for the demo tenant and nothing for any other tenant, and
that a second apply does not duplicate rows.

## HTTP surface today

| Method | Path | Notes |
|--------|------|-------|
| GET | `/health` | ASP.NET health checks |
| POST | `/api/workspaces` | create workspace |
| POST | `/api/workspaces/{tenantId}/invites` | invite; roles Admin / Procurement / Legal / Finance / ReadOnly |
| POST | `/api/documents` | multipart `file` + `X-Tenant-Id` header (optional `X-User-Id` names the actor of a rejection audit row). Task E13/F04/US01/T01 (documents-admission, ADR-024 “gate before persistence”) reordered this endpoint: size → **413**, format by extension *and* magic bytes → **415**, admission gate (parse/OCR → readable-text floor → `classify`) → **422** `{ rejected, detectedType, confidence, reason, hint }` with **nothing persisted** and one `document.rejected` audit row; only an admitted document is stored and then processed. Still runs `DocumentProcessingPipeline` (staged extraction → RAG indexing) synchronously before responding (task E02/F06/US01/T01, r1-integration) — reusing the gate's own parse and classification, so the `classify` role is called once per upload — and the response `processingStatus`/`contractId` reflect that run's outcome, not just the initial “Uploaded” write. See “Documents — admission gate” below |
| GET | `/api/documents/{id}` | metadata/status; same header; `documentType` is the widened `ContractDocumentType` (`Msa`, `OrderForm`, `Amendment`, `Sow`, `RenewalLetter`, `Quote`, `Invoice`, `PriceList`, `Nda`, `Dpa`, `Other`) — task E13/F04/US01/T01 added the last five so “the documents around a contract” keep their own kind |
| GET | `/api/documents` | Server-side Documents list (R-DOC-06/09; task E13/F04/US01/T02); `X-Tenant-Id` header; optional `status` (exact `DocumentProcessingStatus`), `page` (default 1), `pageSize` (default 25, max 100); response `{ items, page, pageSize, totalCount }`, each item `{ id, contractId, supplierName, fileName, documentType, processingStatus, stage, pageCount, createdAt, weakFactCount }` — `stage` is one of R-DOC-09's six real names and is present **only** while `processingStatus` is `Processing`; `supplierName` is resolved through `ISupplierNameLookup` when the Suppliers module is registered, `null` otherwise (never a raw id); `weakFactCount` counts this contract's distinct extracted fields whose latest evidence is missing or below 0.6 |
| GET | `/api/documents/{id}/preview` | First-page preview as `image/png` (R-DOC-08); `X-Tenant-Id` header; 404 when the document does not exist for this tenant **or** has no stored preview — the client never receives a blob URL, the bytes are streamed under the caller's own tenant scope (ADR-009). See “Documents V2” below for what the preview actually contains today |
| POST | `/api/documents/{id}/reprocess` | **Admin only** (403 otherwise): re-loads the stored bytes, re-runs hybrid parse → page-aware embedding → staged extraction (R-DOC-07), writes one `document.reprocessed` audit row; response `{ documentId, contractId, documentType, processingStatus, pagesParsed, chunksIndexed }`. Role resolution: claims → `X-Role`/`X-Workspace-Role` header → `workspace_membership` looked up by `X-User-Id` — see `Contigo.Api.Infrastructure.WorkspaceRoleResolver` |
| DELETE | `/api/documents/{id}` | **Admin only** (403 otherwise): deletes every stored object (each version plus the preview), the retrieval chunks, the version and extraction-job rows and the document row, detaches the contract link and clears every `source_document_id` on the facts that survive; writes one `document.deleted` audit row; 204 (R-DOC-10). The contract and its extracted facts are deliberately kept |
| PATCH | `/api/contracts/{id}` | `{ corrections: { <field>: <string\|null> }, reason? }` + `X-Tenant-Id` header; versioned correction (ADR-003 `ContractVersion`/`CorrectionHistory`, ADR-009 RLS) — see `Contigo.Documents.Contracts.Application.ContractCorrectionService.CorrectableFieldNames` for the accepted field list; also writes one `IAuditWriter` entry (`contract.corrected`) |
| GET | `/api/contracts/{id}/corrections` | `X-Tenant-Id` header; field-level correction history for one contract, newest first (`Contigo.Documents.Contracts.Application.ContractCorrectionHistoryQueryService`) — 404 if the contract does not exist for the tenant, `[]` if it exists but was never corrected |
| GET | `/api/audit` | tenant-scoped; expects a claims principal (integration tests inject one) |
| GET | `/api/contracts` | portfolio list; spec §8.1 columns; `X-Tenant-Id` header; optional filters `supplierId`, `status`, `risk` (Low/Medium/High/Critical), `autoRenewal`, `minAnnualSpend`, `maxAnnualSpend`, `renewalFrom`/`renewalTo` (yyyy-MM-dd) — no `category` filter yet, see `PortfolioFilter`'s doc comment; optional paging `page` (default 1), `pageSize` (default 25, max 100); response is `{ items, page, pageSize, totalCount }`, not a bare array |
| GET | `/api/contracts/{id}` | Contract 360 aggregate; spec §8.2 header + tabs (overview, commercials, products, clauses, obligations, risks, documents, benchmark, renewal, activity); `X-Tenant-Id` header; 404 when the contract does not exist or belongs to another tenant; `benchmark`/`activity` are always empty arrays — no task has yet mapped a real contract's line items into a `Contigo.Benchmark.Contracts.BenchmarkQuery` (no supplier-name/geography field exists on `Contract` today), so this tab stays empty even though R3's own benchmark comparison is real and provable elsewhere (see "R3 demo smoke test" below); `activity` remains an R4 placeholder — see `Contract360Result`'s doc comment |
| POST | `/api/chat/query` | Ask Contigo V2 (ADR-024 §6; task E13/F06/US01/T01, ask-engine); `{ question: string }` + `X-Tenant-Id` header + caller identity (see "Interim auth" below). Kept one release as a thin alias: creates a conversation, then delegates into the same `AskCopilotService`/`POST /api/conversations/{id}/messages` pipeline (see "Ask Contigo — conversations store" below) — the old direct `AskContigoQueryRouter` → `RagAnswerService` → `{ question, intent, canDetermine, answer, citations, message }` shape this route used to return (task E02/F04/US02/T01) no longer exists; that router is now reused *inside* `AskCopilotService` instead. Response is the ADR-024 §6 reply contract, same as the messages endpoint below |
| GET | `/api/conversations` | Caller's last N conversations, most recently updated first (spec §7; R-CONV-02; story us-01-conversations AC-2, task E13/F05/US01/T02); `X-Tenant-Id` header + caller identity (see "Interim auth" below); optional `take` (default 5, must be a positive integer); response is a bare array of `{ id, title, scopeContractId, updatedAt }`, never an `{ items, totalCount }` envelope — there is no paging concept for "my last N conversations" |
| POST | `/api/conversations` | Creates a conversation (AC-2); `X-Tenant-Id` header + caller identity; body `{ scopeContractId? }` — a GUID naming the contract "Ask about it" (Contract 360) was opened from, or omitted for the global Ask bar (ADR-024: "The global Ask bar always opens a new chat"); 201 with the same `{ id, title, scopeContractId, updatedAt }` shape as the list row above; `title` starts as `ConversationService.DefaultTitle` ("New chat") until the first message lands |
| GET | `/api/conversations/{id}` | The conversation plus its messages, oldest first (AC-2); `X-Tenant-Id` header + caller identity; 404 when `{id}` does not exist, belongs to another tenant, or belongs to another user of the same tenant — RLS backstops the tenant half (ADR-009), `Contigo.Chat.Application.Conversations.ConversationService` itself is the only thing enforcing the per-user half (RLS has no per-user predicate), and both read back as the identical 404, never a distinguishing 403; response `{ id, title, scopeContractId, createdAt, updatedAt, messages: [{ id, role, kind, markdown, citations, actions, modelId, promptVersion, inputHash, createdAt }] }` — `role` is `you`/`contigo`, `kind` is `answer`/`abstain`/`redirect`/`refusal` (ADR-024 §6 wire literals); `citations`/`actions` are real JSON arrays, never a JSON string nested inside JSON; never the raw retrieval pack (ADR-011) |
| POST | `/api/conversations/{id}/messages` | Ask Contigo V2 (ADR-024 §6; task E13/F06/US01/T01, ask-engine, AC-8); `{ question: string }` + `X-Tenant-Id` header + caller identity; 400 for a missing/invalid tenant or user header, an invalid `{id}`, or a blank `question` — all before any database call (see `Contigo.Api.Tests.ConversationsEndpointTests`). Runs the full engine (`AskCopilotService`: `Gate.DomainGate` →, for `in_domain` turns, `Planning.IntentPlanner` → per-intent context pack → guarded `answer` call → `Guards.GroundingGuard`/`NumericGuard`/`RegenerateOnce`), appends both the caller's question and Contigo's reply to the conversation via `ConversationService`, then returns the same ADR-024 §6 reply contract `GET /api/conversations/{id}` echoes back for one message: `{ kind, answerMarkdown, citations: [{ n, corpus, title, subtitle, snippet, documentId?, contractId?, page?, section?, previewUrl?, href?, recordId? }], actions: [{ label, href, kind }], provenance: { sources, modelId, promptVersion, inputHash }, followUps }` plus `conversationId`/`messageId` — never engineer chrome (a `Document:` guid, a "Structured query" line) in `answerMarkdown` |
| GET | `/api/renewals` | Renewal pipeline + insight card (spec §9.3/§10.1); `X-Tenant-Id` header; auto-renewing contracts only, most urgent first; response is `{ items, totalCount }`, each item `{ contractId, supplierId, status, renewalDate, daysUntilRenewal, annualSpend, cancellationDeadline, daysUntilCancellationDeadline, autoRenewal, action, insightCard: { facts, recommendations } }` — `insightCard.recommendations`' benchmark/savings fields (`annualUpliftPercent`, `marketPosition`, `potentialSavingsRange`) are honestly `null` until the Benchmark/Savings modules land (R3); `action`/`recommendedAction` is a deterministic urgency rule, not the full spec §9.2 Priority Score — see `Contigo.Renewals.Application.RenewalPipelineBuilder`'s own doc comment |
| GET | `/api/renewals/{contractId}/priority` | Explainable priority-score breakdown for one contract (spec §9.2; story us-02-priority-score AC-1/AC-2, task E03/F01/US02/T02); `X-Tenant-Id` header; 404 when the contract does not exist or belongs to another tenant (same rule as `GET /api/contracts/{id}`); response is `{ contractId, totalScore, components: { spendWeight, timeUrgency, benchmarkOpportunity, priceIncreaseRisk, contractRisk } }`, each component `{ score, explanation }` — component weights are configurable, see `Contigo.Renewals.Configuration.PriorityScoreWeightsOptions` below; `priceIncreaseRisk`/`benchmarkOpportunity` use their honest no-data default (minimum / neutral respectively) since no uplift or benchmark-position data is wired to real contracts yet |
| POST | `/api/renewals/{id}/action` | Updates owner/status/action for one renewal (spec Appendix A; story us-01-renewal-dashboard-api AC-3); `X-Tenant-Id` header; `{id}` is the same `contractId` the GET above returns per row, not a separate stored "renewal" id; body `{ owner, status, action }` — `status` is one of `NotStarted`/`InProgress`/`Completed`; upserts one row (never a second for the same contract) and writes one `IAuditWriter` entry (`renewal.action_updated`); 400 (not 404) for a missing/invalid tenant header or route id, or for an empty `owner`/`action`/unrecognized `status` — see `Contigo.Renewals.Application.RenewalActionService`'s own doc comment for the honest gap this leaves (no check that `{id}` names an existing, tenant-owned contract; `Contigo.Renewals` cannot reference `Contigo.Documents.Contracts` at all) |
| GET | `/api/savings` | Lists the caller's tenant-scoped `SavingsOpportunity` rows, newest identified first (spec §4.3/§6; module-map.md "Savings \| SavingsOpportunity, RealizedSavings \| /api/savings"; story us-02-savings-opportunity AC-1, task E04/F02/US02/T01; story us-01-savings-kpis AC-2/AC-3, task E04/F03/US01/T02); `X-Tenant-Id` header; response `{ items, totalCount }`, each item also carrying `confidenceLevel` (`Low`/`Medium`/`High`, task E04/F03/US01/T02 — see `SavingsOpportunityResult.ConfidenceLevel`'s own doc comment); no filters yet — see `Contigo.Savings.Application.SavingsOpportunityService.ListAsync`'s own doc comment |
| PATCH | `/api/savings/{id}` | Updates `owner`, `status` (`Identified`/`InProgress`/`Realized`) and/or `realizedAmount` on one `SavingsOpportunity` (AC-1 "updates status/owner..."; AC-3 "realized value is captured and audit-tracked", task E04/F02/US02/T02); `X-Tenant-Id` header; body `{ owner?, status?, realizedAmount? }` — a genuine partial update, any subset of the three fields; 404 when `{id}` does not name an opportunity for this tenant, 400 for every other validation failure (empty owner, unrecognized status, a negative `realizedAmount`, a `realizedAmount` combined with an explicit `status` other than `Realized`, or none of the three fields supplied); writes one `IAuditWriter` entry per successful call — `savings_opportunity.updated`, or `savings_opportunity.realized` instead when `realizedAmount` was supplied (never both). Supplying `realizedAmount` also inserts a new, append-only `Contigo.Savings.Domain.RealizedSavings` row (in the opportunity's own `currency`) and finalizes `status` as `Realized` — either because the caller's own explicit `status` already said so, or automatically when `status` was omitted (see `SavingsOpportunityService.UpdateAsync`'s own doc comment). The response's `realizedAmount` field is non-`null` only on the call that just recorded one — it is not a rolled-up read of this opportunity's full realized-value history, see `SavingsOpportunityResult.RealizedAmount`'s own doc comment; the response also carries `confidenceLevel` (task E04/F03/US01/T02 — same field the `GET` row above documents, shared `ToResponse` wire-shaping) |
| PATCH | `/api/savings/{id}` | Updates `owner` and/or `status` (`Identified`/`InProgress`/`Realized`) on one `SavingsOpportunity` (AC-1 "updates status/owner..."); `X-Tenant-Id` header; body `{ owner?, status? }` — a genuine partial update, either or both fields; 404 when `{id}` does not name an opportunity for this tenant, 400 for every other validation failure (empty owner, unrecognized status, or neither field supplied); writes one `IAuditWriter` entry (`savings_opportunity.updated`) per successful call — setting `status` to `Realized` here does **not** yet create an audit-tracked realized-value record, see `Contigo.Savings.Domain.SavingsOpportunityStatus.Realized`'s own doc comment for the gap task E04/F02/US02/T02 (`RealizedSavings`) closes |
| POST | `/api/quotes` | New Purchase / Quote Check (spec §4.4/§11; module-map.md "Quotes \| Quote, QuoteLine, Assessment... \| /api/quotes"; story us-01-quote-line-extraction AC-1/AC-2/AC-4, task E05/F01/US01/T01); multipart `file` + `X-Tenant-Id` header, same shape as `POST /api/documents`, plus four **optional** form fields task E05/F02/US01/T01 (market-assessment) added — `supplier`, `currency`, `geography`, `purchaseDate` (`yyyy-MM-dd`) — all absent by default and never required for the upload to succeed; nothing in this codebase auto-detects them from the document yet (spec §11.1's own "Identify supplier" workflow step has no task/UI of its own), so a quote uploaded without them simply is not matchable via `GET .../assessment` below until corrected (see `Quote`'s own doc comment; a malformed `purchaseDate` is the one new 400 this endpoint can return); synchronously reuses the epic-02 `HybridDocumentParsingService` (native text or the `ocr` gateway role — ADR-017, no 2-page cap) then runs one schema-constrained `extract` call for line items (quantity/SKU/edition/price/discount/term), persisting one `Contigo.Quotes.Domain.QuoteLine` row per item with source span/page/confidence; `unitPrice`/`extendedPrice` are derived deterministically in code when the model reports only `listPrice`/`discountPercent` (AC-3, Appendix C rule 6 — never asked of the model, see `QuoteLineJsonSchema`); immediately afterward, still the same unit of work, `Contigo.Quotes.Application.Normalization.QuoteLineNormalizationService` (task E05/F01/US01/T02, quote-normalization) sets `NormalizedAnnualUnitPrice`/`NormalizedTermMonths` when `term` matches its own small, fixed billing-cadence vocabulary (monthly/quarterly/semi-annual/annual and common synonyms; every other term deliberately leaves both `null` — spec §11.3's own "line-item normalization is unresolved" outcome, Appendix C rule 10), then `Contigo.Quotes.Application.Normalization.SkuNormalizationService` (task E05/F01/US02/T01, sku-normalization) sets `NormalizedSku`/`NormalizedEdition`/`MatchStatus`; response `{ id, fileName, mimeType, processingStatus, lineItemCount, normalizedLineItemCount, unresolvedNormalizationCount, unmatchedSkuCount, supplier, currency, geography, purchaseDate, createdAt }` — the last four echo exactly what was recorded, including a `null`; a pipeline failure still returns 201 (the upload itself succeeded) with the pre-processing counts all `0`, never an HTTP error. *(This row previously existed twice, one per sibling task's own addition, each missing the other's fields — task E05/F02/US01/T01 consolidated it into the one, accurate, combined shape above.)* |
| GET | `/api/quotes/{id}/assessment` | Quote assessment (spec §4.4/§11.2, Appendix A "Quote assessment"; module-map.md "Quotes \| Quote, QuoteLine, Assessment... \| /api/quotes"; story us-01-market-assessment AC-1/AC-2 (both the "flag" half, task E05/F02/US01/T01, and the "recommended target range + potential saving" half, task E05/F02/US01/T02)/AC-3); `X-Tenant-Id` header; 404 when `{id}` does not name a quote for this tenant; one assessment per `Contigo.Quotes.Domain.QuoteLine` on the quote (creation order) — `{ quoteId, lines: [{ quoteLineId, status, position, unitPrice, quantity, benchmark, confidence, targetSaving, explanation }] }`. `status` is `Assessed`/`QuoteDataUnresolved`/`InsufficientBenchmarkData` (`Contigo.Quotes.Domain.MarketAssessmentStatus`); `position` (`BelowMarket`/`InLine`/`AboveMarket`) is populated only when `status` is `Assessed` — the market band is `[P25, P75]` of the matched `Contigo.Benchmark.Contracts.BenchmarkResult.Distribution`, `InLine` otherwise (see `MarketAssessmentCalculator`'s own doc comment); `benchmark`/`confidence`/`targetSaving` are `null` exactly when no Benchmark Service call was even attempted (`QuoteDataUnresolved`: the quote is missing `supplier`/`currency`/`geography`/`purchaseDate`, or the line itself has no usable product/quantity/term/price), never withheld just because the comparison itself abstained (spec §11.3's benchmark-trust rule — `InsufficientBenchmarkData` still carries real `source`/`sampleSize`/`comparisonDimensions` provenance, and a real `targetSaving` object whose `recommendedTargetLow`/`recommendedTargetHigh`/`savingsRangeLow`/`savingsRangeHigh`/`totalSavingsRangeLow`/`totalSavingsRangeHigh` are honestly `null` with a named `explanation` — see `TargetSavingCalculator`'s own doc comment) |
| POST | `/api/quotes/{id}/assessment/recalculate` | Manual product-mapping correction + recalculate (spec Appendix A "Re-run after product mapping correction"; story us-02-sku-normalization AC-2's "...and allow manual product mapping" half, AC-3, task E05/F01/US02/T02, sku-recalculate); `X-Tenant-Id` header; body `{ mappings?: [{ sku, edition?, canonicalSku, canonicalEdition?, canonicalProductName? }] }` — `mappings` may be omitted/empty (`{}` is a valid body) for a pure "what's still unmatched" refresh with no new correction. 404 when `{id}` does not name a quote for this tenant; 400 when a supplied correction's `sku`/`canonicalSku` is blank — validated before any write. For each valid correction, upserts (never duplicates) one tenant-scoped `Contigo.Quotes.Domain.SkuProductMapping` row keyed on the normalized SKU (`Contigo.Quotes.Application.Normalization.SkuNormalizer.Normalize` — same case/whitespace rule `POST /api/quotes`'s own upload-time normalization uses), then re-runs `SkuNormalizationService.NormalizeAsync` for every line on the quote (not just the corrected one — a mapping learned here also resolves any other quote for this tenant sharing the same normalized SKU, the next time that quote is itself (re)normalized) and `MarketAssessmentService.AssessAsync`; response `{ quoteId, mappingsAppliedCount, normalization: { lineCount, matchedCount, unmatchedCount, notApplicableCount }, unmatchedLines: [{ quoteLineId, sku, normalizedSku, edition, description }], assessment: { ...same shape as GET .../assessment... } }` — `unmatchedLines` is AC-2's "Show unmatched SKUs" half made queryable over HTTP (deliberately not a field on the `GET .../assessment` response itself, see `SkuMappingService`'s own doc comment for why); writes one `IAuditWriter` entry (`quote.sku_mapping_recalculated`) per successful call, even a pure refresh. |
| GET | `/api/savings/kpis` | Procurement-homepage KPI row (spec §4.3/§10.1; story us-01-savings-kpis AC-1, task E04/F03/US01/T01); `X-Tenant-Id` header; response `{ annualSpendAnalyzed: [{ currency, amount, contractCount }], contractsAnalyzedCount, savingsIdentified/savingsInProgress/savingsRealized: [{ currency, low, high, count, averageConfidence }], upcomingRenewalsCount }` — every money value is grouped by currency, never summed across currencies (no exchange-rate service exists anywhere in this codebase); `contractsAnalyzedCount` counts contracts whose linked document reached `DocumentProcessingStatus.Completed` (a `Contract` row can exist before that — see `Contigo.Documents.Contracts.Application.PortfolioAnalysisCalculator`'s own doc comment); `savingsRealized` reflects each opportunity's own estimated range, not yet the separate, audit-tracked `RealizedSavings` value (task E04/F02/US02/T02's own gap, see `SavingsOpportunityStatus.Realized`'s doc comment); `upcomingRenewalsCount` is the same auto-renewing-contract count `GET /api/renewals`'s own `totalCount` already reports (same 100-contract-per-tenant cap) — see `Contigo.Api.SavingsKpiEndpointExtensions`'s own comment for why it is not a second, independently-computed number |
| GET | `/api/capabilities` | The versioned V2 capability catalog (R-SYS-01; story us-01-capability-catalog, task E13/F08/US01/T01; mapped by task E13/F06/US01/T01, ask-engine); no `X-Tenant-Id` — static, tenant-agnostic metadata, not a per-tenant read; optional `X-Role` header (resolved through `WorkspaceRoleClaimResolver`, same interim-header posture as every tenant-scoped endpoint above) hides `workspace-members` unless the caller resolves to `Admin`; see "Ask Contigo — capability catalog" below |
| GET | `/api/insights/criticality` | Portfolio-wide criticality ranking (story insights-calculators, task E13/F07/US01/T01; mapped by task E13/F06/US01/T01); `X-Tenant-Id` header; the same `Contigo.Insights.Criticality.CriticalityScoreCalculator` output `AskCopilotService`'s own `PortfolioStrategy` intent narrates — see "Insights" below |
| GET | `/api/contracts/{id}/strategy` | One contract's renewal-strategy pack (when you must move, where you can push, targets, next steps; task E13/F07/US01/T01; mapped by task E13/F06/US01/T01); `X-Tenant-Id` header; 404 when the contract does not exist or belongs to another tenant; the same `Contigo.Insights.Strategy.StrategyPackBuilder` output `AskCopilotService`'s own `RenewalStrategy` intent narrates — see "Insights" below |
| GET | `/api/market/records/{id}` | One market-feed record, for the citation panel (R-EVD-02; task E13/F06/US01/T01, ask-engine); no `X-Tenant-Id` — shared, tenant-agnostic market data (ADR-024); 404 when `{id}` does not name a record in the mock feed; response `{ recordId, supplier, category, product, geography, currency, title, snippet, provenance, updatedAt, unitPriceP25, unitPriceP50, unitPriceP75, sampleSize, source, representative }` — see `Contigo.Api.MarketEndpointExtensions` |

**Interim auth:** every endpoint above that takes an `X-Tenant-Id` header
(all except `GET /api/audit`, which already expects a claims principal)
takes the tenant from that header, not from a validated JWT. ADR-010
(Entra ID / OIDC on the API) is not wired in the host yet. Do not treat
the header as the long-term contract.

`GET/POST /api/conversations` and `GET /api/conversations/{id}` (task
E13/F05/US01/T02) additionally resolve a **caller identity**, not just a
tenant: the token subject of an already-authenticated principal when one
is present (the ADR-010 end state), otherwise the required `X-User-Id`
header (ADR-022 posture, OQ-askv2-005's assumption in force — the MSAL
account username) — missing both is a 400. Since this host wires no
`AddAuthentication`/`AddJwtBearer` yet, every real caller takes the
header branch today. Same caveat as the tenant header: `X-User-Id` is
**never validated** against a real identity provider — it only scopes
which rows a request can read/write, and is replaced by the token
subject the same task that lands the API JWT on this host.

The web client generates TypeScript types from
`web/openapi/contigo-api.v1.json`. The API does **not** yet self-publish
OpenAPI; that document is hand-authored and must grow with these routes.

## Documents — admission gate (task E13/F04/US01/T01)

`POST /api/documents` refuses anything that is not a contract-related
document **before** it writes a blob, a `document` row, an `embedding` or an
extraction job (ADR-024 “gate before persistence”, `inputs/requirements.md`
R-DOC-01/02/03). The endpoint lives in
`Contigo.Api.DocumentsEndpointExtensions`; the decision itself is
`Contigo.Documents.Contracts.Application.Admission.DocumentAdmissionGate`.

Order of checks, and what each one returns:

| Step | Failure | Body |
|------|---------|------|
| tenant header, multipart shape, non-empty `file` | 400 | plain string |
| `file.Length` ≤ `Documents:MaxFileBytes` | 413 | `Contigo accepts files up to 50 MB. This file is larger.` |
| extension **and** magic bytes agree (`DocumentFormatSniffer`) | 415 | `Contigo reads PDF, Word, Excel and scanned images` |
| readable text ≥ `Documents:MinReadableChars` | 422 | `{ rejected: true, detectedType, confidence: 0, reason: "no_readable_text", hint }` |
| `classify` role returns a contract kind with confidence ≥ `Documents:AdmissionThreshold` | 422 | `{ rejected: true, detectedType, confidence, reason: "not_a_contract", hint }` |

Accepted formats are exactly PDF, DOCX, XLSX, PNG and JPEG — checked by
extension *and* signature, so a `.zip` renamed `.pdf` is a 415 that never
reaches the AI gateway, and a PNG renamed `.pdf` is refused rather than
silently re-labelled. What gets stored as `mimeType` is the **sniffed**
canonical type, never the browser's declared `Content-Type`.

A rejection persists nothing and writes exactly one audit row:
`document.rejected`, `resourceType` `document-upload`, `resourceId` = the
SHA-256 of the uploaded bytes, `detail` = `{ detectedType, confidence, reason,
readableChars, bytes, mimeType }` — never the file name, never any document
text (ADR-011). A parse/OCR or `classify` failure is **not** a rejection: it
returns 400 with the underlying error, because “we could not read it” is not
“it is not a contract”, and it leaves no audit row.

An admitted document is uploaded, then processed by
`DocumentProcessingPipeline`'s pages-and-classification overload, so the
gate's own parse and `classify` result are reused instead of being computed a
second time.

Thresholds are configuration (defaults in `DocumentAdmissionOptions`, applied
when the section is absent):

| Key | Default | Meaning |
|-----|---------|---------|
| `Documents:MaxFileBytes` | `52428800` (50 MiB) | larger uploads get 413 |
| `Documents:MinReadableChars` | `200` | non-whitespace characters across all parsed pages |
| `Documents:AdmissionThreshold` | `0.6` | minimum `classify` confidence for an admitted type |

Admitted types are MSA, Order Form, SOW, Amendment, Quote, Invoice, Price
list, NDA and DPA (`ContractDocumentType` gained the last five in this task).
`Other` is never admitted, so no `document` row is stored with it any more.
`RenewalLetter` remains reachable only by correction — nothing in the classify
taxonomy names it.

With the fixture gateway (no Foundry endpoint configured, ADR-004/ADR-017)
the gate is fully testable: a recipe PDF classifies as `Other` and is
rejected, a document containing “MASTER SERVICES AGREEMENT” is admitted as
`Msa`, and a PNG/JPEG whose bytes are the signature followed by UTF-8 page
text takes the `ocr` path — see `Contigo.Api.Tests.DocumentUploadEndpointTests`
and `Contigo.Documents.Contracts.Tests.Admission`.

## Documents V2 — list, preview, reprocess, delete (task E13/F04/US01/T02)

Everything the Documents V2 screen and Ask's citation cards read
(`inputs/requirements.md` R-DOC-06…R-DOC-10, R-EVD-01). The endpoints live in
`Contigo.Api.DocumentsEndpointExtensions`; the work itself is in
`Contigo.Documents.Contracts.Application` (`DocumentQueryService.ListAsync`,
`DocumentReprocessService`, `DocumentDeleteService`, `Preview/*`).

**Page-aware chunks.** `embedding` gained `page` and `section`
(migration `AddDocumentPreviewAndEmbeddingPage`). Every chunk the pipeline
indexes now carries its 1-based page, and the section label when staged
extraction has already attributed a clause to that page. Both stay `null`
when genuinely unknown — an Ask citation prints “p.N” only when the page is
real. `document` gained `page_count` (what the parse produced) and
`preview_path`.

**Reprocess** (`POST /api/documents/{id}/reprocess`, Admin) loads the stored
bytes through `IDocumentStorage.LoadAsync`, deletes this document's existing
chunks, re-parses (native text or the `ocr` role, ADR-017) and re-indexes them
page-aware, then re-runs staged extraction so facts added by later tasks
back-fill onto documents uploaded before those tasks existed. It is a
delete-then-index, not an upsert: R-DOC-07 AC-1 requires that afterwards no
embedding row for the tenant starts with `%PDF`.

**Deletion** (`DELETE /api/documents/{id}`, Admin) removes the objects first,
then the rows. Clauses, obligations, risks, line items and evidence are
*detached* (their `source_document_id` set to null), never deleted: each of
those columns is an `ON DELETE RESTRICT` foreign key, and a fact whose source
file is gone is still a fact. The contract itself always survives.

**Preview — what it is today, honestly.** `DocumentPreviewService` renders a
PNG at upload and stores it under the tenant prefix
(`{tenant}/documents/{id}/preview/page-1.png`). The built-in
`PlaceholderDocumentPreviewRenderer` is pure managed code (a small PNG encoder
plus a 5x7 bitmap font, no native dependency):

- a **PNG** upload is its own preview — a real page-1 image;
- a **PDF, JPEG, DOCX or XLSX** gets a generated placeholder that names the
  format and says “preview not rendered”.

A true first-page raster of a PDF needs a rasteriser (pdfium/Skia), which is a
native provider dependency and belongs in `Contigo.Api`'s infrastructure behind
the existing `IDocumentPreviewRenderer` port — registering one is the only
change needed; the storage path, the endpoint and the stored `preview_path`
stay as they are. Until then the card shows the placeholder, not a fake page.

**Admin resolution while ADR-010 is not wired**
(`Contigo.Api.Infrastructure.WorkspaceRoleResolver`), in order: role claims on
an authenticated principal → an `X-Role` / `X-Workspace-Role` header (the same
interim signal `GET /api/capabilities` reads) → the caller's
`workspace_membership` row looked up by the `X-User-Id` header. No match means
no role, and every Admin-only endpoint answers 403. The web client sends
`X-User-Id` on every call and no role header, so the membership branch is the
one that decides whether its Admin buttons work.

**Audit rows are written inside the tenant scope**, by the services rather than
by the endpoints: `audit_event` is itself RLS-protected, so a write with no
ambient tenant is rejected by Postgres (ADR-009/ADR-011).

## Worker

`Contigo.Worker` references the same application libraries as the API.
The R0 default queue is an **in-process** `InMemoryQueueConsumer` — Azure
Service Bus exists in Terraform (`modules/servicebus`) but is not consumed
here yet, and `QueueConsumerHostedService` still never dispatches a
received message to a domain handler. Extraction runs synchronously inside
`Contigo.Api`'s `POST /api/documents` today instead (`DocumentProcessingPipeline`,
above) — not through this Worker — a documented interim choice pending a
real durable-queue producer/consumer pair. Benchmark / quote handlers land
with those features; renewal threshold scheduling (task E03/F02/US01/T01)
is the first of the four `13.3 Background jobs` categories this host
actually runs — see "Renewal threshold scheduler" above for
`RenewalThresholdSchedulerHostedService` and its own honest gap (no real
cross-tenant contract source wired yet).

## AI Gateway

`Contigo.AiGateway` is wired into DI by `Contigo.Documents.Contracts`'s own
`AddDocumentsContractsModule` (so both the API and Worker hosts get a
working `IAiGateway` with no host-side change). `IAiGateway` is bound to
`FixtureAiGateway` — deterministic, provider-free — until a live Foundry /
Document Intelligence endpoint exists (ADR-004/ADR-017); domain code
depends only on the interface. Per-role model ids/versions
(`classify`/`extract`/`embed`/`answer`/`ocr`) bind from the
`AiGateway:Models` configuration section (`AiGateway:Models:Extract:ModelId`,
etc. — env var form `AiGateway__Models__Extract__ModelId`) and default to
ADR-004/ADR-017's candidate models when that section is absent, so no
config is required to run locally. The `ocr` role's page-count safety
budget (ADR-017: fail visibly, never silently truncate) is its own
`AiGateway:Ocr:MaxPagesPerDocument` section (default 300 — see
`AiGatewayOcrOptions`).

`Contigo.Documents.Contracts.Application.Extraction.HybridDocumentParsingService`
implements the hybrid OCR pre-pass (ADR-017): native text extraction
(`NativeDocumentTextExtractor` — real `DocumentFormat.OpenXml` for
DOCX/XLSX, a self-contained content-stream reader for PDF; no external PDF
library is referenced — see that class's own doc comment for why) when
sufficient, otherwise the full document (no 2-page cap) through the `ocr`
gateway role. `StagedExtractionService` runs product spec §7.2's
seven-stage pipeline (metadata → commercial terms → dates → price/SKU →
clauses → obligations → risk) over the resulting page-mapped text
(`DocumentPageText`) and persists every fact with source span/page +
confidence (spec §7.3) — directly on `ContractLineItem`/`Clause`/
`Obligation`/`Risk`, or via the `ExtractionEvidence` table for `Contract`'s
own scalar fields.

`Contigo.Documents.Contracts.Application.Extraction.DocumentProcessingPipeline`
(task E02/F06/US01/T01, r1-integration) is that caller: given the just-
uploaded bytes, it runs the hybrid parse, then `IAiGateway.ClassifyAsync`
over the resulting text (setting `Document.DocumentType` and completing
the `Classification` job `DocumentUploadService` queues at upload), then
`StagedExtractionService`, then indexes every parsed page into the
`embedding` table (see `EmbeddingRetrievalService` below) — one call proves
the whole spec §7.1 pipeline. `POST /api/documents` (`Contigo.Api.Program`)
runs it synchronously, in the same request, right after the upload itself
is durable — a deliberate interim choice (see `DocumentProcessingPipeline`'s
own doc comment): nothing in this codebase dispatches the queued
`Classification` job off a durable queue yet (`Contigo.Worker.Queue
.QueueConsumerHostedService` still never dispatches a received message to a
domain handler — see the Worker section below), so synchronous/in-request
is the smallest honest way to make R1's "upload → ... → Ask Contigo" promise
true on `dev`/`demo` today. A pipeline failure never turns an already-
successful upload into an HTTP error — it is recorded on the `Document`/
`ExtractionJob` rows and reported in the response, same as any other
per-stage failure in this pipeline.

`Contigo.Documents.Contracts.Application.EmbeddingRetrievalService`
(us-02-embedding-search-index) is the pgvector half of Ask Contigo RAG:
`IndexChunkAsync` embeds a text chunk via `IAiGateway.EmbedAsync` and
persists it to the `embedding` table; `SearchAsync` embeds a query the
same way and returns the tenant's nearest chunks by cosine distance
(`Vector.CosineDistance`), explicitly filtered by `tenant_id` on top of
that table's own RLS policy. Embedding generation never touches a
provider SDK directly — always through `IAiGateway`. `SearchAsync`'s first
caller is `POST /api/chat/query` (task E02/F04/US02/T01, below).
`IndexChunkAsync`'s first production caller is `DocumentProcessingPipeline`
(task E02/F06/US01/T01, r1-integration, above) — one `Embedding` row per
parsed page, `SourceType="Document"`/`SourceId=<documentId>`, so a document
is retrievable for Ask Contigo immediately after it finishes processing. A
tenant that has never uploaded anything (or whose upload is still
processing/failed) still honestly returns "cannot determine" — there is
simply nothing indexed for it yet, not a bug.

**Task E13/F01/US01/T02 (foundry-gateway)** adds the live half of this
module: `Contigo.AiGateway.Foundry.FoundryAiGateway` implements all five
ADR-004/ADR-017 roles — `classify`/`extract`/`embed`/`answer` over an Azure
OpenAI-compatible chat-completions/embeddings surface, `ocr` over Azure AI
Document Intelligence's `documentModels/{model}:analyze` long-running
operation — against the one shared `AiGateway:Endpoint` (ADR-008's single
Cognitive Services account). `AddAiGatewayModule` now picks the
implementation at first resolution: `FoundryAiGateway` when
`AiGateway:Endpoint` is configured (Container Apps inject it on `dev`/
`demo`, see `infra/README.md` "AI Gateway / Foundry + Document
Intelligence"), `FixtureAiGateway` otherwise (local/CI, unchanged) — and
**always** wraps whichever one behind `Logging.LoggingAiGateway` (ADR-004/
ADR-011 "always log-wrapped"), which this module shipped as a class since
task E02/F01/US01/T02 but never actually wired into DI until now.
`IAiGateway` is therefore resolved Scoped, not Singleton, from this task
on — `LoggingAiGateway` depends on the Scoped `IAuditWriter`
(`Contigo.Audit`'s own registration), and every current `IAiGateway`
consumer (`DocumentProcessingPipeline`, `StagedExtractionService`,
`EmbeddingRetrievalService`, `HybridDocumentParsingService`,
`QuoteExtractionPipeline`, `RagAnswerService`) was already Scoped, so this
is a captive-dependency fix, not a behaviour change for any of them; see
`ServiceCollectionExtensions`'s own doc comment for the full reasoning.
Auth is `Azure.Identity.DefaultAzureCredential` (managed identity on
Container Apps, developer sign-in locally) against the
`https://cognitiveservices.azure.com/.default` scope — never a key in
config — acquired through one `FoundryTokenProvider` singleton and cached
until near expiry, not re-fetched per call. The `answer` role's request
body (`Foundry.Wire.ChatCompletionRequest`) has no `tools`/`tool_choice`/
`data_sources` property at all, so ADR-024's "no tools, no grounding"
compliance is a type-system guarantee rather than a remembered omission —
proved on a fake `HttpMessageHandler` in
`Contigo.AiGateway.Tests.Foundry.FoundryAnswerClientTests`, the same
fake-handler convention every `Foundry.*ClientTests` class uses so no unit
test ever calls live Azure. `AiAnswerRequest`/`AiAnswerResult` gained
ADR-024's structured-answer fields (`SystemPrompt`/`PackJson` on the
request; `AnswerMarkdown`/`CitationKeys`/`ActionKeys`/`AbstainReason`/
`FollowUps` on the result), all optional/nullable additions — the existing
`Answer`/`Citations` fields and every pre-existing call site
(`RagAnswerService`, `AbstainGuard`, and their own tests) keep compiling
and behaving unchanged; a later task ("F06") replaces
`RagAnswerService`'s own evidence-chunk-concat with the versioned persona
prompt + context pack ADR-024 describes. New root-level `AiGateway`
configuration keys (siblings of `AiGateway:Models`/`AiGateway:Ocr`, bound
by `Configuration.AiGatewayFoundryOptions`): `AiGateway:Endpoint`,
`AiGateway:ProjectName`, `AiGateway:DocumentIntelligenceConnection`
(non-secret — see `infra/README.md`), and `AiGateway:AnswerTemperature`
(default 0.2, ADR-024's own ceiling; a higher configured value is clamped,
never raised). `Contigo.AiGateway.Tests.SdkAllowListTests` proves the new
`Azure.Core`/`Azure.Identity` package references stay inside
`Contigo.AiGateway.csproj` — no other project in the solution may
reference `Azure.AI.*`/`Azure.Identity` (AC-3).

## Benchmark Service

`Contigo.Benchmark.IBenchmarkService.GetBenchmarkAsync` (task E04/F01/US01/T01)
is the normalized `getBenchmark` contract product spec §10.3 names — P25/P50/P75
plus metric/currency/confidence/source/updated/comparison, so Renewals/Savings/
Quotes never depend on a provider schema. Task E04/F01/US01/T02 adds
`Contigo.Benchmark.BenchmarkAdapterRegistry`, the pluggable
`IBenchmarkProviderAdapter` registry behind that interface, wired into DI by
`Contigo.Benchmark.ServiceCollectionExtensions.AddBenchmarkModule` — it
config-selects the active adapter by name (`Benchmark:Adapter:ActiveAdapter`,
env var form `Benchmark__Adapter__ActiveAdapter`, default `"fixture"` —
`BenchmarkAdapterOptions`), the same "config-selected, swap without a code
change" convention `AiGatewayModelOptions` already uses for ADR-004.

Task E04/F01/US02/T01 (story us-02-fixture-adapter) added the first concrete
adapter as a class, but that task's own wave-spec phase ran alongside this
registry task (parallel, neither depends on the other), so it could not
register what it had just written — the adapter existed and was directly
unit-testable, but unreachable through `AddBenchmarkModule()`. Task
E04/F01/US02/T02 (fixture-confidence) closes that gap: only a concrete
adapter may ever reference a provider SDK — `Contigo.Benchmark`'s own project
file still carries none, and `Contigo.ArchitectureTests.DependencyDirectionTests
.Benchmark_module_must_not_reference_provider_sdks` fails the build if that
changes without an adapter to justify it — and now that adapter is actually
wired in. A host that calls `AddBenchmarkModule()` today gets a real,
resolvable `IBenchmarkService` (`BenchmarkAdapterRegistry`) whose default
configuration dispatches to a genuine, fixture-backed result; an unrecognized
configured adapter name (for example a `Benchmark:Adapter:ActiveAdapter`
naming a paid provider that has not been registered) still fails honestly
rather than fabricating one (ADR-001).
`Contigo.Benchmark.Fixtures.FixtureBenchmarkAdapter` (task E04/F01/US02/T01,
us-02-fixture-adapter) is that first `IBenchmarkService`/`IBenchmarkProviderAdapter`
implementation — deterministic and provider-free, backed by a hand-curated,
in-memory catalog of illustrative SaaS supplier/product comparables (never
Tropic, Vendr, or any paid market API — ADR-001, spec §10.2's "Strategic
requirement"). It registers under the name `"fixture"`
(`Configuration.BenchmarkAdapterOptions.DefaultAdapterName`), so
`BenchmarkAdapterRegistry` finds it with no separate name to keep in sync.
`GetBenchmarkAsync` requires a fixture to match on supplier, product,
geography, currency, contract term, quantity tier and a purchase-date
refresh window — seven of spec §10.4's eleven named comparison dimensions,
always more than supplier name alone — plus SKU as an optional,
confidence-boosting eighth. A fixture that clears every required dimension
*and* carries at least `FixtureBenchmarkAdapter.MinimumViableSampleSize`
comparables (task E04/F01/US02/T02: 10) returns P25/P50/P75 with a
sample-size-scaled confidence score (`Contigo`'s own score, spec §10.3 —
saturates at a sample size of 50); anything weaker — including a fixture that
matches every dimension but is too statistically thin to trust (task
E04/F01/US02/T02's own "weak-comparable abstain" objective) — returns the
explicit "insufficient market data" outcome (`Distribution: null`) instead of
a fabricated number (ADR-001; spec §10.4's benchmark-trust rule, verbatim: "a
precise-looking number from weak comparables is more dangerous than an
explicit 'insufficient market data' result"), falling back to a
same-supplier/same-product comparable's metric and sample size when one
exists so the caller still sees real (if insufficient) provenance.

`ServiceCollectionExtensions.AddBenchmarkModule` wires `IBenchmarkService` to
`BenchmarkAdapterRegistry`, which now dispatches to this adapter by default
(task E04/F01/US02/T02).

**Task E04/F04/US01/T01 (r3-integration)** closes the wiring gap this section
used to name here ("no host calls `AddBenchmarkModule` yet"):
`Contigo.Savings.Infrastructure.ServiceCollectionExtensions.AddSavingsModule`
now calls `AddBenchmarkModule` itself — the same "a module that depends on
another module's interface registers that dependency's own DI wiring
transitively" convention this host already uses for `AddDocumentsContractsModule`
-> `AddAiGatewayModule` (see "AI Gateway" above). `Contigo.Api` already calls
`AddSavingsModule`, so `IBenchmarkService` is now resolvable there with no
`Program.cs` change at all — proven end to end by
`Contigo.IntegrationTests.R3EndToEndTests` (see "R3 demo smoke test" below).
`Contigo.Worker` does not call `AddSavingsModule` (no worker job creates a
`SavingsOpportunity` today — see "Savings Intelligence" below), so it still
does not resolve `IBenchmarkService` either; that is the same, pre-existing
"wiring lands with the first real caller" gap, unrelated to this task's own
fix. `Contigo.Renewals`'s own
`RenewalPriorityInputs.BenchmarkMarketPositionPercent` (see "explainable
priority score" below) still has no real producer wired to it — a different
module, out of this task's own "do not touch unrelated wave artifacts" scope.

## Market Intelligence — mock feed, benchmark projection, in-memory notes

Task E13/F02/US01/T01 (market-feed-mock, ADR-024, R-MKT-01…04) fills in the
`Contigo.Market` scaffold with the "how companies actually close contracts"
side of Ask Contigo V2: a checked-in mock feed behind
`IMarketIntelligenceProvider`, projected into the existing `Contigo.Benchmark`
seam and into a searchable set of narrative notes — no paid third-party API
anywhere in this task or its project (ADR-001: "never a hard dependency of
the first V2 `demo`").

`Contracts/MarketDeal` is R-MKT-01's normalized record shape (supplier,
category, product, SKU, geography, currency, company-size band, term,
annual-value band, unit price P25/P50/P75, discount/uplift-cap/notice/payment
terms, negotiated clauses, closing period, sample size, source, updatedAt,
licence restrictions) — Contigo's own shape; a later live third-party client
(R-MKT-05) maps onto it, never the reverse (OQ-askv2-001).
`Mock.MockMarketIntelligenceProvider` reads the checked-in
`backend/fixtures/market-intelligence.mock.json` — **65 records** (≥ 60,
R-MKT-02) spanning enterprise software (Salesforce, Microsoft, AWS,
Snowflake, ServiceNow, Slack, Zoom, Notion, HubSpot, Workday, SAP, Adobe,
Atlassian, Google Workspace, DocuSign, Okta), insurance (Allianz, AXA,
Zurich, Swiss Re), facilities, telco, logistics and professional services,
across EU/CH/US and CHF/EUR/USD, with 9 rows deliberately carrying
`sampleSize < 5` so the abstain path below is exercised — every record
`source = "mock"` / `representative = true`. The JSON is **embedded** into
`Contigo.Market.dll` (not opened from a runtime file path) so every host
that loads the assembly — API, Worker, this project's own tests, a future
`seed-market-intelligence` job — reads the exact same bytes with no path
configuration and no dependency on a backend Dockerfile `COPY` step that
does not exist yet (see `MockMarketIntelligenceProvider`'s own doc comment).

**Benchmark projection:** `Benchmark.MarketFeedBenchmarkAdapter` implements
`Contigo.Benchmark.Adapters.IBenchmarkProviderAdapter` under the name
`"market-feed"`, matching on supplier + product always, plus geography /
currency / contract term (always present on both sides) and SKU (only when
both the query and a candidate deal name one — two deals disagreeing on SKU
never match each other). A match below `MinimumViableSampleSize` (5 — the
exact boundary the fixture's thin rows were built to cross) never publishes
a P25/P50/P75 distribution (spec §10.4 benchmark-trust rule, ADR-001);
`BenchmarkResult.Source` is always `"market-feed (representative, mock)"`.
`ServiceCollectionExtensions.AddMarketModule` registers this adapter into
the same `IBenchmarkProviderAdapter` enumerable
`Contigo.Benchmark.BenchmarkAdapterRegistry` resolves (`TryAddEnumerable` —
`FixtureBenchmarkAdapter` stays registered, still directly testable) **and**
makes it `BenchmarkAdapterOptions`'s active adapter by default — without
editing `Contigo.Benchmark` and regardless of whether a host calls
`AddBenchmarkModule()` or `AddMarketModule()` first. This does *not* use
`IServiceCollection.PostConfigure<BenchmarkAdapterOptions>`: that only takes
effect through the `Microsoft.Extensions.Options` `IOptions<T>` indirection,
and `Contigo.Benchmark`'s own registration never uses it (a plain singleton
factory instead) — `PostConfigure` here would be a silent no-op. Instead
`AddMarketModule` calls `IServiceCollection.Replace` with an otherwise
byte-for-byte copy of `Contigo.Benchmark`'s own factory (same configuration
section, same `Bind` call), which unconditionally wins the registration
slot regardless of call order — see
`ServiceCollectionExtensions.MakeMarketFeedTheDefaultActiveAdapter`'s own
doc comment for the full reasoning. An explicit
`Benchmark:Adapter:ActiveAdapter` configuration value still overrides the
default either way, unchanged.

**Two projections, one ingestion job (task E13/F02/US01/T02, market-index):**
R-MKT-03's "served from the persisted `market_record` rows, never from the
provider at question time" is now real. `Infrastructure.MarketDbContext`
(pgvector, **no** tenant interceptor and no RLS policy — ADR-024/ADR-011's
epic-13 amendment: shared, read-only, never a tenant row) owns `market_record`
(one row per `MarketDeal`, keyed by `RecordId`) and `market_embedding` (one
narrative chunk per record, `vector(1536)`, same convention
`Contigo.Documents.Contracts.Domain.Embedding` uses), plus the checked-in
idempotent `Migrations/Scripts/market.sql` (ADR-021 — see "Deployable schema
artifact" above for its place in the apply order); that script's own header
documents a conditional, self-activating grant (read for `contigo_app`,
read/write for `contigo_market_ingest`) that stays a harmless no-op until a
later infra task actually provisions those two roles.

`Ingestion.MarketIngestionService.IngestAsync` is the *only* caller of
`IMarketIntelligenceProvider` (ADR-024: "the provider is called only by that
job"): it upserts `market_record` by `RecordId`, composes one narrative per
record (`Retrieval.MarketNoteComposer.Compose`, unchanged from T01), embeds it
via `IAiGateway.EmbedAsync` (ADR-004 `embed` role, hash-logged), and replaces
that record's `market_embedding` row(s) — a second run against the same feed
version and payload changes zero rows and issues zero embed calls (parent
story AC-4). `Retrieval.PgVectorMarketKnowledgeRetrieval` (cosine distance,
top-k, optional category/geography filters) and `MarketFeedBenchmarkAdapter`'s
new `(IDbContextFactory<MarketDbContext>, IClock)` constructor then read only
this store, never the provider, so a throwing `IMarketIntelligenceProvider`
no longer affects either projection once ingestion has run
(`Contigo.Market.Tests.MarketModuleQuestionTimeIsolationTests`);
`Contigo.IntegrationTests.MarketIndexIsolationTests` proves AC-3's other half
— a tenant embedding search never returns a market note, a market search
never returns tenant chunks — against one shared Postgres database.
`ServiceCollectionExtensions.AddMarketModule(string? marketConnectionString)`
is the DI swap: `null` keeps T01's mock-feed/in-memory wiring (still the
default `Retrieval.IMarketKnowledgeRetrieval` — token-overlap over composed
notes, no index, no embedding call); a real `ConnectionStrings:Market`
instead registers `MarketDbContext` and switches both the notes-retrieval and
benchmark-adapter registrations to their DB-backed equivalents.

`Contigo.Worker.Program` now calls `AddMarketModule` (the connection string
stays optional — absent, T01's in-memory wiring stays) so its new one-shot
operator command, `Commands.IngestMarketCommand`
(`dotnet run --project backend/src/Contigo.Worker -- ingest-market --feed
backend/fixtures/market-intelligence.mock.json`; `--feed` is an
informational label only — the mock provider always ingests its one
checked-in feed version, see that type's own doc comment), has something to
call — still no scheduled/background ingestion job. `GET
/api/market/records/{id}` (`Contigo.Api.MarketEndpointExtensions`, parent
story AC-5 — one record with its provenance label and `updatedAt`) exists but
is deliberately not yet mapped from `Program.cs` — task F06/T01 (this same
phase) is expected to call `MapMarketEndpoints()`, the same "endpoint exists,
host wiring is a later task's job" shape `CapabilitiesEndpointExtensions`
already uses.
**Interim data source:** R-MKT-03 describes benchmark rows as "served from
the persisted `market_record` rows, never from the provider at question
time" once an ingestion job exists — this task adds no ingestion job and no
`market_record` table (that is T02's own scope: "Market index, ingestion,
DB-backed retrieval, record endpoint"). Until then,
`MarketFeedBenchmarkAdapter` calls `IMarketIntelligenceProvider.GetDealsAsync`
directly on every query — the only data source T01 has — an explicitly
interim shortcut T02 is expected to replace with the persisted-store read,
with no change to `Contigo.Benchmark.IBenchmarkService` or any domain-module
call site.

**In-memory notes retrieval (Projection 2, interface only in a later phase's
DB-backed form):** `Retrieval.MarketNoteComposer.Compose` turns one
`MarketDeal` into one narrative `Contracts.MarketNote` (e.g. "Companies of
500-2000 employees closing Salesforce Sales Cloud Enterprise in CH in
2026-Q1 paid P50 CHF 132 …, obtained a 4% uplift cap and 90-day notice…"),
labelled via `Contracts.MarketProvenance.Label` (`"representative market
data · mock feed · updated <yyyy-MM-dd>"`, R-MKT-04). `Retrieval
.InMemoryMarketKnowledgeRetrieval` — this task's default
`Retrieval.IMarketKnowledgeRetrieval` — scores every composed note by plain
token overlap against the query (no index, no embedding call) and returns
the top-K; task E13/F02/US01/T02 is expected to swap in a pgvector-backed
implementation over the shared, tenant-free `market_embedding` index behind
this same interface (R-MKT-03: "own table — never rows in the tenant
`embedding` table").

Task E13/F06/US01/T01 (ask-engine) is `AddMarketModule()`'s first real
caller (`Contigo.Api.Program`), the same "wiring lands with the first real
caller" sequencing this README already documents for `AddBenchmarkModule` /
`AddChatModule` above; that task also maps `GET /api/market/records/{id}`
(see the HTTP surface table above and `Contigo.Api.MarketEndpointExtensions`).

## Supplier identity

Task E13/F03/US01/T01 (story us-01-supplier-identity, ADR-024 "Supplier
identity") turns `Contigo.Suppliers.Products` from the bare scaffold task
E13/F01/US01/T01 left behind into this module's first real content:
`Domain.Supplier` (tenant-scoped: `Name`, `NormalizedName`, `Aliases`
(a Postgres `text[]`), `Category?`, `Country?`, `CreatedAt`, `UpdatedAt`)
under its own `Infrastructure.SuppliersDbContext` + Postgres RLS (same
`ENABLE`/`FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy shape every
other module's own `AddTenantRowLevelSecurity` migration already uses).
`Application.SupplierNameNormalizer` lower-cases, strips the R-SUP-02 legal
suffixes (`Inc`/`Ltd`/`Limited`/`GmbH`/`AG`/`SA`/`SpA`/`S.r.l.`/`Srl`/`LLC`/
`Corp`/`Corporation`/`Co.` — a dotted and undotted spelling of the same
suffix fold onto the same token, e.g. `SpA`/`S.p.A.` both become `spa`),
strips punctuation and collapses whitespace — deterministic and pure, no
database. `Application.SupplierResolver` (the `ISupplierResolver`
implementation) matches an existing row by `NormalizedName` or by an entry
in `Aliases` before ever creating one, so "Salesforce, Inc." and
"salesforce" resolve to the same id (parent story AC-2); the unique index
on `(tenant_id, normalized_name)` is the database-level backstop against a
race between two concurrent first-seen resolutions.

The cross-module contract other modules get instead of referencing this
one directly (ADR-002: Documents/Renewals/the API may not reference
`Contigo.Suppliers.Products`) lives in
`Contigo.SharedKernel.Suppliers`: `ISupplierResolver.ResolveAsync(TenantId,
rawName, ct) → Result<SupplierRef>` and `ISupplierNameLookup.GetNamesAsync
(TenantId, ids, ct) → IReadOnlyDictionary<EntityId, string>` (batched, so a
list page resolves every row's supplier name in one call). Both are wired
by `Infrastructure.ServiceCollectionExtensions.AddSuppliersProductsModule
(string connectionString)` — a raw connection string the caller resolves
however it names its own configuration key; `Contigo.Api.Program` (task
E13/F06/US01/T01, ask-engine, its first real caller) reads it from
`ConnectionStrings:Suppliers` (env var form `ConnectionStrings__Suppliers`)
rather than the dots-stripped-full-module-name convention every other
module's own connection string uses (`DocumentsContracts`,
`IdentityWorkspace`) — a shorter key, since `SuppliersProducts` would
otherwise be the only three-word one. `Contigo.Worker` does not call
`AddSuppliersProductsModule` — nothing in the worker needs a supplier name
yet. Nothing in this codebase resolves a supplier name for a real contract
during extraction yet; that is task E13/F03/US01/T02's own job (the
`supplier` critical extraction fact, the pipeline's resolver call, and
reprocess back-fill) — `AskCopilotService` (see "Ask Contigo — conversations
store" below) is `ISupplierNameLookup`'s first real Ask-side caller, not
the extraction pipeline.

Tenant isolation is proved in
`Contigo.IntegrationTests.SupplierCrossTenantIsolationTests` — deliberately
not in `Contigo.Suppliers.Products.Tests` alongside the normalizer/resolver
unit tests, per this task's own file assignment — because `Contigo.Api`
does not reference this module yet, so unlike the `R0`–`R4` suites in that
same project it cannot go through `WebApplicationFactory<Program>`; it
drives `SuppliersDbContext` directly instead, the same shape
`Contigo.Renewals.Tests`' own per-module `*RlsCrossTenantIsolationTests`
already use.

## Ask Contigo — query router + deterministic queries + RAG citations

`Contigo.Chat.Application.AskContigoQueryRouter` classifies a natural-language
question (product spec §8.3) as `Structured` (deterministic query/filter, no
LLM) or `Semantic` (needs RAG retrieval) — task E02/F04/US01/T01.
`DeterministicQueryPlanner` + `DeterministicQueryHandler` (task
E02/F04/US01/T02) turn a `Structured` decision into an actual answer for the
two families spec §8.3 names as "dates" and "spend": "which contracts renew
in the next N days" (a filter on `Contract.AutoRenewal`/`EndDate`) and
"what is our annual spend [with a supplier]" (a sum of `Contract.AnnualSpend`).
No supplier-name -> `SupplierId` resolution exists yet (Suppliers/Products is
still an empty scaffold — the same root cause as the portfolio list's missing
`category` filter above), so a question that names a specific supplier (for
example "What is our Microsoft annual spend?") is still summed across
**every** supplier today; `DeterministicQueryResult.SupplierScopeUnresolved`
is `true` whenever that happened, so a caller can tell "$700,000 total" apart
from "$700,000 with Microsoft" instead of presenting one as the other.
A structured question outside those two families (for example "total
contract value") is reported as `Unsupported` rather than answered against
the wrong field.

`Contigo.Chat.Application.RagAnswerService` (task E02/F04/US02/T01,
us-02-rag-citations, AC-1/AC-2/AC-3) turns a `Semantic` decision plus
already-retrieved, already-authorized evidence into a grounded answer with
citations via `IAiGateway.AnswerAsync` (ADR-004 `answer` role) — citations
or an explicit "cannot determine" (spec §8.4 "no evidence, no claim"), never
a fabricated answer. It also writes one `IAuditWriter` entry per successful
call (`chat.answered` — ADR-011 "audit of access"), never the raw
question/evidence/answer text.

`Contigo.Chat.Application.AbstainGuard` (task E02/F04/US02/T02, abstain-guard)
is the no-fabrication guard `RagAnswerService.AnswerAsync` runs on every
gateway result before it is audited or returned: a "cannot determine" result
passes straight through, but a "determined" result is only trusted when it
carries at least one citation, has non-empty answer text, and every citation's
`DocumentId` matches one of the evidence documents actually handed to the
gateway — otherwise the guard downgrades it to an honest "cannot determine"
(preserving the original `AiCallMetadata` for reproducibility) rather than let
an unsupported or hallucinated citation through (Appendix C rules 2 and 10).
`FixtureAiGateway` can never trigger this — it only ever echoes citations
built from its own input evidence — so today the guard is a no-op in practice;
it exists for the Foundry-backed `IAiGateway` implementation ADR-004
anticipates, which can hallucinate. The audit detail line gains one field,
`abstainGuardIntervened=true|false`, so an operator can see a caught
fabrication attempt without the guard silently discarding the signal — the
free-text reason itself is deliberately not logged (ADR-011: no model
output/content in audit rows).

`Contigo.Chat` cannot reference `Contigo.Documents.Contracts` (see
"Dependency direction" below), so neither `DeterministicQueryHandler` nor
`RagAnswerService` retrieves anything itself: both operate on caller-supplied
data (`ContractFact` / a pre-retrieved evidence list respectively) — small
DTOs/parameters the module owns or accepts, never the real `Contract`/
`Embedding` entities. `DocumentId` on an `AiEvidenceSnippet` built from an
`Embedding` hit is a `{SourceType}:{SourceId}` composite (not a bare id): a
row's `SourceId` only really identifies a document when `SourceType` is
`"Document"` — for `"Clause"`-sourced evidence it identifies the clause row,
and silently relabelling one as the other would misattribute the citation.

**Superseded by the V2 engine (task E13/F06/US01/T01, ask-engine):**
`Contigo.Api.ChatEndpointExtensions` (`POST /api/chat/query`) used to be the
composition root that closed the gap above directly — it resolved the
tenant, called `EmbeddingRetrievalService.SearchAsync` itself, and called
`RagAnswerService` for the `Semantic` branch only, with the `Structured`
branch left as an honest "not wired yet" (no `ContractFact` mapping existed).
`POST /api/chat/query` now instead delegates into `AskCopilotService`, the
new V2 pack-composition root that reuses this router/planner/handler trio as
one of several intents — see "Ask Contigo — conversations store" below for
where that composition now lives; `AskContigoQueryRouter`/
`DeterministicQueryPlanner`/`DeterministicQueryHandler`/`RagAnswerService`/
`AbstainGuard` themselves are unchanged, still pure, and still directly
unit-tested exactly as this section describes.

## Ask Contigo — conversations store

Task E13/F05/US01/T01 (story us-01-conversations, ADR-024 "Conversations
(D5)") gives `Contigo.Chat` its own persistence, independent of the router/
RAG pieces above: `Infrastructure.ChatDbContext` (two tables,
`Domain.Conversations.Conversation` / `ConversationMessage`, both
`TenantScopedEntity` — this module's own copy, not a shared reference, of
`Contigo.Documents.Contracts.Domain.TenantScopedEntity`'s identical shape,
since `Contigo.Chat`'s ADR-002 allow-list is exactly `[SharedKernel,
AiGateway]`) under Postgres RLS (`FORCE ROW LEVEL SECURITY` + policy on
`app.tenant_id`, same shape as every other module — see
`ChatMigrationScriptTests`), and
`Application.Conversations.ConversationService` (create / list-recent /
get-with-messages / append-message). RLS has no per-user predicate, so
"another user of the same workspace cannot read this conversation"
(R-CONV-01 AC-1) is an *application-level* filter on
`Conversation.UserId` — every `ConversationService` method filters by both
tenant and user, not tenant alone (see that type's own doc comment).
Writes two audit rows: `conversation.created`, `conversation.message.appended`
— never the message markdown/citations content itself (ADR-011).

`Title` starts as `ConversationService.DefaultTitle` ("New chat") and is
derived from the first `you`-role message, truncated to
`ConversationService.TitleMaxLength` (48, R-CONV-01 "first question, <= 48
chars") the moment it lands — never re-derived from a later message.
`ConversationMessage.Role`/`Kind` are C# enums stored as strings (PascalCase
column values, e.g. `"You"`/`"Answer"`); mapping them onto ADR-024 §6's
lowercase wire literals (`you`/`contigo`, `answer`/`abstain`/`redirect`/
`refusal`) is the HTTP layer's job, not this module's.

`Infrastructure.ServiceCollectionExtensions.AddChatModule` gained an
optional `chatConnectionString` parameter (AC-4): called with none, it
registers exactly what it always has — the query router/RAG services
above, no database — so nothing that already resolves them without a
connection string breaks (`Contigo.Chat.Tests.ServiceCollectionExtensionsTests`
proves this). Called with one, it additionally registers `ChatDbContext` +
`ConversationService`, keyed by this story's own council-decided
`ConnectionStrings:Chat` (`ConnectionStrings__Chat` env var form, same
`Contigo.Chat.Infrastructure.ChatDbContextFactory` design-time fallback
shape as every other module's `<Module>DbContextFactory`).

**Task E13/F05/US01/T02 (conversations-api)** is that first real caller:
`Contigo.Api.Program` now reads `ConnectionStrings:Chat` and calls
`AddChatModule(chatConnectionString)` — the same fail-fast shape (throws a
named `InvalidOperationException` when the key is missing) as every other
required connection string in that file — and
`Contigo.Api.ConversationsEndpointExtensions.MapConversationsEndpoints()`
maps `GET/POST /api/conversations` and `GET /api/conversations/{id}` (see
the HTTP surface table above for the exact request/response shapes). The
composition root resolves caller identity (token subject, else the
required `X-User-Id` header — see "Interim auth" above) and tenant
(`X-Tenant-Id`) itself, then calls straight into `ConversationService` —
that service's own `tenantId`/`userId` parameters already do all the
RLS/application-level scoping, so this file has no scoping logic of its
own to get wrong.

### The V2 engine (task E13/F06/US01/T01, ask-engine, ADR-024)

`POST /api/conversations/{id}/messages` — deliberately left unmapped by
T02 above until an engine existed to produce a turn worth persisting — is
now mapped in this same file, and `POST /api/chat/query` (see "Ask Contigo
— query router..." above) becomes a thin alias that creates a conversation
and delegates into the identical pipeline. Both routes share one
composition root, `Contigo.Api.AskCopilotService` (`AskAsync`) — the pack
-composition root ADR-024 calls for: everything `Contigo.Chat`'s ADR-002
allow-list (`[SharedKernel, AiGateway]`) forbids that module from doing
itself (querying `PortfolioQueryService`/`Contract360QueryService`,
`EmbeddingRetrievalService.SearchAsync`, `RenewalEngine`/
`PriorityScoreCalculator`/`CriticalityScoreCalculator` (Insights),
`SavingsOpportunityService`, `IBenchmarkService`/`IMarketKnowledgeRetrieval`
(Market), `ISupplierNameLookup`) happens here, then gets handed to
`Contigo.Chat`'s own gate/planner/guards/reply pipeline:

1. **Gate** (`Contigo.Chat.Application.Gate.DomainGate.Classify`) — six
   labels, deterministic lexicons first (greeting, off-domain small talk,
   legal-advice, capability/how-to, then an unresolved named-supplier
   check), an `in_domain` default on ambiguity (no live classify call yet —
   see that type's own doc comment for the honest gap). `greeting` /
   `off_domain` / `legal` / `capability` / `needs_document` are all
   answered directly (`Reply.RedirectReplyBuilder`, real
   `CapabilityRouting`-resolved actions) — **zero retrieval, zero model
   call** — only `in_domain` reaches the planner (R-ASK-02).
2. **Planner** (`Application.Planning.IntentPlanner.Plan`) — nine fixed
   intents (structured fact, clause, market compare, renewal strategy,
   portfolio strategy, savings, document status, quote route, navigate),
   reusing `AskContigoQueryRouter`/`DeterministicQueryPlanner` for the
   legacy structured/clause split. `AskCopilotService` composes one
   `Pack.PackItem` list per intent (tenant facts, clause chunks, market
   notes, calculator output — every item citable, tagged `tenant`/
   `market`/`contigo`/`calc`).
3. **Answer** (`Application.Answering.AnswerComposer`, persona prompt
   `Prompts/answer/v2.1.md`) calls `IAiGateway.AnswerAsync` with the pack +
   last N turns; `Fixtures.FixtureAiGateway.AnswerAsync` gives a
   deterministic v2 behaviour when a pack is supplied (cites the first N
   pack keys, copies their values verbatim — no chunk concatenation), so
   every test below runs without Foundry.
4. **Guards** — `Application.Guards.GroundingGuard` (every citationKey /
   inline `[n]` marker / actionKey must resolve), `Guards.NumericGuard`
   (every currency amount, percentage and date in the answer must equal a
   pack value — currency-aware — or appear verbatim in a cited snippet),
   `Guards.RegenerateOnce` (one retry naming the violation, then downgrade
   to an honest abstain naming the pack's own facts, metadata preserved for
   ADR-011 auditability) — never shown or persisted unguarded.
5. **Reply** (`Application.Reply.CopilotReply`) — the one shape every gate
   label / guard outcome produces: `{ kind, answerMarkdown, citations[],
   actions[], provenance: { sources, modelId, promptVersion, inputHash },
   followUps[] }`, `kind` one of `answer`/`abstain`/`redirect`/`refusal`
   (see the HTTP surface table above for the full citation/action field
   list) — never a `Document:` guid or a "Structured query" line in
   `answerMarkdown` (R-ASK-08).

`AskAsync` writes exactly one audit row per turn (`chat.answered`/
`chat.redirected`/`chat.refused`/`chat.abstained` — counts + a pack hash,
never text, ADR-011), with one further field on every row:
`abstainGuardIntervened=true|false` (AC-7) — `true` only when
`Answering.AnswerComposer`'s own guard pipeline actually rejected the first
attempt and forced `Guards.RegenerateOnce`'s retry-then-downgrade path, never
just because the reply happens to be `abstain` (an empty pack or a failed
gateway call both also produce `kind=abstain` but leave this field `false` —
the same field name/shape `RagAnswerService`'s older, evidence-only audit
entry already uses; see this file's "Ask Contigo — query router" section
above). The context pack's token budget is
`Pack.PackBudget`, optionally configured via `Chat:PackTokenBudget`
(`Chat__PackTokenBudget` env var form) and registered in `Program.cs`
*before* `AddChatModule`'s own always-usable default so a configured value
wins; absent configuration, `PackBudget.DefaultMaxTokens` applies.
Cross-tenant isolation over this new endpoint (parent story AC-9) is
proven the same way as `POST /api/chat/query`'s — see
`Contigo.IntegrationTests.AskContigoRagCrossTenantIsolationTests`.

## Ask Contigo — capability catalog

Task E13/F08/US01/T01 (story us-01-capability-catalog, ADR-024 "Capability
catalog (R-SYS)") adds `Contigo.Chat.Application.Capabilities`: a static,
versioned (`CapabilityCatalog.Version`, `"capabilities-v2.0"`) catalog of
the ten V2 capabilities (`ask`, `documents`, `documents-attention`,
`documents-review`, `portfolio`, `contract-360`, `renewals`, `savings`,
`quote-check`, `workspace-members` — R-SYS-01, `contigo-v2/ia-v2.md`'s own
route map), each a `Capability` record (key/title/route pattern/
description/example questions/role gate/availability/how-to steps).
`CapabilityRouting` (registered `AddScoped` by `AddChatModule` — the same
"stateless router, still an injected instance" convention
`AskContigoQueryRouter` above already uses) turns a planner intent
(`CapabilityIntent` — benchmark, unknown supplier, deadline, savings,
how-to, capability list) plus a `RoutingContext` (validated-contract count,
caller role, known contract/quote/document id) into `CopilotAction`s built
only from catalog patterns and known object ids (R-SYS-02) — and, per
R-SYS-04, replaces any action whose target capability is
`needsValidatedContract` with the Documents upload action and the
prototype's own empty-state copy (`CapabilityRouting
.ValidatedContractsEmptyStateCopy`, `markup.html` "The portfolio lights up
from validated contracts. Upload one to start.") when the caller has zero
validated contracts. `FeatureCitation.For(capability)` builds the R-SYS-03
feature-card shape (`corpus: contigo`); `CapabilityCatalog.SuggestionsFor`
reproduces `app.jsx`'s per-screen `chipsFor`/`c360Chips` suggestion chips.

`Contigo.Api.CapabilitiesEndpointExtensions` maps `GET /api/capabilities`
(role-aware: an `X-Role` header, resolved through
`Contigo.Identity.Workspace.Domain.WorkspaceRoleClaimResolver` — same
interim-header posture as every `X-Tenant-Id` endpoint below, ADR-010 not
yet on this host — hides `workspace-members` unless the caller resolves to
`Admin`). Task E13/F06/US01/T01 (ask-engine) is this endpoint's first-mapped
caller in `Program.cs`, the same "endpoint exists, host wiring is a later
task's job" shape already used above for `AddChatModule`'s
`chatConnectionString` overload. Unlike every other endpoint in this file,
it takes no `X-Tenant-Id` — the catalog is static, tenant-agnostic
metadata, not a per-tenant read.

## Renewal Intelligence — deterministic renewal engine

`Contigo.Renewals.Application.RenewalEngine` (task E03/F01/US01/T01,
us-01-deterministic-dates) is product spec §9.1's "calculate renewal date,
calculate cancellation deadline, calculate days remaining" made concrete:
pure, synchronous arithmetic over a `ContractRenewalTerms` snapshot — no
database, no HTTP call, no LLM call (Appendix C rule 6) — returning a
`RenewalCalculationResult` with a three-way `RenewalCalculationStatus`:

- `Determined` — `RenewalDate` equals `EndDate` when `AutoRenewal` is true
  (the same convention `PortfolioListItem.RenewalDate` /
  `Contract360Header.RenewalDate` already use, reproduced here on purpose).
  `CancellationDeadline` additionally needs `CancellationNoticeDays`
  (`EndDate` minus that many days) and can stay null even inside a
  `Determined` result when that one input is missing or negative — a
  renewal date and its cancellation deadline are independently
  determinable.
- `NoRenewal` — `AutoRenewal` is false: a known fact, not a data gap, so it
  is not folded into `CannotDetermine`.
- `CannotDetermine` — `EndDate` itself is unknown: nothing can be computed
  without fabricating it (Appendix C rule 10; parent story AC-3).

`DaysUntilRenewal`/`DaysUntilCancellationDeadline` are signed, unclamped
day counts relative to `IClock.UtcNow` — a negative value honestly means
the date already passed, rather than being hidden behind a floor of zero.
`RenewalEngine.CalculateMany` is the batch form for spec §9.1's "daily
scheduler for each active contract" shape; deciding which contracts are
"active" (in scope to call it with) is the caller's job, not the engine's.

`ContractRenewalTerms` deliberately does not reference
`Contigo.Documents.Contracts.Domain.Contract` — ADR-002 forbids
`Contigo.Renewals` from referencing `Contigo.Documents.Contracts` at all
(same reason `Contigo.Chat.Application.ContractFact` is its own small DTO,
not the real `Contract` entity). Two honest gaps follow, both deliberately
out of this task's file scope:

1. No host endpoint or worker job calls `RenewalEngine` yet.
   `AddRenewalsModule` exists (`Infrastructure/ServiceCollectionExtensions.cs`)
   so the remaining tasks that depend on `renewal-engine` in the wave-spec DAG
   (priority score, the cancellation-alerts threshold scheduler) can resolve
   it from a container, but `Contigo.Api`/`Contigo.Worker`'s `Program.cs` do
   not call it yet — the same "wiring lands with the first real caller"
   sequencing `AddChatModule` followed before `Contigo.Chat` had one (see
   that section above).
2. `Contract` has no persisted `CancellationNoticeDays` column — its "dates"
not the real `Contract` entity). One of the two gaps this section used to
describe is now closed (see "Renewal threshold scheduler" below); the other
remains, deliberately out of that task's file scope too:

1. `Contract` has no persisted `CancellationNoticeDays` column — its "dates"
   extraction stage (`StagedExtractionService.ApplyDatesFact`) still writes
   a raw `cancellationDeadline` date directly from extraction instead of a
   notice-period day count (product spec §7.3's own extraction-evidence
   example names `cancellation_notice_days`, not a computed date). Mapping
   a real `Contract` row onto `ContractRenewalTerms` — and giving
   extraction a real `CancellationNoticeDays` field to populate — is
   follow-up work in `Contigo.Documents.Contracts`, a different module and
   a different task's file scope.
not the real `Contract` entity).

`Contigo.Renewals.Application.RenewalPipelineBuilder` (task E03/F03/US01/T01,
us-01-renewal-dashboard-api) is `RenewalEngine`'s first real caller and backs
`GET /api/renewals` (see the HTTP surface table above): it turns a batch of
`RenewalDashboardCandidate` (another small DTO, the same dependency-direction
shape as `ContractRenewalTerms`) into a pipeline row plus a facts/
recommendations insight card (spec §9.3), ordered most-urgent-first by days
until the relevant date. `Contigo.Api.RenewalsEndpointExtensions` is the
composition root that maps a real, tenant-scoped `PortfolioListItem`
(Documents/Contracts) onto `RenewalDashboardCandidate` — the one mapping
neither module may do itself, same pattern `ChatEndpointExtensions` already
uses for `EmbeddingSearchResult` → `AiEvidenceSnippet`. `AddRenewalsModule`
is now called by `Contigo.Api`'s `Program.cs` — the same "wiring lands with
the first real caller" sequencing `AddChatModule` followed before
`Contigo.Chat` had one. `Contigo.Worker`'s `Program.cs` still does not call
it — no worker job (the renewal-opportunity generation / cancellation-alerts
threshold scheduler wave-spec tasks) depends on `renewal-engine` yet.

One honest gap remains, deliberately out of this task's file scope:
`Contract` has no persisted `CancellationNoticeDays` column — its "dates"
extraction stage (`StagedExtractionService.ApplyDatesFact`) still writes a
raw `cancellationDeadline` date directly from extraction instead of a
notice-period day count (product spec §7.3's own extraction-evidence example
names `cancellation_notice_days`, not a computed date), so `RenewalEngine`
itself still cannot derive a cancellation deadline for any real contract.
`RenewalPipelineBuilder` works around this for the dashboard specifically by
carrying `Contract.CancellationDeadline` (the already-extracted raw fact)
straight through as its own field, independent of `RenewalEngine`'s
notice-day derivation — see `RenewalDashboardCandidate.CancellationDeadline`'s
own doc comment. Giving extraction a real `CancellationNoticeDays` field (so
`RenewalEngine.Calculate` itself can derive the deadline, the way it already
derives `RenewalDate`) is follow-up work in `Contigo.Documents.Contracts`, a
different module and a different task's file scope.

`Contigo.Renewals.Application.RenewalOpportunityGenerator` (task
E03/F01/US01/T02, us-01-deterministic-dates, the wave-spec's
`renewal-opportunity` artifact) is the next daily-scheduler step from spec
§9.1: "create/update renewal opportunity", built directly on top of
`RenewalEngine.Calculate`'s output. `Generate`/`GenerateMany` take the same
`ContractRenewalTerms` shape `RenewalEngine` does (constructor-injected, so
`AddRenewalsModule` resolves both from one container); the static
`FromCalculation` exposes the mapping rule alone for a caller that already
ran the engine itself. Three-way `RenewalOpportunityStatus` mirrors
`RenewalCalculationStatus` case-for-case (`NoRenewal`/`CannotDetermine` keep
the same names; `Determined` becomes `Open` — an opportunity Procurement has
something to act on) so a `CannotDetermine` calculation never turns into a
fabricated opportunity — it abstains the same way, per parent story AC-3.
Deliberately out of scope here, each a later task's own file: a priority
score/component breakdown (us-02-priority-score), a threshold-alert flag
(feature-02-cancellation-alerts), an owner/status/action
(feature-03-renewal-dashboard's renewal-action task, spec Appendix A `POST
/api/renewals/{id}/action`), and persistence — spec §9.1 says "create/update"
(upsert semantics) but no task has given `Contigo.Renewals` a `DbContext` yet,
so today `RenewalOpportunity` is an in-memory value, not a stored row.
## Renewal Intelligence — explainable, tunable priority score
score/component breakdown (us-02-priority-score) and a threshold-alert flag
(feature-02-cancellation-alerts) remain follow-up work. The other two gaps
this paragraph used to list here are now closed by task E03/F03/US01/T02
(renewal-action, feature-03-renewal-dashboard): an owner/status/action —
`POST /api/renewals/{id}/action`, spec Appendix A, see the HTTP surface
table above — and `Contigo.Renewals`'s first `DbContext`
(`RenewalsDbContext`), which backs that endpoint's
`Contigo.Renewals.Domain.RenewalAction` row. That `DbContext` does not,
though, give `RenewalOpportunity` itself a persisted identity: spec §9.1's
"create/update renewal opportunity" upsert semantics land on the separate
`RenewalAction` (owner/status/action) row, keyed by `ContractId` alone, not
on a stored "renewal" entity — see `RenewalAction`'s own doc comment.
`RenewalOpportunity` remains an in-memory value, not a stored row.
## Renewal Intelligence — explainable priority score

`Contigo.Renewals.Application.PriorityScoreCalculator` (task E03/F01/US02/T01,
us-02-priority-score) is product spec §9.2's formula made concrete: `"Priority
Score = Spend Weight + Time Urgency + Benchmark Opportunity + Price Increase
Risk + Contract Risk"`. Same determinism convention as `RenewalEngine` (pure,
synchronous, no database/HTTP/LLM call) — `Calculate` takes one
`RenewalCalculationResult` (so "days until renewal" is always
`RenewalEngine`'s own arithmetic, never a second copy of it) plus one
`RenewalPriorityInputs` (the raw spend/uplift/contract-risk/benchmark-position
facts `RenewalEngine` does not compute) and returns a `PriorityScoreResult`:
a `TotalScore` (0–100 under the spec-default weights) plus each of the five
components as its own named, explained `PriorityScoreComponent` — spec
§9.2's "Store both total score and component scores so the recommendation is
explainable and tunable" (AC-2), not a single opaque number.

A component whose raw input is unknown never fabricates a guess (Appendix C
rule 10): every component except benchmark opportunity defaults to the
minimum (0); benchmark opportunity defaults to the documented neutral
midpoint (`PriorityScoreCalculator.NeutralComponentScore`, 10 under the spec
default) specifically, because parent story AC-3 names that exact rule —
`"Benchmark-opportunity component reads the R3 benchmark only when available
(else neutral)"`. Today that is *always* the neutral case:
`Contigo.Benchmark.IBenchmarkService` now defines the normalized
`GetBenchmarkAsync` contract (task E04/F01/US01/T01), and
`Contigo.Benchmark.Fixtures.FixtureBenchmarkAdapter` is now registered
behind it via `AddBenchmarkModule` (task E04/F01/US02/T01, see "Benchmark
Service" below), but nothing wires
`RenewalPriorityInputs.BenchmarkMarketPositionPercent` to a real
`GetBenchmarkAsync` call yet — the same "caller supplies it however it likes
today, a real mapping lands later" gap `ContractRenewalTerms` already
documents for this module. Every tier boundary (spend, uplift %,
benchmark %) and the time-urgency tiers (aligned to spec §9.1's own
365/270/180/120/90/60/30-day windows) are fixed, product-spec-cited defaults
— task-02 deliberately did not re-derive that tiering (see next paragraph
for what it did make tunable).

**Task E03/F01/US02/T02 (priority-explainability)** closed both gaps the
paragraph above used to name. *Tunable*: each of the five components' own
*maximum* contribution is now
`Contigo.Renewals.Configuration.PriorityScoreWeightsOptions` (config section
`Renewals:PriorityWeights`, `SpendWeightMax`/`TimeUrgencyMax`/
`BenchmarkOpportunityMax`/`PriceIncreaseRiskMax`/`ContractRiskMax`, each
defaulting to 20 — the untouched spec default) — `PriorityScoreCalculator` rescales every tier's
fixed contribution proportionally (the tier's fraction of the spec-default
20, times the configured maximum), so the tiering itself is unchanged but
each term's weight in the sum is an operator decision, not a compile-time
literal. *Explainable, queryable*: `Contigo.Api.RenewalsEndpointExtensions`
now maps `GET /api/renewals/{contractId}/priority` (see the HTTP surface
table above) — `PriorityScoreCalculator`'s first real host caller, composing
`Contract360QueryService`'s tenant-scoped contract lookup (annual spend, end
date, auto-renewal, risk) the same way `GET /api/renewals` composes
`PortfolioQueryService`; `AnnualUpliftPercent`/`BenchmarkMarketPositionPercent`
stay honestly `null` for the same reason `GET /api/renewals`'s own insight
card does (neither has a real producer yet).

`AddRenewalsModule` registers `PriorityScoreWeightsOptions` the same
"bind lazily from `IConfiguration`, property initializers supply the spec
default" way as `ThresholdWindowOptions` (see below), then registers
`PriorityScoreCalculator` as before — its one constructor parameter is now
that options singleton, injected automatically.

### Renewal threshold scheduler

`Contigo.Renewals.Application.RenewalThresholdScheduler` (task
E03/F02/US01/T01, us-01-threshold-scheduler AC-1/AC-2) is product spec
§9.1's "daily scheduler ... emit threshold events if applicable" made
concrete: it runs `RenewalEngine.CalculateMany` over a tenant's
`ContractRenewalTerms`, then checks each result's `DaysUntilRenewal`/
`DaysUntilCancellationDeadline` against `Contigo.Renewals.Configuration
.ThresholdWindowOptions.DaysBeforeDeadline` (config section
`Renewals:Thresholds`, default 365/270/180/120/90/60/30 days — AC-1,
"configurable"). An exact day-count match raises a `RenewalApproachingEvent`
(`RenewalMilestoneKind.RenewalDate` or `.CancellationDeadline` — a contract
can raise one, both, or neither on a given run) and writes it through
`IAuditWriter` as one `renewal.approaching` entry (spec Appendix B; same
"an audit entry is this codebase's actual event mechanism" convention as
`document.uploaded`/`contract.corrected` — no in-process mediator exists
yet, and picking one is council-owned, not this task's call) — durable and
queryable via `GET /api/audit` even before a real consumer exists.

`Contigo.Worker.Scheduling.RenewalThresholdSchedulerHostedService` is this
module's first real host caller: `WorkerServiceCollectionExtensions
.AddWorkerHost` now calls `AddRenewalsModule` (closing gap 1 that used to
be listed above) and registers this `BackgroundService`, which ticks every
`Worker:RenewalThresholdScheduler:Interval` (default 24h) and, per tenant
batch, calls `RenewalThresholdScheduler.EvaluateThresholdsAsync` from a
fresh DI scope (it must be Scoped, not injected directly into the Singleton
hosted service — it depends on the Scoped `IAuditWriter`). Honest gap: its
`IActiveRenewalContractsSource` port has no real implementation yet — the
default `NoActiveRenewalContractsSource` always returns zero tenants.
Enumerating every tenant's active contracts needs a cross-tenant workspace
listing (`Contigo.Identity.Workspace`, not referenced by `Contigo.Worker`
today) plus a per-tenant RLS-scoped contract query
(`Contigo.Documents.Contracts`) — wiring a real adapter is follow-up
composition work, the same category of gap this section's remaining item
above describes. The timer loop itself is real and proven end to end
(`Contigo.Worker.Tests.RenewalThresholdSchedulerHostedServiceTests`); AC-3
("Scheduler recomputes when a contract/term is corrected") is parent story
task-02's scope ("Alert creation + re-compute on correction"), not this
task's.

**Task E03/F04/US01/T01 (r2-integration) fix:** `RenewalThresholdScheduler
.EvaluateThresholdsAsync` wrote its `renewal.approaching` audit entry
without ever opening an `ITenantContext` scope, so
`TenantRlsConnectionInterceptor` left `app.tenant_id` unset and the Audit
module's own `AddTenantRowLevelSecurity` `WITH CHECK` policy rejected the
insert outright — a real threshold crossing would throw instead of being
recorded. Neither `RenewalThresholdSchedulerTests` (a `RecordingAuditWriter`,
no database) nor `RenewalThresholdSchedulerHostedServiceTests` (a
syntactically-valid-but-never-dialled connection string, by design) ever
exercised a real RLS-enforced connection on this path, so this went
undetected until r2-integration's own real-Postgres proof
(`Contigo.IntegrationTests.R2EndToEndTests`) surfaced it. The method now
opens its own scope before writing, the same convention
`RenewalActionService.SetActionAsync` already follows.

### Renewal alerts

`Contigo.Renewals.Application.RenewalAlertService` (task E03/F02/US01/T02,
the wave-spec's `renewal-alerts` artifact; parent story
us-01-threshold-scheduler AC-2/AC-3) closes the gap the section above
named: a persisted, de-duplicated `Contigo.Renewals.Domain.RenewalAlert` row
per raised `renewal.approaching` event, plus recompute-on-correction.

- **Creation (AC-2)** — `CreateFromEventsAsync` de-duplicates every raised
  `RenewalApproachingEvent` against this contract's own currently-`Active`
  alerts (keyed by tenant/contract/milestone/thresholdDays — a filtered
  unique index on that tuple, `WHERE status = 'Active'`, is the
  database-level backstop) and persists exactly one new row per genuinely
  new match, each writing one `renewal.alert_created` `IAuditWriter` entry.
  `Contigo.Worker.Scheduling.RenewalThresholdSchedulerHostedService` calls
  this immediately after every scheduler tick's own
  `EvaluateThresholdsAsync`, in the same DI scope.
- **Recompute (AC-3)** — `RecomputeForContractAsync` re-derives a contract's
  renewal date/cancellation deadline via `RenewalEngine.Calculate` and
  resolves (`renewal.alert_resolved`, status flips to `Resolved` — never
  deleted, Appendix C rule 5) any `Active` alert whose own `MilestoneDate`
  no longer matches, then re-runs `RenewalThresholdScheduler
  .EvaluateThresholdsAsync` for that one contract against the corrected
  terms so a correction landing exactly on a configured threshold today
  raises the same `renewal.approaching` event (and alert) a scheduled tick
  would raise tomorrow. `Contigo.Api.RenewalAlertRecomputeService` — the
  composition-root orchestrator ADR-002 requires for any code that touches
  both `Contigo.Documents.Contracts` and `Contigo.Renewals` (mirrors
  `NegotiationOutcomePropagationService`) — calls this from `PATCH
  /api/contracts/{id}` (see `ContractsEndpointExtensions`), but only when
  the correction actually touched `endDate` or `autoRenewal` (the only two
  `Contract` fields `ContractRenewalTerms` consumes today — a
  `cancellationDeadline`-only correction is a no-op for this purpose, since
  `RenewalEngine` derives its own deadline from `CancellationNoticeDays`,
  always `null` today, never from that raw field).

This module's second table, `renewal_alert`, and its RLS policy land in one
migration (`AddRenewalAlert` — the same "table doesn't pre-exist, so RLS is
not a retrofit" convention `Contigo.Quotes`'s own `AddSkuProductMapping`/
`AddNegotiationOutcome` migrations already established). No HTTP read
endpoint exists for alerts yet (no AC/task names one) — proven instead via
`Contigo.Renewals.Tests.RenewalAlertServiceTests`/
`RenewalAlertRlsCrossTenantIsolationTests` and
`Contigo.IntegrationTests.R2EndToEndTests`' own
`Renewal_alerts_are_created_from_thresholds_and_recomputed_on_contract_correction`.

## R1 demo smoke test

The automated proof of task E02/F06/US01/T01 (r1-integration) is
`dotnet test` — `Contigo.IntegrationTests.R1EndToEndTests` (AC-1/AC-2/AC-4:
upload → parse/OCR → classify → extract → portfolio → 360 → Ask Contigo
with citations → correction, plus a scanned/image fixture through the
`ocr` gateway role) and `R1CrossTenantIsolationTests` (AC-3, across the
whole path). To manually smoke-test the same path against a running
`dev`/`demo` deployment:

```bash
API=https://<api-host>
TENANT=$(curl -s -X POST "$API/api/workspaces" -H 'Content-Type: application/json' \
  -d '{"name":"Smoke Test Co"}' | jq -r .id)

# 201 only for an admitted contract-related document: a non-contract PDF
# gets 422 (reason not_a_contract | no_readable_text), an unsupported or
# mismatched format 415, a file over Documents:MaxFileBytes 413 -- see
# "Documents -- admission gate" above.
DOC=$(curl -s -X POST "$API/api/documents" -H "X-Tenant-Id: $TENANT" \
  -F "file=@contract.pdf;type=application/pdf" | jq -r .id)

# processingStatus/contractId reflect DocumentProcessingPipeline's own run
# (classify -> hybrid parse -> staged extraction -> RAG indexing) -- POST
# /api/documents runs it synchronously before responding.
curl -s "$API/api/documents/$DOC" -H "X-Tenant-Id: $TENANT" | jq .
CONTRACT=$(curl -s "$API/api/documents/$DOC" -H "X-Tenant-Id: $TENANT" | jq -r .contractId)

curl -s "$API/api/contracts" -H "X-Tenant-Id: $TENANT" | jq .
curl -s "$API/api/contracts/$CONTRACT" -H "X-Tenant-Id: $TENANT" | jq .

curl -s -X POST "$API/api/chat/query" -H "X-Tenant-Id: $TENANT" \
  -H 'Content-Type: application/json' -d '{"question":"What does this contract cover?"}' | jq .
```

Honest caveat: `IAiGateway` still binds to `FixtureAiGateway` (no live
Foundry/Document Intelligence endpoint exists yet, ADR-004/ADR-017) and its
`ExtractAsync` always returns an empty `{}` — a real `demo` upload today
lands `NeedsReview` with zero extracted facts. This smoke path proves the
*pipeline wiring* end-to-end (every stage runs, links, and is queryable),
not extraction accuracy; `R1EndToEndTests` proves the persistence/HTTP
contract against a scripted gateway that returns real, schema-shaped facts.

## R2 demo smoke test

The automated proof of task E03/F04/US01/T01 (r2-integration) is
`dotnet test` — `Contigo.IntegrationTests.R2EndToEndTests` (AC-1/AC-2: every
active contract gets a deterministic renewal date/cancellation deadline
where data exists, an explainable component-scored priority via `GET
/api/renewals/{id}/priority`, a `renewal.approaching` threshold event that
is durably recorded — never fabricated for a contract with an unknown end
date — and a `POST /api/renewals/{id}/action` upsert) and
`R2CrossTenantIsolationTests` (AC-3, across the whole `GET
/api/renewals` / `GET /api/renewals/{id}/priority` / `POST
/api/renewals/{id}/action` surface). Contracts are seeded directly against
the real, RLS-enforced `DocumentsContractsDbContext` (see
`R2IntegrationFixture.SeedContractAsync`) rather than through the R1 upload
path — R2's own leaf artifacts all take already-validated contract data as
an input, never produce it.

**Updated by task E03/F02/US01/T02 (renewal-alerts):** this task's own
wave-spec `depends_on` named `renewal-alerts`, which had not landed any code
as of this task's original run — only the `renewal.approaching` threshold
event (task E03/F02/US01/T01) existed then. `R2EndToEndTests` proved that
literal event and no more; a persisted, de-duplicated `RenewalAlert` row
with recompute-on-correction was still open. That task has since landed:
see "Renewal alerts" above, and `R2EndToEndTests`' own
`Renewal_alerts_are_created_from_thresholds_and_recomputed_on_contract_correction`
for the added proof (alert creation composed with the scheduler tick, then
`PATCH /api/contracts/{id}` resolving/re-raising alerts through the real
`Contigo.Api.RenewalAlertRecomputeService` wiring).

## Savings Intelligence — deterministic price normalization

`Contigo.Savings.Application.PriceNormalizationCalculator` (task E04/F02/US01/T01,
us-01-price-normalization, the wave-spec's `savings-normalization` artifact) is product spec
§4.3/§10's "Normalize current unit price and compare with benchmark P25/P50/P75... Calculate
current percentile, recommended target and savings range" made concrete: pure, synchronous
arithmetic over a `PriceComparisonRequest` (an already-fetched `Contigo.Benchmark.Contracts
.BenchmarkResult` plus the current total cost) — no database, no HTTP call, no LLM call (Appendix C
rule 6) — returning a `PriceComparisonResult` with a four-way `PriceComparisonStatus`:

- `Compared` — the benchmark had a well-ordered distribution and currencies matched: normalized
  unit price, percentile rank (0-100, linearly interpolated between P25/P50/P75 and clamped at the
  ends — never extrapolated beyond the last known marker), a recommended target range
  (`[min(P25, price), min(P50, price)]` — never above the current price) and a per-unit + total
  savings range are all populated.
- `InvalidQuantity` — `BenchmarkQuery.Quantity` is zero or negative: nothing is computed, not even
  the normalized unit price (division would be meaningless).
- `CurrencyMismatch` — `BenchmarkQuery.Currency` does not equal `BenchmarkResult.Currency`; this
  codebase has no exchange-rate service, so converting would fabricate a rate it does not actually
  know (Appendix C rule 10) — the normalized unit price is still reported in its own currency, but
  no comparison is attempted.
- `InsufficientBenchmarkData` — either `BenchmarkResult.Distribution` is null (ADR-001's explicit
  "insufficient market data" outcome) or it is present but not well-ordered (`P25 <= P50 <= P75`
  does not hold, a data-quality problem this calculator refuses to silently paper over rather than
  fail on).

`PriceComparisonRequest` deliberately reuses `Contigo.Benchmark.Contracts.BenchmarkQuery` (rather
than re-declaring supplier/quantity/term/currency on a second type) for the exact query a caller
already built to fetch the `BenchmarkResult` in the first place — so currency/quantity are
guaranteed to be the values the benchmark lookup itself used, and term alignment (comparing a
12-month contract against 12-month comparables, not 36-month ones) stays the Benchmark Service's
own matching responsibility (product spec §10.4), never re-derived here. `PriceComparisonResult`
echoes the original `BenchmarkResult` unchanged on every outcome, so `Confidence`/`Source`/
`ComparisonDimensions`/`SampleSize`/`UpdatedAt` are always reachable from one result without this
task re-declaring or guessing at task-02's (confidence + provenance propagation) own output shape.

Same "benchmark data only ever arrives as an already-known value, never a live call" convention
`Contigo.Renewals.Application.PriorityScoreCalculator` already established: this calculator's
public API structurally cannot accept a live `Contigo.Benchmark.IBenchmarkService`, so Appendix C
rule 3 ("never call a benchmark provider directly from renewal, savings or quote business logic")
can never become an accidental provider call from this module — proven by
`Contigo.Savings.Tests.PriceNormalizationCalculatorTests.Calculator_never_depends_on_the_live_Benchmark_Service_interface`.

`Contigo.Savings.Application.SavingsProvenanceClassifier` (task E04/F02/US01/T02, us-01-price-
normalization task-02, the wave-spec's `savings-provenance` artifact) closes AC-3 ("Show confidence
+ provenance on the comparison"): `PriceComparisonResult.Provenance` is a computed property — not a
constructor argument, so task-01's own tested shape is unchanged — that derives a
`Contigo.Savings.Application.SavingsProvenance` view from `PriceComparisonResult.Benchmark` on every
access. It carries a `Contigo.Savings.Domain.SavingsConfidenceLevel` (`Low`/`Medium`/`High`, spec's
own UI vocabulary for "Benchmark confidence") alongside the raw `[0, 1]` confidence score,
source/comparison-dimensions/sample-size/updated-at (all echoed unchanged from `BenchmarkResult`),
and a deterministic one-line `Summary`. `Classify`'s thresholds (`HighConfidenceThreshold` = 0.7,
`MediumConfidenceThreshold` = 0.4) are this classifier's own documented, adjustable heuristic — not
a council-locked figure — chosen so `FixtureBenchmarkAdapter`'s own catalog already spans all three
tiers (full-sample matches are High; Zoom's thinner 30-of-50 sample is Medium; Snowflake's 18-of-50
sample, and any supplier+product-only weak match, are Low). `Provenance` is available regardless of
`PriceComparisonResult.Status` — `BenchmarkResult`'s own provenance fields are always populated, so
a caller can show confidence/provenance even for an insufficient-data or currency-mismatch result,
never just the `Compared` case.

Deliberately out of this task's file scope, each a later task's own: a persisted, trackable
`SavingsOpportunity` with status/owner/realized outcome (us-02-savings-opportunity), and any
host/worker wiring that calls `PriceNormalizationCalculator` against real contracts — the same
"wiring lands with the first real caller" sequencing this README's other modules already follow
(see "Renewal Intelligence" above). `AddSavingsModule`/DI registration does not exist yet for the
same reason: nothing calls this calculator from a host yet.

**Incidental fix, task E04/F02/US01/T02:** `backend/tests/Contigo.Benchmark.Tests/ServiceCollectionExtensionsTests.cs`
failed to compile (a prior merge had spliced one test method's closing brace together with a second,
differently-named test's signature line, discarding that second method's body) — fixed to restore
`dotnet build Contigo.slnx`, since a broken build blocks every task, not just this one. The
recovered test body is verified against this module's own current source, not guessed; the
unrecoverable second test is not reinvented. That repair surfaced a separate, still-open
`Contigo.Benchmark` wiring gap, left exactly as found (not this task's module or file scope):
`AddBenchmarkModule` registers `FixtureBenchmarkAdapter` directly as `IBenchmarkService` via
`TryAddSingleton`, but the preceding `TryAddSingleton<IBenchmarkService, BenchmarkAdapterRegistry>`
call already claims that slot (first registration wins), and `FixtureBenchmarkAdapter` does not
implement `IBenchmarkProviderAdapter`, so `BenchmarkAdapterRegistry` can never reach it either — a
container built from `AddBenchmarkModule()` resolves `IBenchmarkService` to an always-adapter-less
`BenchmarkAdapterRegistry` today, not `FixtureBenchmarkAdapter`, contradicting
`Resolved_service_fails_honestly_when_no_adapter_is_registered_yet`'s own
`Assert.IsType<FixtureBenchmarkAdapter>` (that test now fails, honestly, instead of the file
silently not compiling). Fixing the wiring itself is us-02-fixture-adapter's/the adapter-registry
task's own module to redesign, not a Savings-module task's file scope.
Deliberately out of this task's file scope, each a later task's own: confidence + provenance
propagation into whatever surface displays this result (us-01-price-normalization task-02), and any
host/worker wiring that calls `PriceNormalizationCalculator` against real contracts — the same
"wiring lands with the first real caller" sequencing this README's other modules already follow (see
"Renewal Intelligence" above). This calculator itself still has no DI registration for the same
reason: nothing calls it from a host yet — see the next section for what `AddSavingsModule` *does*
now register.

## Savings Intelligence — trackable SavingsOpportunity

`Contigo.Savings.Domain.SavingsOpportunity` (task E04/F02/US02/T01, savings-opportunity, the
wave-spec's `savings-opportunity` artifact; parent story us-02-savings-opportunity AC-2) is this
module's first persisted entity — product spec §6's core data model row "SavingsOpportunity |
supplier, contract/quote, type, current_spend, estimated savings range, confidence, status, owner"
(module-map.md: "Savings | SavingsOpportunity, RealizedSavings | `/api/savings`") made concrete:
`SupplierId`/`ContractId` are cross-module references by id only, deliberately no foreign key (same
treatment `Contigo.Renewals.Domain.RenewalAction.ContractId` already gives its own cross-module
reference — ADR-002 forbids this module from referencing `Contigo.Suppliers.Products` or
`Contigo.Documents.Contracts` at all); `Type` is free text (no ADR/spec fixes a vocabulary);
`CurrentSpend`/`EstimatedSavingsLow`/`EstimatedSavingsHigh` carry an explicit `Currency` (this
codebase has no currency-conversion service anywhere); `Confidence` echoes
`Contigo.Benchmark.Contracts.BenchmarkResult.Confidence` (spec §4.3 "Show benchmark confidence and
provenance"). `Status` (`Identified` / `InProgress` / `Realized`) is read directly off spec §4.3's
own three dashboard KPI buckets ("savings identified" / "savings in progress" / "savings realized")
— see `SavingsOpportunityStatus`'s own doc comment for why no fourth "rejected/dismissed" state
exists yet.

`Contigo.Savings.Application.SavingsOpportunityService` backs `GET /api/savings` (list, newest
identified first) and `PATCH /api/savings/{id}` (a genuine partial update of `owner`/`status`, either
or both — see the HTTP surface table above), tenant-scoped via `ITenantContext.BeginScope` the same
way `Contigo.Renewals.Application.RenewalActionService` is, and writes one `IAuditWriter` entry per
successful mutation (`savings_opportunity.identified` / `savings_opportunity.updated` — spec §14.1).
Also exposes `CreateAsync` ("identify"), proven by `Contigo.Savings.Tests
.SavingsOpportunityServiceTests` but not yet wired to an HTTP route — this task's own AC-1 names only
`GET`/`PATCH`, and nothing in this codebase yet maps a real `PriceComparisonResult` against a real
contract into a `CreateSavingsOpportunityRequest`; that composition (in `Contigo.Api`, "the one
project allowed to reference every module") is a follow-up, the same "wiring lands with the first
real caller" gap the previous section names for `PriceNormalizationCalculator` itself.

**Task E04/F04/US01/T01 (r3-integration)** proves the whole chain this gap still leaves manual —
`IBenchmarkService.GetBenchmarkAsync` -> `PriceNormalizationCalculator.Compare` ->
`SavingsOpportunityService.CreateAsync` -> `PATCH .../{id}` (owner, then a realized value) ->
`GET /api/savings`/`GET /api/savings/kpis` — end to end against the real host and a real, migrated,
RLS-enforced database: `Contigo.IntegrationTests.R3EndToEndTests` resolves `IBenchmarkService`/
`SavingsOpportunityService` directly from the host's own container (the same "no dedicated route
exists yet, exercise the service the host resolves" convention `R2EndToEndTests` already established
for `RenewalActionService`), since no real caller maps a contract's line items into a
`BenchmarkQuery` yet either (no supplier-name/geography field exists on `Contract` today). See "R3
demo smoke test" below.

`AddSavingsModule` (task E04/F02/US02/T01) gives this module its first `DbContext`
(`SavingsDbContext`) and is now called by `Contigo.Api`'s `Program.cs` — RLS is wired the same
`AddTenantRowLevelSecurity` migration + `TenantRlsConnectionInterceptor` mechanism every other
tenant-scoped module uses (ADR-009), proven by `Contigo.Savings.Tests
.SavingsOpportunityRlsMigrationCheckTests`/`SavingsOpportunityRlsCrossTenantIsolationTests`.
`Contigo.Worker` is not wired to this module yet (no worker job creates opportunities today) — the
same "wiring lands with the first real caller" gap, not attempted by this task.

**Task E04/F02/US02/T02 (realized-savings)** closes the gap the paragraph above used to name:
`Contigo.Savings.Domain.RealizedSavings` (module-map.md's own second named entity for this module,
"Record realized value + audit event", parent story AC-3) is this module's second tenant-scoped
table — one append-only row per captured realized value (never a destructive overwrite, the same
"never destructively overwrite" spirit `Contigo.Documents.Contracts.Domain.ContractVersion`/
`CorrectionHistory` already apply to their own history), in the opportunity's own `Currency` (no
per-row currency — this codebase has no currency-conversion service anywhere). `PATCH
/api/savings/{id}`'s `realizedAmount` field (see the HTTP surface table above) is the only writer,
via `SavingsOpportunityService.UpdateAsync`: a non-negative `realizedAmount` always finalizes
`Status` as `Realized` (either because the caller's own explicit `status` already said so, or
automatically when `status` was omitted — the two facts are not independent, see
`SavingsOpportunityStatus.Realized`'s own doc comment) and inserts one new `RealizedSavings` row,
still exactly one `IAuditWriter` entry per call (`savings_opportunity.realized` takes the place of
`savings_opportunity.updated` for that call, never both). RLS is wired the same
`AddRealizedSavingsRowLevelSecurity` migration + `TenantRlsConnectionInterceptor` mechanism as
every other tenant-scoped table (ADR-009) — proven by `Contigo.Savings.Tests
.SavingsOpportunityRlsMigrationCheckTests` (dynamic per-table discovery, no test change needed) and
the new `RealizedSavingsRlsCrossTenantIsolationTests`. Honest gap, deliberately out of this task's
own file scope: `GET /api/savings`'s list response does not surface any opportunity's realized-value
history — only the `PATCH` response that just recorded one does (see
`SavingsOpportunityResult.RealizedAmount`'s own doc comment) — a rolled-up read (e.g. for the
dashboard's own "savings realized" KPI, spec §4.3) is a follow-up, the same "wiring lands with the
first real caller" gap this section's other paragraphs already document.

## Savings Intelligence — procurement homepage KPIs

Task E04/F03/US01/T01 (savings-kpis, the wave-spec's `savings-kpis` artifact; parent story
us-01-savings-kpis AC-1) adds `GET /api/savings/kpis` — see the HTTP surface table above for the
response shape. Two new pure calculators do the actual arithmetic, each unit-tested independently
of any database (same convention `Contigo.Renewals.Application.RenewalPipelineBuilder`/
`PriorityScoreCalculator` already establish):

- `Contigo.Savings.Application.SavingsKpiCalculator` groups every tenant-scoped
  `SavingsOpportunity` by `Status` then `Currency` for the "Savings Identified"/"Savings In
  Progress"/"Savings Realized" thirds (`SavingsKpiQueryService` is its thin EF-backed fetch half).
- `Contigo.Documents.Contracts.Application.PortfolioAnalysisCalculator` computes "Contracts
  Analyzed"/"Annual Spend Analyzed" from every tenant-scoped `Contract`, flagged by whether any
  linked `Document` reached `DocumentProcessingStatus.Completed` — a `Contract` row alone is not
  "analyzed" (`StagedExtractionService.EnsureContractAsync` creates one as a bootstrap shell before
  extraction even starts) — see that calculator's own doc comment.
  (`PortfolioQueryService.GetAnalysisSummaryAsync` is its fetch half.)

Every money value in the response is grouped by currency, never summed across currencies — the
same "no exchange-rate service anywhere in this codebase" reasoning
`Contigo.Savings.Domain.SavingsOpportunity.Currency`'s own doc comment already gives. "Upcoming
Renewals" adds no dependency on `Contigo.Renewals` at all: `Contigo.Api.SavingsKpiEndpointExtensions`
reuses the exact same auto-renewing-contract query `GET /api/renewals` already runs for its own
`totalCount`, so the homepage KPI and the renewal pipeline list can never silently disagree.

Honest gap, deliberately out of this task's own file scope: `savingsRealized` is computed from each
`SavingsOpportunity`'s own `EstimatedSavingsLow`/`EstimatedSavingsHigh` range, not the separate,
audit-tracked `RealizedSavings` entity — this task's wave-spec dependency is `savings-opportunity`
only (`RealizedSavings` is task E04/F02/US02/T02's own deliverable, scheduled the same wave-spec
phase, so it is not a dependency this task can assume has landed).

## Savings Intelligence — opportunity list confidence tier

Task E04/F03/US01/T02 (savings-list, the wave-spec's own artifact of that name; parent story
us-01-savings-kpis AC-2/AC-3) closes the one part of `GET /api/savings` (and, via the shared
`ToResponse` wire-shaping, `PATCH /api/savings/{id}`) that AC-3 ("Returns provenance + confidence,
never fabricated precision") still left open: tenant scoping (AC-2) and a raw `confidence` score
already existed from task E04/F02/US02/T01, but nothing paired that decimal with an honest,
interpretable signal. `SavingsOpportunityResult.ConfidenceLevel` — a computed property, not a
constructor argument, the same "cannot drift from its one source of truth" shape
`PriceComparisonResult.Provenance` already established — applies the existing
`Contigo.Savings.Application.SavingsProvenanceClassifier.Classify` (task E04/F02/US01/T02,
`savings-provenance`) to each opportunity's own `Confidence`, so both call sites now report the same
`Low`/`Medium`/`High` tier a live benchmark comparison would.

Deliberately does **not** attempt the fuller `Contigo.Savings.Application.SavingsProvenance` shape
(source, comparison dimensions, sample size, benchmark updated-at) on `SavingsOpportunity`: those
fields describe a specific `BenchmarkResult` comparison, and nothing in this codebase persists one
against a `SavingsOpportunity` row today — `CreateSavingsOpportunityRequest` only ever receives the
already-reduced `Confidence` score (see that request's own doc comment on why no host wires a real
caller yet). Fabricating a source/sample-size/updated-at this entity does not actually have on file
would be exactly the imprecision AC-3 forbids (Appendix C rule 10); a caller that needs the full
`SavingsProvenance` for a live comparison still reaches it via `PriceComparisonResult.Provenance` at
comparison time. Persisting real per-opportunity provenance is a follow-up for whichever future task
first wires `PriceNormalizationCalculator`'s output into `SavingsOpportunityService.CreateAsync` —
the same "wiring lands with the first real caller" gap this README's other Savings sections already
document.

## R3 demo smoke test

The automated proof of task E04/F04/US01/T01 (r3-integration) is `dotnet test` —
`Contigo.IntegrationTests.R3EndToEndTests` (AC-1: a "matched contract" benchmark comparison reports
current price + P25/P50/P75 + percentile/target/saving/confidence/provenance for a confident fixture
match, and honestly abstains — still with confidence/provenance, never a bare failure — when the
matched comparable is dimensionally strong but statistically too thin (`fixture-confidence`, task
E04/F01/US02/T02); AC-2: a `SavingsOpportunity` is identified from that comparison, owned via `PATCH
/api/savings/{id}`, listed with its confidence tier (`savings-list`), and marked realized
(`realized-savings`) — with `GET /api/savings/kpis` reflecting each move; AC-3: the only
`IBenchmarkProviderAdapter` registered anywhere in the composed host is `FixtureBenchmarkAdapter`) and
`R3CrossTenantIsolationTests` (the same AC-2 surface proven isolated across two tenants, the same
"drive the whole path across two tenants through the real host" value-add
`R1CrossTenantIsolationTests`/`R2CrossTenantIsolationTests` already established). Run just these:

```bash
cd backend
dotnet test Contigo.slnx --configuration Release --filter "FullyQualifiedName~R3"
```

To manually smoke-test the parts of this path that already have a public HTTP surface, against a
running `dev`/`demo` deployment:

```bash
API=https://<api-host>
TENANT=$(curl -s -X POST "$API/api/workspaces" -H 'Content-Type: application/json' \
  -d '{"name":"Smoke Test Co"}' | jq -r .id)

# A fresh tenant honestly starts at all-zero KPIs — no fabricated baseline.
curl -s "$API/api/savings/kpis" -H "X-Tenant-Id: $TENANT" | jq .
curl -s "$API/api/savings" -H "X-Tenant-Id: $TENANT" | jq .

# Once an opportunity id exists for this tenant (see honest caveat below), its lifecycle is fully
# curl-able: own it, then realize it, then watch the KPI bucket move.
OPPORTUNITY=<opportunity-id>
curl -s -X PATCH "$API/api/savings/$OPPORTUNITY" -H "X-Tenant-Id: $TENANT" \
  -H 'Content-Type: application/json' -d '{"owner":"procurement@acme.example","status":"InProgress"}' | jq .
curl -s -X PATCH "$API/api/savings/$OPPORTUNITY" -H "X-Tenant-Id: $TENANT" \
  -H 'Content-Type: application/json' -d '{"realizedAmount":20000}' | jq .
curl -s "$API/api/savings/kpis" -H "X-Tenant-Id: $TENANT" | jq .
```

Honest caveat: identifying a *new* `SavingsOpportunity` from a live benchmark comparison
(`IBenchmarkService.GetBenchmarkAsync` -> `PriceNormalizationCalculator.Compare` ->
`SavingsOpportunityService.CreateAsync`) has no public HTTP route yet — `CreateSavingsOpportunityRequest`'s
own doc comment names why, and this task deliberately did not invent a contract-to-`BenchmarkQuery`
mapping to close it (no supplier-name/geography field exists on a real `Contract` yet; fabricating one
would misrepresent data this codebase does not actually have, Appendix C rule 10). This smoke path
proves the *lifecycle* HTTP surface end to end (own -> list -> realize -> KPI rollup, all tenant-scoped
and RLS-enforced); `R3EndToEndTests` proves the benchmark-comparison half — and the identify step that
bridges the two — against the real host directly, the same "no dedicated route yet, exercise the
service the host resolves" convention `R2EndToEndTests` already established for `RenewalActionService`.

## Quote Check — quote upload + line-item extraction

`Contigo.Quotes` (task E05/F01/US01/T01, quote-extraction; parent story
us-01-quote-line-extraction) is the first Quotes-module task: `POST
/api/quotes` (see the HTTP surface table above) uploads a supplier quote
and runs schema-constrained line-item extraction synchronously before
responding — the same "read the bytes once, run the pipeline inline"
shape `POST /api/documents`/`DocumentProcessingPipeline` already
established for contracts (task E02/F06/US01/T01).

- `Contigo.Quotes.Domain.Quote`/`QuoteExtractionJob`/`QuoteLine` are this
  module's own entities — deliberately **not** a reference to
  `Contigo.Documents.Contracts.Domain.Document`/`Contract`: ADR-002 forbids
  `Contigo.Quotes` from referencing `Contigo.Documents.Contracts` at all
  (its allowed Contigo references are exactly `[SharedKernel, Benchmark]`
  — see "Dependency direction" below), and a quote is not a contract (spec
  §11's own Quote → Benchmark → Assessment → Negotiate → **Contract** flow
  treats "becomes a contract" as a later, explicit step).
- `Contigo.Api.QuoteExtractionPipeline` (internal — host-composition
  wiring, the same treatment `Contigo.Worker.Queue.QueueConsumerHostedService`
  already gets from `Contigo.ArchitectureTests
  .DependencyDirectionTests.Host_must_not_contain_domain_types`) is the one
  place that calls both `Contigo.AiGateway` and `Contigo.Quotes`: it reuses
  the epic-02 `Contigo.Documents.Contracts.Application.Extraction
  .HybridDocumentParsingService` verbatim (native text extraction, or the
  `ocr` gateway role — Azure AI Document Intelligence, ADR-017 — for
  scanned/image/low-text quote PDFs; full document, no 2-page cap; AC-4),
  then runs one `extract` call against `Contigo.Quotes.Application
  .Extraction.QuoteLineJsonSchema.LineItems()` and hands the raw payload to
  `Contigo.Quotes.Application.Extraction.QuoteLineExtractionService` to
  persist.
- AC-3 ("Separate arithmetic from LLM language", Appendix C rule 6): the
  line-item schema has **no** computed-total property at all — the model
  reports only `quantity`/`sku`/`edition`/`unitPrice`/`listPrice`/
  `discountPercent`/`term`. `QuoteLineExtractionService.ComputePricing`
  derives `QuoteLine.UnitPrice` (from `listPrice`/`discountPercent` when
  the model did not report a unit price directly) and
  `QuoteLine.ExtendedPrice` (`quantity × unitPrice`) in plain C# decimal
  arithmetic — proved directly by
  `Contigo.Quotes.Tests.QuoteLineExtractionServiceTests` and end-to-end by
  `Contigo.IntegrationTests.QuoteEndToEndTests`.
- Every line carries the same evidence + confidence tail as every other
  extraction pipeline in this codebase (`sourceSpan`/`sourcePage`/
  `confidence`, Appendix C rule 2) directly on the `QuoteLine` row — one
  row is already one fact, the same shape
  `Contigo.Documents.Contracts.Domain.ContractLineItem` uses (no separate
  evidence side-table).
- Deliberately out of task-01's own scope (not silently absorbed): the
  `Quote`-level aggregate fields spec §6 also names ("supplier, dates,
  currency, values, status") and benchmark matching/assessment/negotiation
  (spec §11's later Quote Check steps, `GET /api/quotes/{id}/assessment`,
  `POST /api/negotiations/outcomes`) — task-01's own coding objective was
  "Quote upload + line-item extraction". See below for task-02
  ("Line-item normalization + evidence/confidence"). **Task E05/F02/US01/T01
  (market-assessment) closed the supplier/currency/geography/purchase-date
  and benchmark-matching/assessment half of this gap** — see "Market
  Assessment" below; negotiation (`POST /api/negotiations/outcomes`)
  remains future work no task has picked up yet.

**Task E05/F01/US01/T02 (quote-normalization)** adds spec §11.1's next
pipeline step, "Normalize unit economics" (between "Extract" and "Match
benchmark"), right after line-item extraction inside the same
`QuoteExtractionPipeline.ProcessAsync` unit of work — before the one
shared `SaveChangesAsync`, so extraction and normalization persist
together or not at all. No new AI Gateway role and no new project
reference: `Contigo.Quotes.Application.Normalization
.QuoteLineNormalizationService.NormalizeUnitEconomics` is a second pure,
deterministic calculator alongside task-01's own `ComputePricing` — same
Appendix C rule 6 discipline, applied to a second pipeline stage.
`QuoteLine` gains two columns: `NormalizedAnnualUnitPrice` (`UnitPrice`
rescaled to an annual rate) and `NormalizedTermMonths` (the recognized
cadence length, in months, that produced it — kept as evidence, the same
"never a consequential derived fact without a way to see why" spirit
`SourceSpan`/`SourcePage` already give the raw extraction).
`Contigo.Quotes.Application.Normalization.QuoteBillingCadence
.RecognizeMonths` deliberately recognizes only a small, fixed,
unambiguous vocabulary (`monthly`/`quarterly`/`semi-annual`/`annual` and
their common synonyms — 1/3/6/12 months respectively); a numeric
commitment length ("36 months", "3 years"), "one-time"/"perpetual", a
blank term, or any other free text `QuoteLine.Term` may legitimately hold
(no ADR or spec fixes a closed vocabulary — see that property's own doc
comment) is left honestly unresolved (both new columns stay `null`)
rather than guess a billing-period relationship this codebase does not
actually know — the same restraint
`Contigo.Savings.Application.PriceComparisonRequest`'s own doc comment
already documents for cross-module term alignment (Appendix C rule 10).
A `null` `NormalizedAnnualUnitPrice` on any line **is** spec §11.3's own
"Do not generate a savings target if line-item normalization is
unresolved" guardrail made checkable — this task does not itself gate
anything (no savings target exists yet for a quote to gate), it only
produces the honest, queryable signal for whatever future benchmark-match
task reads it. `POST /api/quotes`'s response gains
`normalizedLineItemCount`/`unresolvedNormalizationCount` (see the HTTP
surface table above) so the same outcome is visible over HTTP, not just
in the database — proved directly by
`Contigo.Quotes.Tests.QuoteLineNormalizationServiceTests` and, for the
already-recognized-cadence common case, end-to-end by the existing
`Contigo.IntegrationTests.QuoteEndToEndTests` fixture (`"term":"Annual"`).

**Task E05/F01/US02/T01 (sku-normalization)** adds story
us-02-sku-normalization's own AC-1 ("Normalize SKU/edition to the
canonical product mapping") and the "show unmatched SKUs" half of AC-2:

- `Contigo.Quotes.Domain.SkuProductMapping` is this module's own,
  self-contained "canonical product mapping" — a tenant-scoped
  raw-normalized-SKU → canonical-SKU/edition/product-name table, **not** a
  reference into `Contigo.Suppliers.Products` (still an empty scaffold, and
  ADR-002 forbids `Contigo.Quotes` from referencing it or any other domain
  module's internals at all). `Contigo.Quotes.Application.Normalization
  .SkuNormalizer.Normalize` is the pure, deterministic text rule (trim,
  collapse whitespace, uppercase; punctuation is left untouched on purpose
  — see that type's own doc comment) both sides of the lookup share.
  `SkuNormalizationService.NormalizeAsync` re-reads a quote's own lines from
  the database and sets each one's `NormalizedSku`/`NormalizedEdition`/
  `MatchStatus` (`NotApplicable`/`Unmatched`/`Matched` —
  `Contigo.Quotes.Domain.SkuMatchStatus`); `Contigo.Api.QuoteExtractionPipeline`
  calls it right after persisting a quote's freshly-extracted lines, so
  every upload gets a real match status, not just a later explicit
  recalculate call.
- Honest gap, by construction: nothing writes a `SkuProductMapping` row yet
  (task E05/F01/US02/T02, "Manual product mapping + recalculate trigger",
  is its intended first writer), so every tenant starts with zero mappings
  and a line with a present SKU is always `Unmatched` today. This is spec
  §11.3's own guardrail ("Do not generate a savings target if line-item
  normalization is unresolved") made concrete rather than a limitation of
  this task: no benchmark/assessment step for quotes exists yet either for
  a resolved mapping to unblock.
- Proved directly by `Contigo.Quotes.Tests.SkuNormalizationServiceTests`
  (pure normalization, pure per-line matching, and a real-Postgres+RLS
  persistence/re-run/cross-tenant proof). `POST /api/quotes`' response now
  also carries `unmatchedSkuCount` (see the HTTP surface table above) —
  `Contigo.IntegrationTests.QuoteEndToEndTests` still passes unchanged with
  it present (that test's own fixture quote has no seeded mapping, so it is
  `1`), but no test yet asserts that field's value over real HTTP
  specifically; the persistence-level proof above is this task's own
  Definition of Done.

**Task E05/F01/US02/T02 (sku-recalculate)** closes story us-02-sku-normalization's
own AC-2 "...and allow manual product mapping" half and AC-3 "Re-run assessment
after mapping correction" — the intended first writer of `SkuProductMapping`
task-01's own doc comment named but never itself wrote:

- `Contigo.Quotes.Application.Normalization.SkuMappingService.RecalculateAsync`
  backs `POST /api/quotes/{id}/assessment/recalculate` (see the HTTP surface
  table above for the full request/response shape). For each caller-supplied
  correction it upserts one `SkuProductMapping` row (update in place when one
  already exists for that tenant+normalized-SKU — never a duplicate insert,
  the unique index would reject one anyway), then re-runs
  `SkuNormalizationService.NormalizeAsync` for every line on the quote and
  `MarketAssessmentService.AssessAsync` — composing both already-accepted
  services rather than re-deriving their logic, the same "reuse, do not
  re-implement" posture `NegotiationStrategyService` already takes for
  `MarketAssessmentService`.
- `mappings` is optional — a caller may POST `{}` to re-read the current
  unmatched-line list and a fresh assessment with no correction at all (e.g.
  right after `POST /api/quotes` reports a non-zero `unmatchedSkuCount`,
  before any correction has been decided).
- AC-2's "Show unmatched SKUs" half is deliberately **not** a field on `GET
  /api/quotes/{id}/assessment` itself: `LineMarketAssessment` is task
  E05/F02/US01/T01's own already-accepted file, and
  `NegotiationStrategyService`'s own doc comment already declined to extend
  it for the identical "do not touch unrelated wave artifacts" reason. This
  task follows that same precedent — `unmatchedLines` on the recalculate
  response is its own small, independent read instead
  (`SkuMappingService.GetUnmatchedLinesAsync`).
- A `SkuProductMapping` is tenant-scoped, not quote-scoped (see that type's
  own doc comment) — a correction made while looking at one quote also
  resolves every other quote for the same tenant sharing the same normalized
  SKU, the next time *that* quote is itself (re)normalized (a fresh upload,
  or its own recalculate call) — proved directly by
  `Contigo.Quotes.Tests.SkuMappingServiceTests`
  `RecalculateAsync_a_mapping_learned_on_one_quote_resolves_a_different_quote_on_its_own_next_refresh`.
- Owns its own tenant scope (`ITenantContext.BeginScope`) from day one — the
  always-404-in-production class of bug task E05/F04/US01/T01 (r4-integration)
  found and fixed for `MarketAssessmentService`/`NegotiationStrategyService`
  (see "Market Assessment" below) is not repeated here.
- Proved directly by `Contigo.Quotes.Tests.SkuMappingServiceTests` (pure
  per-correction upsert rule, and a real-Postgres+RLS persistence proof:
  create, update-in-place, validation-before-any-write, cross-tenant 404,
  cross-quote reuse, and a full correct-then-assess chain against the real
  `FixtureBenchmarkAdapter`) and end to end by
  `Contigo.IntegrationTests.R4EndToEndTests`, which now drives this real
  endpoint over real HTTP for AC-2 instead of the direct-service-call
  workaround that class's own doc comment used to describe.

## Market Assessment — benchmark matching + above/in-line/below

Task E05/F02/US01/T01 (market-assessment; parent story us-01-market-assessment
AC-1 "Match normalized line items to the Benchmark Service
(multi-dimensional)", AC-2's own "flag" half, AC-3 "`GET
/api/quotes/{id}/assessment` returns the assessment with
confidence/provenance") closes the gap this section's own task-01 paragraph
used to name ("benchmark matching/assessment/negotiation remain future work
no task has picked up yet") and the gap `Contigo.Quotes.Infrastructure
.ServiceCollectionExtensions.AddQuotesModule`'s own doc comment used to name
("deliberately does not call `AddBenchmarkModule`... nothing this task adds
resolves `IBenchmarkService` yet").

- **`Quote` gains its own benchmark-matching fields**: `Supplier`,
  `Currency`, `Geography`, `PurchaseDate` — spec §6's "Quote-level aggregate
  fields" that task-01 deliberately deferred. Unlike the identical-looking
  gap `Contigo.IntegrationTests.R3IntegrationFixture`'s own doc comment left
  open for `Contigo.Documents.Contracts.Domain.Contract` (ADR-002 forbids
  `Contigo.Savings` from reaching into that module at all), `Contigo.Quotes`
  owns both `Quote` and `QuoteLine` itself — no cross-module reference is
  involved — so there was no architectural reason to leave this one open
  once a task actually needed it. All four are populated by explicit,
  **optional** `POST /api/quotes` form fields (see the HTTP surface table
  above), never inferred from the document text (Appendix C rule 10):
  nothing in this codebase extracts a document-level supplier/geography/
  currency, and spec §11.1's own "Identify supplier" workflow step has no
  task/UI of its own yet. A quote uploaded without them is simply not
  matchable yet — an honest, expected state
  (`Contigo.Quotes.Application.Assessment.MarketAssessmentQueryBuilder`
  reports that per line, naming exactly which dimension is missing), not a
  validation error at upload time.
- **`AddQuotesModule` now also calls `Contigo.Benchmark
  .ServiceCollectionExtensions.AddBenchmarkModule`** — the same "a module
  that depends on another module's interface registers that dependency's
  own DI wiring transitively" convention `Contigo.Savings
  .Infrastructure.ServiceCollectionExtensions.AddSavingsModule`'s own doc
  comment already established for this exact call (and explicitly
  anticipated a future `Contigo.Quotes` caller doing the same).
  `Contigo.Quotes.csproj`'s own `ProjectReference` to `Contigo.Benchmark`
  pre-dated this task (an R4 scaffold anticipating this exact step) — this
  is that compile-time dependency's first runtime DI registration.
- **`Contigo.Quotes.Application.Assessment.MarketAssessmentQueryBuilder`**
  builds a `Contigo.Benchmark.Contracts.BenchmarkQuery` per line: `Product`
  from `QuoteLine.Description`, `Sku` from `NormalizedSku` (falling back to
  the raw `Sku`), `Quantity`/`Term` from the line, `Supplier`/`Geography`/
  `Currency`/`PurchaseDate` from the quote. Pure, honest, never fabricates a
  missing dimension. **Deliberately compares the line's raw `UnitPrice`, not
  `NormalizedAnnualUnitPrice`**: that annualized figure only exists for a
  term `QuoteBillingCadence` recognizes (a word vocabulary — "annual",
  "monthly", ...), a different, narrower vocabulary than
  `Contigo.Benchmark.Fixtures.FixtureBenchmarkAdapter`'s own catalog `Term`
  values ("12 months", "36 months") — mirrors `Contigo.Savings.Application
  .PriceComparisonRequest`'s own "term alignment is the Benchmark Service's
  own matching responsibility, no additional term-arithmetic here" doc
  comment.
- **`Contigo.Quotes.Application.Assessment.MarketAssessmentCalculator`**
  flags the line's price `BelowMarket`/`InLine`/`AboveMarket` against the
  matched `BenchmarkResult.Distribution`'s `[P25, P75]` band (at-or-below
  P25 is below market; at-or-above P75 is above; anything else, including
  exactly P50, is in line) — or the honest
  `Contigo.Quotes.Domain.MarketAssessmentStatus.InsufficientBenchmarkData`
  when the benchmark has no usable distribution (ADR-001), never a
  fabricated flag (Appendix C rule 10).
- **`Contigo.Quotes.Application.Assessment.MarketAssessmentProvenanceClassifier`**
  mirrors `Contigo.Savings.Application.SavingsProvenanceClassifier` field-
  for-field and threshold-for-threshold (High ≥ 0.7, Medium ≥ 0.4) —
  duplicated, not shared: ADR-002's allowed-reference set for
  `Contigo.Quotes` is exactly `[SharedKernel, Benchmark]`.
  `Contigo.Quotes.Application.Assessment.MarketAssessmentService.AssessAsync`
  is the one place in this module that actually calls
  `IBenchmarkService.GetBenchmarkAsync` — Appendix C's benchmark rule names
  the provider *adapter*, not this abstraction (`IBenchmarkService`'s own
  doc comment: "Domain modules depend on this abstraction only").
- Proved directly by `Contigo.Quotes.Tests.MarketAssessmentCalculatorTests`/
  `MarketAssessmentQueryBuilderTests` (pure, no database) and end to end by
  `Contigo.Quotes.Tests.MarketAssessmentServiceTests` against a real
  Postgres+RLS database and the real `FixtureBenchmarkAdapter` (never a
  stub) — one quote, three lines, demonstrating `Assessed`/
  `QuoteDataUnresolved`/`InsufficientBenchmarkData` together, the same
  "build a query by hand that matches a real fixture catalog row" convention
  `Contigo.IntegrationTests.R3EndToEndTests` already established for the
  analogous Savings comparison.
- **Task E05/F02/US01/T02 (target-saving)** closes the gap this section's own
  task-01 paragraph used to name ("recommended target range and potential
  saving... are task-02's own, separate `target-saving` wave-spec artifact"):
  `Contigo.Quotes.Application.Assessment.TargetSavingCalculator.Compute`
  computes spec §11.2's "Recommended target"/"Potential saving" rows —
  `RecommendedTargetLow/High = min(P25/P50, unitPrice)` (never above the
  current price) and `SavingsRangeLow/High` (per-unit) +
  `TotalSavingsRangeLow/High` (scaled by `QuoteLine.Quantity` — the
  `CHF 80-110k`-shaped total spec §11.2's own example shows, not a per-unit
  rate). Mirrors `Contigo.Savings.Application.PriceNormalizationCalculator`'s
  own target/savings-range formula exactly — duplicated, not referenced,
  the same `[SharedKernel, Benchmark]`-only reference rule
  `MarketAssessmentProvenanceClassifier` already follows. Never fabricates: a
  benchmark with no usable distribution returns a `LineMarketAssessment
  .TargetSaving` with every numeric field `null` plus a named reason —
  still a real object, never silently withheld, the same benchmark-trust
  posture `Provenance` already takes for `InsufficientBenchmarkData` (spec
  §11.3). `LineMarketAssessment` gained a `Quantity` field (echoed from
  `QuoteLine.Quantity`, the same "caller never has to re-fetch the line"
  posture `UnitPrice` already has) so `TargetSaving` can scale its total
  figures without a second database round-trip. `GET
  /api/quotes/{id}/assessment`'s response gained a `targetSaving` object per
  line (see the HTTP surface table above) alongside the existing
  `benchmark`/`confidence` objects. Proved directly by
  `Contigo.Quotes.Tests.TargetSavingCalculatorTests` (pure, no database,
  mirroring `MarketAssessmentCalculatorTests`'s own shape) and end to end by
  the same `MarketAssessmentServiceTests` fixture above — the parent story
  us-01-market-assessment Definition of Done in full ("`dotnet test` proves
  assessment + target/saving from fixture benchmark"). Negotiation strategy
  generation is task E05/F03/US01/T01's own scope — see "Negotiation
  Strategy" below; outcome capture (`POST /api/negotiations/outcomes`,
  feature-03's us-02) remains future work no task has picked up yet.
- **Incidental fix, required for this task's own `dotnet build` to succeed
  at all**: `Contigo.Api.QuoteExtractionPipeline.ProcessAsync` (touched by
  both task E05/F01/US01/T02 and task E05/F01/US02/T01 in parallel
  wave-spec phases) had a duplicate local-variable declaration
  (`normalizationOutcome` declared twice, `CS0128`) and two stray, dangling
  duplicate lines (inside the method's own `return` statement and inside
  `QuoteProcessingSummary`'s record declaration) — each sibling task had
  appended its own new field/parameter without reconciling with the other's
  identical-shaped addition, so the whole `Contigo.Api` project (and every
  test depending on it — `Contigo.Api.Tests`, `Contigo.IntegrationTests`)
  could not compile. Renamed the two outcomes to their own distinct names
  (`lineNormalizationOutcome`/`skuNormalizationOutcome`) and removed the
  duplicate lines; no behavioural change to either sibling task's own
  already-landed logic. The `POST /api/quotes` HTTP-surface-table row above
  had the identical duplicate-row shape (two rows, each missing the other's
  fields) — consolidated into the one row above for the same reason.
- **Task E05/F04/US01/T01 (r4-integration) fixes**: `MarketAssessmentService`
  never opened its own `ITenantContext.BeginScope` — unlike every other
  tenant-scoped application service in this codebase — and neither did
  `Contigo.Api.QuotesEndpointExtensions.GetAssessmentAsync` upstream of it.
  Against a real, RLS-enforced, non-superuser connection (every deployed
  environment), `GET /api/quotes/{id}/assessment` would 404 for every real
  quote, always — undetected because `MarketAssessmentServiceTests` calls
  this method from inside a test-provided scope, and no integration test had
  yet driven this endpoint over real HTTP against an unprivileged Postgres
  role. Fixed the same way every sibling service already does it (see that
  type's own doc comment) — no caller-side change required. Separately,
  `GetAssessmentAsync` never actually serialized `quantity` on the response
  despite `LineMarketAssessment.Quantity` existing exactly to be echoed here
  and despite this very HTTP-surface-table row documenting it since task
  E05/F02/US01/T02 — also fixed, so the wire response now matches its own
  already-published contract. Both surfaced by, and proved fixed by,
  `Contigo.IntegrationTests.R4EndToEndTests`/`R4CrossTenantIsolationTests` —
  see "R4 demo smoke test" below.

## Negotiation Strategy — opening target/range/walk-away + levers

Task E05/F03/US01/T01 (negotiation-strategy; parent story
us-01-negotiation-strategy AC-1 "Generate opening target, acceptable range,
walk-away threshold, levers, rationale", AC-3 "Arithmetic (target/saving) is
deterministic; only language is LLM") closes the gap the "Market Assessment"
section above used to name ("Negotiation ... remains future work no task has
picked up yet").

- **`Contigo.Quotes.Application.Strategy.NegotiationStrategyCalculator`**
  is a pure, synchronous calculator (no database/HTTP/LLM call) that turns
  an already-computed `LineMarketAssessment.TargetSaving` (task
  E05/F02/US01/T02) into `LineNegotiationStrategy.{OpeningTarget,
  AcceptableRangeLow/High, WalkAwayThreshold}`: the acceptable range echoes
  `RecommendedTargetLow/High` verbatim (spec §12.1's "Acceptable target
  range" row is §11.2's own "Recommended target" row carried forward, not a
  second computation), opening target steps one range-width below the low
  end (floored at zero) and walk-away steps one range-width above the high
  end, clamped to the line's own current `UnitPrice` (never recommend
  escalating past what is already quoted — the same clamp
  `TargetSavingCalculator` already applies to `RecommendedTargetHigh`).
  Never fabricates: no usable target range, or no current `UnitPrice`,
  returns every numeric field `null` plus an empty lever list and a named
  reason (Appendix C rule 10) — the same honest-abstain shape
  `TargetSavingCalculator.Compute` already established.
- **Levers are always the full, fixed, spec §12.1-named set of seven**
  (`NegotiationLeverType`: `Volume`, `Term`, `Utilization`, `Alternatives`,
  `QuarterEnd`, `Bundle`, `PaymentTerms`) — never a variable-length subset —
  so a caller always sees the complete playbook. `Volume`/`Term`/`Bundle`
  ground themselves in this line/quote's own recorded data when it exists
  (`QuoteLine.Quantity`/`Term`, and how many `QuoteLine` rows share this
  line's own quote); `QuarterEnd` is date-derived (within 14 days of a
  calendar quarter-end, evaluated as of the caller's own `IClock`-derived
  "today", never a historical quote date); `Utilization`/`Alternatives`/
  `PaymentTerms` have no source field anywhere in this module's schema
  today, so their rationale says so honestly rather than inventing a
  this-quote-specific fact.
- **Deterministic language, not yet an AI Gateway `answer`-role call**: AC-3's
  "only language is LLM" is honoured by keeping every number in the pure
  calculator above; the per-lever `Rationale` text is V1 deterministic
  language, the same "`Explanation` is a computed string, never a model
  call" convention `TargetSavingCalculator`/`MarketAssessmentCalculator`
  already follow. `Contigo.ArchitectureTests.DependencyDirectionTests`'
  allowed-reference set for `Contigo.Quotes` is exactly `[SharedKernel,
  Benchmark]` (see "Dependency direction" below) — unchanged by this task.
  A future task wiring the `answer` role would do it the same way
  `Contigo.Api.QuoteExtractionPipeline` already does for the `extract`
  role: from the composition root, feeding this calculator's own facts in
  as evidence, never asking the model to invent them.
  `Contigo.AiGateway.Fixtures.FixtureAiGateway.AnswerAsync` would today only
  echo those facts back verbatim (no live grounded-generation model exists
  yet), so deferring that wiring loses no real capability now. Evidence
  *citations* per lever (AC-2, Appendix C rule 2) were task-01's own,
  separate, deferred scope (strategy-evidence) — closed below by task
  E05/F03/US01/T02.
- **Structured evidence per lever (task E05/F03/US01/T02, strategy-evidence;
  AC-2 "Rationale cites explicit evidence per lever", Appendix C rule 2
  "never show a consequential... fact without source evidence and
  confidence metadata")**: `NegotiationLever` gained an `Evidence` field —
  `IReadOnlyList<Contigo.Quotes.Application.Strategy.NegotiationLeverEvidence>`,
  each a `FieldName`/`Value`/`SourceSpan`/`SourcePage`/`Confidence` tuple.
  Mirrors `Contigo.Documents.Contracts.Domain.ExtractionEvidence`'s own
  "which field, what value, from where, how confident" addressing scheme,
  kept as its own `Contigo.Quotes`-local record rather than
  `Contigo.AiGateway.Contracts.AiCitation`/`AiEvidenceSnippet` (those are
  document-citation-shaped — `DocumentId`/`Page`/`Section` — for RAG
  answers over unstructured text, and `Contigo.Quotes`' own
  allowed-reference set, `[SharedKernel, Benchmark]`, cannot reach
  `Contigo.AiGateway` anyway). `Volume`/`Term` cite `QuoteLine.Quantity`/
  `Unit`/`Term` carrying this same line's own extraction `SourceSpan`/
  `SourcePage`/`Confidence` (fields the AI Gateway `extract` role
  originally proposed for the row — a `QuoteLine` row is one extraction
  event covering the whole row); `QuoteLine.NormalizedTermMonths` cites
  alongside `Term` but with no provenance of its own, since it is derived
  deterministically from `Term` (Appendix C rule 6), not a second,
  independently-extracted fact. `Bundle`/`QuarterEnd` cite the sibling-line
  count / negotiation-timing as-of date — always populated (never empty,
  unlike `Volume`/`Term`), with no span/page/confidence, since neither is a
  `QuoteLine` field or a document extraction. `Utilization`/`Alternatives`/
  `PaymentTerms` stay evidence-empty, the same "no source field exists"
  reason their `Rationale` already gives (Appendix C rule 10 — never
  fabricate a citation for a fact that is not actually there). The cited
  `Value` always renders exactly as `Rationale` itself renders it, so the
  structured citation and the prose can never silently disagree.
- **`Contigo.Quotes.Application.Strategy.NegotiationStrategyService`**
  composes on top of `MarketAssessmentService.AssessAsync` (reused, not
  re-derived) plus one extra `QuoteLine` read (for `Term`/
  `NormalizedTermMonths`/`Unit`, which `LineMarketAssessment` does not echo)
  and returns one `LineNegotiationStrategy` per line — the same per-line,
  no-quote-level-rollup shape `QuoteMarketAssessment` already established,
  and the same "computed fresh on every call, nothing persisted" posture
  `MarketAssessmentService` already takes. Not yet wired to an
  `AddQuotesModule`-registered HTTP endpoint: parent story
  us-01-negotiation-strategy's own acceptance criteria name no `GET
  /api/quotes/{id}/...` route (unlike us-01-market-assessment's AC-3), so
  none was added — `AddQuotesModule` registers the service so a future
  task/feature-04 (r4-integration) can call it. **Task E05/F04/US01/T01
  (r4-integration) is that caller**: `Contigo.IntegrationTests.R4EndToEndTests`
  resolves this service directly from the real host's own container (the
  same "no dedicated route exists yet" convention `R2EndToEndTests`/
  `R3EndToEndTests` already established), still with no dedicated HTTP route
  of its own — that remains open, un-picked-up scope. That same task also
  gave this service its own `ITenantContext.BeginScope` (it never opened one
  either, for the identical reason and with the identical real-HTTP
  consequence the "Market Assessment" section above now documents for
  `MarketAssessmentService`) — see this type's own doc comment.
- Proved directly by `Contigo.Quotes.Tests.NegotiationStrategyCalculatorTests`
  (pure, no database — range/walk-away arithmetic, all seven levers, every
  honest-abstain branch, determinism) and end to end by
  `Contigo.Quotes.Tests.NegotiationStrategyServiceTests` against a real
  Postgres+RLS database and the real `FixtureBenchmarkAdapter`, reusing
  `MarketAssessmentServiceTests`' own Salesforce/Sales-Cloud-Enterprise
  fixture comparable (P25/P50/P75 = 1500/1800/2100 per seat/year) so both
  tests agree on what the numbers mean. Task E05/F03/US01/T02
  (strategy-evidence) extends the same calculator test class with AC-2's
  own coverage — per-lever evidence content, `SourceSpan`/`SourcePage`/
  `Confidence` pass-through for `Volume`/`Term`, the no-provenance case for
  `NormalizedTermMonths`/`Bundle`/`QuarterEnd`, the honest-empty case for
  `Utilization`/`Alternatives`/`PaymentTerms`, and a citation-vs-`Rationale`
  cross-check — plus one end-to-end assertion in
  `NegotiationStrategyServiceTests` proving `Evidence` also comes back
  populated through the real database round trip, not just the pure
  calculator. The determinism test itself now asserts each lever's
  `LeverType`/`Rationale`/`Evidence` as its own sequence rather than via
  `NegotiationLever`'s own record-generated `Equals`: `Evidence` is an
  `IReadOnlyList<T>`, which has no structural equality of its own, so two
  independently-built lever lists that are otherwise identical would
  compare unequal two levels deep inside a containing record's `Equals`.

## Negotiation Outcome — capture + append-only + audit

Task E05/F03/US02/T01 (negotiation-outcome; parent story
us-02-outcome-capture AC-1 "records original/target/final/saving/discount/
duration/levers", AC-3 "Outcome is versioned + audit-tracked") closes spec
§12.2 ("Negotiation outcome capture") — the "Negotiation Strategy" section
above recommends a target; this is where what actually happened gets
recorded as permissioned proprietary learning data (spec §12.3's data
flywheel).

- **`POST /api/negotiations/outcomes`** (module-map.md "Quotes | ... |
  /api/quotes, /api/negotiations/outcomes"; `X-Tenant-Id` header; plain
  JSON body, unlike `POST /api/quotes`'s multipart upload) —
  `{ quoteId, originalQuoteTotal, targetPrice?, finalPrice,
  negotiationDurationDays, leversUsed: [<NegotiationLeverType name>, ...],
  savingsOpportunityId? }` (`savingsOpportunityId` added by task
  E05/F03/US02/T02, outcome-propagation — see the dedicated bullet below).
  404 (`Contigo.Quotes.Application.Outcome.NegotiationOutcomeService
  .QuoteNotFoundError`) when `quoteId` does not name a quote for this
  tenant; 400 for every validation failure (non-positive
  `originalQuoteTotal`/`finalPrice`, a negative `targetPrice`, a negative
  `negotiationDurationDays`, an empty or unrecognized `leversUsed`).
  Response `{ id, quoteId, originalQuoteTotal, targetPrice, finalPrice,
  realizedSaving, discountPercent, negotiationDurationDays, leversUsed,
  capturedAt, savingsOpportunityId, savingsPropagated,
  savingsPropagationError }` — the last three are `null`/absent-equivalent
  together whenever the caller supplied no `savingsOpportunityId`;
  otherwise `savingsPropagated` is always `true`/`false` and
  `savingsPropagationError` is set only when it is `false` — never a
  distinct HTTP status for a propagation failure (see the propagation
  bullet below).
- **`Contigo.Quotes.Application.Outcome.NegotiationOutcomeCalculator`** is a
  pure, synchronous calculator (no database/HTTP/LLM call, Appendix C rule
  6) — `realizedSaving = originalQuoteTotal - finalPrice`,
  `discountPercent = realizedSaving / originalQuoteTotal * 100`. Never
  clamped at zero: a `finalPrice` above `originalQuoteTotal` is an honest
  negative saving, not a fabricated floor (Appendix C rule 10). Reproduces
  spec §12.2's own worked example exactly (520k / 435k -> 85k saved,
  ~16.3%).
- **`targetPrice` is nullable** — echoes `LineNegotiationStrategy
  .OpeningTarget`'s own nullability for the identical reason (no usable
  target range was ever available, e.g. insufficient benchmark data):
  outcome capture is never blocked on a fact this module honestly never
  had (Appendix C rule 9 "from day one").
- **`leversUsed` reuses `NegotiationStrategyCalculator`'s own closed
  `NegotiationLeverType` vocabulary** (seven canonical levers), not free
  text — parsed case-insensitively from the wire string list by
  `NegotiationOutcomeService.CaptureAsync` itself (this codebase has no
  global `JsonStringEnumConverter`; every enum-accepting endpoint parses
  its own wire strings, e.g. `SavingsOpportunityPatchRequest.Status`), so
  which levers actually work stays a queryable, aggregable dimension for
  spec §12.3's "better recommendation" loop, not prose a later task would
  have to re-parse. At least one entry is required.
- **"Versioned" (AC-3) means append-only, never a `PATCH`/update** — spec
  Appendix A names only `POST` for this resource.
  `NegotiationOutcomeService.CaptureAsync` only ever `Add`s a new
  `Contigo.Quotes.Domain.NegotiationOutcome` row; a second capture for the
  same `quoteId` (a renegotiation, or a correction to an earlier capture)
  is simply another row, ordered by `capturedAt` — the same "never
  destructively overwrite" convention (Appendix C rule 5)
  `Contigo.Savings.Domain.RealizedSavings` already establishes for the
  identical App C #5/#9 pairing on a sibling "capture a final,
  consequential figure" entity.
- **Audit-tracked (AC-3)**: writes one `IAuditWriter` entry
  (`negotiation_outcome.captured`) per successful capture, still inside
  the same call's tenant scope — same placement as `QuoteUploadService
  .UploadAsync`'s own "persist -> audit" write.
- **Realized-savings propagation (task E05/F03/US02/T02,
  outcome-propagation; parent story AC-2 "Realized savings surface on the
  savings dashboard (cross-wave)")**: when the caller supplies
  `savingsOpportunityId`, `Contigo.Api.NegotiationsEndpointExtensions`
  also calls `Contigo.Api.NegotiationOutcomePropagationService
  .PropagateAsync` right after the capture itself is already durable,
  still in the same request. That type is `internal`, host-composition-
  root-only wiring (ADR-002: `Contigo.Quotes` and `Contigo.Savings` cannot
  see each other; only `Contigo.Api` may reference both — the same
  treatment `QuoteExtractionPipeline` already gets, see "Dependency
  direction" below), and it reuses the exact same, already-audited write
  path a human `PATCH /api/savings/{id}` call already uses
  (`SavingsOpportunityService.UpdateAsync` with `realizedAmount` set — see
  "Savings Intelligence — trackable SavingsOpportunity" above): that call
  finalizes the opportunity's own `status` as `Realized` and inserts a new
  `RealizedSavings` row, in the opportunity's own currency. This service
  then writes one more, distinct `IAuditWriter` entry
  (`negotiation_outcome.propagated`) recording the link between the two
  aggregate ids — the one fact neither the `negotiation_outcome.captured`
  nor the `savings_opportunity.realized` entry captures alone. **Never
  fails an already-durable capture**: an unknown `savingsOpportunityId`
  (or any other `UpdateAsync` validation failure) is reported honestly as
  `savingsPropagated: false` + `savingsPropagationError` on the same 201
  response, never a 4xx/5xx — the outcome capture itself already succeeded
  and is already audit-tracked (AC-3) before propagation is even
  attempted. No currency reconciliation: `NegotiationOutcome` carries no
  currency of its own, and `UpdateAsync`'s own `realizedAmount` parameter
  has never reconciled a caller-supplied figure against another currency
  either — the same, already-accepted trust assumption an automated
  caller now shares with a human PATCHing directly. `GET
  /api/savings/kpis`'s own `savingsRealized` bucket does **not** yet read
  this `RealizedSavings` row (the honest gap the "Savings Intelligence —
  trackable SavingsOpportunity" section above already names, task
  E04/F02/US02/T02's own follow-up, not this task's) — "surfaces on the
  savings dashboard" (AC-2) today means the opportunity's own `status` and
  realized-value row are real and queryable, not yet that every KPI number
  reflects them.
- Proved directly by `Contigo.Quotes.Tests.NegotiationOutcomeCalculatorTests`
  (pure, no database — the spec §12.2 worked example, the negative-saving
  honesty case, determinism) and end to end by
  `Contigo.Quotes.Tests.NegotiationOutcomeServiceTests` against a real
  Postgres+RLS database (persistence, the audit entry, the "second capture
  does not overwrite the first" append-only proof, quote-not-found/
  cross-tenant/every validation failure, and — task E05/F03/US02/T02 —
  that a caller-supplied `savingsOpportunityId` persists unvalidated) plus
  `Contigo.Api.Tests.NegotiationsEndpointTests` for the host-level
  tenant-header guard clause. Realized-savings propagation itself (task
  E05/F03/US02/T02) is proved end to end by
  `Contigo.IntegrationTests.NegotiationOutcomePropagationEndToEndTests`
  against the real, composed `Contigo.Api` host and a real, migrated
  Postgres+RLS database spanning both `Contigo.Quotes` and
  `Contigo.Savings` — a real `SavingsOpportunity` realized (`status`,
  the `RealizedSavings` row, the `negotiation_outcome.propagated` audit
  entry, and all three response fields) and an unknown
  `savingsOpportunityId` (the outcome still persists and the call still
  returns 201; `savingsPropagated: false` + `savingsPropagationError`
  reported honestly instead of an HTTP failure).

## Insights — criticality score, priced-line negotiation, strategy pack

Task E13/F07/US01/T01 (insights-calculators; ADR-024; parent story
us-01-insights) fills in `Contigo.Insights` (scaffolded by
E13/F01/US01/T01) with three pure calculators, fed by DTOs only — the
same determinism convention (Appendix C rule 6) every calculator in this
backend already follows:

- `Criticality.CriticalityScoreCalculator.Calculate` — product spec §12.1/
  R-PORT-01's deterministic, explainable 0-100 portfolio-criticality
  score: five weighted components (renewal urgency, risk severity, spend
  weight, savings potential, open critical facts), each its own `Score`/
  `Weight`/`Explanation`, summing to the total (AC-1). Weights are
  `Contigo.Insights.InsightsOptions` (config section
  `Insights:Criticality`, council default 0.30/0.20/0.20/0.20/0.10,
  validated to sum to 1.0 at construction). A contract whose tracked
  critical facts (recorded risks + priced lines with a unit price) are
  all below the 0.8 confidence threshold is flagged "validate first" in
  its own component explanation and its score is raised, not hidden
  (AC-2).
- `Negotiation.PricedLineNegotiationCalculator.Compute` — generalizes
  `Contigo.Quotes.Application.Strategy.NegotiationStrategyCalculator` from
  a quote line to any `Contigo.Benchmark.Contracts.PricedLine` (a contract
  line item included), producing the same opening target/acceptable
  range/walk-away threshold plus the same seven canonical levers (AC-3).
  Unlike the Quotes calculator, levers are never empty: a priced line with
  no benchmark match still gets the levers that do not need one (volume,
  term, quarter-end, bundle), with "insufficient market data" stated for
  the numeric targets (AC-4) — mirrors R-CMP-01 AC-2's identical rule for
  the benchmark-comparison case.
- `Strategy.StrategyPackBuilder.Build` — the renewal-strategy pack for one
  contract, in the council-decided section order: **When you must move**
  (dates, a passed deadline stated as passed, never hidden) → **Where you
  can push** (levers across every priced line) → **Targets** (opening/
  range/walk-away per priced line, "insufficient market data" when no
  band exists) → **Next steps** (the four tracker steps verbatim from
  `contigo-v2/app.jsx`'s own `stepDefs`, mirroring the Contract 360
  tracker) — plus `openWeakFacts` and a citation key for every number
  (`fact:<contractId>:<field>` / `market:<recordId>` / `calc:<name>`,
  `Contigo.Insights.Contracts.InsightsCitationKeys`).

**Where the shared `PricedLine` input lives, and why**: R-STR-02
generalizes `NegotiationStrategyCalculator` to a shared priced-line input.
`Contigo.Insights`' own allow-list is `[SharedKernel, Benchmark]` — the
same one `Contigo.Quotes` already has — so neither module can reference
the other (`Contigo.ArchitectureTests.DependencyDirectionTests`); the one
project both already see is `Contigo.Benchmark`. `PricedLine` therefore
lives at `Contigo.Benchmark.Contracts.PricedLine`, next to
`BenchmarkDistribution` — the only new file this task adds to
`Contigo.Benchmark`. The opening-target/walk-away-threshold "step an
already-known range" arithmetic both calculators must reproduce
bit-for-bit also lives there, as
`Contigo.Benchmark.Contracts.PricedLineNegotiationMath.StepRange` — the
smallest possible shared surface: `NegotiationStrategyCalculator.Compute`
now calls it too (its own public signature, levers and abstain conditions
are otherwise unchanged — every existing
`Contigo.Quotes.Tests.NegotiationStrategyCalculatorTests` assertion still
holds, decimal arithmetic being exact). The earlier "raw distribution ->
recommended range" step stays each calculator's own independent
arithmetic (`PricedLineNegotiationCalculator` mirrors, rather than calls,
`Contigo.Quotes.Application.Assessment.TargetSavingCalculator`'s formula)
because `NegotiationStrategyCalculator.Compute`'s own signature still
takes a pre-computed `LineTargetSaving`, not a raw
`BenchmarkDistribution`, and no task has changed that.

`Contigo.Api.InsightsEndpointExtensions` composes `GET
/api/insights/criticality` and `GET /api/contracts/{id}/strategy` from
`PortfolioQueryService`/`Contract360QueryService` (Documents/Contracts),
`RenewalEngine`/`PriorityScoreCalculator` (Renewals) and
`SavingsOpportunityService` (Savings) — the one project allowed to
reference every module. Task E13/F06/US01/T01 (ask-engine) maps both routes
in `Program.cs` (see the HTTP surface table above); every
composition/mapping method on that class is also `public static` so it can
be (and is) unit-tested directly with hand-built fakes from
`Contigo.Insights.Tests` — no database, no `WebApplicationFactory` — which
is why that test project also references `Contigo.Api` (a test-project
reference is not constrained by `DependencyDirectionTests`, which only
inspects `src/` projects). `AskCopilotService`'s own `PortfolioStrategy`/
`RenewalStrategy` intents narrate the identical `CriticalityScoreCalculator`/
`StrategyPackBuilder` output these two HTTP routes return — one calculation,
reachable both ways. Per-contract benchmark matching is honestly not
wired yet: a `BenchmarkQuery` needs a supplier name and geography, and
`Contract` carries neither (only a bare `SupplierId` guid) — the same gap
`Contract360Result.Benchmark` already has — so `PricedLine.Benchmark` is
always `null` through this composition until a follow-up task resolves a
real supplier name (Suppliers/Products) and geography onto the contract.

## R4 demo smoke test

The automated proof of task E05/F04/US01/T01 (r4-integration) is `dotnet test` —
`Contigo.IntegrationTests.R4EndToEndTests` (AC-1 "Upload quote -> line items -> benchmark match ->
market assessment -> target range -> negotiation strategy", AC-2 "User can correct SKU matching
before accepting assessment", AC-3 "Record final outcome -> realized savings tracked" — the whole
Quote Check Day-1 chain, driven against one real, uploaded quote through the real host, for the
first time; every earlier Quote Check task only proved its own segment in isolation) and
`R4CrossTenantIsolationTests` (the same AC-1/AC-3 surface — `GET /api/quotes/{id}/assessment`,
`POST /api/negotiations/outcomes` — proven isolated across two tenants, the same "drive the whole
path across two tenants through the real host" value-add `R1CrossTenantIsolationTests`/
`R2CrossTenantIsolationTests`/`R3CrossTenantIsolationTests` already established). Run just these:

```bash
cd backend
dotnet test Contigo.slnx --configuration Release --filter "FullyQualifiedName~R4"
```

Running this test end to end (rather than each Quote Check task's own narrower, per-segment test)
surfaced two real gaps — see "Market Assessment" and "Negotiation Strategy" above for the full
account: `MarketAssessmentService`/`NegotiationStrategyService` never opened their own
`ITenantContext.BeginScope`, so `GET /api/quotes/{id}/assessment` would 404 for every real quote
against a real, unprivileged-role Postgres connection (every deployed environment); and that same
endpoint never actually serialized `LineMarketAssessment.Quantity` as `quantity`, despite
backend/README.md's own HTTP surface table documenting it since task E05/F02/US01/T02. Both are
fixed; both are now covered by this task's own tests.

To manually smoke-test the same path against a running `dev`/`demo` deployment:

```bash
API=https://<api-host>
TENANT=$(curl -s -X POST "$API/api/workspaces" -H 'Content-Type: application/json' \
  -d '{"name":"Smoke Test Co"}' | jq -r .id)

QUOTE=$(curl -s -X POST "$API/api/quotes" -H "X-Tenant-Id: $TENANT" \
  -F "file=@quote.pdf;type=application/pdf" \
  -F "supplier=Salesforce" -F "currency=USD" -F "geography=US" -F "purchaseDate=2026-07-01" \
  | jq -r .id)

# processingStatus/lineItemCount/unmatchedSkuCount reflect QuoteExtractionPipeline's own run
# (hybrid parse -> extract -> normalize -> SKU-match) -- POST /api/quotes runs it synchronously.
curl -s "$API/api/quotes/$QUOTE/assessment" -H "X-Tenant-Id: $TENANT" | jq .
```

Honest caveats: `IAiGateway` still binds to `FixtureAiGateway` (no live Foundry endpoint exists yet,
ADR-004), whose `ExtractAsync` always returns an empty `{}` — a real `demo` upload lands zero line
items, so nothing on it will ever match a benchmark. This smoke path proves the *pipeline/endpoint
wiring* end to end (upload responds, the assessment route resolves and returns 200 for a real,
owned quote); `R4EndToEndTests` proves the actual matching/target-saving/negotiation-strategy/
outcome-capture arithmetic against a scripted gateway that returns real, schema-shaped facts, the
same division of labour "R1 demo smoke test" above already documents for contracts. Negotiation
strategy generation (`NegotiationStrategyService.GenerateAsync`) and identifying a new
`SavingsOpportunity` (`SavingsOpportunityService.CreateAsync`) both still have no public HTTP route
— see "Negotiation Strategy"/"Savings Intelligence — trackable SavingsOpportunity" above for why —
so neither is curl-able yet; `R4EndToEndTests` proves both directly against the real host's own
container instead, the same "no dedicated route exists yet" convention this backend has used since
R2.

## Containers and CI

`.github/workflows/backend.yml` (path-filtered to `backend/**`):

1. `dotnet restore / build / test` on `Contigo.slnx` (required status check).
2. On merge to `main` (or `workflow_call` for demo): Azure login via OIDC
   (`contigo-sp-<env>`), `az acr build` of
   `src/Contigo.Api/Dockerfile` and `src/Contigo.Worker/Dockerfile`, then
   `az containerapp update` of `ca-contigo-<env>-api` / `-worker`.

Images are tagged with `github.sha`. Container Apps listen on **8080**
(`ASPNETCORE_URLS=http://+:8080`). Deployed connection strings are
environment variables (`ConnectionStrings__IdentityWorkspace`,
`DocumentsContracts`, `Audit`, `Renewals`, `Savings`, `Quotes`, `Storage`) — never
committed.

Image pull uses this environment's workload identity (`AcrPull` on
`modules/acr`, `registry {}` on `modules/containerapps`). Confirm the
HCP VCS apply on `contigo-<env>` before the first `az containerapp update`
to that registry, or the revision fails with ACR `UNAUTHORIZED`.

## Dependency direction (ADR-002)

Allowed Contigo project references (enforced by
`tests/Contigo.ArchitectureTests`):

| Module | May reference |
|--------|----------------|
| Domain modules | `SharedKernel` only, plus `AiGateway` (Documents, Chat, Market) or `Benchmark` (Renewals, Savings, Quotes, Insights); `Market` is the one module allowed both `AiGateway` and `Benchmark` |
| `AiGateway` / `Benchmark` implementations | provider SDKs — when they exist; domain modules see the interface only |
| `Contigo.Api` / `Contigo.Worker` | all modules (composition roots). Azure Blob SDK is host-only |

Do not add a domain → domain or domain → Azure SDK project/package
reference to make a task compile. Put the adapter in the host or behind
the gateway/service project. When two domain modules with no shared
reference need the identical shared input/arithmetic (`Contigo.Quotes` and
`Contigo.Insights` both need a priced-line negotiation calculation, task
E13/F07/US01/T01), put the shared DTO/arithmetic in a module both already
allow-list — see "Insights" above for the worked example
(`Contigo.Benchmark.Contracts.PricedLine` / `PricedLineNegotiationMath`).

## Ask Contigo V2 — operator jobs, golden set and acceptance (task E13/F11/US01/T01)

Everything a new engineer needs to run the V2 flows end to end against a
deployed environment. The screen-by-screen acceptance list lives in
[`../docs/ask-v2-acceptance.md`](../docs/ask-v2-acceptance.md) (A1–A14, one
exact command or click-path and one observable pass condition per row).

### Order of operations on a fresh environment

| # | Step | How |
|---|------|-----|
| 1 | Deploy + apply schema (ADR-021) | merge to `main` (`dev`), or `git tag demo-v<N> && git push` (`demo`) |
| 2 | Seed the Day-1 fixture rows | Actions → **seed-demo-fixture** (`target_environment`) |
| 3 | Seed the shared market corpus | Actions → **seed-market-intelligence** (`target_environment`) |
| 4 | Re-OCR / re-embed a tenant, back-fill suppliers | Actions → **reprocess-tenant-documents** (`target_environment`, `tenant_id`) |
| 5 | Walk A1–A14 | `docs/ask-v2-acceptance.md`, plus `web/e2e/v2.spec.ts` |

### `seed-market-intelligence` — the market feed ingestion job (R-MKT-03)

`.github/workflows/seed-market-intelligence.yml`, `workflow_dispatch` /
`workflow_call`, input `target_environment` (`dev` | `demo`). Same OIDC login,
resource-group resolution and `postgres-connection` Key Vault fetch as
`seed-demo-fixture.yml`; the `demo` GitHub Environment's required reviewers
gate it exactly like a deploy.

It runs this host's own one-shot operator command
(`Contigo.Worker/Commands/IngestMarketCommand.cs`) on the runner, against that
environment's database:

```bash
ConnectionStrings__DocumentsContracts=<npgsql> \
ConnectionStrings__Audit=<npgsql> \
ConnectionStrings__Renewals=<npgsql> \
ConnectionStrings__Market=<npgsql> \
  dotnet run --project backend/src/Contigo.Worker -- \
    ingest-market --feed backend/fixtures/market-intelligence.mock.json
# Ingested feed 'mock-2026.09.0': 65 inserted, 0 updated, 0 unchanged.
```

The first three connection strings are what `Contigo.Worker/Program.cs`
fail-fasts on when it builds the host; only the fourth is what the command
itself needs. All four are the same database (ADR-003: separate schemas, one
server). `--feed` is an informational label — the mock provider reads its
fixture from an embedded resource, not from that path (see
`IngestMarketCommand`'s own doc comment).

The job then **runs the command a second time and fails unless it reports
`0 inserted, 0 updated`** (R-MKT-03 AC-1, "re-running the job with the same file
changes nothing"), and verifies that `market_record` has ≥ 60 rows (R-MKT-02),
that `market_embedding` is non-empty, and that neither table has a `tenant_id`
column (R-MKT-03 AC-2 / ADR-011's epic-13 amendment — the market corpus is
shared and read-only for every tenant, and would be a defect if it were
tenant-scoped).

`AiGateway__Endpoint` is deliberately **not** set on the job: with it unset the
gateway DI swap keeps the fixture `embed` role, which is deterministic and
free, and the numbers the job seeds (`market_record`) never come from a model
anyway. Set `AiGateway__Endpoint` / `AiGateway__ProjectName` on the job to embed
the market notes with Foundry instead.

### `reprocess-tenant-documents` — re-OCR, re-embed, back-fill (R-DOC-07, R-SUP-03)

`.github/workflows/reprocess-tenant-documents.yml`, `workflow_dispatch` /
`workflow_call`, inputs `target_environment` and `tenant_id`.

**It calls the API, not a `backend/scripts/` helper**, and this is deliberate.
The parent story allowed either; the API wins because (a) the API host is
reachable from a GitHub runner — `web.yml` already resolves that same
`ca-contigo-<env>-api` ingress FQDN and bakes it into the SPA's `config.json`,
so the "API host is not reachable from CI" branch simply does not apply, and
(b) `POST /api/documents/{id}/reprocess` *is* the R-DOC-07 seam: it re-runs the
real `DocumentProcessingPipeline` (load → hybrid parse/OCR → page-aware
re-embed → supplier resolution) and writes the `document.reprocessed` audit
row. A Python helper would have to re-implement that pipeline against the
database, would drift from it on the first pipeline change, and could not write
the same audit trail. There is therefore **no** `backend/scripts/reprocess_tenant.py`.

What the job does, in order:

1. Resolves the API ingress FQDN and health-checks it.
2. Detects whether that environment runs a live Foundry gateway (reads
   `AiGateway__Endpoint` off the Container App) — this decides how strict the
   OCR check in step 5 can be.
3. Pages through `GET /api/documents?page=&pageSize=100` for the tenant (no
   `status` filter — the operator job reprocesses *every* document, not only
   the ones needing attention).
4. `POST /api/documents/{id}/reprocess` per document, attempting all of them and
   failing at the end if any did not return `200`, so one bad document never
   hides the rest. It prints `pagesParsed` / `chunksIndexed` per document.
5. Verifies by SQL, with `SET app.tenant_id` (RLS stays on — the same session
   claim `TenantRlsConnectionInterceptor` sets, never a disabled policy):
   - **`left(chunk_text, 4) = '%PDF'` must be 0** — R-DOC-07 AC-1. This is a
     hard failure: an embedding that is raw bytes means Ask would cite bytes as
     evidence.
   - The `[fixture-ocr: …]` placeholder count is a **failure** when the
     environment has `AiGateway__Endpoint` set (the `ocr` role fell back), and a
     **warning** otherwise (expected fixture behaviour).
6. Reports the tenant's contracts that still have `supplier_id IS NULL`
   (R-SUP-03). A *report*, not a gate: SQL cannot know whether a given document
   actually names a supplier, so failing here would fail honestly supplier-less
   contracts (an unsigned SOW, a price list). Name the supplier through the
   review screen (`/documents?review=<documentId>`) for each one that should
   have had one.

**Role headers.** Reprocess is Admin-only and no host authentication is wired
yet (ADR-022), so the job sends **both** `X-Workspace-Role: Admin` and
`X-Role: Admin` — two spellings are in flight in this codebase
(`CapabilitiesEndpointExtensions` parses `X-Role`; the OpenAPI's
`reprocessDocument` / `deleteDocument` descriptions name `X-Workspace-Role`) and
neither is a declared parameter of the operation. A `403` is surfaced as a named
error, never a silent skip. Both headers disappear with `X-Tenant-Id` when
ADR-010's API JWT lands.

### AI golden set (`Contigo.AiEval`) — how it runs and how to filter it

`backend/tests/Contigo.AiEval/Contigo.AiEval.csproj` is a member of
`Contigo.slnx`, so **`.github/workflows/backend.yml` already runs it** through
its existing `dotnet test Contigo.slnx` step. There is no `--filter` step in
CI and none is wanted: a numeric-guard intervention or a kind mismatch fails
the `build + test` job like any other test failure, which is the required
status check on `main` (ADR-014).

Locally, the filters an engineer actually needs:

```bash
# The whole solution, golden set included — what CI runs.
cd backend && dotnet test Contigo.slnx --configuration Release

# Only the golden set, by project (works whatever traits the suite carries).
dotnet test backend/tests/Contigo.AiEval/Contigo.AiEval.csproj

# Only the golden set, by trait, from the solution — the suite marks its cases
# [Trait("Category","AiEval")] (task E13/F06/US01/T02).
dotnet test backend/Contigo.slnx --filter "Category=AiEval"

# Everything except the golden set — the fast inner loop.
dotnet test backend/Contigo.slnx --filter "Category!=AiEval"

# By fully-qualified name, if the trait is not there yet.
dotnet test backend/Contigo.slnx --filter "FullyQualifiedName~Contigo.AiEval"
```

The set runs against the **fixture** gateway so it is reproducible and free;
the on-demand Foundry run is manual (`AiEval__UseFoundry=true` with
`AiGateway__Endpoint` set) and never part of CI.

### Known gap that blocks the first V2 promotion

`Contigo.Api/Program.cs` fail-fasts on `ConnectionStrings:Suppliers` (task
E13/F06/US01/T01 wired `AddSuppliersProductsModule`), but
`infra/modules/containerapps/main.tf` injects `IdentityWorkspace`,
`DocumentsContracts`, `Audit`, `Renewals`, `Savings`, `Quotes`, `Chat` and
`Storage` — **not** `Suppliers`. The deployed API will not boot until that env
block is added (same `pg-cs` secret as its neighbours). Recorded in
`infra/README.md` and in `docs/ask-v2-acceptance.md`'s "Known gaps" table; it is
an `infra/` change, outside this task's file scope.
