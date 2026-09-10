using Raffa.Benchmark.Contracts;
using Raffa.Savings.Application;
using Raffa.Savings.Domain;

namespace Raffa.Savings.Tests;

/// <summary>
/// Proves parent story us-01-price-normalization AC-3 ("Show confidence + provenance on the
/// comparison") end to end: <see cref="PriceComparisonResult.Provenance"/> is reachable — and
/// matches <see cref="SavingsProvenanceClassifier.FromBenchmark"/> exactly — regardless of
/// <see cref="PriceComparisonResult.Status"/>, the same "confidence/source/updated-at/comparison
/// dimensions are always populated" guarantee <see cref="BenchmarkResult"/>'s own doc comment makes
/// (task E04/F02/US01/T02, the wave-spec's <c>savings-provenance</c> artifact).
/// </summary>
public sealed class PriceComparisonResultProvenanceTests
{
    private readonly PriceNormalizationCalculator _calculator = new();

    private static BenchmarkQuery Query(decimal quantity = 1m, string currency = "USD") =>
        new(
            Supplier: "AWS",
            Product: "Compute",
            Sku: null,
            Geography: "US",
            Quantity: quantity,
            Term: "12 months",
            Currency: currency,
            PurchaseDate: new DateOnly(2026, 1, 1));

    private static BenchmarkResult BenchmarkWith(BenchmarkDistribution? distribution, string currency = "USD") =>
        new(
            Distribution: distribution,
            Metric: "per seat / year",
            Currency: currency,
            Confidence: 0.82,
            Source: "fixture",
            UpdatedAt: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            ComparisonDimensions: [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Product],
            SampleSize: 340);

    [Fact]
    public void Provenance_is_available_and_correct_when_the_comparison_succeeds()
    {
        var benchmark = BenchmarkWith(new BenchmarkDistribution(390m, 430m, 470m));
        var request = new PriceComparisonRequest(Query(), CurrentTotalCost: 520m, Benchmark: benchmark);

        var result = _calculator.Compare(request);

        Assert.Equal(PriceComparisonStatus.Compared, result.Status);
        Assert.Equal(SavingsProvenanceClassifier.FromBenchmark(benchmark), result.Provenance);
        Assert.Equal(SavingsConfidenceLevel.High, result.Provenance.ConfidenceLevel);
    }

    [Fact]
    public void Provenance_is_still_available_when_the_quantity_is_invalid()
    {
        var benchmark = BenchmarkWith(new BenchmarkDistribution(390m, 430m, 470m));
        var request = new PriceComparisonRequest(Query(quantity: 0m), CurrentTotalCost: 520m, Benchmark: benchmark);

        var result = _calculator.Compare(request);

        Assert.Equal(PriceComparisonStatus.InvalidQuantity, result.Status);
        Assert.Equal(SavingsProvenanceClassifier.FromBenchmark(benchmark), result.Provenance);
    }

    [Fact]
    public void Provenance_is_still_available_on_a_currency_mismatch()
    {
        var benchmark = BenchmarkWith(new BenchmarkDistribution(390m, 430m, 470m), currency: "USD");
        var request = new PriceComparisonRequest(Query(currency: "EUR"), CurrentTotalCost: 520m, Benchmark: benchmark);

        var result = _calculator.Compare(request);

        Assert.Equal(PriceComparisonStatus.CurrencyMismatch, result.Status);
        Assert.Equal(SavingsProvenanceClassifier.FromBenchmark(benchmark), result.Provenance);
    }

    [Fact]
    public void Provenance_is_still_available_when_benchmark_data_is_insufficient()
    {
        var benchmark = BenchmarkWith(distribution: null);
        var request = new PriceComparisonRequest(Query(), CurrentTotalCost: 520m, Benchmark: benchmark);

        var result = _calculator.Compare(request);

        Assert.Equal(PriceComparisonStatus.InsufficientBenchmarkData, result.Status);
        Assert.Equal(SavingsProvenanceClassifier.FromBenchmark(benchmark), result.Provenance);
    }

    [Fact]
    public void Provenance_depends_only_on_Benchmark_never_on_any_other_field()
    {
        // Provenance is a computed property deriving solely from Benchmark (see its own doc
        // comment) -- two results that share the same Benchmark but differ in every other field
        // (Status, the arithmetic outputs, Explanation) must still report identical Provenance,
        // proving there is no separate, potentially mismatched value backing it.
        var benchmark = BenchmarkWith(new BenchmarkDistribution(390m, 430m, 470m));

        var compared = new PriceComparisonResult(
            PriceComparisonStatus.Compared, 520m, 75m, 390m, 430m, 90m, 130m, 90m, 130m,
            benchmark, "compared");
        var invalidQuantity = new PriceComparisonResult(
            PriceComparisonStatus.InvalidQuantity, null, null, null, null, null, null, null, null,
            benchmark, "invalid quantity");

        Assert.Equal(compared.Provenance, invalidQuantity.Provenance);
    }
}
