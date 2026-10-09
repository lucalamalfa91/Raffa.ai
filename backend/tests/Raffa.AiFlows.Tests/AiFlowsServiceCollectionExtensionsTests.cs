using Raffa.AiFlows.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.AiFlows.Tests;

/// <summary>
/// <c>AddAiFlows</c> is the stable entry point the hosts call; each flow that has moved in registers
/// itself from it (see the per-flow composition tests).
/// </summary>
public sealed class AiFlowsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAiFlows_returns_the_same_collection()
    {
        var services = new ServiceCollection();

        var returned = services.AddAiFlows();

        Assert.Same(services, returned);
    }

    [Fact]
    public void AddAiFlows_is_safe_to_call_twice()
    {
        // The flows sit on top of the modules, so the container needs the Documents/Contracts module
        // (and what its host supplies) for ValidateOnBuild to find every dependency.
        var services = DocumentsModuleServices.Create();

        services.AddAiFlows().AddAiFlows();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddAiFlows_rejects_a_null_collection()
    {
        Assert.Throws<ArgumentNullException>(() => AiFlowsServiceCollectionExtensions.AddAiFlows(null!));
    }
}
