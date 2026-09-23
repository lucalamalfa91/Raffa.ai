using Raffa.AiGateway;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Application.Preview;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Documents.Contracts.Infrastructure;

/// <summary>
/// Composition-root wiring for the Documents/Contracts module. ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method"; domain modules never wire themselves into a
/// host directly. Task E01/F04/US03/T01 (us-03) wired the ambient tenant claim
/// (<see cref="ITenantContext"/>) and the RLS connection interceptor into the DbContext pipeline
/// itself, so the first endpoint/handler to land only had to call
/// <see cref="ITenantContext.BeginScope"/> around it — the RLS backstop was already live. That
/// first endpoint is task E01/F06/US01/T01's <c>POST /api/documents</c>, wired via
/// <see cref="DocumentUploadService"/>, registered here alongside the DbContext. Task
/// E01/F06/US01/T02's <c>GET /api/documents/{id}</c> reuses the same DbContext registration and
/// adds <see cref="DocumentQueryService"/> alongside it. Task E02/F01/US02/T01
/// (us-02-staged-extraction) adds <see cref="StagedExtractionService"/>, and with it this
/// module's first real dependency on <c>Raffa.AiGateway</c> (already allow-listed for this
/// module — <c>Raffa.ArchitectureTests.DependencyDirectionTests</c>) — see
/// <see cref="Raffa.AiGateway.ServiceCollectionExtensions.AddAiGatewayModule"/>'s own doc
/// comment for why calling it from here, rather than adding it to every host's own composition
/// (<c>Raffa.Api/Program.cs</c>, <c>Raffa.Worker.WorkerServiceCollectionExtensions</c>),
/// keeps <see cref="IAiGateway"/> resolvable everywhere this module already is without changing
/// either host's code.
/// adds <see cref="DocumentQueryService"/> alongside it. Task E02/F03/US01/T01's
/// <c>GET /api/contracts</c> reuses it again and adds <see cref="PortfolioQueryService"/>.
/// adds <see cref="DocumentQueryService"/> alongside it. Task E02/F05/US01/T01's `PATCH
/// /api/contracts/{id}` (<see cref="ContractCorrectionService"/>) reuses the same registration
/// again. Task E02/F02/US02/T02 (us-02-embedding-search-index) adds
/// <see cref="EmbeddingRetrievalService"/> alongside it — no new dependency to wire, since the
/// module's own <see cref="IAiGateway"/> registration (this method's own
/// <c>AddAiGatewayModule</c> call, above) already resolves everything that service needs.
/// Task E02/F03/US01/T01's <c>GET /api/contracts</c> reuses the same registration and adds
/// <see cref="PortfolioQueryService"/>; task E02/F05/US01/T01's `PATCH /api/contracts/{id}` adds
/// <see cref="ContractCorrectionService"/>; task E02/F03/US02/T01's `GET /api/contracts/{id}`
/// (Contract 360) adds <see cref="Contract360QueryService"/> — all reuse the same DbContext
/// registration, never a second one.
/// either host's code. Task E02/F03/US01/T01's <c>GET /api/contracts</c> reuses it again and adds
/// <see cref="PortfolioQueryService"/>. Task E02/F05/US01/T01's `PATCH /api/contracts/{id}`
/// (<see cref="ContractCorrectionService"/>) reuses the same registration again. Task
/// E02/F05/US01/T02 (correction-audit) adds
/// <see cref="ContractCorrectionHistoryQueryService"/> (`GET /api/contracts/{id}/corrections`) and
/// gives <see cref="ContractCorrectionService"/> a required <see cref="IAuditWriter"/> dependency
/// — already resolvable in both hosts (<c>Raffa.Api</c>/<c>Raffa.Worker</c>) because each
/// already calls <c>AddAuditModule</c> alongside this method (see
/// <see cref="Raffa.Worker.WorkerServiceCollectionExtensions.AddWorkerHost"/>'s own doc comment
/// on why <see cref="DocumentUploadService"/>'s identical dependency is already safe there).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentsContractsModule(
        this IServiceCollection services, string connectionString)
    {
        // TryAdd: any module (or the host) may call this defensively; only the first
        // registration wins, and every module shares the same ambient tenant claim (ADR-009)
        // and the same "now" (IClock).
        services.TryAddSingleton<ITenantContext, TenantContext>();
        services.TryAddSingleton<IClock, SystemClock>();

        services.AddDbContext<DocumentsContractsDbContext>(
            (sp, options) => DocumentsContractsDbContextOptions.Configure(
                options, connectionString, sp.GetRequiredService<ITenantContext>()));

        // See the type doc comment: this module's own IAiGateway/AiGatewayModelOptions wiring.
        services.AddAiGatewayModule();

        // Scoped: shares the request/job's own DbContext instance (also Scoped, via AddDbContext
        // above) rather than a second, independently-tracked context.
        services.AddScoped<DocumentUploadService>();
        services.AddScoped<DocumentQueryService>();
        services.AddScoped<StagedExtractionService>();

        // Task E04/F03/US01/T01 (savings-kpis): PortfolioQueryService's own
        // GetAnalysisSummaryAsync needs this stateless, dependency-free calculator — TryAddSingleton,
        // same treatment Raffa.Renewals.Application.RenewalEngine/PriorityScoreCalculator already
        // get — registered before the Scoped service below so constructor injection resolves it.
        services.TryAddSingleton<PortfolioAnalysisCalculator>();
        services.AddScoped<PortfolioQueryService>();
        services.AddScoped<ContractCorrectionService>();
        services.AddScoped<EmbeddingRetrievalService>();

        // Task E02/F01/US02/T02 (hybrid-ocr): the native/OCR pre-pass that produces the
        // DocumentPageText list StagedExtractionService above already depends on.
        // NativeDocumentTextExtractor holds no per-request state (no DbContext, no ambient tenant
        // scope), so — unlike the DbContext-bound services above — Singleton is correct, not just
        // convenient.
        services.TryAddSingleton<INativeDocumentTextExtractor, NativeDocumentTextExtractor>();
        services.AddScoped<HybridDocumentParsingService>();
        services.AddScoped<Contract360QueryService>();
        services.AddScoped<ContractCorrectionHistoryQueryService>();

        // Task E02/F06/US01/T01 (r1-integration): the orchestrator that finally calls
        // HybridDocumentParsingService/StagedExtractionService/EmbeddingRetrievalService together
        // (see DocumentProcessingPipeline's own doc comment for why nothing did before this task).
        // Scoped for the same reason every service above is: it shares this registration's own
        // DbContext instance, not a second one.
        services.AddScoped<DocumentProcessingPipeline>();

        // Per-line market comparison, written at extraction and refreshed when stale on read.
        // Its IMarketPriceMatcher / ISupplierNameLookup ports are optional constructor parameters:
        // a host that composes Raffa.Market / Raffa.Suppliers.Products in supplies them, any other
        // host keeps what is stored.
        services.AddScoped<LineItemMarketPriceService>();

        // Task E13/F04/US01/T01 (documents-admission): the admission gate and its thresholds.
        // DocumentAdmissionOptions is bound once from the "Documents" section (defaults from its
        // own property initializers when the section is absent) and registered as a plain
        // singleton — the same shape Raffa.AiGateway uses for AiGatewayOcrOptions: the gate's
        // constructor takes the options type directly, so IOptions<T> would add nothing here.
        services.TryAddSingleton(sp =>
        {
            var options = new DocumentAdmissionOptions();
            sp.GetRequiredService<IConfiguration>()
                .GetSection(DocumentAdmissionOptions.SectionName)
                .Bind(options);
            return options;
        });
        services.AddScoped<DocumentAdmissionGate>();

        // Task E13/F04/US01/T02 (documents-v2-api): preview rendering + the reprocess/delete units
        // of work. Task E22/F02/US01/T01: PdfPageDocumentPreviewRenderer (Docnet.Core/pdfium) is
        // registered ahead of PlaceholderDocumentPreviewRenderer via TryAdd — the real renderer wins
        // for PDFs. Office (DOCX/XLSX) is painted from native page text so the viewer overlay is
        // not stuck on the "FILE PREVIEW NOT RENDERED" card. A host that registers its own
        // IDocumentPreviewRenderer before calling this extension still wins (TryAdd is
        // first-registration-wins).
        services.TryAddSingleton<PdfPageDocumentPreviewRenderer>();
        services.TryAddSingleton<ImageDocumentPreviewRenderer>();
        services.TryAddSingleton<OfficePageDocumentPreviewRenderer>();
        services.TryAddSingleton<IDocumentPreviewRenderer>(sp =>
            new CompositeDocumentPreviewRenderer(
                sp.GetRequiredService<PdfPageDocumentPreviewRenderer>(),
                sp.GetRequiredService<ImageDocumentPreviewRenderer>(),
                sp.GetRequiredService<OfficePageDocumentPreviewRenderer>()));
        services.AddScoped<DocumentPreviewService>();
        services.AddScoped<DocumentReprocessService>();
        services.AddScoped<DocumentPriorityService>();
        services.AddScoped<DocumentDeleteService>();
        services.AddScoped<ContractPurgeService>();

        // Review sign-off and the evidence read behind the review screen's pane
        // (`POST /api/documents/{id}/validate`, `GET /api/contracts/{id}/evidence`). Scoped for the
        // same reason as every DbContext-bound service above.
        services.AddScoped<DocumentValidationService>();
        services.AddScoped<NothingToReviewAutoValidator>();
        services.AddScoped<ContractEvidenceQueryService>();

        // Task E16/F02/US01/T01 (async-processing-schema): the conditional-UPDATE claim us-02's
        // Worker handler will use. Scoped — shares this registration's own DbContext instance,
        // same reason as every other service above.
        services.AddScoped<IExtractionJobClaimStore, ExtractionJobClaimStore>();
        services.TryAddSingleton<IExtractionRunAborter, ExtractionRunAborter>();
        services.AddScoped<IExtractionHangWatch, ExtractionHangWatch>();
        services.AddScoped<ExtractionProgressHeartbeat>();
        services.AddScoped<HungProcessingRecoveryService>();

        // Task E19/F03/US01/T01 (us-01-step-ticks-api, ADR-028 §D3): Contract 360's negotiation
        // checklist ticks. Scoped -- shares this registration's own DbContext instance, same
        // reason as every other service above.
        services.AddScoped<NegotiationStepService>();

        // Task E23/F03/US01/T01 (NW-63r, phrase-edit-write; ADR-029 w18 footer clause 1): writes a
        // reviewer's corrected OCR phrase as an override beside the field's proposal on
        // ExtractionEvidence (`PATCH /api/contracts/{id}/evidence/{fieldName}`). Scoped -- shares
        // this registration's own DbContext instance, same reason as every other service above.
        services.AddScoped<ContractPhraseEditService>();

        return services;
    }
}
