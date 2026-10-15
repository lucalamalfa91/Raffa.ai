using Raffa.Market.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Market.Infrastructure.Configurations;

public sealed class MarketRecordConfiguration : IEntityTypeConfiguration<MarketRecordEntity>
{
    public void Configure(EntityTypeBuilder<MarketRecordEntity> builder)
    {
        builder.ToTable("market_record");

        // Natural key (task objective: "recordId PK") -- MarketDeal.RecordId itself, not a
        // generated surrogate uuid. ValueGeneratedNever: nothing about this string is
        // database-generated.
        // F7-T08: the key is (record, corpus version) so the next corpus can be built next to the live one.
        builder.HasKey(r => new { r.RecordId, r.CorpusVersion });
        builder.Property(r => r.CorpusVersion).HasMaxLength(100).HasDefaultValue(MarketCorpusVersions.Initial);
        builder.HasIndex(r => r.CorpusVersion);
        builder.Property(r => r.RecordId).HasMaxLength(200).ValueGeneratedNever();

        builder.Property(r => r.FeedVersion).HasMaxLength(100);
        builder.Property(r => r.Provider).HasMaxLength(200);

        // jsonb, not text: task objective's own "json payload" column, same column-type choice
        // Raffa.Chat.Infrastructure.Configurations.ConversationMessageConfiguration already makes
        // for CitationsJson/ActionsJson.
        builder.Property(r => r.PayloadJson).HasColumnType("jsonb");

        builder.Property(r => r.ProvenanceLabel).HasMaxLength(500);

        // F7-T04: nullable on purpose (additive migration; rows that predate it are back-filled by
        // the next ingestion run). 64 = a SHA-256 in hex.
        builder.Property(r => r.PayloadHash).HasMaxLength(64);

        // F7-T02: filterable columns (nullable, additive) and the indexes the read paths use.
        builder.Property(r => r.SupplierNorm).HasMaxLength(400);
        builder.Property(r => r.ProductNorm).HasMaxLength(400);
        builder.Property(r => r.Category).HasMaxLength(200);
        builder.Property(r => r.Geography).HasMaxLength(50);
        builder.Property(r => r.Currency).HasMaxLength(20);
        builder.Property(r => r.UnitMetric).HasMaxLength(100);
        builder.HasIndex(r => r.SupplierNorm);
        builder.HasIndex(r => r.ProductNorm);
        builder.HasIndex(r => new { r.Category, r.Geography });

        builder.HasIndex(r => r.Provider);
    }
}
