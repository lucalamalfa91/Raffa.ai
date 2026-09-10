using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Raffa.Market.Infrastructure;

/// <summary>
/// EF Core has no built-in mapping from <see cref="EntityId"/> (a `readonly record struct` wrapping
/// a <see cref="Guid"/>) to a column — same gap
/// <c>Raffa.Documents.Contracts.Infrastructure.ValueConverters</c> /
/// <c>Raffa.Chat.Infrastructure.ValueConverters</c> already fill for their own modules. No
/// <c>TenantIdConverter</c> here (unlike those two): this module has no tenant-scoped entity at all
/// (see <see cref="MarketDbContext"/>'s own doc comment) and therefore no <c>TenantId</c> column to
/// convert.
/// </summary>
internal static class ValueConverters
{
    public static readonly ValueConverter<EntityId, Guid> EntityIdConverter =
        new(id => id.Value, value => new EntityId(value));
}
