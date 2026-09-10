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
}
