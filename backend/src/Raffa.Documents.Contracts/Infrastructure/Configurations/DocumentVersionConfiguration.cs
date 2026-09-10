using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("document_version");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.DocumentId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.Property(e => e.StoragePath).HasMaxLength(1000);
        builder.Property(e => e.Checksum).HasMaxLength(128);
        builder.Property(e => e.CreatedBy).HasMaxLength(200);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.DocumentId, e.VersionNumber }).IsUnique();

        // Owned history row: deleting the document deletes its version history with it
        // (there is no meaning to a DocumentVersion without its Document).
        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(e => e.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
