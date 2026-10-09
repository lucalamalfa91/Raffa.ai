using Raffa.Market;
using Raffa.AiFlows.DocumentExtraction;
using Raffa.AiFlows.DocumentExtraction.Admission;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.AiFlows.Shared.Parsing;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Extraction;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.AiFlows.Tests.DocumentExtraction;

/// <summary>
/// The document-extraction flow (F5) owns the implementations of two ports that
/// <c>Raffa.Documents.Contracts</c> defines: <see cref="IDocumentAdmissionEvaluator"/> and
/// <see cref="IDocumentProcessingFlow"/>. These tests pin the split: the module alone provides
/// neither, <c>AddAiFlows</c> provides both with the lifetimes and registration shapes the module
/// used before the move.
/// </summary>
public sealed class DocumentExtractionFlowRegistrationTests
{
    private static ServiceCollection ModuleOnly() => DocumentsModuleServices.Create();

    [Fact]
    public void The_module_alone_registers_no_implementation_of_either_port()
    {
        using var provider = ModuleOnly().BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<IDocumentAdmissionEvaluator>());
        Assert.Null(scope.ServiceProvider.GetService<IDocumentProcessingFlow>());
        Assert.Null(scope.ServiceProvider.GetService<HybridDocumentParsingService>());
    }

    [Fact]
    public void AddAiFlows_provides_both_ports_as_the_scoped_concrete_flow_types()
    {
        var services = ModuleOnly();
        services.AddMarketModule();
        services.AddAiFlows();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var admission = scope.ServiceProvider.GetRequiredService<IDocumentAdmissionEvaluator>();
        var processing = scope.ServiceProvider.GetRequiredService<IDocumentProcessingFlow>();

        Assert.IsType<DocumentAdmissionGate>(admission);
        Assert.IsType<DocumentProcessingOrchestrator>(processing);
        Assert.Same(admission, scope.ServiceProvider.GetRequiredService<DocumentAdmissionGate>());
        Assert.Same(processing, scope.ServiceProvider.GetRequiredService<DocumentProcessingOrchestrator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<HybridDocumentParsingService>());
    }

    [Fact]
    public void Every_flow_service_is_scoped_so_the_orchestrator_shares_the_callers_DbContext()
    {
        var services = new ServiceCollection().AddDocumentExtractionFlow();

        var descriptors = services.Where(d =>
            d.ServiceType == typeof(IDocumentAdmissionEvaluator)
            || d.ServiceType == typeof(IDocumentProcessingFlow)
            || d.ServiceType == typeof(DocumentAdmissionGate)
            || d.ServiceType == typeof(DocumentProcessingOrchestrator)
            || d.ServiceType == typeof(HybridDocumentParsingService)).ToList();

        Assert.Equal(5, descriptors.Count);
        Assert.All(descriptors, d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
    }

    [Fact]
    public void AddDocumentExtractionFlow_rejects_a_null_collection()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DocumentExtractionFlowServiceCollectionExtensions.AddDocumentExtractionFlow(null!));
    }
}
