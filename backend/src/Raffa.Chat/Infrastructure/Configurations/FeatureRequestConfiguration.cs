using Raffa.Chat.Domain.Feedback;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Chat.Infrastructure.Configurations;

public sealed class FeatureRequestConfiguration : IEntityTypeConfiguration<FeatureRequest>
{
    public void Configure(EntityTypeBuilder<FeatureRequest> builder)
    {
        builder.ToTable("feature_request");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ConversationId)
            .HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.MessageId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.Property(e => e.UserId).HasMaxLength(200);
        builder.Property(e => e.GapKey).HasMaxLength(40);
        builder.Property(e => e.GapTitle).HasMaxLength(120);
        builder.Property(e => e.Language).HasMaxLength(5);
        builder.Property(e => e.AnswersJson).HasColumnType("jsonb");
        builder.Property(e => e.Environment).HasMaxLength(20);
        builder.Property(e => e.WorkspaceHash).HasMaxLength(16);
        builder.Property(e => e.Status).HasMaxLength(20);
        builder.Property(e => e.IssueUrl).HasMaxLength(300);
        builder.Property(e => e.PublishError).HasMaxLength(500);

        builder.HasIndex(e => e.TenantId);

        // One offer, one report (ADR-030 D5): a second submission for the same turn is a 409.
        builder.HasIndex(e => new { e.TenantId, e.MessageId }).IsUnique();
    }
}
