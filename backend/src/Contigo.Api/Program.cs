// Contigo API Host — thin composition root (ADR-002).
// Wires all modules via DI; contains no business logic.
using Contigo.Api;
using Contigo.Api.Infrastructure;
using Contigo.Audit.Infrastructure;
using Contigo.Chat.Infrastructure;
using Contigo.Documents.Contracts.Application;
using Contigo.Documents.Contracts.Application.Extraction;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.Identity.Workspace.Infrastructure;
using Contigo.Quotes.Infrastructure;
using Contigo.Renewals.Infrastructure;
using Contigo.Savings.Infrastructure;
using Contigo.SharedKernel;

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
// Contigo.Api.csproj already carried a ProjectReference to Contigo.Chat.csproj in anticipation.
// Depends on IAuditWriter (just registered by AddAuditModule above) and IAiGateway (registered
// transitively by AddDocumentsContractsModule above, via its own AddAiGatewayModule call) — both
// already resolvable in this container by the time RagAnswerService is first requested; DI
// registration order does not matter, only that every AddXxxModule call below happens before
// builder.Build().
builder.Services.AddChatModule();

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
// Contigo.Documents.Contracts and Contigo.Renewals" — see that type's own doc comment), registered
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

// QuoteExtractionPipeline is host-composition wiring (Contigo.Api.QuoteExtractionPipeline's own
// doc comment: "the one place in the solution that calls both Contigo.AiGateway and
// Contigo.Quotes"), not a domain module's own AddXxxModule — so, unlike every registration above,
// it is registered directly here rather than inside AddQuotesModule (mirrors
// Contigo.Worker.WorkerServiceCollectionExtensions' own direct registration of
// QueueConsumerHostedService/IQueueConsumer for the identical "host-only wiring" reason). Scoped:
// shares this request's own QuotesDbContext/DocumentsContractsDbContext instances (both Scoped)
// rather than a second, independently-tracked context of either.
builder.Services.AddScoped<QuoteExtractionPipeline>();

// Task E05/F03/US02/T02 (outcome-propagation): NegotiationOutcomePropagationService is the same
// kind of host-composition wiring as QuoteExtractionPipeline above ("the one place... that calls
// both Contigo.Quotes and Contigo.Savings" — see that type's own doc comment), registered directly
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

// Task E01/F06/US01/T01 (us-01-document-upload, AC-1): stores the uploaded bytes in
// tenant-scoped blob storage and creates the queued classification job
// (DocumentUploadService owns the actual business logic; this delegate only translates
// HTTP <-> the service call, per ADR-002's "host is a thin composition root").
//
// Task E02/F06/US01/T01 (r1-integration, AC-1 "upload -> parse/OCR -> classify -> extract"):
// once the upload itself is durable, this handler also runs DocumentProcessingPipeline —
// hybrid parse -> classify -> staged extraction -> Ask Contigo indexing — synchronously, in
// this same request, before responding. See DocumentProcessingPipeline's own doc comment for
// why synchronous/in-request is this task's deliberate interim choice (nothing in this
// codebase dispatches the queued Classification job to a handler off a durable queue yet). The
// file bytes are read into memory once, up front: DocumentUploadService needs a stream for
// storage and DocumentProcessingPipeline needs the same bytes again afterward, and an
// IFormFile's own stream is not guaranteed re-readable after the first copy. A pipeline
// failure is reported honestly in the response (processingStatus/contractId fall back to the
// just-uploaded, pre-processing values) but never turns an already-successful upload into an
// HTTP error — the bytes are safely stored and the document row already exists either way.
//
// ADR-010 (Entra ID/OIDC) is not in the "architecture decisions in force" list for this task,
// so there is no validated caller identity/JWT yet. The tenant is taken from an explicit
// X-Tenant-Id header instead of a token claim — a deliberate interim placeholder (paired with
// DocumentUploadService.UnattributedActor). Deliberately NOT promoted to
// reports/open-questions.md by this task: that file is appended to by every wave/* implementer
// branch in this fan-out, and concurrent appends to it have previously broken a phase-barrier
// merge, so a mid-wave append here would risk repeating that. Fold one consolidated entry for
// both placeholders into the ledger at a safe point (e.g. when the auth-middleware task is
// authored), then replace both with claim-based tenant/actor resolution.
app.MapPost("/api/documents", async Task<IResult> (
    HttpRequest request,
    DocumentUploadService uploadService,
    DocumentProcessingPipeline processingPipeline,
    CancellationToken cancellationToken) =>
{
    if (!request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
        || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
    {
        return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
    }

    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Expected multipart/form-data with a 'file' field.");
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files["file"];
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("A non-empty 'file' form field is required.");
    }

    byte[] fileBytes;
    await using (var uploadStream = file.OpenReadStream())
    await using (var buffer = new MemoryStream())
    {
        await uploadStream.CopyToAsync(buffer, cancellationToken);
        fileBytes = buffer.ToArray();
    }

    var tenantId = new TenantId(tenantGuid);

    using var storageContent = new MemoryStream(fileBytes);
    var result = await uploadService.UploadAsync(
        tenantId, file.FileName, file.ContentType, storageContent, cancellationToken);

    if (result.IsFailure)
    {
        return Results.BadRequest(result.Error);
    }

    var uploaded = result.Value;

    var processingResult = await processingPipeline.ProcessAsync(
        tenantId, uploaded.DocumentId, uploaded.FileName, uploaded.MimeType, fileBytes, cancellationToken);

    var processingStatus = processingResult.IsSuccess
        ? processingResult.Value.ProcessingStatus
        : uploaded.ProcessingStatus;
    var contractId = processingResult.IsSuccess ? processingResult.Value.ContractId.Value : (Guid?)null;

    return Results.Created($"/api/documents/{uploaded.DocumentId}", new
    {
        id = uploaded.DocumentId.Value,
        contractId,
        fileName = uploaded.FileName,
        mimeType = uploaded.MimeType,
        processingStatus = processingStatus.ToString(),
        createdAt = uploaded.CreatedAt,
    });
});

// Task E01/F06/US01/T02 (us-01-document-upload, AC-3): reads back the metadata/status AC-2
// already persists (task T01), scoped to the caller's tenant. Same interim X-Tenant-Id
// placeholder as POST /api/documents above (ADR-010 is not in force for this task either, so
// there is still no validated caller principal to take the tenant from) — see that endpoint's
// comment for why this is not promoted to reports/open-questions.md by this task.
app.MapGet("/api/documents/{id}", async Task<IResult> (
    string id,
    HttpRequest request,
    DocumentQueryService queryService,
    CancellationToken cancellationToken) =>
{
    if (!request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
        || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
    {
        return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
    }

    if (!Guid.TryParse(id, out var documentGuid))
    {
        return Results.BadRequest("The document id in the route must be a GUID.");
    }

    var metadata = await queryService
        .GetByIdAsync(new TenantId(tenantGuid), new EntityId(documentGuid), cancellationToken)
        .ConfigureAwait(false);

    if (metadata is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new
    {
        id = metadata.DocumentId.Value,
        contractId = metadata.ContractId?.Value,
        fileName = metadata.FileName,
        mimeType = metadata.MimeType,
        documentType = metadata.DocumentType.ToString(),
        processingStatus = metadata.ProcessingStatus.ToString(),
        createdAt = metadata.CreatedAt,
    });
});

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
// of adding a Contigo.Renewals dependency here. Same interim X-Tenant-Id placeholder as the
// endpoints above (ADR-010 is not in force for this task either) — see that file's own comment
// for why this gap is not promoted to reports/open-questions.md by this task.
app.MapSavingsKpiEndpoints();

// Task E02/F04/US02/T01 (us-02-rag-citations, AC-1/AC-2/AC-3): POST /api/chat/query — the RAG
// retrieval + grounded-answer-with-citations path for Ask Contigo semantic questions (spec §8.3).
// See ChatEndpointExtensions for the endpoint itself; AskContigoQueryRouter (task
// E02/F04/US01/T01) decides Structured vs Semantic, EmbeddingRetrievalService (task
// E02/F02/US02/T02) performs the tenant-scoped retrieval, and RagAnswerService (this task) turns
// the two into a grounded answer with citations or an explicit "cannot determine".
app.MapChatEndpoints();

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
// validation/persistence/audit decisions respectively. Shares Contigo.Quotes's own QuotesDbContext
// (already registered by AddQuotesModule above) — no new connection string.
//
// Task E05/F03/US02/T02 (outcome-propagation, parent story AC-2 "Realized savings surface on the
// savings dashboard (cross-wave)"): the same handler now also calls
// NegotiationOutcomePropagationService when the caller supplies a savingsOpportunityId, writing the
// realized figure onto Contigo.Savings's own SavingsOpportunity/RealizedSavings (task
// E04/F02/US02/T02) via the exact same PATCH /api/savings/{id} write path a human caller already
// uses. See NegotiationOutcomePropagationService for why this never fails an already-durable
// capture.
app.MapNegotiationsEndpoints();

app.Run();

// Exposes the top-level-statement entry point to WebApplicationFactory<Program> in the
// Contigo.Api.Tests integration test project (a separate assembly).
public partial class Program { }
