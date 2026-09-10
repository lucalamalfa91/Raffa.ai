using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// Append-only snapshot of a <see cref="Document"/>'s stored content at a point in time. A new
/// row is added whenever the underlying file changes; existing rows are never mutated or
/// deleted (Appendix C rule 5).
/// </summary>
public sealed class DocumentVersion : TenantScopedEntity
{
    public required EntityId DocumentId { get; set; }
    public required int VersionNumber { get; set; }
    public required string StoragePath { get; set; }
    public required string Checksum { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
}
