using Raffa.Benchmark.Adapters;
using Raffa.Benchmark.Contracts;
using Raffa.SharedKernel;

namespace Raffa.Benchmark.Tests.TestSupport;

/// <summary>
/// Deterministic <see cref="IBenchmarkProviderAdapter"/> test double — lets
/// <see cref="BenchmarkAdapterRegistry"/>'s own tests exercise dispatch-by-name without needing a
/// real (fixture or provider) adapter implementation, which is us-02-fixture-adapter's own,
/// separate file scope.
/// </summary>
public sealed class StubBenchmarkProviderAdapter(string name, Result<BenchmarkResult> response)
    : IBenchmarkProviderAdapter
{
    public int CallCount { get; private set; }

    public BenchmarkQuery? LastQuery { get; private set; }

    public string Name { get; } = name;

    public Task<Result<BenchmarkResult>> GetBenchmarkAsync(
        BenchmarkQuery query, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastQuery = query;
        return Task.FromResult(response);
    }
}
