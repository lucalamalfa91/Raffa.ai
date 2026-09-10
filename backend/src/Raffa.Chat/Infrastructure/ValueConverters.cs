using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Raffa.Chat.Infrastructure;

/// <summary>
/// Shared EF Core value converters for SharedKernel's strongly-typed id wrappers. EF Core has no
/// built-in mapping from a `readonly record struct` wrapping a <see cref="Guid"/> to a column, so
/// every entity configuration in this module reuses these instead of each writing its own
/// conversion lambda. Deliberately this module's own copy — not a shared reference — of
/// <c>Raffa.Documents.Contracts.Infrastructure.ValueConverters</c>'s identical shape, for the
/// same ADR-002 allow-list reason <c>Domain.Conversations.TenantScopedEntity</c>'s own doc
/// comment explains.
/// </summary>
internal static class ValueConverters
{
    public static readonly ValueConverter<EntityId, Guid> EntityIdConverter =
        new(id => id.Value, value => new EntityId(value));

    public static readonly ValueConverter<EntityId?, Guid?> NullableEntityIdConverter =
        new(
            id => id == null ? null : id.Value.Value,
            value => value == null ? null : new EntityId(value.Value));

    public static readonly ValueConverter<TenantId, Guid> TenantIdConverter =
        new(id => id.Value, value => new TenantId(value));
}
