using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Chat.Application.Council;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Planning;
using Raffa.SharedKernel;

namespace Raffa.Chat.Tests.Council;

/// <summary>
/// Proves the negotiation council's contract with the rest of the engine: it runs the two
/// analysts and the strategist through <see cref="IAiGateway.AnalyzeAsync"/>, turns the
/// strategist's plays into calc-corpus pack items grounded only in the pack's own keys and
/// values, degrades silently when an agent fails, and stays off below the pack-size floor or when
/// disabled.
/// </summary>
public sealed class NegotiationCouncilTests
{
    private static readonly SavingsGoal Goal = new(20000m, null, "EUR", null, null);

    private static PackItem Item(string key, string corpus, string title, string snippet, params PackValue[] values) =>
        new(key, corpus, title, null, null, null, snippet, "/contracts/x", null, null, "test", values, "contract-x");

    private static IReadOnlyList<PackItem> Pack() =>
    [
        Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow · OrderForm", "ServiceNow ends on 2028-04-10 (notice by 2027-10-13).",
            new PackValue("annualSpend", "230000", PackValueKind.Amount, "EUR")),
        Item("calc:savings-target", PackCorpus.Calc, "ServiceNow — saving target and lever coverage", "Target EUR 20000 is 8.7% of the annual spend of EUR 230000.",
            new PackValue("targetAmount", "20000", PackValueKind.Amount, "EUR")),
        Item("calc:lever[market-discount]", PackCorpus.Calc, "ServiceNow — Market discount", "Peers achieved a 9% discount: up to EUR 20700 a year.",
            new PackValue("estimatedHigh", "20700", PackValueKind.Amount, "EUR"), new PackValue("percent", "9", PackValueKind.Percentage)),
        Item("market:MKT-SNOW-EU-01", PackCorpus.Market, "ServiceNow · ITSM Professional", "Companies paid P50 EUR 100, achieved a 9% discount.",
            new PackValue("discountAchievedPct", "9", PackValueKind.Percentage)),
        Item("raffa:playbook:anchor-on-market", PackCorpus.Raffa, "Anchor the ask on what peers paid", "Open with the market median."),
    ];

    private static NegotiationCouncil Council(IAiGateway gateway, CouncilOptions? options = null) =>
        new(gateway, options ?? new CouncilOptions());

    private static FixtureAiGateway Fixture() =>
        new(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());

    [Fact]
    public async Task Runs_both_analysts_and_the_strategist_and_returns_grounded_plays()
    {
        var recording = new RecordingGateway(Fixture());

        var outcome = await Council(recording).RunAsync("quali leve per risparmiare 20k?", Pack(), Goal);

        Assert.Equal(
            [CouncilAgents.ContractAnalystName, CouncilAgents.MarketAnalystName, CouncilAgents.LeverStrategistName],
            recording.Agents);
        Assert.Empty(outcome.Failures);
        Assert.NotEmpty(outcome.Items);

        var play = outcome.Items.First(i => i.CitationKey == "calc:council:play[1]");
        Assert.Equal(PackCorpus.Calc, play.Corpus);
        Assert.Equal(NegotiationCouncil.Provenance, play.Provenance);
        Assert.Contains("Grounded in: calc:lever[market-discount]", play.Snippet, StringComparison.Ordinal);
        Assert.Contains(play.Values, v => v.Key == "estimatedHigh" && v.Value == "20700");
        Assert.Contains(outcome.Items, i => i.CitationKey == "calc:council:verdict");
    }

    [Fact]
    public async Task The_strategist_sees_both_analysts_findings()
    {
        var recording = new RecordingGateway(Fixture());

        await Council(recording).RunAsync("where can I save?", Pack(), Goal);

        var strategistInput = recording.Inputs[CouncilAgents.LeverStrategistName];
        Assert.Contains("\"contractAnalyst\"", strategistInput, StringComparison.Ordinal);
        Assert.Contains("\"marketAnalyst\"", strategistInput, StringComparison.Ordinal);
        Assert.Contains("fact:x:renewal", strategistInput, StringComparison.Ordinal);
        Assert.Contains("market:MKT-SNOW-EU-01", strategistInput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_play_citing_an_unknown_key_or_an_unknown_number_is_dropped()
    {
        var scripted = new ScriptedGateway(
            findings: """{"findings":[]}""",
            plays: """
                   {"plays":[
                     {"rank":1,"lever":"Ghost","ask":"Ask for EUR 99999.","expectedValueKeys":[],"fallback":"","timing":"","citationKeys":["calc:lever[market-discount]"]},
                     {"rank":2,"lever":"Unknown key","ask":"Ask for the discount.","expectedValueKeys":[],"fallback":"","timing":"","citationKeys":["calc:nope"]},
                     {"rank":3,"lever":"Good","ask":"Ask for the 9% discount on EUR 230000.","expectedValueKeys":["estimatedHigh"],"fallback":"Hold the notice.","timing":"Before 2027-10-13.","citationKeys":["calc:lever[market-discount]","fact:x:renewal"]}
                   ],
                   "verdict":{"targetReachable":true,"reason":"The 9% market discount alone covers EUR 20000."}}
                   """);

        var outcome = await Council(scripted).RunAsync("q", Pack(), Goal);

        var play = Assert.Single(outcome.Items, i => i.CitationKey.StartsWith("calc:council:play", StringComparison.Ordinal));
        Assert.Equal("calc:council:play[1]", play.CitationKey);
        Assert.Equal("Play 1 — Good", play.Title);
        Assert.Contains("Timing: Before 2027-10-13.", play.Snippet, StringComparison.Ordinal);
        Assert.Single(play.Values, v => v.Key == "estimatedHigh");
    }

    [Fact]
    public async Task A_failing_analyst_degrades_the_council_but_the_strategist_still_runs()
    {
        var gateway = new FailingAgentGateway(Fixture(), CouncilAgents.MarketAnalystName);

        var outcome = await Council(gateway).RunAsync("q", Pack(), Goal);

        Assert.Single(outcome.Failures, f => f.StartsWith(CouncilAgents.MarketAnalystName, StringComparison.Ordinal));
        Assert.Contains(CouncilAgents.LeverStrategistName, outcome.AgentsRun);
        Assert.NotEmpty(outcome.Items);
    }

    [Fact]
    public async Task A_failing_strategist_yields_no_items_and_names_the_failure()
    {
        var gateway = new FailingAgentGateway(Fixture(), CouncilAgents.LeverStrategistName);

        var outcome = await Council(gateway).RunAsync("q", Pack(), Goal);

        Assert.Empty(outcome.Items);
        Assert.Single(outcome.Failures, f => f.StartsWith(CouncilAgents.LeverStrategistName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_gateway_without_the_analyst_role_degrades_to_an_empty_council()
    {
        var outcome = await Council(new LegacyGateway()).RunAsync("q", Pack(), Goal);

        Assert.Empty(outcome.Items);
        Assert.NotEmpty(outcome.Failures);
    }

    [Fact]
    public async Task Disabled_or_a_thin_pack_never_calls_the_gateway()
    {
        var recording = new RecordingGateway(Fixture());

        var disabled = await Council(recording, new CouncilOptions { Enabled = false }).RunAsync("q", Pack(), Goal);
        var thin = await Council(recording).RunAsync("q", Pack().Take(2).ToList(), Goal);

        Assert.Empty(disabled.Items);
        Assert.Empty(thin.Items);
        Assert.Empty(recording.Agents);
    }

    // ----- doubles -----

    private sealed class RecordingGateway(IAiGateway inner) : IAiGateway
    {
        public List<string> Agents { get; } = [];

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
            lock (Agents)
            {
                Agents.Add(request.AgentName);
                Inputs[request.AgentName] = request.InputJson;
            }

            return inner.AnalyzeAsync(request, cancellationToken);
        }
    }

    private sealed class FailingAgentGateway(IAiGateway inner, string failingAgent) : IAiGateway
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
            request.AgentName == failingAgent
                ? Task.FromResult(Result<AiAnalysisResult>.Failure("simulated outage"))
                : inner.AnalyzeAsync(request, cancellationToken);
    }

    private sealed class ScriptedGateway(string findings, string plays) : IAiGateway
    {
        private static readonly AiCallMetadata Metadata = new("scripted", "v1", CouncilAgents.Version, DateTimeOffset.UnixEpoch, "00");

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(
                request.AgentName == CouncilAgents.LeverStrategistName ? plays : findings, Metadata)));
    }

    /// <summary>An <see cref="IAiGateway"/> that predates the analyst role: the interface's own
    /// default <c>AnalyzeAsync</c> answers for it.</summary>
    private sealed class LegacyGateway : IAiGateway
    {
        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
