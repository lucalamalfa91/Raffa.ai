using Microsoft.Extensions.DependencyInjection;

namespace Raffa.AiFlows.Tests;

/// <summary>
/// The AI flows project starts empty (no flow has moved in yet): <c>AddAiFlows</c> is the stable
/// entry point the hosts already call, and it must be a harmless no-op until flows register.
/// </summary>
public sealed class AiFlowsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAiFlows_returns_the_same_collection_and_registers_nothing_yet()
    {
        var services = new ServiceCollection();

        var returned = services.AddAiFlows();

        Assert.Same(services, returned);
        Assert.Empty(services);
    }

    [Fact]
    public void AddAiFlows_is_safe_to_call_twice()
    {
        var services = new ServiceCollection();

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
