using Raffa.Chat.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Chat.Infrastructure.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversation");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ScopeContractId)
            .HasConversion(ValueConverters.NullableEntityIdConverter);

        builder.Property(e => e.UserId).HasMaxLength(200);
        // Business invariant "title <= 48 chars" (R-CONV-01) enforced in
        // Application.Conversations.ConversationService; HasMaxLength(48) is the same DB-level
        // backstop convention every other bounded field in this codebase already gets (e.g.
        // Raffa.Documents.Contracts.Domain.Contract.Currency's HasMaxLength(3)).
        builder.Property(e => e.Title).HasMaxLength(48);

        builder.HasIndex(e => e.TenantId);

        // ConversationService.ListRecentAsync's own "tenant + user, most recent first" query
        // (mirrors Raffa.Audit.Infrastructure.Configurations.AuditEventConfiguration's
        // (TenantId, OccurredAt) composite index for the identical reason).
        builder.HasIndex(e => new { e.TenantId, e.UserId, e.UpdatedAt });
    }
}
