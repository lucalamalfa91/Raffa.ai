using System.Diagnostics;
using System.Text.Json;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Logging;
using Raffa.AiGateway.Telemetry;
using Raffa.AiGateway.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiGateway.Tests.Agents;

/// <summary>
/// Plan A-01: <see cref="StepRunner"/> calls the gateway, never bypasses it, and ends every step in an
/// explicit <see cref="StepResult{TOut}"/>: success, the declared fallback, an explicit skip, or a
/// failure; one verifier retry with the violation named; a deadline that degrades instead of hanging.
/// </summary>
public sealed class StepRunnerTests
{
    private static Task<Result<AiAnalysisResult>> Ok(AiAnalysisRequest request, string title = "ok", params string[] keys) =>
        Task.FromResult(Result<AiAnalysisResult>.Success(
            new AiAnalysisResult(AgentTestKit.Payload(title, keys), AgentTestKit.Meta(request.PromptVersion))));

    // ---------------------------------------------------------------- success path

    [Fact]
    public async Task A_step_goes_through_the_gateway_with_its_prompt_schema_version_and_serialized_input()
    {
        var gateway = new ScriptedGateway(analyze: (r, _) => Ok(r, "t", "fact:1"));
        var step = AgentTestKit.Step();

        var result = await new StepRunner(gateway).RunAsync(step, AgentTestKit.Input);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.True(result.IsSuccess);
        Assert.Equal("t", result.Value!.Title);
        Assert.Equal(["fact:1"], result.Value.CitationKeys);
        Assert.Equal(1, result.Attempts);
        Assert.False(result.VerifierIntervened);
        Assert.Equal("echo-v1", result.Metadata!.PromptVersion);

        var request = Assert.Single(gateway.AnalyzeRequests);
        Assert.Equal("echo-agent", request.AgentName);
        Assert.Equal("echo-v1", request.PromptVersion);
        Assert.Equal(step.Definition.SystemPrompt, request.SystemPrompt);
        Assert.Equal(step.Definition.JsonSchema, request.JsonSchema);
        Assert.Equal(JsonSerializer.Serialize(AgentTestKit.Input, AgentJson.Options), request.InputJson);
    }

    [Fact]
    public async Task The_input_json_is_camelCase_with_string_enums_exactly_like_the_flows_serialize_today()
    {
        var gateway = new ScriptedGateway();

        await new StepRunner(gateway).RunAsync(AgentTestKit.Step(), AgentTestKit.Input);

        Assert.Equal("""{"question":"question?","keys":["fact:1","fact:2"]}""", gateway.AnalyzeRequests[0].InputJson);
    }

    // ---------------------------------------------------------------- failure policies

    [Fact]
    public async Task A_gateway_failure_fails_a_required_step_and_skips_an_optional_one()
    {
        var gateway = new ScriptedGateway(analyze: (_, _) => Task.FromResult(Result<AiAnalysisResult>.Failure("provider said no")));

        var required = await new StepRunner(gateway).RunAsync(AgentTestKit.Step(AgentTestKit.Definition(policy: AgentFailurePolicy.Fail)), AgentTestKit.Input);
        var optional = await new StepRunner(gateway).RunAsync(AgentTestKit.Step(AgentTestKit.Definition(policy: AgentFailurePolicy.Skip)), AgentTestKit.Input);

        Assert.Equal(StepStatus.Failed, required.Status);
        Assert.True(required.IsFailure);
        Assert.Equal(StepStatus.Skipped, optional.Status);
        Assert.False(optional.IsFailure);
        Assert.All([required, optional], r =>
        {
            Assert.Equal(StepFailureReason.GatewayError, r.Reason);
            Assert.Equal("provider said no", r.Error);
            Assert.Null(r.Value);
            Assert.False(r.HasValue);
            Assert.Equal("echo-agent: provider said no", r.Describe());
        });
    }

    [Fact]
    public async Task The_fallback_policy_returns_the_deterministic_stand_in_and_says_so()
    {
        var gateway = new ScriptedGateway(analyze: (_, _) => Task.FromResult(Result<AiAnalysisResult>.Failure("down")));
        var step = AgentTestKit.Step(
            AgentTestKit.Definition(policy: AgentFailurePolicy.Fallback),
            fallback: input => new EchoOutput("template", input.Keys));

        var result = await new StepRunner(gateway).RunAsync(step, AgentTestKit.Input);

        Assert.Equal(StepStatus.FellBack, result.Status);
        Assert.Equal("template", result.Value!.Title);
        Assert.Equal(["fact:1", "fact:2"], result.Value.CitationKeys);
        Assert.True(result.HasValue);
        Assert.Equal(StepFailureReason.GatewayError, result.Reason);
    }

    [Fact]
    public async Task A_fallback_policy_without_a_fallback_value_fails()
    {
        var gateway = new ScriptedGateway(analyze: (_, _) => Task.FromResult(Result<AiAnalysisResult>.Failure("down")));

        var result = await new StepRunner(gateway).RunAsync(
            AgentTestKit.Step(AgentTestKit.Definition(policy: AgentFailurePolicy.Fallback)), AgentTestKit.Input);

        Assert.Equal(StepStatus.Failed, result.Status);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("null")]
    [InlineData("""{"title": 3, "citationKeys": "x"}""")]
    public async Task An_unusable_payload_is_an_invalid_payload_result_never_an_exception(string payload)
    {
        var gateway = new ScriptedGateway(analyze: (r, _) => Task.FromResult(Result<AiAnalysisResult>.Success(
            new AiAnalysisResult(payload, AgentTestKit.Meta(r.PromptVersion)))));

        var result = await new StepRunner(gateway).RunAsync(
            AgentTestKit.Step(AgentTestKit.Definition(policy: AgentFailurePolicy.Skip)), AgentTestKit.Input);

        Assert.Equal(StepStatus.Skipped, result.Status);
        Assert.Equal(StepFailureReason.InvalidPayload, result.Reason);
        // The call happened and is kept for the audit trail.
        Assert.NotNull(result.Metadata);
    }

    // ---------------------------------------------------------------- verifier

    [Fact]
    public async Task A_verifier_violation_buys_one_retry_with_the_violation_named_in_the_prompt()
    {
        var gateway = new ScriptedGateway();
        var verifier = new RejectingVerifier(rejections: 1, violation: "key fact:9 is not in the input.");
        var step = AgentTestKit.Step(verifier: verifier);

        var result = await new StepRunner(gateway).RunAsync(step, AgentTestKit.Input);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal(2, result.Attempts);
        Assert.True(result.VerifierIntervened);
        Assert.Equal(2, gateway.AnalyzeRequests.Count);
        Assert.Equal(step.Definition.SystemPrompt, gateway.AnalyzeRequests[0].SystemPrompt);
        Assert.StartsWith(step.Definition.SystemPrompt, gateway.AnalyzeRequests[1].SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("key fact:9 is not in the input.", gateway.AnalyzeRequests[1].SystemPrompt, StringComparison.Ordinal);
        // Same input, same schema and version on the retry.
        Assert.Equal(gateway.AnalyzeRequests[0].InputJson, gateway.AnalyzeRequests[1].InputJson);
        Assert.Equal(gateway.AnalyzeRequests[0].JsonSchema, gateway.AnalyzeRequests[1].JsonSchema);
    }

    [Fact]
    public async Task A_verifier_that_still_rejects_after_the_one_retry_degrades_by_policy_with_the_violation()
    {
        var gateway = new ScriptedGateway();
        var verifier = new RejectingVerifier(rejections: 99);

        var result = await new StepRunner(gateway).RunAsync(
            AgentTestKit.Step(AgentTestKit.Definition(policy: AgentFailurePolicy.Skip), verifier), AgentTestKit.Input);

        Assert.Equal(StepStatus.Skipped, result.Status);
        Assert.Equal(StepFailureReason.VerifierRejected, result.Reason);
        Assert.Equal("key fact:9 is not in the input.", result.Error);
        Assert.Equal(2, result.Attempts);
        Assert.True(result.VerifierIntervened);
        // At most one retry: two calls, not three.
        Assert.Equal(2, gateway.AnalyzeRequests.Count);
        Assert.Equal(2, verifier.Calls);
    }

    [Fact]
    public async Task With_retry_off_a_rejection_degrades_at_once()
    {
        var gateway = new ScriptedGateway();

        var result = await new StepRunner(gateway).RunAsync(
            AgentTestKit.Step(AgentTestKit.Definition(retry: false), new RejectingVerifier(1)), AgentTestKit.Input);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.Single(gateway.AnalyzeRequests);
    }

    [Fact]
    public async Task A_verifier_can_be_absent_and_a_passing_output_is_not_marked_intervened()
    {
        var gateway = new ScriptedGateway();

        var result = await new StepRunner(gateway).RunAsync(
            AgentTestKit.Step(verifier: new RejectingVerifier(0)), AgentTestKit.Input);

        Assert.True(result.IsSuccess);
        Assert.False(result.VerifierIntervened);
        Assert.Single(gateway.AnalyzeRequests);
    }

    // ---------------------------------------------------------------- deadline and cancellation

    [Fact]
    public async Task A_step_that_overruns_its_deadline_is_cancelled_and_skipped_explicitly()
    {
        CancellationToken seen = default;
        var gateway = new ScriptedGateway(analyze: async (r, token) =>
        {
            seen = token;
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return Result<AiAnalysisResult>.Failure("unreachable");
        });
        var step = AgentTestKit.Step(AgentTestKit.Definition(policy: AgentFailurePolicy.Skip, deadline: TimeSpan.FromMilliseconds(50)));

        var clock = Stopwatch.StartNew();
        var result = await new StepRunner(gateway).RunAsync(step, AgentTestKit.Input);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "the step must not wait for the slow call");
        Assert.Equal(StepStatus.Skipped, result.Status);
        Assert.Equal(StepFailureReason.DeadlineExceeded, result.Reason);
        Assert.Contains("deadline", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(seen.IsCancellationRequested, "the call is cancelled, not abandoned running");
    }

    [Fact]
    public async Task A_gateway_that_ignores_cancellation_still_cannot_hold_the_step_past_its_deadline()
    {
        var release = new TaskCompletionSource<Result<AiAnalysisResult>>();
        var gateway = new ScriptedGateway(analyze: (_, _) => release.Task);
        var step = AgentTestKit.Step(AgentTestKit.Definition(policy: AgentFailurePolicy.Fail, deadline: TimeSpan.FromMilliseconds(50)));

        var result = await new StepRunner(gateway).RunAsync(step, AgentTestKit.Input);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Equal(StepFailureReason.DeadlineExceeded, result.Reason);

        // The abandoned call finishing (or failing) later is harmless.
        release.SetException(new InvalidOperationException("late"));
    }

    [Fact]
    public async Task The_deadline_covers_the_retry_too()
    {
        var calls = 0;
        var gateway = new ScriptedGateway(analyze: async (r, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                return Result<AiAnalysisResult>.Success(new AiAnalysisResult(AgentTestKit.Payload(), AgentTestKit.Meta(r.PromptVersion)));
            }

            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return Result<AiAnalysisResult>.Failure("unreachable");
        });
        var step = AgentTestKit.Step(
            AgentTestKit.Definition(policy: AgentFailurePolicy.Skip, deadline: TimeSpan.FromMilliseconds(100)),
            new RejectingVerifier(1));

        var result = await new StepRunner(gateway).RunAsync(step, AgentTestKit.Input);

        Assert.Equal(StepStatus.Skipped, result.Status);
        Assert.Equal(StepFailureReason.DeadlineExceeded, result.Reason);
        Assert.Equal(2, result.Attempts);
        Assert.True(result.VerifierIntervened);
    }

    [Fact]
    public async Task The_callers_cancellation_propagates_instead_of_degrading()
    {
        using var cts = new CancellationTokenSource();
        var gateway = new ScriptedGateway(analyze: async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return Result<AiAnalysisResult>.Failure("unreachable");
        });
        var step = AgentTestKit.Step(AgentTestKit.Definition(policy: AgentFailurePolicy.Skip));

        var running = new StepRunner(gateway).RunAsync(step, AgentTestKit.Input, cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task A_provider_client_that_gives_up_is_a_gateway_error_result_not_an_exception()
    {
        var gateway = new ScriptedGateway(analyze: (_, _) => throw new TaskCanceledException("http timeout"));

        var result = await new StepRunner(gateway).RunAsync(
            AgentTestKit.Step(AgentTestKit.Definition(policy: AgentFailurePolicy.Skip)), AgentTestKit.Input);

        Assert.Equal(StepStatus.Skipped, result.Status);
        Assert.Equal(StepFailureReason.GatewayError, result.Reason);
    }

    [Fact]
    public async Task A_caller_bug_such_as_a_missing_tenant_scope_still_throws_out_of_the_runner()
    {
        var logging = new LoggingAiGateway(
            new ScriptedGateway(), new RecordingAuditWriter(), new TenantContext(), new AiGatewayComplianceOptions());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new StepRunner(logging).RunAsync(AgentTestKit.Step(AgentTestKit.Definition(policy: AgentFailurePolicy.Skip)), AgentTestKit.Input));

        Assert.Contains("tenant scope", exception.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- run context, audit, spans

    [Fact]
    public async Task Every_audit_row_carries_the_runner_run_id_and_the_steps_own_name()
    {
        var audit = new RecordingAuditWriter();
        var tenant = new TenantContext();
        var gateway = new LoggingAiGateway(new ScriptedGateway(), audit, tenant, new AiGatewayComplianceOptions());
        var runner = new StepRunner(gateway);

        using (tenant.BeginScope(TenantId.New()))
        {
            await Task.WhenAll(
                runner.RunAsync(AgentTestKit.Step(AgentTestKit.Definition("contract-analyst")), AgentTestKit.Input),
                runner.RunAsync(AgentTestKit.Step(AgentTestKit.Definition("market-analyst")), AgentTestKit.Input),
                runner.RunAsync(AgentTestKit.Step(AgentTestKit.Definition("lever-strategist")), AgentTestKit.Input));

            // The scopes the runner opens do not leak into the caller.
            Assert.Null(RunContext.Current);
        }

        Assert.Equal(3, audit.Written.Count);
        var runIds = audit.Written.Select(e => System.Text.RegularExpressions.Regex.Match(e.Detail!, @"\brun=(\S+)").Groups[1].Value).Distinct().ToList();
        var runId = Assert.Single(runIds);
        Assert.NotEqual("none", runId);
        foreach (var name in new[] { "contract-analyst", "market-analyst", "lever-strategist" })
        {
            Assert.Contains(audit.Written, e => e.Detail!.Contains($"agent={name} run={runId}", StringComparison.Ordinal) &&
                e.Detail!.Contains($"step={name} ", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_run_the_caller_opened_is_kept_and_a_second_runner_gets_its_own_run()
    {
        var audit = new RecordingAuditWriter();
        var tenant = new TenantContext();
        var gateway = new LoggingAiGateway(new ScriptedGateway(), audit, tenant, new AiGatewayComplianceOptions());

        using (tenant.BeginScope(TenantId.New()))
        {
            using (RunContext.BeginTurn("turn-1"))
            using (RunContext.BeginRun("run-mine"))
            {
                await new StepRunner(gateway).RunAsync(AgentTestKit.Step(), AgentTestKit.Input);
            }

            await new StepRunner(gateway).RunAsync(AgentTestKit.Step(), AgentTestKit.Input);
            await new StepRunner(gateway).RunAsync(AgentTestKit.Step(), AgentTestKit.Input);
        }

        Assert.Contains("run=run-mine turn=turn-1 step=echo-agent", audit.Written[0].Detail, StringComparison.Ordinal);
        var other = audit.Written.Skip(1).Select(e => System.Text.RegularExpressions.Regex.Match(e.Detail!, @"\brun=(\S+)").Groups[1].Value).ToList();
        Assert.All(other, id => Assert.NotEqual("none", id));
        Assert.NotEqual(other[0], other[1]);
    }

    [Fact]
    public async Task The_runner_opens_one_span_per_step_with_names_counts_and_outcome_and_never_text()
    {
        var spans = new List<Activity>();
        using var listener = AgentTestKit.Listen(spans);
        var gateway = new ScriptedGateway();
        var name = $"span-agent-{Guid.NewGuid():N}";

        using (RunContext.BeginTurn("turn-s"))
        using (RunContext.BeginRun("run-s"))
        {
            await new StepRunner(gateway).RunAsync(
                AgentTestKit.Step(AgentTestKit.Definition(name), new RejectingVerifier(1)),
                new EchoInput("SECRET-QUESTION-TEXT", ["fact:1"]));
        }

        Activity span;
        lock (spans)
        {
            span = Assert.Single(spans, s => s.DisplayName == $"agent {name}");
        }

        Assert.Equal("invoke_agent", span.GetTagItem(AgentTelemetry.OperationName));
        Assert.Equal(name, span.GetTagItem(AgentTelemetry.AgentName));
        Assert.Equal("echo-v1", span.GetTagItem(AgentTelemetry.AgentVersion));
        Assert.Equal("run-s", span.GetTagItem(AgentTelemetry.RunId));
        Assert.Equal("turn-s", span.GetTagItem(AgentTelemetry.TurnId));
        Assert.Equal(name, span.GetTagItem(AgentTelemetry.StepName));
        Assert.Equal(AgentTelemetry.OutcomeOk, span.GetTagItem(AgentTelemetry.Outcome));
        Assert.Equal(2, span.GetTagItem(AgentTelemetry.StepAttempts));
        Assert.Equal(true, span.GetTagItem(AgentTelemetry.VerifierIntervened));
        Assert.DoesNotContain(span.TagObjects, t => t.Value?.ToString()?.Contains("SECRET", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task A_skipped_and_a_failed_step_are_labelled_in_their_span()
    {
        var spans = new List<Activity>();
        using var listener = AgentTestKit.Listen(spans);
        var gateway = new ScriptedGateway(analyze: (_, _) => Task.FromResult(Result<AiAnalysisResult>.Failure("no")));
        var skipped = $"skip-agent-{Guid.NewGuid():N}";
        var failed = $"fail-agent-{Guid.NewGuid():N}";
        var runner = new StepRunner(gateway);

        await runner.RunAsync(AgentTestKit.Step(AgentTestKit.Definition(skipped, AgentFailurePolicy.Skip)), AgentTestKit.Input);
        await runner.RunAsync(AgentTestKit.Step(AgentTestKit.Definition(failed, AgentFailurePolicy.Fail)), AgentTestKit.Input);

        lock (spans)
        {
            var skipSpan = Assert.Single(spans, s => s.DisplayName == $"agent {skipped}");
            Assert.Equal(AgentTelemetry.OutcomeSkipped, skipSpan.GetTagItem(AgentTelemetry.Outcome));
            Assert.Equal("GatewayError", skipSpan.GetTagItem(AgentTelemetry.StepReason));
            var failSpan = Assert.Single(spans, s => s.DisplayName == $"agent {failed}");
            Assert.Equal(AgentTelemetry.OutcomeError, failSpan.GetTagItem(AgentTelemetry.Outcome));
            Assert.Equal(ActivityStatusCode.Error, failSpan.Status);
        }
    }

    // ---------------------------------------------------------------- answer role

    [Fact]
    public async Task The_answer_role_goes_through_AnswerAsync_with_the_prompt_pack_and_version()
    {
        var gateway = new ScriptedGateway();
        var definition = new AgentDefinition(
            "answer-composer", "answer-v2.5", "ANSWER PROMPT", string.Empty, TimeSpan.FromSeconds(5),
            AgentFailurePolicy.Fail, AgentRole.Answer);
        var step = new AgentStep<AnswerStepInput, AiAnswerResult>(definition);
        var input = new AnswerStepInput("what is the notice?", [], "{\"items\":[]}");

        var result = await new StepRunner(gateway).RunAnswerAsync(step, input);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CanDetermine);
        var request = Assert.Single(gateway.AnswerRequests);
        Assert.Equal("what is the notice?", request.Question);
        Assert.Equal("ANSWER PROMPT", request.SystemPrompt);
        Assert.Equal("{\"items\":[]}", request.PackJson);
        Assert.Equal("answer-v2.5", request.PromptVersion);
        Assert.Empty(gateway.AnalyzeRequests);
    }

    [Fact]
    public async Task An_abstaining_answer_is_a_success_not_a_failure()
    {
        var gateway = new ScriptedGateway(answer: (r, _) => Task.FromResult(Result<AiAnswerResult>.Success(
            new AiAnswerResult(false, null, [], AgentTestKit.Meta(r.PromptVersion ?? "n"), AbstainReason: "no evidence"))));
        var definition = new AgentDefinition("answer-composer", "answer-v1", "P", string.Empty, TimeSpan.FromSeconds(5), role: AgentRole.Answer);

        var result = await new StepRunner(gateway).RunAnswerAsync(
            new AgentStep<AnswerStepInput, AiAnswerResult>(definition), new AnswerStepInput("q", []));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.CanDetermine);
    }

    [Fact]
    public void A_step_run_in_the_wrong_role_is_a_programming_error()
    {
        var runner = new StepRunner(new ScriptedGateway());
        var answer = new AgentDefinition("answer-composer", "v1", "P", string.Empty, TimeSpan.FromSeconds(5), role: AgentRole.Answer);

        // Thrown synchronously, before any call is made.
        Assert.Throws<ArgumentException>(() =>
        {
            _ = runner.RunAsync(new AgentStep<EchoInput, EchoOutput>(answer), AgentTestKit.Input);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            _ = runner.RunAnswerAsync(
                new AgentStep<AnswerStepInput, AiAnswerResult>(AgentTestKit.Definition()), new AnswerStepInput("q", []));
        });
    }

    // ---------------------------------------------------------------- fan-out

    [Fact]
    public async Task Fan_out_returns_results_in_input_order_and_tolerates_partial_failures()
    {
        var gateway = new ScriptedGateway(analyze: (r, _) =>
        {
            var input = JsonSerializer.Deserialize<EchoInput>(r.InputJson, AgentJson.Options)!;
            return input.Question == "boom"
                ? Task.FromResult(Result<AiAnalysisResult>.Failure("this one fails"))
                : Ok(r, input.Question);
        });
        var step = AgentTestKit.Step(AgentTestKit.Definition(policy: AgentFailurePolicy.Skip));
        var inputs = new[] { "a", "boom", "c", "d" }.Select(q => new EchoInput(q, [])).ToList();

        var results = await new StepRunner(gateway).FanOutAsync(step, inputs);

        Assert.Equal(["a", null, "c", "d"], results.Select(r => r.Value?.Title));
        Assert.Equal(
            [StepStatus.Succeeded, StepStatus.Skipped, StepStatus.Succeeded, StepStatus.Succeeded],
            results.Select(r => r.Status));
        Assert.Single(results, r => r.Describe() is not null);
    }

    [Fact]
    public async Task Fan_out_runs_steps_concurrently_up_to_the_configured_cap_and_no_further()
    {
        async Task<int> MaxInFlight(int cap)
        {
            var gateway = new ScriptedGateway(analyze: async (r, token) =>
            {
                await Task.Delay(40, token);
                return Result<AiAnalysisResult>.Success(new AiAnalysisResult(AgentTestKit.Payload(), AgentTestKit.Meta(r.PromptVersion)));
            });
            var inputs = Enumerable.Range(0, 12).Select(i => new EchoInput($"q{i}", [])).ToList();

            var results = await new StepRunner(gateway, new AgentRunnerOptions { MaxParallelism = cap })
                .FanOutAsync(AgentTestKit.Step(), inputs);

            Assert.All(results, r => Assert.True(r.IsSuccess));
            return gateway.MaxInFlight;
        }

        Assert.Equal(1, await MaxInFlight(1));
        Assert.InRange(await MaxInFlight(4), 2, 4);
        Assert.InRange(await MaxInFlight(100), 6, 12);
    }

    [Fact]
    public async Task The_wait_for_a_slot_does_not_eat_into_the_step_deadline()
    {
        var gateway = new ScriptedGateway(analyze: async (r, token) =>
        {
            await Task.Delay(60, token);
            return Result<AiAnalysisResult>.Success(new AiAnalysisResult(AgentTestKit.Payload(), AgentTestKit.Meta(r.PromptVersion)));
        });
        // Five steps one at a time, each 60 ms with a 300 ms deadline: the last waits ~240 ms for its slot.
        var step = AgentTestKit.Step(AgentTestKit.Definition(deadline: TimeSpan.FromMilliseconds(300)));
        var inputs = Enumerable.Range(0, 5).Select(i => new EchoInput($"q{i}", [])).ToList();

        var results = await new StepRunner(gateway, new AgentRunnerOptions { MaxParallelism = 1 }).FanOutAsync(step, inputs);

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Describe()));
    }

    [Fact]
    public async Task Cancelling_while_queued_for_a_slot_propagates()
    {
        using var cts = new CancellationTokenSource();
        var hold = new TaskCompletionSource<Result<AiAnalysisResult>>();
        var gateway = new ScriptedGateway(analyze: (_, _) => hold.Task);
        var runner = new StepRunner(gateway, new AgentRunnerOptions { MaxParallelism = 1 });
        var step = AgentTestKit.Step(AgentTestKit.Definition(deadline: TimeSpan.FromSeconds(30)));

        var first = runner.RunAsync(step, AgentTestKit.Input);
        var queued = runner.RunAsync(step, AgentTestKit.Input, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        hold.SetResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(AgentTestKit.Payload(), AgentTestKit.Meta("echo-v1"))));
        Assert.True((await first).IsSuccess);
    }
}
