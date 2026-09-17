using Raffa.Quotes.Application.Assessment;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Quotes.Application;

/// <summary>
/// One entry of <see cref="QuoteBenchmarkHistoryService.GetHistoryAsync"/>'s tenant-wide list: a
/// quote — the exact <see cref="QuoteListItem"/> shape <c>GET /api/quotes</c> already echoes — plus
/// its freshly-recomputed per-line market-benchmark assessment (the exact
/// <see cref="QuoteMarketAssessment.Lines"/> shape <c>GET /api/quotes/{id}/assessment</c> already
/// echoes). Reuses both rather than duplicating their fields — the same "QuoteDetail = QuoteListItem
/// + Outcomes" composition <see cref="QuoteQueryService"/>'s own <c>QuoteDetail</c> already
/// establishes for <see cref="QuoteQueryService.GetAsync"/>.
/// </summary>
public sealed record QuoteBenchmarkHistoryEntry(QuoteListItem Quote, IReadOnlyList<LineMarketAssessment> Lines);

/// <summary>
/// Implements task E25/F04/US01/T01 (quote-benchmark-backend; parent story
/// us-01-quote-benchmark-backend AC-2 "quote history is read back from server state" — closes
/// <b>NW-57</b>, W18). Backs <c>GET /api/quotes/benchmark-history</c>.
///
/// <para>
/// <b>What this closes, precisely.</b> ADR-028 §D2 (w16, NW-12) deliberately left one gap open:
/// "No <c>GET /api/negotiations/outcomes</c>... A bare tenant-wide outcome list has no caller until
/// NW-57 (W18) builds the history surface" — that is the <i>negotiation-outcome</i> half, and it
/// stays closed: <see cref="QuoteQueryService.GetAsync"/> already embeds a quote's own outcomes, and
/// this type does not duplicate that route. What NW-57 actually asks for
/// (`reports/architecture/waves/w18.md`: "job-to-be-done is a market benchmark, not a worksheet;
/// history + benchmark-first UX") is the <i>benchmark</i> half: a tenant's quotes, each carrying its
/// market-benchmark position, read back as durable server state rather than held in a client store
/// (ADR-028's own driver, ADR-012 §1: "a client store never stands in for a missing GET").
/// </para>
///
/// <para>
/// <b>Composes rather than re-derives</b> (the same "reuse, do not re-implement" posture
/// <c>Raffa.Quotes.Application.Strategy.NegotiationStrategyService</c> and
/// <c>Raffa.Quotes.Application.Normalization.SkuMappingService</c> already take for
/// <see cref="MarketAssessmentService"/>): this type does not resolve <c>IBenchmarkService</c> or
/// duplicate <see cref="MarketAssessmentQueryBuilder"/>/<see cref="MarketAssessmentCalculator"/>'s own
/// matching/classification rules. For every quote this tenant has, it calls the already-accepted
/// <see cref="MarketAssessmentService.AssessAsync"/> — the same per-line
/// <see cref="MarketAssessmentStatus"/>/<see cref="MarketPosition"/> shape
/// <c>GET /api/quotes/{id}/assessment</c> already returns, so a first-of-type quote (no fixture
/// comparable exists yet for its supplier/product) reports the honest
/// <see cref="MarketAssessmentStatus.InsufficientBenchmarkData"/> cold start here exactly as it does
/// there — never a fabricated position (Appendix C rule 10; ADR-001).
/// </para>
///
/// <para>
/// <b>Nothing new is persisted.</b> Like <see cref="MarketAssessmentService"/> itself (see that
/// type's own doc comment: "no ADR/spec names an 'Assessment' table shape... caching or persisting a
/// market position that can silently go stale would be a worse default than recomputing it"), this
/// service computes every line's assessment fresh on every call. "History" here means what ADR-028
/// means everywhere else in this wave: the <see cref="Quote"/>/<see cref="QuoteLine"/> rows
/// themselves are the durable server state (Postgres, RLS-enabled since
/// <c>20260905123141_AddTenantRowLevelSecurity</c>) — no new column, no new table, no schema
/// migration. A prior quote's benchmark "reads back" because the quote that produced it is still in
/// the database, not because a snapshot of the computed position was frozen at upload time.
/// </para>
///
/// <para>
/// Owns its own tenant scope (<see cref="ITenantContext.BeginScope"/>) — the same "trusts nothing
/// upstream already opened one" posture every other tenant-scoped application service in this module
/// takes (<see cref="QuoteQueryService"/>, <c>SkuMappingService</c>, and, after task E05/F04/US01/T01's
/// own fix, <see cref="MarketAssessmentService"/> too). Nesting a second
/// <see cref="ITenantContext.BeginScope"/> call inside <see cref="MarketAssessmentService.AssessAsync"/>
/// for the same tenant is safe and already-established in this codebase:
/// <c>SkuMappingService.RecalculateAsync</c> does exactly this (opens its own scope, then calls
/// <see cref="MarketAssessmentService.AssessAsync"/>, which opens a second, same-tenant scope) —
/// <see cref="TenantContext.BeginScope"/>'s own implementation is a plain push/pop over an
/// <see cref="AsyncLocal{T}"/>, so the inner scope re-sets the identical value and restores it on
/// dispose, never a conflict.
/// </para>
/// </summary>
public sealed class QuoteBenchmarkHistoryService(
    QuotesDbContext dbContext, MarketAssessmentService assessmentService, ITenantContext tenantContext)
{
    /// <summary>
    /// AC-2: the tenant's quotes, newest first (same order <see cref="QuoteQueryService.ListAsync"/>
    /// already establishes), each with a freshly-computed <see cref="LineMarketAssessment"/> per
    /// line. A quote with zero lines (still processing, or extraction found none) carries an empty
    /// <see cref="QuoteBenchmarkHistoryEntry.Lines"/> list — an honest "nothing to assess yet", never
    /// omitted from the history (ADR-001 w15 footer clause 7: "the system must not silently drop a
    /// fact the user is entitled to").
    /// </summary>
    public async Task<IReadOnlyList<QuoteBenchmarkHistoryEntry>> GetHistoryAsync(
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

        var entries = new List<QuoteBenchmarkHistoryEntry>(quotes.Count);

        foreach (var quote in quotes)
        {
            var assessed = await assessmentService
                .AssessAsync(tenantId, quote.Id, cancellationToken)
                .ConfigureAwait(false);

            // AssessAsync's only documented failure is "quote not found for this tenant"
            // (MarketAssessmentService's own doc comment) — unreachable here, since `quote` was just
            // read from this same tenant's own table inside this call's own scope. Still handled
            // honestly rather than assumed away (Appendix C rule 10; the same "never re-validate, but
            // never assume away either" posture SkuMappingService.RecalculateAsync's own doc comment
            // documents for the identical composed call) by falling back to an empty line list rather
            // than throwing or dropping the quote from the history.
            var lines = assessed.IsSuccess
                ? assessed.Value.Lines
                : (IReadOnlyList<LineMarketAssessment>)[];

            entries.Add(new QuoteBenchmarkHistoryEntry(ToListItem(quote), lines));
        }

        return entries;
    }

    /// <summary>Same mapping <see cref="QuoteQueryService"/>'s own private <c>ToListItem</c> uses —
    /// duplicated rather than shared across two sealed classes in the same file family (this
    /// codebase's own "each caller owns its own small wire-shaping helper" convention, e.g.
    /// <c>Raffa.Api.QuotesEndpointExtensions.BuildQuoteListItemResponse</c> alongside
    /// <c>GetQuoteAsync</c>'s own inline projection).</summary>
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
}
