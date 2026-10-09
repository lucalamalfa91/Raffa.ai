using Raffa.AiFlows.DocumentExtraction.Admission;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.AiFlows.Shared.Parsing;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Extraction;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// Composition proof for the document-extraction flow (F5) in the API host. The content gate and
/// the document-processing orchestrator moved out of <c>Raffa.Documents.Contracts</c> into
/// <c>Raffa.AiFlows</c>; the module now only defines the ports
/// (<see cref="IDocumentAdmissionEvaluator"/>, <see cref="IDocumentProcessingFlow"/>) and registers
/// no implementation of them. If <c>Program.cs</c> stopped calling <c>AddAiFlows()</c>, nothing would
/// fail to compile, and the first request to need a port would only fail at run time -- so this
/// resolves both ports (and the concrete types the integration tests ask for) out of the host's
/// real service provider, the same "not just wired in isolation" proof
/// <see cref="DeployableApiTests"/> gives the modules.
/// </summary>
public sealed class DocumentExtractionFlowCompositionTests : IClassFixture<RaffaApiFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DocumentExtractionFlowCompositionTests(RaffaApiFactory factory)
    {
        // Same syntactically valid connection strings DeployableApiTests uses: nothing below opens a
        // connection, so no Postgres is required.
        _factory = factory.WithWebHostBuilder(builder =>
        {
            const string connectionString =
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true";
            builder.UseSetting("ConnectionStrings:DocumentsContracts", connectionString);
            builder.UseSetting("ConnectionStrings:Chat", connectionString);
        });
    }

    [Fact]
    public void Host_resolves_the_admission_port_to_the_flows_gate()
    {
        using var scope = _factory.Services.CreateScope();

        var port = scope.ServiceProvider.GetRequiredService<IDocumentAdmissionEvaluator>();

        Assert.IsType<DocumentAdmissionGate>(port);
        Assert.Same(port, scope.ServiceProvider.GetRequiredService<DocumentAdmissionGate>());
    }

    [Fact]
    public void Host_resolves_the_processing_port_to_the_flows_orchestrator()
    {
        using var scope = _factory.Services.CreateScope();

        var port = scope.ServiceProvider.GetRequiredService<IDocumentProcessingFlow>();

        Assert.IsType<DocumentProcessingOrchestrator>(port);
        Assert.Same(port, scope.ServiceProvider.GetRequiredService<DocumentProcessingOrchestrator>());
    }

    [Fact]
    public void Host_resolves_the_hybrid_parser_the_quote_pipeline_and_the_gate_share()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<HybridDocumentParsingService>());
    }
}
