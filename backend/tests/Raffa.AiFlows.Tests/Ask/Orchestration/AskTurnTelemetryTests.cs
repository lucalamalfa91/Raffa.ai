using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Shared.Audit;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Logging;
using Raffa.AiGateway.Telemetry;
using Raffa.Chat.Application.Reply;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.Ask.Orchestration;

/// <summary>F1 Ask split PR-8: a run id on every turn row, the budget/drop counters, the council's
/// dropped-play count and the keyed hash hook of the turn row (F3-T11).</summary>
public sealed class AskTurnTelemetryTests
{
    private static readonly SavingsGoal Goal = new(20000m, null, "EUR", null, null);

    private static PackItem Item(string key, string corpus, string title, string snippet, params PackValue[] values) =>
        new(key, corpus, title, null, null, null, snippet, "/contracts/x", null, null, "test", values, "contract-x");

    private static IReadOnlyList<PackItem> Pack() =>
    [
        Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow · OrderForm", "ServiceNow ends on 2028-04-10 (notice by 2027-10-13).",
            new PackValue("annualSpend", "230000", PackValueKind.Amount, "EUR")),
        Item("calc:savings-target", PackCorpus.Calc, "ServiceNow — saving target", "Target EUR 20000 is 8.7% of the annual spend of EUR 230000.",
            new PackValue("targetAmount", "20000", PackValueKind.Amount, "EUR")),
        Item("calc:lever[market-discount]", PackCorpus.Calc, "ServiceNow — Market discount", "Peers achieved a 9% discount: up to EUR 20700 a year.",
            new PackValue("estimatedHigh", "20700", PackValueKind.Amount, "EUR"), new PackValue("percent", "9", PackValueKind.Percentage)),
        Item("market:MKT-SNOW-EU-01", PackCorpus.Market, "ServiceNow · ITSM Professional", "Companies paid P50 EUR 100, achieved a 9% discount.",
            new PackValue("discountAchievedPct", "9", PackValueKind.Percentage)),
        Item("raffa:playbook:anchor-on-market", PackCorpus.Raffa, "Anchor the ask", "Open with the market median."),
    ];

    private static FixtureAiGateway Fixture() =>
        new(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());

    private static CopilotReply Reply() =>
        new(ReplyKind.Answer, "ok", [new ReplyCitation(1, PackCorpus.Tenant, "t", null, "k", null, null, null, null, null, null, null)], [], ReplyProvenance.NoModelCall([]), []);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class UpperHasher : IQueryHasher
    {
        public string Compute(string input) => "KEYED-" + input.Length;
    }

    // ---------------------------------------------------------------- the turn row

    [Fact]
    public async Task A_turn_row_carries_the_turn_and_a_run_id_also_when_no_model_was_called()
    {
        var audit = new RecordingAuditWriter();
        using var turn = RunContext.BeginTurn();
        using var run = RunContext.BeginRun();

        await new AskTurnAudit(audit, new FixedClock()).WriteAsync(TenantId.New(), Reply(), false, false, "skipped", false, "actor", default);

        var detail = Assert.Single(audit.Written).Detail!;
        Assert.Matches(@"\bturnId=[0-9a-f]{16}\b", detail);
        Assert.Matches(@"\brunId=[0-9a-f]{16}\b", detail);
        Assert.EndsWith("flow=none", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outside_a_turn_the_run_id_reads_none()
    {
        var audit = new RecordingAuditWriter();

        await new AskTurnAudit(audit, new FixedClock()).WriteAsync(TenantId.New(), Reply(), false, false, "skipped", false, "actor", default);

        Assert.Contains("runId=none", Assert.Single(audit.Written).Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_hasher_the_pack_hash_is_the_unkeyed_sha256_the_platform_always_wrote()
    {
        var audit = new RecordingAuditWriter();
        var reply = Reply();

        await new AskTurnAudit(audit, new FixedClock()).WriteAsync(TenantId.New(), reply, false, false, "skipped", false, "actor", default);
        await new AskTurnAudit(audit, new FixedClock(), new KeyedQueryHasher(() => null)).WriteAsync(TenantId.New(), reply, false, false, "skipped", false, "actor", default);

        var expected = QueryHasher.ComputeHash(string.Join('|', reply.Citations.Select(c => c.N + ":" + c.Corpus)));
        Assert.All(audit.Written, e => Assert.Equal(expected, e.ResourceId));
        Assert.All(audit.Written, e => Assert.Contains($"packHash={expected} ", e.Detail!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_configured_hasher_is_the_one_the_pack_hash_uses()
    {
        var audit = new RecordingAuditWriter();

        await new AskTurnAudit(audit, new FixedClock(), new UpperHasher()).WriteAsync(TenantId.New(), Reply(), false, false, "skipped", false, "actor", default);

        Assert.StartsWith("KEYED-", Assert.Single(audit.Written).ResourceId, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the budget fragment

    [Fact]
    public void The_budget_fragment_names_what_was_kept_what_was_cut_and_what_the_council_lost()
    {
        Assert.Equal(
            "packItems=12 budgetKept=9 budgetDropped=3 councilItemsDropped=2 playsDropped=1",
            AgentPhase.BudgetDetail(12, 9, 2, 1));
    }

    [Fact]
    public void The_fragment_leaves_the_flows_own_text_and_the_audit_regexes_alone()
    {
        using var turn = RunContext.BeginTurn();
        using var run = RunContext.BeginRun();
        RunContext.AddTurnDetail(new AskFlowOutcome([], [], ["market-researcher"], [], []).ToAuditDetail());
        RunContext.AddTurnDetail(AgentPhase.BudgetDetail(5, 5, 0, 0));

        var detail = RunContext.TurnDetail!;
        Assert.Matches(@"\bstepsRun=\d+ steps=\S+ failures=\d+ failedSteps=\S+ marketQueries=\d+", detail);
        Assert.EndsWith("packItems=5 budgetKept=5 budgetDropped=0 councilItemsDropped=0 playsDropped=0", detail, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- dropped plays

    private sealed class PlaysGateway(IAiGateway inner, string plays) : IAiGateway
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
                ? Task.FromResult(Result<AiAnalysisResult>.Success(
                    new AiAnalysisResult(plays, new AiCallMetadata("m", "v", CouncilAgents.Version, DateTimeOffset.UnixEpoch, "h"))))
                : inner.AnalyzeAsync(request, cancellationToken);
    }

    [Fact]
    public async Task The_council_counts_the_plays_it_drops_and_the_flow_forwards_the_count()
    {
        var gateway = new PlaysGateway(
            Fixture(),
            """
            {"plays":[
              {"rank":1,"lever":"","ask":"blank lever","expectedValueKeys":[],"fallback":"","timing":"","citationKeys":["calc:lever[market-discount]"]},
              {"rank":2,"lever":"Ghost","ask":"Ask for EUR 99999.","expectedValueKeys":[],"fallback":"","timing":"","citationKeys":["calc:lever[market-discount]"]},
              {"rank":3,"lever":"Unknown key","ask":"Ask for the discount.","expectedValueKeys":[],"fallback":"","timing":"","citationKeys":["calc:nope"]},
              {"rank":4,"lever":"Good","ask":"Ask for the 9% discount on EUR 230000.","expectedValueKeys":["estimatedHigh"],"fallback":"","timing":"","citationKeys":["calc:lever[market-discount]","fact:x:renewal"]}
            ],
            "verdict":{"targetReachable":true,"reason":"The 9% market discount alone covers EUR 20000."}}
            """);
        var options = new CouncilOptions();

        var council = await new NegotiationCouncil(gateway, options).RunAsync("q", Pack(), Goal);
        var flow = await new AskAgentFlow(new MarketResearcher(gateway, options), new NegotiationCouncil(gateway, options))
            .RunAsync(new AskFlowRequest("q", Pack(), Goal, null, RunMarketResearch: false, ConveneCouncil: true));

        Assert.Equal(3, council.PlaysDropped);
        Assert.Equal(3, flow.PlaysDropped);
        Assert.Single(council.Items, i => i.CitationKey.StartsWith("calc:council:play", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_council_that_drops_nothing_reports_zero_and_the_audit_text_of_the_flow_is_unchanged()
    {
        var options = new CouncilOptions();
        var flow = await new AskAgentFlow(new MarketResearcher(Fixture(), options), new NegotiationCouncil(Fixture(), options))
            .RunAsync(new AskFlowRequest("q", Pack(), Goal, null, RunMarketResearch: false, ConveneCouncil: true));

        Assert.Equal(0, flow.PlaysDropped);
        Assert.DoesNotContain("playsDropped", flow.ToAuditDetail(), StringComparison.Ordinal);
    }
}
