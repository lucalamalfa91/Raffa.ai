using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Negotiation.Tools;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Logging;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Tests.Negotiation.Agents;

/// <summary>
/// Plan A-05 (F1 Ask split PR-7): the market researcher runs as an <see cref="IAgentStep{TIn,TOut}"/>
/// through <see cref="StepRunner"/>, mirroring <c>CouncilStepsTests</c>. Pins the definition, proves the
/// request reaching the gateway is byte-identical to what the pre-runner researcher built by hand (a
/// frozen copy of the old wire shapes is the reference), that failures land in the same failure line,
/// and that the audit rows carry the run and the step.
/// </summary>
public sealed class MarketResearcherStepTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(10);
    private static readonly SavingsGoal Goal = new(20000m, null, "EUR", null, null);

    private static PackItem Item(string key, string corpus, string title, string snippet, params PackValue[] values) =>
        new(key, corpus, title, null, null, null, snippet, "/contracts/x", null, null, "test", values, "contract-x");

    private static IReadOnlyList<PackItem> Pack() =>
    [
        Item("fact:x:renewal", PackCorpus.Tenant, "ServiceNow · OrderForm", "ServiceNow ends on 2028-04-10.",
            new PackValue("annualSpend", "230000", PackValueKind.Amount, "EUR")),
        Item("calc:lever[market-discount]", PackCorpus.Calc, "ServiceNow — Market discount", "Peers achieved a 9% discount.",
            new PackValue("percent", "9", PackValueKind.Percentage)),
        Item("raffa:playbook:anchor-on-market", PackCorpus.Raffa, "Anchor", "Open with the market median."),
    ];

    private static FixtureAiGateway Fixture() =>
        new(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());

    private static AiAnalysisResult Payload(string json) =>
        new(json, new AiCallMetadata("m", "v", CouncilAgents.Version, DateTimeOffset.UnixEpoch, "h"));

    private static readonly StubRag Rag = new();

    // ---------------------------------------------------------------- the definition

    [Fact]
    public void The_definition_pins_the_name_version_prompt_schema_and_failure_policy_of_the_researcher()
    {
        var step = new MarketResearcherStep(Deadline);
        var d = step.Definition;

        Assert.Equal("market-researcher", d.Name);
        Assert.Equal("council-v3", d.Version);
        Assert.Equal(AgentRole.Analyze, d.Role);
        Assert.Null(d.LanguageDirective);
        Assert.Equal(CouncilAgents.MarketResearcherPrompt, d.SystemPrompt);
        Assert.Equal(CouncilAgents.QueriesSchema, d.JsonSchema);
        Assert.Equal(AgentFailurePolicy.Skip, d.FailurePolicy);
        Assert.Equal(Deadline, d.Deadline);
        Assert.Null(((IAgentStep<ResearcherStepInput, ResearcherQueriesPayload>)step).Verifier);
        Assert.Equal(AgentDefinition.HashPrompt(CouncilAgents.MarketResearcherPrompt), d.RegisteredPromptHash);
        Assert.Empty(d.Validate());
    }

    [Fact]
    public void The_researcher_uses_the_councils_backstop_deadline_by_default()
    {
        Assert.Equal(new CouncilOptions().AgentDeadline, new MarketResearcherStep(new CouncilOptions().AgentDeadline).Definition.Deadline);
    }

    // ---------------------------------------------------------------- request byte-equality

    [Fact]
    public async Task The_request_reaching_the_gateway_is_byte_identical_to_the_pre_runner_researchers()
    {
        var gateway = new CapturingGateway(Fixture());
        var options = new CouncilOptions();
        var pack = Pack();
        var missing = new[] { "price", "noticeDays" };

        await new MarketResearcher(gateway, options, Rag).QueryMarketRagAsync("quali leve per risparmiare 20k?", Goal, pack, missing);

        var request = Assert.Single(gateway.Requests);
        var legacyInput = new LegacyResearcherInput(
            "quali leve per risparmiare 20k?",
            new LegacyGoalDto(Goal.TargetAmount, Goal.TargetPercent, Goal.Currency, Goal.WindowDays, Goal.WindowLabel),
            missing,
            pack.Where(i => i.Corpus != PackCorpus.Raffa).Take(options.MaxItemsPerAgent).Select(LegacyItem).ToList());
        var expected = new AiAnalysisRequest(
            CouncilAgents.MarketResearcherName, CouncilAgents.MarketResearcherPrompt,
            JsonSerializer.Serialize(legacyInput, LegacyJson), CouncilAgents.QueriesSchema, CouncilAgents.Version);

        Assert.Equal(expected, request);
    }

    [Fact]
    public async Task A_turn_without_a_goal_sends_a_null_goal_exactly_as_before()
    {
        var gateway = new CapturingGateway(Fixture());

        await new MarketResearcher(gateway, new CouncilOptions(), Rag).QueryMarketRagAsync("q", null, Pack(), []);

        var request = Assert.Single(gateway.Requests);
        var legacy = new LegacyResearcherInput("q", null, [], Pack().Where(i => i.Corpus != PackCorpus.Raffa).Select(LegacyItem).ToList());
        Assert.Equal(JsonSerializer.Serialize(legacy, LegacyJson), request.InputJson);
    }

    [Fact]
    public async Task The_researcher_is_skipped_without_a_call_when_there_is_no_rag_or_it_is_off()
    {
        var gateway = new CapturingGateway(Fixture());

        var noRag = await new MarketResearcher(gateway, new CouncilOptions()).QueryMarketRagAsync("q", Goal, Pack(), []);
        var off = await new MarketResearcher(gateway, new CouncilOptions { MarketResearchEnabled = false }, Rag)
            .QueryMarketRagAsync("q", Goal, Pack(), []);

        Assert.Same(MarketResearchOutcome.Skipped, noRag);
        Assert.Same(MarketResearchOutcome.Skipped, off);
        Assert.Empty(gateway.Requests);
    }

    // ---------------------------------------------------------------- failure -> the same outcome

    [Fact]
    public async Task A_gateway_failure_is_a_run_with_the_gateway_error_as_the_failure()
    {
        var gateway = new CapturingGateway(Fixture())
        {
            Intercept = _ => Result<AiAnalysisResult>.Failure("researcher is down"),
        };

        var outcome = await new MarketResearcher(gateway, new CouncilOptions(), Rag).QueryMarketRagAsync("q", Goal, Pack(), []);

        Assert.True(outcome.Ran);
        Assert.Empty(outcome.Items);
        Assert.Empty(outcome.Queries);
        Assert.Equal("researcher is down", outcome.Failure);
    }

    [Fact]
    public async Task A_payload_that_is_not_json_is_a_run_with_the_same_failure_line_as_before()
    {
        var gateway = new CapturingGateway(Fixture())
        {
            Intercept = _ => Result<AiAnalysisResult>.Success(Payload("not json")),
        };

        var outcome = await new MarketResearcher(gateway, new CouncilOptions(), Rag).QueryMarketRagAsync("q", Goal, Pack(), []);

        Assert.True(outcome.Ran);
        Assert.Empty(outcome.Items);
        Assert.StartsWith("payload was not valid JSON — ", outcome.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_literal_null_payload_is_now_an_explicit_failure_instead_of_a_silent_empty_result()
    {
        // The one accepted difference from the hand-built call (equal or better): the hand-built
        // researcher read `null` as "no queries" without saying so; the runner reports it.
        var gateway = new CapturingGateway(Fixture())
        {
            Intercept = _ => Result<AiAnalysisResult>.Success(Payload("null")),
        };

        var outcome = await new MarketResearcher(gateway, new CouncilOptions(), Rag).QueryMarketRagAsync("q", Goal, Pack(), []);

        Assert.True(outcome.Ran);
        Assert.Empty(outcome.Items);
        Assert.Equal("payload parsed to null.", outcome.Failure);
    }

    [Fact]
    public async Task Queries_are_run_against_the_rag_and_the_notes_come_back_labelled()
    {
        var gateway = new CapturingGateway(Fixture())
        {
            Intercept = _ => Result<AiAnalysisResult>.Success(Payload(
                """{"queries":[{"query":"ServiceNow ITSM discount","scope":"same-supplier"},{"query":"  similar   CRM deals ","scope":"similar-contracts"},{"query":""}]}""")),
        };

        var outcome = await new MarketResearcher(gateway, new CouncilOptions(), Rag).QueryMarketRagAsync("q", Goal, Pack(), []);

        Assert.True(outcome.Ran);
        Assert.Null(outcome.Failure);
        Assert.Equal(["ServiceNow ITSM discount", "similar CRM deals"], outcome.Queries);
        Assert.Equal(2, outcome.Items.Count);
        Assert.Contains(outcome.Items, i => i.Subtitle?.StartsWith("similar contract", StringComparison.Ordinal) == true);
        Assert.All(outcome.Items, i => Assert.Contains("market-researcher", i.Provenance, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_researcher_slower_than_its_deadline_is_skipped_with_an_explicit_reason()
    {
        var hung = new CapturingGateway(Fixture())
        {
            InterceptAsync = async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                return null;
            },
        };
        var options = new CouncilOptions { AgentDeadline = TimeSpan.FromMilliseconds(150) };

        var outcome = await new MarketResearcher(hung, options, Rag).QueryMarketRagAsync("q", Goal, Pack(), []);

        Assert.True(outcome.Ran);
        Assert.Empty(outcome.Items);
        Assert.Contains("deadline", outcome.Failure, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- audit

    [Fact]
    public async Task The_researchers_audit_row_carries_a_run_and_its_own_step_and_no_text()
    {
        var audit = new RecordingAuditWriter();
        var tenant = new TenantContext();
        var gateway = new LoggingAiGateway(Fixture(), audit, tenant, new AiGatewayComplianceOptions());

        using (tenant.BeginScope(TenantId.New()))
        {
            await new MarketResearcher(gateway, new CouncilOptions(), Rag).QueryMarketRagAsync("SECRET-QUESTION", Goal, Pack(), []);
        }

        var row = Assert.Single(audit.Written);
        Assert.Equal("ai.analyzed", row.Action);
        Assert.NotEqual("none", Regex.Match(row.Detail!, @"\brun=(\S+)").Groups[1].Value);
        Assert.Contains("agent=market-researcher ", row.Detail!, StringComparison.Ordinal);
        Assert.Contains("step=market-researcher ", row.Detail!, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", row.Detail!, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the frozen pre-runner wire shapes

    private static readonly JsonSerializerOptions LegacyJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static LegacyItemDto LegacyItem(PackItem item) =>
        new(item.CitationKey, item.Corpus, item.Title, item.Subtitle, item.Snippet,
            item.Values.Select(v => new LegacyValueDto(v.Key, v.Value, v.Kind.ToString(), v.Currency)).ToList());

    private sealed record LegacyGoalDto(decimal? TargetAmount, decimal? TargetPercent, string? Currency, int? WindowDays, string? WindowLabel);

    private sealed record LegacyValueDto(string Key, string Value, string Kind, string? Currency);

    private sealed record LegacyItemDto(string CitationKey, string Corpus, string Title, string? Subtitle, string Snippet, IReadOnlyList<LegacyValueDto> Values);

    private sealed record LegacyResearcherInput(string Question, LegacyGoalDto? Goal, IReadOnlyList<string> MissingFields, IReadOnlyList<LegacyItemDto> Items);

    // ---------------------------------------------------------------- doubles

    private sealed class StubRag : IMarketRagSearch
    {
        public Task<Result<IReadOnlyList<PackItem>>> SearchAsync(string query, int topK, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PackItem> notes =
            [
                new PackItem(
                    $"market:{query.Replace(' ', '-')}", PackCorpus.Market, $"Note for {query}", null, null, null,
                    "A market note.", null, null, null, "rag", [], null),
            ];
            return Task.FromResult(Result<IReadOnlyList<PackItem>>.Success(notes));
        }
    }

    private sealed class CapturingGateway(IAiGateway inner) : IAiGateway
    {
        private readonly object _lock = new();

        public List<AiAnalysisRequest> Requests { get; } = [];

        public Func<AiAnalysisRequest, Result<AiAnalysisResult>?>? Intercept { get; init; }

        public Func<AiAnalysisRequest, CancellationToken, Task<Result<AiAnalysisResult>?>>? InterceptAsync { get; init; }

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
            lock (_lock)
            {
                Requests.Add(request);
            }

            return Intercept?.Invoke(request)
                ?? (InterceptAsync is null ? null : await InterceptAsync(request, cancellationToken).ConfigureAwait(false))
                ?? await inner.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
