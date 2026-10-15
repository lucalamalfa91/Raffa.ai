using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
///
/// <para>
/// <b>Task F6-T09 (matcher with <c>Raffa.Suppliers.Products</c>).</b> When the query as built finds no
/// distribution, the line is retried with the names the tenant has recorded for it, in a fixed order
/// (<see cref="MarketAssessmentMatchLadder"/>): the canonical product/SKU of its confirmed SKU mapping
/// (<see cref="SkuProductMapping"/>, which until now was written by the manual mapping and read by
/// nothing that priced the line) and the other names the tenant's supplier record carries
/// (<see cref="ISupplierAliasLookup"/>). The first retry that finds a distribution wins and the line says
/// which level it matched at; if none does the original answer stands, so a line that matched before
/// matches exactly as before and the match rate can only go up. Both inputs are optional: without the
/// lookup, or when it fails, only the mapping is used.
/// </para>
/// </summary>
public sealed class MarketAssessmentService(
    QuotesDbContext dbContext,
    IBenchmarkService benchmarkService,
    ITenantContext tenantContext,
    ISupplierAliasLookup? supplierAliasLookup = null,
    ILogger<MarketAssessmentService>? logger = null)
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
        var retries = new RetryNames(dbContext, supplierAliasLookup, logger, tenantId, quote);

        foreach (var line in lines)
        {
            assessments.Add(await AssessLineAsync(line, quote, retries, cancellationToken).ConfigureAwait(false));
        }

        return Result<QuoteMarketAssessment>.Success(new QuoteMarketAssessment(quoteId, assessments));
    }

    private async Task<LineMarketAssessment> AssessLineAsync(
        QuoteLine line, Quote quote, RetryNames retries, CancellationToken cancellationToken)
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
        var level = MarketMatchLevel.Direct;

        // Task F6-T09: the query as built found nothing. Retry with the names the tenant recorded; the
        // first retry that finds a distribution wins, otherwise the answer above stands (abstention).
        if (!benchmark.HasSufficientData)
        {
            var retried = await RetryWithTenantNamesAsync(queryResult.Value, line, retries, cancellationToken)
                .ConfigureAwait(false);
            if (retried is { } found)
            {
                benchmark = found.Benchmark;
                level = found.Level;
            }
        }

        var classification = MarketAssessmentCalculator.Classify(line.UnitPrice!.Value, benchmark);

        return new LineMarketAssessment(
            line.Id,
            classification.Status,
            classification.Position,
            line.UnitPrice,
            line.Quantity,
            benchmark,
            level == MarketMatchLevel.Direct
                ? classification.Explanation
                : $"{classification.Explanation} Matched through {Describe(level)}.",
            level);
    }

    private async Task<(BenchmarkResult Benchmark, MarketMatchLevel Level)?> RetryWithTenantNamesAsync(
        BenchmarkQuery direct, QuoteLine line, RetryNames retries, CancellationToken cancellationToken)
    {
        var suppliers = await retries.SupplierNamesAsync(cancellationToken).ConfigureAwait(false);
        var mapping = await retries.MappingAsync(line, cancellationToken).ConfigureAwait(false);

        foreach (var candidate in MarketAssessmentMatchLadder.Retries(direct, suppliers, mapping))
        {
            var outcome = await benchmarkService
                .GetBenchmarkAsync(candidate.Query, cancellationToken)
                .ConfigureAwait(false);
            if (outcome.IsSuccess && outcome.Value.HasSufficientData)
            {
                return (outcome.Value, candidate.Level);
            }
        }

        return null;
    }

    private static string Describe(MarketMatchLevel level) => level switch
    {
        MarketMatchLevel.ProductAlias => "the product name or SKU confirmed for this SKU",
        MarketMatchLevel.SupplierAlias => "another name recorded for this supplier",
        MarketMatchLevel.SupplierAndProductAlias => "another name recorded for this supplier and the product name or SKU confirmed for this SKU",
        _ => "the quote's own names",
    };

    /// <summary>
    /// What the retries need, loaded the first time a line needs them and then reused for the rest of
    /// the quote: a quote whose lines all match directly never reads either.
    /// </summary>
    private sealed class RetryNames(
        QuotesDbContext dbContext,
        ISupplierAliasLookup? aliasLookup,
        ILogger<MarketAssessmentService>? logger,
        TenantId tenantId,
        Quote quote)
    {
        private IReadOnlyList<string>? _suppliers;
        private IReadOnlyDictionary<string, SkuProductMapping>? _mappings;

        public async Task<IReadOnlyList<string>> SupplierNamesAsync(CancellationToken cancellationToken)
        {
            if (_suppliers is not null)
            {
                return _suppliers;
            }

            _suppliers = [];
            if (aliasLookup is not null && !string.IsNullOrWhiteSpace(quote.Supplier))
            {
                try
                {
                    _suppliers = await aliasLookup
                        .GetAlternateNamesAsync(tenantId, quote.Supplier, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The aliases are an enhancement of the direct match, never a precondition of the
                    // assessment: without them the line is assessed as it always was.
                    logger?.LogWarning(
                        exception, "Supplier aliases for quote {QuoteId} could not be read; matching without them.", quote.Id);
                }
            }

            return _suppliers;
        }

        public async Task<SkuProductMapping?> MappingAsync(QuoteLine line, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(line.NormalizedSku))
            {
                return null;
            }

            _mappings ??= (await dbContext.SkuProductMappings.AsNoTracking()
                    .Where(m => m.TenantId == tenantId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(m => m.NormalizedSku, StringComparer.Ordinal);

            return _mappings.GetValueOrDefault(line.NormalizedSku);
        }
    }
}
