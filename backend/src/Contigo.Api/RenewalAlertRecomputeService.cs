using Contigo.Documents.Contracts.Infrastructure;
using Contigo.Renewals.Application;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Contigo.Api;

/// <summary>
/// Orchestrator for task E03/F02/US01/T02 (renewal-alerts; parent story us-01-threshold-scheduler
/// AC-3: "Scheduler recomputes when a contract/term is corrected"). Same shape as
/// <see cref="NegotiationOutcomePropagationService"/>: the one place in the solution that calls both
/// <c>Contigo.Documents.Contracts</c> (to re-read the just-corrected <c>Contract</c> row) and
/// <c>Contigo.Renewals</c> (to recompute its alerts) — ADR-002's dependency-direction rule
/// (<c>Contigo.ArchitectureTests.DependencyDirectionTests</c>) allows neither module to reference the
/// other, only <c>Contigo.Api</c>, the composition root, sees every module at once (backend/README.md's
/// own "Dependency direction" section). <c>internal</c>, not <c>public</c>: this is host-composition
/// wiring, not a domain module's own public API surface — enforced by
/// <c>Contigo.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types</c>, the
/// same treatment <see cref="NegotiationOutcomePropagationService"/>/<c>QuoteExtractionPipeline</c>
/// already get.
///
/// <para>
/// <b>Called synchronously, in the same request</b>: <see cref="ContractsEndpointExtensions"/>'s
/// `PATCH /api/contracts/{id}` handler calls <see cref="RecomputeAsync"/> right after
/// <c>ContractCorrectionService.CorrectAsync</c> durably commits — the same "smallest honest way to
/// make the promise actually resolve today" posture <see cref="NegotiationOutcomePropagationService"/>
/// already takes, rather than deferring to a queue/mediator that does not exist yet (ADR-002 leaves
/// that choice council-owned — see <c>RenewalApproachingEvent</c>'s own doc comment).
/// </para>
///
/// <para>
/// <b>Re-reads rather than reuses the correction's own result</b>:
/// <c>ContractCorrectionService.CorrectAsync</c> returns a small
/// <c>Contigo.Documents.Contracts.Application.ContractCorrectionResult</c> (id/version/corrected
/// field names/timestamp only — no <c>EndDate</c>/<c>AutoRenewal</c>), and by the time control
/// returns here that method's own <see cref="ITenantContext.BeginScope"/> has already been disposed
/// (a <see langword="using"/> local to that method) — so this type opens its own new scope
/// (<see cref="ITenantContext"/>'s own doc comment: nested/sequential scopes are supported) and reads
/// the <c>Contract</c> row fresh, the same "re-read via the DbContext, do not thread extra fields
/// through a DTO built for a different purpose" choice
/// <see cref="NegotiationOutcomePropagationService"/> already makes for
/// <c>Domain.NegotiationOutcome</c>.
/// </para>
///
/// <para>
/// <b>Only recomputes when it could matter</b>: <see cref="RenewalRelevantFields"/> is exactly the
/// two <c>Contract</c> fields <see cref="ContractRenewalTerms"/>/<see cref="RenewalEngine.Calculate"/>
/// actually consume today (<c>endDate</c>, <c>autoRenewal</c>) — not every
/// <c>ContractCorrectionService.CorrectableFieldNames</c> entry. <c>cancellationDeadline</c> is
/// deliberately excluded: <see cref="RenewalEngine"/> derives its own cancellation deadline from
/// <c>EndDate</c> minus <see cref="ContractRenewalTerms.CancellationNoticeDays"/> (always
/// <see langword="null"/> today — the same honest gap
/// <see cref="Contigo.Api.RenewalsEndpointExtensions"/>'s own <c>ComputePriority</c> already
/// documents), never the raw, independently-extracted <c>Contract.CancellationDeadline</c> fact —
/// correcting that field would not change anything this recompute could observe, so treating it as
/// "renewal-relevant" would be misleading, not merely unnecessary. Deciding here, before calling
/// <see cref="RenewalAlertService"/> at all, means an unrelated correction (for example
/// <c>governingLaw</c>) costs this endpoint nothing beyond the field-name check already in hand.
/// </para>
///
/// <para>
/// <b>Never fails an already-durable correction</b>: unlike
/// <see cref="NegotiationOutcomePropagationService.PropagateAsync"/> (which can fail an expected,
/// validate-able way — an unknown cross-aggregate id — and reports that honestly as a
/// <see cref="Result{T}"/>), every input this method touches was already validated to exist by the
/// correction call that precedes it in the same request; there is no expected failure mode of its
/// own to model as a <see cref="Result{T}"/>. A genuine, unexpected failure (for example a dropped
/// connection) is allowed to throw and fail the request — the same "audit/compliance-adjacent side
/// effect is not a best-effort side-channel" posture
/// <c>ContractCorrectionService.CorrectAsync</c>'s own audit write already takes, reproduced here
/// because <see cref="RenewalAlertService"/>'s own writes are exactly that kind of side effect.
/// </para>
/// </summary>
internal sealed class RenewalAlertRecomputeService(
    DocumentsContractsDbContext documentsContractsDbContext,
    RenewalAlertService renewalAlertService,
    ITenantContext tenantContext)
{
    /// <summary>The only <c>ContractCorrectionService.CorrectableFieldNames</c> entries that can
    /// change what <see cref="RenewalEngine.Calculate"/> computes for this contract — see the type
    /// doc comment's "Only recomputes when it could matter" section.</summary>
    public static readonly IReadOnlyCollection<string> RenewalRelevantFields =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "endDate", "autoRenewal" };

    /// <summary>
    /// Re-reads the tenant-scoped <c>Contract</c> named by <paramref name="contractId"/> and, when
    /// it still exists, maps it onto <see cref="ContractRenewalTerms"/> (<c>CancellationNoticeDays</c>
    /// left honestly <see langword="null"/> — see the type doc comment) and calls
    /// <see cref="RenewalAlertService.RecomputeForContractAsync"/>. Returns <see langword="null"/>
    /// when the contract no longer exists for this tenant — should not happen on this method's only
    /// call site (the same contract was just successfully corrected, in this same request, for this
    /// same tenant), kept as an honest null rather than an assumed-safe throw, the same defensive
    /// posture <see cref="NegotiationOutcomePropagationService"/>'s own not-found handling takes.
    /// </summary>
    public async Task<RenewalAlertRecomputeResult?> RecomputeAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var contract = await documentsContractsDbContext.Contracts
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == contractId, cancellationToken)
            .ConfigureAwait(false);

        if (contract is null)
        {
            return null;
        }

        var terms = new ContractRenewalTerms(
            contract.Id, contract.EndDate, contract.AutoRenewal, CancellationNoticeDays: null);

        return await renewalAlertService
            .RecomputeForContractAsync(tenantId, terms, cancellationToken)
            .ConfigureAwait(false);
    }
}
