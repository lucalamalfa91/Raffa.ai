using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

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
/// text (none depends on another's result), so <see cref="StartStagesAsync"/> fires all seven at
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
/// (<see cref="EnsureContractAsync"/>) — nothing else in this wave consumes the
/// <see cref="ExtractionStage.Classification"/> job <c>DocumentUploadService</c> queues at
/// upload, so without this the pipeline would have nothing to extract into. This is a
/// deliberate, documented scope decision by this task (not silently absorbed): see
/// <see cref="EnsureContractAsync"/>'s own doc comment for why it is not promoted to
/// reports/open-questions.md (same reasoning <c>Raffa.Api/Program.cs</c>'s X-Tenant-Id comment
/// already gives for a mid-wave append risking the phase-barrier merge).
/// </summary>
public sealed partial class StagedExtractionService(
    DocumentsContractsDbContext dbContext,
    IAiGateway aiGateway,
    ITenantContext tenantContext,
    IClock clock,
    IAuditWriter auditWriter,
    IExtractionHangWatch? hangWatch = null,
    ExtractionProgressHeartbeat? progressHeartbeat = null)
{
    /// <summary>AC-1's seven stages, in pipeline order. <see cref="ExtractionStage.Classification"/>
    /// is deliberately excluded — it is queued and (eventually) consumed elsewhere, before this
    /// pipeline ever runs (see the type doc comment).</summary>
    private static readonly ExtractionStage[] PipelineStages =
    [
        ExtractionStage.Metadata,
        ExtractionStage.CommercialTerms,
        ExtractionStage.DatesAndRenewalTerms,
        ExtractionStage.LineItems,
        ExtractionStage.LegalClauses,
        ExtractionStage.Obligations,
        ExtractionStage.Risk,
    ];

    /// <summary>Field name of the `supplier` fact (requirements R-SUP-01): the supplier's legal
    /// name exactly as written in the document. Public because
    /// <see cref="DocumentProcessingPipeline"/> and the review surfaces address the resulting
    /// <see cref="ExtractionEvidence.FieldName"/> by this literal, and
    /// <c>ContractCorrectionService</c> accepts a human correction under the same name.</summary>
    public const string SupplierFieldName = "supplier";

    /// <summary>Field name of the classification's own evidence row (<see cref="Contract.Type"/>).
    /// Classification is the one extracted fact this pipeline does not produce itself — the
    /// admission gate / <see cref="DocumentProcessingPipeline"/> run the `classify` role — yet the
    /// review screen shows "Contract type" next to every other field and needs the same real
    /// confidence behind it. <see cref="RunAsync(TenantId, EntityId, IReadOnlyList{DocumentPageText}, double?, CancellationToken)"/>
    /// records it as an <see cref="ExtractionEvidence"/> row under this name, keyed exactly as
    /// <c>ContractCorrectionService</c> accepts a <c>type</c> correction.</summary>
    public const string TypeFieldName = "type";

    /// <summary><see cref="Contract"/> fields the `metadata` stage may propose (allow-listed
    /// both here and in the JSON Schema's <c>enum</c> — see <see cref="StagedExtractionJsonSchemas.Facts"/>).
    /// Deliberately excludes <see cref="Contract.Type"/>: that is Classification's field
    /// (<see cref="Document.DocumentType"/>), not this pipeline's.
    /// <see cref="SupplierFieldName"/> is here even though it maps to no <see cref="Contract"/>
    /// scalar — see <see cref="ApplyMetadataFact"/>.</summary>
    private static readonly string[] MetadataFields = [SupplierFieldName, "currency", "governingLaw", "status"];

    private static readonly string[] CommercialTermsFields =
        ["annualSpend", "totalContractValue", "paymentTerms"];

    private static readonly string[] DatesFields =
        ["startDate", "endDate", "effectiveDate", "cancellationDeadline", "autoRenewal", "renewalTermMonths", "noticePeriodDays"];

    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Recorded actor for the one audit entry this pipeline writes per document per
    /// extraction run (ADR-011 w17 clause 21): field names and confidence numbers, never a field
    /// value. The reserved non-human principal; there is no HTTP caller.</summary>
    private const string SystemActor = "system:extraction";

    /// <summary>Bootstrap-only placeholder <see cref="Contract.Status"/> for a contract shell
    /// this pipeline had to create (see <see cref="EnsureContractAsync"/>). Overwritten by the
    /// `metadata` stage's own "status" fact when the model reports one, or by a human correction
    /// — never presented as a real extracted value.</summary>
    private const string BootstrapContractStatus = "processing";

    /// <summary>Bootstrap-only placeholder <see cref="Contract.Currency"/> — see
    /// <see cref="BootstrapContractStatus"/>. <see cref="Contract.Currency"/> is a required
    /// (NOT NULL) column (contract-schema task, ADR-003); this pipeline cannot leave it unset
    /// even before the `metadata` stage has run.</summary>
    private const string BootstrapContractCurrency = "USD";

    public Task<Result<StagedExtractionSummary>> RunAsync(
        TenantId tenantId,
        EntityId documentId,
        IReadOnlyList<DocumentPageText> pages,
        CancellationToken cancellationToken = default) =>
        RunAsync(tenantId, documentId, pages, classificationConfidence: null, cancellationToken);

    /// <summary>
    /// Runs the seven stages; <paramref name="classificationConfidence"/>, when the caller has one
    /// (the admission gate's or <see cref="DocumentProcessingPipeline"/>'s own `classify` verdict for
    /// this document), is recorded as the <see cref="TypeFieldName"/> evidence row so the review
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

        var document = await dbContext.Documents
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return Result<StagedExtractionSummary>.Failure($"Document {documentId} was not found for this tenant.");
        }

        var now = clock.UtcNow;
        var contract = await EnsureContractAsync(tenantId, document, now, cancellationToken).ConfigureAwait(false);
        var runEvidence = new List<(string FieldName, double? Confidence)>();

        var documentText = BuildPageMarkedText(pages);
        var pageCount = pages.Count;
        var inputHash = ComputeInputHash(documentText);

        // F5-T01/F5-T02: decide whether this call starts a fresh run or continues an incomplete one
        // over the same text, and what a human already owns on this contract. Both are read before
        // anything is written.
        var plan = await PlanRunAsync(tenantId, document.Id, inputHash, now, cancellationToken).ConfigureAwait(false);
        var context = new RunContext(
            tenantId,
            document.Id,
            contract,
            plan,
            pageCount,
            await LoadProtectionAsync(tenantId, contract, cancellationToken).ConfigureAwait(false),
            await ContractHasNoOtherDocumentAsync(tenantId, contract.Id, document.Id, cancellationToken).ConfigureAwait(false),
            runEvidence);

        if (classificationConfidence is { } typeConfidence)
        {
            await RecordClassificationEvidenceAsync(context, document, typeConfidence, now, cancellationToken)
                .ConfigureAwait(false);
        }

        document.ProcessingStatus = DocumentProcessingStatus.Processing;

        // NW-106: the seven stages are independent reads over the same text (none depends on
        // another's result), but every one of them used to run its Foundry call strictly one
        // after another — on a frontier deployment a single stage can already take 100s+
        // (AiGatewayResilienceOptions's own doc comment), so seven in sequence was the single
        // largest, most avoidable cost in the whole pipeline. See StartStagesAsync's own doc
        // comment for why only the gateway calls (Phase 1) run concurrently while every
        // dbContext write (Phase 2, here) stays strictly sequential -- EF Core's DbContext is
        // not safe for concurrent use, and nothing about this fix requires it to be.
        //
        // F5-T02: on a resume only the stages without a finished checkpoint get a job and a
        // gateway call; every stage that already finished for this very text is reused as it is.
        var jobs = await CreateStageJobsAsync(tenantId, document.Id, plan, cancellationToken).ConfigureAwait(false);
        var extractTasks = StartStagesAsync(documentText, plan, cancellationToken);

        var stageResults = new List<StagedExtractionStageResult>(PipelineStages.Length);
        string? acceptedSupplierName = null;

        using (progressHeartbeat?.BeginMemoryPulses())
        {
            for (var i = 0; i < PipelineStages.Length; i++)
            {
                if (plan.Reused.TryGetValue(PipelineStages[i], out var checkpoint))
                {
                    var (reusedResult, reusedSupplierName) = await ReuseCheckpointAsync(context, checkpoint, cancellationToken)
                        .ConfigureAwait(false);
                    stageResults.Add(reusedResult);
                    acceptedSupplierName ??= reusedSupplierName;
                    continue;
                }

                var (stageResult, stageSupplierName) = await ApplyStageResultAsync(
                        context, PipelineStages[i], jobs[i]!, extractTasks[i]!, cancellationToken)
                    .ConfigureAwait(false);
                stageResults.Add(stageResult);

                // First accepted `supplier` fact wins — only the `metadata` stage can produce one
                // (MetadataFields), so this never silently picks between competing stages, in
                // whatever order their (independent) Foundry calls happen to settle.
                acceptedSupplierName ??= stageSupplierName;
            }
        }

        // F5-D08: cancellationDeadline = endDate - noticePeriodDays, computed here and not left to
        // the model's own arithmetic. It reads the evidence the Dates stage wrote, which the loop
        // above has already saved.
        await DeriveCancellationDeadlineAsync(context, cancellationToken).ConfigureAwait(false);

        OfficializeDerivedStatus(context, document, now);

        document.ProcessingStatus = DetermineDocumentStatus(stageResults, classificationConfidence);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // F5-T02: a stage that did not finish leaves the document partial, whatever the facts that
        // did arrive look like. "Review 0 fields" below is only ever about weak facts; it must never
        // turn a document that is missing a whole stage into Completed.
        var failedStages = stageResults.Where(s => s.Status == ExtractionJobStatus.Failed).ToList();

        // "Review 0 fields" is never a state to park a document in: a weak line item, clause,
        // obligation or risk can route the document to review without leaving a single field the
        // review screen can show. Counted after the save, on the same latest-evidence-per-field
        // rule the Documents row uses (NothingToReviewAutoValidator).
        if (document.ProcessingStatus == DocumentProcessingStatus.NeedsReview && failedStages.Count == 0)
        {
            var weakByContract = await NothingToReviewAutoValidator
                .WeakFactCountsAsync(dbContext, tenantId, [contract.Id], cancellationToken)
                .ConfigureAwait(false);

            if (weakByContract.GetValueOrDefault(contract.Id) == 0)
            {
                document.ProcessingStatus = DocumentProcessingStatus.Completed;
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await auditWriter.WriteAsync(
                new AuditEntry(
                    tenantId,
                    SystemActor,
                    $"document.extraction.{document.ProcessingStatus.ToString().ToLowerInvariant()}",
                    "document",
                    documentId.Value.ToString(),
                    now,
                    BuildExtractionAuditDetail(contract.Id, plan, runEvidence, failedStages)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<StagedExtractionSummary>.Success(
            new StagedExtractionSummary(contract.Id, document.ProcessingStatus, stageResults, acceptedSupplierName));
    }

    /// <summary>
    /// Finds the <see cref="Contract"/> this document already links to, or creates a minimal
    /// shell and links it. Nothing in this wave consumes the <see cref="ExtractionStage.Classification"/>
    /// job <c>DocumentUploadService</c> queues at upload (that consumer is a later, not-yet-built
    /// task — <c>Raffa.Worker.Queue.QueueConsumerHostedService</c>'s own doc comment says
    /// dispatching a received message to a domain handler "is a later task once that handler
    /// exists"), so without this method every document would arrive here with
    /// <see cref="Document.ContractId"/> still null and this pipeline would have nothing to
    /// stage facts into. Seeding <see cref="Contract.Type"/> from <see cref="Document.DocumentType"/>
    /// (defaulting to <see cref="ContractDocumentType.Other"/> pre-classification) and a
    /// placeholder status/currency (<see cref="BootstrapContractStatus"/>/
    /// <see cref="BootstrapContractCurrency"/>) is honest about "extraction ran before
    /// classification finished", not a silent guess presented as a real extracted fact — the
    /// `metadata` stage overwrites status/currency the moment it finds a real value.
    ///
    /// <para>
    /// When an existing bootstrap contract is found (created by <c>DocumentUploadService</c> at
    /// upload time with <see cref="ContractDocumentType.Other"/> as a placeholder), its
    /// <see cref="Contract.Type"/> is promoted to <see cref="Document.DocumentType"/> the moment
    /// classification has resolved a specific type. <see cref="DocumentProcessingPipeline"/> flushes
    /// <see cref="Document.DocumentType"/> via <c>SaveChangesAsync</c> <em>before</em> calling
    /// <see cref="RunAsync(TenantId, EntityId, IReadOnlyList{DocumentPageText}, double?, CancellationToken)"/>,
    /// so by the time this method runs the in-memory document already carries the real classified
    /// type and the promotion is safe. The promotion is guarded to only fire while the contract is
    /// still at the bootstrap placeholder (<c>Other</c>) and the document has been classified as a
    /// more specific type — this preserves any human correction a reviewer may already have applied.
    /// </para>
    /// </summary>
    private async Task<Contract> EnsureContractAsync(
        TenantId tenantId, Document document, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (document.ContractId is { } existingContractId)
        {
            var existing = await dbContext.Contracts
                .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == existingContractId, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                // Promote the upload-time bootstrap placeholder (Other) to the now-classified
                // type. document.DocumentType has been set by the pipeline's ClassifyAsync and
                // flushed before RunAsync was called, so it already reflects the real classified
                // type for this run. Only fires while the contract is still at the bootstrap
                // default — preserves any human correction a reviewer may already have applied.
                if (existing.Type == ContractDocumentType.Other
                    && document.DocumentType != ContractDocumentType.Other)
                {
                    existing.Type = document.DocumentType;
                }

                return existing;
            }

            // A dangling ContractId is unexpected (Contract rows are never deleted by this
            // module) but must not crash the pipeline — fall through and create a fresh shell
            // rather than leave the document permanently stuck with nothing to extract into.
        }

        var contract = new Contract
        {
            TenantId = tenantId,
            Type = document.DocumentType,
            Status = BootstrapContractStatus,
            Currency = BootstrapContractCurrency,
            CreatedAt = now,
        };

        dbContext.Contracts.Add(contract);
        document.ContractId = contract.Id;

        return contract;
    }

    /// <summary>Concatenates every page's text with an explicit <c>[[PAGE n]]</c> marker (ADR-017
    /// "Implications for the decomposition": "must persist a page map so evidence source.page /
    /// section still resolve") so a structured-output model can report which page a fact came
    /// from by reading these markers, without this pipeline needing to run a separate call per
    /// page.</summary>
    private static string BuildPageMarkedText(IReadOnlyList<DocumentPageText> pages)
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

    /// <summary>
    /// NW-106 phase 2: awaits one stage's already-in-flight extraction call and applies it --
    /// every dbContext write for this stage, exactly as the pre-NW-106 single-stage call used to
    /// do inline, now separated from the Foundry call itself (phase 1) so the awaiting-and-
    /// writing stays strictly sequential across stages while the actual network waits already
    /// overlapped in phase 1. Reports both the <see cref="StagedExtractionStageResult"/> and the
    /// `supplier` legal name this stage accepted, if any (see
    /// <see cref="StagedExtractionSummary.AcceptedSupplierName"/>) — the two travel together
    /// because whether the fact was accepted at all is decided here, against
    /// <see cref="ExtractionConfidencePolicy"/>, not by the caller re-reading evidence rows.
    ///
    /// <para>
    /// F5-T01: the stage's facts, the removal of the previous run's rows (list stages) and the job
    /// row are written by one <c>SaveChangesAsync</c> -- one transaction -- and only after the
    /// payload has parsed, so a stage that fails leaves what an earlier run stored untouched.
    /// F5-T02: a failure is typed (<see cref="ExtractionStageFailureKind"/>) and recorded on the
    /// job; the caller turns any failed stage into a partial document.
    /// </para>
    /// </summary>
    private async Task<(StagedExtractionStageResult Result, string? AcceptedSupplierName)> ApplyStageResultAsync(
        RunContext context,
        ExtractionStage stage,
        ExtractionJob job,
        Task<Result<AiExtractionResult>> extractTask,
        CancellationToken cancellationToken)
    {
        context.StageJobIds[stage] = job.Id;

        // Always set by CreateStageJobsAsync for every job this method is ever called with.
        var startedAt = job.StartedAt!.Value;

        Result<AiExtractionResult> extractResult;
        try
        {
            extractResult = await extractTask.ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransientTransportFault(exception, cancellationToken))
        {
            // A network-level fault that escaped the gateway's own retries: the same "could not be
            // reached" a returned AiGatewayErrors.UnavailablePrefix failure means, so the stage is
            // recorded as failed-transient instead of taking the whole run down.
            extractResult = Result<AiExtractionResult>.Failure(
                $"{AiGatewayErrors.UnavailablePrefix} {exception.GetType().Name}: {exception.Message}");
        }

        var completedAt = clock.UtcNow;

        if (extractResult.IsFailure)
        {
            return await FailStageAsync(
                    stage, job, extractResult.Error, ClassifyFailure(extractResult.Error), completedAt, cancellationToken)
                .ConfigureAwait(false);
        }

        job.ModelId = extractResult.Value.Metadata.ModelId;
        var payloadJson = extractResult.Value.PayloadJson;

        StageApplyResult applied;

        try
        {
            switch (stage)
            {
                case ExtractionStage.Metadata:
                    applied = ApplyFacts(context, job.Id, payloadJson, MetadataFields, startedAt, ApplyMetadataFact);
                    break;
                case ExtractionStage.CommercialTerms:
                    applied = ApplyFacts(context, job.Id, payloadJson, CommercialTermsFields, startedAt, ApplyCommercialTermsFact);
                    break;
                case ExtractionStage.DatesAndRenewalTerms:
                    applied = ApplyFacts(context, job.Id, payloadJson, DatesFields, startedAt, ApplyDatesFact);
                    break;
                case ExtractionStage.LineItems:
                {
                    var rows = new List<ContractLineItem>();
                    applied = ApplyLineItems(context, payloadJson, startedAt, rows);
                    await ReplaceLineItemsAsync(context, rows, cancellationToken).ConfigureAwait(false);
                    break;
                }

                case ExtractionStage.LegalClauses:
                {
                    var rows = new List<Clause>();
                    applied = ApplyClauses(context, payloadJson, startedAt, rows);
                    await ReplaceClausesAsync(context, rows, cancellationToken).ConfigureAwait(false);
                    break;
                }

                case ExtractionStage.Obligations:
                {
                    var rows = new List<Obligation>();
                    applied = ApplyObligations(context, payloadJson, startedAt, rows);
                    await ReplaceObligationsAsync(context, rows, cancellationToken).ConfigureAwait(false);
                    break;
                }

                case ExtractionStage.Risk:
                {
                    var rows = new List<Risk>();
                    applied = ApplyRisks(context, payloadJson, startedAt, rows);
                    await ReplaceRisksAsync(context, rows, cancellationToken).ConfigureAwait(false);
                    break;
                }

                default:
                    throw new InvalidOperationException($"Stage {stage} is not part of the staged extraction pipeline.");
            }
        }
        catch (JsonException ex)
        {
            // The gateway does not validate the model's output against the schema it was given
            // (IAiGateway.ExtractAsync's own doc comment) — a real (non-fixture) model can still
            // return syntactically invalid JSON. One malformed stage must not crash the other six.
            // Nothing was staged for this stage yet (parsing is the first thing every Apply does),
            // so the save below writes the failed job and nothing else.
            return await FailStageAsync(
                    stage, job, $"Malformed extraction payload: {ex.Message}", ExtractionStageFailureKind.Permanent,
                    completedAt, cancellationToken)
                .ConfigureAwait(false);
        }

        // Human-in-the-loop principle, as narrowed on 2026-09-21 (ADR-024 amendment of that
        // date): a stage needs review exactly when it produced a fact below
        // ExtractionConfidencePolicy — something a reviewer can actually decide on. A stage that
        // found nothing (every fact absent, or an empty list) is a legitimate answer, not a review
        // trigger: "nothing to review" is not a state the review screen can resolve, and parking
        // the document there only asked the user to sign off on an empty list ("Review 0 fields").
        // An absent scalar field still surfaces on the review screen as "Not found in the
        // document", where the user may type it; it never blocks validation. A fact naming a
        // field outside the stage's allow-list is counted in Skipped for the audit detail but is
        // not a review trigger either — strict structured output makes it unreachable in practice.
        job.Status = applied.AnyBelowThreshold
            ? ExtractionJobStatus.NeedsReview
            : ExtractionJobStatus.Completed;
        job.CompletedAt = completedAt;
        job.ErrorDetail = null;
        job.FailureKind = null;
        job.ExtractedCount = applied.Extracted;
        job.SkippedCount = applied.Skipped;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (
            new StagedExtractionStageResult(stage, job.Status, applied.Extracted, applied.Skipped, null),
            applied.AcceptedSupplierName);
    }

    /// <summary>What one stage's `Apply...` call produced: the counts
    /// <see cref="StagedExtractionStageResult"/> reports, whether any fact fell below its own
    /// confidence bar, and the `supplier` legal name (if that stage accepted one). The last member
    /// defaults to <see langword="null"/> so the four "one row = one fact" stages — which can never
    /// produce a supplier fact — construct this with the same three values they always had.</summary>
    private readonly record struct StageApplyResult(
        int Extracted, int Skipped, bool AnyBelowThreshold, string? AcceptedSupplierName = null);

    private static string BuildSchema(ExtractionStage stage) => stage switch
    {
        ExtractionStage.Metadata => StagedExtractionJsonSchemas.Facts(MetadataFields),
        ExtractionStage.CommercialTerms => StagedExtractionJsonSchemas.Facts(CommercialTermsFields),
        ExtractionStage.DatesAndRenewalTerms => StagedExtractionJsonSchemas.Facts(DatesFields),
        ExtractionStage.LineItems => StagedExtractionJsonSchemas.LineItems(),
        ExtractionStage.LegalClauses => StagedExtractionJsonSchemas.Clauses(),
        ExtractionStage.Obligations => StagedExtractionJsonSchemas.Obligations(),
        ExtractionStage.Risk => StagedExtractionJsonSchemas.Risks(),
        _ => throw new InvalidOperationException($"Stage {stage} is not part of the staged extraction pipeline."),
    };

    /// <summary>Shared handling for the three scalar-field stages: parses the generic
    /// <see cref="ExtractedFactsPayload"/> shape, applies each recognized field onto
    /// <paramref name="contract"/> via <paramref name="applyToContract"/>, and records one
    /// <see cref="ExtractionEvidence"/> row per fact (AC-2). A fact naming a field outside
    /// <paramref name="allowedFields"/> is skipped, not applied — the JSON Schema's own
    /// <c>enum</c> constrains a well-behaved model to never send one, but this method does not
    /// trust that alone.
    ///
    /// <para>
    /// Every field is judged against <see cref="ExtractionConfidencePolicy"/> — the five
    /// critical ones included, no second bar. The <see cref="ExtractionEvidence"/> row is
    /// written either way, so a rejected fact still reaches the review list <em>with</em> its page,
    /// span, confidence and decision. Only <see cref="StageApplyResult.AcceptedSupplierName"/>
    /// distinguishes a linked supplier from a proposed one.
    /// </para>
    /// </summary>
    private StageApplyResult ApplyFacts(
        RunContext context,
        EntityId extractionJobId,
        string payloadJson,
        IReadOnlyList<string> allowedFields,
        DateTimeOffset now,
        Action<Contract, string, string?> applyToContract)
    {
        var contract = context.Contract;
        var runEvidence = context.RunEvidence;
        var payload = JsonSerializer.Deserialize<ExtractedFactsPayload>(payloadJson, PayloadSerializerOptions);
        var facts = payload?.Facts ?? [];

        var extracted = 0;
        var skipped = 0;
        var anyBelowThreshold = false;
        string? acceptedSupplierName = null;

        foreach (var fact in facts)
        {
            if (fact.Field is null || !allowedFields.Contains(fact.Field, StringComparer.Ordinal))
            {
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(fact.Value))
            {
                // Strict structured outputs cannot omit a property, so a model reports "the
                // document does not state this" as value: null (ExtractPromptTemplate rule 2). That
                // is an absent fact — not extracted, not skipped, no evidence row, and never a null
                // overwrite of a contract field a previous stage or a human already set.
                continue;
            }

            // F5-T01: a field a human corrected or confirmed is theirs. Extraction does not write it;
            // a different reading is offered on the evidence list as a proposal to review, a reading
            // that adds nothing (same as the contract, or as the last proposal) is dropped.
            if (context.Protection.IsProtected(fact.Field))
            {
                extracted++;

                if (ExtractionConfidencePolicy.IsStatusField(fact.Field)
                    || ProposalAddsNothing(contract, context.Protection, fact.Field, fact.Value))
                {
                    continue;
                }

                anyBelowThreshold = true;
                context.Protection.NoteLatestEvidence(fact.Field, fact.Value);
                dbContext.ExtractionEvidences.Add(new ExtractionEvidence
                {
                    TenantId = context.TenantId,
                    ContractId = contract.Id,
                    SourceDocumentId = context.DocumentId,
                    ExtractionJobId = extractionJobId,
                    ExtractionRunId = context.RunId,
                    FieldName = fact.Field,
                    Value = fact.Value,
                    SourceSpan = fact.SourceSpan,
                    SourcePage = ClampPage(fact.SourcePage, context.PageCount),
                    Confidence = fact.Confidence,
                    Decision = ExtractionConfidencePolicy.ReviewRequired,
                    DecidedAt = now,
                    CreatedAt = now,
                });
                runEvidence.Add((fact.Field, fact.Confidence));
                continue;
            }

            applyToContract(contract, fact.Field, fact.Value);

            // Status is derived from start/end after every stage has run — a fuzzy LLM "active"
            // must not park the document in review. The model's proposal is applied onto the
            // contract as an interim value; evidence is written by OfficializeDerivedStatus.
            if (ExtractionConfidencePolicy.IsStatusField(fact.Field))
            {
                extracted++;
                continue;
            }

            var confidence = fact.Confidence;
            string decision;
            if (ExtractionConfidencePolicy.IsExtractedStartDate(fact.Field) && TryParseDate(fact.Value, out _))
            {
                confidence = ExtractionConfidencePolicy.OfficialConfidence;
                decision = ExtractionConfidencePolicy.AutoAccepted;
            }
            else
            {
                decision = ExtractionConfidencePolicy.Decide(fact.Confidence);
            }

            var accepted = decision == ExtractionConfidencePolicy.AutoAccepted;

            if (!accepted)
            {
                anyBelowThreshold = true;
            }
            else if (fact.Field == SupplierFieldName && !string.IsNullOrWhiteSpace(fact.Value))
            {
                acceptedSupplierName = fact.Value.Trim();
            }

            dbContext.ExtractionEvidences.Add(new ExtractionEvidence
            {
                TenantId = context.TenantId,
                ContractId = contract.Id,
                SourceDocumentId = context.DocumentId,
                ExtractionJobId = extractionJobId,
                ExtractionRunId = context.RunId,
                FieldName = fact.Field,
                Value = fact.Value,
                SourceSpan = fact.SourceSpan,
                SourcePage = ClampPage(fact.SourcePage, context.PageCount),
                Confidence = confidence,
                Decision = decision,
                DecidedAt = now,
                CreatedAt = now,
            });
            runEvidence.Add((fact.Field, confidence));

            extracted++;
        }

        return new StageApplyResult(extracted, skipped, anyBelowThreshold, acceptedSupplierName);
    }

    private static void ApplyMetadataFact(Contract contract, string field, string? value)
    {
        switch (field)
        {
            case SupplierFieldName:
                // Deliberately writes nothing onto Contract. `supplier` is a legal *name*;
                // Contract.SupplierId is a cross-module reference this module may not resolve
                // itself (ADR-002 — Documents/Contracts never references Raffa.Suppliers.Products),
                // so the link is made one layer up by DocumentProcessingPipeline through
                // ISupplierResolver. The fact is still fully persisted as its own
                // ExtractionEvidence row by the caller, exactly like every other metadata fact.
                break;
            case "currency":
                if (!string.IsNullOrWhiteSpace(value))
                {
                    contract.Currency = value.Trim();
                }

                break;
            case "governingLaw":
                contract.GoverningLaw = value;
                break;
            case "status":
                if (!string.IsNullOrWhiteSpace(value))
                {
                    contract.Status = value.Trim();
                }

                break;
        }
    }

    private static void ApplyCommercialTermsFact(Contract contract, string field, string? value)
    {
        switch (field)
        {
            case "annualSpend":
                if (TryParseDecimal(value, out var annualSpend))
                {
                    contract.AnnualSpend = annualSpend;
                }

                break;
            case "totalContractValue":
                if (TryParseDecimal(value, out var totalContractValue))
                {
                    contract.TotalContractValue = totalContractValue;
                }

                break;
            case "paymentTerms":
                contract.PaymentTerms = value;
                break;
        }
    }

    private static void ApplyDatesFact(Contract contract, string field, string? value)
    {
        switch (field)
        {
            case "startDate":
                if (TryParseDate(value, out var startDate))
                {
                    contract.StartDate = startDate;
                }

                break;
            case "endDate":
                if (TryParseDate(value, out var endDate))
                {
                    contract.EndDate = endDate;
                }

                break;
            case "effectiveDate":
                if (TryParseDate(value, out var effectiveDate))
                {
                    contract.EffectiveDate = effectiveDate;
                }

                break;
            case "cancellationDeadline":
                if (TryParseDate(value, out var cancellationDeadline))
                {
                    contract.CancellationDeadline = cancellationDeadline;
                }

                break;
            case "autoRenewal":
                if (bool.TryParse(value, out var autoRenewal))
                {
                    contract.AutoRenewal = autoRenewal;
                }

                break;
            case "renewalTermMonths":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var months))
                {
                    contract.RenewalTermMonths = months;
                }

                break;
            case NoticePeriodDaysFieldName:
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var noticeDays)
                    && noticeDays >= 0)
                {
                    contract.NoticePeriodDays = noticeDays;
                }

                break;
        }
    }

    /// <summary>Builds the rows into <paramref name="rows"/> instead of adding them to the context:
    /// the caller replaces the previous run's rows with them in one go (see
    /// <c>ReplaceRowsAsync</c>), once the payload is known to parse.</summary>
    private StageApplyResult ApplyLineItems(
        RunContext context, string payloadJson, DateTimeOffset now, List<ContractLineItem> rows)
    {
        var payload = JsonSerializer.Deserialize<ExtractedLineItemsPayload>(payloadJson, PayloadSerializerOptions);
        var items = payload?.Items ?? [];

        var extracted = 0;
        var skipped = 0;
        var anyLowConfidence = false;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Description))
            {
                skipped++;
                continue;
            }

            if (ExtractionConfidencePolicy.RequiresReview(item.Confidence))
            {
                anyLowConfidence = true;
            }

            rows.Add(new ContractLineItem
            {
                TenantId = context.TenantId,
                ContractId = context.Contract.Id,
                SourceDocumentId = context.DocumentId,
                ExtractionRunId = context.RunId,
                Sku = item.Sku,
                Description = item.Description,
                Quantity = item.Quantity,
                Unit = item.Unit,
                UnitPrice = item.UnitPrice,
                ListPrice = item.ListPrice,
                Discount = item.Discount,
                BillingPeriod = item.BillingPeriod,
                AnnualCost = item.AnnualCost,
                TotalCost = item.TotalCost,
                SourceSpan = item.SourceSpan,
                SourcePage = ClampPage(item.SourcePage, context.PageCount),
                Confidence = item.Confidence,
                CreatedAt = now,
            });

            extracted++;
        }

        return new StageApplyResult(extracted, skipped, anyLowConfidence);
    }

    private StageApplyResult ApplyClauses(
        RunContext context, string payloadJson, DateTimeOffset now, List<Clause> rows)
    {
        var payload = JsonSerializer.Deserialize<ExtractedClausesPayload>(payloadJson, PayloadSerializerOptions);
        var items = payload?.Items ?? [];

        var extracted = 0;
        var skipped = 0;
        var anyLowConfidence = false;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ClauseType) || string.IsNullOrWhiteSpace(item.RawText))
            {
                skipped++;
                continue;
            }

            RiskSeverity? riskLevel = null;
            if (!string.IsNullOrWhiteSpace(item.RiskLevel)
                && Enum.TryParse<RiskSeverity>(item.RiskLevel, ignoreCase: true, out var parsedRiskLevel))
            {
                riskLevel = parsedRiskLevel;
            }

            if (ExtractionConfidencePolicy.RequiresReview(item.Confidence))
            {
                anyLowConfidence = true;
            }

            rows.Add(new Clause
            {
                TenantId = context.TenantId,
                ContractId = context.Contract.Id,
                SourceDocumentId = context.DocumentId,
                ExtractionRunId = context.RunId,
                ClauseType = item.ClauseType,
                RawText = item.RawText,
                NormalizedValue = item.NormalizedValue,
                RiskLevel = riskLevel,
                SourceSpan = item.SourceSpan,
                SourcePage = ClampPage(item.SourcePage, context.PageCount),
                Confidence = item.Confidence,
                CreatedAt = now,
            });

            extracted++;
        }

        return new StageApplyResult(extracted, skipped, anyLowConfidence);
    }

    private StageApplyResult ApplyObligations(
        RunContext context, string payloadJson, DateTimeOffset now, List<Obligation> rows)
    {
        var payload = JsonSerializer.Deserialize<ExtractedObligationsPayload>(payloadJson, PayloadSerializerOptions);
        var items = payload?.Items ?? [];

        var extracted = 0;
        var skipped = 0;
        var anyLowConfidence = false;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Party)
                || string.IsNullOrWhiteSpace(item.ObligationType)
                || string.IsNullOrWhiteSpace(item.Description))
            {
                skipped++;
                continue;
            }

            DateOnly? dueDate = null;
            if (TryParseDate(item.DueDate, out var parsedDueDate))
            {
                dueDate = parsedDueDate;
            }

            if (ExtractionConfidencePolicy.RequiresReview(item.Confidence))
            {
                anyLowConfidence = true;
            }

            rows.Add(new Obligation
            {
                TenantId = context.TenantId,
                ContractId = context.Contract.Id,
                SourceDocumentId = context.DocumentId,
                ExtractionRunId = context.RunId,
                Party = item.Party,
                ObligationType = item.ObligationType,
                Description = item.Description,
                DueDate = dueDate,
                RecurrenceRule = item.RecurrenceRule,
                Criticality = item.Criticality,
                Status = item.Status,
                Confidence = item.Confidence,
                SourceSpan = item.SourceSpan,
                SourcePage = ClampPage(item.SourcePage, context.PageCount),
                CreatedAt = now,
            });

            extracted++;
        }

        return new StageApplyResult(extracted, skipped, anyLowConfidence);
    }

    private StageApplyResult ApplyRisks(
        RunContext context, string payloadJson, DateTimeOffset now, List<Risk> rows)
    {
        var payload = JsonSerializer.Deserialize<ExtractedRisksPayload>(payloadJson, PayloadSerializerOptions);
        var items = payload?.Items ?? [];

        var extracted = 0;
        var skipped = 0;
        var anyLowConfidence = false;

        foreach (var item in items)
        {
            // Risk.Severity is a required (non-nullable) column — an item whose severity does
            // not parse cannot be persisted at all (Appendix C rule 10: return uncertainty
            // instead of fabricated precision; fabricating a default severity would be exactly
            // that).
            if (string.IsNullOrWhiteSpace(item.RiskType)
                || string.IsNullOrWhiteSpace(item.Description)
                || string.IsNullOrWhiteSpace(item.Severity)
                || !Enum.TryParse<RiskSeverity>(item.Severity, ignoreCase: true, out var severity))
            {
                skipped++;
                continue;
            }

            if (ExtractionConfidencePolicy.RequiresReview(item.Confidence))
            {
                anyLowConfidence = true;
            }

            rows.Add(new Risk
            {
                TenantId = context.TenantId,
                ContractId = context.Contract.Id,
                SourceDocumentId = context.DocumentId,
                ExtractionRunId = context.RunId,
                RiskType = item.RiskType,
                Severity = severity,
                Description = item.Description,
                Status = item.Status,
                Confidence = item.Confidence,
                SourceSpan = item.SourceSpan,
                SourcePage = ClampPage(item.SourcePage, context.PageCount),
                IdentifiedAt = now,
            });

            extracted++;
        }

        return new StageApplyResult(extracted, skipped, anyLowConfidence);
    }

    /// <summary>A page number the model reports outside the document's actual page range is
    /// treated as absent rather than stored as a plausible-looking but wrong citation (spec
    /// principle: source evidence is mandatory and must be trustworthy).</summary>
    private static int? ClampPage(int? page, int pageCount) =>
        page is >= 1 && page <= pageCount ? page : null;

    private static bool TryParseDecimal(string? value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private static bool TryParseDate(string? value, out DateOnly result) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>
    /// Decides the final <see cref="DocumentProcessingStatus"/> for the document once all seven
    /// stages have run. Shares <see cref="ExtractionConfidencePolicy"/> with
    /// <c>DocumentQueryService.IsWeak</c>: a document whose Documents-row badge says "needs
    /// review" has <see cref="DocumentProcessingStatus.NeedsReview"/>, and vice versa.
    ///
    /// <para>
    /// The document needs a human exactly when there is something a human can decide: a
    /// NeedsReview stage (a fact below the bar) or a classification below the same bar. A stage
    /// that found nothing does not route the document to review (ADR-024 amendment 2026-09-21):
    /// with every found fact at or above the bar the document completes on its own instead of
    /// asking the user to confirm an empty review list. Stage <em>failures</em> (model/network
    /// error, malformed payload) still require review: a low-confidence fact is a "weak signal";
    /// a failed stage is a "missing signal". Either way the caller then completes the document
    /// anyway when the contract has no weak field left to show (never "Review 0 fields").
    /// </para>
    /// </summary>
    private static DocumentProcessingStatus DetermineDocumentStatus(
        IReadOnlyList<StagedExtractionStageResult> stages, double? classificationConfidence)
    {
        if (stages.All(s => s.Status == ExtractionJobStatus.Failed))
        {
            return DocumentProcessingStatus.Failed;
        }

        if (stages.Any(s => s.Status == ExtractionJobStatus.Failed))
        {
            return DocumentProcessingStatus.NeedsReview;
        }

        if (stages.Any(s => s.Status == ExtractionJobStatus.NeedsReview))
        {
            return DocumentProcessingStatus.NeedsReview;
        }

        // The classification is a fact like any other (see TypeFieldName): a weak one means a
        // human should confirm the contract type before the document counts as validated.
        if (ExtractionConfidencePolicy.RequiresReview(classificationConfidence)
            && classificationConfidence is not null)
        {
            return DocumentProcessingStatus.NeedsReview;
        }

        return DocumentProcessingStatus.Completed;
    }

    /// <summary>
    /// Overwrites <see cref="Contract.Status"/> with the date-derived value and records it as
    /// <c>auto_accepted</c> at <see cref="ExtractionConfidencePolicy.OfficialConfidence"/>. A fuzzy
    /// metadata-stage guess is never left as <c>review_required</c>. When neither start nor end is
    /// known, the interim extracted status (if any, and not the bootstrap placeholder) is
    /// officialized so the field still cannot block validation.
    /// </summary>
    private void OfficializeDerivedStatus(RunContext context, Document document, DateTimeOffset now)
    {
        var contract = context.Contract;

        // F5-T01: a status a human set or confirmed is not re-derived over their head.
        if (context.Protection.IsProtected(ExtractionConfidencePolicy.StatusFieldName))
        {
            return;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var derived = ExtractionConfidencePolicy.DeriveStatus(contract.StartDate, contract.EndDate, today);
        var status = derived
            ?? (string.Equals(contract.Status, BootstrapContractStatus, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(contract.Status)
                    ? null
                    : contract.Status.Trim().ToLowerInvariant());

        if (status is null)
        {
            return;
        }

        contract.Status = status;
        dbContext.ExtractionEvidences.Add(new ExtractionEvidence
        {
            TenantId = context.TenantId,
            ContractId = contract.Id,
            SourceDocumentId = document.Id,
            ExtractionRunId = context.RunId,
            FieldName = ExtractionConfidencePolicy.StatusFieldName,
            Value = status,
            Confidence = ExtractionConfidencePolicy.OfficialConfidence,
            Decision = ExtractionConfidencePolicy.AutoAccepted,
            DecidedAt = now,
            CreatedAt = now,
        });
        context.RunEvidence.Add((ExtractionConfidencePolicy.StatusFieldName, ExtractionConfidencePolicy.OfficialConfidence));
    }

    /// <summary>
    /// Writes the <see cref="TypeFieldName"/> evidence row for this run's classification verdict:
    /// the proposed type is the document's (what the `classify` role said), never the contract's
    /// current one (which a human may already have corrected), so the row records the proposal
    /// exactly as every other <see cref="ExtractionEvidence"/> row does. Linked to the most recent
    /// classification <see cref="ExtractionJob"/> for traceability when one exists; no page or span
    /// — the classification reads the whole document, and inventing "page 1" would be the fabricated
    /// precision Appendix C rule 10 forbids.
    /// </summary>
    private async Task RecordClassificationEvidenceAsync(
        RunContext context,
        Document document,
        double confidence,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var tenantId = context.TenantId;
        var contract = context.Contract;
        var classificationJobId = await dbContext.ExtractionJobs
            .Where(j => j.TenantId == tenantId && j.DocumentId == document.Id && j.Stage == ExtractionStage.Classification)
            .OrderByDescending(j => j.QueuedAt)
            .Select(j => (EntityId?)j.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var decision = ExtractionConfidencePolicy.Decide(confidence);

        // F5-T01: a contract type a human set or confirmed is theirs. A verdict that agrees with it (or
        // repeats what was proposed last time) adds nothing; one that disagrees is a proposal to review.
        if (context.Protection.IsProtected(TypeFieldName))
        {
            if (ProposalAddsNothing(contract, context.Protection, TypeFieldName, document.DocumentType.ToString()))
            {
                return;
            }

            decision = ExtractionConfidencePolicy.ReviewRequired;
        }

        dbContext.ExtractionEvidences.Add(new ExtractionEvidence
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            SourceDocumentId = document.Id,
            ExtractionJobId = classificationJobId,
            ExtractionRunId = context.RunId,
            FieldName = TypeFieldName,
            Value = document.DocumentType.ToString(),
            SourceSpan = null,
            SourcePage = null,
            Confidence = confidence,
            Decision = decision,
            DecidedAt = now,
            CreatedAt = now,
        });
        context.RunEvidence.Add((TypeFieldName, confidence));
    }

    /// <summary>
    /// Display of the already-compared stored double. Not a rounding step in the decision
    /// (ADR-024 w17 clause A1): <see cref="ExtractionConfidencePolicy.Decide"/> still sees the
    /// raw value. Default general format so the trail shows <c>0.95</c>, not a G17 round-trip.
    /// </summary>
    private static string FormatConfidence(double? confidence) =>
        confidence is { } value ? value.ToString(CultureInfo.InvariantCulture) : "null";

    private static string Truncate(string value, int maxLength = 1000) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
