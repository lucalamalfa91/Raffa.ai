using Raffa.Benchmark.Configuration;
using Raffa.Benchmark.Contracts;
using Raffa.Benchmark.Tests.TestSupport;
using Raffa.SharedKernel;

namespace Raffa.Benchmark.Tests;

/// <summary>
/// Proves task E04/F01/US01/T02's own objective: a pluggable provider-adapter registry behind
/// <see cref="IBenchmarkService"/>. <see cref="StubBenchmarkProviderAdapter"/> stands in for a real
/// adapter (fixture or otherwise) so these tests do not depend on us-02-fixture-adapter's own,
/// separate file scope.
/// </summary>
public class BenchmarkAdapterRegistryTests
{
    private static readonly BenchmarkQuery Query = new(
        Supplier: "AWS", Product: "EC2 Compute", Sku: null, Geography: "US",
        Quantity: 100m, Term: "12 months", Currency: "USD",
        PurchaseDate: new DateOnly(2026, 1, 15));

    private static readonly BenchmarkResult SuccessResult = new(
        Distribution: new BenchmarkDistribution(90m, 100m, 120m),
        Metric: "per seat / year",
        Currency: "USD",
        Confidence: 0.8,
        Source: "stub",
        UpdatedAt: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        ComparisonDimensions: [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Product]);

    [Fact]
    public async Task Dispatches_to_the_adapter_named_by_the_active_adapter_option()
    {
        var active = new StubBenchmarkProviderAdapter("fixture", Result<BenchmarkResult>.Success(SuccessResult));
        var other = new StubBenchmarkProviderAdapter("other", Result<BenchmarkResult>.Failure("must not be called"));
        var registry = new BenchmarkAdapterRegistry(
            [active, other], new BenchmarkAdapterOptions { ActiveAdapter = "fixture" });

        var result = await registry.GetBenchmarkAsync(Query);

        Assert.True(result.IsSuccess);
        Assert.Same(SuccessResult, result.Value);
        Assert.Equal(1, active.CallCount);
        Assert.Equal(0, other.CallCount);
        Assert.Same(Query, active.LastQuery);
    }

    [Fact]
    public async Task Adapter_name_lookup_is_case_insensitive()
    {
        var active = new StubBenchmarkProviderAdapter("Fixture", Result<BenchmarkResult>.Success(SuccessResult));
        var registry = new BenchmarkAdapterRegistry(
            [active], new BenchmarkAdapterOptions { ActiveAdapter = "fixture" });

        var result = await registry.GetBenchmarkAsync(Query);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Task E04/F01/US01/T02's own scope adds the registry but no concrete adapter (story
    /// us-02-fixture-adapter runs in the same wave-spec phase, in parallel, and is blind to this
    /// task) — this is the expected, honest "no adapter yet" state this task must leave the
    /// registry in, proven directly rather than assumed.
    /// </summary>
    [Fact]
    public async Task Fails_honestly_without_fabricating_when_no_adapter_is_registered()
    {
        var registry = new BenchmarkAdapterRegistry([], new BenchmarkAdapterOptions());

        var result = await registry.GetBenchmarkAsync(Query);

        Assert.True(result.IsFailure);
        Assert.Contains("fixture", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fails_honestly_when_the_configured_adapter_name_is_not_registered()
    {
        var registry = new BenchmarkAdapterRegistry(
            [new StubBenchmarkProviderAdapter("fixture", Result<BenchmarkResult>.Success(SuccessResult))],
            new BenchmarkAdapterOptions { ActiveAdapter = "paid-provider" });

        var result = await registry.GetBenchmarkAsync(Query);

        Assert.True(result.IsFailure);
        Assert.Contains("paid-provider", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_throws_on_duplicate_adapter_names()
    {
        var first = new StubBenchmarkProviderAdapter("fixture", Result<BenchmarkResult>.Success(SuccessResult));
        var second = new StubBenchmarkProviderAdapter("fixture", Result<BenchmarkResult>.Success(SuccessResult));

        Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkAdapterRegistry([first, second], new BenchmarkAdapterOptions()));
    }

    [Fact]
    public void Constructor_rejects_null_adapters_or_options()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new BenchmarkAdapterRegistry(null!, new BenchmarkAdapterOptions()));
        Assert.Throws<ArgumentNullException>(() =>
            new BenchmarkAdapterRegistry([], null!));
    }
}
