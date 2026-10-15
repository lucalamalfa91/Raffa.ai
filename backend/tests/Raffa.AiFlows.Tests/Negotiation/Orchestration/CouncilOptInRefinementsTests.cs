using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Negotiation.Tools;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.Negotiation.Orchestration;

/// <summary>
/// The opt-in refinements of the negotiation flow (plan F2-T04 and F2-T09): the market researcher's
/// queries run concurrently and merge as the sequential run does, and the strategist is sent back once
/// when none of its plays can be used. Both are off by default; off, the flow is the one it always was.
/// </summary>
public sealed class CouncilOptInRefinementsTests
{
    private static readonly SavingsGoal Goal = new(20000m, null, "EUR", null, null);

    private static PackItem Item(string key, string corpus, string title, string snippet, params PackValue[] values) =>
        new(key, corpus, title, null, null, null, snippet, "/contracts/x", null, null, "test", values, "contract-x");

    private static IReadOnlyList<PackItem> Pack() =>
    [
        Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow · OrderForm", "ServiceNow ends on 2028-04-10.",
            new PackValue("annualSpend", "230000", PackValueKind.Amount, "EUR")),
        Item("calc:lever[market-discount]", PackCorpus.Calc, "ServiceNow — Market discount", "Peers achieved a 9% discount: up to EUR 20700 a year.",
            new PackValue("estimatedHigh", "20700", PackValueKind.Amount, "EUR"), new PackValue("percent", "9", PackValueKind.Percentage)),
        Item("market:MKT-1", PackCorpus.Market, "ServiceNow · ITSM", "Companies achieved a 9% discount.",
            new PackValue("discountAchievedPct", "9", PackValueKind.Percentage)),
        Item("raffa:playbook:anchor-on-market", PackCorpus.Raffa, "Anchor", "Open with the market median."),
    ];

    private const string EmptyFindings = """{"findings":[]}""";

    private const string ValidPlays =
        """{"plays":[{"rank":1,"lever":"Market discount","ask":"Ask for a discount in line with peers.","expectedValueKeys":["percent"],"fallback":null,"timing":null,"citationKeys":["calc:lever[market-discount]"]}]}""";

    private const string UnusablePlays =
        """{"plays":[{"rank":1,"lever":"Invented lever","ask":"Ask for it.","expectedValueKeys":[],"fallback":null,"timing":null,"citationKeys":["calc:made-up"]}]}""";

    // ---------------------------------------------------------------- F2-T09: the semantic retry

    [Fact]
    public async Task Off_by_default_an_unusable_strategist_answer_is_dropped_without_a_second_call()
    {
        var gateway = new ScriptedGateway(EmptyFindings, [UnusablePlays, ValidPlays]);

        var outcome = await new NegotiationCouncil(gateway, new CouncilOptions()).RunAsync("q", Pack(), Goal);

        Assert.Equal(1, gateway.StrategistCalls);
        Assert.Equal(1, outcome.PlaysDropped);
        Assert.DoesNotContain(outcome.Items, i => i.CitationKey.StartsWith("calc:council:play[", StringComparison.Ordinal));
    }

    [Fact]
    public async Task On_an_answer_with_no_usable_play_is_sent_back_once_with_the_violation_named()
    {
        var gateway = new ScriptedGateway(EmptyFindings, [UnusablePlays, ValidPlays]);

        var outcome = await new NegotiationCouncil(gateway, new CouncilOptions { SemanticPlayRetry = true }).RunAsync("q", Pack(), Goal);

        Assert.Equal(2, gateway.StrategistCalls);
        Assert.Contains("play 1: it cites no key that is in the input items", gateway.StrategistPrompts[1], StringComparison.Ordinal);
        Assert.DoesNotContain("play 1:", gateway.StrategistPrompts[0], StringComparison.Ordinal);
        Assert.Contains(outcome.Items, i => i.CitationKey == "calc:council:play[1]");
        Assert.Equal(0, outcome.PlaysDropped);
        Assert.Empty(outcome.Failures);
    }

    [Fact]
    public async Task On_a_usable_first_answer_costs_no_retry()
    {
        var gateway = new ScriptedGateway(EmptyFindings, [ValidPlays]);

        var outcome = await new NegotiationCouncil(gateway, new CouncilOptions { SemanticPlayRetry = true }).RunAsync("q", Pack(), Goal);

        Assert.Equal(1, gateway.StrategistCalls);
        Assert.Contains(outcome.Items, i => i.CitationKey == "calc:council:play[1]");
    }

    [Fact]
    public async Task On_a_partly_usable_answer_keeps_the_good_play_and_does_not_retry()
    {
        const string mixed =
            """{"plays":[{"rank":1,"lever":"Invented","ask":"x","expectedValueKeys":[],"fallback":null,"timing":null,"citationKeys":["calc:made-up"]},{"rank":2,"lever":"Market discount","ask":"Ask for a discount in line with peers.","expectedValueKeys":[],"fallback":null,"timing":null,"citationKeys":["calc:lever[market-discount]"]}]}""";
        var gateway = new ScriptedGateway(EmptyFindings, [mixed, ValidPlays]);

        var outcome = await new NegotiationCouncil(gateway, new CouncilOptions { SemanticPlayRetry = true }).RunAsync("q", Pack(), Goal);

        Assert.Equal(1, gateway.StrategistCalls);
        Assert.Equal(1, outcome.PlaysDropped);
        Assert.Contains(outcome.Items, i => i.CitationKey == "calc:council:play[1]");
    }

    [Fact]
    public async Task On_a_second_unusable_answer_the_council_reports_the_strategist_failure_like_an_empty_one()
    {
        var gateway = new ScriptedGateway(EmptyFindings, [UnusablePlays, UnusablePlays]);

        var outcome = await new NegotiationCouncil(gateway, new CouncilOptions { SemanticPlayRetry = true }).RunAsync("q", Pack(), Goal);

        Assert.Equal(2, gateway.StrategistCalls);
        Assert.Empty(outcome.Items);
        Assert.Contains(outcome.Failures, f => f.StartsWith(CouncilAgents.LeverStrategistName + ":", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- F2-T04: concurrent market queries

    private const string ThreeQueries =
        """{"queries":[{"query":"alpha","scope":"same-supplier"},{"query":"beta","scope":"similar-contracts"},{"query":"gamma","scope":"similar-contracts"}]}""";

    [Fact]
    public async Task Queries_run_one_after_the_other_by_default()
    {
        var rag = new GatedRag();
        var researcher = new MarketResearcher(new ScriptedGateway(ThreeQueries, []), new CouncilOptions(), rag);

        await researcher.QueryMarketRagAsync("q", Goal, Pack(), []);

        Assert.Equal(1, rag.MaxConcurrent);
        Assert.Equal(["alpha", "beta", "gamma"], rag.Issued);
    }

    [Fact]
    public async Task With_the_option_on_the_queries_overlap_and_the_pack_is_the_sequential_one()
    {
        var sequentialRag = new GatedRag();
        var sequential = await new MarketResearcher(new ScriptedGateway(ThreeQueries, []), new CouncilOptions(), sequentialRag)
            .QueryMarketRagAsync("q", Goal, Pack(), []);

        var parallelRag = new GatedRag { Gate = new TaskCompletionSource() };
        var researcher = new MarketResearcher(
            new ScriptedGateway(ThreeQueries, []), new CouncilOptions { MarketResearchParallelQueries = true }, parallelRag);
        var running = researcher.QueryMarketRagAsync("q", Goal, Pack(), []);

        // All three searches must be in flight before any of them is allowed to finish.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (parallelRag.MaxConcurrent < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(3, parallelRag.MaxConcurrent);
        parallelRag.Gate.SetResult();
        var parallel = await running;

        Assert.Equal(sequential.Items.Select(i => i.CitationKey), parallel.Items.Select(i => i.CitationKey));
        Assert.Equal(sequential.Items.Select(i => i.Subtitle), parallel.Items.Select(i => i.Subtitle));
        Assert.Equal(sequential.Queries, parallel.Queries);
        Assert.Null(parallel.Failure);
    }

    [Fact]
    public async Task With_the_option_on_a_failed_query_is_reported_and_the_others_still_land()
    {
        var rag = new GatedRag { FailQuery = "beta" };
        var outcome = await new MarketResearcher(
                new ScriptedGateway(ThreeQueries, []), new CouncilOptions { MarketResearchParallelQueries = true }, rag)
            .QueryMarketRagAsync("q", Goal, Pack(), []);

        Assert.Equal(["market:alpha", "market:gamma"], outcome.Items.Select(i => i.CitationKey));
        Assert.Contains("'beta'", outcome.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_the_option_on_the_item_cap_still_holds_and_the_earlier_queries_win()
    {
        var rag = new GatedRag();
        var outcome = await new MarketResearcher(
                new ScriptedGateway(ThreeQueries, []),
                new CouncilOptions { MarketResearchParallelQueries = true, MarketResearchMaxItems = 2 },
                rag)
            .QueryMarketRagAsync("q", Goal, Pack(), []);

        Assert.Equal(["market:alpha", "market:beta"], outcome.Items.Select(i => i.CitationKey));
    }

    // ---------------------------------------------------------------- doubles

    private sealed class GatedRag : IMarketRagSearch
    {
        private int running;
        private int maxConcurrent;
        private readonly object sync = new();
        private readonly List<string> issued = [];

        public TaskCompletionSource? Gate { get; init; }

        public string? FailQuery { get; init; }

        public int MaxConcurrent => Volatile.Read(ref maxConcurrent);

        public IReadOnlyList<string> Issued
        {
            get
            {
                lock (sync)
                {
                    return [.. issued];
                }
            }
        }

        public async Task<Result<IReadOnlyList<PackItem>>> SearchAsync(string query, int topK, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                issued.Add(query);
            }

            var now = Interlocked.Increment(ref running);
            int seen;
            do
            {
                seen = Volatile.Read(ref maxConcurrent);
            }
            while (now > seen && Interlocked.CompareExchange(ref maxConcurrent, now, seen) != seen);

            try
            {
                if (Gate is not null)
                {
                    await Gate.Task.ConfigureAwait(false);
                }

                if (query == FailQuery)
                {
                    return Result<IReadOnlyList<PackItem>>.Failure("no index");
                }

                IReadOnlyList<PackItem> notes =
                [
                    new PackItem($"market:{query}", PackCorpus.Market, $"Note {query}", null, null, null, "A market note.", null, null, null, "rag", [], null),
                ];
                return Result<IReadOnlyList<PackItem>>.Success(notes);
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        }
    }

    /// <summary>Answers the researcher with fixed queries, the analysts with fixed findings and the
    /// strategist with the next scripted plays (the last one repeats), recording the strategist's prompts.</summary>
    private sealed class ScriptedGateway(string firstPayload, IReadOnlyList<string> strategistPlays) : IAiGateway
    {
        private static readonly AiCallMetadata Metadata = new("scripted", "v1", CouncilAgents.Version, DateTimeOffset.UnixEpoch, "00");
        private readonly object sync = new();

        public int StrategistCalls { get; private set; }

        public List<string> StrategistPrompts { get; } = [];

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

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            string payload;
            lock (sync)
            {
                if (request.AgentName == CouncilAgents.LeverStrategistName)
                {
                    payload = strategistPlays[Math.Min(StrategistCalls, strategistPlays.Count - 1)];
                    StrategistCalls++;
                    StrategistPrompts.Add(request.SystemPrompt);
                }
                else
                {
                    payload = firstPayload;
                }
            }

            return Task.FromResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(payload, Metadata)));
        }
    }
}
