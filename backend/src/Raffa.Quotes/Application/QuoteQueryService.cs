using Raffa.Quotes.Application.Strategy;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Quotes.Application;

/// <summary>
/// One quote as returned by <see cref="QuoteQueryService.ListAsync"/>/<see cref="QuoteQueryService.GetAsync"/>
/// (task E19/F02/US01/T01, quote-read-api; parent story us-01-quote-read-api AC-1/AC-2). Every
/// field is a stored <see cref="Quote"/> column, echoed exactly as recorded (ADR-028 §D2: "returns
/// stored fields and computes nothing") — the same four benchmark-matching fields `POST
/// /api/quotes` already echoes back on upload
/// (<c>Raffa.Api.QuotesEndpointExtensions.UploadQuoteAsync</c>), not re-derived here.
/// </summary>
public sealed record QuoteListItem(
    EntityId Id,
    string FileName,
    string MimeType,
    QuoteProcessingStatus ProcessingStatus,
    string? Supplier,
    string? Currency,
    string? Geography,
    DateOnly? PurchaseDate,
    DateTimeOffset CreatedAt);

/// <summary>
/// One recorded <see cref="NegotiationOutcome"/> as embedded on <see cref="QuoteDetail"/> — every
/// field a stored column of that row, the same shape `POST /api/negotiations/outcomes` already
/// returns on capture (<c>Raffa.Api.NegotiationsEndpointExtensions.CaptureOutcomeAsync</c>), minus
/// the two capture-time-only propagation-attempt fields (<c>savingsPropagated</c>/
/// <c>savingsPropagationError</c>): those report whether *that call's own* propagation attempt
/// succeeded, which is not a fact this row stores (ADR-028 §D2 — this service computes nothing).
/// <see cref="SavingsOpportunityId"/> — the stored link itself — is included; whether it actually
/// resolved at capture time is not.
/// </summary>
public sealed record NegotiationOutcomeRecord(
    EntityId Id,
    decimal OriginalQuoteTotal,
    decimal? TargetPrice,
    decimal FinalPrice,
    decimal RealizedSaving,
    decimal DiscountPercent,
    int NegotiationDurationDays,
    IReadOnlyList<NegotiationLeverType> LeversUsed,
    DateTimeOffset CapturedAt,
    EntityId? SavingsOpportunityId);

/// <summary>
/// <see cref="QuoteQueryService.GetAsync"/>'s own result: one quote plus every
/// <see cref="NegotiationOutcome"/> recorded against it, newest first (parent story
/// us-01-quote-read-api AC-2). <see cref="Outcomes"/> is an empty list, never
/// <see langword="null"/>, when none has been recorded yet — the caller distinguishes "no outcome
/// yet" from "quote not found" by whether <see cref="QuoteQueryService.GetAsync"/> itself returned
/// a <see cref="QuoteDetail"/> at all, never by inspecting this list.
/// </summary>
public sealed record QuoteDetail(QuoteListItem Quote, IReadOnlyList<NegotiationOutcomeRecord> Outcomes);

/// <summary>
/// The read side of the Quotes module (task E19/F02/US01/T01, quote-read-api; parent story
/// us-01-quote-read-api; ADR-028 §D2, wave w16 NW-12): backs `GET /api/quotes` and
/// `GET /api/quotes/{id}`. Modelled on
/// <see cref="Raffa.Documents.Contracts.Application.PortfolioQueryService"/>/
/// <see cref="Raffa.Documents.Contracts.Application.DocumentQueryService"/> — same shape, same
/// module family, different bounded context: nothing upstream (no middleware; see
/// <c>Raffa.Api.Program</c>) opens a tenant scope before a read runs, so every public method here
/// opens its own <see cref="ITenantContext.BeginScope"/> rather than trusting one is already
/// active.
///
/// <para>
/// <b>Returns stored fields and computes nothing</b> (ADR-028 §D2, binding on this type): unlike
/// <see cref="Assessment.MarketAssessmentService"/> — which recomputes a fresh
/// <see cref="Assessment.QuoteMarketAssessment"/> on every call, nothing persisted — this service
/// never calls it, never resolves <c>IBenchmarkService</c>, and takes no dependency capable of
/// doing either. That is a structural guarantee, not a convention documented in prose: a
/// `GET /api/quotes/{id}` can never re-report a negotiation this call did not touch, which is
/// exactly the hazard ADR-028 §D2 gives for declining to carry the recorded outcome on
/// `GET /api/quotes/{id}/assessment` instead (that route's own `.../assessment/recalculate`
/// sibling returns the identical shape, so a recalculation would appear to re-report an outcome it
/// never touched).
/// </para>
///
/// <para>
/// <see cref="NegotiationOutcome"/> is keyed by <see cref="NegotiationOutcome.QuoteId"/> and is
/// append-only (that type's own doc comment), so "the outcomes for a quote" is a property of the
/// quote, not a tenant-wide feed — <b>this type deliberately has no <c>ListOutcomesAsync</c> or
/// similar tenant-wide method</b>: ADR-028 §D2 forbids a bare `GET /api/negotiations/outcomes` (no
/// caller until NW-57, W18) and this service does not create one under another name.
/// </para>
/// </summary>
public sealed class QuoteQueryService(QuotesDbContext dbContext, ITenantContext tenantContext)
{
    /// <summary>
    /// AC-1: the tenant's quotes, newest first — and only the tenant's (application-level filter on
    /// top of the RLS backstop `quote` already carries, same belt-and-suspenders convention
    /// <see cref="Raffa.Documents.Contracts.Application.PortfolioQueryService"/>'s own doc comment
    /// establishes). No paging yet: parent story us-01-quote-read-api names none, the same "no
    /// filters yet" posture `GET /api/savings` takes at this story's size.
    /// </summary>
    public async Task<IReadOnlyList<QuoteListItem>> ListAsync(
        TenantId tenantId, CancellationToken cancellationToken = default)
    {
        // Entry point: open this call's own tenant scope (see the type doc comment) — required
        // before the query below, since the RLS connection interceptor reads ITenantContext.Current
        // only when the connection opens, which EF Core does lazily on first use.
        using var _ = tenantContext.BeginScope(tenantId);

        var quotes = await dbContext.Quotes.AsNoTracking()
            .Where(q => q.TenantId == tenantId)
            .OrderByDescending(q => q.CreatedAt)
            .ThenBy(q => q.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return quotes.Select(ToListItem).ToList();
    }

    /// <summary>
    /// AC-2/AC-3/AC-4: one quote for this tenant, with every <see cref="NegotiationOutcome"/>
    /// recorded against it embedded, newest first — <see langword="null"/> when no quote with this
    /// id exists for this tenant (the caller's own 404; same "tenant-scoped lookup miss is a 404,
    /// not an error" convention <see cref="Raffa.Documents.Contracts.Application.DocumentQueryService
    /// .GetByIdAsync"/> already uses), never when it exists with zero recorded outcomes (AC-2: that
    /// is an empty <see cref="QuoteDetail.Outcomes"/> list on an otherwise-populated result).
    /// </summary>
    public async Task<QuoteDetail?> GetAsync(
        TenantId tenantId, EntityId quoteId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);

        var quote = await dbContext.Quotes.AsNoTracking()
            .SingleOrDefaultAsync(q => q.TenantId == tenantId && q.Id == quoteId, cancellationToken)
            .ConfigureAwait(false);

        if (quote is null)
        {
            return null;
        }

        // Newest first (AC-2) — CapturedAt is caller-request-time (NegotiationOutcome's own doc
        // comment), never a database default, so this is a real chronological order, not an
        // insertion-order proxy. Id is a pure tiebreaker for determinism (this story has no
        // Skip/Take concept over this list yet), same convention
        // PortfolioQueryService/DocumentQueryService already use for their own newest-first lists.
        var outcomes = await dbContext.NegotiationOutcomes.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.QuoteId == quoteId)
            .OrderByDescending(o => o.CapturedAt)
            .ThenBy(o => o.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new QuoteDetail(ToListItem(quote), outcomes.Select(ToOutcomeRecord).ToList());
    }

    private static QuoteListItem ToListItem(Quote quote) => new(
        quote.Id,
        quote.FileName,
        quote.MimeType,
        quote.ProcessingStatus,
        quote.Supplier,
        quote.Currency,
        quote.Geography,
        quote.PurchaseDate,
        quote.CreatedAt);

    private static NegotiationOutcomeRecord ToOutcomeRecord(NegotiationOutcome outcome) => new(
        outcome.Id,
        outcome.OriginalQuoteTotal,
        outcome.TargetPrice,
        outcome.FinalPrice,
        outcome.RealizedSaving,
        outcome.DiscountPercent,
        outcome.NegotiationDurationDays,
        outcome.LeversUsed,
        outcome.CapturedAt,
        outcome.SavingsOpportunityId);
}
