using Raffa.AiFlows.DocumentExtraction.Stages;
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
/// order, not the execution order: the calls are independent reads over the same text, so all of them are
/// fired at once; the store writes are awaited and applied strictly one at a time, in pipeline order,
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
    private readonly IExtractionHangWatch? _hangWatch;
    private readonly ExtractionProgressHeartbeat? _progressHeartbeat;

    public ExtractionOrchestrator(
        IExtractionRunStore runStore,
        IAiGateway aiGateway,
        IEnumerable<IExtractionStage> stages,
        ITenantContext tenantContext,
        ExtractionOrchestrationOptions? options = null,
        IExtractionHangWatch? hangWatch = null,
        ExtractionProgressHeartbeat? progressHeartbeat = null)
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
                    tenantId, documentId, ExtractionInputHash.Compute(documentText), pages.Count, classificationConfidence),
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

        var input = new ExtractionStageInput(documentText, pages.Count, EmptyContext);
        var pending = run.StagesToRun.ToDictionary(
            stage => stage,
            stage => CallStageAsync(_stages[stage], input, cancellationToken));
        var stageResults = new List<StagedExtractionStageResult>(ExtractionPipeline.Stages.Count);

        using (_progressHeartbeat?.BeginMemoryPulses())
        {
            foreach (var stage in ExtractionPipeline.Stages)
            {
                stageResults.Add(
                    run.ReusedStages.Contains(stage)
                        ? await _runStore.ReuseCheckpointAsync(run, stage, cancellationToken).ConfigureAwait(false)
                        : await ApplyStageResultAsync(run, _stages[stage], pending[stage], cancellationToken).ConfigureAwait(false));
            }
        }

        return Result<StagedExtractionSummary>.Success(
            await _runStore.CloseAsync(run, stageResults, cancellationToken).ConfigureAwait(false));
    }

    private static readonly IReadOnlyDictionary<ExtractionStage, StageFacts> EmptyContext =
        new Dictionary<ExtractionStage, StageFacts>();

    /// <summary>One stage's gateway call, labelled as a step of the run. A network-level fault thrown (not
    /// returned) by the gateway is the same "could not be reached" a returned
    /// <see cref="AiGatewayErrors.UnavailablePrefix"/> failure means, so it becomes a failed-transient result
    /// instead of taking the whole run down.</summary>
    private async Task<Result<AiExtractionResult>> CallStageAsync(
        IExtractionStage stage, ExtractionStageInput input, CancellationToken cancellationToken)
    {
        using var step = RunContext.BeginStep(stage.StageName);

        try
        {
            return await _aiGateway.ExtractAsync(stage.BuildRequest(input), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransientTransportFault(exception, cancellationToken))
        {
            return Result<AiExtractionResult>.Failure(
                $"{AiGatewayErrors.UnavailablePrefix} {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// Awaits one stage's in-flight extraction call, parses its payload and hands the typed facts to the
    /// store. F5-T02: a failure is typed and recorded on the job; the caller turns any failed stage into a
    /// partial document.
    /// </summary>
    private async Task<StagedExtractionStageResult> ApplyStageResultAsync(
        ExtractionRun run, IExtractionStage stage, Task<Result<AiExtractionResult>> call, CancellationToken cancellationToken)
    {
        var extractResult = await call.ConfigureAwait(false);

        if (extractResult.IsFailure)
        {
            return await _runStore
                .FailAsync(run, stage.Stage, extractResult.Error, ClassifyFailure(extractResult.Error), cancellationToken)
                .ConfigureAwait(false);
        }

        var modelId = extractResult.Value.Metadata.ModelId;

        StageFacts facts;

        try
        {
            facts = stage.Parse(extractResult.Value.PayloadJson);
        }
        catch (System.Text.Json.JsonException ex)
        {
            // The gateway does not validate the model's output against the schema it was given
            // (IAiGateway.ExtractAsync's own doc comment): a real model can still return invalid JSON.
            // One malformed stage must not crash the others. Parsing happens before anything is staged.
            return await _runStore
                .FailAsync(
                    run, stage.Stage, $"Malformed extraction payload: {ex.Message}", ExtractionStageFailureKind.Permanent,
                    cancellationToken, modelId)
                .ConfigureAwait(false);
        }

        return await _runStore.ApplyAsync(run, stage.Stage, facts, modelId, cancellationToken).ConfigureAwait(false);
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
