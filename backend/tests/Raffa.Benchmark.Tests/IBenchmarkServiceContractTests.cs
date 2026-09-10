using Raffa.Benchmark.Contracts;
using Raffa.SharedKernel;

namespace Raffa.Benchmark.Tests;

/// <summary>
/// Proves AC-1 (us-01-benchmark-interface): <see cref="IBenchmarkService"/> exposes a
/// <c>getBenchmark</c>-shaped method returning the normalized DTO, matching product spec §10.3.
/// There is no implementation to exercise yet — task E04/F01/US01/T02 adds the adapter registry,
/// and story us-02-fixture-adapter adds the first adapter — so this task's own scope (the
/// contract) is what these tests hold to the spec, via reflection over the real interface type.
/// </summary>
public class IBenchmarkServiceContractTests
{
    [Fact]
    public void GetBenchmarkAsync_accepts_a_benchmark_query_and_returns_a_normalized_result()
    {
        var method = typeof(IBenchmarkService).GetMethod(nameof(IBenchmarkService.GetBenchmarkAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result<BenchmarkResult>>), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(BenchmarkQuery), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.True(parameters[1].HasDefaultValue);
    }

    [Fact]
    public void Interface_exposes_exactly_one_member_for_this_task_scope()
    {
        // Task objective: "Benchmark Service interface getBenchmark + normalized DTO." Only the
        // getBenchmark contract is in scope here; the adapter registry (task T02) is additive and
        // not yet present.
        var members = typeof(IBenchmarkService).GetMethods();

        Assert.Single(members);
    }
}
