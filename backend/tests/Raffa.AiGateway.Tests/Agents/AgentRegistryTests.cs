using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Logging;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Tests.Agents;

/// <summary>Plan A-02: the registry lists every agent with name and version, validates all definitions
/// (T-06 drift) and carries a per-agent and per-flow kill switch the runner obeys.</summary>
public sealed class AgentRegistryTests
{
    private sealed class MutableMonitor(AgentRegistryOptions value) : IOptionsMonitor<AgentRegistryOptions>
    {
        public AgentRegistryOptions CurrentValue { get; set; } = value;

        public AgentRegistryOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AgentRegistryOptions, string?> listener) => null;
    }

    private static AgentRegistry Registry(MutableMonitor? monitor = null, params AgentRegistration[] registrations) =>
        new(registrations, monitor ?? new MutableMonitor(new AgentRegistryOptions()));

    private static AgentRegistration Reg(string flow, string name, string version = "echo-v1") =>
        new(flow, AgentTestKit.Definition(name, version: version));

    [Fact]
    public void Every_registered_agent_is_listed_with_flow_name_and_version_in_a_stable_order()
    {
        var registry = Registry(null, Reg("zeta", "b-agent"), Reg("alpha", "z-agent", "z-v2"), Reg("alpha", "a-agent"));

        Assert.Equal(
            ["alpha/a-agent/echo-v1", "alpha/z-agent/z-v2", "zeta/b-agent/echo-v1"],
            registry.All.Select(r => $"{r.Flow}/{r.Definition.Name}/{r.Definition.Version}"));
        Assert.Equal(["a-agent", "z-agent"], registry.ForFlow("ALPHA").Select(r => r.Definition.Name));
        Assert.Equal("z-v2", registry.Get("z-agent").Definition.Version);
        Assert.False(registry.TryGet("nope", out _));
        Assert.Throws<KeyNotFoundException>(() => registry.Get("nope"));
    }

    [Fact]
    public void Registering_one_agent_name_twice_is_a_configuration_error()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Registry(null, Reg("a", "dup"), Reg("b", "dup")));

        Assert.Contains("'dup' is registered twice", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_aggregates_the_drift_of_every_definition()
    {
        var drifted = new AgentDefinition(
            "drifted", "v1", "prompt", AgentSchema.For<EchoOutput>(), TimeSpan.FromSeconds(1),
            registeredPromptHash: new string('a', 64), promptResource: "Prompts/x/drifted/v1.md");
        var registry = Registry(null, Reg("f", "sound"), new AgentRegistration("f", drifted));

        var problem = Assert.Single(registry.Validate());
        Assert.Contains("prompt drift", problem, StringComparison.Ordinal);
        Assert.Contains("drifted", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_agent_is_on_by_default_and_an_unknown_agent_is_never_blocked()
    {
        var registry = Registry(null, Reg("f", "a"));

        Assert.True(registry.IsEnabled("a"));
        Assert.True(registry.IsEnabled("not-registered"));
        Assert.Null(registry.DisabledReason("a"));
    }

    [Fact]
    public void An_agent_switch_and_a_flow_switch_turn_agents_off_and_follow_the_monitor_live()
    {
        var monitor = new MutableMonitor(new AgentRegistryOptions());
        var registry = Registry(monitor, Reg("council", "x"), Reg("council", "y"), Reg("other", "z"));

        monitor.CurrentValue = new AgentRegistryOptions
        {
            Agents = new(StringComparer.OrdinalIgnoreCase) { ["x"] = new AgentSwitchOptions { Enabled = false } },
        };
        Assert.False(registry.IsEnabled("x"));
        Assert.True(registry.IsEnabled("y"));
        Assert.Contains("'x'", registry.DisabledReason("x"), StringComparison.Ordinal);

        monitor.CurrentValue = new AgentRegistryOptions
        {
            Flows = new(StringComparer.OrdinalIgnoreCase) { ["council"] = new AgentSwitchOptions { Enabled = false } },
        };
        Assert.False(registry.IsEnabled("x"));
        Assert.False(registry.IsEnabled("y"));
        Assert.True(registry.IsEnabled("z"));
        Assert.Contains("flow 'council'", registry.DisabledReason("y"), StringComparison.Ordinal);

        monitor.CurrentValue = new AgentRegistryOptions();
        Assert.True(registry.IsEnabled("x"));
    }

    [Fact]
    public async Task A_disabled_agent_is_not_called_and_degrades_by_its_policy_with_the_disabled_reason()
    {
        var monitor = new MutableMonitor(new AgentRegistryOptions
        {
            Agents = new(StringComparer.OrdinalIgnoreCase)
            {
                ["optional-agent"] = new AgentSwitchOptions { Enabled = false },
                ["required-agent"] = new AgentSwitchOptions { Enabled = false },
                ["fallback-agent"] = new AgentSwitchOptions { Enabled = false },
            },
        });
        var optional = AgentTestKit.Definition("optional-agent", AgentFailurePolicy.Skip);
        var required = AgentTestKit.Definition("required-agent", AgentFailurePolicy.Fail);
        var fallback = AgentTestKit.Definition("fallback-agent", AgentFailurePolicy.Fallback);
        var registry = new AgentRegistry(
            [new("f", optional), new("f", required), new("f", fallback)], monitor);
        var gateway = new ScriptedGateway();
        var runner = new StepRunner(gateway, registry: registry);

        var skipped = await runner.RunAsync(AgentTestKit.Step(optional), AgentTestKit.Input);
        var failed = await runner.RunAsync(AgentTestKit.Step(required), AgentTestKit.Input);
        var fell = await runner.RunAsync(
            AgentTestKit.Step(fallback, fallback: i => new EchoOutput("template", i.Keys)), AgentTestKit.Input);

        Assert.Empty(gateway.AnalyzeRequests);
        Assert.Equal(StepStatus.Skipped, skipped.Status);
        Assert.Equal(StepStatus.Failed, failed.Status);
        Assert.Equal(StepStatus.FellBack, fell.Status);
        Assert.All([skipped, failed, fell], r =>
        {
            Assert.Equal(StepFailureReason.Disabled, r.Reason);
            Assert.Equal(0, r.Attempts);
            Assert.Contains("switched off", r.Error, StringComparison.Ordinal);
        });

        // Flipped back on, the same runner calls the model again: no restart needed.
        monitor.CurrentValue = new AgentRegistryOptions();
        var ok = await runner.RunAsync(AgentTestKit.Step(optional), AgentTestKit.Input);
        Assert.Equal(StepStatus.Succeeded, ok.Status);
        Assert.Single(gateway.AnalyzeRequests);
    }

    [Fact]
    public async Task A_runner_without_a_registry_or_with_an_unregistered_agent_behaves_exactly_as_before()
    {
        var gateway = new ScriptedGateway();
        var empty = Registry();

        var plain = await new StepRunner(gateway).RunAsync(AgentTestKit.Step(), AgentTestKit.Input);
        var unregistered = await new StepRunner(gateway, registry: empty).RunAsync(AgentTestKit.Step(), AgentTestKit.Input);

        Assert.Equal(StepStatus.Succeeded, plain.Status);
        Assert.Equal(StepStatus.Succeeded, unregistered.Status);
        Assert.Equal(2, gateway.AnalyzeRequests.Count);
    }

    // ---------------------------------------------------------------- DI

    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static ServiceProvider Build(Dictionary<string, string?>? settings, Action<IServiceCollection>? add = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());
        services.AddScoped<IAuditWriter, NoOpAuditWriter>();
        services.AddAiGatewayModule();
        add?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public void The_gateway_module_registers_one_singleton_registry_with_everything_on()
    {
        using var provider = Build(null);

        var registry = provider.GetRequiredService<AgentRegistry>();

        Assert.Same(registry, provider.GetRequiredService<AgentRegistry>());
        Assert.Empty(registry.All);
    }

    [Fact]
    public void Flows_register_definitions_and_the_switches_bind_from_the_agents_configuration_section()
    {
        using var provider = Build(
            new Dictionary<string, string?>
            {
                ["Agents:Agents:b-agent:Enabled"] = "false",
                ["Agents:Flows:gone:Enabled"] = "false",
            },
            services =>
            {
                services.AddAgentRegistry();
                services.AddAgentDefinitions("demo", AgentTestKit.Definition("a-agent"), AgentTestKit.Definition("b-agent"));
                services.AddAgentDefinitions("gone", _ => [AgentTestKit.Definition("c-agent")]);
            });

        var registry = provider.GetRequiredService<AgentRegistry>();

        Assert.Equal(["a-agent", "b-agent", "c-agent"], registry.All.Select(r => r.Definition.Name));
        Assert.True(registry.IsEnabled("a-agent"));
        Assert.False(registry.IsEnabled("b-agent"));
        Assert.False(registry.IsEnabled("c-agent"));

        // The runner resolved from the container obeys it.
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<StepRunner>());
    }
}
