using Microsoft.Extensions.Logging;
using Raffa.AiFlows.DocumentExtraction.Stages;
using Raffa.AiFlows.DocumentExtraction.Validation;
using Raffa.AiFlows.DocumentExtraction.Verification;
using Raffa.AiFlows.Shared.Parsing;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Telemetry;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.DocumentExtraction.Orchestration;

/// <summary>
/// Implements task E02/F01/US02/T01 (us-02-staged-extraction): a staged, schema-constrained extraction
/// run over an already-parsed document. It owns the AI side of the run and no persistence: the page-marked
/// text and its input hash, the gateway calls (one <see cref="IExtractionStage"/> each), the parsing of the
/// model's payloads into typed facts and the failure classification. Everything that touches the database
/// goes through <see cref="IExtractionRunStore"/>.
///
/// <b>AC-1</b> ("staged: metadata -&gt; commercial terms -&gt; dates -&gt; price/SKU -&gt; clauses -&gt;
/// obligations -&gt; risk"): <see cref="RunAsync(TenantId, EntityId, IReadOnlyList{DocumentPageText}, double?, CancellationToken)"/>
/// runs exactly the <see cref="ExtractionPipeline.Stages"/>, each as its own
/// <see cref="IAiGateway.ExtractAsync"/> call against its own schema (spec §7.2 "avoid one giant prompt;
/// split extraction into bounded, schema-constrained tasks") and its own <c>ExtractionJob</c> row. One
/// stage's failure is recorded on that stage's job and does not abort the remaining stages (ADR-017's
/// "fail visibly, never silently truncate"). <b>NW-106:</b> "in that order" names the conceptual/display
/// order, not the execution order: by default the calls are independent reads over the same text, so all of
/// them are fired at once (F5-T05: <see cref="ExtractionOrchestrationOptions"/> can cap the concurrency, make
/// the list stages wait for the scalar stages and receive their facts, and retry transient failures in run;
/// see <see cref="StageScheduler"/>); the store writes are awaited and applied strictly one at a time, in pipeline order,
/// because the DbContext behind the store is not safe for concurrent use.
///
/// <b>AC-2</b> ("every extracted fact carries source span + confidence") is a property of the schemas and
/// of the store, which persists each fact with its <c>SourceSpan</c>/<c>SourcePage</c>/<c>Confidence</c>.
///
/// <b>AC-3</b> ("hybrid parse") is satisfied jointly with the hybrid parsing step: this class takes
/// already-parsed, page-mapped text (<see cref="DocumentPageText"/>) and does not know whether a page came
/// from native parsing or OCR.
///
/// A missing or foreign tenant scope is a programming error: the run opens its own scope (ADR-009) and the
/// store refuses to touch tenant rows without it, by throwing.
/// </summary>
public sealed class ExtractionOrchestrator
{
    private readonly IExtractionRunStore _runStore;
    private readonly IAiGateway _aiGateway;
    private readonly IReadOnlyDictionary<ExtractionStage, IExtractionStage> _stages;
    private readonly ITenantContext _tenantContext;
    private readonly StageScheduler _scheduler;
    private readonly IExtractionHangWatch? _hangWatch;
    private readonly ExtractionProgressHeartbeat? _progressHeartbeat;
    private readonly ILogger<ExtractionOrchestrator>? _logger;

    public ExtractionOrchestrator(
        IExtractionRunStore runStore,
        IAiGateway aiGateway,
        IEnumerable<IExtractionStage> stages,
        ITenantContext tenantContext,
        ExtractionOrchestrationOptions? options = null,
        IExtractionHangWatch? hangWatch = null,
        ExtractionProgressHeartbeat? progressHeartbeat = null,
        ILogger<ExtractionOrchestrator>? logger = null,
        StageTpmLimit? tpmLimit = null)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(aiGateway);
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _runStore = runStore;
        _aiGateway = aiGateway;
        _tenantContext = tenantContext;
        _hangWatch = hangWatch;
        _progressHeartbeat = progressHeartbeat;
        _logger = logger;
        Options = options ?? new ExtractionOrchestrationOptions();

        var byStage = new Dictionary<ExtractionStage, IExtractionStage>();
        foreach (var stage in stages)
        {
            if (!byStage.TryAdd(stage.Stage, stage))
            {
                throw new InvalidOperationException($"Extraction stage {stage.Stage} is registered more than once.");
            }
        }

        foreach (var required in ExtractionPipeline.Stages)
        {
            if (!byStage.ContainsKey(required))
            {
                throw new InvalidOperationException($"No extraction stage is registered for {required}.");
            }
        }

        _stages = byStage;
        _scheduler = new StageScheduler(
            aiGateway, byStage, Options,
            tpmLimit?.Limiter
            ?? (Options.TokensPerMinuteLimit > 0 ? new TpmRateLimiter(Options.TokensPerMinuteLimit) : null));
    }

    /// <summary>The effective options (defaults when none were supplied).</summary>
    internal ExtractionOrchestrationOptions Options { get; }

    public Task<Result<StagedExtractionSummary>> RunAsync(
        TenantId tenantId,
        EntityId documentId,
        IReadOnlyList<DocumentPageText> pages,
        CancellationToken cancellationToken = default) =>
        RunAsync(tenantId, documentId, pages, classificationConfidence: null, cancellationToken);

    /// <summary>
    /// Runs the seven stages; <paramref name="classificationConfidence"/>, when the caller has one (the
    /// admission gate's or <c>DocumentProcessingOrchestrator</c>'s own `classify` verdict for this
    /// document), is recorded by the store as the <c>type</c> evidence row so the review screen can show a
    /// real confidence for "Contract type"; a classification below the auto-accept threshold routes the
    /// document to review like any other weak fact.
    /// </summary>
    public async Task<Result<StagedExtractionSummary>> RunAsync(
        TenantId tenantId,
        EntityId documentId,
        IReadOnlyList<DocumentPageText> pages,
        double? classificationConfidence,
        CancellationToken cancellationToken = default)
    {
        if (pages.Count == 0)
        {
            // ADR-017: "over-budget jobs fail visibly... they are not silently truncated" — generalized
            // here to "no readable text at all": an empty run that reports success would look like
            // "processed, nothing found" rather than the true "could not read this document".
            return Result<StagedExtractionSummary>.Failure(
                "Staged extraction requires at least one page of document text.");
        }

        using var tenantScope = _tenantContext.BeginScope(tenantId);

        var documentText = PageMarkedText.Build(pages);

        var opened = await _runStore
            .OpenAsync(
                new ExtractionRunRequest(
                    tenantId, documentId, ExtractionInputHash.Compute(documentText), pages.Count, classificationConfidence,
                    Options.UseContextDependencies ? _scheduler.ContextualStages : null),
                cancellationToken)
            .ConfigureAwait(false);

        if (opened.IsFailure)
        {
            return Result<StagedExtractionSummary>.Failure(opened.Error);
        }

        var run = opened.Value;

        // Telemetry: the ai.* audit rows of this run carry run=<id> (additive; an enclosing run is kept).
        using var runScope = RunContext.Current?.RunId is null ? RunContext.BeginRun(run.RunId.ToString("N")) : null;

        // NW-106: the stages are independent reads over the same text, so their gateway calls are started
        // together while every store write below stays sequential.
        // F5-T02: on a resume only the stages without a finished checkpoint get a job and a call.
        _hangWatch?.Heartbeat();
        await _runStore.StartStagesAsync(run, run.StagesToRun, cancellationToken).ConfigureAwait(false);

        // F5-T05: on a resume with context dependencies on, the stages this run does not execute are not
        // pending tasks, so their facts come from the Contract. Read before any call starts: the store is
        // not safe for concurrent use and the apply loop below starts as soon as the first call returns.
        var resumeContext = EmptyContext;
        if (Options.UseContextDependencies && run.IsResume)
        {
            var reusedDependencies = ExtractionPipeline.Stages
                .Where(run.ReusedStages.Contains)
                .Where(stage => run.StagesToRun.Any(toRun => _stages[toRun].DependsOn.Contains(stage)))
                .ToList();
            if (reusedDependencies.Count > 0)
            {
                resumeContext = await _runStore
                    .ReadScalarContextAsync(run, reusedDependencies, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // F5-T07: built at most once per run, on first use, from the same pages the model read.
        var spanVerifier = Options.SpanVerification == SpanVerificationMode.Off
            ? null
            : new Lazy<SpanVerifier>(() => new SpanVerifier(pages, Options.SpanFuzzyThreshold));

        var pending = _scheduler.Start(run.StagesToRun, documentText, pages.Count, resumeContext, cancellationToken);
        var stageResults = new List<StagedExtractionStageResult>(ExtractionPipeline.Stages.Count);

        // F5-D19: with the cross-stage rules on, the scalar stages are awaited and parsed up front (nothing is
        // applied yet) so the rules see them all before the store applies any. Off: untouched, today's flow.
        var resolved = new Dictionary<ExtractionStage, ResolvedStage>();
        if (Options.CrossStageValidation != ValidationMode.Off)
        {
            await ValidateScalarStagesAsync(run, pending, resolved, spanVerifier).ConfigureAwait(false);
        }

        using (_progressHeartbeat?.BeginMemoryPulses())
        {
            foreach (var stage in ExtractionPipeline.Stages)
            {
                stageResults.Add(
                    run.ReusedStages.Contains(stage)
                        ? await _runStore.ReuseCheckpointAsync(run, stage, cancellationToken).ConfigureAwait(false)
                        : await ApplyStageResultAsync(run, _stages[stage], pending[stage], resolved.GetValueOrDefault(stage), spanVerifier, cancellationToken).ConfigureAwait(false));
            }
        }

        return Result<StagedExtractionSummary>.Success(
            await _runStore.CloseAsync(run, stageResults, cancellationToken).ConfigureAwait(false));
    }

    private static readonly IReadOnlyDictionary<ExtractionStage, StageFacts> EmptyContext =
        new Dictionary<ExtractionStage, StageFacts>();

    /// <summary>
    /// Awaits one stage's in-flight extraction call, parses its payload and hands the typed facts to the
    /// store. F5-T02: a failure is typed and recorded on the job; the caller turns any failed stage into a
    /// partial document.
    /// </summary>
    private async Task<StagedExtractionStageResult> ApplyStageResultAsync(
        ExtractionRun run,
        IExtractionStage stage,
        Task<StageCall> call,
        ResolvedStage? alreadyResolved,
        Lazy<SpanVerifier>? spanVerifier,
        CancellationToken cancellationToken)
    {
        var resolved = alreadyResolved ?? await ResolveStageAsync(stage, call, spanVerifier).ConfigureAwait(false);
        var stageCall = resolved.Call;
        var extractResult = stageCall.Result;

        // F5-T04: part of a very long document was not read. Visible on the run, the audit row, the summary,
        // the log and a metric; the store never completes such a document.
        if (stageCall.Truncated)
        {
            run.MarkTruncated();
            ExtractionMetrics.TruncatedStages.Add(1);
            _logger?.LogWarning(
                "Stage {Stage} of run {RunId} read only the first {MaxChunks} chunks of the document; marked truncated.",
                stage.Stage, run.RunId, Options.MaxChunks);
        }

        // F5-T05 (Q2): with context dependencies on, the stage's input hash is that of the request text it
        // actually sent (context included), so a resume never mixes modes. Off: never touched, the job keeps
        // SHA256("staged-v1\n" + text) exactly. Sequential with every other store call: the apply loop is.
        if (Options.UseContextDependencies)
        {
            var stageHash = ExtractionInputHash.Compute(stageCall.RequestText);
            if (!string.Equals(stageHash, run.InputHash, StringComparison.Ordinal))
            {
                _runStore.SetStageInputHash(run, stage.Stage, stageHash);
            }
        }

        if (extractResult.IsFailure)
        {
            return await _runStore
                .FailAsync(run, stage.Stage, extractResult.Error, ClassifyFailure(extractResult.Error), cancellationToken)
                .ConfigureAwait(false);
        }

        if (resolved.Facts is null)
        {
            return await _runStore
                .FailAsync(
                    run, stage.Stage, resolved.MalformedMessage!, ExtractionStageFailureKind.Permanent,
                    cancellationToken, resolved.ModelId)
                .ConfigureAwait(false);
        }

        return await _runStore
            .ApplyAsync(run, stage.Stage, resolved.Facts, resolved.ModelId, cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record ResolvedStage(StageCall Call, StageFacts? Facts, string? ModelId, string? MalformedMessage);

    private static readonly ExtractionStage[] ScalarStages =
        [ExtractionStage.Metadata, ExtractionStage.CommercialTerms, ExtractionStage.DatesAndRenewalTerms];

    /// <summary>
    /// Awaits one stage's call, parses its payload and span-verifies the facts. Touches no store state, so it
    /// can run ahead of the apply loop.
    /// </summary>
    private async Task<ResolvedStage> ResolveStageAsync(IExtractionStage stage, Task<StageCall> call, Lazy<SpanVerifier>? spanVerifier)
    {
        var stageCall = await call.ConfigureAwait(false);
        if (stageCall.Result.IsFailure)
        {
            return new ResolvedStage(stageCall, null, null, null);
        }

        var modelId = stageCall.Result.Value.Metadata.ModelId;
        try
        {
            var facts = stage.Parse(stageCall.Result.Value.PayloadJson);
            return new ResolvedStage(stageCall, VerifySpans(stage.Stage, facts, spanVerifier), modelId, null);
        }
        catch (System.Text.Json.JsonException ex)
        {
            // The gateway does not validate the model's output against the schema it was given
            // (IAiGateway.ExtractAsync's own doc comment): a real model can still return invalid JSON.
            // One malformed stage must not crash the others. Parsing happens before anything is staged.
            return new ResolvedStage(stageCall, null, modelId, $"Malformed extraction payload: {ex.Message}");
        }
    }

    /// <summary>
    /// F5-D19: resolves the scalar stages this run executes, evaluates the cross-stage rules over what parsed,
    /// and, in Gate only, flags the involved facts for review. Shadow records counts, a log line and metrics
    /// and leaves the facts as they are. A fault in a rule never fails the run.
    /// </summary>
    private async Task ValidateScalarStagesAsync(
        ExtractionRun run,
        IReadOnlyDictionary<ExtractionStage, Task<StageCall>> pending,
        Dictionary<ExtractionStage, ResolvedStage> resolved,
        Lazy<SpanVerifier>? spanVerifier)
    {
        foreach (var stage in ScalarStages)
        {
            if (!run.ReusedStages.Contains(stage) && pending.TryGetValue(stage, out var call))
            {
                resolved[stage] = await ResolveStageAsync(_stages[stage], call, spanVerifier).ConfigureAwait(false);
            }
        }

        var mode = Options.CrossStageValidation;
        try
        {
            var scalars = resolved.Values.Select(r => r.Facts).OfType<StageFacts.Scalars>().ToList();
            var report = CrossStageValidator.Evaluate(scalars);
            CrossStageValidator.Record(report, mode);

            _logger?.LogInformation(
                "Cross-stage validation ({Mode}): {Outcomes}",
                mode,
                string.Join(", ", report.Rules.Select(r => $"{r.RuleId}={r.Evaluation.Status}")));
            foreach (var violation in report.Violations)
            {
                _logger?.LogInformation(
                    "Cross-stage violation {RuleId} on {Fields} ({Mode}): {Message}",
                    violation.RuleId, string.Join(",", violation.Fields), mode, violation.Message);
            }

            if (mode == ValidationMode.Gate && report.Violations.Count > 0)
            {
                foreach (var flagged in CrossStageValidator.ApplyGate(scalars, report))
                {
                    resolved[flagged.Stage] = resolved[flagged.Stage] with { Facts = flagged };
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Cross-stage validation failed for run {RunId}; the facts are applied unvalidated.", run.RunId);
        }
    }

    /// <summary>
    /// F5-T07: checks each fact's span against the document. Shadow counts and logs and hands back the very
    /// same facts; Gate hands back copies flagged <c>SpanVerified</c>. A verifier fault never fails the stage.
    /// </summary>
    private StageFacts VerifySpans(ExtractionStage stage, StageFacts facts, Lazy<SpanVerifier>? spanVerifier)
    {
        if (spanVerifier is null)
        {
            return facts;
        }

        try
        {
            var (verified, counts) = StageSpanCheck.Run(facts, spanVerifier.Value, Options.SpanVerification);
            _logger?.LogInformation(
                "Span verification ({Mode}) for stage {Stage}: found={Found} otherPage={OtherPage} notFound={NotFound} noSpan={NoSpan}",
                Options.SpanVerification, stage, counts.Found, counts.FoundOnOtherPage, counts.NotFound, counts.NoSpan);
            return verified;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Span verification failed for stage {Stage}; the facts are applied unverified.", stage);
            return facts;
        }
    }

    /// <summary>A gateway failure meaning "the provider could not be reached or kept throttling"
    /// (<see cref="AiGatewayErrors.UnavailablePrefix"/>) is transient; anything else (empty text, a
    /// rejected request, a payload that does not parse) is a property of this input.</summary>
    internal static ExtractionStageFailureKind ClassifyFailure(string error) =>
        error.StartsWith(AiGatewayErrors.UnavailablePrefix, StringComparison.Ordinal)
            ? ExtractionStageFailureKind.Transient
            : ExtractionStageFailureKind.Permanent;

    /// <summary>A network-level fault thrown (not returned) by a gateway call. A cancellation the caller
    /// asked for is never one.</summary>
    internal static bool IsTransientTransportFault(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or TimeoutException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}
