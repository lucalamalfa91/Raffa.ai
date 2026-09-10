using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

public sealed class ExtractionJobConfiguration : IEntityTypeConfiguration<ExtractionJob>
{
    public void Configure(EntityTypeBuilder<ExtractionJob> builder)
    {
        builder.ToTable("extraction_job");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.DocumentId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.Property(e => e.Stage).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.ModelId).HasMaxLength(200);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => e.DocumentId);
        builder.HasIndex(e => e.Status);

        // Owned by the document: a job is meaningless once its document is gone.
        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(e => e.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
