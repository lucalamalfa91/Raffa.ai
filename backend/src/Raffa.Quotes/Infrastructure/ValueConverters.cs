using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Raffa.Quotes.Infrastructure;

/// <summary>
/// Shared EF Core value converters for SharedKernel's strongly-typed id wrappers. EF Core has no
/// built-in mapping from a `readonly record struct` wrapping a <see cref="Guid"/> to a column, so
/// every entity configuration in this module reuses these instead of each writing its own
/// conversion lambda. Module-local (not shared with the other modules' own copies, e.g.
/// <c>Raffa.Savings.Infrastructure.ValueConverters</c>) for the same ADR-002 dependency-direction
/// reason as this module's own <c>Domain.TenantScopedEntity</c>.
/// </summary>
internal static class ValueConverters
{
    public static readonly ValueConverter<EntityId, Guid> EntityIdConverter =
        new(id => id.Value, value => new EntityId(value));

    /// <summary>Task E05/F03/US02/T02 (outcome-propagation) — this module's first nullable
    /// cross-module id column (<c>Domain.NegotiationOutcome.SavingsOpportunityId</c>). Mirrors
    /// <c>Raffa.Savings.Infrastructure.ValueConverters.NullableEntityIdConverter</c> exactly (a
    /// small, deliberate, documented duplicate per module, not a shared helper — see this class's
    /// own doc comment).</summary>
    public static readonly ValueConverter<EntityId?, Guid?> NullableEntityIdConverter =
        new(
            id => id == null ? null : id.Value.Value,
            value => value == null ? null : new EntityId(value.Value));

    public static readonly ValueConverter<TenantId, Guid> TenantIdConverter =
        new(id => id.Value, value => new TenantId(value));
}
