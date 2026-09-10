using Raffa.Renewals.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Raffa.Worker.Scheduling;

/// <summary>
/// The Worker host's daily renewal-threshold cadence (task E03/F02/US01/T01, parent story
/// us-01-threshold-scheduler: "a daily scheduler that fires renewal/cancellation threshold
/// events"). Internal: this is host wiring, not a public API surface — enforced by
/// <c>Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types</c>,
/// mirroring <c>Raffa.Worker.Queue.QueueConsumerHostedService</c>'s own internal visibility.
///
/// On every tick: asks <see cref="IActiveRenewalContractsSource"/> which tenants currently have
/// contracts to evaluate, then calls
/// <see cref="RenewalThresholdScheduler.EvaluateThresholdsAsync"/> once per tenant batch. Resolves
/// <see cref="RenewalThresholdScheduler"/> from a fresh <see cref="IServiceScope"/> per tick rather
/// than taking it as a constructor dependency: hosted services are registered Singleton, but
/// <see cref="RenewalThresholdScheduler"/> is Scoped (it depends on the Scoped
/// <see cref="Raffa.SharedKernel.IAuditWriter"/>) — injecting it directly would be the captive-
/// dependency violation <c>ValidateScopes</c> exists to catch; the
/// <c>IServiceScopeFactory</c>-per-tick shape below is the standard .NET answer to "a singleton
/// background service needs a scoped service", the same pattern this codebase has not needed until
/// this task (<see cref="Queue.QueueConsumerHostedService"/> never dispatches to a scoped domain
/// handler yet — see that type's own doc comment).
///
/// Task E03/F02/US01/T02 (renewal-alerts): every tick's raised events are now also handed to
/// <see cref="Raffa.Renewals.Application.RenewalAlertService.CreateFromEventsAsync"/> (same
/// per-tick <see cref="IServiceScope"/>, same Scoped-dependency reasoning), so a real threshold
/// crossing leaves a durable, de-duplicated <see cref="Raffa.Renewals.Domain.RenewalAlert"/> row —
/// not just the audit-only event <see cref="RenewalThresholdScheduler.EvaluateThresholdsAsync"/>
/// already wrote — closing the gap that method's own doc comment named ("de-duplicating which
/// alerts already exist for a threshold is parent story task-02's job").
///
/// A failed tick is logged and does not stop the loop — the next tick tries again on schedule,
/// mirroring <see cref="Queue.QueueConsumerHostedService"/>'s own catch-log-continue shape for a
/// single failed message.
/// </summary>
internal sealed class RenewalThresholdSchedulerHostedService(
    IServiceScopeFactory scopeFactory,
    IActiveRenewalContractsSource contractsSource,
    RenewalThresholdSchedulerOptions options,
    ILogger<RenewalThresholdSchedulerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);

            try
            {
                await Task.Delay(options.Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutting down between ticks — fall through and let the loop condition exit.
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var batches = await contractsSource.GetActiveContractsAsync(cancellationToken).ConfigureAwait(false);

            using var scope = scopeFactory.CreateScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<RenewalThresholdScheduler>();
            var alertService = scope.ServiceProvider.GetRequiredService<RenewalAlertService>();

            foreach (var batch in batches)
            {
                var events = await scheduler
                    .EvaluateThresholdsAsync(batch.TenantId, batch.Contracts, cancellationToken)
                    .ConfigureAwait(false);

                logger.LogInformation(
                    "Renewal threshold scheduler emitted {Count} renewal.approaching event(s) for tenant {TenantId}",
                    events.Count, batch.TenantId);

                // Task E03/F02/US01/T02 (renewal-alerts): persist a de-duplicated alert per
                // genuinely new (contract, milestone, thresholdDays) match (class doc comment).
                var alerts = await alertService
                    .CreateFromEventsAsync(batch.TenantId, events, cancellationToken)
                    .ConfigureAwait(false);

                logger.LogInformation(
                    "Renewal alert service created {Count} renewal alert(s) for tenant {TenantId}",
                    alerts.Count, batch.TenantId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Renewal threshold scheduler run failed");
        }
    }
}
