using Raffa.Chat.Domain.WebResearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Chat.Infrastructure.Configurations;

public sealed class WebResearchUsageConfiguration : IEntityTypeConfiguration<WebResearchUsage>
{
    public const string TableName = "chat_web_research_usage";

    public void Configure(EntityTypeBuilder<WebResearchUsage> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(e => new { e.TenantId, e.Day });

        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.Day).HasColumnType("date");
        builder.Property(e => e.Calls);
    }
}
