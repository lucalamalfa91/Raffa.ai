using Raffa.Quotes.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Quotes.Infrastructure.Configurations;

/// <summary>Task E05/F01/US02/T01 (sku-normalization) — this module's second tenant-scoped table
/// (after <c>quote</c>/<c>quote_extraction_job</c>/<c>quote_line</c>). Its own migration must also
/// enable Postgres Row-Level Security for it (mirrors the existing
/// <c>Migrations.AddTenantRowLevelSecurity</c> pattern) — <see cref="Raffa.Quotes.Tests
/// .QuoteRlsMigrationCheckTests"/> discovers this table dynamically from the EF model (every
/// <see cref="TenantScopedEntity"/> subclass) and fails the build if it is missing RLS.</summary>
public sealed class SkuProductMappingConfiguration : IEntityTypeConfiguration<SkuProductMapping>
{
    public void Configure(EntityTypeBuilder<SkuProductMapping> builder)
    {
        builder.ToTable("sku_product_mapping");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);

        builder.Property(e => e.NormalizedSku).IsRequired().HasMaxLength(100);
        builder.Property(e => e.NormalizedEdition).HasMaxLength(100);
        builder.Property(e => e.CanonicalSku).IsRequired().HasMaxLength(100);
        builder.Property(e => e.CanonicalEdition).HasMaxLength(100);
        builder.Property(e => e.CanonicalProductName).HasMaxLength(500);

        builder.HasIndex(e => e.TenantId);

        // One canonical mapping per tenant per normalized SKU -- SkuNormalizationService's own
        // lookup dictionary relies on this being unique (ToDictionary would throw on a duplicate
        // key otherwise).
        builder.HasIndex(e => new { e.TenantId, e.NormalizedSku }).IsUnique();
    }
}
