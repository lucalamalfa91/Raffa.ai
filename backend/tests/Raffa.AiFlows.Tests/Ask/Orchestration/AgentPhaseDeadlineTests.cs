using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Telemetry;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.Ask.Orchestration;

/// <summary>F1 Ask split PR-10 (A-04): the turn's caps, composed with the caller's token, and the explicit
/// degrade of an agent phase that hits one. Defaults (no cap) are today's behaviour.</summary>
public sealed class AgentPhaseDeadlineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

    private static PackItem Item(string key, string corpus, string snippet, params PackValue[] values) =>
        new(key, corpus, key, null, null, null, snippet, "/contracts/x", null, null, "test", values, "contract-x");

    private static IReadOnlyList<PackItem> Pack() =>
    [
        Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow ends on 2028-04-10 (notice by 2027-10-13).",
            new PackValue("annualSpend", "230000", PackValueKind.Amount, "EUR")),
        Item("calc:savings-target", PackCorpus.Calc, "Target EUR 20000 is 8.7% of the annual spend of EUR 230000.",
            new PackValue("targetAmount", "20000", PackValueKind.Amount, "EUR")),
        Item("calc:lever[market-discount]", PackCorpus.Calc, "Peers achieved a 9% discount: up to EUR 20700 a year.",
            new PackValue("estimatedHigh", "20700", PackValueKind.Amount, "EUR")),
        Item("market:MKT-SNOW-EU-01", PackCorpus.Market, "Companies paid P50 EUR 100, achieved a 9% discount.",
            new PackValue("discountAchievedPct", "9", PackValueKind.Percentage)),
    ];

    /// <summary>A pack reduced to what must be identical (the items carry list-typed values, compared by reference).</summary>
    private static IEnumerable<(string Key, string Title, string Snippet)> Shape(IEnumerable<PackItem> pack) =>
        pack.Select(i => (i.CitationKey, i.Title, i.Snippet));

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = Now;
    }

    /// <summary>Delegates to the offline fixture, except the agents named in <c>hang</c>, which never answer
    /// (until their token is cancelled).</summary>
    private sealed class HangingGateway(IAiGateway inner, params string[] hang) : IAiGateway
    {
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

        public async Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            if (hang.Contains(request.AgentName))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return await inner.AnalyzeAsync(request, cancellationToken);
        }
    }

    private static FixtureAiGateway Fixture() =>
        new(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());

    private static AgentPhase Phase(IAiGateway gateway, AskTurnOptions? options)
    {
        var council = new CouncilOptions();
        var flow = new AskAgentFlow(new MarketResearcher(gateway, council), new NegotiationCouncil(gateway, council));
        return new AgentPhase(flow, new MarketDataCheck(null!, new FixedClock()), new PackBudget(), new FixedClock(), options);
    }

    private static readonly AgentPhaseRequest CouncilRequest = new([], false, true, false);

    private static async Task<(AgentPhaseResult Result, string? Detail)> RunAsync(
        AgentPhase phase, string question, CancellationToken token = default)
    {
        using var turn = RunContext.BeginTurn();
        using var run = RunContext.BeginRun();
        var result = await phase.RunAsync(question, null, Pack(), CouncilRequest, token);
        return (result, RunContext.TurnDetail);
    }

    private static readonly string[] BothAnalysts = [CouncilAgents.ContractAnalystName, CouncilAgents.MarketAnalystName];

    // ---------------------------------------------------------------- defaults

    [Fact]
    public async Task With_no_cap_a_turn_is_not_degraded_and_the_audit_row_carries_no_deadline_fragment()
    {
        var (result, detail) = await RunAsync(Phase(Fixture(), new AskTurnOptions()), "which levers can I use?");

        Assert.False(result.AgentsDegraded);
        Assert.False(result.Flow.CutByDeadline);
        Assert.DoesNotContain(result.BoundedPack, i => i.CitationKey == "calc:deadline:market-analysis");
        Assert.DoesNotContain("deadline=", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_caps_set_but_not_reached_the_pack_is_the_one_without_caps_and_the_row_says_none()
    {
        var off = await RunAsync(Phase(Fixture(), new AskTurnOptions()), "which levers can I use?");
        var on = await RunAsync(
            Phase(Fixture(), new AskTurnOptions
            {
                TurnDeadline = TimeSpan.FromMinutes(5),
                AgentPhaseDeadline = TimeSpan.FromMinutes(5),
                AnswerDeadline = TimeSpan.FromMinutes(5),
            }),
            "which levers can I use?");

        Assert.Equal(Shape(off.Result.BoundedPack), Shape(on.Result.BoundedPack));
        Assert.False(on.Result.AgentsDegraded);
        Assert.Matches(@"\bdeadline=none turnMs=\d+", on.Detail);
    }

    [Fact]
    public async Task A_non_positive_cap_is_ignored()
    {
        var (result, detail) = await RunAsync(
            Phase(Fixture(), new AskTurnOptions { AgentPhaseDeadline = TimeSpan.Zero, TurnDeadline = TimeSpan.FromSeconds(-1) }),
            "which levers can I use?");

        Assert.False(result.AgentsDegraded);
        Assert.DoesNotContain("deadline=", detail, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the degrade

    [Fact]
    public async Task An_agent_phase_that_hits_its_cap_returns_with_an_explicit_item_and_the_row_says_agents()
    {
        var gateway = new HangingGateway(Fixture(), BothAnalysts);
        var phase = Phase(gateway, new AskTurnOptions { AgentPhaseDeadline = TimeSpan.FromMilliseconds(150) });

        var (result, detail) = await RunAsync(phase, "which levers can I use?");

        Assert.True(result.AgentsDegraded);
        Assert.True(result.Flow.CutByDeadline);
        Assert.Contains(result.Flow.Failures, f => f.StartsWith("deadline: negotiation-council skipped", StringComparison.Ordinal));
        Assert.Empty(result.Flow.CouncilItems);
        // The intent items come first, then the explicit gap.
        Assert.Equal(Pack().Select(i => i.CitationKey), result.BoundedPack.Take(Pack().Count).Select(i => i.CitationKey).Where(k => k != "calc:deadline:market-analysis"));
        var skipped = Assert.Single(result.BoundedPack, i => i.CitationKey == "calc:deadline:market-analysis");
        Assert.Equal(PackCorpus.Calc, skipped.Corpus);
        Assert.Equal("Market analysis not available", skipped.Title);
        Assert.Matches(@"\bdeadline=agents turnMs=\d+", detail);
        Assert.Contains("failedSteps=deadline", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_degraded_item_is_in_the_language_of_the_question()
    {
        var gateway = new HangingGateway(Fixture(), BothAnalysts);
        var phase = Phase(gateway, new AskTurnOptions { AgentPhaseDeadline = TimeSpan.FromMilliseconds(100) });

        var (result, _) = await RunAsync(phase, "quali leve posso usare per risparmiare sul rinnovo?");

        var skipped = Assert.Single(result.BoundedPack, i => i.CitationKey == "calc:deadline:market-analysis");
        Assert.Equal("Analisi di mercato non disponibile", skipped.Title);
        Assert.Equal(TurnDeadline.AgentsSkippedSentence(italian: true), skipped.Snippet);
    }

    [Fact]
    public async Task The_whole_turn_cap_degrades_the_agents_too_and_the_row_names_the_turn()
    {
        var gateway = new HangingGateway(Fixture(), BothAnalysts);
        var phase = Phase(gateway, new AskTurnOptions { TurnDeadline = TimeSpan.FromMilliseconds(150) });

        var (result, detail) = await RunAsync(phase, "which levers can I use?");

        Assert.True(result.AgentsDegraded);
        Assert.Matches(@"\bdeadline=turn turnMs=\d+", detail);
    }

    [Fact]
    public async Task What_an_earlier_step_found_survives_the_cut()
    {
        // The market data check (step 1) finishes; the council (step 3) is the one cut.
        var gateway = new HangingGateway(Fixture(), BothAnalysts);
        var council = new CouncilOptions();
        var flow = new AskAgentFlow(new MarketResearcher(gateway, council), new NegotiationCouncil(gateway, council));
        var check = new AskFlowRequest(
            "which levers can I use?",
            Pack(),
            null,
            _ => Task.FromResult(new MarketDataCheckResult([Item("market:check", PackCorpus.Market, "Typical notice is 90 days.")], [])),
            RunMarketResearch: false,
            ConveneCouncil: true,
            CancellationIsDeadline: () => true);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var outcome = await flow.RunAsync(check, cts.Token);

        Assert.Equal(["market:check"], outcome.MarketItems.Select(i => i.CitationKey));
        Assert.Equal([AskAgentFlow.MarketDataCheckStepName], outcome.StepsRun);
        Assert.True(outcome.CutByDeadline);
    }

    [Fact]
    public async Task The_caller_giving_up_is_not_a_degrade_it_still_propagates()
    {
        var gateway = new HangingGateway(Fixture(), BothAnalysts);
        var phase = Phase(gateway, new AskTurnOptions { AgentPhaseDeadline = TimeSpan.FromMinutes(5) });
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(phase, "which levers can I use?", caller.Token));
    }

    [Fact]
    public async Task Without_caps_a_cancellation_propagates_exactly_as_before()
    {
        var gateway = new HangingGateway(Fixture(), BothAnalysts);
        var phase = Phase(gateway, null);
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(phase, "which levers can I use?", caller.Token));
    }
}
