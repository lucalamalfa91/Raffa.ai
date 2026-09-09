using Contigo.AiGateway;
using Contigo.Documents.Contracts.Application;
using Contigo.Documents.Contracts.Application.Admission;
using Contigo.Documents.Contracts.Application.Extraction;
using Contigo.Documents.Contracts.Application.Preview;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Contigo.Documents.Contracts.Infrastructure;

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
/// module's first real dependency on <c>Contigo.AiGateway</c> (already allow-listed for this
/// module — <c>Contigo.ArchitectureTests.DependencyDirectionTests</c>) — see
/// <see cref="Contigo.AiGateway.ServiceCollectionExtensions.AddAiGatewayModule"/>'s own doc
/// comment for why calling it from here, rather than adding it to every host's own composition
/// (<c>Contigo.Api/Program.cs</c>, <c>Contigo.Worker.WorkerServiceCollectionExtensions</c>),
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
/// — already resolvable in both hosts (<c>Contigo.Api</c>/<c>Contigo.Worker</c>) because each
/// already calls <c>AddAuditModule</c> alongside this method (see
/// <see cref="Contigo.Worker.WorkerServiceCollectionExtensions.AddWorkerHost"/>'s own doc comment
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
        // same treatment Contigo.Renewals.Application.RenewalEngine/PriorityScoreCalculator already
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

        // Task E13/F04/US01/T01 (documents-admission): the admission gate and its thresholds.
        // DocumentAdmissionOptions is bound once from the "Documents" section (defaults from its
        // own property initializers when the section is absent) and registered as a plain
        // singleton — the same shape Contigo.AiGateway uses for AiGatewayOcrOptions: the gate's
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
        // of work. The renderer is a TryAdd, so a host that registers a rasteriser-backed
        // IDocumentPreviewRenderer of its own (Contigo.Api infrastructure - ADR-002 keeps the
        // native SDK out of this module) wins over the built-in placeholder renderer.
        services.TryAddSingleton<IDocumentPreviewRenderer, PlaceholderDocumentPreviewRenderer>();
        services.AddScoped<DocumentPreviewService>();
        services.AddScoped<DocumentReprocessService>();
        services.AddScoped<DocumentDeleteService>();

        return services;
    }
}
