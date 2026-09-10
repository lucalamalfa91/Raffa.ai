using Raffa.SharedKernel;

namespace Raffa.Suppliers.Products.Domain;

/// <summary>
/// Base type for every entity owned by the Suppliers/Products bounded context (task
/// E13/F03/US01/T01, ADR-024 "Supplier identity"). ADR-009 requires every business table —
/// including this module's own — to carry a not-null, indexed <see cref="TenantId"/>, with
/// Postgres Row-Level Security (wired by this module's own `AddTenantRowLevelSecurity` migration)
/// as the non-bypassable backstop. This is a module-local copy of the same shape
/// <c>Raffa.Documents.Contracts.Domain.TenantScopedEntity</c>,
/// <c>Raffa.Renewals.Domain.TenantScopedEntity</c>, and every other module's own copy already
/// use: ADR-002's dependency-direction rule (enforced by
/// <c>Raffa.ArchitectureTests.DependencyDirectionTests</c>) only allows this module to reference
/// <c>Raffa.SharedKernel</c>, never another domain module's internals, so the base type is
/// duplicated per module rather than shared.
/// </summary>
public abstract class TenantScopedEntity
{
    /// <summary>Client-generated primary key (ADR-003/ADR-009 do not require DB identity columns).</summary>
    public EntityId Id { get; set; } = EntityId.New();

    public required TenantId TenantId { get; set; }
}
