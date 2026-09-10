using Raffa.Quotes.Infrastructure;
using Raffa.Savings.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Api;

/// <summary>
/// Orchestrator for task E05/F03/US02/T02 (outcome-propagation; parent story us-02-outcome-capture
/// AC-2 "Realized savings surface on the savings dashboard (cross-wave)"; product spec Appendix B
/// event "<c>negotiation.completed</c> | Negotiation module | Update realized savings/data
/// flywheel"). This is the one place in the solution that calls both <c>Raffa.Quotes</c> (to read
/// the just-captured <c>Domain.NegotiationOutcome</c>) and <c>Raffa.Savings</c> (to write the
/// realized value onto a tracked <c>Domain.SavingsOpportunity</c>): ADR-002's dependency-direction
/// rule (<c>Raffa.ArchitectureTests.DependencyDirectionTests</c>) allows neither module to
/// reference the other — only <c>Raffa.Api</c>, the composition root, is allowed to see every
/// module at once (backend/README.md's own "Dependency direction" section). <c>internal</c>, not
/// <c>public</c>: this is host-composition wiring, not a domain module's own public API surface —
/// enforced by <c>Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types</c>,
/// the same treatment <c>QuoteExtractionPipeline</c> already gets for the identical reason.
///
/// <para>
/// <b>No mediator, no event bus (yet)</b>: spec Appendix B frames this as an event
/// (<c>negotiation.completed</c>) with a downstream consumer/action ("Update realized
/// savings/data flywheel"), and <c>Raffa.SharedKernel.DomainEvent</c> is a pre-existing
/// scaffold for that — but no in-process mediator exists anywhere in this codebase yet
/// (<c>Raffa.Renewals.Application.RenewalApproachingEvent</c>'s own doc comment: "ADR-002 leaves
/// the mediator/DI pattern an open, council-owned choice"). Unlike that event — which only makes
/// itself durable as an audit entry and defers the actual consumer to a later task — this task's own
/// wave-spec artifact (<c>outcome-propagation</c>) is exactly that consumer, so it does not defer:
/// <see cref="PropagateAsync"/> is called synchronously, in the same
/// `POST /api/negotiations/outcomes` request, right after
/// <c>Raffa.Quotes.Application.Outcome.NegotiationOutcomeService.CaptureAsync</c> returns (see
/// <see cref="NegotiationsEndpointExtensions"/>) — the same "smallest honest way to make the promise
/// actually resolve today" posture <c>QuoteExtractionPipeline</c>/<c>DocumentProcessingPipeline</c>
/// already take for their own synchronous, in-request pipelines.
/// </para>
///
/// <para>
/// <b>Never fails an already-durable capture</b>: by the time this runs, the
/// <c>NegotiationOutcome</c> row is already committed and its own audit entry
/// (<c>negotiation_outcome.captured</c>) already written — a negotiated outcome is exactly the kind
/// of consequential fact Appendix C rule 9 says must be captured "from day one", so a failure here
/// (unknown <c>savingsOpportunityId</c>, or a negative <c>RealizedSaving</c> that
/// <see cref="SavingsOpportunityService.UpdateAsync"/>'s own
/// <see cref="SavingsOpportunityService.RealizedAmountMustBeNonNegativeError"/> rightly rejects —
/// see this type's own <see cref="PropagateAsync"/> doc comment) must never unwind or fail the
/// capture itself. <see cref="NegotiationsEndpointExtensions"/> reports this method's own
/// <see cref="Result{T}"/> honestly in the HTTP response instead (mirrors <c>Program.cs</c>'s own
/// `POST /api/documents` handler: "A pipeline failure is reported honestly in the response... but
/// never turns an already-successful upload into an HTTP error").
/// </para>
///
/// <para>
/// <b>No currency reconciliation</b>: <c>Domain.NegotiationOutcome</c> carries no currency of its
/// own (unlike every other money value in this codebase — see that type's own doc comment), and
/// <see cref="SavingsOpportunityService.UpdateAsync"/>'s own pre-existing, already-shipped
/// <c>realizedAmount</c> parameter (task E04/F02/US02/T02) has never reconciled the figure a caller
/// supplies against any other currency either — a human calling `PATCH /api/savings/{id}` directly
/// is trusted the same way. This method holds the identical, already-accepted trust assumption for
/// its own automated caller rather than inventing a new cross-cutting validation rule this codebase
/// does not have anywhere else (KB contract: "Do not invent extra locked platform rules").
/// </para>
/// </summary>
internal sealed class NegotiationOutcomePropagationService(
    QuotesDbContext quotesDbContext,
    SavingsOpportunityService savingsOpportunityService,
    ITenantContext tenantContext,
    IClock clock,
    IAuditWriter auditWriter)
{
    /// <summary>Returned by <see cref="PropagateAsync"/> when <paramref name="negotiationOutcomeId"/>
    /// (below) does not name a <c>Domain.NegotiationOutcome</c> for the caller's tenant — should not
    /// happen on the only call site (<see cref="NegotiationsEndpointExtensions"/> calls this with the
    /// id of the row it just persisted, in the same tenant scope), kept as an honest
    /// <see cref="Result{T}"/> failure rather than an assumed-safe throw all the same, the same
    /// defensive posture every other tenant-scoped lookup in this codebase takes.</summary>
    public const string NegotiationOutcomeNotFoundError = "Negotiation outcome not found.";

    /// <summary>Returned by <see cref="PropagateAsync"/> when
    /// <see cref="SavingsOpportunityService.UpdateAsync"/> reports
    /// <see cref="SavingsOpportunityService.NotFoundError"/> — <paramref name="savingsOpportunityId"/>
    /// does not name a <c>Domain.SavingsOpportunity</c> for this tenant. Re-exposed under this type's
    /// own name (rather than the string this module's own dependency cannot even reference at compile
    /// time) so <see cref="NegotiationsEndpointExtensions"/> can map it to 404 the same
    /// <see cref="Result{T}"/>-sentinel convention every other endpoint in this host already
    /// uses.</summary>
    public const string SavingsOpportunityNotFoundError = "Savings opportunity not found.";

    private const string AuditPropagatedAction = "negotiation_outcome.propagated";
    private const string AuditResourceType = "negotiation_outcome";

    /// <summary>Same interim actor placeholder as every other automated write in this host (ADR-010
    /// is not wired in yet) — see <c>QuoteExtractionPipeline.SystemActor</c>'s own doc comment for
    /// why.</summary>
    private const string SystemActor = "system:negotiation-outcome-propagation";

    /// <summary>
    /// Reads the <c>Domain.NegotiationOutcome</c> named by <paramref name="negotiationOutcomeId"/>
    /// and writes its <c>RealizedSaving</c> onto the <c>Domain.SavingsOpportunity</c> named by
    /// <paramref name="savingsOpportunityId"/>, via <see cref="SavingsOpportunityService.UpdateAsync"/>
    /// — the exact same, already-tested realized-value write path a human PATCHing
    /// `/api/savings/{id}` directly already uses (task E04/F02/US02/T02), never a second,
    /// independent way to create a <c>Domain.RealizedSavings</c> row. That call also finalizes the
    /// opportunity's own <c>Status</c> as <c>Realized</c> and writes its own
    /// <c>savings_opportunity.realized</c> audit entry — this method adds one more, distinct entry
    /// (<see cref="AuditPropagatedAction"/>) recording the link between the two aggregate ids, the one
    /// fact neither existing audit entry captures on its own.
    /// </summary>
    public async Task<Result<NegotiationOutcomePropagationResult>> PropagateAsync(
        TenantId tenantId,
        EntityId negotiationOutcomeId,
        EntityId savingsOpportunityId,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var outcome = await quotesDbContext.NegotiationOutcomes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                o => o.TenantId == tenantId && o.Id == negotiationOutcomeId, cancellationToken)
            .ConfigureAwait(false);

        if (outcome is null)
        {
            return Result<NegotiationOutcomePropagationResult>.Failure(NegotiationOutcomeNotFoundError);
        }

        var updateResult = await savingsOpportunityService.UpdateAsync(
            tenantId,
            savingsOpportunityId,
            owner: null,
            status: null,
            realizedAmount: outcome.RealizedSaving,
            cancellationToken).ConfigureAwait(false);

        if (updateResult.IsFailure)
        {
            var error = string.Equals(
                updateResult.Error, SavingsOpportunityService.NotFoundError, StringComparison.Ordinal)
                ? SavingsOpportunityNotFoundError
                : updateResult.Error;
            return Result<NegotiationOutcomePropagationResult>.Failure(error);
        }

        var opportunity = updateResult.Value;
        var now = clock.UtcNow;

        // Recorded only once the propagating write itself is durable (same "write then audit"
        // placement as NegotiationOutcomeService.CaptureAsync's own audit write).
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                SystemActor,
                AuditPropagatedAction,
                AuditResourceType,
                negotiationOutcomeId.Value.ToString(),
                now,
                $"savingsOpportunityId={savingsOpportunityId} quoteId={outcome.QuoteId} " +
                $"realizedSaving={outcome.RealizedSaving} currency={opportunity.Currency}"),
            cancellationToken).ConfigureAwait(false);

        return Result<NegotiationOutcomePropagationResult>.Success(new NegotiationOutcomePropagationResult(
            negotiationOutcomeId,
            outcome.QuoteId,
            savingsOpportunityId,
            outcome.RealizedSaving,
            opportunity.Currency,
            now));
    }
}

/// <summary>Outcome of one <see cref="NegotiationOutcomePropagationService.PropagateAsync"/> call —
/// the shape <see cref="NegotiationsEndpointExtensions"/> folds into `POST
/// /api/negotiations/outcomes`'s own JSON reply. <c>internal</c>, same reasoning as
/// <c>QuoteExtractionPipeline.QuoteProcessingSummary</c>'s own doc comment.</summary>
internal sealed record NegotiationOutcomePropagationResult(
    EntityId NegotiationOutcomeId,
    EntityId QuoteId,
    EntityId SavingsOpportunityId,
    decimal RealizedSaving,
    string Currency,
    DateTimeOffset PropagatedAt);
