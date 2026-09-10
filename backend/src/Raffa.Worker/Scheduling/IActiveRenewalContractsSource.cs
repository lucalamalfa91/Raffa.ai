using Raffa.Renewals.Application;
using Raffa.SharedKernel;

namespace Raffa.Worker.Scheduling;

/// <summary>
/// Host-side port <see cref="RenewalThresholdSchedulerHostedService"/> drives to find out which
/// tenants' active contracts to run <see cref="Raffa.Renewals.Application.RenewalThresholdScheduler"/>
/// against on each daily tick (task E03/F02/US01/T01, parent story us-01-threshold-scheduler;
/// ADR-002: "queue message handlers belong to the worker host, not to domain projects" — the same
/// reasoning applies to a timer-driven job's own data source). Internal: this is host-composition
/// wiring, not a public API surface — enforced by
/// <c>Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types</c>, and
/// by design <c>Raffa.Renewals</c> never receives from this port directly (it only ever sees the
/// <see cref="ContractRenewalTerms"/> the host already assembled for one tenant at a time).
///
/// R0/R2 placeholder for the concrete adapter, same shape as the
/// <c>Raffa.Worker.Queue.IQueueConsumer</c> "R0 placeholder" pattern: enumerating every tenant's
/// active contracts requires a cross-tenant workspace listing (<c>Raffa.Identity.Workspace</c>)
/// plus a per-tenant, RLS-scoped contract query (<c>Raffa.Documents.Contracts</c>) — the former
/// is not referenced by <c>Raffa.Worker.csproj</c> today, so wiring a real implementation is
/// follow-up composition work, not a redesign of this interface or of
/// <see cref="RenewalThresholdSchedulerHostedService"/>'s own timer loop, which is real and proven
/// end to end today against <see cref="NoActiveRenewalContractsSource"/>.
/// </summary>
internal interface IActiveRenewalContractsSource
{
    /// <summary>One batch of contract terms per tenant that currently has any "active" contracts to
    /// evaluate. An empty result is a legitimate answer (no source wired yet, or a tenant with
    /// nothing due), not an error.</summary>
    Task<IReadOnlyList<TenantRenewalContracts>> GetActiveContractsAsync(CancellationToken cancellationToken = default);
}

/// <summary>One tenant's worth of <see cref="ContractRenewalTerms"/> for one scheduler run — the
/// exact shape <see cref="Raffa.Renewals.Application.RenewalThresholdScheduler.EvaluateThresholdsAsync"/>
/// takes (ADR-009: one tenant context per call).</summary>
internal sealed record TenantRenewalContracts(TenantId TenantId, IReadOnlyList<ContractRenewalTerms> Contracts);

/// <summary>
/// Default <see cref="IActiveRenewalContractsSource"/>: always empty. Honest about today's real gap
/// (see this file's own interface doc comment) rather than fabricating tenants/contracts that do
/// not exist — <see cref="RenewalThresholdSchedulerHostedService"/> still runs on its configured
/// interval and calls this on every tick, it simply has nothing to evaluate until a real adapter
/// replaces this registration.
/// </summary>
internal sealed class NoActiveRenewalContractsSource : IActiveRenewalContractsSource
{
    public Task<IReadOnlyList<TenantRenewalContracts>> GetActiveContractsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TenantRenewalContracts>>([]);
}
