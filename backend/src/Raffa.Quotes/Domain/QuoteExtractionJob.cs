using Raffa.SharedKernel;

namespace Raffa.Quotes.Domain;

/// <summary>
/// The durable job record for one quote's line-item extraction run (task E05/F01/US01/T01,
/// quote-extraction; parent story us-01-quote-line-extraction AC-1 "...and creates an extraction
/// job"). Mirrors <c>Raffa.Documents.Contracts.Domain.ExtractionJob</c>'s own shape and purpose
/// — the durable row a caller can poll/audit while the AI Gateway `extract` role (behind
/// <c>Raffa.Api.QuoteExtractionPipeline</c>, the one project allowed to see both
/// <c>Raffa.AiGateway</c> and this module — see that type's own doc comment) does the actual
/// work.
///
/// Deliberately has no <c>Stage</c> column (unlike <c>ExtractionJob</c>'s seven-stage pipeline for
/// contracts): this task's own coding objective is exactly one bounded extraction stage — line
/// items — so a single-valued discriminator column would carry no information; a future task that
/// adds a second quote extraction stage (for example a dedicated "terms" stage) is that task's own
/// migration to add, not invented here ahead of need (same restraint
/// <c>Raffa.Savings.Domain.SavingsOpportunity</c>'s own doc comment documents for its own
/// still-missing columns).
/// </summary>
public sealed class QuoteExtractionJob : TenantScopedEntity
{
    public required EntityId QuoteId { get; set; }

    public QuoteExtractionJobStatus Status { get; set; } = QuoteExtractionJobStatus.Queued;

    /// <summary>Foundry model id that ran this job, for cost/usage traceability (brief §8) —
    /// mirrors <c>Raffa.Documents.Contracts.Domain.ExtractionJob.ModelId</c>.</summary>
    public string? ModelId { get; set; }

    public required DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorDetail { get; set; }
}
