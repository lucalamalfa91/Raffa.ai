using System.Diagnostics;
using System.Text.Json;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Telemetry;

namespace Raffa.AiGateway.Agents;

/// <summary>
/// Runs <see cref="IAgentStep{TIn,TOut}"/>s on top of <see cref="IAiGateway"/> (plan A-01, decision D1:
/// an in-house runner, activity-first, no framework). Per step it: serializes the input, calls the
/// gateway's <see cref="IAiGateway.AnalyzeAsync"/> (or <see cref="IAiGateway.AnswerAsync"/>),
/// deserializes the output, runs the step's verifier, and, if the verifier names a violation, asks
/// once more with that violation appended to the prompt; all under the step's deadline, and ending in
/// a <see cref="StepResult{TOut}"/> that is explicit about success, fallback, skip or failure.
///
/// <para>
/// <b>It never bypasses the gateway.</b> The only path to a model is the injected
/// <see cref="IAiGateway"/>, so tenant scope (the ambient <c>AsyncLocal</c> the gateway reads), the
/// <c>NoTraining</c> audit row per call (<see cref="Logging.LoggingAiGateway"/>) and
/// <c>FoundryRetryPolicy</c> are exactly what they were. A missing tenant scope is still the
/// caller's bug and still throws out of <see cref="RunAsync{TIn,TOut}"/>; only expected outcomes
/// (a failed call, an unusable payload, a rejected output, a missed deadline) become results.
/// </para>
///
/// <para>
/// <b>Concurrency.</b> Steps of one runner run in parallel when the flow starts them together
/// (<see cref="FanOutAsync{TIn,TOut}"/>, or <c>Task.WhenAll</c> over several <see cref="RunAsync{TIn,TOut}"/>),
/// capped by <see cref="AgentRunnerOptions.MaxParallelism"/>. The runner touches no <c>DbContext</c>
/// and holds no mutable state besides the cap and the default run id, so it adds no thread-safety
/// hazard; the one shared resource steps reach, the audit write, is serialized inside the (Scoped)
/// gateway instance the runner shares. The runner is therefore Scoped with the gateway: one per
/// request or turn.
/// </para>
///
/// <para>
/// <b>Traceability.</b> Each step runs inside a <see cref="RunContext"/> step of its own name (and a
/// run of the runner's own when the flow has not opened one, shared by every step of this runner) and
/// inside an <c>Raffa.Agents</c> span <c>agent {name}</c>, the parent of the gateway's call spans; so
/// every <c>ai.*</c> audit row carries <c>run=</c> and <c>step=</c> (plan T-01, A-01). Tags are names,
/// versions, ids, counts and the outcome, never text.
/// </para>
/// </summary>
public sealed class StepRunner
{
    private readonly IAiGateway _gateway;
    private readonly SemaphoreSlim _gate;
    private string? _defaultRunId;

    public StepRunner(IAiGateway gateway, AgentRunnerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        _gateway = gateway;
        _gate = new SemaphoreSlim(Math.Max(1, (options ?? new AgentRunnerOptions()).MaxParallelism));
    }

    /// <summary>Opens an agentic run (a fresh id by default) around a flow, so every step started inside
    /// it is audited under that run id. Dispose the result to leave the run.</summary>
    public IDisposable BeginRun(string? runId = null) => RunContext.BeginRun(runId);

    /// <summary>Runs one <see cref="AgentRole.Analyze"/> step.</summary>
    public Task<StepResult<TOut>> RunAsync<TIn, TOut>(
        IAgentStep<TIn, TOut> step, TIn input, CancellationToken cancellationToken = default)
        where TIn : notnull
        where TOut : class
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(input);
        RequireRole(step.Definition, AgentRole.Analyze);

        var inputJson = JsonSerializer.Serialize(input, AgentJson.Options);
        return ExecuteAsync(
            step,
            input,
            (system, token) => AnalyzeAttemptAsync<TOut>(step.Definition, system, inputJson, token),
            cancellationToken);
    }

    /// <summary>Runs one <see cref="AgentRole.Answer"/> step (the grounded answer role).</summary>
    public Task<StepResult<AiAnswerResult>> RunAnswerAsync(
        IAgentStep<AnswerStepInput, AiAnswerResult> step, AnswerStepInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(input);
        RequireRole(step.Definition, AgentRole.Answer);

        return ExecuteAsync(
            step,
            input,
            async (system, token) =>
            {
                var result = await _gateway.AnswerAsync(
                        new AiAnswerRequest(input.Question, input.Evidence, system, input.PackJson, step.Definition.Version), token)
                    .ConfigureAwait(false);
                return result.IsFailure
                    ? Attempt<AiAnswerResult>.Fail(StepFailureReason.GatewayError, result.Error)
                    : Attempt<AiAnswerResult>.Ok(result.Value, result.Value.Metadata);
            },
            cancellationToken);
    }

    /// <summary>
    /// The same step over several inputs at once (the seven extraction stages, one analysis per
    /// contract), results in input order. A partial failure is a result, not an exception: the flow
    /// reads each <see cref="StepResult{TOut}.Status"/>.
    /// </summary>
    public async Task<IReadOnlyList<StepResult<TOut>>> FanOutAsync<TIn, TOut>(
        IAgentStep<TIn, TOut> step, IReadOnlyList<TIn> inputs, CancellationToken cancellationToken = default)
        where TIn : notnull
        where TOut : class
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(inputs);

        var tasks = new Task<StepResult<TOut>>[inputs.Count];
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = RunAsync(step, inputs[i], cancellationToken);
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<StepResult<TOut>> ExecuteAsync<TIn, TOut>(
        IAgentStep<TIn, TOut> step,
        TIn input,
        Func<string, CancellationToken, Task<Attempt<TOut>>> attemptCall,
        CancellationToken cancellationToken)
        where TIn : notnull
        where TOut : class
    {
        // The queue for a free slot is not part of the step's deadline: the deadline starts when the
        // step does.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExecuteGatedAsync(step, input, attemptCall, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StepResult<TOut>> ExecuteGatedAsync<TIn, TOut>(
        IAgentStep<TIn, TOut> step,
        TIn input,
        Func<string, CancellationToken, Task<Attempt<TOut>>> attemptCall,
        CancellationToken cancellationToken)
        where TIn : notnull
        where TOut : class
    {
        var definition = step.Definition;

        // The caller's run when it opened one; otherwise this runner's, so sibling steps of one
        // fan-out share a run id instead of each minting its own. Scopes are local to this async flow.
        using var run = RunContext.Current?.RunId is null
            ? RunContext.BeginRun(LazyInitializer.EnsureInitialized(ref _defaultRunId, RunContext.NewId))
            : null;
        using var stepScope = RunContext.BeginStep(definition.Name);
        using var activity = StartSpan(definition);

        var clock = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(definition.Deadline);

        var verifier = step.Verifier;
        var maxAttempts = verifier is not null && definition.RetryOnVerifierViolation ? 2 : 1;
        var systemPrompt = definition.SystemPrompt;
        var attempts = 0;
        var intervened = false;
        AiCallMetadata? metadata = null;
        StepResult<TOut> result;

        try
        {
            while (true)
            {
                attempts++;
                var attempt = await WithDeadline(attemptCall(systemPrompt, deadline.Token), deadline.Token).ConfigureAwait(false);
                metadata = attempt.Metadata ?? metadata;

                if (attempt.Value is null)
                {
                    result = Degrade(step, input, attempt.Reason, attempt.Error, attempts, intervened, metadata, clock);
                    break;
                }

                var verdict = verifier?.Verify(input, attempt.Value) ?? StepVerdict.Pass;
                if (verdict.Ok)
                {
                    result = new StepResult<TOut>(
                        definition.Name, StepStatus.Succeeded, attempt.Value, StepFailureReason.None, null,
                        attempts, intervened, metadata, clock.Elapsed);
                    break;
                }

                intervened = true;
                if (attempts < maxAttempts)
                {
                    // The generalization of RegenerateOnce: one more attempt, the violation named.
                    systemPrompt = definition.SystemPrompt + Environment.NewLine + Environment.NewLine + verifier!.RetryInstruction(verdict);
                    continue;
                }

                result = Degrade(
                    step, input, StepFailureReason.VerifierRejected, string.Join(' ', verdict.Violations),
                    attempts, intervened, metadata, clock);
                break;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Not the caller's cancellation (that propagates): the step's own deadline, or a provider
            // client giving up. Either way the step degrades by its policy, explicitly.
            result = deadline.IsCancellationRequested
                ? Degrade(
                    step, input, StepFailureReason.DeadlineExceeded,
                    $"deadline of {definition.Deadline.TotalSeconds:0.##}s exceeded.", attempts, intervened, metadata, clock)
                : Degrade(
                    step, input, StepFailureReason.GatewayError,
                    "the call was cancelled by the provider client.", attempts, intervened, metadata, clock);
        }

        Annotate(activity, result);
        return result;
    }

    private async Task<Attempt<TOut>> AnalyzeAttemptAsync<TOut>(
        AgentDefinition definition, string systemPrompt, string inputJson, CancellationToken token)
        where TOut : class
    {
        var result = await _gateway.AnalyzeAsync(
                new AiAnalysisRequest(definition.Name, systemPrompt, inputJson, definition.JsonSchema, definition.Version), token)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Attempt<TOut>.Fail(StepFailureReason.GatewayError, result.Error);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<TOut>(result.Value.PayloadJson, AgentJson.Options);
            return payload is null
                ? Attempt<TOut>.Fail(StepFailureReason.InvalidPayload, "payload parsed to null.", result.Value.Metadata)
                : Attempt<TOut>.Ok(payload, result.Value.Metadata);
        }
        catch (JsonException exception)
        {
            return Attempt<TOut>.Fail(
                StepFailureReason.InvalidPayload, $"payload was not valid JSON — {exception.Message}", result.Value.Metadata);
        }
    }

    /// <summary>Applies the step's failure policy to a step that could not deliver.</summary>
    private static StepResult<TOut> Degrade<TIn, TOut>(
        IAgentStep<TIn, TOut> step,
        TIn input,
        StepFailureReason reason,
        string? error,
        int attempts,
        bool intervened,
        AiCallMetadata? metadata,
        Stopwatch clock)
        where TIn : notnull
        where TOut : class
    {
        var definition = step.Definition;

        if (definition.FailurePolicy == AgentFailurePolicy.Fallback && step.Fallback(input) is { } fallback)
        {
            return new StepResult<TOut>(
                definition.Name, StepStatus.FellBack, fallback, reason, error, attempts, intervened, metadata, clock.Elapsed);
        }

        var status = definition.FailurePolicy == AgentFailurePolicy.Skip ? StepStatus.Skipped : StepStatus.Failed;
        return new StepResult<TOut>(
            definition.Name, status, null, reason, error, attempts, intervened, metadata, clock.Elapsed);
    }

    /// <summary>Waits for <paramref name="call"/> but never past <paramref name="token"/>: a gateway
    /// that ignores cancellation must still not hold the step past its deadline. The abandoned call's
    /// fault, if any, is observed so it cannot surface later as an unobserved task exception.</summary>
    private static async Task<T> WithDeadline<T>(Task<T> call, CancellationToken token)
    {
        try
        {
            return await call.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!call.IsCompleted)
        {
            _ = call.ContinueWith(
                static t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    private static Activity? StartSpan(AgentDefinition definition)
    {
        var activity = AgentTelemetry.Source.StartActivity($"agent {definition.Name}", ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        var context = RunContext.Current;
        activity.SetTag(AgentTelemetry.OperationName, "invoke_agent");
        activity.SetTag(AgentTelemetry.AgentName, definition.Name);
        activity.SetTag(AgentTelemetry.AgentVersion, definition.Version);
        activity.SetTag(AgentTelemetry.RunId, context?.RunId);
        activity.SetTag(AgentTelemetry.TurnId, context?.TurnId);
        activity.SetTag(AgentTelemetry.StepName, definition.Name);
        return activity;
    }

    private static void Annotate<TOut>(Activity? activity, StepResult<TOut> result)
        where TOut : class
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(AgentTelemetry.Outcome, result.Status switch
        {
            StepStatus.Succeeded => AgentTelemetry.OutcomeOk,
            StepStatus.FellBack => AgentTelemetry.OutcomeFellBack,
            StepStatus.Skipped => AgentTelemetry.OutcomeSkipped,
            _ => AgentTelemetry.OutcomeError,
        });
        activity.SetTag(AgentTelemetry.StepStatus, result.Status.ToString());
        activity.SetTag(AgentTelemetry.StepReason, result.Reason.ToString());
        activity.SetTag(AgentTelemetry.StepAttempts, result.Attempts);
        activity.SetTag(AgentTelemetry.VerifierIntervened, result.VerifierIntervened);
        activity.SetTag(AgentTelemetry.LatencyMs, (long)result.Elapsed.TotalMilliseconds);
        if (result.Status == StepStatus.Failed)
        {
            activity.SetStatus(ActivityStatusCode.Error);
        }
    }

    private static void RequireRole(AgentDefinition definition, AgentRole expected)
    {
        if (definition.Role != expected)
        {
            throw new ArgumentException(
                $"Agent '{definition.Name}' has role {definition.Role} but was run as {expected}.", nameof(definition));
        }
    }

    /// <summary>One gateway call's outcome before verification.</summary>
    private readonly record struct Attempt<TOut>(
        TOut? Value, AiCallMetadata? Metadata, StepFailureReason Reason, string? Error)
        where TOut : class
    {
        public static Attempt<TOut> Ok(TOut value, AiCallMetadata metadata) =>
            new(value, metadata, StepFailureReason.None, null);

        public static Attempt<TOut> Fail(StepFailureReason reason, string? error, AiCallMetadata? metadata = null) =>
            new(null, metadata, reason, error);
    }
}
