using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.DocumentExtraction.Admission;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.AiFlows.DocumentExtraction.Stages;
using Raffa.AiFlows.Shared.Parsing;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.AiFlows.DocumentExtraction;

/// <summary>
/// Registration of the document-extraction flow (F5): the content gate
/// (<see cref="DocumentAdmissionGate"/>, port <see cref="IDocumentAdmissionEvaluator"/>), the
/// document-processing orchestrator (<see cref="DocumentProcessingOrchestrator"/>, port
/// <see cref="IDocumentProcessingFlow"/>), the staged-extraction orchestrator
/// (<see cref="ExtractionOrchestrator"/>) with its seven <see cref="IExtractionStage"/>s, and the hybrid
/// parser they use (<see cref="HybridDocumentParsingService"/>). The two ports are defined in
/// <c>Raffa.Documents.Contracts</c> and consumed by its <c>ExtractionRequestedHandler</c>; the module
/// registers no implementation of them, so a host that does not call <c>AddAiFlows</c> fails fast
/// when the handler is resolved.
///
/// <para>
/// The orchestrators reach the module's persistence through the types the module already registers and
/// exposes: <c>DocumentsContractsDbContext</c>, <c>IExtractionRunStore</c> (the persistence of a staged
/// run), <c>EmbeddingRetrievalService</c>, <c>DocumentPreviewService</c> and
/// <c>LineItemMarketPriceService</c>. All are Scoped, so they share the request/job's own DbContext
/// instance. The stages are stateless singletons.
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

        // Staged extraction: one stage class per pipeline stage (idempotent: AddAiFlows may be called twice),
        // and the orchestrator that runs them against the module's IExtractionRunStore.
        services.TryAddEnumerable(
        [
            ServiceDescriptor.Singleton<IExtractionStage, MetadataStage>(),
            ServiceDescriptor.Singleton<IExtractionStage, CommercialTermsStage>(),
            ServiceDescriptor.Singleton<IExtractionStage, DatesAndRenewalTermsStage>(),
            ServiceDescriptor.Singleton<IExtractionStage, LineItemsStage>(),
            ServiceDescriptor.Singleton<IExtractionStage, LegalClausesStage>(),
            ServiceDescriptor.Singleton<IExtractionStage, ObligationsStage>(),
            ServiceDescriptor.Singleton<IExtractionStage, RiskStage>(),
        ]);

        // Bound once from "Documents:Extraction"; defaults from the property initialisers when absent.
        services.TryAddSingleton(sp =>
        {
            var options = new ExtractionOrchestrationOptions();
            sp.GetService<IConfiguration>()?.GetSection(ExtractionOrchestrationOptions.SectionName).Bind(options);
            return options;
        });
        services.AddScoped<ExtractionOrchestrator>();

        // The extraction handler depends on the port; the concrete type stays resolvable for callers
        // that ask for it directly. Both names resolve to the same scoped instance.
        services.AddScoped<DocumentProcessingOrchestrator>();
        services.AddScoped<IDocumentProcessingFlow>(sp => sp.GetRequiredService<DocumentProcessingOrchestrator>());

        services.AddScoped<DocumentAdmissionGate>();
        services.AddScoped<IDocumentAdmissionEvaluator>(sp => sp.GetRequiredService<DocumentAdmissionGate>());

        return services;
    }
}
