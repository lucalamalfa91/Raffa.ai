using Raffa.Chat.Domain.Conversations;
using Raffa.Chat.Domain.Feedback;
using Raffa.Chat.Domain.WebResearch;
using Raffa.Chat.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Chat.Infrastructure;

public sealed class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<FeatureRequest> FeatureRequests => Set<FeatureRequest>();
    public DbSet<WebResearchUsage> WebResearchUsage => Set<WebResearchUsage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ConversationConfiguration());
        modelBuilder.ApplyConfiguration(new ConversationMessageConfiguration());
        modelBuilder.ApplyConfiguration(new FeatureRequestConfiguration());
        modelBuilder.ApplyConfiguration(new WebResearchUsageConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
