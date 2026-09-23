using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

/// <summary>
/// EF configuration for <see cref="ContractLineItemMarketPrice"/>. Table
/// <c>contract_line_item_market_price</c>; its Row-Level Security policy ships in the table's own
/// migration (ADR-009 w16 clause 2). One row per line item per tenant; the row goes with its line
/// item (cascade), since a comparison has no meaning without the line it priced.
/// </summary>
public sealed class ContractLineItemMarketPriceConfiguration : IEntityTypeConfiguration<ContractLineItemMarketPrice>
{
    public void Configure(EntityTypeBuilder<ContractLineItemMarketPrice> builder)
    {
        builder.ToTable("contract_line_item_market_price");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.LineItemId)
            .HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.ContractId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.Property(e => e.RecordId).HasMaxLength(100);
        builder.Property(e => e.Product).HasMaxLength(300);
        builder.Property(e => e.Geography).HasMaxLength(20);
        builder.Property(e => e.Currency).HasMaxLength(3);
        builder.Property(e => e.Provenance).HasMaxLength(300);
        builder.Property(e => e.MatchKind).HasConversion<string>().HasMaxLength(20);

        builder.Property(e => e.UnitPriceP25).HasPrecision(18, 4);
        builder.Property(e => e.UnitPriceP50).HasPrecision(18, 4);
        builder.Property(e => e.UnitPriceP75).HasPrecision(18, 4);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => e.ContractId);
        builder.HasIndex(e => new { e.TenantId, e.LineItemId }).IsUnique();

        builder.HasOne<ContractLineItem>()
            .WithMany()
            .HasForeignKey(e => e.LineItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
