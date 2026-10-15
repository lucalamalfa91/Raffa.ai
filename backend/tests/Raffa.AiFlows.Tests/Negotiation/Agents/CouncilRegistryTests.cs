using Microsoft.Extensions.DependencyInjection;
using Raffa.AiFlows.Negotiation;
using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiGateway;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.Negotiation.Agents;

/// <summary>
/// Plan A-02 / T-06 on the pilot: the council's three agents are in the <see cref="AgentRegistry"/>,
/// their prompts are embedded files whose text and hash are byte-identical to the constants they were
/// moved from, the drift test is green, and the per-agent kill switch skips the model call.
/// </summary>
public sealed class CouncilRegistryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(10);

    private static readonly (string Name, string File, string Prompt, string Hash)[] Prompts =
    [
        ("contract-analyst", "Prompts/council/contract-analyst/v3.md", CouncilAgents.ContractAnalystPrompt,
            "7a7ebbff304bbec28aa680ea3a21f7d6e8693d52881b57ec3817e9b8951b9c6f"),
        ("market-analyst", "Prompts/council/market-analyst/v3.md", CouncilAgents.MarketAnalystPrompt,
            "6588ffe9477b2b7d063a17efc0f769020c9ecf5658a051c052e7fbcc8bccebd3"),
        ("lever-strategist", "Prompts/council/lever-strategist/v3.md", CouncilAgents.LeverStrategistPrompt,
            "ca07e9bbdb4da29da985a5fcf30b2cc259d6199955a0048e87710c78816d5bb9"),
    ];

    private static ServiceProvider Build(Action<AgentRegistryOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddNegotiationFlow();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_council_registers_its_three_agents_with_name_and_version_and_a_clean_drift_check()
    {
        using var provider = Build();

        var registry = provider.GetRequiredService<AgentRegistry>();

        Assert.Equal(
            ["contract-analyst", "lever-strategist", "market-analyst"],
            registry.ForFlow("council").Select(r => r.Definition.Name));
        Assert.All(registry.All, r => Assert.Equal("council-v3", r.Definition.Version));
        Assert.Empty(registry.Validate());
    }

    [Fact]
    public void The_registered_deadline_follows_the_councils_own_options()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new CouncilOptions { AgentDeadline = TimeSpan.FromSeconds(42) });
        services.AddNegotiationFlow();
        using var provider = services.BuildServiceProvider();

        Assert.All(provider.GetRequiredService<AgentRegistry>().All, r => Assert.Equal(TimeSpan.FromSeconds(42), r.Definition.Deadline));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Each_prompt_file_is_byte_identical_to_the_constant_it_replaced_and_hashes_to_the_registered_value(int index)
    {
        var (name, file, constant, hash) = Prompts[index];
        var assembly = typeof(CouncilStepDefinitions).Assembly;

        var raw = AgentPromptResource.ReadEmbedded(assembly, file);
        var definition = CouncilStepDefinitions.All(Deadline).Single(d => d.Name == name);

        Assert.Equal(constant, AgentPromptResource.Normalize(raw));
        Assert.Equal(constant, definition.Prompt);
        Assert.Equal(constant, definition.SystemPrompt);
        Assert.Equal(hash, definition.PromptHash);
        Assert.Equal(hash, definition.RegisteredPromptHash);
        Assert.Equal(AgentDefinition.HashPrompt(constant), hash);
        Assert.Equal(file, definition.PromptResource);
        Assert.Empty(definition.Validate());
    }

    [Fact]
    public void A_prompt_file_edited_without_a_version_bump_is_drift()
    {
        var edited = new AgentDefinition(
            "lever-strategist", "council-v3", Prompts[2].Prompt + "\n6. A new law.", CouncilAgents.PlaysSchema, Deadline,
            registeredPromptHash: Prompts[2].Hash, promptResource: Prompts[2].File);

        Assert.Contains("prompt drift", Assert.Single(edited.Validate()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_kill_switch_of_an_analyst_skips_its_model_call_and_the_council_still_convenes()
    {
        using var provider = Build(o => o.Agents["contract-analyst"] = new AgentSwitchOptions { Enabled = false });
        var recording = new RecordingGateway(new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
        var runner = new StepRunner(recording, registry: provider.GetRequiredService<AgentRegistry>());

        var outcome = await new NegotiationCouncil(recording, new CouncilOptions(), runner)
            .RunAsync("q", Pack(), new SavingsGoal(20000m, null, "EUR", null, null));

        Assert.DoesNotContain("contract-analyst", recording.Agents);
        Assert.Contains("market-analyst", recording.Agents);
        Assert.Contains("lever-strategist", recording.Agents);
        Assert.Contains(outcome.Failures, f => f.StartsWith("contract-analyst: ", StringComparison.Ordinal) && f.Contains("switched off", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_flow_switch_turns_the_whole_council_off_without_a_single_model_call()
    {
        using var provider = Build(o => o.Flows["council"] = new AgentSwitchOptions { Enabled = false });
        var recording = new RecordingGateway(new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
        var runner = new StepRunner(recording, registry: provider.GetRequiredService<AgentRegistry>());

        var outcome = await new NegotiationCouncil(recording, new CouncilOptions(), runner)
            .RunAsync("q", Pack(), new SavingsGoal(20000m, null, "EUR", null, null));

        Assert.Empty(recording.Agents);
        Assert.Empty(outcome.Items);
    }

    private static PackItem Item(string key, string corpus, string title, string snippet, params PackValue[] values) =>
        new(key, corpus, title, null, null, null, snippet, "/contracts/x", null, null, "test", values, "contract-x");

    private static IReadOnlyList<PackItem> Pack() =>
    [
        Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow · OrderForm", "ServiceNow ends on 2028-04-10.",
            new PackValue("annualSpend", "230000", PackValueKind.Amount, "EUR")),
        Item("calc:savings-target", PackCorpus.Calc, "ServiceNow — saving target", "Target EUR 20000.",
            new PackValue("targetAmount", "20000", PackValueKind.Amount, "EUR")),
        Item("calc:lever[market-discount]", PackCorpus.Calc, "ServiceNow — Market discount", "Peers achieved a 9% discount.",
            new PackValue("percent", "9", PackValueKind.Percentage)),
        Item("market:MKT-SNOW-EU-01", PackCorpus.Market, "ServiceNow · ITSM", "Companies achieved a 9% discount.",
            new PackValue("discountAchievedPct", "9", PackValueKind.Percentage)),
        Item("raffa:playbook:anchor-on-market", PackCorpus.Raffa, "Anchor the ask", "Open with the market median."),
    ];

    private sealed class RecordingGateway(IAiGateway inner) : IAiGateway
    {
        private readonly object _lock = new();

        public List<string> Agents { get; } = [];

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            inner.ClassifyAsync(request, cancellationToken);

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            inner.ExtractAsync(request, cancellationToken);

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            inner.EmbedAsync(request, cancellationToken);

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            inner.AnswerAsync(request, cancellationToken);

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            inner.OcrAsync(request, cancellationToken);

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                Agents.Add(request.AgentName);
            }

            return inner.AnalyzeAsync(request, cancellationToken);
        }
    }
}
