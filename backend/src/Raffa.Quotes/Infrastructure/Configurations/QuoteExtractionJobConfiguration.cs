using Raffa.Quotes.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Quotes.Infrastructure.Configurations;

public sealed class QuoteExtractionJobConfiguration : IEntityTypeConfiguration<QuoteExtractionJob>
{
    public void Configure(EntityTypeBuilder<QuoteExtractionJob> builder)
    {
        builder.ToTable("quote_extraction_job");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);

        // Same-module reference (the Quote this job runs against), deliberately no FK -- mirrors
        // every other module's own "cross-row, no physical FK" convention (see e.g.
        // Raffa.Savings.Infrastructure.Configurations.RealizedSavingsConfiguration's
        // SavingsOpportunityId) so a correction/replay flow is never blocked by referential
        // integrity if a Quote row is ever superseded.
        builder.Property(e => e.QuoteId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ModelId).HasMaxLength(200);
        builder.Property(e => e.ErrorDetail).HasMaxLength(1000);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.TenantId, e.QuoteId });
    }
}
