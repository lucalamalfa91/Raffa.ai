using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Planning;

namespace Raffa.Chat.Application.Council;

/// <summary>What the council produced for one turn: the pack items to insert, which agents ran,
/// and which failed (a failed agent degrades the council, never the turn).</summary>
public sealed record CouncilOutcome(
    IReadOnlyList<PackItem> Items,
    IReadOnlyList<string> AgentsRun,
    IReadOnlyList<string> Failures)
{
    public static CouncilOutcome Empty { get; } = new([], [], []);
}

/// <summary>
/// The multi-agent process behind a savings or negotiation turn — step 3 of <see cref="AskAgentFlow"/>,
/// after the market data check and the market researcher have enriched the pack. Round one: the contract analyst
/// and the market analyst read disjoint slices of the pack in parallel. Round two: the lever
/// strategist reads both sets of findings, the calculators' items and the playbook, and returns
/// ranked plays plus a verdict on the goal. The agents talk to each other only through these
/// structured findings — never free text — and their output enters the answer only as pack items
/// (corpus <see cref="PackCorpus.Calc"/>, provenance "negotiation council"), so the grounding and
/// numeric guards still police everything the final answer says.
///
/// <para>
/// Local validation, no model call: a play whose citation keys are not in the pack is dropped; a
/// play whose text carries a number the pack does not contain is dropped
/// (<see cref="NumericGuard"/> over the play's own text); the values a play names are copied from
/// the cited items so the answer can quote them verbatim.
/// </para>
/// </summary>
public sealed class NegotiationCouncil(IAiGateway aiGateway, CouncilOptions options)
{
    public const string Provenance = "negotiation council";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public async Task<CouncilOutcome> RunAsync(
        string question,
        IReadOnlyList<PackItem> pack,
        SavingsGoal? goal,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(pack);

        if (!options.Enabled || pack.Count < options.MinPackItems)
        {
            return CouncilOutcome.Empty;
        }

        var agentsRun = new List<string>();
        var failures = new List<string>();
        var goalDto = ToGoalDto(goal);

        var tenantItems = pack.Where(i => i.Corpus == PackCorpus.Tenant).Take(options.MaxItemsPerAgent).ToList();
        // The market analyst also reads what the market data check found missing on the contract.
        var marketItems = pack
            .Where(i => i.Corpus == PackCorpus.Market ||
                i.CitationKey.StartsWith("calc:lever", StringComparison.Ordinal) ||
                i.CitationKey.StartsWith("calc:contract-gaps", StringComparison.Ordinal))
            .Take(options.MaxItemsPerAgent)
            .ToList();

        // ----- Round one: two analysts, in parallel, over disjoint evidence -----
        var contractTask = tenantItems.Count > 0
            ? RunAnalystAsync(CouncilAgents.ContractAnalystName, CouncilAgents.ContractAnalystPrompt, question, goalDto, tenantItems, cancellationToken)
            : Task.FromResult<(IReadOnlyList<Finding> Findings, string? Failure)>(([], null));
        var marketTask = marketItems.Count > 0
            ? RunAnalystAsync(CouncilAgents.MarketAnalystName, CouncilAgents.MarketAnalystPrompt, question, goalDto, marketItems, cancellationToken)
            : Task.FromResult<(IReadOnlyList<Finding> Findings, string? Failure)>(([], null));

        await Task.WhenAll(contractTask, marketTask).ConfigureAwait(false);

        var (contractFindings, contractFailure) = contractTask.Result;
        var (marketFindings, marketFailure) = marketTask.Result;

        if (tenantItems.Count > 0)
        {
            agentsRun.Add(CouncilAgents.ContractAnalystName);
        }

        if (marketItems.Count > 0)
        {
            agentsRun.Add(CouncilAgents.MarketAnalystName);
        }

        if (contractFailure is not null)
        {
            failures.Add($"{CouncilAgents.ContractAnalystName}: {contractFailure}");
        }

        if (marketFailure is not null)
        {
            failures.Add($"{CouncilAgents.MarketAnalystName}: {marketFailure}");
        }

        // Findings may only cite the pack — filter here so a hallucinated key never reaches round two.
        var packKeys = pack.Select(i => i.CitationKey).ToHashSet(StringComparer.Ordinal);
        contractFindings = FilterFindings(contractFindings, packKeys);
        marketFindings = FilterFindings(marketFindings, packKeys);

        // ----- Round two: the strategist over both analysts' findings + calculators + playbook -----
        var strategistItems = pack
            .Where(i => i.Corpus == PackCorpus.Calc || i.CitationKey.StartsWith("raffa:playbook:", StringComparison.Ordinal))
            .Take(options.MaxItemsPerAgent + 6)
            .ToList();

        var strategistInput = new StrategistInput(
            question,
            goalDto,
            strategistItems.Select(ToItemDto).ToList(),
            new FindingsByAgent(contractFindings, marketFindings));

        agentsRun.Add(CouncilAgents.LeverStrategistName);

        var strategistResult = await aiGateway.AnalyzeAsync(
                new AiAnalysisRequest(
                    CouncilAgents.LeverStrategistName,
                    CouncilAgents.LeverStrategistPrompt,
                    JsonSerializer.Serialize(strategistInput, JsonOptions),
                    CouncilAgents.PlaysSchema,
                    CouncilAgents.Version),
                cancellationToken)
            .ConfigureAwait(false);

        if (strategistResult.IsFailure)
        {
            failures.Add($"{CouncilAgents.LeverStrategistName}: {strategistResult.Error}");
            return new CouncilOutcome([], agentsRun, failures);
        }

        StrategistPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<StrategistPayload>(strategistResult.Value.PayloadJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            failures.Add($"{CouncilAgents.LeverStrategistName}: payload was not valid JSON — {ex.Message}");
            return new CouncilOutcome([], agentsRun, failures);
        }

        if (payload is null)
        {
            failures.Add($"{CouncilAgents.LeverStrategistName}: payload parsed to null.");
            return new CouncilOutcome([], agentsRun, failures);
        }

        var items = BuildPlayItems(payload, pack, packKeys);

        if (payload.Verdict is { } verdict && !string.IsNullOrWhiteSpace(verdict.Reason) && NumericGuard.Validate(verdict.Reason, pack).Passed)
        {
            items.Add(new PackItem(
                "calc:council:verdict",
                PackCorpus.Calc,
                verdict.TargetReachable ? "Council verdict — the goal is reachable" : "Council verdict — the goal is not reachable as things stand",
                Provenance,
                null, null,
                verdict.Reason.Trim(),
                null, null, null,
                Provenance,
                []));
        }

        return new CouncilOutcome(items, agentsRun, failures);
    }

    private async Task<(IReadOnlyList<Finding> Findings, string? Failure)> RunAnalystAsync(
        string agentName,
        string systemPrompt,
        string question,
        GoalDto? goal,
        IReadOnlyList<PackItem> items,
        CancellationToken cancellationToken)
    {
        var input = new AnalystInput(question, goal, items.Select(ToItemDto).ToList());

        var result = await aiGateway.AnalyzeAsync(
                new AiAnalysisRequest(
                    agentName,
                    systemPrompt,
                    JsonSerializer.Serialize(input, JsonOptions),
                    CouncilAgents.FindingsSchema,
                    CouncilAgents.Version),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return ([], result.Error);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<FindingsPayload>(result.Value.PayloadJson, JsonOptions);
            return (payload?.Findings ?? [], null);
        }
        catch (JsonException ex)
        {
            return ([], $"payload was not valid JSON — {ex.Message}");
        }
    }

    private static IReadOnlyList<Finding> FilterFindings(IReadOnlyList<Finding> findings, HashSet<string> packKeys) =>
        findings
            .Where(f => !string.IsNullOrWhiteSpace(f.Title) && !string.IsNullOrWhiteSpace(f.Insight))
            .Select(f => f with { CitationKeys = (f.CitationKeys ?? []).Where(packKeys.Contains).Distinct(StringComparer.Ordinal).ToList() })
            .Where(f => f.CitationKeys!.Count > 0)
            .Take(5)
            .ToList();

    private List<PackItem> BuildPlayItems(StrategistPayload payload, IReadOnlyList<PackItem> pack, HashSet<string> packKeys)
    {
        var itemsByKey = pack.ToDictionary(i => i.CitationKey, StringComparer.Ordinal);
        var items = new List<PackItem>();
        var rank = 0;

        foreach (var play in (payload.Plays ?? []).OrderBy(p => p.Rank))
        {
            if (string.IsNullOrWhiteSpace(play.Lever) || string.IsNullOrWhiteSpace(play.Ask))
            {
                continue;
            }

            var citationKeys = (play.CitationKeys ?? []).Where(packKeys.Contains).Distinct(StringComparer.Ordinal).ToList();
            if (citationKeys.Count == 0)
            {
                continue;
            }

            var text = string.Join(" ", new[] { play.Ask, play.Timing, play.Fallback }.Where(t => !string.IsNullOrWhiteSpace(t)));
            if (!NumericGuard.Validate(text, pack).Passed)
            {
                continue;
            }

            var wantedValueKeys = (play.ExpectedValueKeys ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var citedItems = citationKeys.Select(k => itemsByKey[k]).ToList();
            var values = citedItems
                .SelectMany(i => i.Values)
                .Where(v => wantedValueKeys.Contains(v.Key))
                .GroupBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            rank++;
            var snippet = play.Ask.Trim() +
                          (string.IsNullOrWhiteSpace(play.Timing) ? string.Empty : $" Timing: {play.Timing.Trim()}") +
                          (string.IsNullOrWhiteSpace(play.Fallback) ? string.Empty : $" Fallback: {play.Fallback.Trim()}") +
                          $" Grounded in: {string.Join(", ", citationKeys)}.";

            items.Add(new PackItem(
                $"calc:council:play[{rank}]",
                PackCorpus.Calc,
                $"Play {rank} — {play.Lever.Trim()}",
                $"{Provenance} · rank {rank}",
                null, null,
                snippet,
                citedItems.Select(i => i.Href).FirstOrDefault(h => h is not null),
                null, null,
                Provenance,
                values,
                citedItems.Select(i => i.ContractId).FirstOrDefault(c => c is not null)));

            if (rank >= options.MaxPlays)
            {
                break;
            }
        }

        return items;
    }

    private static GoalDto? ToGoalDto(SavingsGoal? goal) =>
        goal is null || !goal.HasTarget
            ? null
            : new GoalDto(goal.TargetAmount, goal.TargetPercent, goal.Currency, goal.WindowDays, goal.WindowLabel);

    private static ItemDto ToItemDto(PackItem item) =>
        new(item.CitationKey, item.Corpus, item.Title, item.Subtitle, item.Snippet,
            item.Values.Select(v => new ValueDto(v.Key, v.Value, v.Kind.ToString(), v.Currency)).ToList());

    // ----- wire shapes (camelCase JSON; the fixture gateway mirrors "items" structurally) -----

    private sealed record GoalDto(decimal? TargetAmount, decimal? TargetPercent, string? Currency, int? WindowDays, string? WindowLabel);

    private sealed record ValueDto(string Key, string Value, string Kind, string? Currency);

    private sealed record ItemDto(string CitationKey, string Corpus, string Title, string? Subtitle, string Snippet, IReadOnlyList<ValueDto> Values);

    private sealed record AnalystInput(string Question, GoalDto? Goal, IReadOnlyList<ItemDto> Items);

    private sealed record FindingsByAgent(IReadOnlyList<Finding> ContractAnalyst, IReadOnlyList<Finding> MarketAnalyst);

    private sealed record StrategistInput(string Question, GoalDto? Goal, IReadOnlyList<ItemDto> Items, FindingsByAgent Findings);

    private sealed record FindingsPayload(IReadOnlyList<Finding>? Findings);

    internal sealed record Finding(string Title, string Insight, string? LeverType, IReadOnlyList<string>? CitationKeys);

    private sealed record StrategistPayload(IReadOnlyList<Play>? Plays, Verdict? Verdict);

    private sealed record Play(int Rank, string Lever, string Ask, IReadOnlyList<string>? ExpectedValueKeys, string? Fallback, string? Timing, IReadOnlyList<string>? CitationKeys);

    private sealed record Verdict(bool TargetReachable, string Reason);
}
