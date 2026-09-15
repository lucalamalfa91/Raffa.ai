using Raffa.Quotes.Infrastructure;
using Raffa.Savings.Application;
using Raffa.Savings.Domain;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Raffa.Suppliers.Products.Application;
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
///
/// <para>
/// <b>Task E19/F04/US01/T01 (outcome-resolves-the-opportunity; ADR-028 §D5 clause 2, ratified by
/// the w16 round-2 footer)</b>: the single production caller
/// (<c>web/src/routes/quotes/index.tsx</c>) never supplies a <c>savingsOpportunityId</c> at all, so
/// clause 1 above (<see cref="PropagateAsync"/>, unchanged by this task) was reachable only from a
/// test. <see cref="ResolveSavingsOpportunityIdAsync"/> is the missing second route:
/// <c>Raffa.Suppliers.Products.Application.SupplierNameNormalizer</c> plus
/// <see cref="ISupplierNameLookup.FindByNormalizedNameAsync"/> resolve the product's own definition
/// of supplier identity, read-only — never <c>Raffa.Suppliers.Products.Application
/// .SupplierResolver</c>, which resolves *or creates* and would mint a supplier row as a side
/// effect of recording an outcome. <see cref="NegotiationsEndpointExtensions"/> calls it only when
/// the capture request carried no id, and calls <see cref="PropagateAsync"/> on the result only
/// when it resolved to exactly one open opportunity — an ambiguous or absent match declines
/// (returns <see langword="null"/>) without ever calling <see cref="PropagateAsync"/> at all (Fence
/// 2: no opportunity row updates, no <c>RealizedSavings</c> row is inserted, so
/// <c>SavingsKpiCalculator</c> cannot absorb a write that never happened).
/// </para>
/// </summary>
internal sealed class NegotiationOutcomePropagationService(
    QuotesDbContext quotesDbContext,
    SavingsDbContext savingsDbContext,
    SavingsOpportunityService savingsOpportunityService,
    ISupplierNameLookup supplierNameLookup,
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

    /// <summary>
    /// The reserved, documented non-human principal for this service's own writes: this method runs
    /// as part of the negotiation-outcome capture request, never with a caller a human supplied
    /// directly, so <c>"system:negotiation-outcome-propagation"</c> is the permanent, correct actor
    /// for a non-human write (ADR-011 w16 clause 16c) — not an interim placeholder pending ADR-010,
    /// which landed in wave w15. The convention this constant originated (ADR-011 w16 clause 16) is
    /// reused, not re-invented, by <c>Raffa.Savings.Application.SavingsOpportunityService
    /// .SystemActor</c> and <c>Raffa.Chat.Application.RagAnswerService.SystemActor</c>.
    /// </summary>
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
            actor: SystemActor,
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

    /// <summary>
    /// Task E19/F04/US01/T01 (outcome-resolves-the-opportunity; ADR-028 §D5 clause 2). Called by
    /// <see cref="NegotiationsEndpointExtensions"/> only when the capture request carried no
    /// <c>savingsOpportunityId</c> at all — clause 1 (<see cref="PropagateAsync"/>) stays untouched
    /// and takes precedence whenever the caller does supply one. Resolves the product's own
    /// definition of supplier identity — <c>SupplierNameNormalizer.Normalize</c> against the
    /// <c>(tenant_id, normalized_name)</c> unique index
    /// (<see cref="ISupplierNameLookup.FindByNormalizedNameAsync"/>) — then the tenant's own
    /// <b>open</b> (not yet <see cref="SavingsOpportunityStatus.Realized"/>) <c>SavingsOpportunity</c>
    /// rows carrying that supplier id.
    ///
    /// <para>
    /// <b>Resolved, never guessed</b> (ADR-001 w16 clause 4): returns <see langword="null"/> —
    /// asking the caller to leave the capture's own <c>savingsPropagated</c> at its honest
    /// <see langword="null"/> and never call <see cref="PropagateAsync"/> at all (w16 round-2
    /// footer, Fence 2) — for every case that is not an exact, unambiguous match: the quote names no
    /// supplier, the name matches no <c>Supplier</c> this tenant has ever resolved before, or the
    /// matched supplier's own open opportunities number zero or two-or-more. Only
    /// <c>Raffa.Suppliers.Products.Application.SupplierResolver</c> creates a row on a miss; this
    /// method never does (it calls <see cref="ISupplierNameLookup"/>, never that type), so a decline
    /// here can never mint a supplier as a side effect of recording an outcome.
    /// </para>
    /// </summary>
    public async Task<EntityId?> ResolveSavingsOpportunityIdAsync(
        TenantId tenantId,
        EntityId quoteId,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var supplierName = await quotesDbContext.Quotes
            .AsNoTracking()
            .Where(q => q.TenantId == tenantId && q.Id == quoteId)
            .Select(q => q.Supplier)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(supplierName))
        {
            return null;
        }

        var normalizedName = SupplierNameNormalizer.Normalize(supplierName);

        var supplierId = await supplierNameLookup
            .FindByNormalizedNameAsync(tenantId, normalizedName, cancellationToken)
            .ConfigureAwait(false);

        if (supplierId is not { } resolvedSupplierId)
        {
            return null;
        }

        // At most 2 fetched -- distinguishing "exactly one" from "two or more" never needs the
        // true count of an ambiguous match.
        var openOpportunityIds = await savingsDbContext.SavingsOpportunities
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId
                && o.SupplierId == resolvedSupplierId
                && o.Status != SavingsOpportunityStatus.Realized)
            .Select(o => o.Id)
            .Take(2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return openOpportunityIds.Count == 1 ? openOpportunityIds[0] : null;
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
