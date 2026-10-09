using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Negotiation.Tools;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Telemetry;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Planning;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.Negotiation.Orchestration;

/// <summary>
/// Plan T-01 / F2-T01 (the flow's run, steps and audit fragment) and F2-T08 / F2-D03 (one owner of
/// the verdict: the strategist's schema carries plays only, whatever it still says about
/// reachability is ignored, and the verdict follows the calculators' own items).
/// </summary>
public sealed class AskFlowTelemetryAndVerdictTests
{
    private const string Question = "quali leve per risparmiare 20k su ServiceNow?";

    private static readonly SavingsGoal Goal = new(20000m, null, "EUR", null, null);

    private static PackItem Item(string key, string corpus, string title, string? subtitle, string snippet, params PackValue[] values) =>
        new(key, corpus, title, subtitle, null, null, snippet, "/contracts/x", null, null, "test", values, "contract-x");

    private static IReadOnlyList<PackItem> Pack(string targetLabel, decimal coverageHigh) =>
    [
        Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow · OrderForm", null, "ServiceNow ends on 2028-04-10 (notice by 2027-10-13).",
            new PackValue("annualSpend", "230000", PackValueKind.Amount, "EUR")),
        Item("calc:savings-target", PackCorpus.Calc, "ServiceNow — saving target and lever coverage", targetLabel, "Target EUR 20000 is 8.7% of the annual spend of EUR 230000.",
            new PackValue("targetAmount", "20000", PackValueKind.Amount, "EUR"),
            new PackValue("annualSpend", "230000", PackValueKind.Amount, "EUR"),
            new PackValue("coverageLow", "1000", PackValueKind.Amount, "EUR"),
            new PackValue("coverageHigh", coverageHigh.ToString("0", System.Globalization.CultureInfo.InvariantCulture), PackValueKind.Amount, "EUR")),
        Item("calc:lever[market-discount]", PackCorpus.Calc, "ServiceNow — Market discount", null, "Peers achieved a 9% discount: up to EUR 20700 a year.",
            new PackValue("estimatedHigh", "20700", PackValueKind.Amount, "EUR"), new PackValue("percent", "9", PackValueKind.Percentage)),
        Item("market:MKT-SNOW-EU-01", PackCorpus.Market, "ServiceNow · ITSM Professional", null, "Companies paid P50 EUR 100, achieved a 9% discount.",
            new PackValue("discountAchievedPct", "9", PackValueKind.Percentage)),
        Item("raffa:playbook:anchor-on-market", PackCorpus.Raffa, "Anchor the ask on what peers paid", null, "Open with the market median."),
    ];

    private static FixtureAiGateway Fixture() =>
        new(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());

    // ----- one owner of the verdict -----

    [Fact]
    public void The_strategist_no_longer_owns_a_verdict()
    {
        Assert.DoesNotContain("targetReachable", CouncilAgents.PlaysSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("verdict", CouncilAgents.PlaysSchema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("targetReachable", CouncilAgents.LeverStrategistPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Then a verdict", CouncilAgents.LeverStrategistPrompt, StringComparison.Ordinal);
        Assert.Equal("council-v3", CouncilAgents.Version);

        using var schema = System.Text.Json.JsonDocument.Parse(CouncilAgents.PlaysSchema);
        var required = schema.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["plays"], required);
    }

    [Theory]
    [InlineData("target reachable", 25000, "is reachable")]
    [InlineData("target is a stretch", 14000, "is a stretch")]
    [InlineData("target not supported by the evidence", 3000, "not supported")]
    public async Task The_verdict_follows_the_calculators_item_whatever_the_strategist_says(string label, int coverageHigh, string expectedInTitle)
    {
        // The strategist (a scripted double) still sends the old verdict, always the opposite of the calculator's.
        var contradicting = label == "target reachable"
            ? """{"plays":[],"verdict":{"targetReachable":false,"reason":"No."}}"""
            : """{"plays":[],"verdict":{"targetReachable":true,"reason":"Yes."}}""";
        var gateway = new ScriptedStrategist(contradicting);

        var outcome = await new NegotiationCouncil(gateway, new CouncilOptions())
            .RunAsync("which levers can save 20k on ServiceNow?", Pack(label, coverageHigh), Goal);

        var verdict = Assert.Single(outcome.Items, i => i.CitationKey == "calc:council:verdict");
        Assert.Contains(expectedInTitle, verdict.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("Yes.", verdict.Snippet, StringComparison.Ordinal);
        Assert.DoesNotContain("No.", verdict.Snippet, StringComparison.Ordinal);
        Assert.Empty(outcome.Failures);
    }

    [Fact]
    public async Task A_strategist_payload_without_a_verdict_is_complete_and_the_fixture_sends_none()
    {
        var recording = new RecordingGateway(Fixture());

        var outcome = await new NegotiationCouncil(recording, new CouncilOptions()).RunAsync(Question, Pack("target reachable", 25000), Goal);

        Assert.DoesNotContain("verdict", recording.LastStrategistPayload!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(outcome.Failures);
        Assert.Contains(outcome.Items, i => i.CitationKey == "calc:council:play[1]");
        Assert.Single(outcome.Items, i => i.CitationKey == "calc:council:verdict");
    }

    [Fact]
    public async Task Without_a_goal_the_council_never_calls_anything_reachable()
    {
        var pack = Pack("no target named", 25000);

        var outcome = await new NegotiationCouncil(new RecordingGateway(Fixture()), new CouncilOptions())
            .RunAsync("Where can I save on ServiceNow?", pack, goal: null);

        var verdict = Assert.Single(outcome.Items, i => i.CitationKey == "calc:council:verdict");
        Assert.DoesNotContain("reachable", verdict.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("meaningful saving", verdict.Title, StringComparison.Ordinal);
    }

    // ----- the flow's run, steps and audit fragment -----

    [Fact]
    public async Task The_flow_runs_every_ai_call_under_one_run_and_the_turn_with_each_steps_name()
    {
        var capturing = new RecordingGateway(Fixture());
        var rag = new StubRag();
        var options = new CouncilOptions();
        var flow = new AskAgentFlow(new MarketResearcher(capturing, options, rag), new NegotiationCouncil(capturing, options));
        RunContextData? duringDataCheck = null;

        AskFlowOutcome outcome;
        using (RunContext.BeginTurn("turn-77"))
        {
            outcome = await flow.RunAsync(new AskFlowRequest(
                Question,
                Pack("target reachable", 25000),
                Goal,
                _ =>
                {
                    duringDataCheck = RunContext.Current;
                    return Task.FromResult(new MarketDataCheckResult([], ["ServiceNow: annual spend"]));
                },
                RunMarketResearch: true,
                ConveneCouncil: true));

            // The flow's scopes are gone once it returns.
            Assert.Null(RunContext.Current!.StepName);
            Assert.Null(RunContext.Current.RunId);
        }

        Assert.Equal("market-data-check", duringDataCheck!.StepName);
        Assert.Equal("turn-77", duringDataCheck.TurnId);
        Assert.Equal(outcome.RunId, duringDataCheck.RunId);
        Assert.NotNull(outcome.RunId);

        Assert.All(capturing.Contexts.Values, c =>
        {
            Assert.Equal(outcome.RunId, c.RunId);
            Assert.Equal("turn-77", c.TurnId);
        });
        Assert.Equal(CouncilAgents.MarketResearcherName, capturing.Contexts[CouncilAgents.MarketResearcherName].StepName);
        Assert.Equal(CouncilAgents.ContractAnalystName, capturing.Contexts[CouncilAgents.ContractAnalystName].StepName);
        Assert.Equal(CouncilAgents.MarketAnalystName, capturing.Contexts[CouncilAgents.MarketAnalystName].StepName);
        Assert.Equal(CouncilAgents.LeverStrategistName, capturing.Contexts[CouncilAgents.LeverStrategistName].StepName);
    }

    [Fact]
    public async Task A_flow_inside_a_run_reuses_that_run_and_one_outside_opens_its_own()
    {
        var gateway = new RecordingGateway(Fixture());
        var options = new CouncilOptions();
        var flow = new AskAgentFlow(new MarketResearcher(gateway, options, new StubRag()), new NegotiationCouncil(gateway, options));
        var request = new AskFlowRequest(Question, Pack("target reachable", 25000), Goal, null, RunMarketResearch: false, ConveneCouncil: true);

        AskFlowOutcome inside;
        using (RunContext.BeginTurn())
        using (RunContext.BeginRun("turn-run-1"))
        {
            inside = await flow.RunAsync(request);
        }

        var outside = await flow.RunAsync(request);

        Assert.Equal("turn-run-1", inside.RunId);
        Assert.Matches("^[0-9a-f]{16}$", outside.RunId!);
        Assert.Null(RunContext.Current);
    }

    [Fact]
    public async Task The_audit_fragment_names_steps_failures_and_counts_and_no_text()
    {
        var failing = new FailingStrategist(Fixture());
        var options = new CouncilOptions();
        var flow = new AskAgentFlow(new MarketResearcher(failing, options, new StubRag()), new NegotiationCouncil(failing, options));

        var outcome = await flow.RunAsync(new AskFlowRequest(
            Question, Pack("target reachable", 25000), Goal, null, RunMarketResearch: true, ConveneCouncil: true));
        var detail = outcome.ToAuditDetail();

        Assert.Contains($"runId={outcome.RunId}", detail, StringComparison.Ordinal);
        Assert.Contains($"stepsRun={outcome.StepsRun.Count}", detail, StringComparison.Ordinal);
        Assert.Contains("steps=market-researcher,contract-analyst,market-analyst,lever-strategist", detail, StringComparison.Ordinal);
        Assert.Contains("failures=1 failedSteps=lever-strategist", detail, StringComparison.Ordinal);
        Assert.Contains($"marketQueries={outcome.MarketQueries.Count}", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("provider exploded: SECRET-MODEL-OUTPUT", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-MODEL-OUTPUT", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceNow", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_audit_fragment_of_an_empty_flow_and_of_odd_failure_names_is_still_well_formed()
    {
        var empty = new AskFlowOutcome([], [], [], [], []).ToAuditDetail();
        var odd = new AskFlowOutcome([], [], ["market-data-check"], ["Some Free Text\nwith: lines", "market-researcher: boom"], ["q1", "q2"], "abc").ToAuditDetail();

        Assert.Equal("runId=none stepsRun=0 steps=none failures=0 failedSteps=none marketQueries=0 marketItems=0 councilItems=0", empty);
        Assert.Contains("runId=abc stepsRun=1 steps=market-data-check failures=2 failedSteps=unknown,market-researcher marketQueries=2", odd, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', odd);
    }

    // ----- doubles -----

    private sealed class StubRag : IMarketRagSearch
    {
        public Task<Result<IReadOnlyList<PackItem>>> SearchAsync(string query, int topK, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<PackItem>>.Success(
                (IReadOnlyList<PackItem>)[Item("market:MKT-1", PackCorpus.Market, "ServiceNow · note", null, "Peers paid P50 EUR 100.")]));
    }

    /// <summary>Delegates to the fixture, remembering the run context each agent was called under.</summary>
    private sealed class RecordingGateway(IAiGateway inner) : IAiGateway
    {
        public Dictionary<string, RunContextData> Contexts { get; } = new(StringComparer.Ordinal);

        public string? LastStrategistPayload { get; private set; }

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
            lock (Contexts)
            {
                Contexts[request.AgentName] = RunContext.Current ?? new RunContextData(null, null, null);
            }

            var result = await inner.AnalyzeAsync(request, cancellationToken);
            if (request.AgentName == CouncilAgents.LeverStrategistName && result.IsSuccess)
            {
                LastStrategistPayload = result.Value.PayloadJson;
            }

            return result;
        }
    }

    /// <summary>Analysts get the fixture's answer; the strategist returns a fixed script.</summary>
    private sealed class ScriptedStrategist(string strategistPayload) : IAiGateway
    {
        private static readonly AiCallMetadata Metadata = new("scripted", "v1", CouncilAgents.Version, DateTimeOffset.UnixEpoch, "00");

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(
                request.AgentName == CouncilAgents.LeverStrategistName ? strategistPayload : """{"findings":[]}""", Metadata)));
    }

    /// <summary>The fixture, except the strategist fails with a message that must never reach the audit.</summary>
    private sealed class FailingStrategist(IAiGateway inner) : IAiGateway
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

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
            request.AgentName == CouncilAgents.LeverStrategistName
                ? Task.FromResult(Result<AiAnalysisResult>.Failure("provider exploded: SECRET-MODEL-OUTPUT"))
                : inner.AnalyzeAsync(request, cancellationToken);
    }
}
