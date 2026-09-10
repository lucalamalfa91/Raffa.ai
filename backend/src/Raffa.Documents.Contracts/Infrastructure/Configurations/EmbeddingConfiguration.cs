using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

public sealed class EmbeddingConfiguration : IEntityTypeConfiguration<Embedding>
{
    public void Configure(EntityTypeBuilder<Embedding> builder)
    {
        builder.ToTable("embedding");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        // Deliberately no conversion tied to a single parent table — see the type's doc comment.
        builder.Property(e => e.SourceId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.Property(e => e.SourceType).HasMaxLength(100);
        builder.Property(e => e.Model).HasMaxLength(200);

        // Task E13/F04/US01/T02 (R-EVD-01): page-aware chunks. Both nullable — see the entity's
        // own doc comments for why an unknown page is null rather than 1.
        builder.Property(e => e.Section).HasMaxLength(200);

        // ADR-003: the `vector` column type is the whole point of this task. Dimension fixed
        // at schema time per ADR-004 ("small dimension preferred") — see Embedding.VectorDimensions.
        // Requires Pgvector.EntityFrameworkCore's UseVector() to be enabled on the provider
        // (DocumentsContractsDbContextOptions) for the Pgvector.Vector CLR type to map here.
        builder.Property(e => e.Vector)
            .HasColumnType($"vector({Embedding.VectorDimensions})")
            .IsRequired();

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.SourceType, e.SourceId });

        // No HasOne/foreign key here by design: SourceId can point at Document, Clause, ... —
        // a polymorphic reference no single FK constraint can express. Similarity-index choice
        // (HNSW vs IVFFlat) is a later tuning decision, not part of this schema (ADR-003).
    }
}
