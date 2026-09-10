using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// Base type for every entity owned by the Documents/Contracts bounded context. ADR-009
/// requires every business table to carry a not-null, indexed <see cref="TenantId"/>; Postgres
/// Row-Level Security (wired by us-03) is the non-bypassable backstop. This base type is the
/// structural guarantee that no entity added to this module can skip it.
/// </summary>
public abstract class TenantScopedEntity
{
    /// <summary>Client-generated primary key (ADR-003/ADR-009 do not require DB identity columns).</summary>
    public EntityId Id { get; set; } = EntityId.New();

    public required TenantId TenantId { get; set; }
}
