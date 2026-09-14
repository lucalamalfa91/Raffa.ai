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
    /// When a worker last claimed this job (ADR-027 §D3). Null until the first claim; us-02's
    /// handler uses it, alongside a lease window, to decide whether a stale claim may be
    /// reclaimed. Nullable so a never-claimed <see cref="ExtractionJobStatus.Queued"/> row needs
    /// no sentinel value.
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
}
