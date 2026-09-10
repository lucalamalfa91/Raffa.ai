using Raffa.Suppliers.Products.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Suppliers.Products.Infrastructure.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Supplier"/> (task E13/F03/US01/T01, parent story
/// us-01-supplier-identity). The unique index on (tenant_id, normalized_name) is the database-level
/// half of AC-2 ("'Salesforce, Inc.' and 'salesforce' resolve to one row") — the actual backstop
/// against a race between two concurrent first-seen resolutions of the same name that
/// <see cref="Application.SupplierResolver"/>'s own check-then-act read/insert cannot fully close
/// by itself. <see cref="Supplier.Aliases"/> needs no explicit configuration: the Npgsql EF Core
/// provider maps a <c>string[]</c> CLR property to a Postgres <c>text[]</c> column (with a
/// structural value comparer) by convention.
/// </summary>
public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("supplier");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);

        builder.Property(e => e.Name).HasMaxLength(500);
        builder.Property(e => e.NormalizedName).HasMaxLength(500);
        builder.Property(e => e.Category).HasMaxLength(200);
        builder.Property(e => e.Country).HasMaxLength(200);

        builder.HasIndex(e => e.TenantId);

        // AC-2's actual backstop (see the type doc comment) — one row per (tenant, normalized
        // name), enforced by Postgres itself, not just by SupplierResolver's own read-then-insert.
        builder.HasIndex(e => new { e.TenantId, e.NormalizedName })
            .IsUnique()
            .HasDatabaseName("ix_supplier_tenant_id_normalized_name");
    }
}
