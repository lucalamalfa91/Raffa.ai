using Raffa.Quotes.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Quotes.Infrastructure.Configurations;

public sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.ToTable("quote");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);

        builder.Property(e => e.FileName).IsRequired().HasMaxLength(500);
        builder.Property(e => e.MimeType).IsRequired().HasMaxLength(200);
        builder.Property(e => e.StoragePath).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.Checksum).IsRequired().HasMaxLength(128);

        // Same enum-as-string convention as every other module's own closed-set columns (e.g.
        // Raffa.Savings.Infrastructure.Configurations.SavingsOpportunityConfiguration's
        // `Status`).
        builder.Property(e => e.ProcessingStatus).HasConversion<string>().HasMaxLength(20);

        // Task E05/F02/US01/T01 (market-assessment): the Quote-level benchmark-matching fields —
        // see Quote's own doc comment for why they are nullable and caller-supplied. Same length
        // budget as the equivalent free-text columns on Raffa.Benchmark.Fixtures
        // .FixtureBenchmarkAdapter's own Comparable.Supplier/Geography/Currency (short codes/names,
        // never a long free-text field).
        builder.Property(e => e.Supplier).HasMaxLength(200);
        builder.Property(e => e.Currency).HasMaxLength(3);
        builder.Property(e => e.Geography).HasMaxLength(100);

        builder.HasIndex(e => e.TenantId);
    }
}
