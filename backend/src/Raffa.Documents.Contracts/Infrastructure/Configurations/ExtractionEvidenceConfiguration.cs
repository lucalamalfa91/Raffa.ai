using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

public sealed class ExtractionEvidenceConfiguration : IEntityTypeConfiguration<ExtractionEvidence>
{
    public void Configure(EntityTypeBuilder<ExtractionEvidence> builder)
    {
        builder.ToTable("extraction_evidence");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ContractId)
            .HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.SourceDocumentId)
            .HasConversion(ValueConverters.NullableEntityIdConverter);
        builder.Property(e => e.ExtractionJobId)
            .HasConversion(ValueConverters.NullableEntityIdConverter);

        builder.Property(e => e.FieldName).HasMaxLength(200);
        builder.Property(e => e.SourceSpan).HasMaxLength(500);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.ContractId, e.FieldName });

        // Owned by the contract: evidence has no meaning without it (mirrors ContractLineItemConfiguration).
        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(e => e.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        // Evidence pointer only; see ClauseConfiguration for the same "do not cascade-delete a
        // fact just because its source document is removed" reasoning.
        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(e => e.SourceDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Traceability pointer only; deleting an ExtractionJob (it never is today — jobs are
        // append-only run records) must not cascade-delete the evidence it produced.
        builder.HasOne<ExtractionJob>()
            .WithMany()
            .HasForeignKey(e => e.ExtractionJobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
