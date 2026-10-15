using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Negotiation.Verdict;
using Raffa.AiFlows.Shared.Guards;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiGateway;
using Raffa.AiGateway.Agents;

namespace Raffa.AiFlows.Negotiation.Orchestration;

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
/// ranked plays; the verdict on the goal is not theirs to give but <see cref="CouncilVerdict"/>'s
/// (computed from the calculators' items, one owner). The agents talk to each other only through these
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
public sealed class NegotiationCouncil
{
    public const string Provenance = "negotiation council";

    private readonly CouncilOptions options;
    private readonly StepRunner runner;
    private readonly ContractAnalystStep contractAnalyst;
    private readonly MarketAnalystStep marketAnalyst;
    private readonly LeverStrategistStep leverStrategist;

    /// <param name="aiGateway">The gateway every agent call goes through (the runner never bypasses it).</param>
    /// <param name="options">The council's bounds and kill switch.</param>
    /// <param name="runner">The request's Scoped step runner; one over <paramref name="aiGateway"/> when omitted.</param>
    public NegotiationCouncil(IAiGateway aiGateway, CouncilOptions options, StepRunner? runner = null)
    {
        ArgumentNullException.ThrowIfNull(aiGateway);
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        this.runner = runner ?? new StepRunner(aiGateway);
        contractAnalyst = new ContractAnalystStep(options.AgentDeadline);
        marketAnalyst = new MarketAnalystStep(options.AgentDeadline);
        leverStrategist = new LeverStrategistStep(options.AgentDeadline);
    }

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

        var tenantItems = pack.Where(i => i.Corpus == PackCorpus.Tenant).Take(options.MaxItemsPerAgent).ToList();
        // The market analyst also reads what the market data check found missing on the contract.
        var marketItems = pack
            .Where(i => i.Corpus == PackCorpus.Market ||
                i.CitationKey.StartsWith("calc:lever", StringComparison.Ordinal) ||
                i.CitationKey.StartsWith("calc:contract-gaps", StringComparison.Ordinal))
            .Take(options.MaxItemsPerAgent)
            .ToList();

        // Precondition (R1-05): the council reads the tenant's and the market's evidence. A pack of
        // calculator and playbook items only (no tenant item, no market item, no lever or gap
        // calculation for the market analyst) gives both analysts nothing to read, and the
        // strategist would run on empty findings - a model call with no input of its own. Not
        // convened: no agent runs, so none is reported in AgentsRun.
        if (tenantItems.Count == 0 && marketItems.Count == 0)
        {
            return CouncilOutcome.Empty;
        }

        var agentsRun = new List<string>();
        var failures = new List<string>();
        var goalDto = ToGoalDto(goal);

        // ----- Round one: two analysts, in parallel, over disjoint evidence -----
        var contractTask = tenantItems.Count > 0
            ? RunAnalystAsync(contractAnalyst, question, goalDto, tenantItems, cancellationToken)
            : Task.FromResult<(IReadOnlyList<CouncilFinding> Findings, string? Failure)>(([], null));
        var marketTask = marketItems.Count > 0
            ? RunAnalystAsync(marketAnalyst, question, goalDto, marketItems, cancellationToken)
            : Task.FromResult<(IReadOnlyList<CouncilFinding> Findings, string? Failure)>(([], null));

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

        var strategistInput = new CouncilStrategistInput(
            question,
            goalDto,
            strategistItems.Select(ToItemDto).ToList(),
            new CouncilFindingsByAgent(contractFindings, marketFindings));

        agentsRun.Add(CouncilAgents.LeverStrategistName);

        // The one required step: it cannot deliver (gateway failure, unusable payload, deadline) and
        // there is no council for the turn.
        var strategistResult = await runner.RunAsync(leverStrategist, strategistInput, cancellationToken).ConfigureAwait(false);
        if (strategistResult.Value is not { } payload)
        {
            failures.Add(strategistResult.Describe()!);
            return new CouncilOutcome([], agentsRun, failures);
        }

        var items = BuildPlayItems(payload, pack, packKeys);

        // One owner of the verdict (plan F2-D03): the strategist returns plays only; the verdict is
        // computed from the calculators' own items, so it cannot contradict them.
        if (CouncilVerdict.BuildItem(pack, goal, question) is { } verdictItem)
        {
            items.Add(verdictItem);
        }

        return new CouncilOutcome(items, agentsRun, failures);
    }

    /// <summary>One round-one analyst on the runner. A failed analyst is a skipped step, never an
    /// exception: no findings and the reason the council reports as <c>"agent: reason"</c>.</summary>
    private async Task<(IReadOnlyList<CouncilFinding> Findings, string? Failure)> RunAnalystAsync(
        IAgentStep<CouncilAnalystInput, CouncilFindingsPayload> step,
        string question,
        CouncilGoal? goal,
        IReadOnlyList<PackItem> items,
        CancellationToken cancellationToken)
    {
        var input = new CouncilAnalystInput(question, goal, items.Select(ToItemDto).ToList());
        var result = await runner.RunAsync(step, input, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? (result.Value?.Findings ?? [], null)
            : ([], result.Error ?? result.Reason.ToString());
    }

    private static IReadOnlyList<CouncilFinding> FilterFindings(IReadOnlyList<CouncilFinding> findings, HashSet<string> packKeys) =>
        findings
            .Where(f => !string.IsNullOrWhiteSpace(f.Title) && !string.IsNullOrWhiteSpace(f.Insight))
            .Select(f => f with { CitationKeys = (f.CitationKeys ?? []).Where(packKeys.Contains).Distinct(StringComparer.Ordinal).ToList() })
            .Where(f => f.CitationKeys!.Count > 0)
            .Take(5)
            .ToList();

    private List<PackItem> BuildPlayItems(CouncilPlaysPayload payload, IReadOnlyList<PackItem> pack, HashSet<string> packKeys)
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

    private static CouncilGoal? ToGoalDto(SavingsGoal? goal) =>
        goal is null || !goal.HasTarget
            ? null
            : new CouncilGoal(goal.TargetAmount, goal.TargetPercent, goal.Currency, goal.WindowDays, goal.WindowLabel);

    private static CouncilItem ToItemDto(PackItem item) =>
        new(item.CitationKey, item.Corpus, item.Title, item.Subtitle, item.Snippet,
            item.Values.Select(v => new CouncilValue(v.Key, v.Value, v.Kind.ToString(), v.Currency)).ToList());
}
