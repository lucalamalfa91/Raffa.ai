using Raffa.Market.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Market.Infrastructure.Configurations;

public sealed class MarketEmbeddingConfiguration : IEntityTypeConfiguration<MarketEmbeddingEntity>
{
    public void Configure(EntityTypeBuilder<MarketEmbeddingEntity> builder)
    {
        builder.ToTable("market_embedding");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();

        builder.Property(e => e.RecordId).HasMaxLength(200);
        builder.Property(e => e.Model).HasMaxLength(200);

        // ADR-003: the `vector` column type. Dimension fixed at schema time per ADR-004 -- see
        // MarketEmbeddingEntity.VectorDimensions. Requires Pgvector.EntityFrameworkCore's
        // UseVector() to be enabled on the provider (MarketDbContextOptions) for the
        // Pgvector.Vector CLR type to map here.
        builder.Property(e => e.Vector)
            .HasColumnType($"vector({MarketEmbeddingEntity.VectorDimensions})")
            .IsRequired();

        builder.HasIndex(e => e.RecordId);

        // FK to market_record (task objective: "recordId FK") -- both tables are owned by this
        // same module/DbContext, unlike Raffa.Documents.Contracts.Domain.Embedding's own
        // deliberately-no-FK polymorphic SourceId. Cascade: deleting a market_record (a future
        // ingestion re-seed / feed-retirement path) removes its own embedding chunks with it --
        // same "owned child row" cascade convention
        // Raffa.Chat.Infrastructure.Configurations.ConversationMessageConfiguration already uses
        // for Conversation -> ConversationMessage.
        builder.HasOne<MarketRecordEntity>()
            .WithMany()
            .HasForeignKey(e => e.RecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
