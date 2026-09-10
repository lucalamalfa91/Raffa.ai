using Raffa.Benchmark;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Quotes.Application.Assessment;

/// <summary>
/// Implements task E05/F02/US01/T01 (market-assessment; parent story us-01-market-assessment AC-1
/// "Match normalized line items to the Benchmark Service (multi-dimensional)", AC-2's "flag" half,
/// AC-3 "<c>GET /api/quotes/{id}/assessment</c> returns the assessment with
/// confidence/provenance") and task E05/F02/US01/T02 (target-saving; AC-2's "recommended target
/// range + potential saving" half — <see cref="LineMarketAssessment.TargetSaving"/> is computed from
/// the same <c>line.Quantity</c> this service now also passes through per line below). The one place
/// in <c>Raffa.Quotes</c> that actually calls
/// <see cref="IBenchmarkService"/> — <c>Raffa.Quotes.Infrastructure.ServiceCollectionExtensions
/// .AddQuotesModule</c>'s own doc comment named this task as the "later step" that would need to
/// (it deliberately left <c>AddBenchmarkModule()</c> uncalled until a real caller existed). Calling
/// the interface here is exactly what Appendix C's benchmark rule sanctions — "Never call a
/// benchmark provider **directly**" names the provider adapter, not this abstraction; see
/// <see cref="IBenchmarkService"/>'s own doc comment: "Domain modules depend on this abstraction
/// only." Mirrors <c>Raffa.IntegrationTests.R3EndToEndTests</c>'s own convention of resolving
/// <see cref="IBenchmarkService"/> directly where a real caller is needed, rather than inventing an
/// extra indirection layer.
///
/// Read-only and computed fresh on every call — nothing here is persisted (unlike
/// <c>Raffa.Quotes.Application.Normalization.SkuNormalizationService</c>'s own
/// <c>QuoteLine.MatchStatus</c> column): no ADR/spec names an "Assessment" table shape beyond the
/// module-map's one-line mention, and a live benchmark distribution can change between two calls —
/// caching or persisting a market position that can silently go stale would be a worse default than
/// recomputing it, the same "computed fresh, not stored" posture
/// <c>Raffa.Savings.Application.PriceComparisonResult.Provenance</c> already takes for its own
/// derived property.
///
/// <para>
/// <b>Task E05/F04/US01/T01 (r4-integration) fix</b>: this type never opened its own
/// <see cref="ITenantContext.BeginScope"/> — unlike every other tenant-scoped application service in
/// this codebase (<c>QuoteUploadService</c>, <c>NegotiationOutcomeService</c>,
/// <c>SavingsOpportunityService</c>, <c>SavingsKpiQueryService</c> all "own their own tenant scope
/// rather than trusting one is already active"), and nothing upstream of it opens one either —
/// <c>Raffa.Api.QuotesEndpointExtensions.GetAssessmentAsync</c> calls <see cref="AssessAsync"/>
/// directly, with no <c>ITenantContext</c> parameter of its own. Against a real, RLS-enforced,
/// non-superuser connection (every deployed environment; every <c>Raffa.IntegrationTests</c>
/// fixture), <c>app.tenant_id</c> was therefore never set for this call, so
/// <c>TenantRlsConnectionInterceptor</c>'s own documented "fail closed" behaviour
/// (<see cref="ITenantContext.Current"/>'s own doc comment: "<see langword="null"/> means the RLS
/// claim is left unset... RLS denies every tenant-scoped row") denied the very row this method's own
/// explicit <c>tenantId</c> filter was trying to read — <c>GET /api/quotes/{id}/assessment</c> would
/// 404 for every real quote, always, in `dev`/`demo`. Undetected until now because
/// <c>Raffa.Quotes.Tests.MarketAssessmentServiceTests</c> calls this method from inside a
/// test-provided <c>tenantContext.BeginScope(tenantId)</c> block (masking the gap the same way
/// <c>Raffa.IntegrationTests.R2EndToEndTests</c>' own doc comment describes for
/// <c>RenewalThresholdScheduler.EvaluateThresholdsAsync</c>'s identical class of bug), and no
/// integration test had yet driven this endpoint over real HTTP against a real, unprivileged-role
/// Postgres connection. Fixed the same way every sibling service already does it: this type now
/// takes its own <see cref="ITenantContext"/> and opens the scope itself, so every current and future
/// caller gets a correct claim with no caller-side change required.
/// </para>
/// </summary>
public sealed class MarketAssessmentService(
    QuotesDbContext dbContext, IBenchmarkService benchmarkService, ITenantContext tenantContext)
{
    /// <summary>
    /// Assesses every <see cref="QuoteLine"/> currently persisted for <paramref name="quoteId"/>/
    /// <paramref name="tenantId"/>. One <see cref="IBenchmarkService.GetBenchmarkAsync"/> call per
    /// line that has enough data to build a query (see <see cref="MarketAssessmentQueryBuilder"/>) —
    /// never batched/cached across lines, since two lines can legitimately name different
    /// products/SKUs that must each be matched independently (spec §10.4).
    /// </summary>
    public async Task<Result<QuoteMarketAssessment>> AssessAsync(
        TenantId tenantId, EntityId quoteId, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var quote = await dbContext.Quotes
            .SingleOrDefaultAsync(q => q.TenantId == tenantId && q.Id == quoteId, cancellationToken)
            .ConfigureAwait(false);

        if (quote is null)
        {
            return Result<QuoteMarketAssessment>.Failure($"Quote {quoteId} was not found for this tenant.");
        }

        var lines = await dbContext.QuoteLines
            .Where(l => l.TenantId == tenantId && l.QuoteId == quoteId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var assessments = new List<LineMarketAssessment>(lines.Count);

        foreach (var line in lines)
        {
            assessments.Add(await AssessLineAsync(line, quote, cancellationToken).ConfigureAwait(false));
        }

        return Result<QuoteMarketAssessment>.Success(new QuoteMarketAssessment(quoteId, assessments));
    }

    private async Task<LineMarketAssessment> AssessLineAsync(
        QuoteLine line, Quote quote, CancellationToken cancellationToken)
    {
        var queryResult = MarketAssessmentQueryBuilder.Build(quote, line);

        if (queryResult.IsFailure)
        {
            return new LineMarketAssessment(
                line.Id,
                MarketAssessmentStatus.QuoteDataUnresolved,
                Position: null,
                UnitPrice: line.UnitPrice,
                Quantity: line.Quantity,
                Benchmark: null,
                Explanation: queryResult.Error);
        }

        var benchmarkOutcome = await benchmarkService
            .GetBenchmarkAsync(queryResult.Value, cancellationToken)
            .ConfigureAwait(false);

        if (benchmarkOutcome.IsFailure)
        {
            // The Benchmark Service itself failed (a provider-adapter-level problem, not a data
            // gap this module can name more precisely) — reported the same honest way a missing
            // query dimension is, never silently swallowed into a misleading "assessed" state.
            return new LineMarketAssessment(
                line.Id,
                MarketAssessmentStatus.QuoteDataUnresolved,
                Position: null,
                UnitPrice: line.UnitPrice,
                Quantity: line.Quantity,
                Benchmark: null,
                Explanation: benchmarkOutcome.Error);
        }

        var benchmark = benchmarkOutcome.Value;
        var classification = MarketAssessmentCalculator.Classify(line.UnitPrice!.Value, benchmark);

        return new LineMarketAssessment(
            line.Id,
            classification.Status,
            classification.Position,
            line.UnitPrice,
            line.Quantity,
            benchmark,
            classification.Explanation);
    }
}
