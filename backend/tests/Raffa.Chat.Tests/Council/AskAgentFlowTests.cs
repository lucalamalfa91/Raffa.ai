using System.Text.Json;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Chat.Application.Council;
using Raffa.Chat.Application.Pack;
using Raffa.SharedKernel;

namespace Raffa.Chat.Tests.Council;

/// <summary>
/// Ask's agentic flow, one coordinated sequence: the deterministic market data check first, then
/// the market researcher querying the market RAG for what is still missing, then the negotiation
/// council over everything the market steps found. Each step sees the steps before it; a failed
/// agent or search degrades the flow, never the turn; the researcher's notes are the RAG's own
/// records, de-duplicated, capped and labelled when they come from similar contracts.
/// </summary>
public sealed class AskAgentFlowTests
{
    private const string Question = "mi aiuti a creare una mail per il rinnovo del contratto Oracle?";

    private static PackItem Item(string key, string corpus, string title, string snippet, params PackValue[] values) =>
        new(key, corpus, title, "subtitle", null, null, snippet, null, null, null, "test", values, "contract-x");

    private static IReadOnlyList<PackItem> IntentPack() =>
    [
        Item("fact:x:renewal", PackCorpus.Tenant, "Oracle · OrderForm", "Oracle ends on 2027-03-19 (notice by 2026-10-21)."),
        Item("calc:criticality[x]", PackCorpus.Calc, "Oracle — criticality", "Criticality 50.9 out of 100."),
    ];

    private static MarketDataCheckResult DataCheck() =>
        new(
            [
                Item("calc:contract-gaps[x]", PackCorpus.Calc, "Oracle · data not on the contract", "The validated Oracle contract has no value for: annual spend."),
                Item("market:estimate:x:annual-value", PackCorpus.Market, "Oracle · market estimate · annual value",
                    "Market estimate, not a figure from your contract: the annual value is between EUR 250,000 and EUR 500,000.",
                    new PackValue("estimateLow", "250000", PackValueKind.Amount, "EUR")),
                Item("fact:x:renewal", PackCorpus.Tenant, "Oracle · OrderForm", "Oracle ends on 2027-03-19 (notice by 2026-10-21)."),
            ],
            ["Oracle: annual spend"]);

    private static FixtureAiGateway Fixture() =>
        new(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());

    private static AskAgentFlow Flow(IAiGateway gateway, IMarketRagSearch? rag, CouncilOptions? options = null)
    {
        options ??= new CouncilOptions();
        return new AskAgentFlow(new MarketResearcher(gateway, options, rag), new NegotiationCouncil(gateway, options));
    }

    [Fact]
    public async Task The_steps_run_in_order_and_each_sees_what_the_steps_before_it_found()
    {
        var gateway = new RecordingGateway(Fixture());
        var rag = new StubRag(Note("market:MKT-ORA-1", "Oracle · Fusion Cloud HCM"), Note("market:MKT-ORA-2", "Oracle · Database"));
        var dataCheckRuns = 0;

        var outcome = await Flow(gateway, rag).RunAsync(new AskFlowRequest(
            Question,
            IntentPack(),
            null,
            _ => { dataCheckRuns++; return Task.FromResult(DataCheck()); },
            RunMarketResearch: true,
            ConveneCouncil: true));

        Assert.Equal(1, dataCheckRuns);
        Assert.Equal(
            [AskAgentFlow.MarketDataCheckStepName, CouncilAgents.MarketResearcherName, CouncilAgents.ContractAnalystName, CouncilAgents.MarketAnalystName, CouncilAgents.LeverStrategistName],
            outcome.StepsRun);
        Assert.Empty(outcome.Failures);

        // The researcher read the data check's findings and the missing fields...
        var researcherInput = gateway.Inputs[CouncilAgents.MarketResearcherName];
        Assert.Contains("market:estimate:x:annual-value", researcherInput, StringComparison.Ordinal);
        Assert.Contains("Oracle: annual spend", researcherInput, StringComparison.Ordinal);
        Assert.Equal(["Oracle OrderForm"], rag.Queries);

        // ...and the market analyst read the data check's and the researcher's items.
        var marketAnalystInput = gateway.Inputs[CouncilAgents.MarketAnalystName];
        Assert.Contains("market:estimate:x:annual-value", marketAnalystInput, StringComparison.Ordinal);
        Assert.Contains("market:MKT-ORA-1", marketAnalystInput, StringComparison.Ordinal);
        Assert.Contains("calc:contract-gaps[x]", marketAnalystInput, StringComparison.Ordinal);

        // Market items come back once each, never repeating the intent's own.
        Assert.Equal(
            ["calc:contract-gaps[x]", "market:estimate:x:annual-value", "market:MKT-ORA-1", "market:MKT-ORA-2"],
            outcome.MarketItems.Select(i => i.CitationKey));
        Assert.All(outcome.MarketItems.Where(i => i.CitationKey.StartsWith("market:MKT", StringComparison.Ordinal)),
            i => Assert.StartsWith("market RAG · market-researcher", i.Provenance, StringComparison.Ordinal));
        Assert.NotEmpty(outcome.CouncilItems);
    }

    [Fact]
    public async Task Without_a_data_check_or_the_council_only_the_researcher_runs()
    {
        var gateway = new RecordingGateway(Fixture());

        var outcome = await Flow(gateway, new StubRag(Note("market:MKT-ORA-1", "Oracle · Database"))).RunAsync(
            new AskFlowRequest(Question, IntentPack(), null, null, RunMarketResearch: true, ConveneCouncil: false));

        Assert.Equal([CouncilAgents.MarketResearcherName], outcome.StepsRun);
        Assert.Equal(["market:MKT-ORA-1"], outcome.MarketItems.Select(i => i.CitationKey));
        Assert.Empty(outcome.CouncilItems);
    }

    [Fact]
    public async Task Without_the_market_rag_the_researcher_is_skipped_and_the_rest_still_runs()
    {
        var gateway = new RecordingGateway(Fixture());

        var outcome = await Flow(gateway, rag: null).RunAsync(
            new AskFlowRequest(Question, IntentPack(), null, _ => Task.FromResult(DataCheck()), RunMarketResearch: true, ConveneCouncil: false));

        Assert.Equal([AskAgentFlow.MarketDataCheckStepName], outcome.StepsRun);
        Assert.DoesNotContain(CouncilAgents.MarketResearcherName, gateway.Agents);
        Assert.Equal(2, outcome.MarketItems.Count);
    }

    [Fact]
    public async Task A_failed_researcher_degrades_the_flow_never_the_turn()
    {
        var gateway = new FailingGateway(Fixture(), CouncilAgents.MarketResearcherName);

        var outcome = await Flow(gateway, new StubRag(Note("market:MKT-ORA-1", "Oracle · Database"))).RunAsync(
            new AskFlowRequest(Question, IntentPack(), null, _ => Task.FromResult(DataCheck()), RunMarketResearch: true, ConveneCouncil: false));

        Assert.Contains(outcome.Failures, f => f.StartsWith(CouncilAgents.MarketResearcherName, StringComparison.Ordinal));
        Assert.Equal(["calc:contract-gaps[x]", "market:estimate:x:annual-value"], outcome.MarketItems.Select(i => i.CitationKey));
    }

    [Fact]
    public async Task A_question_nothing_in_the_market_could_inform_gets_no_query()
    {
        var rag = new StubRag(Note("market:MKT-ORA-1", "Oracle · Database"));

        var outcome = await Flow(new RecordingGateway(Fixture()), rag).RunAsync(
            new AskFlowRequest("What liability do we have with Oracle?", IntentPack(), null, null, RunMarketResearch: true, ConveneCouncil: false));

        Assert.Empty(rag.Queries);
        Assert.Empty(outcome.MarketItems);
    }

    [Fact]
    public async Task The_researchers_queries_are_bounded_deduplicated_capped_and_similar_contracts_are_labelled()
    {
        var payload = JsonSerializer.Serialize(new
        {
            queries = new object[]
            {
                new { query = "Oracle Database EU", scope = "same-supplier" },
                new { query = "  oracle   database EU ", scope = "same-supplier" },
                new { query = "ERP enterprise licences EU 500-2000 employees", scope = "similar-contracts" },
                new { query = "", scope = "same-supplier" },
                new { query = "a fourth query past the cap", scope = "same-supplier" },
            },
        });
        var rag = new StubRag(query => query.StartsWith("Oracle", StringComparison.Ordinal)
            ? [Note("market:MKT-ORA-1", "Oracle · Database"), Note("market:MKT-ORA-1", "Oracle · Database")]
            : [Note("market:MKT-SAP-1", "SAP · S/4HANA Cloud"), Note("market:MKT-ORA-1", "Oracle · Database")]);
        var options = new CouncilOptions { MarketResearchMaxQueries = 2, MarketResearchMaxItems = 3 };

        var outcome = await new MarketResearcher(new ScriptedGateway(Fixture(), payload), options, rag)
            .QueryMarketRagAsync("Quanto pagano clienti simili per Oracle?", null, IntentPack(), []);

        Assert.Equal(["Oracle Database EU", "ERP enterprise licences EU 500-2000 employees"], outcome.Queries);
        Assert.Equal(outcome.Queries, rag.Queries);
        Assert.Equal(["market:MKT-ORA-1", "market:MKT-SAP-1"], outcome.Items.Select(i => i.CitationKey));
        Assert.DoesNotContain("similar contract", outcome.Items[0].Subtitle ?? string.Empty, StringComparison.Ordinal);
        Assert.StartsWith("similar contract · ", outcome.Items[1].Subtitle, StringComparison.Ordinal);
    }

    private static PackItem Note(string key, string title) =>
        new(key, PackCorpus.Market, title, "representative market data", null, null,
            $"Companies of 500-2000 employees closing {title} paid P50 EUR 100 (P25 90-P75 110).",
            null, null, key["market:".Length..], "representative market data · mock feed", []);

    /// <summary>Returns notes per query (the same ones for every query by default), recording the
    /// queries.</summary>
    private sealed class StubRag(Func<string, PackItem[]> notesFor) : IMarketRagSearch
    {
        public StubRag(params PackItem[] notes)
            : this(_ => notes)
        {
        }

        public List<string> Queries { get; } = [];

        public Task<Result<IReadOnlyList<PackItem>>> SearchAsync(string query, int topK, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(Result<IReadOnlyList<PackItem>>.Success(notesFor(query).Take(topK).ToList()));
        }
    }

    private class RecordingGateway(IAiGateway inner) : IAiGateway
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

        public virtual Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            lock (Agents)
            {
                Agents.Add(request.AgentName);
                Inputs[request.AgentName] = request.InputJson;
            }

            return inner.AnalyzeAsync(request, cancellationToken);
        }
    }

    private sealed class FailingGateway(IAiGateway inner, string failingAgent) : RecordingGateway(inner)
    {
        public override Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
            request.AgentName == failingAgent
                ? Task.FromResult(Result<AiAnalysisResult>.Failure("agent unavailable"))
                : base.AnalyzeAsync(request, cancellationToken);
    }

    /// <summary>Answers the market researcher with a fixed payload.</summary>
    private sealed class ScriptedGateway(IAiGateway inner, string researcherPayload) : RecordingGateway(inner)
    {
        public override async Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            var result = await base.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
            return request.AgentName == CouncilAgents.MarketResearcherName && result.IsSuccess
                ? Result<AiAnalysisResult>.Success(result.Value with { PayloadJson = researcherPayload })
                : result;
        }
    }
}
