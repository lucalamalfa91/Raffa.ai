using Raffa.Benchmark.Contracts;
using Raffa.Market.Benchmark;
using Raffa.Market.Mock;
using Raffa.SharedKernel;

namespace Raffa.Market.Tests;

/// <summary>
/// Proves <see cref="MarketFeedBenchmarkAdapter"/>'s matching/trust rules against the real,
/// checked-in mock fixture (<see cref="MockMarketIntelligenceProvider"/>) — not a hand-rolled
/// stand-in dataset — so a change to the fixture that breaks one of these named scenarios (the
/// Allianz row, the Swiss Re thin row, the two conflicting AWS EC2 SKUs) is caught here.
/// </summary>
public class MarketFeedBenchmarkAdapterTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

    private readonly MarketFeedBenchmarkAdapter _adapter =
        new(new MockMarketIntelligenceProvider(), new FixedClock(FixedNow));

    [Fact]
    public void Name_is_market_feed()
    {
        Assert.Equal("market-feed", _adapter.Name);
        Assert.Equal(MarketFeedBenchmarkAdapter.AdapterName, _adapter.Name);
    }

    [Fact]
    public async Task Allianz_class_query_returns_confident_distribution_with_mock_provenance()
    {
        var query = new BenchmarkQuery(
            Supplier: "Allianz",
            Product: "Commercial Property Insurance",
            Sku: null,
            Geography: "CH",
            Quantity: 1m,
            Term: "12 months",
            Currency: "CHF",
            PurchaseDate: new DateOnly(2026, 6, 1));

        var result = await _adapter.GetBenchmarkAsync(query);

        Assert.True(result.IsSuccess);
        var benchmark = result.Value;
        Assert.True(benchmark.HasSufficientData);
        Assert.NotNull(benchmark.Distribution);
        Assert.Equal(85000m, benchmark.Distribution!.P25);
        Assert.Equal(98000m, benchmark.Distribution.P50);
        Assert.Equal(112000m, benchmark.Distribution.P75);
        Assert.Contains("mock", benchmark.Source, StringComparison.Ordinal);
        Assert.Contains(BenchmarkComparisonDimension.Supplier, benchmark.ComparisonDimensions);
        Assert.Contains(BenchmarkComparisonDimension.Product, benchmark.ComparisonDimensions);
        Assert.True(benchmark.SampleSize >= 5);
    }

    /// <summary>
    /// Swiss Re carries exactly one fixture row (sampleSize 2) with no same-supplier/same-product
    /// sibling — the cleanest possible proof that a thin row abstains rather than the weak-match
    /// fallback quietly "rescuing" the query onto a larger, different row.
    /// </summary>
    [Fact]
    public async Task Thin_row_query_returns_insufficient_data()
    {
        var query = new BenchmarkQuery(
            Supplier: "Swiss Re",
            Product: "Reinsurance Treaty",
            Sku: null,
            Geography: "CH",
            Quantity: 1m,
            Term: "12 months",
            Currency: "CHF",
            PurchaseDate: new DateOnly(2026, 2, 1));

        var result = await _adapter.GetBenchmarkAsync(query);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasSufficientData);
        Assert.Null(result.Value.Distribution);
        // Still honest, non-fabricated provenance (ADR-001) — the weak match's own real sample size.
        Assert.Equal(2, result.Value.SampleSize);
        Assert.Contains("mock", result.Value.Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// AWS EC2 in the US has two fixture rows with different, concrete SKUs
    /// (<c>m5.large</c>, <c>c5.xlarge</c>). A query naming a third SKU neither row carries must not
    /// strong-match either one — spec §10.4's "matching must use more than supplier name" applied
    /// down to "a named SKU must actually agree, not just be present on one side".
    /// </summary>
    [Fact]
    public async Task Unknown_sku_query_returns_insufficient_data()
    {
        var query = new BenchmarkQuery(
            Supplier: "AWS",
            Product: "EC2 Compute",
            Sku: "m5.unknown-instance-type",
            Geography: "US",
            Quantity: 10m,
            Term: "12 months",
            Currency: "USD",
            PurchaseDate: new DateOnly(2026, 7, 1));

        var result = await _adapter.GetBenchmarkAsync(query);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasSufficientData);
        Assert.Null(result.Value.Distribution);
    }

    [Fact]
    public async Task Completely_unmatched_supplier_returns_insufficient_data_with_no_dimensions()
    {
        var query = new BenchmarkQuery(
            Supplier: "Definitely Not In The Feed Inc",
            Product: "Nonexistent Product",
            Sku: null,
            Geography: "US",
            Quantity: 1m,
            Term: "12 months",
            Currency: "USD",
            PurchaseDate: new DateOnly(2026, 1, 1));

        var result = await _adapter.GetBenchmarkAsync(query);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasSufficientData);
        Assert.Null(result.Value.SampleSize);
        Assert.Empty(result.Value.ComparisonDimensions);
        Assert.Equal(0d, result.Value.Confidence);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
