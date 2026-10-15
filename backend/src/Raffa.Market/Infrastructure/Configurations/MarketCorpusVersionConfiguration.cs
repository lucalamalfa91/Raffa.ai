using Raffa.Market.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Market.Infrastructure.Configurations;

public sealed class MarketCorpusVersionConfiguration : IEntityTypeConfiguration<MarketCorpusVersionEntity>
{
    // Fixed, so the seed row is the same in every migration run.
    private static readonly DateTimeOffset InitialAt = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<MarketCorpusVersionEntity> builder)
    {
        builder.ToTable("market_corpus_version");
        builder.HasKey(v => v.Version);
        builder.Property(v => v.Version).HasMaxLength(100).ValueGeneratedNever();
        builder.Property(v => v.State).HasMaxLength(20);
        builder.Property(v => v.FeedVersion).HasMaxLength(100);

        // At most one active version, enforced by the database: the switch is one transaction that
        // retires the old and activates the new, and a second concurrent activation fails.
        builder.HasIndex(v => v.State)
            .IsUnique()
            .HasFilter("state = 'active'")
            .HasDatabaseName("ux_market_corpus_version_active");

        // The corpus that existed before blue/green is the first active version.
        builder.HasData(new MarketCorpusVersionEntity
        {
            Version = MarketCorpusVersions.Initial,
            State = MarketCorpusStates.Active,
            CreatedAt = InitialAt,
            ActivatedAt = InitialAt,
        });
    }
}
