using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Telemetry;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.Ask.Orchestration;

/// <summary>F1 Ask split PR-9 (F1-T03 part 2): the budget before the agents and the play
/// revalidation, each behind its <see cref="AskTurnOptions"/> flag. Flag off is today's code path;
/// flag on is proved over the offline fixture gateway.</summary>
public sealed class AgentPhaseBudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly SavingsGoal Goal = new(20000m, null, "EUR", null, null);

    private static PackItem Item(string key, string corpus, string snippet, params PackValue[] values) =>
        new(key, corpus, key, null, null, null, snippet, "/contracts/x", null, null, "test", values, "contract-x");

    /// <summary>The savings pack of the council tests: facts, the target verdict, a lever, market evidence and a playbook.</summary>
    private static IReadOnlyList<PackItem> SavingsPack() =>
    [
        Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow ends on 2028-04-10 (notice by 2027-10-13).",
            new PackValue("annualSpend", "230000", PackValueKind.Amount, "EUR")),
        Item("calc:savings-target", PackCorpus.Calc, "Target EUR 20000 is 8.7% of the annual spend of EUR 230000.",
            new PackValue("targetAmount", "20000", PackValueKind.Amount, "EUR")),
        Item("calc:lever[market-discount]", PackCorpus.Calc, "Peers achieved a 9% discount: up to EUR 20700 a year.",
            new PackValue("estimatedHigh", "20700", PackValueKind.Amount, "EUR"), new PackValue("percent", "9", PackValueKind.Percentage)),
        Item("market:MKT-SNOW-EU-01", PackCorpus.Market, "Companies paid P50 EUR 100, achieved a 9% discount.",
            new PackValue("discountAchievedPct", "9", PackValueKind.Percentage)),
        Item("raffa:playbook:anchor-on-market", PackCorpus.Raffa, "Open with the market median."),
    ];

    private static FixtureAiGateway Fixture() =>
        new(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());

    /// <summary>A pack reduced to what must be identical (the items carry list-typed values, compared by reference).</summary>
    private static IEnumerable<(string Key, string Title, string Snippet)> Shape(IEnumerable<PackItem> pack) =>
        pack.Select(i => (i.CitationKey, i.Title, i.Snippet));

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = Now;
    }

    private sealed class RecordingGateway(IAiGateway inner) : IAiGateway
    {
        public Dictionary<string, string> Inputs { get; } = new(StringComparer.Ordinal);

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
            lock (Inputs)
            {
                Inputs[request.AgentName] = request.InputJson;
            }

            return inner.AnalyzeAsync(request, cancellationToken);
        }
    }

    private static AgentPhase Phase(IAiGateway gateway, PackBudget budget, AskTurnOptions? options)
    {
        var council = new CouncilOptions();
        var flow = new AskAgentFlow(new MarketResearcher(gateway, council), new NegotiationCouncil(gateway, council));
        return new AgentPhase(flow, new MarketDataCheck(null!, new FixedClock()), budget, new FixedClock(), options);
    }

    private static AgentPhaseRequest Council() => new([], false, true, false);

    private static async Task<(AgentPhaseResult Result, string? Detail)> RunAsync(
        AgentPhase phase, IReadOnlyList<PackItem> pack)
    {
        using var turn = RunContext.BeginTurn();
        using var run = RunContext.BeginRun();
        var result = await phase.RunAsync("quali leve per risparmiare 20k?", Goal, pack, Council(), CancellationToken.None);
        return (result, RunContext.TurnDetail);
    }

    // ---------------------------------------------------------------- parity

    [Fact]
    public async Task When_the_pack_fits_every_flag_state_yields_the_same_bounded_pack_and_the_same_council()
    {
        var off = await RunAsync(Phase(Fixture(), new PackBudget(), null), SavingsPack());
        var on = await RunAsync(
            Phase(Fixture(), new PackBudget(), new AskTurnOptions { BudgetBeforeAgents = true, AgentPackReserveTokens = 100, RevalidatePlaysAfterBudget = true }),
            SavingsPack());

        Assert.Equal(Shape(off.Result.BoundedPack), Shape(on.Result.BoundedPack));
        Assert.Equal(off.Result.Flow.StepsRun, on.Result.Flow.StepsRun);
        Assert.Equal(Shape(off.Result.Flow.CouncilItems), Shape(on.Result.Flow.CouncilItems));
        Assert.Contains(off.Result.BoundedPack, i => i.CitationKey.StartsWith("calc:council:play[", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_every_flag_off_the_audit_fragments_are_exactly_todays()
    {
        var (result, detail) = await RunAsync(Phase(Fixture(), new PackBudget(), new AskTurnOptions()), SavingsPack());

        Assert.NotNull(detail);
        Assert.DoesNotContain("budgetBeforeAgents", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("playsUngrounded", detail, StringComparison.Ordinal);
        Assert.Contains($"packItems={result.BoundedPack.Count} budgetKept={result.BoundedPack.Count} budgetDropped=0", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_a_flag_on_the_fragment_reads_both_counters_at_zero_when_nothing_was_cut()
    {
        var (_, detail) = await RunAsync(
            Phase(Fixture(), new PackBudget(), new AskTurnOptions { BudgetBeforeAgents = true, RevalidatePlaysAfterBudget = true }),
            SavingsPack());

        Assert.Contains("budgetBeforeAgents=on budgetDroppedBeforeAgents=0 revalidatePlays=on playsUngrounded=0", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_budget_overload_with_the_default_ceiling_is_the_plain_apply()
    {
        var big = new string('x', 3500);
        var pack = new[] { Item("a", PackCorpus.Tenant, big), Item("b", PackCorpus.Tenant, big), Item("c", PackCorpus.Tenant, "small") };
        var budget = new PackBudget(maxTokens: 1500);

        Assert.Equal(budget.Apply(pack), budget.Apply(pack, budget.MaxTokens));
        Assert.Equal(["a"], budget.Apply(pack, 1).Select(i => i.CitationKey));
        Assert.Equal(["a"], budget.Apply(pack, -50).Select(i => i.CitationKey));
    }

    // ---------------------------------------------------------------- budget before the agents

    private static IReadOnlyList<PackItem> LongIntentPack()
    {
        var big = new string('x', 3500); // about 1,000 tokens
        return
        [
            Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow ends on 2028-04-10."),
            Item("fact:x:big-tail", PackCorpus.Tenant, big),
            Item("fact:x:small-1", PackCorpus.Tenant, "Annual spend is EUR 230000."),
            Item("fact:x:small-2", PackCorpus.Tenant, "Payment terms are Net 30."),
        ];
    }

    [Fact]
    public async Task Flag_off_the_agents_read_the_whole_intent_pack_and_the_budget_runs_once_after_the_merge()
    {
        var gateway = new RecordingGateway(Fixture());

        var (result, detail) = await RunAsync(Phase(gateway, new PackBudget(maxTokens: 3000), new AskTurnOptions()), LongIntentPack());

        Assert.Contains("fact:x:big-tail", gateway.Inputs[CouncilAgents.ContractAnalystName], StringComparison.Ordinal);
        Assert.Contains("fact:x:small-2", gateway.Inputs[CouncilAgents.ContractAnalystName], StringComparison.Ordinal);
        Assert.DoesNotContain("budgetBeforeAgents", detail, StringComparison.Ordinal);
        Assert.NotEmpty(result.BoundedPack);
    }

    [Fact]
    public async Task Flag_on_the_intent_pack_is_cut_before_the_agents_read_it_and_the_cut_is_counted()
    {
        var gateway = new RecordingGateway(Fixture());
        var tail = new string('x', 3500);
        // Budget 1000, reserve 900: the intent ceiling is 100 tokens, so only the leading small items
        // survive; the 1,000-token tail (and everything after it) never reaches an agent.
        var pack = new List<PackItem>
        {
            Item("fact:x:a", PackCorpus.Tenant, "ServiceNow ends on 2028-04-10."),
            Item("fact:x:b", PackCorpus.Tenant, "Annual spend is EUR 230000."),
            Item("fact:x:c", PackCorpus.Market, "Peers paid less."),
            Item("fact:x:big-tail", PackCorpus.Tenant, tail),
            Item("fact:x:after", PackCorpus.Tenant, "Net 30."),
        };

        var (result, detail) = await RunAsync(
            Phase(gateway, new PackBudget(maxTokens: 1000), new AskTurnOptions { BudgetBeforeAgents = true, AgentPackReserveTokens = 900 }),
            pack);

        var analysts = gateway.Inputs[CouncilAgents.ContractAnalystName] + gateway.Inputs[CouncilAgents.MarketAnalystName];
        Assert.Contains("fact:x:a", analysts, StringComparison.Ordinal);
        Assert.DoesNotContain("fact:x:big-tail", analysts, StringComparison.Ordinal);
        Assert.DoesNotContain("fact:x:after", analysts, StringComparison.Ordinal);
        Assert.Contains("budgetBeforeAgents=on budgetDroppedBeforeAgents=2", detail, StringComparison.Ordinal);
        Assert.DoesNotContain(result.BoundedPack, i => i.CitationKey == "fact:x:big-tail");
    }

    [Fact]
    public async Task A_negative_reserve_is_read_as_none()
    {
        var gateway = new RecordingGateway(Fixture());

        var (result, _) = await RunAsync(
            Phase(gateway, new PackBudget(), new AskTurnOptions { BudgetBeforeAgents = true, AgentPackReserveTokens = -500 }),
            SavingsPack());

        Assert.Equal(SavingsPack().Count, result.BoundedPack.Count(i => !i.CitationKey.StartsWith("calc:council", StringComparison.Ordinal)));
    }

    // ---------------------------------------------------------------- play revalidation

    /// <summary>The budget (in tokens) that keeps the merged pack up to and including the council's last
    /// item and cuts the rest: the plays survive the budget, the evidence they cite after them does not.</summary>
    private static async Task<(int Budget, IReadOnlyList<PackItem> Merged)> BudgetThatKeepsPlaysButCutsTheirEvidenceAsync()
    {
        var wide = await RunAsync(Phase(Fixture(), new PackBudget(maxTokens: 100000), null), SavingsPack());
        var merged = wide.Result.BoundedPack;
        var lastCouncil = merged.Select((item, index) => (item, index))
            .Last(x => x.item.CitationKey.StartsWith("calc:council:", StringComparison.Ordinal)).index;
        var keep = merged.Take(lastCouncil + 1).ToList();
        return (PackBudgetEstimate(keep), merged);
    }

    private static int PackBudgetEstimate(IReadOnlyList<PackItem> items) =>
        // The same estimate PackBudget uses (3.5 characters per token, at least 1 per item).
        items.Sum(i => Math.Max(1, (int)Math.Ceiling(
            (i.Title.Length + (i.Subtitle?.Length ?? 0) + i.Snippet.Length + i.Values.Sum(v => v.Key.Length + v.Value.Length)) / 3.5)));

    [Fact]
    public async Task A_play_whose_cited_item_the_budget_cut_is_removed_and_counted_when_revalidation_is_on_and_kept_when_off()
    {
        var (budget, merged) = await BudgetThatKeepsPlaysButCutsTheirEvidenceAsync();
        var plays = merged.Where(i => i.CitationKey.StartsWith("calc:council:play[", StringComparison.Ordinal)).Select(i => i.CitationKey).ToList();
        Assert.NotEmpty(plays);

        var off = await RunAsync(Phase(Fixture(), new PackBudget(maxTokens: budget), new AskTurnOptions()), SavingsPack());
        var on = await RunAsync(
            Phase(Fixture(), new PackBudget(maxTokens: budget), new AskTurnOptions { RevalidatePlaysAfterBudget = true }),
            SavingsPack());

        var citations = on.Result.Flow.PlayCitations!;
        var cut = citations.Where(p => p.Value.Any(k => off.Result.BoundedPack.All(i => i.CitationKey != k))).Select(p => p.Key).ToList();
        Assert.NotEmpty(cut); // the scenario really cuts some play's evidence

        Assert.All(cut, key => Assert.Contains(off.Result.BoundedPack, i => i.CitationKey == key));
        Assert.All(cut, key => Assert.DoesNotContain(on.Result.BoundedPack, i => i.CitationKey == key));
        Assert.Contains($"revalidatePlays=on playsUngrounded={cut.Count}", on.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("playsUngrounded", off.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plays_whose_evidence_survives_are_never_removed()
    {
        var on = await RunAsync(
            Phase(Fixture(), new PackBudget(), new AskTurnOptions { RevalidatePlaysAfterBudget = true }),
            SavingsPack());

        Assert.Contains(on.Result.BoundedPack, i => i.CitationKey.StartsWith("calc:council:play[", StringComparison.Ordinal));
        Assert.Contains("playsUngrounded=0", on.Detail, StringComparison.Ordinal);
    }
}
