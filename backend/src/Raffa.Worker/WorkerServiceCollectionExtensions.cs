using Raffa.Audit.Infrastructure;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Renewals.Infrastructure;
using Raffa.Worker.Queue;
using Raffa.Worker.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Worker;

/// <summary>
/// Composition-root wiring for the Worker host (ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method", applied here to the host itself). Single source
/// of truth for what <c>Program.cs</c> registers — Raffa.Worker.Tests calls this same method,
/// so the deployable-worker tests prove what the host actually boots with, not a parallel
/// hand-rolled container (mirrors Raffa.Api.Tests.DeployableApiTests' approach for the API
/// host).
/// </summary>
public static class WorkerServiceCollectionExtensions
{
    /// <summary>
    /// Wires the Worker host: the same Documents/Contracts application services the API host
    /// composes (parent story us-04 AC-2 "references the same application services"), plus the
    /// queue-consumption hosted service (AC-2 "... and consumes the queue"; ADR-002 "queue
    /// message handlers belong to the worker host, not to domain projects").
    ///
    /// Also wires the Audit module (task E01/F09/US01/T01, r0-integration): task E01/F06/US01/T01
    /// gave <c>DocumentUploadService</c> a required <c>IAuditWriter</c> dependency, and
    /// <c>AddDocumentsContractsModule</c> registers that service in *any* host that calls it —
    /// including this one (<c>Raffa.Worker.csproj</c> already referenced <c>Raffa.Audit</c>
    /// before this task, anticipating exactly this). Without this call, the Worker's container
    /// would hold a registered <c>DocumentUploadService</c> whose own dependency graph can never
    /// resolve — harmless while nothing in this host asks for it (today), but a landmine under
    /// any host-builder configuration that validates the DI graph eagerly
    /// (<c>ServiceProviderOptions.ValidateOnBuild</c>, on by default whenever
    /// <c>DOTNET_ENVIRONMENT=Development</c>). Keeping both hosts' module composition in lockstep
    /// is also what this method's own doc comment already promises ("the same application
    /// services") — future worker jobs (extraction, renewal recompute) will need
    /// <c>IAuditWriter</c> for their own audit writes anyway (ADR-011).
    ///
    /// Task E03/F02/US01/T01 (threshold-scheduler): also wires the Renewals module — the first
    /// real host caller <c>Raffa.Renewals.Infrastructure.ServiceCollectionExtensions
    /// .AddRenewalsModule</c>'s own doc comment anticipated — plus this host's own daily-cadence
    /// hosted service (<see cref="RenewalThresholdSchedulerHostedService"/>) and its
    /// <see cref="IActiveRenewalContractsSource"/> port. <c>AddAuditModule</c> above must run
    /// first (or at all) for <c>RenewalThresholdScheduler</c>'s own <c>IAuditWriter</c> dependency
    /// to resolve — the identical landmine this doc comment already flags for
    /// <c>DocumentUploadService</c>.
    ///
    /// Task E03/F03/US01/T02 (renewal-action): <c>AddRenewalsModule</c> now takes a connection
    /// string (this module's first <c>DbContext</c>) — <paramref name="renewalsConnectionString"/>
    /// is that parameter, threaded through the same way <paramref name="documentsContractsConnectionString"/>/
    /// <paramref name="auditConnectionString"/> already are. No worker job resolves
    /// <c>RenewalActionService</c> today (only <c>Raffa.Api</c>'s `POST /api/renewals/{id}/action`
    /// does), but this host must still supply a valid connection string the moment
    /// <c>AddDbContext</c>'s eager <c>UseNpgsql()</c> parsing runs during <c>AddRenewalsModule</c>
    /// itself, not lazily on first use.
    /// </summary>
    public static IServiceCollection AddWorkerHost(
        this IServiceCollection services,
        string documentsContractsConnectionString,
        string auditConnectionString,
        string renewalsConnectionString)
    {
        services.AddDocumentsContractsModule(documentsContractsConnectionString);
        services.AddAuditModule(auditConnectionString);
        services.AddRenewalsModule(renewalsConnectionString);

        services.AddSingleton<InMemoryQueueConsumer>();
        services.AddSingleton<IQueueConsumer>(sp => sp.GetRequiredService<InMemoryQueueConsumer>());
        services.AddHostedService<QueueConsumerHostedService>();

        // TryAdd: Raffa.Worker.Tests pre-registers a fake IActiveRenewalContractsSource /
        // millisecond-scale RenewalThresholdSchedulerOptions before calling AddWorkerHost (mirrors
        // Raffa.Renewals.Tests.ServiceCollectionExtensionsTests' identical override-before-
        // registering proof for IClock) — the first registration must win here for that to work.
        services.TryAddSingleton<IActiveRenewalContractsSource, NoActiveRenewalContractsSource>();
        services.TryAddSingleton(sp =>
        {
            var options = new RenewalThresholdSchedulerOptions();
            sp.GetRequiredService<IConfiguration>()
                .GetSection(RenewalThresholdSchedulerOptions.SectionName)
                .Bind(options);
            return options;
        });
        services.AddHostedService<RenewalThresholdSchedulerHostedService>();

        return services;
    }
}
