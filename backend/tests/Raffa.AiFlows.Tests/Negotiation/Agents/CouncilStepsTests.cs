using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Negotiation.Orchestration;
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
/// Plan A-05a: the council's three agents run as <see cref="IAgentStep{TIn,TOut}"/>s through
/// <see cref="StepRunner"/>. Pins the definitions (names, version, prompts, schemas, policies), proves
/// the requests that reach the gateway are byte-identical to what the pre-runner council built by
/// hand (a frozen copy of the old wire shapes is the reference), that a failed step lands in the same
/// failure line / fallback as before, and that every audit row carries the run and the step.
/// </summary>
public sealed class CouncilStepsTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(10);
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

    private static FixtureAiGateway Fixture() =>
        new(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());

    private static AiAnalysisResult Payload(string json) =>
        new(json, new AiCallMetadata("m", "v", CouncilAgents.Version, DateTimeOffset.UnixEpoch, "h"));

    // ---------------------------------------------------------------- the definitions

    [Fact]
    public void The_definitions_pin_the_names_version_prompts_schemas_and_failure_policies_of_the_council()
    {
        var steps = new IAgentStep<CouncilAnalystInput, CouncilFindingsPayload>[] { new ContractAnalystStep(Deadline), new MarketAnalystStep(Deadline) };
        var strategist = new LeverStrategistStep(Deadline);

        Assert.Equal("contract-analyst", steps[0].Definition.Name);
        Assert.Equal("market-analyst", steps[1].Definition.Name);
        Assert.Equal("lever-strategist", strategist.Definition.Name);
        Assert.All(new[] { steps[0].Definition, steps[1].Definition, strategist.Definition }, d =>
        {
            Assert.Equal("council-v3", d.Version);
            Assert.Equal(AgentRole.Analyze, d.Role);
            Assert.Null(d.LanguageDirective);
            Assert.Equal(d.Prompt, d.SystemPrompt);
            Assert.Equal(Deadline, d.Deadline);
            Assert.Empty(d.Validate());
        });

        Assert.Equal(CouncilAgents.ContractAnalystPrompt, steps[0].Definition.SystemPrompt);
        Assert.Equal(CouncilAgents.MarketAnalystPrompt, steps[1].Definition.SystemPrompt);
        Assert.Equal(CouncilAgents.LeverStrategistPrompt, strategist.Definition.SystemPrompt);
        Assert.Equal(CouncilAgents.FindingsSchema, steps[0].Definition.JsonSchema);
        Assert.Equal(CouncilAgents.FindingsSchema, steps[1].Definition.JsonSchema);
        Assert.Equal(CouncilAgents.PlaysSchema, strategist.Definition.JsonSchema);

        Assert.Equal(AgentFailurePolicy.Skip, steps[0].Definition.FailurePolicy);
        Assert.Equal(AgentFailurePolicy.Skip, steps[1].Definition.FailurePolicy);
        Assert.Equal(AgentFailurePolicy.Fail, strategist.Definition.FailurePolicy);
        Assert.Null(steps[0].Verifier);
        Assert.Null(((IAgentStep<CouncilStrategistInput, CouncilPlaysPayload>)strategist).Verifier);
    }

    [Fact]
    public void The_council_has_a_backstop_deadline_by_default()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), new CouncilOptions().AgentDeadline);
    }

    // ---------------------------------------------------------------- request byte-equality

    [Fact]
    public async Task The_requests_reaching_the_gateway_are_byte_identical_to_the_pre_runner_councils_and_in_the_same_order()
    {
        var gateway = new ScriptedGateway(Fixture());

        await new NegotiationCouncil(gateway, new CouncilOptions()).RunAsync("quali leve per risparmiare 20k?", Pack(), Goal);

        var pack = Pack();
        var tenant = pack.Where(i => i.Corpus == PackCorpus.Tenant).ToList();
        var market = pack.Where(i => i.Corpus == PackCorpus.Market || i.CitationKey.StartsWith("calc:lever", StringComparison.Ordinal)).ToList();
        var strategistItems = pack.Where(i => i.Corpus == PackCorpus.Calc || i.CitationKey.StartsWith("raffa:playbook:", StringComparison.Ordinal)).ToList();

        var expectedContract = LegacyRequest(CouncilAgents.ContractAnalystName, CouncilAgents.ContractAnalystPrompt, CouncilAgents.FindingsSchema,
            new LegacyAnalystInput("quali leve per risparmiare 20k?", LegacyGoal(Goal), tenant.Select(LegacyItem).ToList()));
        var expectedMarket = LegacyRequest(CouncilAgents.MarketAnalystName, CouncilAgents.MarketAnalystPrompt, CouncilAgents.FindingsSchema,
            new LegacyAnalystInput("quali leve per risparmiare 20k?", LegacyGoal(Goal), market.Select(LegacyItem).ToList()));

        // The two analysts start together (contract first); the strategist is strictly last.
        Assert.Equal(3, gateway.Requests.Count);
        Assert.Equal(expectedContract, gateway.Requests[0]);
        Assert.Equal(expectedMarket, gateway.Requests[1]);

        var strategist = gateway.Requests[2];
        Assert.Equal(CouncilAgents.LeverStrategistName, strategist.AgentName);
        Assert.Equal(CouncilAgents.LeverStrategistPrompt, strategist.SystemPrompt);
        Assert.Equal(CouncilAgents.PlaysSchema, strategist.JsonSchema);
        Assert.Equal(CouncilAgents.Version, strategist.PromptVersion);

        // Rebuild the strategist's input the old way from what the analysts answered.
        var contractFindings = Findings(gateway.Responses[CouncilAgents.ContractAnalystName]);
        var marketFindings = Findings(gateway.Responses[CouncilAgents.MarketAnalystName]);
        var packKeys = pack.Select(i => i.CitationKey).ToHashSet(StringComparer.Ordinal);
        var expectedStrategistInput = new LegacyStrategistInput(
            "quali leve per risparmiare 20k?", LegacyGoal(Goal), strategistItems.Select(LegacyItem).ToList(),
            new LegacyFindingsByAgent(Filter(contractFindings, packKeys), Filter(marketFindings, packKeys)));
        Assert.Equal(JsonSerializer.Serialize(expectedStrategistInput, LegacyJson), strategist.InputJson);
    }

    // ---------------------------------------------------------------- failure -> the same fallback

    [Fact]
    public async Task A_gateway_failure_of_an_analyst_is_the_same_failure_line_and_the_strategist_still_runs()
    {
        var gateway = new ScriptedGateway(Fixture())
        {
            Intercept = request => request.AgentName == CouncilAgents.ContractAnalystName
                ? Result<AiAnalysisResult>.Failure("contract analyst is down")
                : null,
        };

        var outcome = await new NegotiationCouncil(gateway, new CouncilOptions()).RunAsync("q", Pack(), Goal);

        Assert.Equal(["contract-analyst: contract analyst is down"], outcome.Failures);
        Assert.Equal(["contract-analyst", "market-analyst", "lever-strategist"], outcome.AgentsRun);
        Assert.NotEmpty(outcome.Items);
        Assert.Contains("\"contractAnalyst\":[]", gateway.Requests.Single(r => r.AgentName == CouncilAgents.LeverStrategistName).InputJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_analyst_payload_that_is_not_json_is_the_same_failure_line_as_before()
    {
        var gateway = new ScriptedGateway(Fixture())
        {
            Intercept = request => request.AgentName == CouncilAgents.MarketAnalystName
                ? Result<AiAnalysisResult>.Success(Payload("not json"))
                : null,
        };

        var outcome = await new NegotiationCouncil(gateway, new CouncilOptions()).RunAsync("q", Pack(), Goal);

        var failure = Assert.Single(outcome.Failures);
        Assert.StartsWith("market-analyst: payload was not valid JSON — ", failure, StringComparison.Ordinal);
        Assert.NotEmpty(outcome.Items);
    }

    [Theory]
    [InlineData("gateway", "lever-strategist: strategist is down")]
    [InlineData("null", "lever-strategist: payload parsed to null.")]
    public async Task A_failing_strategist_yields_no_items_and_the_same_failure_line_as_before(string kind, string expected)
    {
        var gateway = new ScriptedGateway(Fixture())
        {
            Intercept = request => request.AgentName == CouncilAgents.LeverStrategistName
                ? kind == "gateway" ? Result<AiAnalysisResult>.Failure("strategist is down") : Result<AiAnalysisResult>.Success(Payload("null"))
                : null,
        };

        var outcome = await new NegotiationCouncil(gateway, new CouncilOptions()).RunAsync("q", Pack(), Goal);

        Assert.Empty(outcome.Items);
        Assert.Equal([expected], outcome.Failures);
        Assert.Equal(CouncilAgents.LeverStrategistName, outcome.AgentsRun[^1]);
    }

    [Fact]
    public async Task A_missed_deadline_degrades_each_agent_by_its_policy_with_an_explicit_reason()
    {
        var hung = new ScriptedGateway(Fixture())
        {
            InterceptAsync = async (request, token) =>
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                return null;
            },
        };
        var options = new CouncilOptions { AgentDeadline = TimeSpan.FromMilliseconds(150) };

        var outcome = await new NegotiationCouncil(hung, options).RunAsync("q", Pack(), Goal);

        // Both analysts skipped (named in the failures), then the strategist failed: no council.
        Assert.Empty(outcome.Items);
        Assert.Equal(3, outcome.Failures.Count);
        Assert.All(outcome.Failures, f => Assert.Contains("deadline", f, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- audit

    [Fact]
    public async Task Every_audit_row_of_the_council_carries_a_run_and_its_own_step_and_no_text()
    {
        var audit = new RecordingAuditWriter();
        var tenant = new TenantContext();
        var gateway = new LoggingAiGateway(Fixture(), audit, tenant, new AiGatewayComplianceOptions());

        using (tenant.BeginScope(TenantId.New()))
        {
            await new NegotiationCouncil(gateway, new CouncilOptions()).RunAsync("SECRET-QUESTION", Pack(), Goal);
        }

        Assert.Equal(3, audit.Written.Count);
        Assert.All(audit.Written, e => Assert.Equal("ai.analyzed", e.Action));
        var runIds = audit.Written.Select(e => Regex.Match(e.Detail!, @"\brun=(\S+)").Groups[1].Value).Distinct().ToList();
        Assert.NotEqual("none", Assert.Single(runIds));
        foreach (var name in new[] { CouncilAgents.ContractAnalystName, CouncilAgents.MarketAnalystName, CouncilAgents.LeverStrategistName })
        {
            Assert.Contains(audit.Written, e => e.Detail!.Contains($"agent={name} ", StringComparison.Ordinal) &&
                e.Detail!.Contains($"step={name} ", StringComparison.Ordinal));
        }

        Assert.All(audit.Written, e => Assert.DoesNotContain("SECRET", e.Detail!, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- the frozen pre-runner wire shapes

    private static readonly JsonSerializerOptions LegacyJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static AiAnalysisRequest LegacyRequest(string name, string prompt, string schema, LegacyAnalystInput input) =>
        new(name, prompt, JsonSerializer.Serialize(input, LegacyJson), schema, CouncilAgents.Version);

    private static LegacyGoalDto? LegacyGoal(SavingsGoal? goal) =>
        goal is null || !goal.HasTarget
            ? null
            : new LegacyGoalDto(goal.TargetAmount, goal.TargetPercent, goal.Currency, goal.WindowDays, goal.WindowLabel);

    private static LegacyItemDto LegacyItem(PackItem item) =>
        new(item.CitationKey, item.Corpus, item.Title, item.Subtitle, item.Snippet,
            item.Values.Select(v => new LegacyValueDto(v.Key, v.Value, v.Kind.ToString(), v.Currency)).ToList());

    private static IReadOnlyList<LegacyFinding> Findings(string payloadJson) =>
        JsonSerializer.Deserialize<LegacyFindingsPayload>(payloadJson, LegacyJson)?.Findings ?? [];

    private static IReadOnlyList<LegacyFinding> Filter(IReadOnlyList<LegacyFinding> findings, HashSet<string> packKeys) =>
        findings
            .Where(f => !string.IsNullOrWhiteSpace(f.Title) && !string.IsNullOrWhiteSpace(f.Insight))
            .Select(f => f with { CitationKeys = (f.CitationKeys ?? []).Where(packKeys.Contains).Distinct(StringComparer.Ordinal).ToList() })
            .Where(f => f.CitationKeys!.Count > 0)
            .Take(5)
            .ToList();

    private sealed record LegacyGoalDto(decimal? TargetAmount, decimal? TargetPercent, string? Currency, int? WindowDays, string? WindowLabel);

    private sealed record LegacyValueDto(string Key, string Value, string Kind, string? Currency);

    private sealed record LegacyItemDto(string CitationKey, string Corpus, string Title, string? Subtitle, string Snippet, IReadOnlyList<LegacyValueDto> Values);

    private sealed record LegacyAnalystInput(string Question, LegacyGoalDto? Goal, IReadOnlyList<LegacyItemDto> Items);

    private sealed record LegacyFindingsByAgent(IReadOnlyList<LegacyFinding> ContractAnalyst, IReadOnlyList<LegacyFinding> MarketAnalyst);

    private sealed record LegacyStrategistInput(string Question, LegacyGoalDto? Goal, IReadOnlyList<LegacyItemDto> Items, LegacyFindingsByAgent Findings);

    private sealed record LegacyFindingsPayload(IReadOnlyList<LegacyFinding>? Findings);

    private sealed record LegacyFinding(string Title, string Insight, string? LeverType, IReadOnlyList<string>? CitationKeys);

    // ---------------------------------------------------------------- doubles

    /// <summary>Records every analyst request and its response (in call order, thread-safe) and lets a
    /// test answer some of them itself: the interceptor returns <see langword="null"/> to fall through.</summary>
    private sealed class ScriptedGateway(IAiGateway inner) : IAiGateway
    {
        private readonly object _lock = new();

        public List<AiAnalysisRequest> Requests { get; } = [];

        public Dictionary<string, string> Responses { get; } = [];

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

            var result = Intercept?.Invoke(request)
                ?? (InterceptAsync is null ? null : await InterceptAsync(request, cancellationToken).ConfigureAwait(false))
                ?? await inner.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                lock (_lock)
                {
                    Responses[request.AgentName] = result.Value.PayloadJson;
                }
            }

            return result;
        }
    }
}
