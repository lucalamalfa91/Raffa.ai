using Raffa.Benchmark.Adapters;
using Raffa.Benchmark.Contracts;
using Raffa.SharedKernel;

namespace Raffa.Benchmark.Fixtures;

/// <summary>
/// Deterministic, provider-free <see cref="IBenchmarkService"/> implementation — the first
/// Benchmark Service adapter (story us-02-fixture-adapter; task E04/F01/US02/T01, objective:
/// "Fixture adapter returning P25/P50/P75 + confidence + provenance"). ADR-001 fixes this as the
/// *only* adapter for the first `demo`: "R3/R4 benchmark work gated on the interface + fixture
/// adapter, never a paid external API." Product spec §10.2's own Benchmark Service diagram names
/// "Internal Dataset" as one of four Provider Adapter kinds (alongside "Provider A", "Provider B",
/// "Customer History") — this class is that adapter, backed by a hand-curated, in-memory catalog of
/// illustrative SaaS supplier/product comparables. Never a real provider's licensed data, and never a
/// call to Tropic, Vendr, or any paid market API anywhere in this type or its project (AC-2; spec
/// §10.2's "Strategic requirement").
///
/// Matching (spec §10.4: "Matching must use more than supplier name") requires <see
/// cref="BenchmarkQuery.Supplier"/>, <see cref="BenchmarkQuery.Product"/>,
/// <see cref="BenchmarkQuery.Geography"/>, <see cref="BenchmarkQuery.Currency"/>,
/// <see cref="BenchmarkQuery.Term"/>, <see cref="BenchmarkQuery.Quantity"/> (against a fixture's own
/// quantity tier) and <see cref="BenchmarkQuery.PurchaseDate"/> (against a fixture's own refresh
/// date — spec §10.3's stated reason for carrying a purchase date at all: "so comparables can be
/// filtered to a relevant window") as a required baseline — seven of product spec §10.4's eleven
/// named comparison dimensions — plus <see cref="BenchmarkQuery.Sku"/> as an optional,
/// confidence-boosting eighth when both the query and the matched fixture name one. A <see
/// cref="BenchmarkResult.ComparisonDimensions"/> set containing only
/// <see cref="BenchmarkComparisonDimension.Supplier"/> can never come out of this adapter (AC-3).
///
/// When no fixture clears that baseline, this adapter never fabricates a number (ADR-001; spec
/// §10.4's benchmark-trust rule, verbatim: "A precise-looking number from weak comparables is more
/// dangerous than an explicit 'insufficient market data' result"). It reports <see
/// cref="BenchmarkResult.Distribution"/> as <see langword="null"/> instead — falling back to a
/// weaker, supplier+product-only comparable when one exists (so the caller still sees a real metric
/// and sample size, just not a trustworthy distribution), or to no comparison dimensions at all when
/// even that does not exist. Task E04/F01/US02/T02 (fixture-confidence) extends that same abstain
/// path to a comparable that is <i>dimensionally</i> strong (clears every baseline dimension) but
/// still <i>statistically</i> weak — see <see cref="MinimumViableSampleSize"/> — so a thin sample
/// size can never dress itself up as a trustworthy distribution just because it happened to match on
/// every field.
///
/// Task E04/F01/US02/T02 also completes the registry wiring <see cref="IBenchmarkProviderAdapter"/>'s
/// own doc comment names ("story us-02-fixture-adapter adds the first implementation... expected to
/// register under <see cref="Configuration.BenchmarkAdapterOptions.DefaultAdapterName"/>"): this
/// class now implements that seam directly (<see cref="Name"/>) so
/// <see cref="BenchmarkAdapterRegistry"/> — the actual <see cref="IBenchmarkService"/> a caller
/// resolves from <see cref="ServiceCollectionExtensions.AddBenchmarkModule"/> — can dispatch to it by
/// name instead of leaving it registered but unreachable.
///
/// Mirrors <c>Raffa.AiGateway.Fixtures.FixtureAiGateway</c>: a later, council-justified paid
/// provider adapter swaps in behind the same <see cref="IBenchmarkService"/> seam — domain code
/// (Renewals, Savings, Quotes; ADR-002 module-map) never notices, since it only ever depends on the
/// interface (us-01-benchmark-interface AC-2).
/// </summary>
public sealed class FixtureBenchmarkAdapter : IBenchmarkService, IBenchmarkProviderAdapter
{
    /// <summary>Provenance <see cref="BenchmarkResult.Source"/> for every result this adapter
    /// produces — never a named provider, per ADR-001.</summary>
    private const string SourceName = "fixture";

    /// <inheritdoc/>
    /// <remarks>
    /// Equal to <see cref="SourceName"/> — the same "fixture" literal
    /// <see cref="Configuration.BenchmarkAdapterOptions.DefaultAdapterName"/> already names as the
    /// expected registration name (task E04/F01/US02/T02), so
    /// <see cref="BenchmarkAdapterRegistry"/> resolves this adapter with no separate name to keep in
    /// sync.
    /// </remarks>
    public string Name => SourceName;

    /// <summary>Honest placeholder <see cref="BenchmarkResult.Metric"/> when not even a weak,
    /// supplier+product comparable exists — there is no fixture data at all to name a unit from
    /// (never a fabricated-looking unit, same "empty JSON is honest" choice
    /// <c>Raffa.AiGateway.Fixtures.FixtureAiGateway.ExtractAsync</c> makes for its own
    /// placeholder).</summary>
    private const string UnknownMetric = "n/a";

    /// <summary>
    /// Sample size at or above which this adapter treats a fixture's comparable count as fully
    /// trustworthy (the confidence sample-size factor saturates at 1.0). Below this, confidence
    /// scales down linearly. Spec §10.3's confidence field is "Raffa's own score", not a provider
    /// one, so this threshold is this adapter's own documented, deliberately simple heuristic.
    /// </summary>
    private const int FullConfidenceSampleSize = 50;

    /// <summary>
    /// Minimum sample size a fixture comparable must carry to publish a confident distribution at
    /// all, even when it clears every required baseline dimension (<see cref="IsBaselineMatch"/>).
    /// Task E04/F01/US02/T02's own objective ("fixture-confidence": weak-comparable abstain): a
    /// comparable can be <i>dimensionally</i> strong — matches supplier, product, geography,
    /// currency, term, quantity tier and purchase-date window — yet still be <i>statistically</i> too
    /// thin to trust with a precise-looking P25/P50/P75 number (spec §10.4's benchmark-trust rule:
    /// "a precise-looking number from weak comparables is more dangerous than an explicit
    /// 'insufficient market data' result"). Below this floor, <see cref="FindStrongMatch"/> treats
    /// the comparable as not qualifying at all, so <see cref="GetBenchmarkAsync"/> falls back to
    /// <see cref="FindWeakMatch"/>'s honest insufficient-data provenance instead — the same abstain
    /// outcome a dimensionally-weak comparable already takes (AC-3), now also reachable from a
    /// statistically-weak one.
    ///
    /// Deliberately below <see cref="FullConfidenceSampleSize"/> (which only ever scales confidence
    /// down, never abstains) and strictly below the catalog's own thinnest still-confident fixture
    /// (Snowflake, sample size 18) so that fixture's documented "confident-but-thin" behaviour is
    /// unchanged by this task.
    /// </summary>
    private const int MinimumViableSampleSize = 10;

    /// <summary>
    /// How many days a query's <see cref="BenchmarkQuery.PurchaseDate"/> may fall from a fixture
    /// comparable's own <see cref="Comparable.UpdatedAt"/> and still be considered the same pricing
    /// window (~13 months). A purchase further out than this from when the comparable was last
    /// refreshed cannot be trusted to reflect the same market conditions.
    /// </summary>
    private const int PurchaseDateWindowDays = 400;

    /// <summary>
    /// When not even a weak comparable exists, the fixture catalog itself still has a notional
    /// "last curated" date — distinct from any one comparable's own <see cref="Comparable.UpdatedAt"/>
    /// — so an insufficient-data result never reports a fabricated/default provenance date.
    /// </summary>
    private static readonly DateTimeOffset CatalogUpdatedAt = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// One row of this adapter's in-memory, hand-curated comparable dataset (task objective: "...
    /// + benchmark dataset"). Every value is illustrative fixture data for the first `demo` — not
    /// sourced from any real provider, contract or licensed dataset (AC-2; spec §10.2's "Internal
    /// Dataset" adapter kind).
    /// </summary>
    private sealed record Comparable(
        string Supplier,
        string Product,
        string? Sku,
        string Geography,
        string Currency,
        string Term,
        string Metric,
        decimal MinQuantity,
        decimal MaxQuantity,
        decimal P25,
        decimal P50,
        decimal P75,
        int SampleSize,
        DateTimeOffset UpdatedAt);

    /// <summary>
    /// The fixture catalog. Deliberately covers more than one geography (AWS US vs EU) and more than
    /// one contract term (Salesforce 12 vs 36 months) for the same supplier+product, so a
    /// test/caller can observe that geography and term actually change the published distribution —
    /// proof this adapter really matches on them rather than keying off supplier name alone (spec
    /// §10.4). Snowflake's low sample size (18, below <see cref="FullConfidenceSampleSize"/> but at
    /// or above <see cref="MinimumViableSampleSize"/>) deliberately demonstrates a confident-but-thin
    /// result. Notion's very low sample size (4, below <see cref="MinimumViableSampleSize"/>)
    /// deliberately demonstrates task E04/F01/US02/T02's statistical weak-comparable abstain: every
    /// baseline dimension matches, yet the result must still report insufficient market data (AC-3).
    /// </summary>
    private static readonly IReadOnlyList<Comparable> Catalog =
    [
        new(Supplier: "AWS", Product: "EC2 Compute", Sku: "m5.large", Geography: "US", Currency: "USD",
            Term: "12 months", Metric: "per instance-hour", MinQuantity: 1m, MaxQuantity: 500m,
            P25: 0.085m, P50: 0.096m, P75: 0.108m, SampleSize: 340,
            UpdatedAt: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)),

        new(Supplier: "AWS", Product: "EC2 Compute", Sku: "m5.large", Geography: "EU", Currency: "EUR",
            Term: "12 months", Metric: "per instance-hour", MinQuantity: 1m, MaxQuantity: 500m,
            P25: 0.081m, P50: 0.093m, P75: 0.104m, SampleSize: 210,
            UpdatedAt: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)),

        new(Supplier: "Salesforce", Product: "Sales Cloud Enterprise", Sku: null, Geography: "US", Currency: "USD",
            Term: "12 months", Metric: "per seat / year", MinQuantity: 10m, MaxQuantity: 1000m,
            P25: 1500m, P50: 1800m, P75: 2100m, SampleSize: 512,
            UpdatedAt: new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)),

        new(Supplier: "Salesforce", Product: "Sales Cloud Enterprise", Sku: null, Geography: "US", Currency: "USD",
            Term: "36 months", Metric: "per seat / year", MinQuantity: 10m, MaxQuantity: 1000m,
            P25: 1350m, P50: 1620m, P75: 1890m, SampleSize: 260,
            UpdatedAt: new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)),

        new(Supplier: "Slack", Product: "Business+", Sku: null, Geography: "US", Currency: "USD",
            Term: "12 months", Metric: "per seat / year", MinQuantity: 1m, MaxQuantity: 5000m,
            P25: 96m, P50: 108m, P75: 132m, SampleSize: 780,
            UpdatedAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),

        new(Supplier: "Snowflake", Product: "Standard Compute Credits", Sku: null, Geography: "US", Currency: "USD",
            Term: "12 months", Metric: "per credit", MinQuantity: 100m, MaxQuantity: 100000m,
            P25: 2.10m, P50: 2.35m, P75: 2.60m, SampleSize: 18,
            UpdatedAt: new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero)),

        new(Supplier: "Zoom", Product: "Workplace Pro", Sku: null, Geography: "US", Currency: "USD",
            Term: "12 months", Metric: "per seat / year", MinQuantity: 1m, MaxQuantity: 2000m,
            P25: 140m, P50: 156m, P75: 180m, SampleSize: 30,
            UpdatedAt: new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero)),

        new(Supplier: "Notion", Product: "Enterprise Plan", Sku: null, Geography: "US", Currency: "USD",
            Term: "12 months", Metric: "per seat / year", MinQuantity: 1m, MaxQuantity: 2000m,
            P25: 180m, P50: 204m, P75: 228m, SampleSize: 4,
            UpdatedAt: new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero)),
    ];

    /// <inheritdoc/>
    public Task<Result<BenchmarkResult>> GetBenchmarkAsync(
        BenchmarkQuery query, CancellationToken cancellationToken = default)
    {
        var strongMatch = FindStrongMatch(query);

        var result = strongMatch is not null
            ? BuildConfidentResult(query, strongMatch)
            : BuildInsufficientDataResult(query, FindWeakMatch(query));

        return Task.FromResult(Result<BenchmarkResult>.Success(result));
    }

    /// <summary>
    /// The strongest fixture that clears every required baseline dimension (AC-1/AC-3) <i>and</i>
    /// carries at least <see cref="MinimumViableSampleSize"/> comparables — a dimensionally strong
    /// but statistically thin match (task E04/F01/US02/T02) does not qualify here and instead falls
    /// through to <see cref="FindWeakMatch"/>'s honest insufficient-data path. When more than one
    /// comparable clears both bars — this adapter's own dataset never produces that today, but a
    /// future addition might — prefers an actual <see cref="BenchmarkQuery.Sku"/> match, then the
    /// larger sample size, so the answer stays deterministic.
    /// </summary>
    private static Comparable? FindStrongMatch(BenchmarkQuery query) =>
        Catalog
            .Where(c => IsBaselineMatch(c, query) && c.SampleSize >= MinimumViableSampleSize)
            .OrderByDescending(c => SkuActuallyMatched(c, query))
            .ThenByDescending(c => c.SampleSize)
            .FirstOrDefault();

    /// <summary>
    /// A same-supplier, same-product fixture when the full baseline did not clear — spec §10.4's
    /// "weak comparables" case (AC-3): there is *some* data, just not enough alignment to publish a
    /// distribution. Picks the larger sample size when more than one such fixture exists.
    /// </summary>
    private static Comparable? FindWeakMatch(BenchmarkQuery query) =>
        Catalog
            .Where(c =>
                string.Equals(c.Supplier, query.Supplier, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Product, query.Product, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.SampleSize)
            .FirstOrDefault();

    /// <summary>
    /// Required match dimensions (spec §10.4): supplier, product, geography, currency, contract
    /// term, quantity tier and purchase-date window — seven of the eleven named dimensions, always
    /// more than supplier alone (AC-3). SKU is deliberately not required here: a fixture with no SKU
    /// recorded represents a product-level price that still applies regardless of SKU, so it is
    /// excluded only when both sides name a SKU and they disagree.
    /// </summary>
    private static bool IsBaselineMatch(Comparable comparable, BenchmarkQuery query) =>
        string.Equals(comparable.Supplier, query.Supplier, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(comparable.Product, query.Product, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(comparable.Geography, query.Geography, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(comparable.Currency, query.Currency, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(comparable.Term, query.Term, StringComparison.OrdinalIgnoreCase) &&
        query.Quantity >= comparable.MinQuantity && query.Quantity <= comparable.MaxQuantity &&
        IsWithinPurchaseDateWindow(comparable, query) &&
        (comparable.Sku is null || query.Sku is null ||
            string.Equals(comparable.Sku, query.Sku, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <see langword="true"/> only when both the query and the comparable name the same SKU — the
    /// one optional, confidence-boosting dimension beyond <see cref="IsBaselineMatch"/>'s required
    /// seven.
    /// </summary>
    private static bool SkuActuallyMatched(Comparable comparable, BenchmarkQuery query)
    {
        var comparableSku = comparable.Sku;
        var querySku = query.Sku;

        return comparableSku is not null && querySku is not null &&
            string.Equals(comparableSku, querySku, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <see langword="true"/> when <paramref name="query"/>'s purchase date falls within
    /// <see cref="PurchaseDateWindowDays"/> of when <paramref name="comparable"/> was last
    /// refreshed — spec §10.3's own reason for carrying <see cref="BenchmarkQuery.PurchaseDate"/> at
    /// all: "so comparables can be filtered to a relevant window."
    /// </summary>
    private static bool IsWithinPurchaseDateWindow(Comparable comparable, BenchmarkQuery query)
    {
        var purchaseDateUtc = query.PurchaseDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var daysFromRefresh = Math.Abs((purchaseDateUtc - comparable.UpdatedAt.UtcDateTime).TotalDays);

        return daysFromRefresh <= PurchaseDateWindowDays;
    }

    /// <summary>Builds the confident (<see cref="BenchmarkResult.HasSufficientData"/>) outcome for a
    /// fixture that cleared <see cref="IsBaselineMatch"/> (AC-1).</summary>
    private static BenchmarkResult BuildConfidentResult(BenchmarkQuery query, Comparable match)
    {
        var dimensions = BuildMatchedDimensions(SkuActuallyMatched(match, query));
        var totalPossibleDimensions = query.Sku is null ? 7 : 8;

        return new BenchmarkResult(
            Distribution: new BenchmarkDistribution(match.P25, match.P50, match.P75),
            Metric: match.Metric,
            Currency: match.Currency,
            Confidence: ComputeConfidence(dimensions.Count, totalPossibleDimensions, match.SampleSize),
            Source: SourceName,
            UpdatedAt: match.UpdatedAt,
            ComparisonDimensions: dimensions,
            SampleSize: match.SampleSize);
    }

    /// <summary>
    /// Builds the explicit "insufficient market data" outcome (ADR-001; AC-3): <see
    /// cref="BenchmarkResult.Distribution"/> is always <see langword="null"/> here. When <paramref
    /// name="weakMatch"/> is not <see langword="null"/>, provenance still names the real (weak)
    /// comparable's metric/sample size/refresh date; when it is <see langword="null"/>, this adapter
    /// has nothing at all to say beyond the requested currency.
    /// </summary>
    private static BenchmarkResult BuildInsufficientDataResult(BenchmarkQuery query, Comparable? weakMatch)
    {
        if (weakMatch is null)
        {
            return new BenchmarkResult(
                Distribution: null,
                Metric: UnknownMetric,
                Currency: query.Currency,
                Confidence: 0d,
                Source: SourceName,
                UpdatedAt: CatalogUpdatedAt,
                ComparisonDimensions: [],
                SampleSize: null);
        }

        var totalPossibleDimensions = query.Sku is null ? 7 : 8;
        const int weakMatchedDimensionCount = 2; // Supplier + Product only.

        return new BenchmarkResult(
            Distribution: null,
            Metric: weakMatch.Metric,
            Currency: query.Currency,
            Confidence: ComputeConfidence(weakMatchedDimensionCount, totalPossibleDimensions, weakMatch.SampleSize),
            Source: SourceName,
            UpdatedAt: weakMatch.UpdatedAt,
            ComparisonDimensions: [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Product],
            SampleSize: weakMatch.SampleSize);
    }

    /// <summary>
    /// Raffa's own confidence score (spec §10.3), not a provider-reported one: how many of the
    /// possible dimensions this result actually matched on, scaled down when the underlying sample
    /// size is thin (<see cref="FullConfidenceSampleSize"/>).
    /// </summary>
    private static double ComputeConfidence(int matchedDimensionCount, int totalPossibleDimensions, int sampleSize)
    {
        var dimensionScore = matchedDimensionCount / (double)totalPossibleDimensions;
        var sampleSizeFactor = Math.Min(1.0, sampleSize / (double)FullConfidenceSampleSize);

        return Math.Round(dimensionScore * sampleSizeFactor, 2);
    }

    /// <summary>The always-more-than-supplier dimension set for a confident result (AC-3): the six
    /// required baseline dimensions, plus SKU when <paramref name="skuMatched"/>.</summary>
    private static IReadOnlyCollection<BenchmarkComparisonDimension> BuildMatchedDimensions(bool skuMatched)
    {
        List<BenchmarkComparisonDimension> dimensions =
        [
            BenchmarkComparisonDimension.Supplier,
            BenchmarkComparisonDimension.Product,
            BenchmarkComparisonDimension.Geography,
            BenchmarkComparisonDimension.Currency,
            BenchmarkComparisonDimension.ContractTerm,
            BenchmarkComparisonDimension.QuantityTier,
            BenchmarkComparisonDimension.PurchaseDate,
        ];

        if (skuMatched)
        {
            dimensions.Add(BenchmarkComparisonDimension.Sku);
        }

        return dimensions;
    }
}
