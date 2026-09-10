using Raffa.Savings.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Savings.Infrastructure.Configurations;

public sealed class SavingsOpportunityConfiguration : IEntityTypeConfiguration<SavingsOpportunity>
{
    public void Configure(EntityTypeBuilder<SavingsOpportunity> builder)
    {
        builder.ToTable("savings_opportunity");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);

        // Cross-module references by id only — deliberately no FK (ADR-002; see the entity's own
        // doc comment), same treatment ContractConfiguration gives Contract.SupplierId.
        builder.Property(e => e.SupplierId)
            .HasConversion(ValueConverters.NullableEntityIdConverter);
        builder.Property(e => e.ContractId)
            .HasConversion(ValueConverters.NullableEntityIdConverter);

        builder.Property(e => e.Type).IsRequired().HasMaxLength(100);
        builder.Property(e => e.CurrentSpend).HasPrecision(18, 2);
        builder.Property(e => e.Currency).IsRequired().HasMaxLength(3);
        builder.Property(e => e.EstimatedSavingsLow).HasPrecision(18, 2);
        builder.Property(e => e.EstimatedSavingsHigh).HasPrecision(18, 2);

        // Same enum-as-string convention as every other module's own closed-set columns (e.g.
        // Raffa.Renewals.Infrastructure.Configurations.RenewalActionConfiguration's `Status`).
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        builder.Property(e => e.Owner).HasMaxLength(200);

        builder.HasIndex(e => e.TenantId);
        // Not unique (unlike RenewalAction's own (tenant, contract) index): a tenant can have many
        // opportunities against the same contract over time (repeat comparisons, different line
        // items) — this index only ever speeds up "opportunities for this contract", never
        // constrains cardinality.
        builder.HasIndex(e => new { e.TenantId, e.ContractId });
    }
}
