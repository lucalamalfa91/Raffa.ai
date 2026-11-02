using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Raffa.Market.Infrastructure.Entities;

namespace Raffa.Market.Infrastructure.Configurations;

public sealed class MarketObservationConfiguration : IEntityTypeConfiguration<MarketObservationEntity>
{
    public void Configure(EntityTypeBuilder<MarketObservationEntity> builder)
    {
        builder.ToTable("market_observation");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.WorkspaceKey).HasMaxLength(64);
        builder.Property(o => o.ObservationKey).HasMaxLength(64);
        builder.Property(o => o.SupplierKey).HasMaxLength(200);
        builder.Property(o => o.Supplier).HasMaxLength(200);
        builder.Property(o => o.Category).HasMaxLength(100);
        builder.Property(o => o.Geography).HasMaxLength(100);
        builder.Property(o => o.Currency).HasMaxLength(10);
        builder.Property(o => o.Product).HasMaxLength(200);
        builder.Property(o => o.Sku).HasMaxLength(100);
        builder.Property(o => o.Unit).HasMaxLength(60);
        builder.Property(o => o.UnitPrice).HasPrecision(18, 4);
        builder.Property(o => o.PaymentTerms).HasMaxLength(100);
        builder.Property(o => o.Source).HasMaxLength(40);

        // The same reply is one observation, whatever happens twice (a retry, a replay).
        builder.HasIndex(o => o.ObservationKey).IsUnique().HasDatabaseName("ux_market_observation_key");
        builder.HasIndex(o => new { o.SupplierKey, o.TermMonths }).HasDatabaseName("ix_market_observation_supplier_term");
    }
}
