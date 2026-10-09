using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// F5-T01 / F5-T02 / F5-D02 / F5-D03 / F5-D08: everything that makes a staged-extraction run
/// <b>idempotent, resumable and respectful of what a human already decided</b>, kept apart from the
/// per-stage parsing in <c>StagedExtractionService.cs</c>:
/// <list type="bullet">
/// <item><b>Run id.</b> Every call to <c>RunAsync</c> belongs to one extraction run
/// (<see cref="ExtractionJob.ExtractionRunId"/>), stamped on its stage jobs and on every fact it
/// writes. A stage that applies successfully <em>replaces</em> the previous rows of the same kind
/// for the same (contract, source document) in one <c>SaveChangesAsync</c> (one database
/// transaction), instead of adding next to them: a reprocess, or a recovery after a hang, no longer
/// duplicates line items, clauses, obligations or risks. Rows carrying a human correction are kept.
/// Replacing happens only once the new payload has parsed, so a stage that fails keeps whatever the
/// previous run had stored.</item>
/// <item><b>Human-owned fields.</b> A <see cref="Contract"/> field that has a
/// <see cref="CorrectionHistory"/> row, or whose latest evidence is <c>human_accepted</c>, is never
/// written by extraction. If the new run reads a different value it is recorded as a
/// <c>review_required</c> proposal on the evidence list, never applied.</item>
/// <item><b>Checkpoint and resume.</b> A run whose stages did not all finish is <em>open</em>. A
/// later run over the same text (same <see cref="ExtractionJob.InputHash"/>) continues it: stages
/// that finished are reused as they are, only the others get a new job and a gateway call. A run
/// that finished entirely is never resumed (an explicit reprocess re-extracts everything).</item>
/// <item><b>Typed stage failure.</b> A failed stage records an
/// <see cref="ExtractionStageFailureKind"/>; the document is partial and never <c>Completed</c>.</item>
/// <item><b>Notice period.</b> <c>cancellationDeadline = endDate - noticePeriodDays</c> is computed
/// here, deterministically.</item>
/// </list>
/// </summary>
public sealed partial class StagedExtractionService
{
    /// <summary>Mixed into <see cref="ExtractionJob.InputHash"/> so a checkpoint written by a
    /// different pipeline shape is never reused. Bump when the stage set or schemas change in a way
    /// that makes an old stage result not equivalent.</summary>
    public const string PipelineVersion = "staged-v1";

    private const string ContractCorrectionEntityType = nameof(Contract);

    /// <summary>Field name of the derived cancellation deadline (see <see cref="DatesFields"/>).</summary>
    public const string CancellationDeadlineFieldName = "cancellationDeadline";

    /// <summary>Field name of the extracted notice period, in calendar days (F5-D08).</summary>
    public const string NoticePeriodDaysFieldName = "noticePeriodDays";

    /// <summary>The pipeline's seven stages, in order (shared with the document list / validator,
    /// which decide "partial" from the stage jobs).</summary>
    public static IReadOnlyList<ExtractionStage> Stages => PipelineStages;

    // ------------------------------------------------------------------------------------------
    // Run context
    // ------------------------------------------------------------------------------------------

    /// <summary>What one <c>RunAsync</c> call needs while applying stages. A class, not a record:
    /// the evidence list and the protection state are appended to as stages are applied.</summary>
    private sealed class RunContext(
        TenantId tenantId,
        EntityId documentId,
        Contract contract,
        RunPlan plan,
        int pageCount,
        FieldProtection protection,
        bool replaceUnattributedRows,
        List<(string FieldName, double? Confidence)> runEvidence)
    {
        public TenantId TenantId { get; } = tenantId;

        public EntityId DocumentId { get; } = documentId;

        public Contract Contract { get; } = contract;

        public Guid RunId => Plan.RunId;

        public RunPlan Plan { get; } = plan;

        public int PageCount { get; } = pageCount;

        public FieldProtection Protection { get; } = protection;

        /// <summary>True when this document is the only one linked to the contract, so rows written
        /// before <c>SourceDocumentId</c> was populated (line items, risks) can only be its own.</summary>
        public bool ReplaceUnattributedRows { get; } = replaceUnattributedRows;

        public List<(string FieldName, double? Confidence)> RunEvidence { get; } = runEvidence;

        /// <summary>Job id per stage of this run (new jobs and reused checkpoints alike).</summary>
        public Dictionary<ExtractionStage, EntityId> StageJobIds { get; } = [];
    }

    private sealed record RunPlan(
        Guid RunId,
        string InputHash,
        IReadOnlyDictionary<ExtractionStage, ExtractionJob> Reused,
        bool Resumed);

    // ------------------------------------------------------------------------------------------
    // Planning: fresh run vs. resume
    // ------------------------------------------------------------------------------------------

    private static string ComputeInputHash(string documentText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PipelineVersion + "\n" + documentText)));

    private static bool IsFinished(ExtractionJob job) =>
        job.Status is ExtractionJobStatus.Completed or ExtractionJobStatus.NeedsReview && job.CompletedAt is not null;

    /// <summary>
    /// Decides how this call relates to what earlier calls left behind. Stage jobs that are still
    /// <c>Running</c> belong to a run that stopped without finishing (a hang, a crash, a cancelled
    /// worker); they are closed as interrupted so they cannot be mistaken for live work and the
    /// stage reads as failed until something finishes it.
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

        var fresh = new RunPlan(Guid.NewGuid(), inputHash, new Dictionary<ExtractionStage, ExtractionJob>(), Resumed: false);

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
                .Where(j => j.Stage == stage && IsFinished(j) && string.Equals(j.InputHash, inputHash, StringComparison.Ordinal))
                .OrderByDescending(j => j.CompletedAt)
                .FirstOrDefault();
            if (checkpoint is not null)
            {
                reused[stage] = checkpoint;
            }
        }

        return reused.Count == 0
            ? fresh
            : new RunPlan(latest.RunId, inputHash, reused, Resumed: true);
    }

    // ------------------------------------------------------------------------------------------
    // Stage jobs (Phase 0) and gateway calls (Phase 1)
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// NW-106 phase 0: creates and persists the stage <see cref="ExtractionJob"/> rows up front,
    /// <c>Running</c>, in one <c>SaveChangesAsync</c> — fast, local, sequential writes (never the
    /// bottleneck), done before any Foundry call so every stage's hang-recovery window starts
    /// from the same point <see cref="StartStagesAsync"/> fires its gateway call. F5-T02: a stage
    /// whose checkpoint is reused gets no new job (its slot is <see langword="null"/>); on a fresh
    /// run all seven stages get one, exactly as before.
    /// </summary>
    private async Task<ExtractionJob?[]> CreateStageJobsAsync(
        TenantId tenantId, EntityId documentId, RunPlan plan, CancellationToken cancellationToken)
    {
        var jobs = new ExtractionJob?[PipelineStages.Length];

        for (var i = 0; i < PipelineStages.Length; i++)
        {
            if (plan.Reused.ContainsKey(PipelineStages[i]))
            {
                continue;
            }

            var startedAt = clock.UtcNow;
            var job = new ExtractionJob
            {
                TenantId = tenantId,
                DocumentId = documentId,
                Stage = PipelineStages[i],
                Status = ExtractionJobStatus.Running,
                QueuedAt = startedAt,
                StartedAt = startedAt,
                ExtractionRunId = plan.RunId,
                InputHash = plan.InputHash,
            };
            dbContext.ExtractionJobs.Add(job);
            jobs[i] = job;
        }

        hangWatch?.Heartbeat();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return jobs;
    }

    /// <summary>
    /// NW-106 phase 1: fires the <see cref="IAiGateway.ExtractAsync"/> calls back to back, without
    /// awaiting any of them — each is an independent, schema-constrained read over the same
    /// <paramref name="documentText"/> (none depends on another stage's result), so starting them
    /// together instead of one after another is exactly the fix for the single largest, purely
    /// structural cost in this pipeline. Returns the (still in-flight) tasks in pipeline order;
    /// <see cref="ApplyStageResultAsync"/> awaits and applies each one. A stage whose checkpoint is
    /// reused gets no call (its slot is <see langword="null"/>).
    ///
    /// <para>
    /// Deliberately does <b>not</b> bind <see cref="ExtractionProgressHeartbeat.Bind"/> /
    /// <see cref="ExtractionProgressHeartbeat.BeginFoundryAttempts"/> per stage here: that
    /// durable, per-retry-attempt heartbeat writes <see cref="ExtractionJob.StartedAt"/> through
    /// <c>dbContext</c>, which is not safe to touch from several calls in flight at once. The
    /// caller wraps the whole await-and-apply phase in one
    /// <see cref="ExtractionProgressHeartbeat.BeginMemoryPulses"/> scope instead — the in-memory
    /// hang watch (never dbContext) stays alive for however long the slowest call takes;
    /// <see cref="Raffa.AiGateway.Foundry.FoundryAttemptHeartbeat.NotifyAsync"/> simply no-ops
    /// with nothing bound for these calls, the same safe default a caller with no heartbeat
    /// support at all already got today.
    /// </para>
    /// </summary>
    private Task<Result<AiExtractionResult>>?[] StartStagesAsync(
        string documentText, RunPlan plan, CancellationToken cancellationToken)
    {
        var tasks = new Task<Result<AiExtractionResult>>?[PipelineStages.Length];

        for (var i = 0; i < PipelineStages.Length; i++)
        {
            var stage = PipelineStages[i];
            if (plan.Reused.ContainsKey(stage))
            {
                continue;
            }

            var request = new AiExtractionRequest(
                StageName: stage.ToString(),
                DocumentText: documentText,
                JsonSchema: BuildSchema(stage));

            tasks[i] = aiGateway.ExtractAsync(request, cancellationToken);
        }

        return tasks;
    }

    /// <summary>
    /// F5-T02: a stage that already finished for this very text in the run being continued. Nothing
    /// is applied again; the result is rebuilt from the checkpoint and the evidence it wrote, so
    /// the pipeline's later steps (supplier link, audit detail) see the same picture a full run
    /// would have given them.
    /// </summary>
    private async Task<(StagedExtractionStageResult Result, string? AcceptedSupplierName)> ReuseCheckpointAsync(
        RunContext context, ExtractionJob checkpoint, CancellationToken cancellationToken)
    {
        context.StageJobIds[checkpoint.Stage] = checkpoint.Id;

        var evidence = await dbContext.ExtractionEvidences
            .AsNoTracking()
            .Where(e => e.TenantId == context.TenantId && e.ExtractionJobId == checkpoint.Id)
            .Select(e => new { e.FieldName, e.Confidence, e.Value, e.Decision })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        string? supplierName = null;
        foreach (var row in evidence)
        {
            context.RunEvidence.Add((row.FieldName, row.Confidence));

            if (supplierName is null
                && string.Equals(row.FieldName, SupplierFieldName, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(row.Value)
                && (row.Decision == ExtractionConfidencePolicy.AutoAccepted
                    || row.Decision == ExtractionConfidencePolicy.HumanAccepted)
                && !context.Protection.IsProtected(SupplierFieldName))
            {
                supplierName = row.Value.Trim();
            }
        }

        return (
            new StagedExtractionStageResult(
                checkpoint.Stage, checkpoint.Status, checkpoint.ExtractedCount ?? 0, checkpoint.SkippedCount ?? 0, null),
            supplierName);
    }

    // ------------------------------------------------------------------------------------------
    // Failure typing
    // ------------------------------------------------------------------------------------------

    /// <summary>A gateway failure that means "the provider could not be reached or kept throttling"
    /// (<see cref="AiGatewayErrors.UnavailablePrefix"/>: 429 / 5xx / timeout after the in-call
    /// retries) is transient; anything else (empty text, a rejected request, a payload that does not
    /// parse) is a property of this input.</summary>
    internal static ExtractionStageFailureKind ClassifyFailure(string error) =>
        error.StartsWith(AiGatewayErrors.UnavailablePrefix, StringComparison.Ordinal)
            ? ExtractionStageFailureKind.Transient
            : ExtractionStageFailureKind.Permanent;

    /// <summary>An exception thrown (rather than returned as a failed result) by a gateway call that
    /// is a network-level transient. A cancellation the caller asked for is never one.</summary>
    private static bool IsTransientTransportFault(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or TimeoutException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private async Task<(StagedExtractionStageResult Result, string? AcceptedSupplierName)> FailStageAsync(
        ExtractionStage stage,
        ExtractionJob job,
        string error,
        ExtractionStageFailureKind kind,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        job.Status = ExtractionJobStatus.Failed;
        job.ErrorDetail = Truncate(error);
        job.FailureKind = kind;
        job.CompletedAt = completedAt;
        job.ExtractedCount = 0;
        job.SkippedCount = 0;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (new StagedExtractionStageResult(stage, job.Status, 0, 0, job.ErrorDetail, kind), null);
    }

    // ------------------------------------------------------------------------------------------
    // Human-owned fields
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The <see cref="Contract"/> fields extraction must not overwrite: those with a
    /// <see cref="CorrectionHistory"/> row (a human changed them) and those whose latest
    /// <see cref="ExtractionEvidence"/> is <c>human_accepted</c> (a human confirmed them). Also keeps
    /// the latest evidence value per field, so a re-read of a value the human already saw and
    /// decided on is not proposed again.
    /// </summary>
    private async Task<FieldProtection> LoadProtectionAsync(
        TenantId tenantId, Contract contract, CancellationToken cancellationToken)
    {
        var protection = new FieldProtection();

        var corrected = await dbContext.CorrectionHistories
            .AsNoTracking()
            .Where(h => h.TenantId == tenantId
                && h.TargetEntityType == ContractCorrectionEntityType
                && h.TargetEntityId == contract.Id)
            .Select(h => h.FieldName)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var field in corrected)
        {
            protection.Protect(field);
        }

        var evidence = await dbContext.ExtractionEvidences
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.ContractId == contract.Id)
            .Select(e => new { e.FieldName, e.Value, e.Decision, e.CreatedAt, e.Id })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var latest in evidence
            .GroupBy(e => e.FieldName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id.Value).First()))
        {
            protection.NoteLatestEvidence(latest.FieldName, latest.Value);
            if (!string.Equals(latest.Decision, ExtractionConfidencePolicy.HumanAccepted, StringComparison.Ordinal))
            {
                continue;
            }

            // The supplier is a link on the contract, not a value on it. A name a reviewer accepted
            // while nothing was ever linked (the extraction was too weak to link it) defends no
            // choice of theirs: a later, confident reading may still link it. Only a link that
            // exists, or a supplier the reviewer corrected (CorrectionHistory above), is theirs.
            if (string.Equals(latest.FieldName, SupplierFieldName, StringComparison.OrdinalIgnoreCase)
                && contract.SupplierId is null)
            {
                continue;
            }

            protection.Protect(latest.FieldName);
        }

        return protection;
    }

    private async Task<bool> ContractHasNoOtherDocumentAsync(
        TenantId tenantId, EntityId contractId, EntityId documentId, CancellationToken cancellationToken) =>
        !await dbContext.Documents
            .AnyAsync(d => d.TenantId == tenantId && d.ContractId == contractId && d.Id != documentId, cancellationToken)
            .ConfigureAwait(false);

    private sealed class FieldProtection
    {
        private readonly HashSet<string> _protected = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _latestEvidenceValue = new(StringComparer.OrdinalIgnoreCase);

        public bool IsProtected(string field) => _protected.Contains(field);

        public void Protect(string field) => _protected.Add(field);

        public void NoteLatestEvidence(string field, string? value) => _latestEvidenceValue[field] = value;

        /// <summary>True when <paramref name="value"/> is what extraction last proposed for the field
        /// (so the human has already seen and decided on it).</summary>
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
        CancellationDeadlineFieldName => FormatDate(contract.CancellationDeadline),
        "autoRenewal" => contract.AutoRenewal ? "true" : "false",
        "renewalTermMonths" => contract.RenewalTermMonths?.ToString(CultureInfo.InvariantCulture),
        NoticePeriodDaysFieldName => contract.NoticePeriodDays?.ToString(CultureInfo.InvariantCulture),
        TypeFieldName => contract.Type.ToString(),
        _ => null,
    };

    private static string? FormatDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Compares two renderings of the same field by its real type (dates, numbers, booleans)
    /// and falls back to a trimmed, case-insensitive text comparison.</summary>
    private static bool ValuesEquivalent(string field, string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        switch (field)
        {
            case "startDate":
            case "endDate":
            case "effectiveDate":
            case CancellationDeadlineFieldName:
                return TryParseDate(left, out var leftDate) && TryParseDate(right, out var rightDate) && leftDate == rightDate;
            case "annualSpend":
            case "totalContractValue":
                return TryParseDecimal(left, out var leftNumber) && TryParseDecimal(right, out var rightNumber) && leftNumber == rightNumber;
            case "autoRenewal":
                return bool.TryParse(left, out var leftFlag) && bool.TryParse(right, out var rightFlag) && leftFlag == rightFlag;
            case "renewalTermMonths":
            case NoticePeriodDaysFieldName:
                return int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var leftInt)
                    && int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rightInt)
                    && leftInt == rightInt;
            default:
                return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------------------------------------------
    // Replacing the previous run's rows
    // ------------------------------------------------------------------------------------------

    /// <summary>Ids, among <paramref name="ids"/>, that a human corrected (a
    /// <see cref="CorrectionHistory"/> row targets them).</summary>
    private async Task<HashSet<EntityId>> LoadHumanCorrectedIdsAsync(
        TenantId tenantId, IReadOnlyCollection<EntityId> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var idList = ids.ToList();
        var corrected = await dbContext.CorrectionHistories
            .AsNoTracking()
            .Where(h => h.TenantId == tenantId && idList.Contains(h.TargetEntityId))
            .Select(h => h.TargetEntityId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. corrected];
    }

    /// <summary>
    /// The replace-not-add step shared by the four list stages: every row of the previous run(s)
    /// for this (contract, source document) goes, except those a human corrected or something else
    /// links to; the new rows are added, minus any that restate a row that was kept. Everything is
    /// only staged here; the caller's single <c>SaveChangesAsync</c> commits removals, additions
    /// and the stage job together.
    /// </summary>
    private async Task ReplaceRowsAsync<T>(
        RunContext context,
        IQueryable<T> existingQuery,
        DbSet<T> set,
        List<T> newRows,
        Func<T, bool> isLinkedElsewhere,
        Func<T, string> keyOf,
        CancellationToken cancellationToken)
        where T : TenantScopedEntity
    {
        var existing = await existingQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
        var corrected = await LoadHumanCorrectedIdsAsync(context.TenantId, existing.Select(x => x.Id).ToList(), cancellationToken)
            .ConfigureAwait(false);

        var kept = new List<T>();
        foreach (var row in existing)
        {
            if (corrected.Contains(row.Id) || isLinkedElsewhere(row))
            {
                kept.Add(row);
            }
            else
            {
                set.Remove(row);
            }
        }

        var keptKeys = kept.Select(keyOf).ToHashSet(StringComparer.Ordinal);
        set.AddRange(newRows.Where(row => !keptKeys.Contains(keyOf(row))));
    }

    private static string NormalizeKeyPart(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private Task ReplaceLineItemsAsync(RunContext context, List<ContractLineItem> rows, CancellationToken cancellationToken) =>
        ReplaceRowsAsync(
            context,
            dbContext.ContractLineItems.Where(x => x.TenantId == context.TenantId
                && x.ContractId == context.Contract.Id
                && (x.SourceDocumentId == context.DocumentId
                    || (context.ReplaceUnattributedRows && x.SourceDocumentId == null))),
            dbContext.ContractLineItems,
            rows,
            isLinkedElsewhere: x => x.ProductId is not null,
            keyOf: x => NormalizeKeyPart(x.Sku) + "|" + NormalizeKeyPart(x.Description),
            cancellationToken);

    private async Task ReplaceClausesAsync(RunContext context, List<Clause> rows, CancellationToken cancellationToken)
    {
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
                isLinkedElsewhere: x => referenced.Contains(x.Id),
                keyOf: x => NormalizeKeyPart(x.ClauseType) + "|" + NormalizeKeyPart(x.RawText),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Task ReplaceObligationsAsync(RunContext context, List<Obligation> rows, CancellationToken cancellationToken) =>
        ReplaceRowsAsync(
            context,
            dbContext.Obligations.Where(x => x.TenantId == context.TenantId
                && x.ContractId == context.Contract.Id
                && x.SourceDocumentId == context.DocumentId),
            dbContext.Obligations,
            rows,
            isLinkedElsewhere: _ => false,
            keyOf: x => NormalizeKeyPart(x.ObligationType) + "|" + NormalizeKeyPart(x.Party) + "|" + NormalizeKeyPart(x.Description),
            cancellationToken);

    private Task ReplaceRisksAsync(RunContext context, List<Risk> rows, CancellationToken cancellationToken) =>
        ReplaceRowsAsync(
            context,
            dbContext.Risks.Where(x => x.TenantId == context.TenantId
                && x.ContractId == context.Contract.Id
                && (x.SourceDocumentId == context.DocumentId
                    || (context.ReplaceUnattributedRows && x.SourceDocumentId == null))),
            dbContext.Risks,
            rows,
            isLinkedElsewhere: _ => false,
            keyOf: x => NormalizeKeyPart(x.RiskType) + "|" + NormalizeKeyPart(x.Description),
            cancellationToken);

    // ------------------------------------------------------------------------------------------
    // F5-D08: cancellation deadline from the notice period
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>cancellationDeadline = endDate - noticePeriodDays</c>, deterministic and never left to the
    /// model's own date arithmetic. Applies when both inputs are known on the contract and the
    /// deadline itself is not human-owned. The derived value gets its own evidence row (page and
    /// span of the notice clause) with the weaker of the two inputs' confidences, so a deadline built
    /// on a shaky end date or notice period is reviewed like any other weak fact. A deadline that
    /// already equals the derived value is left alone.
    /// </summary>
    private async Task DeriveCancellationDeadlineAsync(
        RunContext context, CancellationToken cancellationToken)
    {
        var contract = context.Contract;
        if (contract.EndDate is not { } endDate
            || contract.NoticePeriodDays is not { } noticeDays
            || noticeDays < 0
            || context.Protection.IsProtected(CancellationDeadlineFieldName))
        {
            return;
        }

        var derived = endDate.AddDays(-noticeDays);
        if (contract.CancellationDeadline == derived)
        {
            return;
        }

        var evidence = await dbContext.ExtractionEvidences
            .AsNoTracking()
            .Where(e => e.TenantId == context.TenantId
                && e.ContractId == contract.Id
                && (e.FieldName == "endDate" || e.FieldName == NoticePeriodDaysFieldName))
            .Select(e => new { e.FieldName, e.Confidence, e.SourcePage, e.SourceSpan, e.CreatedAt, e.Id })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var latestByField = evidence
            .GroupBy(e => e.FieldName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id.Value).First(),
                StringComparer.Ordinal);

        // A human-set input counts as certain; an input with no evidence row at all is unknown, and
        // an unknown confidence is never accepted (ExtractionConfidencePolicy.Decide).
        double? ConfidenceOf(string field) =>
            context.Protection.IsProtected(field)
                ? ExtractionConfidencePolicy.OfficialConfidence
                : latestByField.TryGetValue(field, out var row) ? row.Confidence : null;

        var endConfidence = ConfidenceOf("endDate");
        var noticeConfidence = ConfidenceOf(NoticePeriodDaysFieldName);
        double? confidence = endConfidence is { } e1 && noticeConfidence is { } e2 ? Math.Min(e1, e2) : null;

        latestByField.TryGetValue(NoticePeriodDaysFieldName, out var noticeEvidence);

        contract.CancellationDeadline = derived;

        // Stamped now, not with the run's start: the derived row must read as newer than the model's
        // own cancellationDeadline row the Dates stage wrote a moment ago ("latest row per field" wins).
        var derivedAt = clock.UtcNow;

        var datesJobId = context.StageJobIds.TryGetValue(ExtractionStage.DatesAndRenewalTerms, out var jobId)
            ? jobId
            : (EntityId?)null;

        dbContext.ExtractionEvidences.Add(new ExtractionEvidence
        {
            TenantId = context.TenantId,
            ContractId = contract.Id,
            SourceDocumentId = context.DocumentId,
            ExtractionJobId = datesJobId,
            ExtractionRunId = context.RunId,
            FieldName = CancellationDeadlineFieldName,
            Value = FormatDate(derived),
            SourceSpan = noticeEvidence?.SourceSpan,
            SourcePage = noticeEvidence?.SourcePage,
            Confidence = confidence,
            Decision = ExtractionConfidencePolicy.Decide(confidence),
            DecidedAt = derivedAt,
            CreatedAt = derivedAt,
        });
        context.RunEvidence.Add((CancellationDeadlineFieldName, confidence));
    }

    // ------------------------------------------------------------------------------------------
    // Audit detail
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// One audit row's detail: field names and confidence numbers, never a field value
    /// (ADR-011 w17 clause 21). The audit table is append-only, so a value written once cannot
    /// be removed. F5: also the run id, the stages a resume reused and the stages left partial.
    /// </summary>
    private static string BuildExtractionAuditDetail(
        EntityId contractId,
        RunPlan plan,
        IReadOnlyList<(string FieldName, double? Confidence)> runEvidence,
        IReadOnlyList<StagedExtractionStageResult> failedStages)
    {
        var fields = string.Join(",",
            runEvidence
                .OrderBy(f => f.FieldName, StringComparer.OrdinalIgnoreCase)
                .Select(f => f.FieldName + ":" + FormatConfidence(f.Confidence)));
        var detail = $"contractId={contractId.Value}; fields={fields}; runId={plan.RunId}";

        if (plan.Resumed)
        {
            detail += "; resumedStages=" + string.Join(",", plan.Reused.Keys.OrderBy(s => s));
        }

        if (failedStages.Count > 0)
        {
            detail += "; partialStages=" + string.Join(",", failedStages.Select(s => $"{s.Stage}({s.FailureKind})"));
        }

        return detail;
    }
}
