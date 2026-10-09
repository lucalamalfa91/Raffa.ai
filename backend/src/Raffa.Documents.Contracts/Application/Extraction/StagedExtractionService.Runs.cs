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
/// F5-T01 / F5-T02 / F5-D08: what makes a staged-extraction run idempotent, resumable and respectful
/// of what a human already decided (the per-stage parsing stays in <c>StagedExtractionService.cs</c>).
/// <list type="bullet">
/// <item><b>Run id.</b> Every <c>RunAsync</c> belongs to one run (<see cref="ExtractionJob.ExtractionRunId"/>),
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
/// </summary>
public sealed partial class StagedExtractionService
{
    /// <summary>Mixed into <see cref="ExtractionJob.InputHash"/>: bump it when the stage set or schemas
    /// change so that an old checkpoint is never reused.</summary>
    private const string PipelineVersion = "staged-v1";

    /// <summary>What one <c>RunAsync</c> call needs while applying stages. The evidence list and the
    /// protection state are appended to as stages are applied.</summary>
    private sealed record RunContext(
        TenantId TenantId,
        EntityId DocumentId,
        Contract Contract,
        RunPlan Plan,
        int PageCount,
        FieldProtection Protection,
        bool ReplaceUnattributedRows)
    {
        public Guid RunId => Plan.RunId;

        public List<(string FieldName, double? Confidence)> RunEvidence { get; } = [];

        public Dictionary<ExtractionStage, EntityId> StageJobIds { get; } = [];

        /// <summary>First accepted <c>supplier</c> legal name of the run; only <c>metadata</c> produces one.</summary>
        public string? AcceptedSupplierName { get; set; }
    }

    private sealed record RunPlan(Guid RunId, string InputHash, IReadOnlyDictionary<ExtractionStage, ExtractionJob> Reused);

    /// <summary>A stage of this run: its job and its gateway call, still in flight.</summary>
    private sealed record PendingStage(ExtractionJob Job, Task<Result<AiExtractionResult>> Call);

    // ------------------------------------------------------------------------------------------
    // Planning: fresh run vs. resume
    // ------------------------------------------------------------------------------------------

    private static string ComputeInputHash(string documentText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PipelineVersion + "\n" + documentText)));

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
    // Stage jobs and gateway calls
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// NW-106: creates and saves the <c>Running</c> job of every stage that is not reused (one
    /// <c>SaveChangesAsync</c>, so every hang-recovery window starts together), then fires their
    /// <see cref="IAiGateway.ExtractAsync"/> calls back to back without awaiting. The stages are
    /// independent reads over the same text; <see cref="ApplyStageResultAsync"/> awaits and applies
    /// them one at a time because the <c>DbContext</c> is not safe for concurrent use. No per-stage
    /// <see cref="ExtractionProgressHeartbeat"/> is bound for the same reason: only the in-memory
    /// pulses of <c>RunAsync</c> run while the calls are in flight.
    /// </summary>
    private async Task<Dictionary<ExtractionStage, PendingStage>> StartStagesAsync(
        RunContext context, string documentText, CancellationToken cancellationToken)
    {
        var jobs = new Dictionary<ExtractionStage, ExtractionJob>();
        foreach (var stage in PipelineStages.Where(stage => !context.Plan.Reused.ContainsKey(stage)))
        {
            var startedAt = clock.UtcNow;
            var job = new ExtractionJob
            {
                TenantId = context.TenantId,
                DocumentId = context.DocumentId,
                Stage = stage,
                Status = ExtractionJobStatus.Running,
                QueuedAt = startedAt,
                StartedAt = startedAt,
                ExtractionRunId = context.RunId,
                InputHash = context.Plan.InputHash,
            };
            dbContext.ExtractionJobs.Add(job);
            jobs[stage] = job;
        }

        hangWatch?.Heartbeat();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return jobs.ToDictionary(
            entry => entry.Key,
            entry => new PendingStage(
                entry.Value,
                aiGateway.ExtractAsync(
                    new AiExtractionRequest(entry.Key.ToString(), documentText, BuildSchema(entry.Key)), cancellationToken)));
    }

    /// <summary>A stage that already finished for this very text in the run being continued: nothing is
    /// applied again; its result is rebuilt from the checkpoint and the evidence it wrote, so the later
    /// steps (supplier link, audit detail) see what a full run would have given them.</summary>
    private async Task<StagedExtractionStageResult> ReuseCheckpointAsync(
        RunContext context, ExtractionJob checkpoint, CancellationToken cancellationToken)
    {
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

    // ------------------------------------------------------------------------------------------
    // Failure typing
    // ------------------------------------------------------------------------------------------

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

    private async Task<StagedExtractionStageResult> FailStageAsync(
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

        return new StagedExtractionStageResult(job.Stage, job.Status, 0, 0, job.ErrorDetail, kind);
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
        RunContext context,
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

    private sealed class FieldProtection
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
        RunContext context,
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
    private async Task DeriveCancellationDeadlineAsync(RunContext context, CancellationToken cancellationToken)
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
        RunContext context, IReadOnlyList<StagedExtractionStageResult> failedStages)
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
