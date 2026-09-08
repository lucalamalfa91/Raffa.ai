using Contigo.Chat.Domain.Conversations;
using Contigo.Chat.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Contigo.Chat.Infrastructure;

/// <summary>
/// EF Core DbContext for the Chat bounded context's conversations store (task E13/F05/US01/T01,
/// ADR-003). Postgres via npgsql is the only access path; schema changes flow through code-first
/// migrations only (no hand-edited DDL). RLS policies and the ambient per-request tenant claim
/// are wired exactly like <c>Contigo.Documents.Contracts.Infrastructure.DocumentsContractsDbContext</c>
/// / <c>Contigo.Audit.Infrastructure.AuditDbContext</c> — this context only shapes the model and
/// exposes the DbSets. No `HasPostgresExtension("vector")` call: unlike Documents/Contracts,
/// neither <see cref="Conversation"/> nor <see cref="ConversationMessage"/> has a vector column.
/// </summary>
public sealed class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ConversationConfiguration());
        modelBuilder.ApplyConfiguration(new ConversationMessageConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
