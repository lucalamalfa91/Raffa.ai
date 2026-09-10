using Raffa.Quotes.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Quotes.Infrastructure.Configurations;

public sealed class QuoteLineConfiguration : IEntityTypeConfiguration<QuoteLine>
{
    public void Configure(EntityTypeBuilder<QuoteLine> builder)
    {
        builder.ToTable("quote_line");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);

        builder.Property(e => e.QuoteId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.Property(e => e.Sku).HasMaxLength(100);
        builder.Property(e => e.Edition).HasMaxLength(100);
        builder.Property(e => e.Description).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.Unit).HasMaxLength(50);
        builder.Property(e => e.Term).HasMaxLength(100);

        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 2);
        builder.Property(e => e.ListPrice).HasPrecision(18, 2);
        builder.Property(e => e.DiscountPercent).HasPrecision(9, 4);
        builder.Property(e => e.ExtendedPrice).HasPrecision(18, 2);

        // Task E05/F01/US01/T02 (quote-normalization): same precision as UnitPrice/ExtendedPrice —
        // NormalizedAnnualUnitPrice is that same unit price, just rescaled to an annual rate.
        builder.Property(e => e.NormalizedAnnualUnitPrice).HasPrecision(18, 2);

        builder.Property(e => e.SourceSpan).HasMaxLength(2000);

        // Task E05/F01/US02/T01 (sku-normalization).
        builder.Property(e => e.NormalizedSku).HasMaxLength(100);
        builder.Property(e => e.NormalizedEdition).HasMaxLength(100);
        // Same enum-as-string convention as QuoteConfiguration's own ProcessingStatus.
        // Explicit HasDefaultValue: without it, EF's migration scaffolder backfills this new
        // NOT NULL column on any pre-existing row with "" (the CLR default for the underlying
        // string-mapped column, not for the enum) — a value SkuMatchStatus's own string converter
        // cannot parse back. Unmatched is also QuoteLine.MatchStatus's own C#-side default, for the
        // same "needs attention until normalization actually runs" reasoning.
        builder.Property(e => e.MatchStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(SkuMatchStatus.Unmatched);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.TenantId, e.QuoteId });
    }
}
