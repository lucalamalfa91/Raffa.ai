using System.Diagnostics;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Logging;
using Raffa.AiGateway.Telemetry;
using Raffa.AiGateway.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiGateway.Tests.Logging;

/// <summary>
/// Plan T-01: every AI call is a <c>Raffa.Agents</c> span and an <c>ai.*</c> audit row that carries
/// <c>agent=</c>, <c>run=</c>, <c>turn=</c> and <c>step=</c> (RunId/TurnId/StepName from the ambient
/// <see cref="RunContext"/>) -- 100% of the rows, for all seven roles, with or without a run -- and
/// never raw text.
/// </summary>
public sealed class AgentTelemetryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private const string Secret = "TOP-SECRET-CLAUSE-TEXT";

    private static (LoggingAiGateway Gateway, RecordingAuditWriter Audit, TenantContext Tenant) Create(StubGateway? inner = null)
    {
        var audit = new RecordingAuditWriter();
        var tenant = new TenantContext();
        return (new LoggingAiGateway(inner ?? new StubGateway(), audit, tenant, new AiGatewayComplianceOptions()), audit, tenant);
    }

    /// <summary>One call of each role, in a fixed order; each returns the role's result.</summary>
    private static readonly (string Action, Func<LoggingAiGateway, Task<bool>> Call)[] AllRoles =
    [
        ("ai.classified", async g => (await g.ClassifyAsync(new AiClassificationRequest(Secret))).IsSuccess),
        ("ai.extracted", async g => (await g.ExtractAsync(new AiExtractionRequest("commercial-terms", Secret, "{}"))).IsSuccess),
        ("ai.embedded", async g => (await g.EmbedAsync(new AiEmbeddingRequest(Secret))).IsSuccess),
        ("ai.answered", async g => (await g.AnswerAsync(new AiAnswerRequest(Secret, []))).IsSuccess),
        ("ai.analyzed", async g => (await g.AnalyzeAsync(new AiAnalysisRequest("lever-strategist", Secret, Secret, "{}", "council-v3"))).IsSuccess),
        ("ai.researched", async g => (await g.ResearchAsync(new AiResearchRequest(Secret, "p", "en", 3, Secret, "web-v1"))).IsSuccess),
        ("ai.ocr", async g => (await g.OcrAsync(new AiOcrRequest("a.pdf", "application/pdf", new byte[] { 1, 2 }))).IsSuccess),
    ];

    [Fact]
    public async Task Every_role_row_carries_agent_run_turn_and_step_even_outside_a_run()
    {
        var (gateway, audit, tenant) = Create();

        using (tenant.BeginScope(TenantId.New()))
        {
            foreach (var (_, call) in AllRoles)
            {
                Assert.True(await call(gateway));
            }
        }

        Assert.Equal(AllRoles.Length, audit.Written.Count);
        Assert.Equal(AllRoles.Select(r => r.Action), audit.Written.Select(e => e.Action));
        foreach (var entry in audit.Written)
        {
            var detail = entry.Detail!;
            Assert.Matches(@"\bagent=\S+", detail);
            Assert.Matches(@"\brun=\S+", detail);
            Assert.Matches(@"\bturn=\S+", detail);
            Assert.Matches(@"\bstep=\S+", detail);
            Assert.Matches(@"\blatencyMs=\d+", detail);
            Assert.Contains("outcome=ok", detail, StringComparison.Ordinal);
            // No run is open: the keys are still there, explicitly "none".
            Assert.Contains("run=none", detail, StringComparison.Ordinal);
            Assert.Contains("turn=none", detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Rows_carry_the_ambient_run_turn_and_step_and_the_agent_name()
    {
        var (gateway, audit, tenant) = Create();

        using (tenant.BeginScope(TenantId.New()))
        using (RunContext.BeginTurn("turn-123"))
        using (RunContext.BeginRun("run-456"))
        {
            using (RunContext.BeginStep("market-researcher"))
            {
                await gateway.AnalyzeAsync(new AiAnalysisRequest("market-researcher", "s", "i", "{}", "council-v3"));
            }

            // Outside a named step the analyst's own name is the step.
            await gateway.AnalyzeAsync(new AiAnalysisRequest("contract-analyst", "s", "i", "{}", "council-v3"));
        }

        var first = audit.Written[0].Detail!;
        Assert.Contains("agent=market-researcher run=run-456 turn=turn-123 step=market-researcher", first, StringComparison.Ordinal);
        var second = audit.Written[1].Detail!;
        Assert.Contains("agent=contract-analyst run=run-456 turn=turn-123 step=contract-analyst", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rows_never_carry_raw_text()
    {
        var (gateway, audit, tenant) = Create();

        using (tenant.BeginScope(TenantId.New()))
        {
            foreach (var (_, call) in AllRoles)
            {
                await call(gateway);
            }
        }

        Assert.All(audit.Written, e => Assert.DoesNotContain(Secret, e.Detail!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Each_call_is_one_span_with_agent_version_tenant_hash_tokens_and_outcome()
    {
        var tenantId = TenantId.New();
        var hash = AgentTelemetry.HashTenant(tenantId);
        var spans = new List<Activity>();
        using var listener = Listen(spans, hash);
        var (gateway, _, tenant) = Create();

        using (tenant.BeginScope(tenantId))
        using (RunContext.BeginTurn("turn-9"))
        using (RunContext.BeginRun("run-9"))
        using (RunContext.BeginStep("lever-strategist"))
        {
            await gateway.AnalyzeAsync(new AiAnalysisRequest("lever-strategist", Secret, Secret, "{}", "council-v3"));
        }

        var span = Assert.Single(spans);
        Assert.Equal("chat lever-strategist", span.DisplayName);
        Assert.Equal("lever-strategist", span.GetTagItem(AgentTelemetry.AgentName));
        Assert.Equal("council-v3", span.GetTagItem(AgentTelemetry.AgentVersion));
        Assert.Equal("run-9", span.GetTagItem(AgentTelemetry.RunId));
        Assert.Equal("turn-9", span.GetTagItem(AgentTelemetry.TurnId));
        Assert.Equal("lever-strategist", span.GetTagItem(AgentTelemetry.StepName));
        Assert.Equal(StubGateway.ModelId, span.GetTagItem(AgentTelemetry.ResponseModel));
        Assert.Equal(120, span.GetTagItem(AgentTelemetry.UsageInputTokens));
        Assert.Equal(9, span.GetTagItem(AgentTelemetry.UsageOutputTokens));
        Assert.Equal(AgentTelemetry.OutcomeOk, span.GetTagItem(AgentTelemetry.Outcome));
        Assert.NotNull(span.GetTagItem(AgentTelemetry.LatencyMs));

        // The tenant is a hash, never the id; no tag holds request text.
        Assert.Equal(hash, span.GetTagItem(AgentTelemetry.TenantHash));
        Assert.DoesNotContain(span.TagObjects, t =>
            t.Value?.ToString()?.Contains(tenantId.Value.ToString("N"), StringComparison.OrdinalIgnoreCase) == true ||
            t.Value?.ToString()?.Contains(tenantId.Value.ToString("D"), StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(span.TagObjects, t => t.Value?.ToString()?.Contains(Secret, StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task All_seven_roles_open_a_span()
    {
        var tenantId = TenantId.New();
        var spans = new List<Activity>();
        using var listener = Listen(spans, AgentTelemetry.HashTenant(tenantId));
        var (gateway, _, tenant) = Create();

        using (tenant.BeginScope(tenantId))
        {
            foreach (var (_, call) in AllRoles)
            {
                await call(gateway);
            }
        }

        Assert.Equal(AllRoles.Length, spans.Count);
        Assert.All(spans, s => Assert.Equal(AgentTelemetry.OutcomeOk, s.GetTagItem(AgentTelemetry.Outcome)));
        Assert.Contains(spans, s => s.GetTagItem(AgentTelemetry.OperationName) as string == "embeddings");
    }

    [Fact]
    public async Task A_failed_call_has_an_error_span_and_still_no_audit_row()
    {
        var tenantId = TenantId.New();
        var spans = new List<Activity>();
        using var listener = Listen(spans, AgentTelemetry.HashTenant(tenantId));
        var (gateway, audit, tenant) = Create(new StubGateway { Fail = true });

        using (tenant.BeginScope(tenantId))
        {
            var result = await gateway.AnalyzeAsync(new AiAnalysisRequest("lever-strategist", "s", "i", "{}", "v"));
            Assert.True(result.IsFailure);
        }

        Assert.Empty(audit.Written);
        var span = Assert.Single(spans);
        Assert.Equal(AgentTelemetry.OutcomeError, span.GetTagItem(AgentTelemetry.Outcome));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        // The provider's error text is never copied onto the span.
        Assert.DoesNotContain(span.TagObjects, t => t.Value?.ToString()?.Contains("provider said no", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task A_cancelled_call_is_a_cancelled_span()
    {
        var tenantId = TenantId.New();
        var spans = new List<Activity>();
        using var listener = Listen(spans, AgentTelemetry.HashTenant(tenantId));
        var (gateway, _, tenant) = Create(new StubGateway { Throw = new OperationCanceledException() });

        using (tenant.BeginScope(tenantId))
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                gateway.AnalyzeAsync(new AiAnalysisRequest("a", "s", "i", "{}", "v")));
        }

        Assert.Equal(AgentTelemetry.OutcomeCancelled, Assert.Single(spans).GetTagItem(AgentTelemetry.Outcome));
    }

    [Fact]
    public async Task Without_a_listener_the_calls_still_work_and_log()
    {
        var (gateway, audit, tenant) = Create();

        using (tenant.BeginScope(TenantId.New()))
        {
            Assert.True(await AllRoles[0].Call(gateway));
        }

        Assert.Single(audit.Written);
    }

    [Fact]
    public async Task Parallel_steps_each_keep_their_own_step_name()
    {
        var (gateway, audit, tenant) = Create();

        using (tenant.BeginScope(TenantId.New()))
        using (RunContext.BeginTurn("t"))
        using (RunContext.BeginRun("r"))
        {
            async Task Agent(string name)
            {
                using var step = RunContext.BeginStep(name);
                await Task.Yield();
                await gateway.AnalyzeAsync(new AiAnalysisRequest(name, "s", "i", "{}", "v"));
            }

            await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Agent($"agent-{i}")));

            // The caller's own context is untouched by the children.
            Assert.Null(RunContext.Current!.StepName);
        }

        Assert.Equal(20, audit.Written.Count);
        foreach (var entry in audit.Written)
        {
            var agent = System.Text.RegularExpressions.Regex.Match(entry.Detail!, @"agent=(\S+)").Groups[1].Value;
            Assert.Contains($"step={agent} ", entry.Detail!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RunContext_scopes_nest_and_restore_and_values_are_sanitised()
    {
        Assert.Null(RunContext.Current);

        using (RunContext.BeginTurn("a b\nc=d"))
        {
            Assert.Equal("a_b_c_d", RunContext.Current!.TurnId);

            using (RunContext.BeginRun())
            using (RunContext.BeginStep("s"))
            {
                Assert.Equal("a_b_c_d", RunContext.Current!.TurnId);
                Assert.Equal(16, RunContext.Current.RunId!.Length);
                Assert.Equal("s", RunContext.Current.StepName);
            }

            Assert.Null(RunContext.Current!.RunId);
            Assert.Null(RunContext.Current.StepName);
        }

        Assert.Null(RunContext.Current);
    }

    [Fact]
    public void Turn_details_are_shared_by_the_turns_scopes_and_cannot_split_a_line()
    {
        Assert.Null(RunContext.TurnDetail);
        RunContext.AddTurnDetail("ignored outside a turn");
        Assert.Null(RunContext.TurnDetail);

        using (RunContext.BeginTurn())
        {
            using (RunContext.BeginRun())
            using (RunContext.BeginStep("x"))
            {
                RunContext.AddTurnDetail("stepsRun=2\nforged=1");
            }

            Assert.Equal("stepsRun=2forged=1", RunContext.TurnDetail);
        }

        Assert.Null(RunContext.TurnDetail);
    }

    private static ActivityListener Listen(List<Activity> sink, string tenantHash)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AgentTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (Equals(activity.GetTagItem(AgentTelemetry.TenantHash), tenantHash))
                {
                    lock (sink)
                    {
                        sink.Add(activity);
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    /// <summary>Succeeds (with token usage) on every role, or fails / throws on request.</summary>
    private sealed class StubGateway : IAiGateway
    {
        public const string ModelId = "stub-model";

        public bool Fail { get; init; }

        public Exception? Throw { get; init; }

        private static AiCallMetadata Meta(string promptVersion = "stub-v1") =>
            new(ModelId, "2026-10", promptVersion, Now, "inputhash", new AiTokenUsage(120, 9));

        private Task<Result<T>> Respond<T>(Func<T> success)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            return Task.FromResult(Fail ? Result<T>.Failure("provider said no") : Result<T>.Success(success()));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            Respond(() => new AiClassificationResult(AiDocumentType.Msa, 0.9, Meta()));

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            Respond(() => new AiExtractionResult("{}", Meta()));

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            Respond(() => new AiEmbeddingResult([0.1f], Meta()));

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            Respond(() => new AiAnswerResult(true, "a", [], Meta()));

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            Respond(() => new AiOcrResult([new AiOcrPage(1, "t")], Meta()));

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
            Respond(() => new AiAnalysisResult("{}", Meta(request.PromptVersion)));

        public Task<Result<AiResearchResult>> ResearchAsync(AiResearchRequest request, CancellationToken cancellationToken = default) =>
            Respond(() => new AiResearchResult("s", [], false, Meta()));
    }
}
