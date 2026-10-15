using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.Chat.Application.WebResearch;
using Raffa.Chat.Infrastructure;

namespace Raffa.AiFlows.Tests.WebResearch.Configuration;

/// <summary>F3-T10: the kill switch (and the other settings) follow the configuration without a restart.</summary>
public sealed class WebResearchOptionsMonitorTests
{
    /// <summary>A provider whose keys can be changed and removed, as a reloaded file's can.</summary>
    private sealed class MutableProvider : ConfigurationProvider
    {
        public override void Set(string key, string? value) => Data[key] = value;

        public void Remove(string key) => Data.Remove(key);
    }

    private static (ConfigurationRoot Root, MutableProvider Provider) Config(params (string Key, string? Value)[] values)
    {
        var provider = new MutableProvider();
        foreach (var (key, value) in values)
        {
            provider.Set(key, value);
        }

        return (new ConfigurationRoot([provider]), provider);
    }

    [Fact]
    public void Switching_the_kill_switch_off_in_the_configuration_applies_without_a_restart()
    {
        var (root, provider) = Config(("Chat:WebResearch:Enabled", "true"));
        var options = new WebResearchOptions { Enabled = true };
        using var monitor = new WebResearchOptionsMonitor(options, root);
        Assert.True(monitor.CurrentValue.Enabled);

        provider.Set("Chat:WebResearch:Enabled", "false");
        root.Reload();

        Assert.False(monitor.CurrentValue.Enabled);
    }

    [Fact]
    public void A_key_removed_from_the_configuration_goes_back_to_its_default_so_the_gate_closes()
    {
        var (root, provider) = Config(("Chat:WebResearch:Enabled", "true"), ("Chat:WebResearch:DailyCallsPerTenant", "5"));
        var options = new WebResearchOptions { Enabled = true, DailyCallsPerTenant = 5 };
        using var monitor = new WebResearchOptionsMonitor(options, root);

        provider.Remove("Chat:WebResearch:Enabled");
        provider.Remove("Chat:WebResearch:DailyCallsPerTenant");
        root.Reload();

        Assert.False(monitor.CurrentValue.Enabled);
        Assert.Equal(20, monitor.CurrentValue.DailyCallsPerTenant);
    }

    [Fact]
    public void The_reload_reaches_the_singleton_the_budget_and_the_settings_endpoint_hold()
    {
        var (root, provider) = Config(("Chat:WebResearch:Enabled", "true"), ("Chat:WebResearch:DailyCallsPerTenant", "5"));
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(root);
        services.AddSingleton(new WebResearchOptions { Enabled = true, DailyCallsPerTenant = 5 });
        services.AddChatModule();
        services.AddAiFlows();
        using var provider2 = services.BuildServiceProvider();

        var monitor = provider2.GetRequiredService<IOptionsMonitor<WebResearchOptions>>();
        Assert.True(monitor.CurrentValue.Enabled);

        provider.Set("Chat:WebResearch:Enabled", "false");
        provider.Set("Chat:WebResearch:DailyCallsPerTenant", "9");
        root.Reload();

        Assert.False(provider2.GetRequiredService<WebResearchOptions>().Enabled);
        Assert.Equal(9, provider2.GetRequiredService<IWebResearchBudgetLimit>().DailyCallsPerTenant);
    }

    [Fact]
    public void Listeners_are_told_of_a_reload_until_they_unsubscribe()
    {
        var (root, provider) = Config(("Chat:WebResearch:Enabled", "true"));
        using var monitor = new WebResearchOptionsMonitor(new WebResearchOptions { Enabled = true }, root);
        var calls = 0;
        var subscription = monitor.OnChange((_, _) => calls++);

        provider.Set("Chat:WebResearch:Enabled", "false");
        root.Reload();
        subscription!.Dispose();
        provider.Set("Chat:WebResearch:Enabled", "true");
        root.Reload();

        Assert.Equal(1, calls);
        Assert.True(monitor.CurrentValue.Enabled);
    }

    [Fact]
    public void Without_a_configuration_the_monitor_is_static()
    {
        var options = new WebResearchOptions { Enabled = true };
        var monitor = WebResearchOptionsMonitor.Static(options);

        Assert.Same(options, monitor.CurrentValue);
        Assert.Same(options, monitor.Get(null));
    }
}
