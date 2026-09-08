using Contigo.Chat.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contigo.Chat.Infrastructure.Configurations;

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
        // Markdown/CitationsJson/ActionsJson: deliberately no HasMaxLength -> Postgres `text`/
        // `jsonb` (unbounded free-form content, same convention
        // Contigo.Audit.Domain.AuditEvent.Detail and
        // Contigo.Documents.Contracts.Domain.ContractVersion.SnapshotJson already use).
        builder.Property(e => e.CitationsJson).HasColumnType("jsonb");
        builder.Property(e => e.ActionsJson).HasColumnType("jsonb");

        builder.Property(e => e.ModelId).HasMaxLength(200);
        builder.Property(e => e.PromptVersion).HasMaxLength(50);
        builder.Property(e => e.InputHash).HasMaxLength(128);

        builder.HasIndex(e => e.TenantId);

        // ConversationService.GetAsync's own "one conversation's messages, creation order" read.
        builder.HasIndex(e => new { e.ConversationId, e.CreatedAt });

        // Owned child row of the same aggregate (Contigo.Chat owns both Conversation and
        // ConversationMessage) — deleting a conversation deletes its messages with it, same
        // "history/child row" cascade convention
        // Contigo.Documents.Contracts.Infrastructure.Configurations.ContractVersionConfiguration
        // already uses for Contract -> ContractVersion.
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
