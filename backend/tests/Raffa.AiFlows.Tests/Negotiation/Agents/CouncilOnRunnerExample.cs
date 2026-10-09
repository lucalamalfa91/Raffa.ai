using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiGateway.Agents;

namespace Raffa.AiFlows.Tests.Negotiation.Agents;

// The wire shapes of the council, as plain immutable records (plan 3.2: input/output records that
// serialize, so a step can become a durable activity unchanged). They mirror the private DTOs of
// NegotiationCouncil field for field, so the runner serializes the same InputJson.

internal sealed record ValueDto(string Key, string Value, string Kind, string? Currency);

internal sealed record ItemDto(string CitationKey, string Corpus, string Title, string? Subtitle, string Snippet, IReadOnlyList<ValueDto> Values);

internal sealed record GoalDto(decimal? TargetAmount, decimal? TargetPercent, string? Currency, int? WindowDays, string? WindowLabel);

internal sealed record AnalystInput(string Question, GoalDto? Goal, IReadOnlyList<ItemDto> Items);

internal sealed record Finding(string Title, string Insight, string? LeverType, IReadOnlyList<string> CitationKeys);

internal sealed record FindingsOutput(IReadOnlyList<Finding> Findings);

internal sealed record FindingsByAgent(IReadOnlyList<Finding> ContractAnalyst, IReadOnlyList<Finding> MarketAnalyst);

internal sealed record StrategistInput(string Question, GoalDto? Goal, IReadOnlyList<ItemDto> Items, FindingsByAgent Findings);

internal sealed record Play(
    int Rank, string Lever, string Ask, IReadOnlyList<string> ExpectedValueKeys, string Fallback, string Timing, IReadOnlyList<string> CitationKeys);

internal sealed record PlaysOutput(IReadOnlyList<Play> Plays);

internal sealed record ExampleCouncilOutcome(
    IReadOnlyList<Play> Plays,
    IReadOnlyList<string> AgentsRun,
    IReadOnlyList<string> Failures);

/// <summary>The definitions of the example council: the real agents' names, version, prompts (the
/// <see cref="CouncilAgents"/> constants, unchanged) and a schema derived from the output type.</summary>
internal static class ExampleCouncilDefinitions
{
    public static AgentDefinition ContractAnalyst(TimeSpan? deadline = null) => new(
        CouncilAgents.ContractAnalystName, CouncilAgents.Version, CouncilAgents.ContractAnalystPrompt,
        AgentSchema.For<FindingsOutput>(), deadline ?? TimeSpan.FromSeconds(120), AgentFailurePolicy.Skip);

    public static AgentDefinition MarketAnalyst(TimeSpan? deadline = null) => new(
        CouncilAgents.MarketAnalystName, CouncilAgents.Version, CouncilAgents.MarketAnalystPrompt,
        AgentSchema.For<FindingsOutput>(), deadline ?? TimeSpan.FromSeconds(120), AgentFailurePolicy.Skip);

    // The strategist is the one required step: no plays means no council for the turn.
    public static AgentDefinition LeverStrategist(TimeSpan? deadline = null) => new(
        CouncilAgents.LeverStrategistName, CouncilAgents.Version, CouncilAgents.LeverStrategistPrompt,
        AgentSchema.For<PlaysOutput>(), deadline ?? TimeSpan.FromSeconds(120), AgentFailurePolicy.Fail);

    public static IReadOnlyList<AgentDefinition> All() => [ContractAnalyst(), MarketAnalyst(), LeverStrategist()];
}

/// <summary>A verifier that sends an analyst back once when it cites a key the pack does not hold
/// (the council today drops such citations silently; as a verifier the model gets one chance to fix it).</summary>
internal sealed class CitesOnlyPackKeys(IReadOnlySet<string> packKeys) : IStepVerifier<AnalystInput, FindingsOutput>
{
    public string Name => "cites-only-pack-keys";

    public StepVerdict Verify(AnalystInput input, FindingsOutput output)
    {
        var unknown = output.Findings.SelectMany(f => f.CitationKeys ?? []).Where(k => !packKeys.Contains(k)).Distinct().ToList();
        return unknown.Count == 0
            ? StepVerdict.Pass
            : StepVerdict.Reject($"Unknown citation keys: {string.Join(", ", unknown)}. Cite only keys copied from the input items.");
    }
}

/// <summary>
/// A pretend council on the runner: the shape of <c>NegotiationCouncil.RunAsync</c> (round one, two
/// analysts in parallel over disjoint slices; round two, the strategist over both sets of findings),
/// with every model call a <see cref="StepRunner"/> step. It migrates nothing (the pilot is plan
/// A-05a); it is the worked example the runner is proven against with <c>FixtureAiGateway</c>.
/// </summary>
internal sealed class CouncilOnRunner(StepRunner runner, TimeSpan? analystDeadline = null, CouncilOptions? options = null)
{
    private readonly CouncilOptions _options = options ?? new CouncilOptions();

    public async Task<ExampleCouncilOutcome> RunAsync(
        string question, IReadOnlyList<PackItem> pack, SavingsGoal? goal, CancellationToken cancellationToken = default)
    {
        var agentsRun = new List<string>();
        var failures = new List<string>();
        var goalDto = goal is null || !goal.HasTarget
            ? null
            : new GoalDto(goal.TargetAmount, goal.TargetPercent, goal.Currency, goal.WindowDays, goal.WindowLabel);
        var packKeys = pack.Select(i => i.CitationKey).ToHashSet(StringComparer.Ordinal);

        var tenantItems = pack.Where(i => i.Corpus == PackCorpus.Tenant).Take(_options.MaxItemsPerAgent).ToList();
        var marketItems = pack
            .Where(i => i.Corpus == PackCorpus.Market ||
                i.CitationKey.StartsWith("calc:lever", StringComparison.Ordinal) ||
                i.CitationKey.StartsWith("calc:contract-gaps", StringComparison.Ordinal))
            .Take(_options.MaxItemsPerAgent)
            .ToList();

        // ----- Round one: both analysts at once; a failure of either degrades the council only -----
        var contract = RunAnalystAsync(ExampleCouncilDefinitions.ContractAnalyst(analystDeadline), question, goalDto, tenantItems, packKeys, cancellationToken);
        var market = RunAnalystAsync(ExampleCouncilDefinitions.MarketAnalyst(analystDeadline), question, goalDto, marketItems, packKeys, cancellationToken);
        await Task.WhenAll(contract, market).ConfigureAwait(false);

        foreach (var (name, items, task) in new[]
        {
            (CouncilAgents.ContractAnalystName, tenantItems, contract),
            (CouncilAgents.MarketAnalystName, marketItems, market),
        })
        {
            if (items.Count > 0)
            {
                agentsRun.Add(name);
            }

            if (task.Result?.Describe() is { } failure)
            {
                failures.Add(failure);
            }
        }

        var contractFindings = Filter(contract.Result?.Value, packKeys);
        var marketFindings = Filter(market.Result?.Value, packKeys);

        // ----- Round two: the strategist, required -----
        var strategistItems = pack
            .Where(i => i.Corpus == PackCorpus.Calc || i.CitationKey.StartsWith("raffa:playbook:", StringComparison.Ordinal))
            .Take(_options.MaxItemsPerAgent + 6)
            .ToList();
        var strategistInput = new StrategistInput(
            question, goalDto, strategistItems.Select(ToItemDto).ToList(), new FindingsByAgent(contractFindings, marketFindings));

        agentsRun.Add(CouncilAgents.LeverStrategistName);
        var plays = await runner.RunAsync(
                new AgentStep<StrategistInput, PlaysOutput>(ExampleCouncilDefinitions.LeverStrategist()),
                strategistInput,
                cancellationToken)
            .ConfigureAwait(false);

        if (!plays.IsSuccess)
        {
            failures.Add(plays.Describe()!);
            return new ExampleCouncilOutcome([], agentsRun, failures);
        }

        var grounded = plays.Value!.Plays
            .Where(p => p.CitationKeys.Any(packKeys.Contains))
            .OrderBy(p => p.Rank)
            .Take(_options.MaxPlays)
            .ToList();
        return new ExampleCouncilOutcome(grounded, agentsRun, failures);
    }

    private async Task<StepResult<FindingsOutput>?> RunAnalystAsync(
        AgentDefinition definition,
        string question,
        GoalDto? goal,
        IReadOnlyList<PackItem> items,
        IReadOnlySet<string> packKeys,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return null;
        }

        var step = new AgentStep<AnalystInput, FindingsOutput>(definition, new CitesOnlyPackKeys(packKeys));
        return await runner
            .RunAsync(step, new AnalystInput(question, goal, items.Select(ToItemDto).ToList()), cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<Finding> Filter(FindingsOutput? output, IReadOnlySet<string> packKeys) =>
        (output?.Findings ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f.Title) && !string.IsNullOrWhiteSpace(f.Insight))
            .Select(f => f with { CitationKeys = (f.CitationKeys ?? []).Where(packKeys.Contains).Distinct(StringComparer.Ordinal).ToList() })
            .Where(f => f.CitationKeys.Count > 0)
            .Take(5)
            .ToList();

    private static ItemDto ToItemDto(PackItem item) =>
        new(item.CitationKey, item.Corpus, item.Title, item.Subtitle, item.Snippet,
            item.Values.Select(v => new ValueDto(v.Key, v.Value, v.Kind.ToString(), v.Currency)).ToList());
}
