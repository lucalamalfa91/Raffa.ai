using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Telemetry;

namespace Raffa.AiGateway.Tests;

/// <summary>The playground variant seams: inert without a scope, exact override inside one, gone when it ends.</summary>
public sealed class PlaygroundScopeTests
{
    private static readonly AiModelSelection Configured = new("gpt-5.4-nano-dev", "2026-01") { ReasoningEffort = "low", MaxCompletionTokens = 4096 };

    [Fact]
    public void Without_a_scope_every_seam_returns_the_configured_value()
    {
        Assert.Null(PlaygroundScope.Current);
        Assert.Same(Configured, PlaygroundScope.ModelFor("agent", Configured));
        Assert.Null(PlaygroundScope.PromptFor("agent"));
        Assert.False(PlaygroundScope.IsToolDisabled("get_contract"));
        Assert.Equal(8, PlaygroundScope.MaxToolCallsOr(8));
    }

    [Fact]
    public void A_variant_replaces_only_the_knobs_it_sets_and_only_for_its_role()
    {
        var overrides = new PlaygroundOverrides(
            Prompts: new Dictionary<string, string> { ["agent"] = "NEW" },
            Models: new Dictionary<string, PlaygroundModelOverride> { ["agent"] = new("gpt-5.4-dev", null, 1024, null) },
            DisabledTools: ["web_research"],
            MaxToolCalls: 3);

        using (PlaygroundScope.Begin(overrides))
        {
            var agent = PlaygroundScope.ModelFor("agent", Configured);
            Assert.Equal("gpt-5.4-dev", agent.ModelId);
            Assert.Equal("low", agent.ReasoningEffort);
            Assert.Equal(1024, agent.MaxCompletionTokens);
            Assert.Equal("2026-01", agent.ModelVersion);
            Assert.Same(Configured, PlaygroundScope.ModelFor("answer", Configured));
            Assert.Equal("NEW", PlaygroundScope.PromptFor("agent"));
            Assert.True(PlaygroundScope.IsToolDisabled("web_research"));
            Assert.False(PlaygroundScope.IsToolDisabled("get_contract"));
            Assert.Equal(3, PlaygroundScope.MaxToolCallsOr(8));
        }

        Assert.Null(PlaygroundScope.Current);
        Assert.Null(PlaygroundScope.PromptFor("agent"));
    }

    [Fact]
    public async Task The_scope_flows_into_tasks_started_inside_it_and_not_into_siblings()
    {
        var overrides = new PlaygroundOverrides(Prompts: new Dictionary<string, string> { ["agent"] = "NEW" });
        string? inside;
        using (PlaygroundScope.Begin(overrides))
        {
            inside = await Task.Run(() => PlaygroundScope.PromptFor("agent"));
        }

        var outside = await Task.Run(() => PlaygroundScope.PromptFor("agent"));
        Assert.Equal("NEW", inside);
        Assert.Null(outside);
    }
}
