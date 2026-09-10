using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// Shared EF Core value converters for SharedKernel's strongly-typed id wrappers. EF Core has no
/// built-in mapping from a `readonly record struct` wrapping a <see cref="Guid"/> to a column, so
/// every entity configuration in this module reuses these instead of each writing its own
/// conversion lambda. Module-local (not shared with `Raffa.Documents.Contracts`'s own copy) for
/// the same ADR-002 dependency-direction reason as this module's own `Domain.TenantScopedEntity`.
/// </summary>
internal static class ValueConverters
{
    public static readonly ValueConverter<EntityId, Guid> EntityIdConverter =
        new(id => id.Value, value => new EntityId(value));

    public static readonly ValueConverter<TenantId, Guid> TenantIdConverter =
        new(id => id.Value, value => new TenantId(value));
}
