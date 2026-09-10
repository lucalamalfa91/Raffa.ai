using Raffa.SharedKernel;

namespace Raffa.Quotes.Domain;

/// <summary>
/// Base type for every entity owned by the Quotes bounded context (task E05/F01/US01/T01,
/// quote-extraction — the first task to give <c>Raffa.Quotes</c> a <c>DbContext</c>). ADR-009
/// requires every business table — including this module's own — to carry a not-null, indexed
/// <see cref="TenantId"/>, with Postgres Row-Level Security (wired by this module's own
/// `AddTenantRowLevelSecurity` migration) as the non-bypassable backstop. This is a module-local
/// copy of the same shape <c>Raffa.Audit.Domain.TenantScopedEntity</c>,
/// <c>Raffa.Documents.Contracts.Domain.TenantScopedEntity</c>,
/// <c>Raffa.Identity.Workspace.Domain.TenantScopedEntity</c>,
/// <c>Raffa.Renewals.Domain.TenantScopedEntity</c> and
/// <c>Raffa.Savings.Domain.TenantScopedEntity</c> already use: ADR-002's dependency-direction
/// rule (enforced by <c>Raffa.ArchitectureTests.DependencyDirectionTests</c>) only allows this
/// module to reference <c>Raffa.SharedKernel</c> (plus <c>Raffa.Benchmark</c>), never another
/// domain module's internals, so the base type is duplicated per module rather than shared.
/// </summary>
public abstract class TenantScopedEntity
{
    /// <summary>Client-generated primary key (ADR-003/ADR-009 do not require DB identity columns).</summary>
    public EntityId Id { get; set; } = EntityId.New();

    public required TenantId TenantId { get; set; }
}
