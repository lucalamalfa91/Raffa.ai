using Raffa.Benchmark.Adapters;
using Raffa.Benchmark.Configuration;
using Raffa.Benchmark.Contracts;
using Raffa.Benchmark.Fixtures;

namespace Raffa.Benchmark.Tests;

/// <summary>
/// Proves task E04/F01/US02/T01's own objective — "Fixture adapter returning P25/P50/P75 +
/// confidence + provenance" — against <see cref="FixtureBenchmarkAdapter"/>, the first
/// <see cref="IBenchmarkService"/> implementation (story us-02-fixture-adapter; ADR-001). Also
/// proves task E04/F01/US02/T02's own objective ("fixture-confidence": weak-comparable abstain, no
/// paid API) — the statistical (thin-sample) abstain floor alongside T01's dimensional one, and the
/// <see cref="IBenchmarkProviderAdapter"/> registration seam this task completes.
/// </summary>
public class FixtureBenchmarkAdapterTests
{
    private static readonly DateOnly StandardPurchaseDate = new(2026, 8, 1);

    private readonly FixtureBenchmarkAdapter _adapter = new();

    [Fact]
    public async Task Returns_p25_p50_p75_confidence_and_provenance_for_a_matched_fixture()
    {
        var query = new BenchmarkQuery(
            Supplier: "AWS", Product: "EC2 Compute", Sku: "m5.large", Geography: "US",
            Quantity: 50m, Term: "12 months", Currency: "USD", PurchaseDate: StandardPurchaseDate);

        var result = await _adapter.GetBenchmarkAsync(query);

        Assert.True(result.IsSuccess);
        var benchmark = result.Value;

        Assert.True(benchmark.HasSufficientData);
        Assert.Equal(new BenchmarkDistribution(0.085m, 0.096m, 0.108m), benchmark.Distribution);
        Assert.Equal("per instance-hour", benchmark.Metric);
        Assert.Equal("USD", benchmark.Currency);
        Assert.Equal("fixture", benchmark.Source);
        Assert.Equal(340, benchmark.SampleSize);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), benchmark.UpdatedAt);
        Assert.Equal(1.0, benchmark.Confidence, precision: 2);

        // AC-3 / spec §10.4: matching (and therefore reported provenance) must use more than
        // supplier name alone.
        Assert.True(benchmark.ComparisonDimensions.Count > 1);
        Assert.Contains(BenchmarkComparisonDimension.Supplier, benchmark.ComparisonDimensions);
        Assert.Contains(BenchmarkComparisonDimension.Product, benchmark.ComparisonDimensions);
        Assert.Contains(BenchmarkComparisonDimension.Sku, benchmark.ComparisonDimensions);
        Assert.Contains(BenchmarkComparisonDimension.Geography, benchmark.ComparisonDimensions);
        Assert.Contains(BenchmarkComparisonDimension.Currency, benchmark.ComparisonDimensions);
        Assert.Contains(BenchmarkComparisonDimension.ContractTerm, benchmark.ComparisonDimensions);
        Assert.Contains(BenchmarkComparisonDimension.QuantityTier, benchmark.ComparisonDimensions);
        Assert.Contains(BenchmarkComparisonDimension.PurchaseDate, benchmark.ComparisonDimensions);
    }

    [Fact]
    public async Task Geography_and_currency_drive_a_different_distribution_for_the_same_supplier_and_product()
    {
        var usQuery = new BenchmarkQuery("AWS", "EC2 Compute", "m5.large", "US", 50m, "12 months", "USD", StandardPurchaseDate);
        var euQuery = new BenchmarkQuery("AWS", "EC2 Compute", "m5.large", "EU", 50m, "12 months", "EUR", StandardPurchaseDate);

        var usResult = (await _adapter.GetBenchmarkAsync(usQuery)).Value;
        var euResult = (await _adapter.GetBenchmarkAsync(euQuery)).Value;

        Assert.True(usResult.HasSufficientData);
        Assert.True(euResult.HasSufficientData);
        Assert.NotEqual(usResult.Distribution, euResult.Distribution);
        Assert.Equal("EUR", euResult.Currency);
    }

    [Fact]
    public async Task Contract_term_drives_a_different_distribution_for_the_same_supplier_and_product()
    {
        var twelveMonthQuery = new BenchmarkQuery("Salesforce", "Sales Cloud Enterprise", null, "US", 100m, "12 months", "USD", StandardPurchaseDate);
        var thirtySixMonthQuery = new BenchmarkQuery("Salesforce", "Sales Cloud Enterprise", null, "US", 100m, "36 months", "USD", StandardPurchaseDate);

        var shortTerm = (await _adapter.GetBenchmarkAsync(twelveMonthQuery)).Value;
        var longTerm = (await _adapter.GetBenchmarkAsync(thirtySixMonthQuery)).Value;

        Assert.True(shortTerm.HasSufficientData);
        Assert.True(longTerm.HasSufficientData);
        Assert.NotEqual(shortTerm.Distribution, longTerm.Distribution);

        // The longer-commitment fixture is priced lower in this catalog — a real signal, not a
        // coincidence of match order.
        Assert.True(longTerm.Distribution!.P50 < shortTerm.Distribution!.P50);
    }

    [Fact]
    public async Task Sku_agnostic_query_matches_a_product_level_fixture_without_reporting_a_sku_dimension()
    {
        var query = new BenchmarkQuery("Salesforce", "Sales Cloud Enterprise", null, "US", 100m, "12 months", "USD", StandardPurchaseDate);

        var result = (await _adapter.GetBenchmarkAsync(query)).Value;

        Assert.True(result.HasSufficientData);
        Assert.DoesNotContain(BenchmarkComparisonDimension.Sku, result.ComparisonDimensions);
        Assert.Equal(1.0, result.Confidence, precision: 2);
    }

    [Fact]
    public async Task Confidence_is_reduced_for_a_thin_sample_size_even_when_data_is_sufficient()
    {
        var query = new BenchmarkQuery("Snowflake", "Standard Compute Credits", null, "US", 500m, "12 months", "USD", StandardPurchaseDate);

        var result = (await _adapter.GetBenchmarkAsync(query)).Value;

        Assert.True(result.HasSufficientData);
        Assert.Equal(18, result.SampleSize);
        Assert.Equal(0.36, result.Confidence, precision: 2);
    }

    [Fact]
    public async Task Statistically_weak_comparable_still_abstains_even_when_every_baseline_dimension_matches()
    {
        // Task E04/F01/US02/T02 objective ("fixture-confidence": weak-comparable abstain). The
        // Notion fixture clears every one of IsBaselineMatch's seven required dimensions for this
        // query, yet its sample size (4) is far below MinimumViableSampleSize — dimensionally strong,
        // statistically weak. AC-3 requires "insufficient market data", not a precise-looking number
        // just because every field happened to line up.
        var query = new BenchmarkQuery(
            Supplier: "Notion", Product: "Enterprise Plan", Sku: null, Geography: "US",
            Quantity: 100m, Term: "12 months", Currency: "USD", PurchaseDate: StandardPurchaseDate);

        var result = (await _adapter.GetBenchmarkAsync(query)).Value;

        Assert.False(result.HasSufficientData);
        Assert.Null(result.Distribution);

        // Provenance is still real and honest (the same weak-comparable fallback a dimensionally
        // weak match uses) — a caller can see *why* this abstained, not just that it did.
        Assert.Equal("per seat / year", result.Metric);
        Assert.Equal(4, result.SampleSize);
        Assert.Equal(2, result.ComparisonDimensions.Count);
        Assert.Contains(BenchmarkComparisonDimension.Supplier, result.ComparisonDimensions);
        Assert.Contains(BenchmarkComparisonDimension.Product, result.ComparisonDimensions);
        Assert.True(result.Confidence < 0.05);
        Assert.Equal("fixture", result.Source);
    }

    [Fact]
    public async Task Weak_comparable_with_wrong_geography_yields_insufficient_data_not_a_fabricated_number()
    {
        var query = new BenchmarkQuery("AWS", "EC2 Compute", null, "APAC", 50m, "12 months", "USD", StandardPurchaseDate);

        var result = (await _adapter.GetBenchmarkAsync(query)).Value;

        Assert.False(result.HasSufficientData);
        Assert.Null(result.Distribution);

        // A weak comparable still exists (same supplier + product, the US fixture) — provenance is
        // real, just not enough alignment to publish a distribution (ADR-001; spec §10.4).
        Assert.Equal("per instance-hour", result.Metric);
        Assert.Equal(340, result.SampleSize);
        Assert.Equal(2, result.ComparisonDimensions.Count);
        Assert.Contains(BenchmarkComparisonDimension.Supplier, result.ComparisonDimensions);
        Assert.Contains(BenchmarkComparisonDimension.Product, result.ComparisonDimensions);
        Assert.True(result.Confidence < 0.5);
    }

    [Fact]
    public async Task Quantity_outside_every_fixture_tier_yields_insufficient_data()
    {
        var query = new BenchmarkQuery("Salesforce", "Sales Cloud Enterprise", null, "US", 5000m, "12 months", "USD", StandardPurchaseDate);

        var result = (await _adapter.GetBenchmarkAsync(query)).Value;

        Assert.False(result.HasSufficientData);
        Assert.Null(result.Distribution);
        Assert.Equal(512, result.SampleSize); // the larger of the two weak Salesforce comparables.
    }

    [Fact]
    public async Task Purchase_date_far_outside_the_fixture_refresh_window_yields_insufficient_data()
    {
        var query = new BenchmarkQuery(
            "AWS", "EC2 Compute", "m5.large", "US", 50m, "12 months", "USD",
            PurchaseDate: new DateOnly(2020, 1, 1));

        var result = (await _adapter.GetBenchmarkAsync(query)).Value;

        Assert.False(result.HasSufficientData);
        Assert.Null(result.Distribution);
    }

    [Fact]
    public async Task Unknown_supplier_yields_insufficient_data_with_no_provenance_dimensions()
    {
        var query = new BenchmarkQuery("Acme Cloud Co", "Whatever Suite", null, "US", 10m, "12 months", "USD", StandardPurchaseDate);

        var result = (await _adapter.GetBenchmarkAsync(query)).Value;

        Assert.False(result.HasSufficientData);
        Assert.Null(result.Distribution);
        Assert.Equal("n/a", result.Metric);
        Assert.Equal("USD", result.Currency);
        Assert.Equal(0d, result.Confidence);
        Assert.Empty(result.ComparisonDimensions);
        Assert.Null(result.SampleSize);
        Assert.Equal("fixture", result.Source);
    }

    [Fact]
    public void Registers_under_the_same_name_the_registry_and_default_options_expect()
    {
        // Task E04/F01/US02/T02: FixtureBenchmarkAdapter now implements IBenchmarkProviderAdapter so
        // BenchmarkAdapterRegistry can dispatch to it by name; that name must match both this
        // adapter's own BenchmarkResult.Source literal and BenchmarkAdapterOptions.DefaultAdapterName,
        // or ServiceCollectionExtensions.AddBenchmarkModule's default wiring would resolve nothing.
        IBenchmarkProviderAdapter providerAdapter = _adapter;

        Assert.Equal("fixture", providerAdapter.Name);
        Assert.Equal(BenchmarkAdapterOptions.DefaultAdapterName, providerAdapter.Name);
    }

    [Fact]
    public void Assembly_never_references_a_paid_market_api_or_provider_sdk()
    {
        // AC-2; spec §10.2 "Strategic requirement": "No business module should depend on Tropic,
        // Vendr or any single provider schema." Proven at the compiled-assembly level (not just by
        // inspecting source), so a transitive package reference would also be caught.
        string[] forbiddenNameFragments =
        [
            "Azure", "Tropic", "Vendr", "OpenAI", "Google.Cloud", "Amazon", "AWSSDK",
        ];

        var referencedAssemblyNames = typeof(FixtureBenchmarkAdapter).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        var violations = referencedAssemblyNames
            .Where(name => forbiddenNameFragments.Any(fragment =>
                name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"Raffa.Benchmark must never reference a provider SDK: [{string.Join(", ", violations)}].");
    }
}
