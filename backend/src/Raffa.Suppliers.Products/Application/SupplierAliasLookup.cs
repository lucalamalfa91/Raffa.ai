using Microsoft.EntityFrameworkCore;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.Suppliers.Products.Infrastructure;

namespace Raffa.Suppliers.Products.Application;

/// <summary>
/// <see cref="ISupplierAliasLookup"/> implementation (task F6-T09): finds the tenant's supplier the
/// same way <see cref="SupplierResolver"/> does (by <see cref="SupplierNameNormalizer"/>'s key, against
/// the row's normalised name or one of its aliases) but never creates one, and returns the names that
/// supplier is recorded under.
/// </summary>
public sealed class SupplierAliasLookup(SuppliersDbContext dbContext) : ISupplierAliasLookup
{
    public async Task<IReadOnlyList<string>> GetAlternateNamesAsync(
        TenantId tenantId, string rawName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return [];
        }

        var normalized = SupplierNameNormalizer.Normalize(rawName);

        var supplier = await dbContext.Suppliers
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Where(s => s.NormalizedName == normalized || s.Aliases.Contains(normalized))
            .OrderBy(s => s.CreatedAt)
            .ThenBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (supplier is null)
        {
            return [];
        }

        var raw = rawName.Trim();
        return new[] { supplier.Name }
            .Concat(supplier.Aliases)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !string.Equals(name, raw, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
