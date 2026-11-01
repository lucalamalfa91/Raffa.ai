using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.Chat.Application.Ask4;
using Raffa.Chat.Domain.Conversations;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Fixtures;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Chat.Infrastructure.Fixtures;

/// <summary>
/// AskRaffa 4.0 fixture seeder for Chiedi (order <see cref="FixtureSeedOrder.Chat"/>): the four
/// conversations of the prototype's <c>initial()</c> (<c>conversations.json</c>) — the same in
/// both scenarios — asked by Luca: two recent, two archived.
/// </summary>
public sealed class ChatFixtureSeeder(ChatDbContext dbContext, ITenantContext tenantContext) : IFixtureScenarioSeeder
{
    public const string ConversationsFile = "conversations.json";

    public int Order => FixtureSeedOrder.Chat;

    public Task WipeAsync(FixtureSeedContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SeedAsync(FixtureSeedContext context, string scenario, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var file = context.ReadJson<FixtureConversationsFile>(ConversationsFile);
        if (!file.Scenarios.Contains(scenario, StringComparer.Ordinal))
        {
            return;
        }

        var user = context.User(file.AskedBy).ExternalSubjectId;
        using var _ = tenantContext.BeginScope(context.TenantId);
        foreach (var raw in file.Conversations)
        {
            // Noon UTC of the display date, so the date reads the same in every time zone.
            var at = new DateTimeOffset(raw.WhenDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
            var conversationId = context.Id("conversation", raw.Key);
            dbContext.Conversations.Add(new Conversation
            {
                Id = conversationId,
                TenantId = context.TenantId,
                UserId = user,
                Title = raw.Title,
                CreatedAt = at,
                UpdatedAt = at,
                ArchivedAt = raw.Archived ? at : null,
            });

            var i = 0;
            foreach (var m in raw.Messages)
            {
                var message = m.Who == "user"
                    ? new Application.Conversations.AppendConversationMessageRequest(
                        ConversationRole.You, ConversationMessageKind.Answer, m.Text, "[]", "[]")
                    : AskConversationService.ToRaffaMessage(new AskAnswer(false, m.Text, m.Source, null, null, null));
                dbContext.ConversationMessages.Add(new ConversationMessage
                {
                    Id = context.Id("conversation-message", $"{raw.Key}:{i}"),
                    TenantId = context.TenantId,
                    ConversationId = conversationId,
                    Role = message.Role,
                    Kind = message.Kind,
                    Markdown = message.Markdown,
                    CitationsJson = message.CitationsJson,
                    ActionsJson = message.ActionsJson,
                    CreatedAt = at.AddSeconds(i),
                });
                i++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public sealed record FixtureConversationsFile
    {
        public List<string> Scenarios { get; init; } = [];
        public string AskedBy { get; init; } = "luca";
        public List<FixtureConversation> Conversations { get; init; } = [];
    }

    public sealed record FixtureConversation
    {
        public string Key { get; init; } = "";
        public string Title { get; init; } = "";
        public DateOnly WhenDate { get; init; }
        public bool Archived { get; init; }
        public List<FixtureMessage> Messages { get; init; } = [];
    }

    public sealed record FixtureMessage
    {
        public string Who { get; init; } = "";
        public string Text { get; init; } = "";
        public string? Source { get; init; }
    }
}

public static class ChatFixtureServiceCollectionExtensions
{
    /// <summary>Fixture mode only: the Chiedi conversations seeder and the deterministic answer
    /// service (replaces the AI-backed one).</summary>
    public static IServiceCollection AddChatFixtureSeeder(this IServiceCollection services)
    {
        services.AddScoped<IFixtureScenarioSeeder, ChatFixtureSeeder>();
        services.RemoveAll<IAskAnswerService>();
        services.AddScoped<IAskAnswerService, FixtureAskAnswerService>();
        return services;
    }
}
