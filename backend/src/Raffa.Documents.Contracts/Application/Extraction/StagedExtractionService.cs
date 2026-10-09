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

        var documentText = BuildPageMarkedText(pages);

        // F5-T01/F5-T02: decide whether this call starts a fresh run or continues an incomplete one
        // over the same text, and what a human already owns on this contract. Both are read before
        // anything is written.
        var plan = await PlanRunAsync(tenantId, document.Id, ComputeInputHash(documentText), now, cancellationToken)
            .ConfigureAwait(false);
        var context = new RunContext(
            tenantId,
            document.Id,
            contract,
            plan,
            pages.Count,
            await LoadProtectionAsync(tenantId, contract, cancellationToken).ConfigureAwait(false),
            ReplaceUnattributedRows: !await dbContext.Documents
                .AnyAsync(d => d.TenantId == tenantId && d.ContractId == contract.Id && d.Id != document.Id, cancellationToken)
                .ConfigureAwait(false));

        if (classificationConfidence is { } typeConfidence)
        {
            await RecordClassificationEvidenceAsync(context, document, typeConfidence, now, cancellationToken)
                .ConfigureAwait(false);
        }

        document.ProcessingStatus = DocumentProcessingStatus.Processing;

        // NW-106: the seven stages are independent reads over the same text, so their gateway calls are
        // started together (StartStagesAsync) while every dbContext write below stays sequential.
        // F5-T02: on a resume only the stages without a finished checkpoint get a job and a call.
        var pending = await StartStagesAsync(context, documentText, cancellationToken).ConfigureAwait(false);
        var stageResults = new List<StagedExtractionStageResult>(PipelineStages.Length);

        using (progressHeartbeat?.BeginMemoryPulses())
        {
            foreach (var stage in PipelineStages)
            {
                stageResults.Add(
                    plan.Reused.TryGetValue(stage, out var checkpoint)
                        ? await ReuseCheckpointAsync(context, checkpoint, cancellationToken).ConfigureAwait(false)
                        : await ApplyStageResultAsync(context, pending[stage], cancellationToken).ConfigureAwait(false));
            }
        }

        // F5-D08: cancellationDeadline = endDate - noticePeriodDays, computed here and not left to
        // the model's own arithmetic. It reads the evidence the Dates stage wrote, which the loop
        // above has already saved.
        await DeriveCancellationDeadlineAsync(context, cancellationToken).ConfigureAwait(false);

        OfficializeDerivedStatus(context, now);

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
                    BuildExtractionAuditDetail(context, failedStages)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<StagedExtractionSummary>.Success(
            new StagedExtractionSummary(contract.Id, document.ProcessingStatus, stageResults, context.AcceptedSupplierName));
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
    /// Awaits one stage's in-flight extraction call and applies it. F5-T01: the stage's facts, the
    /// removal of the previous run's rows (list stages) and the job row go out in one
    /// <c>SaveChangesAsync</c> -- one transaction -- and only after the payload has parsed, so a failed
    /// stage leaves what an earlier run stored untouched. F5-T02: a failure is typed and recorded on the
    /// job; the caller turns any failed stage into a partial document.
    /// </summary>
    private async Task<StagedExtractionStageResult> ApplyStageResultAsync(
        RunContext context, PendingStage pending, CancellationToken cancellationToken)
    {
        var job = pending.Job;
        context.StageJobIds[job.Stage] = job.Id;

        // Always set by StartStagesAsync for every job this method is ever called with.
        var startedAt = job.StartedAt!.Value;

        Result<AiExtractionResult> extractResult;
        try
        {
            extractResult = await pending.Call.ConfigureAwait(false);
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
                    job, extractResult.Error, ClassifyFailure(extractResult.Error), completedAt, cancellationToken)
                .ConfigureAwait(false);
        }

        job.ModelId = extractResult.Value.Metadata.ModelId;
        var payloadJson = extractResult.Value.PayloadJson;

        StageApplyResult applied;

        try
        {
            applied = job.Stage switch
            {
                ExtractionStage.Metadata => ApplyFacts(context, job.Id, payloadJson, MetadataFields, startedAt, ApplyMetadataFact),
                ExtractionStage.CommercialTerms => ApplyFacts(context, job.Id, payloadJson, CommercialTermsFields, startedAt, ApplyCommercialTermsFact),
                ExtractionStage.DatesAndRenewalTerms => ApplyFacts(context, job.Id, payloadJson, DatesFields, startedAt, ApplyDatesFact),
                ExtractionStage.LineItems => await ApplyLineItemsAsync(context, payloadJson, startedAt, cancellationToken).ConfigureAwait(false),
                ExtractionStage.LegalClauses => await ApplyClausesAsync(context, payloadJson, startedAt, cancellationToken).ConfigureAwait(false),
                ExtractionStage.Obligations => await ApplyObligationsAsync(context, payloadJson, startedAt, cancellationToken).ConfigureAwait(false),
                ExtractionStage.Risk => await ApplyRisksAsync(context, payloadJson, startedAt, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Stage {job.Stage} is not part of the staged extraction pipeline."),
            };
        }
        catch (JsonException ex)
        {
            // The gateway does not validate the model's output against the schema it was given
            // (IAiGateway.ExtractAsync's own doc comment): a real model can still return invalid JSON.
            // One malformed stage must not crash the other six. Parsing is the first thing every Apply
            // does, so nothing was staged for this stage yet.
            return await FailStageAsync(
                    job, $"Malformed extraction payload: {ex.Message}", ExtractionStageFailureKind.Permanent,
                    completedAt, cancellationToken)
                .ConfigureAwait(false);
        }

        // A stage needs review exactly when it produced a fact below ExtractionConfidencePolicy --
        // something a reviewer can decide on (ADR-024 amendment 2026-09-21). A stage that found nothing
        // is a legitimate answer, and so is a fact outside the stage's allow-list (counted in Skipped).
        job.Status = applied.AnyBelowThreshold
            ? ExtractionJobStatus.NeedsReview
            : ExtractionJobStatus.Completed;
        job.CompletedAt = completedAt;
        job.ErrorDetail = null;
        job.FailureKind = null;
        job.ExtractedCount = applied.Extracted;
        job.SkippedCount = applied.Skipped;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new StagedExtractionStageResult(job.Stage, job.Status, applied.Extracted, applied.Skipped, null);
    }

    /// <summary>What one stage's `Apply...` call produced: the counts
    /// <see cref="StagedExtractionStageResult"/> reports, and whether any fact fell below its own
    /// confidence bar.</summary>
    private readonly record struct StageApplyResult(int Extracted, int Skipped, bool AnyBelowThreshold);

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
    /// <see cref="ExtractedFactsPayload"/>, applies each recognized field onto the contract via
    /// <paramref name="applyToContract"/> and records one <see cref="ExtractionEvidence"/> row per fact
    /// (AC-2), accepted or not, so a rejected fact still reaches the review list with its page, span,
    /// confidence and decision. A fact naming a field outside <paramref name="allowedFields"/> is
    /// skipped: the schema's <c>enum</c> constrains a well-behaved model, but this does not trust that.
    /// Every field is judged against <see cref="ExtractionConfidencePolicy"/>, no second bar.
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
        var payload = JsonSerializer.Deserialize<ExtractedFactsPayload>(payloadJson, PayloadSerializerOptions);

        var extracted = 0;
        var skipped = 0;
        var anyBelowThreshold = false;
        string? acceptedSupplierName = null;

        foreach (var fact in payload?.Facts ?? [])
        {
            if (fact.Field is null || !allowedFields.Contains(fact.Field, StringComparer.Ordinal))
            {
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(fact.Value))
            {
                // Strict structured outputs cannot omit a property, so "the document does not state
                // this" arrives as value: null. That is an absent fact: not extracted, not skipped, no
                // evidence row, and never a null overwrite of a field a previous stage or a human set.
                continue;
            }

            var page = ClampPage(fact.SourcePage, context.PageCount);

            // F5-T01: a field a human corrected or confirmed is theirs. Extraction does not write it;
            // a different reading is offered on the evidence list as a proposal to review, a reading
            // that adds nothing (same as the contract, or as the last proposal) is dropped.
            if (context.Protection.IsProtected(fact.Field))
            {
                extracted++;

                if (!ExtractionConfidencePolicy.IsStatusField(fact.Field)
                    && !ProposalAddsNothing(contract, context.Protection, fact.Field, fact.Value))
                {
                    anyBelowThreshold = true;
                    context.Protection.NoteLatestEvidence(fact.Field, fact.Value);
                    AddEvidence(
                        context, fact.Field, fact.Value, fact.Confidence, ExtractionConfidencePolicy.ReviewRequired,
                        now, extractionJobId, page, fact.SourceSpan);
                }

                continue;
            }

            applyToContract(contract, fact.Field, fact.Value);
            extracted++;

            // Status is derived from start/end after every stage has run (OfficializeDerivedStatus): a
            // fuzzy LLM "active" must not park the document in review, so it gets no evidence here.
            if (ExtractionConfidencePolicy.IsStatusField(fact.Field))
            {
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

            if (decision != ExtractionConfidencePolicy.AutoAccepted)
            {
                anyBelowThreshold = true;
            }
            else if (fact.Field == SupplierFieldName)
            {
                acceptedSupplierName = fact.Value.Trim();
            }

            AddEvidence(context, fact.Field, fact.Value, confidence, decision, now, extractionJobId, page, fact.SourceSpan);
        }

        // Only `metadata` produces a supplier, so this never silently picks between competing stages.
        context.AcceptedSupplierName ??= acceptedSupplierName;

        return new StageApplyResult(extracted, skipped, anyBelowThreshold);
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
                if (TryParseInt(value, out var months))
                {
                    contract.RenewalTermMonths = months;
                }

                break;
            case NoticePeriodDaysFieldName:
                if (TryParseInt(value, out var noticeDays) && noticeDays >= 0)
                {
                    contract.NoticePeriodDays = noticeDays;
                }

                break;
        }
    }

    /// <summary>
    /// Builds the rows of a list stage ("one row = one fact"): an item that <paramref name="toRow"/>
    /// maps to <see langword="null"/> is skipped, and one row below the confidence bar flags the stage
    /// for review.
    /// </summary>
    private static StageApplyResult CollectRows<TItem, TRow>(
        IEnumerable<TItem>? items, List<TRow> rows, Func<TItem, TRow?> toRow, Func<TRow, double?> confidenceOf)
        where TRow : class
    {
        var skipped = 0;
        var anyLowConfidence = false;

        foreach (var item in items ?? [])
        {
            if (toRow(item) is not { } row)
            {
                skipped++;
                continue;
            }

            anyLowConfidence |= ExtractionConfidencePolicy.RequiresReview(confidenceOf(row));
            rows.Add(row);
        }

        return new StageApplyResult(rows.Count, skipped, anyLowConfidence);
    }

    private async Task<StageApplyResult> ApplyLineItemsAsync(
        RunContext context, string payloadJson, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = new List<ContractLineItem>();
        var applied = CollectRows(
            JsonSerializer.Deserialize<ExtractedLineItemsPayload>(payloadJson, PayloadSerializerOptions)?.Items,
            rows,
            item => string.IsNullOrWhiteSpace(item.Description)
                ? null
                : new ContractLineItem
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
                },
            row => row.Confidence);

        await ReplaceRowsAsync(
                context,
                dbContext.ContractLineItems.Where(x => x.TenantId == context.TenantId
                    && x.ContractId == context.Contract.Id
                    && (x.SourceDocumentId == context.DocumentId
                        || (context.ReplaceUnattributedRows && x.SourceDocumentId == null))),
                dbContext.ContractLineItems,
                rows,
                x => NormalizeKeyPart(x.Sku) + "|" + NormalizeKeyPart(x.Description),
                cancellationToken,
                isLinkedElsewhere: x => x.ProductId is not null)
            .ConfigureAwait(false);

        return applied;
    }

    private async Task<StageApplyResult> ApplyClausesAsync(
        RunContext context, string payloadJson, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = new List<Clause>();
        var applied = CollectRows(
            JsonSerializer.Deserialize<ExtractedClausesPayload>(payloadJson, PayloadSerializerOptions)?.Items,
            rows,
            item => string.IsNullOrWhiteSpace(item.ClauseType) || string.IsNullOrWhiteSpace(item.RawText)
                ? null
                : new Clause
                {
                    TenantId = context.TenantId,
                    ContractId = context.Contract.Id,
                    SourceDocumentId = context.DocumentId,
                    ExtractionRunId = context.RunId,
                    ClauseType = item.ClauseType,
                    RawText = item.RawText,
                    NormalizedValue = item.NormalizedValue,
                    RiskLevel = Enum.TryParse<RiskSeverity>(item.RiskLevel, ignoreCase: true, out var riskLevel) ? riskLevel : null,
                    SourceSpan = item.SourceSpan,
                    SourcePage = ClampPage(item.SourcePage, context.PageCount),
                    Confidence = item.Confidence,
                    CreatedAt = now,
                },
            row => row.Confidence);

        // A risk may point at a clause (Risk.ClauseId, restrict): such a clause is not ours to delete.
        var referenced = (await dbContext.Risks
                .AsNoTracking()
                .Where(r => r.TenantId == context.TenantId && r.ContractId == context.Contract.Id && r.ClauseId != null)
                .Select(r => r.ClauseId!.Value)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();

        await ReplaceRowsAsync(
                context,
                dbContext.Clauses.Where(x => x.TenantId == context.TenantId
                    && x.ContractId == context.Contract.Id
                    && x.SourceDocumentId == context.DocumentId),
                dbContext.Clauses,
                rows,
                x => NormalizeKeyPart(x.ClauseType) + "|" + NormalizeKeyPart(x.RawText),
                cancellationToken,
                isLinkedElsewhere: x => referenced.Contains(x.Id))
            .ConfigureAwait(false);

        return applied;
    }

    private async Task<StageApplyResult> ApplyObligationsAsync(
        RunContext context, string payloadJson, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = new List<Obligation>();
        var applied = CollectRows(
            JsonSerializer.Deserialize<ExtractedObligationsPayload>(payloadJson, PayloadSerializerOptions)?.Items,
            rows,
            item => string.IsNullOrWhiteSpace(item.Party)
                || string.IsNullOrWhiteSpace(item.ObligationType)
                || string.IsNullOrWhiteSpace(item.Description)
                ? null
                : new Obligation
                {
                    TenantId = context.TenantId,
                    ContractId = context.Contract.Id,
                    SourceDocumentId = context.DocumentId,
                    ExtractionRunId = context.RunId,
                    Party = item.Party,
                    ObligationType = item.ObligationType,
                    Description = item.Description,
                    DueDate = TryParseDate(item.DueDate, out var dueDate) ? dueDate : null,
                    RecurrenceRule = item.RecurrenceRule,
                    Criticality = item.Criticality,
                    Status = item.Status,
                    Confidence = item.Confidence,
                    SourceSpan = item.SourceSpan,
                    SourcePage = ClampPage(item.SourcePage, context.PageCount),
                    CreatedAt = now,
                },
            row => row.Confidence);

        await ReplaceRowsAsync(
                context,
                dbContext.Obligations.Where(x => x.TenantId == context.TenantId
                    && x.ContractId == context.Contract.Id
                    && x.SourceDocumentId == context.DocumentId),
                dbContext.Obligations,
                rows,
                x => NormalizeKeyPart(x.ObligationType) + "|" + NormalizeKeyPart(x.Party) + "|" + NormalizeKeyPart(x.Description),
                cancellationToken)
            .ConfigureAwait(false);

        return applied;
    }

    private async Task<StageApplyResult> ApplyRisksAsync(
        RunContext context, string payloadJson, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = new List<Risk>();
        var applied = CollectRows(
            JsonSerializer.Deserialize<ExtractedRisksPayload>(payloadJson, PayloadSerializerOptions)?.Items,
            rows,
            // Risk.Severity is a required column: an item whose severity does not parse cannot be
            // persisted, and a fabricated default would be exactly the fake precision Appendix C rule 10
            // forbids.
            item => string.IsNullOrWhiteSpace(item.RiskType)
                || string.IsNullOrWhiteSpace(item.Description)
                || !Enum.TryParse<RiskSeverity>(item.Severity, ignoreCase: true, out var severity)
                ? null
                : new Risk
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
                },
            row => row.Confidence);

        await ReplaceRowsAsync(
                context,
                dbContext.Risks.Where(x => x.TenantId == context.TenantId
                    && x.ContractId == context.Contract.Id
                    && (x.SourceDocumentId == context.DocumentId
                        || (context.ReplaceUnattributedRows && x.SourceDocumentId == null))),
                dbContext.Risks,
                rows,
                x => NormalizeKeyPart(x.RiskType) + "|" + NormalizeKeyPart(x.Description),
                cancellationToken)
            .ConfigureAwait(false);

        return applied;
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

    private static bool TryParseInt(string? value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

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
    private void OfficializeDerivedStatus(RunContext context, DateTimeOffset now)
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
        AddEvidence(
            context,
            ExtractionConfidencePolicy.StatusFieldName,
            status,
            ExtractionConfidencePolicy.OfficialConfidence,
            ExtractionConfidencePolicy.AutoAccepted,
            now);
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

        AddEvidence(
            context, TypeFieldName, document.DocumentType.ToString(), confidence, decision, now, classificationJobId);
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
