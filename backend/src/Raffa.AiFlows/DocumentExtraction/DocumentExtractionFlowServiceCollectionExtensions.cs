using Microsoft.Extensions.DependencyInjection;
using Raffa.AiFlows.DocumentExtraction.Admission;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.AiFlows.Shared.Parsing;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.AiFlows.DocumentExtraction;

/// <summary>
/// Registration of the document-extraction flow (F5): the content gate
/// (<see cref="DocumentAdmissionGate"/>, port <see cref="IDocumentAdmissionEvaluator"/>), the
/// document-processing orchestrator (<see cref="DocumentProcessingOrchestrator"/>, port
/// <see cref="IDocumentProcessingFlow"/>) and the hybrid parser both of them use
/// (<see cref="HybridDocumentParsingService"/>). The two ports are defined in
/// <c>Raffa.Documents.Contracts</c> and consumed by its <c>ExtractionRequestedHandler</c>; the module
/// registers no implementation of them, so a host that does not call <c>AddAiFlows</c> fails fast
/// when the handler is resolved.
///
/// <para>
/// The orchestrator reaches the module's persistence and its staged extraction through the types the
/// module already registers and exposes: <c>DocumentsContractsDbContext</c>,
/// <c>StagedExtractionService</c> (still in the module: it mixes AI and persistence and is split
/// stage by stage in a later step), <c>EmbeddingRetrievalService</c>, <c>DocumentPreviewService</c> and
/// <c>LineItemMarketPriceService</c>. All are Scoped, so the orchestrator shares the request/job's own
/// DbContext instance exactly as before the move.
/// </para>
/// </summary>
public static class DocumentExtractionFlowServiceCollectionExtensions
{
    /// <summary>Registers the document-extraction flow. Same lifetimes and registration shapes the
    /// module used before the flow moved here.</summary>
    public static IServiceCollection AddDocumentExtractionFlow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<HybridDocumentParsingService>();

        // The extraction handler depends on the port; the concrete type stays resolvable for callers
        // that ask for it directly. Both names resolve to the same scoped instance.
        services.AddScoped<DocumentProcessingOrchestrator>();
        services.AddScoped<IDocumentProcessingFlow>(sp => sp.GetRequiredService<DocumentProcessingOrchestrator>());

        services.AddScoped<DocumentAdmissionGate>();
        services.AddScoped<IDocumentAdmissionEvaluator>(sp => sp.GetRequiredService<DocumentAdmissionGate>());

        return services;
    }
}
