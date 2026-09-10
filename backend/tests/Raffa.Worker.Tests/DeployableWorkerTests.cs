using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Renewals.Application;
using Raffa.SharedKernel.Tenancy;
using Raffa.Worker.Queue;
using Raffa.Worker.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Raffa.Worker.Tests;

/// <summary>
/// Proves the Definition of Done for task E01/F04/US04/T02 (deployable-worker, ADR-002): the
/// worker host actually boots as a composition root, references the same Documents/Contracts
/// application services the API host does (parent story us-04 AC-2 "Worker host references the
/// same application services"), and its background service really drains a message off the
/// queue end-to-end (AC-2 "... and consumes the queue") — not just left the "module registration
/// will go here" placeholder from the solution scaffold (E01/F04/US01/T01).
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
    public void Host_registers_the_queue_consumer_hosted_service()
    {
        using var host = BuildHost();

        // AC-2 ("... and consumes the queue"): a hosted service is actually registered to drive
        // the queue, not just the queue port sitting unused.
        var hostedServices = host.Services.GetServices<IHostedService>();

        Assert.Contains(hostedServices, service => service is QueueConsumerHostedService);
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
    public async Task Worker_consumes_a_queued_message_end_to_end()
    {
        using var host = BuildHost();
        var queueConsumer = host.Services.GetRequiredService<InMemoryQueueConsumer>();
        var message = new QueueMessage(Guid.NewGuid().ToString(), "extraction-job-queued");

        queueConsumer.Enqueue(message);

        await host.StartAsync();
        try
        {
            // QueueConsumerHostedService polls on a background thread (200ms delay between empty
            // receives); bound the wait instead of asserting immediately.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!queueConsumer.CompletedMessageIds.Contains(message.MessageId) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            Assert.Contains(message.MessageId, queueConsumer.CompletedMessageIds);
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
