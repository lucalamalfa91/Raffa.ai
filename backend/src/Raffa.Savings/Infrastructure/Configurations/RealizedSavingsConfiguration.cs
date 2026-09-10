using Raffa.Savings.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Savings.Infrastructure.Configurations;

public sealed class RealizedSavingsConfiguration : IEntityTypeConfiguration<RealizedSavings>
{
    public void Configure(EntityTypeBuilder<RealizedSavings> builder)
    {
        builder.ToTable("realized_savings");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);

        // Same-module reference, deliberately no FK -- mirrors SavingsOpportunityConfiguration's
        // own treatment of its cross-module SupplierId/ContractId (see the entity's own doc
        // comment for why this module keeps that convention even here).
        builder.Property(e => e.SavingsOpportunityId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Currency).IsRequired().HasMaxLength(3);

        builder.HasIndex(e => e.TenantId);
        // Not unique: append-only (see the entity's own doc comment) -- a tenant can realize a
        // saving against the same opportunity more than once over time (a correction to a
        // previously-recorded figure is its own new row, never a silent overwrite).
        builder.HasIndex(e => new { e.TenantId, e.SavingsOpportunityId });
    }
}
