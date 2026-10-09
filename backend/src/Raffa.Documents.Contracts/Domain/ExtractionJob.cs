using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// One bounded, schema-constrained extraction task against a document (spec §7.2). The AI
/// Gateway performs the work behind its interface; this row is the durable job record the
/// Worker host tracks to completion (module-map "Worker responsibilities").
/// </summary>
public sealed class ExtractionJob : TenantScopedEntity
{
    public required EntityId DocumentId { get; set; }
    public required ExtractionStage Stage { get; set; }
    public ExtractionJobStatus Status { get; set; } = ExtractionJobStatus.Queued;

    /// <summary>Foundry model id that ran this stage, for cost/usage traceability (brief §8).</summary>
    public string? ModelId { get; set; }

    public required DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorDetail { get; set; }

    /// <summary>
    /// Task E16/F02/US01/T01 (async-processing-schema, ADR-027 §D3): how many times a worker has
    /// claimed this job, incremented atomically by the conditional-<c>UPDATE</c> claim
    /// (<see cref="Infrastructure.ExtractionJobClaimStore"/>) — never by a plain EF
    /// <c>SaveChanges</c>. Defaulted to 0 at the database, so every pre-existing row backfills
    /// safely and the previous API image, which never reads or writes this column, keeps working
    /// unchanged (AC-2). Compared against the application's <c>MaxAttempts</c> so the database,
    /// not the dead-letter queue, owns the terminal state (ADR-027 §D3 round-3 footer §C7).
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// When a worker last claimed this job (ADR-027 §D3). Null until the first claim. Hang
    /// recovery (<see cref="Application.Extraction.HungProcessingDetector"/>) uses it, with
    /// <see cref="StartedAt"/> / <see cref="CompletedAt"/>, as the last-progress heartbeat.
    /// </summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>
    /// Which worker instance last claimed this job (ADR-027 §D3) — an operator-facing identifier
    /// (host/replica name), never a tenant-supplied value. Nullable for the same reason as
    /// <see cref="ClaimedAt"/>.
    /// </summary>
    public string? ClaimedBy { get; set; }

    /// <summary>ADR-027 w15 footer C12 (2026-09-14): the instant a user opened this document while it
    /// was still queued. A prioritised job is taken by the next free Worker slot in its own tenant,
    /// ahead of the FIFO, ordered by this instant ("asked first"). Null for every job nobody waited
    /// for; reset to null by a reprocess, which queues a fresh run nobody has asked for yet.</summary>
    public DateTimeOffset? PrioritisedAt { get; set; }

    /// <summary>F5-T01/F5-T02: identifies one extraction run over a document. The stage jobs of a run and
    /// the facts they wrote share it; a resumed run keeps the id of the run it continues. Null on the
    /// classification job and on older rows.</summary>
    public Guid? ExtractionRunId { get; set; }

    /// <summary>F5-T02 checkpoint: SHA-256 (hex) of the exact page-marked text this stage was given. A
    /// finished stage job is reused by a later run of an <em>incomplete</em> run only when this hash is
    /// unchanged, so a resume never skips a stage on different text. Null for classification and older
    /// rows (never reused).</summary>
    public string? InputHash { get; set; }

    /// <summary>F5-T02: what kind of failure ended a <see cref="ExtractionJobStatus.Failed"/> stage job.</summary>
    public ExtractionStageFailureKind? FailureKind { get; set; }

    /// <summary>F5-T02 checkpoint: facts this stage accepted, so a reused stage reports the same counts
    /// without being re-applied. Null on older rows.</summary>
    public int? ExtractedCount { get; set; }

    /// <summary>F5-T02 checkpoint: facts this stage skipped (see <see cref="ExtractedCount"/>).</summary>
    public int? SkippedCount { get; set; }
}

/// <summary>
/// F5-T02: why a staged-extraction stage failed. <see cref="Transient"/>: the AI provider could not be
/// reached or kept throttling (429 / 5xx / timeout after the in-call retries), so running the stage
/// again later may well succeed. <see cref="Permanent"/>: the stage's own input or output was unusable
/// (empty text, malformed payload). Either way the document is <em>partial</em> and never
/// <c>Completed</c>. Stored as text, so a new value is a code-only change.
/// </summary>
public enum ExtractionStageFailureKind
{
    Transient,
    Permanent,
}
