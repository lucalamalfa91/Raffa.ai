using System.Text.Json;
using Raffa.Benchmark.Adapters;
using Raffa.Benchmark.Contracts;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Market.Benchmark;

/// <summary>
/// Projection 1 of R-MKT-03's "two projections, one ingestion job": the mock feed's
/// <see cref="Raffa.Benchmark.IBenchmarkService"/> adapter (spec §10.2's Benchmark Service
/// boundary — Renewals/Savings/Quotes depend on <c>IBenchmarkService</c> only and never see this
/// type or <see cref="MarketDeal"/> directly). Registered under <see cref="AdapterName"/>
/// (<c>"market-feed"</c>) by <see cref="ServiceCollectionExtensions.AddMarketModule"/>, which also
/// makes it the active adapter by default — R-MKT-02: "It replaces the eight
/// <c>FixtureBenchmarkAdapter</c> rows as the default provider." <c>FixtureBenchmarkAdapter</c>
/// itself stays registered (harmless, still directly testable) — only the default changes.
///
/// <b>Interim data source (see <see cref="IMarketIntelligenceProvider"/>'s own doc comment):</b>
/// R-MKT-03 says benchmark rows are "served from the persisted <c>market_record</c> rows, never
/// from the provider at question time" — but this task (T01) adds no ingestion job and no
/// <c>market_record</c> table (that is T02's own scope: "Market index, ingestion, DB-backed
/// retrieval, record endpoint"). Until T02 lands, this adapter reads <see cref="MarketDeal"/> rows
/// directly from <see cref="IMarketIntelligenceProvider.GetDealsAsync"/> on every call — the only
/// data source T01 actually has — and T02 is expected to point this same adapter (or a like-for-like
/// replacement under the same <see cref="AdapterName"/>) at the persisted store instead, with no
/// change to <see cref="Raffa.Benchmark.IBenchmarkService"/> or any domain-module call site.
///
/// <b>Matching</b> (spec §10.4's "matching must use more than supplier name", applied to this
/// feed's own shape): supplier and product are always required; geography, currency and contract
/// term are required whenever both sides can express them (they always can — every field is
/// non-nullable on both <see cref="MarketDeal"/> and <see cref="BenchmarkQuery"/>); SKU is
/// evaluated only when <em>both</em> the query and the candidate deal name one — a deal with no
/// recorded SKU is not excluded just because the query happens to ask for one, but two SKUs that
/// disagree do exclude a candidate (the same convention <c>FixtureBenchmarkAdapter.IsBaselineMatch</c>
/// already uses). Quantity/company-size are deliberately not matched here: <see cref="MarketDeal"/>'s
/// own R-MKT-01 shape carries <see cref="MarketDeal.CompanySizeBand"/> /
/// <see cref="MarketDeal.AnnualValueBand"/> (coarse bands), not the numeric quantity range
/// <see cref="BenchmarkQuery.Quantity"/> would need to compare against — a real, task-scoped gap
/// in this feed's own fixed shape, not an oversight.
///
/// <b>Trust</b> (spec §10.4 benchmark-trust rule, ADR-001): a candidate that clears every baseline
/// dimension but carries fewer than <see cref="MinimumViableSampleSize"/> comparables — the mock
/// fixture's own deliberately-thin rows (R-MKT-02) — never publishes a distribution. Same fallback
/// shape as <c>FixtureBenchmarkAdapter</c>: a same-supplier/same-product-only "weak match" still
/// reports honest (if thin) provenance when one exists; otherwise an honest empty result. Never a
/// bare precise-looking number without provenance (ADR-001).
///
/// <b>Task E13/F02/US01/T02 (market-index)</b> adds this type's second data source: R-MKT-03 says
/// benchmark rows are "served from the persisted `market_record` rows, never from the provider at
/// question time" once an ingestion job exists. <see cref="MarketFeedBenchmarkAdapter(IDbContextFactory{MarketDbContext}, IClock)"/>
/// is that DB-backed constructor — <c>ServiceCollectionExtensions.AddMarketModule</c> chooses it
/// over the original, provider-backed constructor above when <c>ConnectionStrings:Market</c> is
/// present (task objective: "MarketFeedBenchmarkAdapter likewise switches to read `market_record`
/// when the connection string is present"). Every matching/confidence/provenance rule below is
/// unchanged and shared by both constructors — <see cref="LoadDealsAsync"/> is the only seam that
/// differs, so a query result is identical either way for the same underlying deals.
/// </summary>
public sealed class MarketFeedBenchmarkAdapter : IBenchmarkProviderAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMarketIntelligenceProvider? _provider;
    private readonly IDbContextFactory<MarketDbContext>? _dbContextFactory;
    private readonly IClock _clock;

    /// <summary>
    /// T01's original constructor: reads <see cref="MarketDeal"/> rows directly from
    /// <paramref name="provider"/> on every call — the interim, question-time provider call
    /// <see cref="IMarketIntelligenceProvider"/>'s own doc comment describes as "not the
    /// steady-state shape R-MKT-03 describes". Kept, unchanged, for every existing caller
    /// (<c>ServiceCollectionExtensions.AddMarketModule()</c> with no connection string;
    /// <c>MarketFeedBenchmarkAdapterTests</c>) that constructs this type directly against the mock
    /// feed with no database at all.
    /// </summary>
    public MarketFeedBenchmarkAdapter(IMarketIntelligenceProvider provider, IClock clock)
    {
        _provider = provider;
        _clock = clock;
    }

    /// <summary>
    /// T02's DB-backed constructor: reads every <see cref="MarketDeal"/> back out of the persisted
    /// <c>market_record</c> table instead of calling <see cref="IMarketIntelligenceProvider"/> at
    /// all — this constructor overload never even takes one, so it is structurally incapable of
    /// reaching the provider at question time (ADR-024), regardless of what
    /// <see cref="IMarketIntelligenceProvider"/> is registered elsewhere in the same container (see
    /// the type-level "throwing provider" proof in <c>Raffa.Market.Tests</c>).
    /// <paramref name="dbContextFactory"/>, not a directly-injected <see cref="MarketDbContext"/>:
    /// this adapter is registered Singleton (same lifetime T01's provider-backed registration
    /// already used, so <c>Raffa.Benchmark.BenchmarkAdapterRegistry</c>'s own Singleton
    /// <c>IEnumerable&lt;IBenchmarkProviderAdapter&gt;</c> constructor injection is unaffected by
    /// this task) — see <see cref="Retrieval.PgVectorMarketKnowledgeRetrieval"/>'s own doc comment
    /// for the identical captive-dependency reasoning.
    /// </summary>
    public MarketFeedBenchmarkAdapter(IDbContextFactory<MarketDbContext> dbContextFactory, IClock clock)
    {
        _dbContextFactory = dbContextFactory;
        _clock = clock;
    }

    /// <summary>Registry key (<c>IBenchmarkProviderAdapter.Name</c>) and
    /// <c>BenchmarkAdapterOptions.ActiveAdapter</c> default — task objective, verbatim.
    /// Deliberately distinct from <see cref="ProvenanceSource"/> (the UX-facing provenance
    /// string): this one is a stable machine key, that one is human-readable.</summary>
    public const string AdapterName = "market-feed";

    /// <summary>Task objective, verbatim — <see cref="BenchmarkResult.Source"/> for every result
    /// this adapter produces, confident or not (contains "mock" per
    /// us-01-market-intelligence AC-2 / the task's own definition of done).</summary>
    private const string ProvenanceSource = "market-feed (representative, mock)";

    /// <summary>Honest placeholder <see cref="BenchmarkResult.Metric"/> when not even a weak,
    /// supplier+product comparable exists — mirrors <c>FixtureBenchmarkAdapter.UnknownMetric</c>'s
    /// own "never a fabricated-looking unit" reasoning.</summary>
    private const string UnknownMetric = "n/a";

    /// <summary>
    /// <see cref="MarketDeal"/> has no per-record billing-unit string (R-MKT-01's own field list
    /// has no "metric"/"unit" field — only the generic "unit price P25/P50/P75"). Reporting the
    /// spec's own generic term here, rather than guessing "per seat/month" or similar per record,
    /// is the honest choice: this adapter cannot verify a finer-grained unit than the feed itself
    /// records.
    /// </summary>
    private const string MetricLabel = "unit price";

    /// <summary>Sample size at or above which this adapter treats a match as fully trustworthy
    /// (the confidence sample-size factor saturates at 1.0) — same value and rationale as
    /// <c>FixtureBenchmarkAdapter.FullConfidenceSampleSize</c>.</summary>
    private const int FullConfidenceSampleSize = 50;

    /// <summary>
    /// Minimum sample size a deal must carry to publish a confident distribution, even when it
    /// clears every baseline dimension. R-MKT-02 names the exact boundary this fixture was built
    /// to exercise: "several deliberately thin rows with <c>sampleSize &lt; 5</c> so abstain paths
    /// are exercised" — so a sample size below <c>5</c> must abstain, and <c>5</c> is the natural
    /// floor.
    /// </summary>
    private const int MinimumViableSampleSize = 5;

    /// <inheritdoc/>
    public string Name => AdapterName;

    /// <inheritdoc/>
    public async Task<Result<BenchmarkResult>> GetBenchmarkAsync(
        BenchmarkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var dealsResult = await LoadDealsAsync(cancellationToken).ConfigureAwait(false);
        if (dealsResult.IsFailure)
        {
            return Result<BenchmarkResult>.Failure(dealsResult.Error);
        }

        var deals = dealsResult.Value;
        var strongMatch = FindStrongMatch(deals, query);

        var result = strongMatch is not null
            ? BuildConfidentResult(query, strongMatch)
            : BuildInsufficientDataResult(query, FindWeakMatch(deals, query), deals, _clock);

        return Result<BenchmarkResult>.Success(result);
    }

    /// <summary>
    /// The one seam that differs between this type's two constructors (see their own doc
    /// comments): the DB-backed path (<see cref="_dbContextFactory"/> set) reads every persisted
    /// <c>market_record</c> row and deserializes its <c>PayloadJson</c> back into a
    /// <see cref="MarketDeal"/> — never calling <see cref="IMarketIntelligenceProvider"/> at all
    /// (ADR-024: "the provider is called only by the ingestion job"); the provider-backed path
    /// (T01, <see cref="_provider"/> set) is unchanged. Exactly one of the two fields is non-null
    /// for any instance (enforced by which constructor ran), so exactly one branch below executes.
    /// </summary>
    private async Task<Result<IReadOnlyList<MarketDeal>>> LoadDealsAsync(CancellationToken cancellationToken)
    {
        if (_dbContextFactory is not null)
        {
            await using var dbContext = await _dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            var records = await dbContext.MarketRecords
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var deals = records
                .Select(record => JsonSerializer.Deserialize<MarketDeal>(record.PayloadJson, JsonOptions))
                .Where(deal => deal is not null)
                .Select(deal => deal!)
                .ToList();

            return Result<IReadOnlyList<MarketDeal>>.Success(deals);
        }

        var feedResult = await _provider!.GetDealsAsync(feedVersion: null, cancellationToken).ConfigureAwait(false);
        return feedResult.IsFailure
            ? Result<IReadOnlyList<MarketDeal>>.Failure(feedResult.Error)
            : Result<IReadOnlyList<MarketDeal>>.Success(feedResult.Value.Deals);
    }

    /// <summary>
    /// The strongest deal that clears every baseline dimension (<see cref="IsBaselineMatch"/>)
    /// <em>and</em> carries at least <see cref="MinimumViableSampleSize"/> comparables. When more
    /// than one clears both bars, prefers an actual SKU match, then the larger sample size, so the
    /// answer stays deterministic.
    /// </summary>
    private static MarketDeal? FindStrongMatch(IReadOnlyList<MarketDeal> deals, BenchmarkQuery query) =>
        deals
            .Where(deal => IsBaselineMatch(deal, query) && deal.SampleSize >= MinimumViableSampleSize)
            .OrderByDescending(deal => SkuActuallyMatched(deal, query))
            .ThenByDescending(deal => deal.SampleSize)
            .FirstOrDefault();

    /// <summary>Same-supplier, same-product deal when the full baseline did not clear (spec
    /// §10.4's "weak comparables" case) — picks the larger sample size when more than one such
    /// deal exists, ignoring geography/currency/term/SKU entirely.</summary>
    private static MarketDeal? FindWeakMatch(IReadOnlyList<MarketDeal> deals, BenchmarkQuery query) =>
        deals
            .Where(deal =>
                string.Equals(deal.Supplier, query.Supplier, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(deal.Product, query.Product, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(deal => deal.SampleSize)
            .FirstOrDefault();

    /// <summary>Required match dimensions (see the type-level doc comment's "Matching" section):
    /// supplier, product, geography, currency and contract term always; SKU only when both sides
    /// name one and they disagree.</summary>
    private static bool IsBaselineMatch(MarketDeal deal, BenchmarkQuery query)
    {
        var queryTermMonths = ParseTermMonths(query.Term);

        return string.Equals(deal.Supplier, query.Supplier, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(deal.Product, query.Product, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(deal.Geography, query.Geography, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(deal.Currency, query.Currency, StringComparison.OrdinalIgnoreCase) &&
            (queryTermMonths is null || queryTermMonths == deal.TermMonths) &&
            (deal.Sku is null || query.Sku is null ||
                string.Equals(deal.Sku, query.Sku, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SkuActuallyMatched(MarketDeal deal, BenchmarkQuery query) =>
        deal.Sku is not null && query.Sku is not null &&
        string.Equals(deal.Sku, query.Sku, StringComparison.OrdinalIgnoreCase);

    /// <summary>Extracts the leading integer from a term string such as <c>"12 months"</c>.
    /// Returns <see langword="null"/> when no digits are present, in which case
    /// <see cref="IsBaselineMatch"/> does not gate on term at all rather than rejecting every
    /// candidate over an unparseable query.</summary>
    private static int? ParseTermMonths(string term)
    {
        var digits = new string(term.Where(char.IsAsciiDigit).ToArray());
        return digits.Length > 0 && int.TryParse(digits, out var months) ? months : null;
    }

    private static BenchmarkResult BuildConfidentResult(BenchmarkQuery query, MarketDeal match)
    {
        var skuMatched = SkuActuallyMatched(match, query);
        var dimensions = BuildMatchedDimensions(skuMatched);
        var totalPossibleDimensions = query.Sku is null ? 5 : 6;

        return new BenchmarkResult(
            Distribution: new BenchmarkDistribution(match.UnitPriceP25, match.UnitPriceP50, match.UnitPriceP75),
            Metric: MetricLabel,
            Currency: query.Currency,
            Confidence: ComputeConfidence(dimensions.Count, totalPossibleDimensions, match.SampleSize),
            Source: ProvenanceSource,
            UpdatedAt: match.UpdatedAt,
            ComparisonDimensions: dimensions,
            SampleSize: match.SampleSize,
            LicenseRestrictions: match.LicenseRestrictions);
    }

    /// <summary>Builds the explicit "insufficient market data" outcome (ADR-001):
    /// <see cref="BenchmarkResult.Distribution"/> is always <see langword="null"/> here. When
    /// <paramref name="weakMatch"/> exists, provenance still names its real metric/sample
    /// size/refresh date; otherwise this adapter has nothing at all to say beyond the requested
    /// currency and the freshest date anywhere in the feed.</summary>
    private static BenchmarkResult BuildInsufficientDataResult(
        BenchmarkQuery query, MarketDeal? weakMatch, IReadOnlyList<MarketDeal> deals, IClock clock)
    {
        if (weakMatch is null)
        {
            return new BenchmarkResult(
                Distribution: null,
                Metric: UnknownMetric,
                Currency: query.Currency,
                Confidence: 0d,
                Source: ProvenanceSource,
                UpdatedAt: LatestUpdatedAt(deals, clock),
                ComparisonDimensions: [],
                SampleSize: null);
        }

        const int weakMatchedDimensionCount = 2; // Supplier + Product only.
        var totalPossibleDimensions = query.Sku is null ? 5 : 6;

        return new BenchmarkResult(
            Distribution: null,
            Metric: MetricLabel,
            Currency: query.Currency,
            Confidence: ComputeConfidence(weakMatchedDimensionCount, totalPossibleDimensions, weakMatch.SampleSize),
            Source: ProvenanceSource,
            UpdatedAt: weakMatch.UpdatedAt,
            ComparisonDimensions: [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Product],
            SampleSize: weakMatch.SampleSize,
            LicenseRestrictions: weakMatch.LicenseRestrictions);
    }

    private static double ComputeConfidence(int matchedDimensionCount, int totalPossibleDimensions, int sampleSize)
    {
        var dimensionScore = matchedDimensionCount / (double)totalPossibleDimensions;
        var sampleSizeFactor = Math.Min(1.0, sampleSize / (double)FullConfidenceSampleSize);
        return Math.Round(dimensionScore * sampleSizeFactor, 2);
    }

    /// <summary>The freshest <see cref="MarketDeal.UpdatedAt"/> anywhere in the feed — an honest
    /// "when this mock feed was last refreshed" fallback for the case where not even a weak
    /// comparable exists to report a date from. Falls back to <paramref name="clock"/> only for the
    /// degenerate empty-catalog case (never reachable through the real, 60+-record fixture; only
    /// through a test-supplied empty provider).</summary>
    private static DateTimeOffset LatestUpdatedAt(IReadOnlyList<MarketDeal> deals, IClock clock) =>
        deals.Count > 0 ? deals.Max(deal => deal.UpdatedAt) : clock.UtcNow;

    private static IReadOnlyCollection<BenchmarkComparisonDimension> BuildMatchedDimensions(bool skuMatched)
    {
        List<BenchmarkComparisonDimension> dimensions =
        [
            BenchmarkComparisonDimension.Supplier,
            BenchmarkComparisonDimension.Product,
            BenchmarkComparisonDimension.Geography,
            BenchmarkComparisonDimension.Currency,
            BenchmarkComparisonDimension.ContractTerm,
        ];

        if (skuMatched)
        {
            dimensions.Add(BenchmarkComparisonDimension.Sku);
        }

        return dimensions;
    }
}
