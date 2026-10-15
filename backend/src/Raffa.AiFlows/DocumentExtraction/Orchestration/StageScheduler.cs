using System.Text.Json;
using Raffa.AiFlows.DocumentExtraction.Stages;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Telemetry;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.DocumentExtraction.Orchestration;

/// <summary>What one stage's gateway call produced, and the exact request text it sent (context block
/// included), which is what the stage's <c>ExtractionJob.InputHash</c> is computed from when context
/// dependencies are on.</summary>
internal sealed record StageCall(Result<AiExtractionResult> Result, string RequestText, bool Truncated = false);

/// <summary>
/// F5-T05: starts the gateway calls of the stages a run has to execute and hands back one task per stage.
/// It owns only the AI side: no store access, so it can run while the orchestrator applies results.
/// <list type="bullet">
/// <item><b>Defaults</b> (<c>MaxConcurrentStages = 0</c>, <c>UseContextDependencies = false</c>,
/// <c>TransientRetriesPerStage = 0</c>): every call is started at once, in pipeline order, with the plain
/// request: exactly the NW-106 behaviour the pipeline always had.</item>
/// <item><b>Cap</b> (<c>MaxConcurrentStages &gt; 0</c>): a <see cref="SemaphoreSlim"/> around each gateway
/// call; start order is pipeline order.</item>
/// <item><b>Dependencies</b> (<c>UseContextDependencies</c>): a stage starts once every
/// <see cref="IExtractionStage.DependsOn"/> stage that is part of this run has completed; the dependency's
/// parsed facts (or, for a stage this run does not execute, the context rebuilt from the Contract) are
/// handed to <see cref="IExtractionStage.BuildRequest"/>. A failed or malformed dependency contributes
/// nothing and never stops the dependant (one stage's failure never aborts another). The slot is taken
/// only after the dependencies finished, so the cap cannot deadlock.</item>
/// <item><b>In-run retry</b> (<c>TransientRetriesPerStage</c>): a call that ends in a transient failure is
/// repeated immediately, up to that many times.</item>
/// </list>
/// </summary>
internal sealed class StageScheduler(
    IAiGateway aiGateway,
    IReadOnlyDictionary<ExtractionStage, IExtractionStage> stages,
    ExtractionOrchestrationOptions options,
    TpmRateLimiter? tpmLimiter = null)
{
    private static readonly IReadOnlyDictionary<ExtractionStage, StageFacts> NoContext =
        new Dictionary<ExtractionStage, StageFacts>();

    /// <summary>Stages whose request depends on other stages' output when context dependencies are on.</summary>
    public IReadOnlySet<ExtractionStage> ContextualStages =>
        options.UseContextDependencies
            ? stages.Values.Where(s => s.DependsOn.Count > 0).Select(s => s.Stage).ToHashSet()
            : new HashSet<ExtractionStage>();

    /// <summary>Starts the calls of <paramref name="toRun"/> (pipeline order). <paramref name="resumeContext"/>
    /// holds the context of dependency stages this run does not execute (resume only).</summary>
    public IReadOnlyDictionary<ExtractionStage, Task<StageCall>> Start(
        IReadOnlyList<ExtractionStage> toRun,
        string documentText,
        int pageCount,
        IReadOnlyDictionary<ExtractionStage, StageFacts> resumeContext,
        CancellationToken cancellationToken)
    {
        var gate = options.MaxConcurrentStages > 0 ? new SemaphoreSlim(options.MaxConcurrentStages) : null;
        var pending = new Dictionary<ExtractionStage, Task<StageCall>>();

        foreach (var stage in toRun)
        {
            var definition = stages[stage];

            if (!options.UseContextDependencies || definition.DependsOn.Count == 0)
            {
                pending[stage] = CallAsync(definition, new ExtractionStageInput(documentText, pageCount, NoContext), gate, cancellationToken);
                continue;
            }

            // Captured now, synchronously: the dictionary keeps growing while this stage waits.
            var dependencies = definition.DependsOn
                .Select(dependency => (Stage: dependency, Call: pending.GetValueOrDefault(dependency)))
                .ToList();
            pending[stage] = CallWithContextAsync(definition, dependencies, resumeContext, documentText, pageCount, gate, cancellationToken);
        }

        return pending;
    }

    private async Task<StageCall> CallWithContextAsync(
        IExtractionStage stage,
        List<(ExtractionStage Stage, Task<StageCall>? Call)> dependencies,
        IReadOnlyDictionary<ExtractionStage, StageFacts> resumeContext,
        string documentText,
        int pageCount,
        SemaphoreSlim? gate,
        CancellationToken cancellationToken)
    {
        var context = new Dictionary<ExtractionStage, StageFacts>();

        foreach (var (dependency, call) in dependencies)
        {
            if (call is null)
            {
                // Not executed by this run (reused checkpoint): what the Contract holds now.
                if (resumeContext.TryGetValue(dependency, out var stored))
                {
                    context[dependency] = stored;
                }

                continue;
            }

            StageCall finished;
            try
            {
                finished = await call.ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The dependency's own fault is surfaced when its result is applied; here it only means
                // "no context from it".
                continue;
            }

            if (finished.Result.IsFailure)
            {
                continue;
            }

            try
            {
                context[dependency] = stages[dependency].Parse(finished.Result.Value.PayloadJson);
            }
            catch (JsonException)
            {
                // Malformed payload: recorded by the apply step; no context from it.
            }
        }

        return await CallAsync(stage, new ExtractionStageInput(documentText, pageCount, context), gate, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>A-07: one shadow call of a <see cref="SecondOpinionStage"/>, paced and chunked like any stage call
    /// but outside the concurrency gate (it runs after the primary calls have been applied).</summary>
    public Task<StageCall> CallSecondOpinionAsync(
        SecondOpinionStage stage, string documentText, int pageCount, CancellationToken cancellationToken) =>
        CallAsync(stage, new ExtractionStageInput(documentText, pageCount, NoContext), gate: null, cancellationToken);

    /// <summary>One stage's gateway call (with the optional cap and in-run retry), labelled as a step of the
    /// run. A network-level fault thrown (not returned) by the gateway is the same "could not be reached" a
    /// returned <see cref="AiGatewayErrors.UnavailablePrefix"/> failure means, so it becomes a failed-transient
    /// result instead of taking the whole run down.</summary>
    private async Task<StageCall> CallAsync(
        IExtractionStage stage, ExtractionStageInput input, SemaphoreSlim? gate, CancellationToken cancellationToken)
    {
        using var step = RunContext.BeginStep(stage.StageName);

        if (options.ChunkThresholdChars > 0 && input.DocumentText.Length > options.ChunkThresholdChars)
        {
            return await CallChunkedAsync(stage, input, gate, cancellationToken).ConfigureAwait(false);
        }

        return await CallOnceAsync(stage, input, gate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>F5-T04 map-reduce: the stage runs over each chunk of the long document (one after the other,
    /// each call under the same cap, pacing and in-run retry), then the facts are merged into the single payload
    /// the stage would have returned. Any failed chunk fails the stage (visible, retryable) rather than
    /// applying a partial read; chunks dropped for exceeding <c>MaxChunks</c> mark the call truncated.</summary>
    private async Task<StageCall> CallChunkedAsync(
        IExtractionStage stage, ExtractionStageInput input, SemaphoreSlim? gate, CancellationToken cancellationToken)
    {
        var chunks = DocumentChunker.Split(input.DocumentText, options.ChunkSizeChars, options.MaxChunks);
        var parts = new List<StageFacts>(chunks.Texts.Count);
        var requestTexts = new List<string>(chunks.Texts.Count);
        AiCallMetadata? metadata = null;
        int prompt = 0, completion = 0;
        var anyUsage = false;

        foreach (var text in chunks.Texts)
        {
            var call = await CallOnceAsync(stage, input with { DocumentText = text }, gate, cancellationToken).ConfigureAwait(false);
            requestTexts.Add(call.RequestText);
            if (call.Result.IsFailure)
            {
                return new StageCall(call.Result, string.Join('\n', requestTexts), chunks.Truncated);
            }

            metadata ??= call.Result.Value.Metadata;
            if (call.Result.Value.Metadata.Usage is { } usage)
            {
                anyUsage = true;
                prompt += usage.PromptTokens;
                completion += usage.CompletionTokens;
            }

            try
            {
                parts.Add(stage.Parse(call.Result.Value.PayloadJson));
            }
            catch (JsonException)
            {
                // Malformed chunk payload: hand it to the apply step, which records the typed failure.
                return new StageCall(call.Result, string.Join('\n', requestTexts), chunks.Truncated);
            }
        }

        var merged = StageFactsMerger.ToPayloadJson(StageFactsMerger.Merge(parts));
        var combined = metadata! with { Usage = anyUsage ? new AiTokenUsage(prompt, completion) : null };
        return new StageCall(
            Result<AiExtractionResult>.Success(new AiExtractionResult(merged, combined)),
            string.Join('\n', requestTexts),
            chunks.Truncated);
    }

    private async Task<StageCall> CallOnceAsync(
        IExtractionStage stage, ExtractionStageInput input, SemaphoreSlim? gate, CancellationToken cancellationToken)
    {
        AiExtractionRequest? request = null;

        for (var attempt = 0; ; attempt++)
        {
            if (gate is not null)
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            Result<AiExtractionResult> result;
            var reserved = 0;
            var used = -1; // -1 = keep the estimate
            try
            {
                request ??= stage.BuildRequest(input);

                // F5-T10: wait for the TPM bucket (off by default = no wait at all).
                if (tpmLimiter is not null)
                {
                    reserved = await tpmLimiter.AcquireAsync(EstimateTokens(request), cancellationToken).ConfigureAwait(false);
                }

                result = await aiGateway.ExtractAsync(request, cancellationToken).ConfigureAwait(false);
                if (result.IsSuccess && result.Value.Metadata.Usage is { } usage)
                {
                    used = usage.PromptTokens + usage.CompletionTokens;
                }
            }
            catch (Exception exception) when (ExtractionOrchestrator.IsTransientTransportFault(exception, cancellationToken))
            {
                used = 0; // never reached the provider's accounting
                result = Result<AiExtractionResult>.Failure(
                    $"{AiGatewayErrors.UnavailablePrefix} {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                if (tpmLimiter is not null && reserved > 0)
                {
                    tpmLimiter.Settle(reserved, used < 0 ? reserved : used);
                }

                gate?.Release();
            }

            if (result.IsSuccess
                || attempt >= options.TransientRetriesPerStage
                || ExtractionOrchestrator.ClassifyFailure(result.Error) != ExtractionStageFailureKind.Transient)
            {
                return new StageCall(result, request?.DocumentText ?? input.DocumentText);
            }
        }
    }

    /// <summary>Rough input + reserved output tokens of one stage call, for the TPM bucket.</summary>
    private int EstimateTokens(AiExtractionRequest request)
    {
        var chars = request.DocumentText.Length + request.JsonSchema.Length;
        var perToken = Math.Max(1.0, options.TpmCharsPerToken);
        return (int)Math.Min(int.MaxValue / 2, Math.Ceiling(chars / perToken) + options.TpmEstimatedOutputTokens);
    }
}
