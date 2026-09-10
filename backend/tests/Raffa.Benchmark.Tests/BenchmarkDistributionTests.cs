using Raffa.Benchmark.Contracts;

namespace Raffa.Benchmark.Tests;

public class BenchmarkDistributionTests
{
    [Fact]
    public void Holds_p25_p50_p75_values()
    {
        var distribution = new BenchmarkDistribution(P25: 90m, P50: 100m, P75: 120m);

        Assert.Equal(90m, distribution.P25);
        Assert.Equal(100m, distribution.P50);
        Assert.Equal(120m, distribution.P75);
    }

    [Fact]
    public void Two_distributions_with_the_same_values_are_equal()
    {
        var a = new BenchmarkDistribution(90m, 100m, 120m);
        var b = new BenchmarkDistribution(90m, 100m, 120m);

        Assert.Equal(a, b);
    }
}
