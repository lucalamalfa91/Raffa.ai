using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.Suppliers.Products.Domain;
using Raffa.Suppliers.Products.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Suppliers.Products.Application;

/// <summary>
/// <see cref="ISupplierResolver"/> implementation (task E13/F03/US01/T01, parent story
/// us-01-supplier-identity AC-2). Matches an existing tenant-scoped <see cref="Supplier"/> by
/// <see cref="SupplierNameNormalizer"/>'s own normalization of the raw name — against either the
/// row's <see cref="Supplier.NormalizedName"/> or one of its <see cref="Supplier.Aliases"/> —
/// before ever creating a new row — so "Salesforce, Inc." and "salesforce" resolve to one id, not
/// two. The unique index on
/// (tenant_id, normalized_name) (<see cref="Infrastructure.Configurations.SupplierConfiguration"/>)
/// is the database-level backstop against a race between two concurrent first-seen resolutions of
/// the same name; this method itself does a plain check-then-act (read, then insert if absent),
/// the same shape every other module's own upsert-style service already uses (for example
/// <c>Raffa.Renewals.Application.RenewalActionService.SetActionAsync</c>).
/// </summary>
public sealed class SupplierResolver(SuppliersDbContext dbContext, IClock clock) : ISupplierResolver
{
    internal const string BlankNameError = "Supplier name must not be blank.";

    public async Task<Result<SupplierRef>> ResolveAsync(
        TenantId tenantId, string rawName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return Result<SupplierRef>.Failure(BlankNameError);
        }

        var normalizedName = SupplierNameNormalizer.Normalize(rawName);

        var existing = await dbContext.Suppliers
            .Where(s => s.TenantId == tenantId)
            .Where(s => s.NormalizedName == normalizedName || s.Aliases.Contains(normalizedName))
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return Result<SupplierRef>.Success(new SupplierRef(existing.Id, existing.Name));
        }

        var now = clock.UtcNow;
        var supplier = new Supplier
        {
            TenantId = tenantId,
            Name = rawName.Trim(),
            NormalizedName = normalizedName,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Suppliers.Add(supplier);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<SupplierRef>.Success(new SupplierRef(supplier.Id, supplier.Name));
    }
}
