using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Documents.Contracts.Infrastructure.Extraction;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Implements task E02/F01/US02/T01 (us-02-staged-extraction): a staged, schema-constrained
/// extraction pipeline over an already-parsed document.
///
/// <b>AC-1</b> ("staged: metadata -&gt; commercial terms -&gt; dates -&gt; price/SKU -&gt; clauses
/// -&gt; obligations -&gt; risk"): <see cref="RunAsync"/> runs exactly those seven
/// <see cref="ExtractionStage"/> values, each as its own <see cref="IAiGateway.ExtractAsync"/>
/// call against its own schema (spec §7.2 "avoid one giant prompt; split extraction into
/// bounded, schema-constrained tasks") and its own <see cref="ExtractionJob"/> row — one stage's
/// failure is recorded on that stage's job and does not abort the remaining stages (mirrors
/// ADR-017's "fail visibly, never silently truncate", generalized from OCR page-budget to any
/// stage of this pipeline). <b>NW-106:</b> "in that order" names the pipeline's conceptual/
/// display order, not its execution order — the seven calls are independent reads over the same
/// text (none depends on another's result), so <c>RunAsync</c> fires all seven at
/// once instead of one after another (the single largest, purely structural latency in this
/// pipeline before this fix); <see cref="ApplyStageResultAsync"/> still awaits and persists them
/// strictly one at a time, in pipeline order, because <see cref="DocumentsContractsDbContext"/>
/// is not safe for concurrent use.
///
/// <b>AC-2</b> ("every extracted fact carries source span + confidence"): every persisted fact
/// carries <c>SourceSpan</c>/<c>SourcePage</c>/<c>Confidence</c> — directly on
/// <see cref="ContractLineItem"/>/<see cref="Clause"/>/<see cref="Obligation"/>/<see cref="Risk"/>
/// rows (one row = one fact), or via a new <see cref="ExtractionEvidence"/> row per field for the
/// scalar-field stages that mutate <see cref="Contract"/> directly (Metadata, CommercialTerms,
/// DatesAndRenewalTerms) — see <see cref="ExtractionEvidence"/>'s own doc comment for why those
/// three stages need a side table and the other four do not.
///
/// <b>AC-3</b> ("hybrid parse: native text... OCR...") is satisfied jointly with task
/// E02/F01/US02/T02 (hybrid-ocr): this service does not read document bytes at all — it takes
/// already-parsed, page-mapped text (<see cref="DocumentPageText"/>) as input, so it does not
/// know or care whether a given page's text came from native parsing or OCR. That parsing step
/// is task T02's own coding objective ("hybrid OCR pre-pass behind gateway"); this task's own
/// architecture decisions in force (ADR-004, ADR-002) do not name ADR-017, unlike T02's.
///
/// Ensures a <see cref="Contract"/> exists for the document before staging into it
/// (<c>ExtractionRunStore.EnsureContractAsync</c>) — nothing else in this wave consumes the
/// <see cref="ExtractionStage.Classification"/> job <c>DocumentUploadService</c> queues at
/// upload, so without this the pipeline would have nothing to extract into. This is a
/// deliberate, documented scope decision by this task (not silently absorbed): see
/// <c>ExtractionRunStore.EnsureContractAsync</c>'s own doc comment for why it is not promoted to
/// reports/open-questions.md (same reasoning <c>Raffa.Api/Program.cs</c>'s X-Tenant-Id comment
/// already gives for a mid-wave append risking the phase-barrier merge).
/// </summary>
public sealed class StagedExtractionService(
    IExtractionRunStore runStore,
    IAiGateway aiGateway,
    ITenantContext tenantContext,
    IExtractionHangWatch? hangWatch = null,
    ExtractionProgressHeartbeat? progressHeartbeat = null)
{
    /// <summary>Mixed into <see cref="ExtractionJob.InputHash"/>: bump it when the stage set or schemas
    /// change so that an old checkpoint is never reused.</summary>
    internal const string PipelineVersion = "staged-v1";

    private static IReadOnlyList<ExtractionStage> PipelineStages => ExtractionPipeline.Stages;

    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Convenience constructor (tests, direct callers): builds the EF-backed store over
    /// <paramref name="dbContext"/>.</summary>
    public StagedExtractionService(
        DocumentsContractsDbContext dbContext,
        IAiGateway aiGateway,
        ITenantContext tenantContext,
        IClock clock,
        IAuditWriter auditWriter,
        IExtractionHangWatch? hangWatch = null,
        ExtractionProgressHeartbeat? progressHeartbeat = null)
        : this(
            new ExtractionRunStore(dbContext, tenantContext, clock, auditWriter),
            aiGateway,
            tenantContext,
            hangWatch,
            progressHeartbeat)
    {
    }

    public Task<Result<StagedExtractionSummary>> RunAsync(
        TenantId tenantId,
        EntityId documentId,
        IReadOnlyList<DocumentPageText> pages,
        CancellationToken cancellationToken = default) =>
        RunAsync(tenantId, documentId, pages, classificationConfidence: null, cancellationToken);

    /// <summary>
    /// Runs the seven stages; <paramref name="classificationConfidence"/>, when the caller has one
    /// (the admission gate's or <c>DocumentProcessingOrchestrator</c>'s own `classify` verdict for
    /// this document), is recorded as the <see cref="ExtractionFieldNames.Type"/> evidence row so the review
    /// screen can show a real confidence for "Contract type" — and a classification below
    /// <see cref="ExtractionConfidencePolicy.AutoAcceptThreshold"/> routes the document to review like any other weak fact.
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
            // ADR-017: "over-budget jobs fail visibly... they are not silently truncated" —
            // generalized here to "no readable text at all" for the same reason: an empty
            // pipeline run that reports success would look like "processed, nothing found"
            // rather than the true "could not read this document" (spec principle: source
            // evidence is mandatory).
            return Result<StagedExtractionSummary>.Failure(
                "Staged extraction requires at least one page of document text.");
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var documentText = BuildPageMarkedText(pages);

        var opened = await runStore
            .OpenAsync(
                new ExtractionRunRequest(tenantId, documentId, ComputeInputHash(documentText), pages.Count, classificationConfidence),
                cancellationToken)
            .ConfigureAwait(false);

        if (opened.IsFailure)
        {
            return Result<StagedExtractionSummary>.Failure(opened.Error);
        }

        var run = opened.Value;

        // NW-106: the seven stages are independent reads over the same text, so their gateway calls are
        // started together while every store write below stays sequential.
        // F5-T02: on a resume only the stages without a finished checkpoint get a job and a call.
        hangWatch?.Heartbeat();
        await runStore.StartStagesAsync(run, run.StagesToRun, cancellationToken).ConfigureAwait(false);

        var pending = run.StagesToRun.ToDictionary(
            stage => stage,
            stage => aiGateway.ExtractAsync(
                new AiExtractionRequest(stage.ToString(), documentText, BuildSchema(stage)), cancellationToken));
        var stageResults = new List<StagedExtractionStageResult>(PipelineStages.Count);

        using (progressHeartbeat?.BeginMemoryPulses())
        {
            foreach (var stage in PipelineStages)
            {
                stageResults.Add(
                    run.ReusedStages.Contains(stage)
                        ? await runStore.ReuseCheckpointAsync(run, stage, cancellationToken).ConfigureAwait(false)
                        : await ApplyStageResultAsync(run, stage, pending[stage], cancellationToken).ConfigureAwait(false));
            }
        }

        return Result<StagedExtractionSummary>.Success(
            await runStore.CloseAsync(run, stageResults, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Awaits one stage's in-flight extraction call, parses its payload and hands the typed facts to the
    /// store. F5-T02: a failure is typed and recorded on the job; the caller turns any failed stage into a
    /// partial document.
    /// </summary>
    private async Task<StagedExtractionStageResult> ApplyStageResultAsync(
        ExtractionRun run, ExtractionStage stage, Task<Result<AiExtractionResult>> call, CancellationToken cancellationToken)
    {
        Result<AiExtractionResult> extractResult;
        try
        {
            extractResult = await call.ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransientTransportFault(exception, cancellationToken))
        {
            // A network-level fault that escaped the gateway's own retries: the same "could not be
            // reached" a returned AiGatewayErrors.UnavailablePrefix failure means, so the stage is
            // recorded as failed-transient instead of taking the whole run down.
            extractResult = Result<AiExtractionResult>.Failure(
                $"{AiGatewayErrors.UnavailablePrefix} {exception.GetType().Name}: {exception.Message}");
        }

        if (extractResult.IsFailure)
        {
            return await runStore
                .FailAsync(run, stage, extractResult.Error, ClassifyFailure(extractResult.Error), cancellationToken)
                .ConfigureAwait(false);
        }

        var modelId = extractResult.Value.Metadata.ModelId;
        var payloadJson = extractResult.Value.PayloadJson;

        StageFacts facts;

        try
        {
            facts = ParseFacts(stage, payloadJson);
        }
        catch (JsonException ex)
        {
            // The gateway does not validate the model's output against the schema it was given
            // (IAiGateway.ExtractAsync's own doc comment): a real model can still return invalid JSON.
            // One malformed stage must not crash the other six. Parsing happens before anything is
            // staged for this stage.
            return await runStore
                .FailAsync(
                    run, stage, $"Malformed extraction payload: {ex.Message}", ExtractionStageFailureKind.Permanent,
                    cancellationToken, modelId)
                .ConfigureAwait(false);
        }

        return await runStore.ApplyAsync(run, stage, facts, modelId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deserialises one stage's payload into its typed facts. Throws <see cref="JsonException"/>
    /// on malformed JSON; an empty object becomes an empty fact list.</summary>
    internal static StageFacts ParseFacts(ExtractionStage stage, string payloadJson) => stage switch
    {
        ExtractionStage.Metadata or ExtractionStage.CommercialTerms or ExtractionStage.DatesAndRenewalTerms =>
            new StageFacts.Scalars(
                stage,
                JsonSerializer.Deserialize<ExtractedFactsPayload>(payloadJson, PayloadSerializerOptions)?.Facts ?? []),
        ExtractionStage.LineItems =>
            new StageFacts.LineItems(
                JsonSerializer.Deserialize<ExtractedLineItemsPayload>(payloadJson, PayloadSerializerOptions)?.Items ?? []),
        ExtractionStage.LegalClauses =>
            new StageFacts.Clauses(
                JsonSerializer.Deserialize<ExtractedClausesPayload>(payloadJson, PayloadSerializerOptions)?.Items ?? []),
        ExtractionStage.Obligations =>
            new StageFacts.Obligations(
                JsonSerializer.Deserialize<ExtractedObligationsPayload>(payloadJson, PayloadSerializerOptions)?.Items ?? []),
        ExtractionStage.Risk =>
            new StageFacts.Risks(
                JsonSerializer.Deserialize<ExtractedRisksPayload>(payloadJson, PayloadSerializerOptions)?.Items ?? []),
        _ => throw new InvalidOperationException($"Stage {stage} is not part of the staged extraction pipeline."),
    };

    private static string BuildSchema(ExtractionStage stage) => stage switch
    {
        ExtractionStage.Metadata => StagedExtractionJsonSchemas.Facts(ExtractionStageFields.Metadata),
        ExtractionStage.CommercialTerms => StagedExtractionJsonSchemas.Facts(ExtractionStageFields.CommercialTerms),
        ExtractionStage.DatesAndRenewalTerms => StagedExtractionJsonSchemas.Facts(ExtractionStageFields.DatesAndRenewalTerms),
        ExtractionStage.LineItems => StagedExtractionJsonSchemas.LineItems(),
        ExtractionStage.LegalClauses => StagedExtractionJsonSchemas.Clauses(),
        ExtractionStage.Obligations => StagedExtractionJsonSchemas.Obligations(),
        ExtractionStage.Risk => StagedExtractionJsonSchemas.Risks(),
        _ => throw new InvalidOperationException($"Stage {stage} is not part of the staged extraction pipeline."),
    };

    /// <summary>Concatenates every page's text with an explicit <c>[[PAGE n]]</c> marker (ADR-017
    /// "Implications for the decomposition": "must persist a page map so evidence source.page /
    /// section still resolve") so a structured-output model can report which page a fact came
    /// from by reading these markers, without this pipeline needing to run a separate call per
    /// page.</summary>
    internal static string BuildPageMarkedText(IReadOnlyList<DocumentPageText> pages)
    {
        var builder = new StringBuilder();

        foreach (var page in pages)
        {
            builder.Append("[[PAGE ").Append(page.PageNumber).Append("]]\n");
            builder.Append(page.Text);
            builder.Append("\n\n");
        }

        return builder.ToString();
    }

    internal static string ComputeInputHash(string documentText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PipelineVersion + "\n" + documentText)));

    /// <summary>A gateway failure meaning "the provider could not be reached or kept throttling"
    /// (<see cref="AiGatewayErrors.UnavailablePrefix"/>) is transient; anything else (empty text, a
    /// rejected request, a payload that does not parse) is a property of this input.</summary>
    internal static ExtractionStageFailureKind ClassifyFailure(string error) =>
        error.StartsWith(AiGatewayErrors.UnavailablePrefix, StringComparison.Ordinal)
            ? ExtractionStageFailureKind.Transient
            : ExtractionStageFailureKind.Permanent;

    /// <summary>A network-level fault thrown (not returned) by a gateway call. A cancellation the caller
    /// asked for is never one.</summary>
    private static bool IsTransientTransportFault(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or TimeoutException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}
