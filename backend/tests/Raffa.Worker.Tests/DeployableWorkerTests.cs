using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Messaging;
using Raffa.Renewals.Application;
using Raffa.SharedKernel.Tenancy;
using Raffa.Worker.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Raffa.Worker.Tests;

/// <summary>
/// Proves the Definition of Done for task E01/F04/US04/T02 (deployable-worker, ADR-002): the
/// worker host actually boots as a composition root and references the same Documents/Contracts
/// application services the API host does (parent story us-04 AC-2 "Worker host references the
/// same application services") — not just left the "module registration will go here"
/// placeholder from the solution scaffold (E01/F04/US01/T01).
///
/// Task E19/F05/US01/T01 deleted this class's own R0 in-process-queue end-to-end proof along
/// with the dead <c>Raffa.Worker.Queue.QueueConsumerHostedService</c> it drove — a second, inert
/// <see cref="IHostedService"/> the Worker booted alongside the real ADR-027 extraction consumer
/// in every deployed process. <see cref="Host_registers_exactly_one_hosted_service_for_document_extraction"/>
/// proves the replacement at the registration level (AC-2 "... and consumes the queue"); the
/// end-to-end proof now lives in <c>Raffa.IntegrationTests</c>, against the real consumer.
/// </summary>
public sealed class DeployableWorkerTests
{
    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        // A syntactically valid Npgsql connection string satisfies AddDocumentsContractsModule's
        // eager UseNpgsql() parsing. Nothing below opens a real connection, so no running
        // Postgres instance is required for this test (same approach as
        // Raffa.Api.Tests.DeployableApiTests). Same connection string reused for all three
        // parameters, mirroring appsettings.Development.json's own single shared `raffa_dev`
        // database (ADR-003 "single system of record") — task E03/F03/US01/T02 (renewal-action)
        // added the third (Renewals) when AddRenewalsModule got its own first DbContext.
        const string connectionString =
            "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true";
        builder.Services.AddWorkerHost(connectionString, connectionString, connectionString);

        return builder.Build();
    }

    [Fact]
    public void Host_composes_the_documents_contracts_module_into_di()
    {
        using var host = BuildHost();
        using var scope = host.Services.CreateScope();

        // AC-2 ("Worker host references the same application services"): resolve the same
        // module DbContext and shared tenant claim the API host composes
        // (Raffa.Api.Tests.DeployableApiTests.Host_composes_the_documents_contracts_module_into_di)
        // out of the worker's own real service provider, not a hand-rolled stand-in container.
        var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        Assert.NotNull(dbContext);
        Assert.NotNull(tenantContext);
    }

    [Fact]
    public void Host_composes_the_renewals_module_into_di()
    {
        using var host = BuildHost();
        using var scope = host.Services.CreateScope();

        // Task E03/F02/US01/T01 (threshold-scheduler): AddWorkerHost now calls AddRenewalsModule
        // for real (RenewalEngine's own doc comment named this task as one of the first host
        // callers) — resolve the Scoped RenewalThresholdScheduler out of the worker's own real
        // service provider, proving it has no captive/unresolvable dependency (it needs
        // IAuditWriter, which AddAuditModule above must have already registered).
        var scheduler = scope.ServiceProvider.GetRequiredService<RenewalThresholdScheduler>();

        Assert.NotNull(scheduler);
    }

    [Fact]
    public void Host_registers_the_renewal_threshold_scheduler_hosted_service()
    {
        using var host = BuildHost();

        // Parent story us-01-threshold-scheduler: "a daily scheduler" — a hosted service is
        // actually registered to drive it, not just RenewalThresholdScheduler sitting unused.
        var hostedServices = host.Services.GetServices<IHostedService>();

        Assert.Contains(hostedServices, service => service is RenewalThresholdSchedulerHostedService);
    }

    [Fact]
    public void Host_registers_exactly_one_hosted_service_for_document_extraction()
    {
        // Task E19/F05/US01/T01 (parent story AC-1): AddWorkerHost used to also register the dead
        // R0 queue's QueueConsumerHostedService -- a second, inert IHostedService alongside the
        // real ADR-027 extraction consumer in every deployed Worker process. That whole R0 queue
        // port and its two implementations are deleted; this wires the host the same way
        // Program.cs does -- AddWorkerHost, then the extraction-queue publisher/consumer pair
        // (ADR-027 D2) -- and proves exactly one hosted service resolves for document extraction.
        var builder = Host.CreateApplicationBuilder();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        const string connectionString =
            "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true";

        builder.Services.AddWorkerHost(connectionString, connectionString, connectionString);
        builder.Services.AddExtractionQueuePublisher(configuration);
        builder.Services.AddExtractionQueueConsumer(configuration);
        builder.Services.AddEnrichQueuePublisher(configuration);

        using var host = builder.Build();
        var hostedServices = host.Services.GetServices<IHostedService>().ToList();

        // Exactly three hosted services boot out of this whole composition (instant-identity-ingest
        // ADR-027 §D two-queue split): the renewal-threshold scheduler, one extraction-intake
        // consumer, and one enrich consumer. Asserting the total catches any duplicate/dead service
        // coming back -- not just a mismatch against these three names.
        Assert.Equal(3, hostedServices.Count);
        Assert.Contains(hostedServices, service => service is RenewalThresholdSchedulerHostedService);

        // No ServiceBus:FullyQualifiedNamespace configured -> the in-process consumers fire for
        // both the intake and enrich queues (ExtractionTransportSelectionTests proves the Service
        // Bus branch at the registration level).
        Assert.Equal(1, hostedServices.Count(service =>
            service is InMemoryExtractionConsumerHostedService or ServiceBusExtractionConsumerHostedService));
        Assert.Equal(1, hostedServices.Count(service =>
            service is InMemoryEnrichConsumerHostedService));
    }
}
