using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.Chat.Application.WebResearch;
using Raffa.Chat.Infrastructure;

namespace Raffa.AiFlows.Tests.WebResearch;

/// <summary>
/// The web-research flow owns <see cref="WebResearchOptions"/>; the budget (persistence, in
/// <c>Raffa.Chat</c>) reads its daily limit through <see cref="IWebResearchBudgetLimit"/>, which
/// <c>AddChatModule</c> registers closed and the flow replaces with the options.
/// </summary>
public sealed class WebResearchFlowRegistrationTests
{
    [Fact]
    public void The_budget_limit_follows_the_registered_WebResearchOptions()
    {
        var services = new ServiceCollection();
        services.AddChatModule();
        services.AddAiFlows();

        using (var defaults = services.BuildServiceProvider())
        {
            Assert.Equal(20, defaults.GetRequiredService<IWebResearchBudgetLimit>().DailyCallsPerTenant);
        }

        // The API tests swap the options after the composition methods ran: the limit must follow.
        services.RemoveAll<WebResearchOptions>();
        services.AddSingleton(new WebResearchOptions { DailyCallsPerTenant = 3 });

        using var swapped = services.BuildServiceProvider();
        Assert.Equal(3, swapped.GetRequiredService<IWebResearchBudgetLimit>().DailyCallsPerTenant);
    }

    [Fact]
    public void The_options_default_to_the_kill_switch_off()
    {
        var services = new ServiceCollection();
        services.AddAiFlows();

        using var provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<WebResearchOptions>().Enabled);
    }

    [Fact]
    public void Calling_the_flow_after_a_host_registered_its_own_options_keeps_the_host_values()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new WebResearchOptions { Enabled = true, DailyCallsPerTenant = 7 });
        services.AddChatModule();
        services.AddAiFlows();

        using var provider = services.BuildServiceProvider();

        Assert.True(provider.GetRequiredService<WebResearchOptions>().Enabled);
        Assert.Equal(7, provider.GetRequiredService<IWebResearchBudgetLimit>().DailyCallsPerTenant);
    }
}
