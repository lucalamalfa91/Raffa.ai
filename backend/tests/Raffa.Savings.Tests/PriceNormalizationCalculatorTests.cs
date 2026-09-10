using System.Reflection;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Savings.Application;
using Raffa.Savings.Domain;

namespace Raffa.Savings.Tests;

/// <summary>
/// Proves task E04/F02/US01/T01's execution step: <see cref="PriceNormalizationCalculator"/>
/// normalizes a current unit price and, when the benchmark supports it, computes a percentile
/// rank, recommended target range and savings range — parent story us-01-price-normalization AC-1
/// ("Normalize current unit price (currency/quantity/term) before comparison") and AC-2 ("Compute
/// percentile, recommended target, and savings range deterministically") — with no database, no
/// HTTP call and no LLM call anywhere in the path.
/// </summary>
public sealed class PriceNormalizationCalculatorTests
{
    private readonly PriceNormalizationCalculator _calculator = new();

    private static readonly DateTimeOffset UpdatedAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static BenchmarkQuery Query(decimal quantity = 1m, string currency = "USD", string term = "12 months") =>
        new(
            Supplier: "AWS",
            Product: "Compute",
            Sku: null,
            Geography: "US",
            Quantity: quantity,
            Term: term,
            Currency: currency,
            PurchaseDate: new DateOnly(2026, 1, 1));

    private static BenchmarkResult BenchmarkWith(
        BenchmarkDistribution? distribution, string currency = "USD", double confidence = 0.8) =>
        new(
            Distribution: distribution,
            Metric: "per seat / year",
            Currency: currency,
            Confidence: confidence,
            Source: "fixture",
            UpdatedAt: UpdatedAt,
            ComparisonDimensions: [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Product]);

    /// <summary>Builds a request whose <see cref="PriceComparisonRequest.CurrentTotalCost"/> is
    /// exactly <paramref name="unitPrice"/> times <paramref name="quantity"/>, so every test can
    /// reason directly in per-unit terms. Always carries a well-ordered distribution — use
    /// <see cref="RequestWithDistribution"/> or <see cref="RequestWithoutDistribution"/> for tests
    /// that need a specific or absent one (a nullable optional parameter here could not
    /// distinguish "use the default" from "a caller explicitly wants null").</summary>
    private static PriceComparisonRequest Request(
        decimal unitPrice, decimal quantity = 1m, string currency = "USD") =>
        RequestWithDistribution(unitPrice, new BenchmarkDistribution(390m, 430m, 470m), quantity, currency);

    private static PriceComparisonRequest RequestWithDistribution(
        decimal unitPrice, BenchmarkDistribution distribution, decimal quantity = 1m, string currency = "USD") =>
        new(
            Query: Query(quantity, currency),
            CurrentTotalCost: unitPrice * quantity,
            Benchmark: BenchmarkWith(distribution, currency));

    private static PriceComparisonRequest RequestWithoutDistribution(
        decimal unitPrice, decimal quantity = 1m, string currency = "USD") =>
        new(
            Query: Query(quantity, currency),
            CurrentTotalCost: unitPrice * quantity,
            Benchmark: BenchmarkWith(distribution: null, currency));

    // ----- AC-1: quantity normalization -----

    [Fact]
    public void NormalizedUnitPrice_divides_total_cost_by_quantity()
    {
        var request = new PriceComparisonRequest(
            Query(quantity: 10m),
            CurrentTotalCost: 5_200m,
            Benchmark: BenchmarkWith(new BenchmarkDistribution(390m, 430m, 470m)));

        var result = _calculator.Compare(request);

        Assert.Equal(PriceComparisonStatus.Compared, result.Status);
        Assert.Equal(520m, result.NormalizedUnitPrice);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void InvalidQuantity_computes_nothing_not_even_the_normalized_price(decimal quantity)
    {
        var request = new PriceComparisonRequest(
            Query(quantity),
            CurrentTotalCost: 1_000m,
            Benchmark: BenchmarkWith(new BenchmarkDistribution(390m, 430m, 470m)));

        var result = _calculator.Compare(request);

        Assert.Equal(PriceComparisonStatus.InvalidQuantity, result.Status);
        Assert.Null(result.NormalizedUnitPrice);
        Assert.Null(result.PercentileRank);
        Assert.Contains("Appendix C rule 10", result.Explanation);
    }

    // ----- AC-1: currency normalization -----

    [Fact]
    public void CurrencyMismatch_still_reports_the_normalized_unit_price_but_no_comparison()
    {
        var request = new PriceComparisonRequest(
            Query(quantity: 1m, currency: "EUR"),
            CurrentTotalCost: 520m,
            Benchmark: BenchmarkWith(new BenchmarkDistribution(390m, 430m, 470m), currency: "USD"));

        var result = _calculator.Compare(request);

        Assert.Equal(PriceComparisonStatus.CurrencyMismatch, result.Status);
        Assert.Equal(520m, result.NormalizedUnitPrice);
        Assert.Null(result.PercentileRank);
        Assert.Null(result.RecommendedTargetLow);
        Assert.Null(result.SavingsRangeLow);
        Assert.Contains("EUR", result.Explanation);
        Assert.Contains("USD", result.Explanation);
    }

    [Fact]
    public void Currency_comparison_is_case_insensitive()
    {
        var request = new PriceComparisonRequest(
            Query(quantity: 1m, currency: "usd"),
            CurrentTotalCost: 400m,
            Benchmark: BenchmarkWith(new BenchmarkDistribution(390m, 430m, 470m), currency: "USD"));

        var result = _calculator.Compare(request);

        Assert.Equal(PriceComparisonStatus.Compared, result.Status);
    }

    // ----- AC-2: insufficient / malformed benchmark data -----

    [Fact]
    public void InsufficientBenchmarkData_when_the_benchmark_has_no_distribution()
    {
        var request = RequestWithoutDistribution(unitPrice: 520m);

        var result = _calculator.Compare(request);

        Assert.Equal(PriceComparisonStatus.InsufficientBenchmarkData, result.Status);
        Assert.Equal(520m, result.NormalizedUnitPrice);
        Assert.Null(result.PercentileRank);
        Assert.Null(result.RecommendedTargetLow);
        Assert.Null(result.SavingsRangeLow);
        Assert.Null(result.TotalSavingsRangeLow);
    }

    [Theory]
    [MemberData(nameof(MalformedDistributions))]
    public void InsufficientBenchmarkData_when_the_distribution_is_not_well_ordered(BenchmarkDistribution malformed)
    {
        var request = RequestWithDistribution(unitPrice: 400m, malformed);

        var result = _calculator.Compare(request);

        Assert.Equal(PriceComparisonStatus.InsufficientBenchmarkData, result.Status);
        Assert.Equal(400m, result.NormalizedUnitPrice);
        Assert.Null(result.PercentileRank);
        Assert.Contains("not well-ordered", result.Explanation);
    }

    public static TheoryData<BenchmarkDistribution> MalformedDistributions() =>
    [
        new BenchmarkDistribution(430m, 390m, 470m), // P50 < P25
        new BenchmarkDistribution(390m, 470m, 430m), // P75 < P50
        new BenchmarkDistribution(470m, 430m, 390m), // fully inverted
    ];

    // ----- AC-2: percentile interpolation -----

    [Theory]
    [InlineData(300, 25)]   // below P25 -> clamped
    [InlineData(390, 25)]   // exactly P25
    [InlineData(410, 37.5)] // interpolated between P25 and P50
    [InlineData(430, 50)]   // exactly P50
    [InlineData(450, 62.5)] // interpolated between P50 and P75
    [InlineData(470, 75)]   // exactly P75
    [InlineData(520, 75)]   // above P75 -> clamped
    public void PercentileRank_is_interpolated_between_the_known_markers_and_clamped_at_the_ends(
        double price, double expectedPercentile)
    {
        var request = Request((decimal)price);

        var result = _calculator.Compare(request);

        Assert.Equal(PriceComparisonStatus.Compared, result.Status);
        Assert.Equal((decimal)expectedPercentile, result.PercentileRank);
    }

    // ----- AC-2: recommended target range + savings range -----

    [Fact]
    public void Above_market_price_gets_a_P25_to_P50_target_and_a_positive_savings_range()
    {
        // Loosely inspired by product spec §11.2's example shape (a quote priced above market),
        // not its exact figures — the spec gives an illustrative example, not a formula.
        var request = Request(unitPrice: 520m);

        var result = _calculator.Compare(request);

        Assert.Equal(390m, result.RecommendedTargetLow);
        Assert.Equal(430m, result.RecommendedTargetHigh);
        Assert.Equal(90m, result.SavingsRangeLow);  // 520 - 430
        Assert.Equal(130m, result.SavingsRangeHigh); // 520 - 390
    }

    [Fact]
    public void Price_already_below_P25_never_recommends_paying_more_and_reports_zero_savings()
    {
        var request = Request(unitPrice: 350m); // below P25 (390)

        var result = _calculator.Compare(request);

        Assert.Equal(350m, result.RecommendedTargetLow);
        Assert.Equal(350m, result.RecommendedTargetHigh);
        Assert.Equal(0m, result.SavingsRangeLow);
        Assert.Equal(0m, result.SavingsRangeHigh);
    }

    [Fact]
    public void Price_between_P25_and_P50_targets_down_to_P25_at_most_and_never_above_current()
    {
        var request = Request(unitPrice: 410m); // between P25 (390) and P50 (430)

        var result = _calculator.Compare(request);

        Assert.Equal(390m, result.RecommendedTargetLow);
        Assert.Equal(410m, result.RecommendedTargetHigh); // capped at current price, not P50
        Assert.Equal(0m, result.SavingsRangeLow);
        Assert.Equal(20m, result.SavingsRangeHigh);
    }

    [Fact]
    public void TotalSavingsRange_multiplies_the_per_unit_range_by_quantity()
    {
        var request = new PriceComparisonRequest(
            Query(quantity: 10m),
            CurrentTotalCost: 5_200m, // 520/unit * 10
            Benchmark: BenchmarkWith(new BenchmarkDistribution(390m, 430m, 470m)));

        var result = _calculator.Compare(request);

        Assert.Equal(90m, result.SavingsRangeLow);
        Assert.Equal(130m, result.SavingsRangeHigh);
        Assert.Equal(900m, result.TotalSavingsRangeLow);
        Assert.Equal(1_300m, result.TotalSavingsRangeHigh);
    }

    // ----- Invariants across scenarios -----

    [Theory]
    [MemberData(nameof(ComparedScenarios))]
    public void Recommended_target_never_exceeds_the_normalized_price_and_savings_are_never_negative(
        PriceComparisonResult result)
    {
        Assert.Equal(PriceComparisonStatus.Compared, result.Status);
        Assert.True(result.RecommendedTargetLow <= result.RecommendedTargetHigh);
        Assert.True(result.RecommendedTargetHigh <= result.NormalizedUnitPrice);
        Assert.True(result.SavingsRangeLow >= 0m);
        Assert.True(result.SavingsRangeHigh >= result.SavingsRangeLow);
        Assert.InRange(result.PercentileRank!.Value, 0m, 100m);
    }

    public static TheoryData<PriceComparisonResult> ComparedScenarios()
    {
        var calculator = new PriceNormalizationCalculator();
        var prices = new[] { 100m, 300m, 390m, 410m, 430m, 450m, 470m, 520m, 1_000m };

        var data = new TheoryData<PriceComparisonResult>();
        foreach (var price in prices)
        {
            data.Add(calculator.Compare(Request(price)));
        }

        return data;
    }

    // ----- Determinism (Appendix C rule 6) -----

    [Fact]
    public void Same_inputs_produce_the_same_result_every_time()
    {
        var request = Request(unitPrice: 450m);

        var first = _calculator.Compare(request);
        var second = _calculator.Compare(request);

        // PriceComparisonResult is a record: this is value equality across every field, including
        // Explanation and the echoed Benchmark.
        Assert.Equal(first, second);
    }

    // ----- Null-argument handling -----

    [Fact]
    public void Rejects_a_null_request_argument()
    {
        Assert.Throws<ArgumentNullException>(() => _calculator.Compare(null!));
    }

    [Fact]
    public void Rejects_a_null_request_list_in_the_batch_form()
    {
        Assert.Throws<ArgumentNullException>(() => _calculator.CompareMany(null!));
    }

    // ----- Batch form -----

    [Fact]
    public void CompareMany_computes_one_result_per_request_preserving_order()
    {
        var first = Request(unitPrice: 300m);
        var second = Request(unitPrice: 520m);
        var third = RequestWithoutDistribution(unitPrice: 520m);

        var results = _calculator.CompareMany([first, second, third]);

        Assert.Equal(3, results.Count);
        Assert.Equal(_calculator.Compare(first), results[0]);
        Assert.Equal(_calculator.Compare(second), results[1]);
        Assert.Equal(PriceComparisonStatus.InsufficientBenchmarkData, results[2].Status);
    }

    // ----- Appendix C rule 3: no live dependency on the Benchmark Service -----
    //
    // Same structural proof Raffa.Renewals.Tests.PriorityScoreCalculatorTests
    // .Calculator_has_no_dependency_on_the_Benchmark_Service uses: this calculator's public API can
    // only ever accept an already-fetched BenchmarkResult (a plain data contract), never the live
    // IBenchmarkService itself, so Appendix C rule 3 ("never call a benchmark provider directly
    // from renewal, savings or quote business logic") can never become an accidental provider call
    // from this class.
    [Fact]
    public void Calculator_never_depends_on_the_live_Benchmark_Service_interface()
    {
        var type = typeof(PriceNormalizationCalculator);

        var constructorParamsFromService = type.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Where(p => p.ParameterType == typeof(IBenchmarkService))
            .ToList();

        var methodParamsFromService = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters())
            .Where(p => p.ParameterType == typeof(IBenchmarkService))
            .ToList();

        Assert.Empty(constructorParamsFromService);
        Assert.Empty(methodParamsFromService);
    }
}
