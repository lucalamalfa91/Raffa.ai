using Contigo.SharedKernel;

namespace Contigo.Chat.Domain.Conversations;

/// <summary>
/// Base type for every entity owned by the Chat bounded context's conversations store (task
/// E13/F05/US01/T01, story us-01-conversations). ADR-009 requires every business table to carry
/// a not-null, indexed <see cref="TenantId"/>; Postgres Row-Level Security (this task's own
/// migration) is the non-bypassable backstop. This base type is the structural guarantee that no
/// entity added to this module can skip it.
///
/// Deliberately its own copy of
/// <c>Contigo.Documents.Contracts.Domain.TenantScopedEntity</c>'s identical shape, not a shared
/// reference to it: ADR-002's allow-list for <c>Contigo.Chat</c> is exactly
/// <c>[SharedKernel, AiGateway]</c> (<c>Contigo.ArchitectureTests.DependencyDirectionTests</c>) —
/// this module may not reference <c>Contigo.Documents.Contracts</c> at all, so the base type is
/// copied rather than shared, the same way <c>Contigo.Audit.Domain.TenantScopedEntity</c> already
/// is its own copy.
/// </summary>
public abstract class TenantScopedEntity
{
    /// <summary>Client-generated primary key (ADR-003/ADR-009 do not require DB identity columns).</summary>
    public EntityId Id { get; set; } = EntityId.New();

    public required TenantId TenantId { get; set; }
}
