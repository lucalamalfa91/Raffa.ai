// Raffa API Host — thin composition root (ADR-002).
// Wires all modules via DI; contains no business logic.
using Raffa.Api;
using Raffa.Api.Infrastructure;
using Raffa.Audit.Infrastructure;
using Raffa.Chat.Infrastructure;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.Insights;
using Raffa.Market;
using Raffa.Quotes.Infrastructure;
using Raffa.Renewals.Infrastructure;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.Suppliers.Products.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Module registration: each module exposes an AddXxx(IServiceCollection) extension method
// (ADR-002); the host calls it once it takes a dependency on that module. Documents/Contracts
// is the first module with real infrastructure to wire in (us-03's RLS backstop rides along
// automatically via AddDocumentsContractsModule). Further modules register here the same way
// as their own tasks land — this call list is the "composition" ADR-002 asks the host to do.
//
// Task E01/F09/US01/T01 (r0-integration, AC-1 "create workspace"): Identity/Workspace's own
// AddIdentityWorkspaceModule already existed (task E01/F05/US01/T01/T02) but had never been
// called by a host — no endpoint used to attach a tenant claim to. WorkspaceEndpointExtensions
// below is that first endpoint.
var identityWorkspaceConnectionString = builder.Configuration.GetConnectionString("IdentityWorkspace")
    ?? throw new InvalidOperationException(
        "Missing required configuration 'ConnectionStrings:IdentityWorkspace' " +
        "(set env var ConnectionStrings__IdentityWorkspace in deployed environments).");

builder.Services.AddIdentityWorkspaceModule(identityWorkspaceConnectionString);

var documentsContractsConnectionString = builder.Configuration.GetConnectionString("DocumentsContracts")
    ?? throw new InvalidOperationException(
        "Missing required configuration 'ConnectionStrings:DocumentsContracts' " +
        "(set env var ConnectionStrings__DocumentsContracts in deployed environments).");

builder.Services.AddDocumentsContractsModule(documentsContractsConnectionString);

// Task E13/F04/US01/T02 (documents-v2-api): resolves the caller's workspace role for the
// Admin-only document endpoints (reprocess, delete). Lives in the host because it reads the
// Identity/Workspace membership table AND the request's own claims/headers -- see
// Raffa.Api.Infrastructure.WorkspaceRoleResolver for the three-source order and why the
// interim header/membership branches exist while ADR-010 is not wired.
builder.Services.AddScoped<WorkspaceRoleResolver>();

// Object storage (ADR-005 "Object storage" row, ADR-011): the Azure Blob Storage adapter is
// wired here, in the host, and only here — domain modules see IDocumentStorage, never the Azure
// SDK (ADR-002). Container name is fixed by Terraform (infra/modules/storage/main.tf).
var storageConnectionString = builder.Configuration.GetConnectionString("Storage")
    ?? throw new InvalidOperationException(
        "Missing required configuration 'ConnectionStrings:Storage' " +
        "(set env var ConnectionStrings__Storage in deployed environments).");

builder.Services.AddAzureBlobDocumentStorage(storageConnectionString);

// Audit module (task E01/F06/US02/T02, GET /api/audit). Fails fast with a named error rather
// than silently falling back when the config is missing (same "fail loud, not silent"
// convention this codebase already uses for required CI/CD config).
var auditConnectionString = builder.Configuration.GetConnectionString("Audit")
    ?? throw new InvalidOperationException(
        "Missing required configuration: ConnectionStrings:Audit " +
        "(see appsettings.Development.json for the local dev default).");

builder.Services.AddAuditModule(auditConnectionString);

// Task E02/F04/US02/T01 (rag-citations, POST /api/chat/query): the Chat module's own
// AddChatModule(IServiceCollection) (ADR-002) — nothing called it before this task, though
// Raffa.Api.csproj already carried a ProjectReference to Raffa.Chat.csproj in anticipation.
// Depends on IAuditWriter (just registered by AddAuditModule above) and IAiGateway (registered
// transitively by AddDocumentsContractsModule above, via its own AddAiGatewayModule call) — both
// already resolvable in this container by the time RagAnswerService is first requested; DI
// registration order does not matter, only that every AddXxxModule call below happens before
// builder.Build().
//
// Task E13/F05/US01/T02 (story us-01-conversations, AC-4): AddChatModule now also accepts the
// optional chatConnectionString parameter task E13/F05/US01/T01 added — this is the first caller
// that passes one, the same fail-fast shape as every other required connection string above.
// Passing it additionally registers ChatDbContext + ConversationService (see that overload's own
// doc comment) — MapConversationsEndpoints below needs both.
var chatConnectionString = builder.Configuration.GetConnectionString("Chat")
    ?? throw new InvalidOperationException(
        "Missing required configuration 'ConnectionStrings:Chat' " +
        "(set env var ConnectionStrings__Chat in deployed environments).");

builder.Services.AddChatModule(chatConnectionString);

// Task E13/F06/US01/T01 (ask-engine): Chat:PackTokenBudget, registered *before* AddChatModule's
// own always-usable-default TryAddSingleton<PackBudget> so a configured value wins (TryAdd's
// "first registration wins" — see Raffa.Chat.Infrastructure.ServiceCollectionExtensions's own
// doc comment on this exact ordering contract). Absent configuration, GetValue<int?> returns
// null and PackBudget falls back to its own DefaultMaxTokens, unchanged from before this line.
builder.Services.AddSingleton(new Raffa.Chat.Application.Pack.PackBudget(
    builder.Configuration.GetValue<int?>(Raffa.Chat.Application.Pack.PackBudget.SectionName)));

// Task E13/F06/US01/T01 (ask-engine): Suppliers/Products' own AddSuppliersProductsModule(string)
// (ADR-002) — task E13/F03/US01/T01 registered ISupplierResolver/ISupplierNameLookup here but no
// host called it yet (that task's own doc comment: "task E13/F06/US01/T01 is the first real
// caller"). Same fail-fast connection-string shape as every other required module above.
var suppliersConnectionString = builder.Configuration.GetConnectionString("Suppliers")
    ?? throw new InvalidOperationException(
        "Missing required configuration 'ConnectionStrings:Suppliers' " +
        "(set env var ConnectionStrings__Suppliers in deployed environments).");

builder.Services.AddSuppliersProductsModule(suppliersConnectionString);

// Task E13/F06/US01/T01 (ask-engine): the Market module's own AddMarketModule() (ADR-002) — task
// E13/F02/US01/T01 registered the mock feed, its benchmark projection and its in-memory notes
// retrieval here. Also makes "market-feed" the default active Benchmark Service adapter (replacing
// the fixture default AddBenchmarkModule alone would leave in place — R-MKT-02), regardless of the
// order AddBenchmarkModule (transitively, via AddSavingsModule/AddQuotesModule above) already ran
// in.
//
// The connection string is OPTIONAL here, unlike every other module above, and that asymmetry is
// deliberate. Task E13/F02/US01/T02 landed the persisted `market_record`/`market_embedding` index
// the Worker's `ingest-market` job fills (ADR-024: "the provider is called only by the ingestion
// job"; R-MKT-03). When `ConnectionStrings:Market` is configured, this host reads that index —
// pgvector retrieval, the DB-backed benchmark projection, and the persisted record behind
// GET /api/market/records/{id}. When it is absent (a local run with no market database), the
// module keeps the in-memory mock projection and the API still answers, which is why a missing
// value must not fail startup the way a missing tenant database does: the market index is shared,
// read-only reference data, not a tenant's own records.
builder.Services.AddMarketModule(builder.Configuration.GetConnectionString("Market"));

// Task E13/F06/US01/T01 (ask-engine): the Insights module's own AddInsightsModule() (ADR-002) —
// task E13/F07/US01/T01 registered InsightsOptions/CriticalityScoreCalculator here but no host
// called it yet (that task's own doc comment: "Program.cs wiring is a later phase's task
// (F06/T01)"). No connection string: every Insights type is a pure calculator over caller-supplied
// DTOs (Raffa.Insights' own allow-list is [SharedKernel, Benchmark] — no DbContext of its own).
builder.Services.AddInsightsModule();

// AskCopilotService is host-composition wiring (this task's own new pack-composition root — see
// that type's own doc comment: "everything Raffa.Chat's allow-list forbids that module from
// doing itself happens here"), the same kind of direct registration
// QuoteExtractionPipeline/NegotiationOutcomePropagationService already use below for the identical
// "the one place that calls into several modules at once" reason — not a domain module's own
// AddXxxModule. Scoped: shares this request's own DbContext-backed services (all already Scoped)
// rather than a second, independently-tracked instance of any of them.
builder.Services.AddScoped<AskCopilotService>();

// Task E03/F03/US01/T01 (renewal-dashboard, GET /api/renewals): the Renewals module's own
// AddRenewalsModule(IServiceCollection) (ADR-002) — task E03/F01/US01/T01 registered RenewalEngine
// here already but nothing called it; this task is that first real caller (same "wiring lands with
// the first real caller" sequencing AddChatModule followed above).
//
// Task E03/F03/US01/T02 (renewal-action, POST /api/renewals/{id}/action): this module's first
// DbContext (RenewalsDbContext, backing RenewalActionService) means AddRenewalsModule now takes a
// connection string too, the same fail-fast shape as every other required connection string above.
var renewalsConnectionString = builder.Configuration.GetConnectionString("Renewals")
    ?? throw new InvalidOperationException(
        "Missing required configuration 'ConnectionStrings:Renewals' " +
        "(set env var ConnectionStrings__Renewals in deployed environments).");

builder.Services.AddRenewalsModule(renewalsConnectionString);

// Task E03/F02/US01/T02 (renewal-alerts, parent story us-01-threshold-scheduler AC-3):
// RenewalAlertRecomputeService is host-composition wiring, the same kind as
// NegotiationOutcomePropagationService below ("the one place... that calls both
// Raffa.Documents.Contracts and Raffa.Renewals" — see that type's own doc comment), registered
// directly here rather than inside either module's own AddXxxModule. Scoped: shares this request's
// own DocumentsContractsDbContext (already Scoped via AddDocumentsContractsModule above) and
// resolves the already-Scoped RenewalAlertService (registered by AddRenewalsModule above) rather
// than a second, independently-tracked instance of either. ContractsEndpointExtensions.CorrectContractAsync
// (PATCH /api/contracts/{id}) is its only caller.
builder.Services.AddScoped<RenewalAlertRecomputeService>();

// Task E04/F02/US02/T01 (savings-opportunity, GET/PATCH /api/savings): the Savings module's own
// AddSavingsModule(IServiceCollection, string) (ADR-002) — this module's first DbContext
// (SavingsDbContext, backing SavingsOpportunityService), the same "wiring lands with the first
// real caller" sequencing AddRenewalsModule/AddChatModule followed above. Fails fast with the same
// named-error shape as every other required connection string above.
var savingsConnectionString = builder.Configuration.GetConnectionString("Savings")
    ?? throw new InvalidOperationException(
        "Missing required configuration 'ConnectionStrings:Savings' " +
        "(set env var ConnectionStrings__Savings in deployed environments).");

builder.Services.AddSavingsModule(savingsConnectionString);

// Task E05/F01/US01/T01 (quote-extraction, POST /api/quotes): the Quotes module's own
// AddQuotesModule(IServiceCollection, string) (ADR-002) — this module's first DbContext
// (QuotesDbContext, backing QuoteUploadService/QuoteLineExtractionService/
// QuoteLineNormalizationService, the last added by task E05/F01/US01/T02 quote-normalization),
// the same "wiring
// lands with the first real caller" sequencing AddSavingsModule/AddRenewalsModule/AddChatModule
// followed above. Fails fast with the same named-error shape as every other required connection
// string above.
var quotesConnectionString = builder.Configuration.GetConnectionString("Quotes")
    ?? throw new InvalidOperationException(
        "Missing required configuration 'ConnectionStrings:Quotes' " +
        "(set env var ConnectionStrings__Quotes in deployed environments).");

builder.Services.AddQuotesModule(quotesConnectionString);

// QuoteExtractionPipeline is host-composition wiring (Raffa.Api.QuoteExtractionPipeline's own
// doc comment: "the one place in the solution that calls both Raffa.AiGateway and
// Raffa.Quotes"), not a domain module's own AddXxxModule — so, unlike every registration above,
// it is registered directly here rather than inside AddQuotesModule (mirrors
// Raffa.Worker.WorkerServiceCollectionExtensions' own direct registration of
// QueueConsumerHostedService/IQueueConsumer for the identical "host-only wiring" reason). Scoped:
// shares this request's own QuotesDbContext/DocumentsContractsDbContext instances (both Scoped)
// rather than a second, independently-tracked context of either.
builder.Services.AddScoped<QuoteExtractionPipeline>();

// Task E05/F03/US02/T02 (outcome-propagation): NegotiationOutcomePropagationService is the same
// kind of host-composition wiring as QuoteExtractionPipeline above ("the one place... that calls
// both Raffa.Quotes and Raffa.Savings" — see that type's own doc comment), registered directly
// here for the identical reason. Scoped: shares this request's own QuotesDbContext (already Scoped
// via AddQuotesModule above) and resolves the already-Scoped SavingsOpportunityService (registered
// by AddSavingsModule above) rather than a second, independently-tracked instance of either.
builder.Services.AddScoped<NegotiationOutcomePropagationService>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

// Task E01/F09/US01/T01 (r0-integration, AC-1 "create workspace -> invite"): see
// WorkspaceEndpointExtensions for the endpoints themselves.
app.MapWorkspaceEndpoints();

// Task E01/F06/US01/T01 (us-01-document-upload, AC-1) and E01/F06/US01/T02 (AC-3) first mapped
// POST /api/documents and GET /api/documents/{id} inline here; task E02/F06/US01/T01
// (r1-integration) made the upload also run DocumentProcessingPipeline synchronously. Task
// E13/F04/US01/T01 (documents-admission, ADR-024 "gate before persistence") moved both into
// DocumentsEndpointExtensions and reordered the upload: size (413) -> format by extension and
// magic bytes (415) -> DocumentAdmissionGate (422, nothing persisted, one audit row) -> only then
// DocumentUploadService + the pipeline, reusing the gate's own parse and classification. See that
// file's own doc comment, including the interim X-Tenant-Id / X-User-Id posture (ADR-022,
// OQ-askv2-005) every tenant-scoped endpoint in this host still shares.
app.MapDocumentsEndpoints();

// Task E02/F05/US01/T01 (us-01-correction-history, AC-1): versioned PATCH /api/contracts/{id}.
// Task E02/F03/US02/T01 (us-02-contract-360-aggregate, AC-1/AC-2/AC-3): GET /api/contracts/{id},
// the spec §8.2 header + tab aggregate. See ContractsEndpointExtensions for both endpoints —
// ContractCorrectionService owns the versioning/history decisions for the PATCH (never a
// destructive overwrite — Appendix C rule 5); Contract360QueryService owns the tenant-scoped
// aggregation for the GET (ADR-009).
app.MapContractsEndpoints();

// Task E01/F06/US02/T02 (us-02-audit-baseline, AC-2): authorized, tenant-scoped GET /api/audit.
// See AuditEndpointExtensions for the endpoint itself and WorkspacePrincipalAuthorization for
// the authorization decision (401 vs 403 vs the tenant-scoped read).
app.MapAuditEndpoints();

// Task E02/F03/US01/T01 (us-01-portfolio-list-filters, AC-1/AC-2/AC-3): GET /api/contracts, the
// spec §8.1 portfolio columns + filters, tenant-scoped (ADR-009). Same interim X-Tenant-Id
// placeholder as the document endpoints above (ADR-010 is not in force for this task either) —
// see PortfolioEndpointExtensions and those endpoints' own comments for why this gap is not
// promoted to reports/open-questions.md by this task.
app.MapPortfolioEndpoints();

// Task E03/F03/US01/T01 (us-01-renewal-dashboard-api, AC-1/AC-2): GET /api/renewals, the spec
// §9.3/§10.1 renewal pipeline + insight card, tenant-scoped (ADR-009). Same interim X-Tenant-Id
// placeholder as the endpoints above (ADR-010 is not in force for this task either) — see
// RenewalsEndpointExtensions and PortfolioEndpointExtensions' own comments for why this gap is not
// promoted to reports/open-questions.md by this task.
app.MapRenewalsEndpoints();

// Task E04/F02/US02/T01 (savings-opportunity, AC-1): GET /api/savings (list) and PATCH
// /api/savings/{id} (update status/owner), tenant-scoped (ADR-009). Same interim X-Tenant-Id
// placeholder as the endpoints above (ADR-010 is not in force for this task either) — see
// SavingsEndpointExtensions and RenewalsEndpointExtensions' own comments for why this gap is not
// promoted to reports/open-questions.md by this task.
app.MapSavingsEndpoints();

// Task E04/F03/US01/T01 (savings-kpis, AC-1): GET /api/savings/kpis — the procurement-homepage
// KPI row (spec §4.3/§10.1), tenant-scoped (ADR-009). Composes PortfolioQueryService
// (Documents/Contracts) and SavingsKpiQueryService (Savings); see SavingsKpiEndpointExtensions'
// own comment for why "Upcoming Renewals" reuses GET /api/renewals's own candidate query instead
// of adding a Raffa.Renewals dependency here. Same interim X-Tenant-Id placeholder as the
// endpoints above (ADR-010 is not in force for this task either) — see that file's own comment
// for why this gap is not promoted to reports/open-questions.md by this task.
app.MapSavingsKpiEndpoints();

// Task E13/F06/US01/T01 (ask-engine, ADR-024 §6): POST /api/chat/query — kept one release as a
// thin alias that creates a conversation and delegates into AskCopilotService (see
// ChatEndpointExtensions' own doc comment). Supersedes task E02/F04/US02/T01's own
// Structured-vs-Semantic RAG path at this same route — that router/planner/handler trio is now
// reused *inside* AskCopilotService instead (see that type's own doc comment), never called
// directly from this handler any more.
app.MapChatEndpoints();

// Task E13/F05/US01/T02 (story us-01-conversations, AC-2/AC-3) mapped GET/POST /api/conversations
// and GET /api/conversations/{id}; task E13/F06/US01/T01 (ask-engine, AC-8) adds
// POST /api/conversations/{id}/messages to the same call — list/create/get-with-messages/ask over
// the conversations store task E13/F05/US01/T01 added (ADR-024 "Conversations (D5)"). See
// ConversationsEndpointExtensions for the endpoints themselves and their own doc comment for the
// caller-identity rule (token subject when an authenticated principal is present, else the
// required X-User-Id header — ADR-022 posture, non-authoritative, OQ-askv2-005).
app.MapConversationsEndpoints();

// Task E13/F08/US01/T01 (story us-01-capability-catalog, AC-1): GET /api/capabilities — the
// versioned, role-aware capability catalog (R-SYS-01). See CapabilitiesEndpointExtensions; this
// task (F06/T01) is its first-mapped caller.
app.MapCapabilitiesEndpoints();

// Task E13/F07/US01/T01 (insights-calculators, AC-5): GET /api/insights/criticality and
// GET /api/contracts/{id}/strategy — the portfolio criticality ranking and the per-contract
// renewal-strategy pack Ask itself narrates (same numbers, one source — see
// InsightsEndpointExtensions/AskCopilotService's own doc comments). This task (F06/T01) is this
// file's first-mapped caller.
app.MapInsightsEndpoints();

// Task E13/F06/US01/T01 (ask-engine): GET /api/market/records/{id} — the market citation panel
// (R-EVD-02). See MarketEndpointExtensions.
app.MapMarketEndpoints();

// Task E05/F01/US01/T01 (quote-extraction, parent story us-01-quote-line-extraction AC-1/AC-2/
// AC-4): POST /api/quotes — upload a supplier quote, then synchronously reuse the epic-02 hybrid
// OCR path and run schema-constrained line-item extraction (quantity/SKU/edition/price/discount/
// term with evidence + confidence). See QuotesEndpointExtensions/QuoteExtractionPipeline for the
// endpoint and orchestration respectively.
app.MapQuotesEndpoints();

// Task E05/F03/US02/T01 (negotiation-outcome, parent story us-02-outcome-capture AC-1): POST
// /api/negotiations/outcomes — records the final negotiated outcome (original/target/final/
// saving/discount/duration/levers) as permissioned proprietary learning data (spec §12.2/§12.3).
// See NegotiationsEndpointExtensions/NegotiationOutcomeService for the endpoint and
// validation/persistence/audit decisions respectively. Shares Raffa.Quotes's own QuotesDbContext
// (already registered by AddQuotesModule above) — no new connection string.
//
// Task E05/F03/US02/T02 (outcome-propagation, parent story AC-2 "Realized savings surface on the
// savings dashboard (cross-wave)"): the same handler now also calls
// NegotiationOutcomePropagationService when the caller supplies a savingsOpportunityId, writing the
// realized figure onto Raffa.Savings's own SavingsOpportunity/RealizedSavings (task
// E04/F02/US02/T02) via the exact same PATCH /api/savings/{id} write path a human caller already
// uses. See NegotiationOutcomePropagationService for why this never fails an already-durable
// capture.
app.MapNegotiationsEndpoints();

app.Run();

// Exposes the top-level-statement entry point to WebApplicationFactory<Program> in the
// Raffa.Api.Tests integration test project (a separate assembly).
public partial class Program { }
