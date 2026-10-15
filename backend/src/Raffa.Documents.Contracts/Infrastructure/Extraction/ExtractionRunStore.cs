using System.Globalization;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Infrastructure.Extraction;

/// <summary>
/// F5 PR-1: the persistence side of a staged-extraction run, behind <see cref="IExtractionRunStore"/>.
/// What makes a run idempotent, resumable and respectful of what a human already decided
/// (F5-T01 / F5-T02 / F5-D08), moved out of the former staged-extraction service without a behaviour change.
/// <list type="bullet">
/// <item><b>Run id.</b> Every run belongs to one run (<see cref="ExtractionJob.ExtractionRunId"/>),
/// stamped on its stage jobs and facts. A list stage that applies successfully <em>replaces</em> the
/// rows of the same kind for the same (contract, source document) in one <c>SaveChangesAsync</c>;
/// rows with a human correction are kept. Replacing happens only once the payload has parsed, so a
/// failed stage keeps what the previous run stored.</item>
/// <item><b>Human-owned fields.</b> A <see cref="Contract"/> field with a <see cref="CorrectionHistory"/>
/// row, or whose latest evidence is <c>human_accepted</c>, is never written by extraction; a different
/// reading becomes a <c>review_required</c> proposal.</item>
/// <item><b>Checkpoint and resume.</b> A run whose stages did not all finish is <em>open</em>; a later
/// run over the same text (<see cref="ExtractionJob.InputHash"/>) reuses the stages that finished and
/// calls the gateway only for the others. A run that finished entirely is never resumed.</item>
/// <item><b>Typed stage failure.</b> A failed stage records an <see cref="ExtractionStageFailureKind"/>;
/// the document is then partial and never <c>Completed</c>.</item>
/// <item><b>Notice period.</b> <c>cancellationDeadline = endDate - noticePeriodDays</c> is computed here.</item>
/// </list>
/// Tenancy (ADR-009): every public method refuses to run outside the tenant scope of the run it
/// belongs to, and a run handle is only valid for the store instance (so the scoped DbContext) that
/// opened it.
/// </summary>
public sealed partial class ExtractionRunStore(
    DocumentsContractsDbContext dbContext,
    ITenantContext tenantContext,
    IClock clock,
    IAuditWriter auditWriter) : IExtractionRunStore
{
    private static IReadOnlyList<ExtractionStage> PipelineStages => ExtractionPipeline.Stages;

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

    internal sealed record RunPlan(Guid RunId, string InputHash, IReadOnlyDictionary<ExtractionStage, ExtractionJob> Reused);

    // ------------------------------------------------------------------------------------------
    // The port
    // ------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<Result<ExtractionRun>> OpenAsync(ExtractionRunRequest request, CancellationToken cancellationToken)
    {
        RequireTenantScope(request.TenantId);

        var tenantId = request.TenantId;
        var documentId = request.DocumentId;

        var document = await dbContext.Documents
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return Result<ExtractionRun>.Failure($"Document {documentId} was not found for this tenant.");
        }

        var now = clock.UtcNow;
        var contract = await EnsureContractAsync(tenantId, document, now, cancellationToken).ConfigureAwait(false);

        // F5-T01/F5-T02: decide whether this call starts a fresh run or continues an incomplete one
        // over the same text, and what a human already owns on this contract. Both are read before
        // anything is written.
        var plan = await PlanRunAsync(tenantId, document.Id, request.InputHash, now, cancellationToken)
            .ConfigureAwait(false);
        var run = new ExtractionRun(
            this,
            tenantId,
            document,
            contract,
            plan,
            request.PageCount,
            await LoadProtectionAsync(tenantId, contract, cancellationToken).ConfigureAwait(false),
            replaceUnattributedRows: !await dbContext.Documents
                .AnyAsync(d => d.TenantId == tenantId && d.ContractId == contract.Id && d.Id != document.Id, cancellationToken)
                .ConfigureAwait(false),
            now,
            request.ClassificationConfidence);

        if (request.ClassificationConfidence is { } typeConfidence)
        {
            await RecordClassificationEvidenceAsync(run, document, typeConfidence, now, cancellationToken)
                .ConfigureAwait(false);
        }

        document.ProcessingStatus = DocumentProcessingStatus.Processing;

        return Result<ExtractionRun>.Success(run);
    }

    /// <summary>
    /// NW-106: creates and saves the <c>Running</c> job of every stage that is not reused (one
    /// <c>SaveChangesAsync</c>, so every hang-recovery window starts together). The caller then fires
    /// the stages' gateway calls; <see cref="ApplyAsync"/> applies them one at a time because the
    /// <c>DbContext</c> is not safe for concurrent use.
    /// </summary>
    public async Task<IReadOnlyDictionary<ExtractionStage, EntityId>> StartStagesAsync(
        ExtractionRun run, IReadOnlyList<ExtractionStage> stages, CancellationToken cancellationToken)
    {
        Guard(run);

        var jobIds = new Dictionary<ExtractionStage, EntityId>();
        foreach (var stage in stages)
        {
            var startedAt = clock.UtcNow;
            var job = new ExtractionJob
            {
                TenantId = run.TenantId,
                DocumentId = run.DocumentId,
                Stage = stage,
                Status = ExtractionJobStatus.Running,
                QueuedAt = startedAt,
                StartedAt = startedAt,
                ExtractionRunId = run.RunId,
                InputHash = run.Plan.InputHash,
            };
            dbContext.ExtractionJobs.Add(job);
            run.Jobs[stage] = job;
            jobIds[stage] = job.Id;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return jobIds;
    }

    /// <summary>A stage that already finished for this very text in the run being continued: nothing is
    /// applied again; its result is rebuilt from the checkpoint and the evidence it wrote, so the later
    /// steps (supplier link, audit detail) see what a full run would have given them.</summary>
    public async Task<StagedExtractionStageResult> ReuseCheckpointAsync(
        ExtractionRun run, ExtractionStage stage, CancellationToken cancellationToken)
    {
        Guard(run);

        var context = run;
        var checkpoint = run.Plan.Reused[stage];
        context.StageJobIds[checkpoint.Stage] = checkpoint.Id;

        var evidence = await dbContext.ExtractionEvidences
            .AsNoTracking()
            .Where(e => e.TenantId == context.TenantId && e.ExtractionJobId == checkpoint.Id)
            .Select(e => new { e.FieldName, e.Confidence, e.Value, e.Decision })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        context.RunEvidence.AddRange(evidence.Select(e => (e.FieldName, e.Confidence)));

        if (!context.Protection.IsProtected(ExtractionFieldNames.Supplier))
        {
            context.AcceptedSupplierName ??= evidence
                .Where(e => e.FieldName == ExtractionFieldNames.Supplier
                    && !string.IsNullOrWhiteSpace(e.Value)
                    && e.Decision is ExtractionConfidencePolicy.AutoAccepted or ExtractionConfidencePolicy.HumanAccepted)
                .Select(e => e.Value!.Trim())
                .FirstOrDefault();
        }

        return new StagedExtractionStageResult(
            checkpoint.Stage, checkpoint.Status, checkpoint.ExtractedCount ?? 0, checkpoint.SkippedCount ?? 0, null);
    }

    /// <summary>
    /// Applies one stage's parsed facts. F5-T01: the stage's facts, the removal of the previous run's
    /// rows (list stages) and the job row go out in one <c>SaveChangesAsync</c> -- one transaction --
    /// and only after the payload has parsed (the caller parses before calling), so a failed stage
    /// leaves what an earlier run stored untouched.
    /// </summary>
    public async Task<StagedExtractionStageResult> ApplyAsync(
        ExtractionRun run, ExtractionStage stage, StageFacts facts, string? modelId, CancellationToken cancellationToken)
    {
        Guard(run);
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.Stage != stage)
        {
            throw new InvalidOperationException(
                $"Facts of stage {facts.Stage} cannot be applied as stage {stage}.");
        }

        var job = JobOf(run, stage);
        var context = run;
        context.StageJobIds[job.Stage] = job.Id;

        // Always set by StartStagesAsync for every job this method is ever called with.
        var startedAt = job.StartedAt!.Value;

        var completedAt = clock.UtcNow;

        job.ModelId = modelId;

        var applied = job.Stage switch
        {
            ExtractionStage.Metadata => ApplyFacts(context, job.Id, ((StageFacts.Scalars)facts).Facts, ExtractionStageFields.Metadata, startedAt, ApplyMetadataFact),
            ExtractionStage.CommercialTerms => ApplyFacts(context, job.Id, ((StageFacts.Scalars)facts).Facts, ExtractionStageFields.CommercialTerms, startedAt, ApplyCommercialTermsFact),
            ExtractionStage.DatesAndRenewalTerms => ApplyFacts(context, job.Id, ((StageFacts.Scalars)facts).Facts, ExtractionStageFields.DatesAndRenewalTerms, startedAt, ApplyDatesFact),
            ExtractionStage.LineItems => await ApplyLineItemsAsync(context, ((StageFacts.LineItems)facts).Items, startedAt, cancellationToken).ConfigureAwait(false),
            ExtractionStage.LegalClauses => await ApplyClausesAsync(context, ((StageFacts.Clauses)facts).Items, startedAt, cancellationToken).ConfigureAwait(false),
            ExtractionStage.Obligations => await ApplyObligationsAsync(context, ((StageFacts.Obligations)facts).Items, startedAt, cancellationToken).ConfigureAwait(false),
            ExtractionStage.Risk => await ApplyRisksAsync(context, ((StageFacts.Risks)facts).Items, startedAt, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Stage {job.Stage} is not part of the staged extraction pipeline."),
        };

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

    /// <summary>Marks the stage job Failed with a typed kind and saves. <paramref name="modelId"/> is
    /// kept on the job when the gateway did answer (a payload that then failed to parse).</summary>
    public async Task<StagedExtractionStageResult> FailAsync(
        ExtractionRun run,
        ExtractionStage stage,
        string error,
        ExtractionStageFailureKind kind,
        CancellationToken cancellationToken,
        string? modelId = null)
    {
        Guard(run);

        var job = JobOf(run, stage);
        run.StageJobIds[job.Stage] = job.Id;

        var completedAt = clock.UtcNow;

        if (modelId is not null)
        {
            job.ModelId = modelId;
        }

        job.Status = ExtractionJobStatus.Failed;
        job.ErrorDetail = Truncate(error);
        job.FailureKind = kind;
        job.CompletedAt = completedAt;
        job.ExtractedCount = 0;
        job.SkippedCount = 0;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new StagedExtractionStageResult(job.Stage, job.Status, 0, 0, job.ErrorDetail, kind);
    }

    /// <summary>
    /// Closes the run: derived deadline (F5-D08), status officialisation, document status, save, the
    /// "never Review 0 fields" auto-complete, and the one audit row. Returns the summary.
    /// </summary>
    public async Task<StagedExtractionSummary> CloseAsync(
        ExtractionRun run, IReadOnlyList<StagedExtractionStageResult> stageResults, CancellationToken cancellationToken)
    {
        Guard(run);

        var context = run;
        var document = run.Document;
        var contract = run.Contract;
        var tenantId = run.TenantId;
        var now = run.OpenedAt;

        // F5-D08: cancellationDeadline = endDate - noticePeriodDays, computed here and not left to
        // the model's own arithmetic. It reads the evidence the Dates stage wrote, which the apply
        // loop has already saved.
        await DeriveCancellationDeadlineAsync(context, cancellationToken).ConfigureAwait(false);

        OfficializeDerivedStatus(context, now);

        document.ProcessingStatus = DetermineDocumentStatus(stageResults, run.ClassificationConfidence);

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
                    run.DocumentId.Value.ToString(),
                    now,
                    BuildExtractionAuditDetail(context, failedStages)),
                cancellationToken)
            .ConfigureAwait(false);

        return new StagedExtractionSummary(contract.Id, document.ProcessingStatus, stageResults, context.AcceptedSupplierName);
    }

    // ------------------------------------------------------------------------------------------
    // Guards
    // ------------------------------------------------------------------------------------------

    private void Guard(ExtractionRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (!ReferenceEquals(run.Owner, this))
        {
            throw new InvalidOperationException(
                "ExtractionRun belongs to another store instance (another DbContext scope).");
        }

        RequireTenantScope(run.TenantId);
    }

    /// <summary>ADR-009: a missing or foreign tenant scope is a programmer error, never a quiet empty
    /// result -- under RLS it would plan a fresh run beside the old rows.</summary>
    private void RequireTenantScope(TenantId expected)
    {
        if (tenantContext.Current != expected)
        {
            throw new InvalidOperationException(
                $"No tenant scope for {expected} (ADR-009): refusing to touch tenant-scoped rows without the RLS claim.");
        }
    }

    private static ExtractionJob JobOf(ExtractionRun run, ExtractionStage stage) =>
        run.Jobs.TryGetValue(stage, out var job)
            ? job
            : throw new InvalidOperationException($"Stage {stage} has no running job in this run (not started, or reused).");

    // ------------------------------------------------------------------------------------------
    // Planning: fresh run vs. resume
    // ------------------------------------------------------------------------------------------

    private static bool IsFinished(ExtractionJob job) =>
        job.Status is ExtractionJobStatus.Completed or ExtractionJobStatus.NeedsReview && job.CompletedAt is not null;

    /// <summary>
    /// Decides how this call relates to what earlier calls left behind. Stage jobs still <c>Running</c>
    /// belong to a run that stopped (hang, crash, cancelled worker): they are closed as interrupted so
    /// they are not mistaken for live work.
    /// </summary>
    private async Task<RunPlan> PlanRunAsync(
        TenantId tenantId, EntityId documentId, string inputHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = await dbContext.ExtractionJobs
            .Where(j => j.TenantId == tenantId && j.DocumentId == documentId && j.Stage != ExtractionStage.Classification)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var orphan in jobs.Where(j => j.Status == ExtractionJobStatus.Running && j.CompletedAt is null))
        {
            orphan.Status = ExtractionJobStatus.Failed;
            orphan.CompletedAt = now;
            orphan.FailureKind = ExtractionStageFailureKind.Transient;
            orphan.ErrorDetail = "Interrupted: the extraction run ended before this stage finished.";
        }

        var runs = jobs
            .Where(j => j.ExtractionRunId is not null)
            .GroupBy(j => j.ExtractionRunId!.Value)
            .Select(g => (RunId: g.Key, Jobs: g.ToList(), Last: g.Max(j => j.QueuedAt)))
            .OrderByDescending(r => r.Last)
            .ToList();

        var fresh = new RunPlan(Guid.NewGuid(), inputHash, new Dictionary<ExtractionStage, ExtractionJob>());

        // No earlier run, or two runs that cannot be told apart by time: start clean rather than
        // risk continuing the wrong one.
        if (runs.Count == 0 || (runs.Count > 1 && runs[0].Last == runs[1].Last))
        {
            return fresh;
        }

        var latest = runs[0];

        // A run in which every stage finished is complete: reprocessing it means "extract again".
        if (PipelineStages.All(stage => latest.Jobs.Any(j => j.Stage == stage && IsFinished(j))))
        {
            return fresh;
        }

        var reused = new Dictionary<ExtractionStage, ExtractionJob>();
        foreach (var stage in PipelineStages)
        {
            var checkpoint = latest.Jobs
                .Where(j => j.Stage == stage && IsFinished(j) && j.InputHash == inputHash)
                .OrderByDescending(j => j.CompletedAt)
                .FirstOrDefault();
            if (checkpoint is not null)
            {
                reused[stage] = checkpoint;
            }
        }

        return reused.Count == 0 ? fresh : new RunPlan(latest.RunId, inputHash, reused);
    }

    // ------------------------------------------------------------------------------------------
    // Human-owned fields and evidence
    // ------------------------------------------------------------------------------------------

    /// <summary>Latest <see cref="ExtractionEvidence"/> row per field of the contract (newest first by
    /// <c>CreatedAt</c>, then id), optionally restricted to <paramref name="fields"/>.</summary>
    private async Task<Dictionary<string, ExtractionEvidence>> LatestEvidenceAsync(
        TenantId tenantId, EntityId contractId, string[]? fields, CancellationToken cancellationToken)
    {
        var query = dbContext.ExtractionEvidences
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.ContractId == contractId);
        if (fields is not null)
        {
            query = query.Where(e => fields.Contains(e.FieldName));
        }

        return (await query.ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(e => e.FieldName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id.Value).First(),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Stages one evidence row of this run and notes it for the audit detail.</summary>
    private void AddEvidence(
        ExtractionRun context,
        string field,
        string? value,
        double? confidence,
        string decision,
        DateTimeOffset at,
        EntityId? jobId = null,
        int? page = null,
        string? span = null)
    {
        dbContext.ExtractionEvidences.Add(new ExtractionEvidence
        {
            TenantId = context.TenantId,
            ContractId = context.Contract.Id,
            SourceDocumentId = context.DocumentId,
            ExtractionJobId = jobId,
            ExtractionRunId = context.RunId,
            FieldName = field,
            Value = value,
            SourceSpan = span,
            SourcePage = page,
            Confidence = confidence,
            Decision = decision,
            DecidedAt = at,
            CreatedAt = at,
        });
        context.RunEvidence.Add((field, confidence));
    }

    /// <summary>
    /// The <see cref="Contract"/> fields extraction must not overwrite: those with a
    /// <see cref="CorrectionHistory"/> row and those whose latest evidence is <c>human_accepted</c>.
    /// Also keeps the latest evidence value per field, so a re-read of a value the human already saw
    /// and decided on is not proposed again.
    /// </summary>
    private async Task<FieldProtection> LoadProtectionAsync(
        TenantId tenantId, Contract contract, CancellationToken cancellationToken)
    {
        var protection = new FieldProtection();

        var corrected = await dbContext.CorrectionHistories
            .AsNoTracking()
            .Where(h => h.TenantId == tenantId
                && h.TargetEntityType == nameof(Contract)
                && h.TargetEntityId == contract.Id)
            .Select(h => h.FieldName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        corrected.ForEach(protection.Protect);

        foreach (var (field, latest) in await LatestEvidenceAsync(tenantId, contract.Id, null, cancellationToken).ConfigureAwait(false))
        {
            protection.NoteLatestEvidence(field, latest.Value);

            // The supplier is a link on the contract, not a value on it: a name accepted while nothing
            // was ever linked defends no choice of the reviewer, so a later confident reading may still
            // link it. Only an existing link, or a supplier the reviewer corrected, is theirs.
            if (latest.Decision == ExtractionConfidencePolicy.HumanAccepted
                && !(string.Equals(field, ExtractionFieldNames.Supplier, StringComparison.OrdinalIgnoreCase) && contract.SupplierId is null))
            {
                protection.Protect(field);
            }
        }

        return protection;
    }

    internal sealed class FieldProtection
    {
        private readonly HashSet<string> _protected = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _latestEvidenceValue = new(StringComparer.OrdinalIgnoreCase);

        public bool IsProtected(string field) => _protected.Contains(field);

        public void Protect(string field) => _protected.Add(field);

        public void NoteLatestEvidence(string field, string? value) => _latestEvidenceValue[field] = value;

        /// <summary>True when <paramref name="value"/> is what extraction last proposed for the field.</summary>
        public bool IsAlreadyProposed(string field, string? value) =>
            _latestEvidenceValue.TryGetValue(field, out var known) && ValuesEquivalent(field, known, value);
    }

    /// <summary>Whether a proposal for a human-owned field would tell the reviewer nothing new: it
    /// equals what the contract already holds, or what extraction proposed last time.</summary>
    private static bool ProposalAddsNothing(Contract contract, FieldProtection protection, string field, string? value) =>
        ValuesEquivalent(field, ReadContractValue(contract, field), value) || protection.IsAlreadyProposed(field, value);

    private static string? ReadContractValue(Contract contract, string field) => field switch
    {
        "currency" => contract.Currency,
        "governingLaw" => contract.GoverningLaw,
        "status" => contract.Status,
        "annualSpend" => contract.AnnualSpend?.ToString(CultureInfo.InvariantCulture),
        "totalContractValue" => contract.TotalContractValue?.ToString(CultureInfo.InvariantCulture),
        "paymentTerms" => contract.PaymentTerms,
        "startDate" => FormatDate(contract.StartDate),
        "endDate" => FormatDate(contract.EndDate),
        "effectiveDate" => FormatDate(contract.EffectiveDate),
        ExtractionFieldNames.CancellationDeadline => FormatDate(contract.CancellationDeadline),
        "autoRenewal" => contract.AutoRenewal ? "true" : "false",
        "renewalTermMonths" => contract.RenewalTermMonths?.ToString(CultureInfo.InvariantCulture),
        ExtractionFieldNames.NoticePeriodDays => contract.NoticePeriodDays?.ToString(CultureInfo.InvariantCulture),
        ExtractionFieldNames.Type => contract.Type.ToString(),
        _ => null,
    };

    private static string? FormatDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Compares two renderings of the same field by its real type (dates, numbers, booleans),
    /// falling back to a trimmed, case-insensitive text comparison.</summary>
    private static bool ValuesEquivalent(string field, string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return field switch
        {
            "startDate" or "endDate" or "effectiveDate" or ExtractionFieldNames.CancellationDeadline =>
                TryParseDate(left, out var leftDate) && TryParseDate(right, out var rightDate) && leftDate == rightDate,
            "annualSpend" or "totalContractValue" =>
                TryParseDecimal(left, out var leftNumber) && TryParseDecimal(right, out var rightNumber) && leftNumber == rightNumber,
            "autoRenewal" =>
                bool.TryParse(left, out var leftFlag) && bool.TryParse(right, out var rightFlag) && leftFlag == rightFlag,
            "renewalTermMonths" or ExtractionFieldNames.NoticePeriodDays =>
                TryParseInt(left, out var leftInt) && TryParseInt(right, out var rightInt) && leftInt == rightInt,
            _ => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase),
        };
    }

    // ------------------------------------------------------------------------------------------
    // Replacing the previous run's rows
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The replace-not-add step of the four list stages: every row of the previous run(s) for this
    /// (contract, source document) goes, except those a human corrected or that
    /// <paramref name="isLinkedElsewhere"/> says something else points at; the new rows are added,
    /// minus any that restate a kept row. Only staged here: the caller's single <c>SaveChangesAsync</c>
    /// commits removals, additions and the stage job together.
    /// </summary>
    private async Task ReplaceRowsAsync<T>(
        ExtractionRun context,
        IQueryable<T> existingQuery,
        DbSet<T> set,
        List<T> newRows,
        Func<T, string> keyOf,
        CancellationToken cancellationToken,
        Func<T, bool>? isLinkedElsewhere = null)
        where T : TenantScopedEntity
    {
        var existing = await existingQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = existing.Select(x => x.Id).ToList();
        var corrected = (await dbContext.CorrectionHistories
                .AsNoTracking()
                .Where(h => h.TenantId == context.TenantId && ids.Contains(h.TargetEntityId))
                .Select(h => h.TargetEntityId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();

        bool IsKept(T row) => corrected.Contains(row.Id) || isLinkedElsewhere?.Invoke(row) == true;

        set.RemoveRange(existing.Where(row => !IsKept(row)));

        var keptKeys = existing.Where(IsKept).Select(keyOf).ToHashSet(StringComparer.Ordinal);
        set.AddRange(newRows.Where(row => !keptKeys.Contains(keyOf(row))));
    }

    private static string NormalizeKeyPart(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    // ------------------------------------------------------------------------------------------
    // F5-D08: cancellation deadline from the notice period
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>cancellationDeadline = endDate - noticePeriodDays</c>, never left to the model's own date
    /// arithmetic. Applies when both inputs are known on the contract and the deadline is not
    /// human-owned. The derived value gets its own evidence row (page and span of the notice clause)
    /// with the weaker of the two inputs' confidences, so a deadline built on a shaky input is
    /// reviewed like any weak fact. A deadline that already equals the derived value is left alone.
    /// </summary>
    private async Task DeriveCancellationDeadlineAsync(ExtractionRun context, CancellationToken cancellationToken)
    {
        var contract = context.Contract;
        if (contract.EndDate is not { } endDate
            || contract.NoticePeriodDays is not { } noticeDays
            || noticeDays < 0
            || context.Protection.IsProtected(ExtractionFieldNames.CancellationDeadline))
        {
            return;
        }

        var derived = endDate.AddDays(-noticeDays);
        if (contract.CancellationDeadline == derived)
        {
            return;
        }

        var latest = await LatestEvidenceAsync(
                context.TenantId, contract.Id, ["endDate", ExtractionFieldNames.NoticePeriodDays], cancellationToken)
            .ConfigureAwait(false);

        // A human-set input counts as certain; an input with no evidence row is unknown, and an
        // unknown confidence is never accepted (ExtractionConfidencePolicy.Decide).
        double? ConfidenceOf(string field) =>
            context.Protection.IsProtected(field)
                ? ExtractionConfidencePolicy.OfficialConfidence
                : latest.TryGetValue(field, out var row) ? row.Confidence : null;

        double? confidence = ConfidenceOf("endDate") is { } endConfidence && ConfidenceOf(ExtractionFieldNames.NoticePeriodDays) is { } noticeConfidence
            ? Math.Min(endConfidence, noticeConfidence)
            : null;

        latest.TryGetValue(ExtractionFieldNames.NoticePeriodDays, out var noticeEvidence);

        contract.CancellationDeadline = derived;

        // Stamped now, not with the run's start: this row must read as newer than the model's own
        // cancellationDeadline row ("latest row per field" wins).
        AddEvidence(
            context,
            ExtractionFieldNames.CancellationDeadline,
            FormatDate(derived),
            confidence,
            ExtractionConfidencePolicy.Decide(confidence),
            clock.UtcNow,
            context.StageJobIds.TryGetValue(ExtractionStage.DatesAndRenewalTerms, out var datesJobId) ? datesJobId : null,
            noticeEvidence?.SourcePage,
            noticeEvidence?.SourceSpan);
    }

    // ------------------------------------------------------------------------------------------
    // Audit detail
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// One audit row's detail: field names and confidence numbers, never a field value (ADR-011 w17
    /// clause 21; the audit table is append-only), plus the run id, the stages a resume reused and the
    /// stages left partial.
    /// </summary>
    private static string BuildExtractionAuditDetail(
        ExtractionRun context, IReadOnlyList<StagedExtractionStageResult> failedStages)
    {
        var fields = string.Join(",",
            context.RunEvidence
                .OrderBy(f => f.FieldName, StringComparer.OrdinalIgnoreCase)
                .Select(f => f.FieldName + ":" + FormatConfidence(f.Confidence)));
        var detail = $"contractId={context.Contract.Id.Value}; fields={fields}; runId={context.RunId}";

        if (context.Plan.Reused.Count > 0)
        {
            detail += "; resumedStages=" + string.Join(",", context.Plan.Reused.Keys.OrderBy(s => s));
        }

        if (failedStages.Count > 0)
        {
            detail += "; partialStages=" + string.Join(",", failedStages.Select(s => $"{s.Stage}({s.FailureKind})"));
        }

        return detail;
    }
}
