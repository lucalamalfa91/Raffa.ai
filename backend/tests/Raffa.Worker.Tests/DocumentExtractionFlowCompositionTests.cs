using Raffa.AiFlows.DocumentExtraction.Admission;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Messaging;
using Raffa.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Raffa.Worker.Tests;

/// <summary>
/// Composition proof for the document-extraction flow (F5) in the Worker host. The Worker is the
/// process that runs the content gate and the document-processing orchestrator: its
/// <see cref="ExtractionRequestedHandler"/> receives the two ports
/// (<see cref="IDocumentAdmissionEvaluator"/>, <see cref="IDocumentProcessingFlow"/>) the
/// <c>Raffa.Documents.Contracts</c> module defines but no longer implements. The handler resolves
/// its ports only when the first message arrives, so a host that forgot <c>AddAiFlows()</c> would
/// boot green and fail on the first upload -- this builds the host the way <c>Program.cs</c> does
/// and resolves the handler out of it.
/// </summary>
public sealed class DocumentExtractionFlowCompositionTests
{
    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        const string connectionString =
            "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true";

        // The same sequence Raffa.Worker/Program.cs runs: AddWorkerHost (modules + AddAiFlows), the
        // blob adapter and the extraction-queue pair. The blob client is constructed lazily and never
        // reaches the network here.
        builder.Services.AddWorkerHost(connectionString, connectionString, connectionString);
        builder.Services.AddAzureBlobDocumentStorage("UseDevelopmentStorage=true");
        builder.Services.AddExtractionQueuePublisher(configuration);
        builder.Services.AddExtractionQueueConsumer(configuration);

        return builder.Build();
    }

    [Fact]
    public void Worker_resolves_the_extraction_handler_with_both_ports_provided_by_the_flows_project()
    {
        using var host = BuildHost();
        using var scope = host.Services.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<ExtractionRequestedHandler>();

        Assert.NotNull(handler);
        Assert.IsType<DocumentAdmissionGate>(scope.ServiceProvider.GetRequiredService<IDocumentAdmissionEvaluator>());
        Assert.IsType<DocumentProcessingOrchestrator>(scope.ServiceProvider.GetRequiredService<IDocumentProcessingFlow>());
    }

    [Fact]
    public void Worker_resolves_both_ports_to_the_scoped_concrete_instances()
    {
        using var host = BuildHost();
        using var scope = host.Services.CreateScope();

        Assert.Same(
            scope.ServiceProvider.GetRequiredService<IDocumentAdmissionEvaluator>(),
            scope.ServiceProvider.GetRequiredService<DocumentAdmissionGate>());
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<IDocumentProcessingFlow>(),
            scope.ServiceProvider.GetRequiredService<DocumentProcessingOrchestrator>());
    }
}
