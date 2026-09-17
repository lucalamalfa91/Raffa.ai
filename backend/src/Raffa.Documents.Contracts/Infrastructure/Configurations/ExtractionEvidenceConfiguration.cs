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
        // character varying, not a SQL enum: FieldName is deliberately not one either.
        builder.Property(e => e.Decision).HasMaxLength(40).HasColumnType("character varying");
        builder.Property(e => e.DecidedAt).HasColumnType("timestamp with time zone");

        // Epic-23 feature-02 (ADR-003 w18 footer clauses 1-2): OverrideValue and BoxX/BoxY/
        // BoxWidth/BoxHeight need no explicit mapping of their own. OverrideValue is unbounded
        // corrected text, exactly like Value above (no HasMaxLength call either) -> "text" by the
        // Npgsql provider's own convention. BoxX/Y/Width/Height are plain nullable doubles, exactly
        // like Confidence above -> nullable "double precision" by the same convention. Nullability
        // for all five already follows from their C# "?" types with zero configuration needed.

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
