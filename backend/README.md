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
    Contigo.Documents.Contracts/ # upload, metadata, hybrid OCR pre-pass, staged extraction, contract correction (live)
    Contigo.Audit/               # append-only audit events (live)
    Contigo.AiGateway/           # IAiGateway (classify/extract/embed/answer/ocr) + FixtureAiGateway + LoggingAiGateway decorator, wired via DI — no Foundry/Document Intelligence SDK yet
    Contigo.Documents.Contracts/ # upload, metadata, extraction jobs, staged extraction pipeline,
                                  # portfolio list, Contract 360, contract correction (live)
    Contigo.Audit/               # append-only audit events (live)
    Contigo.AiGateway/           # IAiGateway + FixtureAiGateway (deterministic) behind the
                                  # LoggingAiGateway decorator, wired via DI — no Foundry SDK yet
    Contigo.Documents.Contracts/ # upload, metadata, staged extraction, portfolio, contract correction + history (live)
    Contigo.Audit/               # append-only audit events (live)
    Contigo.AiGateway/           # IAiGateway + FixtureAiGateway (wired via DI) + LoggingAiGateway decorator — no Foundry SDK yet
    Contigo.Benchmark/           # IBenchmarkService.GetBenchmarkAsync + normalized Contracts DTOs (E04/F01/US01/T01); BenchmarkAdapterRegistry + AddBenchmarkModule (E04/F01/US01/T02); FixtureBenchmarkAdapter registered as the default IBenchmarkProviderAdapter, incl. statistical weak-comparable abstain (E04/F01/US02/T01+T02) — no host calls AddBenchmarkModule yet (R3)
    Contigo.Suppliers.Products/  # scaffold (R1+)
    Contigo.Renewals/            # renewal engine + opportunity + explainable priority score + threshold scheduler + dashboard pipeline + action (R2; live) — see "Renewal Intelligence" below
    Contigo.Savings/             # price normalization + percentile/target/savings-range calculator (R3; task E04/F02/US01/T01) + persisted, trackable SavingsOpportunity + GET/PATCH /api/savings (task E04/F02/US02/T01) — see "Savings Intelligence" below
    Contigo.Quotes/              # quote upload + hybrid-OCR-reused, schema-constrained line-item extraction (evidence + confidence; deterministic pricing) + POST /api/quotes (R4; task E05/F01/US01/T01) + SKU/edition normalization against a per-tenant canonical mapping, unmatched-SKU flagging (task E05/F01/US02/T01) + benchmark matching/above-in-line-below market assessment + GET /api/quotes/{id}/assessment, AddBenchmarkModule now wired (task E05/F02/US01/T01) + deterministic recommended target range/potential saving on that same endpoint (task E05/F02/US01/T02) + deterministic negotiation strategy (opening target/acceptable range/walk-away threshold + seven canonical levers with rationale, NegotiationStrategyService, no HTTP endpoint yet) (task E05/F03/US01/T01) + NegotiationOutcome capture (original/target/final/deterministic saving+discount/duration/levers used) + POST /api/negotiations/outcomes, append-only/audit-tracked (task E05/F03/US02/T01) — see "Quote Check" / "Market Assessment" / "Negotiation Strategy" / "Negotiation Outcome" below
    Contigo.Chat/                # Ask Contigo structured-vs-semantic query router (R1, task E02/F04/US01/T01) + deterministic dates/spend query handlers (task E02/F04/US01/T02) + RagAnswerService (task E02/F04/US02/T01) + AbstainGuard no-fabrication guard (task E02/F04/US02/T02); AddChatModule wired into Contigo.Api by this last task
  tests/                         # per-module + architecture + R0 integration
```

Hosts are composition roots only: they register modules via `AddXxxModule`
and map HTTP / hosted services. Business logic lives in the libraries.

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
`Contigo.Audit`, `Contigo.Renewals`, `Contigo.Savings`, `Contigo.Quotes`). Apply them against
the same database the hosts use; RLS policies are added in those
migrations, not in Terraform.

**Deployable schema artifact (ADR-021):** every module above also checks in
`Migrations/Scripts/<module>.sql` — `identity-workspace.sql`,
`documents-contracts.sql`, `audit.sql`, `renewals.sql`, `savings.sql`,
`quotes.sql` — generated with `dotnet ef migrations script --idempotent`
from that module's `src/` folder. That checked-in script, applied with
`psql` (or any plain Npgsql client), is the actual `dev`/`demo` deploy
path: CI applies all six, in ADR-021's fixed order, after both
`az containerapp update` steps (task E09/F02/US01/T02) — `Contigo.Api` and
`Contigo.Worker` deliberately never call `Database.MigrateAsync()`, so a
replica boot never mutates schema. Regenerate the script after adding or
changing a migration; `<Module>MigrationScriptStaleCheckTests` (task
E09/F01/US01/T01) fails `dotnet test` if a script is missing or no longer
matches a fresh idempotent generate, and `<Module>MigrationScriptTests`
proves the checked-in script itself — not `MigrateAsync`, no DbContext —
applies (and re-applies) cleanly to a bare `pgvector/pgvector:pg16` server.
`.github/workflows/backend.yml`'s CI apply step (`scripts/pg_connection_string_env.py`
turns the Key Vault `postgres-connection` secret into `psql`'s `PG*`
environment variables; `scripts/schema_apply_verify.py` then proves every
migration_id all six scripts declare landed in `contigo_<env>`'s own
`__EFMigrationsHistory`, failing the job by name otherwise) needs the CI
deploy principal to hold `Key Vault Secrets User` on that environment's
vault — a grant that does not exist yet, see `infra/README.md`'s
"Known gaps".

## HTTP surface today

| Method | Path | Notes |
|--------|------|-------|
| GET | `/health` | ASP.NET health checks |
| POST | `/api/workspaces` | create workspace |
| POST | `/api/workspaces/{tenantId}/invites` | invite; roles Admin / Procurement / Legal / Finance / ReadOnly |
| POST | `/api/documents` | multipart `file` + `X-Tenant-Id` header; also runs `DocumentProcessingPipeline` (classify → hybrid parse → staged extraction → RAG indexing) synchronously before responding (task E02/F06/US01/T01, r1-integration) — response `processingStatus`/`contractId` reflect that run's outcome, not just the initial "Uploaded" write |
| GET | `/api/documents/{id}` | metadata/status; same header |
| PATCH | `/api/contracts/{id}` | `{ corrections: { <field>: <string\|null> }, reason? }` + `X-Tenant-Id` header; versioned correction (ADR-003 `ContractVersion`/`CorrectionHistory`, ADR-009 RLS) — see `Contigo.Documents.Contracts.Application.ContractCorrectionService.CorrectableFieldNames` for the accepted field list; also writes one `IAuditWriter` entry (`contract.corrected`) |
| GET | `/api/contracts/{id}/corrections` | `X-Tenant-Id` header; field-level correction history for one contract, newest first (`Contigo.Documents.Contracts.Application.ContractCorrectionHistoryQueryService`) — 404 if the contract does not exist for the tenant, `[]` if it exists but was never corrected |
| GET | `/api/audit` | tenant-scoped; expects a claims principal (integration tests inject one) |
| GET | `/api/contracts` | portfolio list; spec §8.1 columns; `X-Tenant-Id` header; optional filters `supplierId`, `status`, `risk` (Low/Medium/High/Critical), `autoRenewal`, `minAnnualSpend`, `maxAnnualSpend`, `renewalFrom`/`renewalTo` (yyyy-MM-dd) — no `category` filter yet, see `PortfolioFilter`'s doc comment; optional paging `page` (default 1), `pageSize` (default 25, max 100); response is `{ items, page, pageSize, totalCount }`, not a bare array |
| GET | `/api/contracts/{id}` | Contract 360 aggregate; spec §8.2 header + tabs (overview, commercials, products, clauses, obligations, risks, documents, benchmark, renewal, activity); `X-Tenant-Id` header; 404 when the contract does not exist or belongs to another tenant; `benchmark`/`activity` are always empty arrays — no task has yet mapped a real contract's line items into a `Contigo.Benchmark.Contracts.BenchmarkQuery` (no supplier-name/geography field exists on `Contract` today), so this tab stays empty even though R3's own benchmark comparison is real and provable elsewhere (see "R3 demo smoke test" below); `activity` remains an R4 placeholder — see `Contract360Result`'s doc comment |
| POST | `/api/chat/query` | Ask Contigo (spec §8.3); `{ question: string }` + `X-Tenant-Id` header; routes via `AskContigoQueryRouter`. `Semantic` questions run the real RAG pipeline (`EmbeddingRetrievalService.SearchAsync` tenant-scoped retrieval → `RagAnswerService` → `IAiGateway.AnswerAsync`) and respond `{ question, intent, canDetermine, answer, citations: [{documentId, page, section}], message }` — `citations` empty and `canDetermine: false` when authorized retrieval finds nothing (spec §8.4 "no evidence, no claim"), never a fabricated answer. `Structured` questions get an honest `canDetermine: false` + explanatory `message` — no task has yet mapped a real, tenant-scoped `Contract` row into `Contigo.Chat.Application.ContractFact` for `DeterministicQueryHandler` to run against, see that type's own doc comment |
| GET | `/api/renewals` | Renewal pipeline + insight card (spec §9.3/§10.1); `X-Tenant-Id` header; auto-renewing contracts only, most urgent first; response is `{ items, totalCount }`, each item `{ contractId, supplierId, status, renewalDate, daysUntilRenewal, annualSpend, cancellationDeadline, daysUntilCancellationDeadline, autoRenewal, action, insightCard: { facts, recommendations } }` — `insightCard.recommendations`' benchmark/savings fields (`annualUpliftPercent`, `marketPosition`, `potentialSavingsRange`) are honestly `null` until the Benchmark/Savings modules land (R3); `action`/`recommendedAction` is a deterministic urgency rule, not the full spec §9.2 Priority Score — see `Contigo.Renewals.Application.RenewalPipelineBuilder`'s own doc comment |
| GET | `/api/renewals/{contractId}/priority` | Explainable priority-score breakdown for one contract (spec §9.2; story us-02-priority-score AC-1/AC-2, task E03/F01/US02/T02); `X-Tenant-Id` header; 404 when the contract does not exist or belongs to another tenant (same rule as `GET /api/contracts/{id}`); response is `{ contractId, totalScore, components: { spendWeight, timeUrgency, benchmarkOpportunity, priceIncreaseRisk, contractRisk } }`, each component `{ score, explanation }` — component weights are configurable, see `Contigo.Renewals.Configuration.PriorityScoreWeightsOptions` below; `priceIncreaseRisk`/`benchmarkOpportunity` use their honest no-data default (minimum / neutral respectively) since no uplift or benchmark-position data is wired to real contracts yet |
| POST | `/api/renewals/{id}/action` | Updates owner/status/action for one renewal (spec Appendix A; story us-01-renewal-dashboard-api AC-3); `X-Tenant-Id` header; `{id}` is the same `contractId` the GET above returns per row, not a separate stored "renewal" id; body `{ owner, status, action }` — `status` is one of `NotStarted`/`InProgress`/`Completed`; upserts one row (never a second for the same contract) and writes one `IAuditWriter` entry (`renewal.action_updated`); 400 (not 404) for a missing/invalid tenant header or route id, or for an empty `owner`/`action`/unrecognized `status` — see `Contigo.Renewals.Application.RenewalActionService`'s own doc comment for the honest gap this leaves (no check that `{id}` names an existing, tenant-owned contract; `Contigo.Renewals` cannot reference `Contigo.Documents.Contracts` at all) |
| GET | `/api/savings` | Lists the caller's tenant-scoped `SavingsOpportunity` rows, newest identified first (spec §4.3/§6; module-map.md "Savings \| SavingsOpportunity, RealizedSavings \| /api/savings"; story us-02-savings-opportunity AC-1, task E04/F02/US02/T01; story us-01-savings-kpis AC-2/AC-3, task E04/F03/US01/T02); `X-Tenant-Id` header; response `{ items, totalCount }`, each item also carrying `confidenceLevel` (`Low`/`Medium`/`High`, task E04/F03/US01/T02 — see `SavingsOpportunityResult.ConfidenceLevel`'s own doc comment); no filters yet — see `Contigo.Savings.Application.SavingsOpportunityService.ListAsync`'s own doc comment |
| PATCH | `/api/savings/{id}` | Updates `owner`, `status` (`Identified`/`InProgress`/`Realized`) and/or `realizedAmount` on one `SavingsOpportunity` (AC-1 "updates status/owner..."; AC-3 "realized value is captured and audit-tracked", task E04/F02/US02/T02); `X-Tenant-Id` header; body `{ owner?, status?, realizedAmount? }` — a genuine partial update, any subset of the three fields; 404 when `{id}` does not name an opportunity for this tenant, 400 for every other validation failure (empty owner, unrecognized status, a negative `realizedAmount`, a `realizedAmount` combined with an explicit `status` other than `Realized`, or none of the three fields supplied); writes one `IAuditWriter` entry per successful call — `savings_opportunity.updated`, or `savings_opportunity.realized` instead when `realizedAmount` was supplied (never both). Supplying `realizedAmount` also inserts a new, append-only `Contigo.Savings.Domain.RealizedSavings` row (in the opportunity's own `currency`) and finalizes `status` as `Realized` — either because the caller's own explicit `status` already said so, or automatically when `status` was omitted (see `SavingsOpportunityService.UpdateAsync`'s own doc comment). The response's `realizedAmount` field is non-`null` only on the call that just recorded one — it is not a rolled-up read of this opportunity's full realized-value history, see `SavingsOpportunityResult.RealizedAmount`'s own doc comment; the response also carries `confidenceLevel` (task E04/F03/US01/T02 — same field the `GET` row above documents, shared `ToResponse` wire-shaping) |
| PATCH | `/api/savings/{id}` | Updates `owner` and/or `status` (`Identified`/`InProgress`/`Realized`) on one `SavingsOpportunity` (AC-1 "updates status/owner..."); `X-Tenant-Id` header; body `{ owner?, status? }` — a genuine partial update, either or both fields; 404 when `{id}` does not name an opportunity for this tenant, 400 for every other validation failure (empty owner, unrecognized status, or neither field supplied); writes one `IAuditWriter` entry (`savings_opportunity.updated`) per successful call — setting `status` to `Realized` here does **not** yet create an audit-tracked realized-value record, see `Contigo.Savings.Domain.SavingsOpportunityStatus.Realized`'s own doc comment for the gap task E04/F02/US02/T02 (`RealizedSavings`) closes |
| POST | `/api/quotes` | New Purchase / Quote Check (spec §4.4/§11; module-map.md "Quotes \| Quote, QuoteLine, Assessment... \| /api/quotes"; story us-01-quote-line-extraction AC-1/AC-2/AC-4, task E05/F01/US01/T01); multipart `file` + `X-Tenant-Id` header, same shape as `POST /api/documents`, plus four **optional** form fields task E05/F02/US01/T01 (market-assessment) added — `supplier`, `currency`, `geography`, `purchaseDate` (`yyyy-MM-dd`) — all absent by default and never required for the upload to succeed; nothing in this codebase auto-detects them from the document yet (spec §11.1's own "Identify supplier" workflow step has no task/UI of its own), so a quote uploaded without them simply is not matchable via `GET .../assessment` below until corrected (see `Quote`'s own doc comment; a malformed `purchaseDate` is the one new 400 this endpoint can return); synchronously reuses the epic-02 `HybridDocumentParsingService` (native text or the `ocr` gateway role — ADR-017, no 2-page cap) then runs one schema-constrained `extract` call for line items (quantity/SKU/edition/price/discount/term), persisting one `Contigo.Quotes.Domain.QuoteLine` row per item with source span/page/confidence; `unitPrice`/`extendedPrice` are derived deterministically in code when the model reports only `listPrice`/`discountPercent` (AC-3, Appendix C rule 6 — never asked of the model, see `QuoteLineJsonSchema`); immediately afterward, still the same unit of work, `Contigo.Quotes.Application.Normalization.QuoteLineNormalizationService` (task E05/F01/US01/T02, quote-normalization) sets `NormalizedAnnualUnitPrice`/`NormalizedTermMonths` when `term` matches its own small, fixed billing-cadence vocabulary (monthly/quarterly/semi-annual/annual and common synonyms; every other term deliberately leaves both `null` — spec §11.3's own "line-item normalization is unresolved" outcome, Appendix C rule 10), then `Contigo.Quotes.Application.Normalization.SkuNormalizationService` (task E05/F01/US02/T01, sku-normalization) sets `NormalizedSku`/`NormalizedEdition`/`MatchStatus`; response `{ id, fileName, mimeType, processingStatus, lineItemCount, normalizedLineItemCount, unresolvedNormalizationCount, unmatchedSkuCount, supplier, currency, geography, purchaseDate, createdAt }` — the last four echo exactly what was recorded, including a `null`; a pipeline failure still returns 201 (the upload itself succeeded) with the pre-processing counts all `0`, never an HTTP error. *(This row previously existed twice, one per sibling task's own addition, each missing the other's fields — task E05/F02/US01/T01 consolidated it into the one, accurate, combined shape above.)* |
| GET | `/api/quotes/{id}/assessment` | Quote assessment (spec §4.4/§11.2, Appendix A "Quote assessment"; module-map.md "Quotes \| Quote, QuoteLine, Assessment... \| /api/quotes"; story us-01-market-assessment AC-1/AC-2 (both the "flag" half, task E05/F02/US01/T01, and the "recommended target range + potential saving" half, task E05/F02/US01/T02)/AC-3); `X-Tenant-Id` header; 404 when `{id}` does not name a quote for this tenant; one assessment per `Contigo.Quotes.Domain.QuoteLine` on the quote (creation order) — `{ quoteId, lines: [{ quoteLineId, status, position, unitPrice, quantity, benchmark, confidence, targetSaving, explanation }] }`. `status` is `Assessed`/`QuoteDataUnresolved`/`InsufficientBenchmarkData` (`Contigo.Quotes.Domain.MarketAssessmentStatus`); `position` (`BelowMarket`/`InLine`/`AboveMarket`) is populated only when `status` is `Assessed` — the market band is `[P25, P75]` of the matched `Contigo.Benchmark.Contracts.BenchmarkResult.Distribution`, `InLine` otherwise (see `MarketAssessmentCalculator`'s own doc comment); `benchmark`/`confidence`/`targetSaving` are `null` exactly when no Benchmark Service call was even attempted (`QuoteDataUnresolved`: the quote is missing `supplier`/`currency`/`geography`/`purchaseDate`, or the line itself has no usable product/quantity/term/price), never withheld just because the comparison itself abstained (spec §11.3's benchmark-trust rule — `InsufficientBenchmarkData` still carries real `source`/`sampleSize`/`comparisonDimensions` provenance, and a real `targetSaving` object whose `recommendedTargetLow`/`recommendedTargetHigh`/`savingsRangeLow`/`savingsRangeHigh`/`totalSavingsRangeLow`/`totalSavingsRangeHigh` are honestly `null` with a named `explanation` — see `TargetSavingCalculator`'s own doc comment) |
| GET | `/api/savings/kpis` | Procurement-homepage KPI row (spec §4.3/§10.1; story us-01-savings-kpis AC-1, task E04/F03/US01/T01); `X-Tenant-Id` header; response `{ annualSpendAnalyzed: [{ currency, amount, contractCount }], contractsAnalyzedCount, savingsIdentified/savingsInProgress/savingsRealized: [{ currency, low, high, count, averageConfidence }], upcomingRenewalsCount }` — every money value is grouped by currency, never summed across currencies (no exchange-rate service exists anywhere in this codebase); `contractsAnalyzedCount` counts contracts whose linked document reached `DocumentProcessingStatus.Completed` (a `Contract` row can exist before that — see `Contigo.Documents.Contracts.Application.PortfolioAnalysisCalculator`'s own doc comment); `savingsRealized` reflects each opportunity's own estimated range, not yet the separate, audit-tracked `RealizedSavings` value (task E04/F02/US02/T02's own gap, see `SavingsOpportunityStatus.Realized`'s doc comment); `upcomingRenewalsCount` is the same auto-renewing-contract count `GET /api/renewals`'s own `totalCount` already reports (same 100-contract-per-tenant cap) — see `Contigo.Api.SavingsKpiEndpointExtensions`'s own comment for why it is not a second, independently-computed number |

**Interim auth:** every endpoint above that takes an `X-Tenant-Id` header
(all except `GET /api/audit`, which already expects a claims principal)
takes the tenant from that header, not from a validated JWT. ADR-010
(Entra ID / OIDC on the API) is not wired in the host yet. Do not treat
the header as the long-term contract.

The web client generates TypeScript types from
`web/openapi/contigo-api.v1.json`. The API does **not** yet self-publish
OpenAPI; that document is hand-authored and must grow with these routes.

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
`Embedding` entities. `Contigo.Api.ChatEndpointExtensions` (`POST
/api/chat/query`, task E02/F04/US02/T01) is the composition root that closes
this gap for the `Semantic` branch: it resolves the tenant, calls
`EmbeddingRetrievalService.SearchAsync` (auth-before-retrieval, ADR-011),
maps each hit into `Contigo.AiGateway.Contracts.AiEvidenceSnippet`, then
calls `RagAnswerService`. `DocumentId` on that mapping is a
`{SourceType}:{SourceId}` composite (not a bare id): an `Embedding` row's
`SourceId` only really identifies a document when `SourceType` is
`"Document"` — for `"Clause"`-sourced evidence it identifies the clause row,
and silently relabelling one as the other would misattribute the citation.
`Page` is left `null` (no page column on `Embedding` yet) and `Section`
reports the real chunk index instead of a fabricated section title — true
page/section resolution (joining back to `Clause.SourcePage`/`SourceSpan`) is
a follow-up gap, not attempted by this task. No task has yet mapped a real,
tenant-scoped `Contract` row into `ContractFact`, so the endpoint's
`Structured` branch reports an honest "not wired yet" instead of guessing.

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

**Honest scope note:** this task's own wave-spec `depends_on` names
`renewal-alerts` (task E03/F02/US01/T02, "Alert creation + re-compute on
correction"), but that task has not landed any code as of this task's own
run. The only "alert" artifact that actually exists is the
`renewal.approaching` **threshold event** (task E03/F02/US01/T01,
`threshold-scheduler`, see above) — a durable, queryable audit entry, not a
persisted, de-duplicated `RenewalAlert` row with recompute-on-correction.
`R2EndToEndTests` proves exactly the former (parent story AC-2's own literal
wording, "Threshold events fire") and no more; a persisted alert entity
with recompute-on-correction remains task E03/F02/US01/T02's own, still-open
file scope.

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
| Domain modules | `SharedKernel` only, plus `AiGateway` (Documents, Chat) or `Benchmark` (Renewals, Savings, Quotes) |
| `AiGateway` / `Benchmark` implementations | provider SDKs — when they exist; domain modules see the interface only |
| `Contigo.Api` / `Contigo.Worker` | all modules (composition roots). Azure Blob SDK is host-only |

Do not add a domain → domain or domain → Azure SDK project/package
reference to make a task compile. Put the adapter in the host or behind
the gateway/service project.
