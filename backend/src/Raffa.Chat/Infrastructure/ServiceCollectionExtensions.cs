using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Application.Feedback;
using Raffa.Chat.Application.WebResearch;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Chat.Infrastructure;

/// <summary>
/// Composition-root wiring for the Chat module (ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method"; domain modules never wire themselves into a host
/// directly). This module owns the conversation store and its persistence: the optional
/// <paramref name="chatConnectionString"/> below adds <c>Infrastructure.ChatDbContext</c>,
/// <c>Application.Conversations.ConversationService</c>, the feedback write path and the
/// web-research budget. Called with no argument (unit tests, this module's own DI-shape test
/// <c>ServiceCollectionExtensionsTests</c>) it registers only what needs no database.
///
/// The Ask AI logic (router, gate, planner, answer composer, council, drafting, gaps, web
/// research, pack and guards) lives in the AI flows layer, which sits above the modules and is
/// registered by the hosts through <c>AddAiFlows</c> (ADR-002 amendment); this module does not
/// reference it.
///
/// Every registration is Scoped, not Singleton, for one uniform per-request/job lifetime across the
/// module, the safe one for services that depend on <see cref="IAuditWriter"/>, which
/// <c>Raffa.Audit.Infrastructure.ServiceCollectionExtensions.AddAuditModule</c> registers Scoped
/// (it wraps a Scoped <c>AuditDbContext</c>): a Singleton capturing it would be rejected at startup by
/// <c>ServiceProviderOptions.ValidateOnBuild</c> (enabled by default for the Development environment
/// <c>WebApplicationFactory</c>-based tests run under).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChatModule(
        this IServiceCollection services, string? chatConnectionString = null)
    {
        // TryAdd: any module (or the host) may call this defensively; only the first registration
        // wins, and every module shares the same "now" (IClock) — mirrors
        // Raffa.AiGateway.ServiceCollectionExtensions.AddAiGatewayModule /
        // Raffa.Documents.Contracts.Infrastructure.ServiceCollectionExtensions
        // .AddDocumentsContractsModule.
        services.TryAddSingleton<IClock, SystemClock>();

        // The feedback loop's seam (Application.Feedback, ADR-030 D5): the host registers the
        // GitHub publisher and binds Feedback:* before calling this when a token is configured;
        // otherwise these defaults keep every submission "recorded" with no outbound call.
        services.TryAddSingleton(new FeedbackOptions());
        services.TryAddScoped<IFeatureRequestPublisher, NullFeatureRequestPublisher>();

        // F4-T01: the host supplies the tenant's supplier names so the free text of a feedback
        // answer is scrubbed of them before it is published; without a host source none are known.
        services.TryAddScoped<IFeedbackNameSource, NullFeedbackNameSource>();

        // ADR-030 gate 3: the budget (persistence, this module) reads its daily limit through the
        // IWebResearchBudgetLimit port. The web-research flow (AddAiFlows) replaces this closed
        // default with its configured WebResearchOptions; a host without that flow has no web path,
        // so the gate stays closed, consistent with the kill switch being off by default.
        services.TryAddSingleton<IWebResearchBudgetLimit>(new ClosedWebResearchBudgetLimit());

        if (chatConnectionString is not null)
        {
            // TryAdd: same defensive convention as IClock above; every module shares the same
            // ambient tenant claim (ADR-009).
            services.TryAddSingleton<ITenantContext, TenantContext>();

            services.AddDbContext<ChatDbContext>(
                (sp, options) => ChatDbContextOptions.Configure(
                    options, chatConnectionString, sp.GetRequiredService<ITenantContext>()));

            // Scoped: shares the request/job's own DbContext instance (also Scoped, via
            // AddDbContext above) rather than a second, independently-tracked context — same
            // reason every DbContext-backed service in this codebase is Scoped, not Singleton.
            services.AddScoped<ConversationService>();

            // ADR-030 D5: the feedback write path shares the request's own ChatDbContext.
            services.AddScoped<FeedbackService>();

            // ADR-030 gate 3: the daily budget lives in this module's own database.
            services.AddScoped<WebResearchBudget>();
            services.AddScoped<IWebResearchBudget>(sp => sp.GetRequiredService<WebResearchBudget>());
        }

        return services;
    }
}
