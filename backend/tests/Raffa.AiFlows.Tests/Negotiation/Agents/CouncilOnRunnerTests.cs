using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiGateway;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Logging;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Tests.Negotiation.Agents;

/// <summary>
/// Plan A-01, worked example: a pretend council whose every model call is a <see cref="StepRunner"/>
/// step, run against <see cref="FixtureAiGateway"/>. It proves what the pilot migration (A-05a) will
/// rely on, without migrating anything: the runner sends the council's own prompts, version and
/// <c>InputJson</c> unchanged, the schemas derived from the output types are the council's schemas,
/// round one tolerates a failed analyst the way <see cref="NegotiationCouncil"/> does, a missed
/// deadline degrades explicitly, every audit row carries the run and the step, and 200 repetitions
/// through the logging gateway never overlap a write on the (non-thread-safe) audit context.
/// </summary>
public sealed class CouncilOnRunnerTests
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

    private static FixtureAiGateway Fixture() =>
        new(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());

    private static bool SameJson(string a, string b) => JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b));

    // ---------------------------------------------------------------- the definitions

    [Fact]
    public void Every_definition_has_a_strict_schema_derived_from_its_type_and_the_councils_own_prompt_and_version()
    {
        var definitions = ExampleCouncilDefinitions.All();

        Assert.Equal(
            [CouncilAgents.ContractAnalystName, CouncilAgents.MarketAnalystName, CouncilAgents.LeverStrategistName],
            definitions.Select(d => d.Name));
        Assert.All(definitions, d =>
        {
            Assert.Empty(d.Validate());
            Assert.Equal(CouncilAgents.Version, d.Version);
        });
        Assert.Equal(CouncilAgents.ContractAnalystPrompt, definitions[0].Prompt);
        Assert.Equal(CouncilAgents.MarketAnalystPrompt, definitions[1].Prompt);
        Assert.Equal(CouncilAgents.LeverStrategistPrompt, definitions[2].Prompt);
    }

    [Fact]
    public void The_schemas_derived_from_the_output_types_are_the_councils_hand_written_schemas()
    {
        Assert.True(SameJson(AgentSchema.For<FindingsOutput>(), CouncilAgents.FindingsSchema));
        Assert.True(SameJson(AgentSchema.For<PlaysOutput>(), CouncilAgents.PlaysSchema));
    }

    // ---------------------------------------------------------------- the run

    [Fact]
    public async Task The_council_runs_on_the_runner_over_the_fixture_gateway_and_returns_grounded_plays()
    {
        var recording = new RecordingGateway(Fixture());

        var outcome = await new CouncilOnRunner(new StepRunner(recording)).RunAsync("quali leve per risparmiare 20k?", Pack(), Goal);

        Assert.Equal(
            [CouncilAgents.ContractAnalystName, CouncilAgents.MarketAnalystName, CouncilAgents.LeverStrategistName],
            outcome.AgentsRun);
        Assert.Empty(outcome.Failures);
        Assert.NotEmpty(outcome.Plays);
        Assert.All(outcome.Plays, p => Assert.Contains(p.CitationKeys, k => Pack().Any(i => i.CitationKey == k)));
        // The strategist ran last, after both analysts.
        Assert.Equal(CouncilAgents.LeverStrategistName, recording.Requests[^1].AgentName);
        Assert.Contains("\"contractAnalyst\"", recording.Requests[^1].InputJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_runner_sends_exactly_the_requests_the_real_council_sends()
    {
        var real = new RecordingGateway(Fixture());
        var onRunner = new RecordingGateway(Fixture());

        await new NegotiationCouncil(real, new CouncilOptions()).RunAsync("q", Pack(), Goal);
        await new CouncilOnRunner(new StepRunner(onRunner)).RunAsync("q", Pack(), Goal);

        Assert.Equal(real.Requests.Count, onRunner.Requests.Count);
        foreach (var name in new[] { CouncilAgents.ContractAnalystName, CouncilAgents.MarketAnalystName, CouncilAgents.LeverStrategistName })
        {
            var expected = real.Requests.Single(r => r.AgentName == name);
            var actual = onRunner.Requests.Single(r => r.AgentName == name);

            Assert.Equal(expected.SystemPrompt, actual.SystemPrompt);
            Assert.Equal(expected.PromptVersion, actual.PromptVersion);
            // Payload identical byte for byte (a fortiori up to key order, plan section 7 metric 1)...
            Assert.Equal(expected.InputJson, actual.InputJson);
            // ...and the schema the model is held to is the same up to whitespace.
            Assert.True(SameJson(expected.JsonSchema, actual.JsonSchema), $"{name}: the derived schema differs from the council's");
        }
    }

    [Fact]
    public async Task Every_audit_row_of_the_run_has_the_same_run_id_and_its_own_step_name_and_no_text()
    {
        var audit = new RecordingAuditWriter();
        var tenant = new TenantContext();
        var gateway = new LoggingAiGateway(Fixture(), audit, tenant, new AiGatewayComplianceOptions());

        using (tenant.BeginScope(TenantId.New()))
        {
            await new CouncilOnRunner(new StepRunner(gateway)).RunAsync("SECRET-QUESTION", Pack(), Goal);
        }

        Assert.Equal(3, audit.Written.Count);
        Assert.All(audit.Written, e => Assert.Equal("ai.analyzed", e.Action));
        var runIds = audit.Written.Select(e => Regex.Match(e.Detail!, @"\brun=(\S+)").Groups[1].Value).Distinct().ToList();
        Assert.NotEqual("none", Assert.Single(runIds));
        foreach (var name in new[] { CouncilAgents.ContractAnalystName, CouncilAgents.MarketAnalystName, CouncilAgents.LeverStrategistName })
        {
            Assert.Contains(audit.Written, e => e.Detail!.Contains($"agent={name} ", StringComparison.Ordinal) &&
                e.Detail!.Contains($"step={name} ", StringComparison.Ordinal) &&
                e.Detail!.Contains("noTraining=True", StringComparison.Ordinal));
        }

        Assert.All(audit.Written, e => Assert.DoesNotContain("SECRET", e.Detail!, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- partial failures, deadline, verifier

    [Fact]
    public async Task A_failing_analyst_degrades_the_council_and_the_strategist_still_runs()
    {
        var gateway = new InterceptingGateway(Fixture(), (request, _) =>
            request.AgentName == CouncilAgents.MarketAnalystName
                ? Task.FromResult(Result<AiAnalysisResult>.Failure("market analyst is down"))
                : null);

        var outcome = await new CouncilOnRunner(new StepRunner(gateway)).RunAsync("q", Pack(), Goal);

        Assert.Equal([$"{CouncilAgents.MarketAnalystName}: market analyst is down"], outcome.Failures);
        Assert.Contains(CouncilAgents.LeverStrategistName, outcome.AgentsRun);
        Assert.NotEmpty(outcome.Plays);
        // The strategist read an empty market list instead of failing.
        Assert.Contains("\"marketAnalyst\":[]", gateway.Requests.Single(r => r.AgentName == CouncilAgents.LeverStrategistName).InputJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failing_strategist_yields_no_plays_and_names_the_failure()
    {
        var gateway = new InterceptingGateway(Fixture(), (request, _) =>
            request.AgentName == CouncilAgents.LeverStrategistName
                ? Task.FromResult(Result<AiAnalysisResult>.Failure("strategist is down"))
                : null);

        var outcome = await new CouncilOnRunner(new StepRunner(gateway)).RunAsync("q", Pack(), Goal);

        Assert.Empty(outcome.Plays);
        Assert.Equal([$"{CouncilAgents.LeverStrategistName}: strategist is down"], outcome.Failures);
    }

    [Fact]
    public async Task A_slow_analyst_is_skipped_at_its_deadline_and_the_council_goes_on_without_it()
    {
        static async Task<Result<AiAnalysisResult>> Hang(CancellationToken token)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return Result<AiAnalysisResult>.Failure("unreachable");
        }

        var gateway = new InterceptingGateway(Fixture(), (request, token) =>
            request.AgentName == CouncilAgents.MarketAnalystName ? Hang(token) : null);

        var outcome = await new CouncilOnRunner(new StepRunner(gateway), analystDeadline: TimeSpan.FromMilliseconds(400))
            .RunAsync("q", Pack(), Goal);

        var failure = Assert.Single(outcome.Failures);
        Assert.StartsWith($"{CouncilAgents.MarketAnalystName}: deadline", failure, StringComparison.Ordinal);
        Assert.NotEmpty(outcome.Plays);
    }

    [Fact]
    public async Task An_analyst_that_cites_a_key_outside_the_pack_is_sent_back_once_with_the_violation_named()
    {
        var calls = 0;
        var gateway = new InterceptingGateway(Fixture(), (request, _) =>
        {
            if (request.AgentName != CouncilAgents.ContractAnalystName || Interlocked.Increment(ref calls) > 1)
            {
                return null;
            }

            const string hallucinated =
                """{"findings":[{"title":"Ghost","insight":"A ghost clause.","leverType":null,"citationKeys":["ghost:key"]}]}""";
            return Task.FromResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(
                hallucinated,
                new AiCallMetadata("m", "v", request.PromptVersion, DateTimeOffset.UtcNow, "h"))));
        });

        var outcome = await new CouncilOnRunner(new StepRunner(gateway)).RunAsync("q", Pack(), Goal);

        var contractCalls = gateway.Requests.Where(r => r.AgentName == CouncilAgents.ContractAnalystName).ToList();
        Assert.Equal(2, contractCalls.Count);
        Assert.Contains("ghost:key", contractCalls[1].SystemPrompt, StringComparison.Ordinal);
        Assert.StartsWith(CouncilAgents.ContractAnalystPrompt, contractCalls[1].SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(contractCalls[0].InputJson, contractCalls[1].InputJson);
        Assert.Empty(outcome.Failures);
        Assert.NotEmpty(outcome.Plays);
    }

    // ---------------------------------------------------------------- fan-out under the audit context

    /// <summary>The audit write behind <see cref="LoggingAiGateway"/> is a Scoped EF context in
    /// production; this double throws when two writes overlap, as a DbContext does.</summary>
    private sealed class NonThreadSafeAuditWriter : IAuditWriter
    {
        private int _active;

        public int Written;

        public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _active) != 1)
            {
                Interlocked.Decrement(ref _active);
                throw new InvalidOperationException(
                    "A second operation was started on this context instance before a previous operation completed.");
            }

            try
            {
                await Task.Yield();
                await Task.Delay(1, cancellationToken);
                Interlocked.Increment(ref Written);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    [Fact]
    public async Task Two_hundred_council_runs_never_overlap_a_write_on_the_audit_context()
    {
        var writer = new NonThreadSafeAuditWriter();
        var tenant = new TenantContext();
        var fixture = Fixture();

        for (var i = 0; i < 200; i++)
        {
            // A fresh gateway and runner per repetition, as a DI scope gives each turn.
            var runner = new StepRunner(new LoggingAiGateway(fixture, writer, tenant, new AiGatewayComplianceOptions()));

            using (tenant.BeginScope(TenantId.New()))
            {
                var outcome = await new CouncilOnRunner(runner).RunAsync($"question {i}", Pack(), Goal);

                Assert.Empty(outcome.Failures);
                Assert.NotEmpty(outcome.Plays);
            }
        }

        Assert.Equal(200 * 3, writer.Written);
    }

    // ---------------------------------------------------------------- doubles

    private class RecordingGateway(IAiGateway inner) : IAiGateway
    {
        private readonly object _lock = new();

        public List<AiAnalysisRequest> Requests { get; } = [];

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
            lock (_lock)
            {
                Requests.Add(request);
            }

            return CallAsync(request, cancellationToken);
        }

        protected virtual Task<Result<AiAnalysisResult>> CallAsync(AiAnalysisRequest request, CancellationToken cancellationToken) =>
            inner.AnalyzeAsync(request, cancellationToken);
    }

    /// <summary>Records every analyst request, and lets a test answer some of them itself: the
    /// interceptor returns <see langword="null"/> to fall through to the inner gateway.</summary>
    private sealed class InterceptingGateway(
        IAiGateway inner,
        Func<AiAnalysisRequest, CancellationToken, Task<Result<AiAnalysisResult>>?> intercept) : RecordingGateway(inner)
    {
        protected override Task<Result<AiAnalysisResult>> CallAsync(AiAnalysisRequest request, CancellationToken cancellationToken) =>
            intercept(request, cancellationToken) ?? base.CallAsync(request, cancellationToken);
    }
}
