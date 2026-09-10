using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// Append-only snapshot of a <see cref="Contract"/>'s material terms at a point in time —
/// written whenever extraction or a human correction changes the canonical record. Never
/// mutated or deleted (Appendix C rule 5). <see cref="SnapshotJson"/> holds the versioned
/// fields as of <see cref="CreatedAt"/> as a jsonb document so history survives schema growth
/// without a migration per new snapshotted field.
/// </summary>
public sealed class ContractVersion : TenantScopedEntity
{
    public required EntityId ContractId { get; set; }
    public required int VersionNumber { get; set; }
    public required string SnapshotJson { get; set; }
    public string? ChangeReason { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
}
