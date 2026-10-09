using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("document");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ContractId)
            .HasConversion(ValueConverters.NullableEntityIdConverter);

        builder.Property(e => e.FileName).HasMaxLength(500);
        builder.Property(e => e.MimeType).HasMaxLength(200);
        builder.Property(e => e.DocumentType).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.StoragePath).HasMaxLength(1000);
        builder.Property(e => e.Checksum).HasMaxLength(128);
        builder.Property(e => e.ProcessingStatus).HasConversion<string>().HasMaxLength(30);

        // Task E13/F04/US01/T02: parsed page count (R-DOC-06) and the first-page preview's own
        // tenant-prefixed storage path (R-DOC-08) — same length budget as StoragePath below.
        builder.Property(e => e.PreviewPath).HasMaxLength(1000);

        // Task E16/F02/US01/T01 (async-processing-schema, ADR-027 §D6): the refusal record.
        // All three nullable, no CHECK, no default — a rejected row is the only kind that ever
        // sets them. RejectionReason is sized like ProcessingStatus (a small closed-set code);
        // RejectionDetectedType like DocumentType (the same ContractDocumentType vocabulary).
        builder.Property(e => e.RejectionReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.RejectionDetectedType).HasConversion<string>().HasMaxLength(50);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => e.ContractId);

        // F5-D01: the same file (same SHA-256) uploaded twice by one tenant is one document and one
        // Contract. The upload service answers "already uploaded" before anything is created; this
        // index is the backstop for two uploads racing past that check. Partial on purpose:
        //   * a Rejected row keeps its checksum but has no blob any more and the only way forward is a
        //     new upload (DocumentReprocessService), so it must not block that re-upload;
        //   * rows created before 2026-10-08 may already hold duplicates (the old
        //     upload path never checked), and creating a unique index over them would fail the
        //     migration -- the upload service still treats them as the existing document.
        builder.HasIndex(e => new { e.TenantId, e.Checksum })
            .IsUnique()
            .HasDatabaseName("ux_document_tenant_checksum")
            .HasFilter("processing_status <> 'Rejected' AND created_at >= '2026-10-08T00:00:00+00'");

        // Cross-entity but intra-module reference (Documents/Contracts owns both Document and
        // Contract); nullable + Restrict because a document may exist before it is classified
        // and linked, and deleting a contract should not silently delete its evidence.
        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(e => e.ContractId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
