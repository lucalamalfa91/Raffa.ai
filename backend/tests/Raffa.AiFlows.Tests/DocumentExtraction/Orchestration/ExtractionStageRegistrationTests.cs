using Microsoft.Extensions.DependencyInjection;
using Raffa.AiFlows.DocumentExtraction;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.AiFlows.DocumentExtraction.Stages;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Infrastructure.Extraction;
using Raffa.SharedKernel.Tenancy;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.Tests.DocumentExtraction.Orchestration;

/// <summary>
/// F5 PR-2: the seven stages are registered once each (idempotently), their names are the contract with
/// the fixture extractor and every scripted gateway, and the pipeline version stored on job rows is pinned.
/// </summary>
public sealed class ExtractionStageRegistrationTests
{
    [Fact]
    public void AddDocumentExtractionFlow_registers_exactly_one_singleton_stage_per_pipeline_stage()
    {
        var services = new ServiceCollection().AddDocumentExtractionFlow();

        var descriptors = services.Where(d => d.ServiceType == typeof(IExtractionStage)).ToList();

        Assert.Equal(ExtractionPipeline.Stages.Count, descriptors.Count);
        Assert.All(descriptors, d => Assert.Equal(ServiceLifetime.Singleton, d.Lifetime));

        using var provider = services.BuildServiceProvider();
        var stages = provider.GetServices<IExtractionStage>().ToList();
        Assert.Equal(ExtractionPipeline.Stages.OrderBy(s => s), stages.Select(s => s.Stage).OrderBy(s => s));
        Assert.Equal(7, stages.Select(s => s.Stage).Distinct().Count());
    }

    [Fact]
    public void Calling_the_registration_twice_still_yields_seven_stages()
    {
        var services = new ServiceCollection().AddDocumentExtractionFlow().AddDocumentExtractionFlow();

        using var provider = services.BuildServiceProvider();

        Assert.Equal(7, provider.GetServices<IExtractionStage>().Count());
    }

    [Fact]
    public void The_orchestrator_is_registered_scoped()
    {
        var services = new ServiceCollection().AddDocumentExtractionFlow();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ExtractionOrchestrator));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void Stage_names_and_pipeline_version_are_contracts()
    {
        Assert.Equal("staged-v1", ExtractionInputHash.PipelineVersion);

        foreach (var stage in ExtractionStages.Default)
        {
            Assert.Equal(stage.Stage.ToString(), stage.StageName);
            // F5-T05: only the four list stages declare dependencies (the three scalar stages); covered in
            // StageSchedulerTests. Declared, but inert unless UseContextDependencies is on.
            Assert.Equal(
                stage.Stage is ExtractionStage.Metadata or ExtractionStage.CommercialTerms or ExtractionStage.DatesAndRenewalTerms,
                stage.DependsOn.Count == 0);
        }

        Assert.Equal(
            ["Metadata", "CommercialTerms", "DatesAndRenewalTerms", "LineItems", "LegalClauses", "Obligations", "Risk"],
            ExtractionStages.Default.Select(s => s.StageName));
        Assert.Equal(ExtractionPipeline.Stages, ExtractionStages.Default.Select(s => s.Stage));
    }

    [Fact]
    public void The_orchestrator_refuses_a_stage_set_with_a_missing_or_duplicated_stage()
    {
        var missing = ExtractionStages.Default.Where(s => s.Stage != ExtractionStage.Risk).ToList();
        var duplicated = ExtractionStages.Default.Append(new MetadataStage()).ToList();

        var tenantContext = new TenantContext();
        var store = new ExtractionRunStore(
            InMemoryDocumentsDb.Create(Guid.NewGuid().ToString("N")), tenantContext, new FixedClock(DateTimeOffset.UnixEpoch), new RecordingAuditWriter());
        var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(DateTimeOffset.UnixEpoch));

        Assert.Throws<InvalidOperationException>(() => new ExtractionOrchestrator(store, gateway, missing, tenantContext));
        Assert.Throws<InvalidOperationException>(() => new ExtractionOrchestrator(store, gateway, duplicated, tenantContext));
    }
}
