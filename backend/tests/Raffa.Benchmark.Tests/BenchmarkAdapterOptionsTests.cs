using Raffa.Benchmark.Configuration;

namespace Raffa.Benchmark.Tests;

/// <summary>
/// Locks in task E04/F01/US01/T02's own default adapter name so an accidental change is caught
/// here rather than silently changing which adapter <see cref="BenchmarkAdapterRegistry"/>
/// dispatches to out of the box.
/// </summary>
public class BenchmarkAdapterOptionsTests
{
    [Fact]
    public void Default_active_adapter_is_fixture()
    {
        var options = new BenchmarkAdapterOptions();

        Assert.Equal("fixture", options.ActiveAdapter);
        Assert.Equal(BenchmarkAdapterOptions.DefaultAdapterName, options.ActiveAdapter);
    }

    [Fact]
    public void Section_name_is_stable_for_configuration_binding()
    {
        Assert.Equal("Benchmark:Adapter", BenchmarkAdapterOptions.SectionName);
    }
}
