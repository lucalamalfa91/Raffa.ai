using Raffa.Chat.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Chat.Infrastructure.Configurations;

public sealed class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("conversation_message");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ConversationId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.CitationsJson).HasColumnType("jsonb");
        builder.Property(e => e.ActionsJson).HasColumnType("jsonb");
        builder.Property(e => e.PayloadJson).HasColumnType("jsonb");
        builder.Property(e => e.InterviewJson).HasColumnType("jsonb");

        builder.Property(e => e.ModelId).HasMaxLength(200);
        builder.Property(e => e.PromptVersion).HasMaxLength(50);
        builder.Property(e => e.InputHash).HasMaxLength(128);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.ConversationId, e.CreatedAt });

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
