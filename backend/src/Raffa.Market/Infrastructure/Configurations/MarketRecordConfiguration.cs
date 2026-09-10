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
        builder.HasKey(r => r.RecordId);
        builder.Property(r => r.RecordId).HasMaxLength(200).ValueGeneratedNever();

        builder.Property(r => r.FeedVersion).HasMaxLength(100);
        builder.Property(r => r.Provider).HasMaxLength(200);

        // jsonb, not text: task objective's own "json payload" column, same column-type choice
        // Raffa.Chat.Infrastructure.Configurations.ConversationMessageConfiguration already makes
        // for CitationsJson/ActionsJson.
        builder.Property(r => r.PayloadJson).HasColumnType("jsonb");

        builder.Property(r => r.ProvenanceLabel).HasMaxLength(500);

        builder.HasIndex(r => r.Provider);
    }
}
