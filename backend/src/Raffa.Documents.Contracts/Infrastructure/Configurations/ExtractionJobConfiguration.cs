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

        // Task E16/F02/US01/T01 (async-processing-schema, ADR-027 §D3): the claim. AttemptCount
        // is NOT NULL with a database default so every pre-existing row backfills to 0 with no
        // manual data migration (AC-1/AC-2); ClaimedAt/ClaimedBy stay nullable — a never-claimed
        // Queued row needs no sentinel.
        builder.Property(e => e.AttemptCount).HasDefaultValue(0);
        builder.Property(e => e.ClaimedBy).HasMaxLength(200);

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
