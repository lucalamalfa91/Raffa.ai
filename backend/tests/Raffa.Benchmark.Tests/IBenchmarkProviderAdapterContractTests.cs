using Raffa.Benchmark.Adapters;

namespace Raffa.Benchmark.Tests;

/// <summary>
/// Proves the <see cref="IBenchmarkProviderAdapter"/> seam shape task E04/F01/US01/T02 adds: same
/// normalized request/response contract as <see cref="IBenchmarkService.GetBenchmarkAsync"/>, plus
/// a stable <see cref="IBenchmarkProviderAdapter.Name"/> for registry dispatch.
/// </summary>
public class IBenchmarkProviderAdapterContractTests
{
    [Fact]
    public void GetBenchmarkAsync_matches_IBenchmarkServices_own_normalized_signature()
    {
        var adapterMethod = typeof(IBenchmarkProviderAdapter).GetMethod(
            nameof(IBenchmarkProviderAdapter.GetBenchmarkAsync));
        var serviceMethod = typeof(IBenchmarkService).GetMethod(
            nameof(IBenchmarkService.GetBenchmarkAsync));

        Assert.NotNull(adapterMethod);
        Assert.NotNull(serviceMethod);
        Assert.Equal(serviceMethod!.ReturnType, adapterMethod!.ReturnType);

        var adapterParameterTypes = adapterMethod.GetParameters().Select(p => p.ParameterType);
        var serviceParameterTypes = serviceMethod.GetParameters().Select(p => p.ParameterType);
        Assert.Equal(serviceParameterTypes, adapterParameterTypes);
    }

    [Fact]
    public void Exposes_a_stable_name_for_registry_dispatch()
    {
        var nameProperty = typeof(IBenchmarkProviderAdapter).GetProperty(
            nameof(IBenchmarkProviderAdapter.Name));

        Assert.NotNull(nameProperty);
        Assert.Equal(typeof(string), nameProperty!.PropertyType);
    }
}
