using Contigo.Chat.Application;
using Contigo.Chat.Application.Capabilities;
using Contigo.Chat.Application.Conversations;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Contigo.Chat.Infrastructure;

/// <summary>
/// Composition-root wiring for the Chat module (ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method"; domain modules never wire themselves into a host
/// directly). Task E02/F04/US01/T01 (query-router) and task E02/F04/US01/T02
/// (deterministic-queries) added <see cref="AskContigoQueryRouter"/>,
/// <see cref="DeterministicQueryPlanner"/> and <see cref="DeterministicQueryHandler"/> but no host
/// took a dependency on any of them yet, so this composition method did not exist —
/// <c>Contigo.Api.Contigo.Api.csproj</c> already carried a <c>ProjectReference</c> to
/// <c>Contigo.Chat.csproj</c> in anticipation of it (see that file). Task E02/F04/US02/T01
/// (rag-citations) is the first thing that needs any of this resolvable from a container — it adds
/// <see cref="RagAnswerService"/> alongside the three pre-existing types, and
/// <c>Contigo.Api.ChatEndpointExtensions</c> (<c>POST /api/chat/query</c>) is the first caller. Task
/// E02/F04/US02/T02 (abstain-guard) adds <see cref="AbstainGuard"/>, a constructor dependency of
/// <see cref="RagAnswerService"/> (see that type's own doc comment).
///
/// Every registration is Scoped, not Singleton: <see cref="RagAnswerService"/> depends on
/// <see cref="IAuditWriter"/>, which <c>Contigo.Audit.Infrastructure.ServiceCollectionExtensions
/// .AddAuditModule</c> registers Scoped (it wraps a Scoped <c>AuditDbContext</c>) — a Singleton
/// <see cref="RagAnswerService"/> would capture that Scoped dependency for the lifetime of the
/// host, which <c>ServiceProviderOptions.ValidateOnBuild</c> (enabled by default for the
/// Development environment <c>WebApplicationFactory</c>-based tests run under) rejects at startup.
/// <see cref="AskContigoQueryRouter"/>/<see cref="DeterministicQueryPlanner"/>/
/// <see cref="DeterministicQueryHandler"/>/<see cref="AbstainGuard"/> have no such constraint but
/// are registered the same way for one uniform per-request/job lifetime across the module — the
/// same choice <c>Contigo.Documents.Contracts.Infrastructure.ServiceCollectionExtensions
/// .AddDocumentsContractsModule</c> already makes for every one of its own services.
///
/// Task E13/F05/US01/T01 (story us-01-conversations, AC-4) adds the optional
/// <paramref name="chatConnectionString"/> the overload below takes: called with no argument
/// (every existing caller — unit tests, and this module's own DI-shape test
/// <c>ServiceCollectionExtensionsTests</c>), <see cref="AddChatModule"/> registers exactly what
/// it always has, unchanged, so nothing that already resolves
/// <see cref="AskContigoQueryRouter"/>/<see cref="RagAnswerService"/> without a database breaks.
/// Called with a connection string, it additionally registers <c>Infrastructure.ChatDbContext</c>
/// and <c>Application.Conversations.ConversationService</c> — the same "a module's own
/// composition method also wires its own DbContext" shape
/// <see cref="Contigo.Documents.Contracts.Infrastructure.ServiceCollectionExtensions.AddDocumentsContractsModule"/>/
/// <see cref="Contigo.Audit.Infrastructure.ServiceCollectionExtensions.AddAuditModule"/> already
/// use, except optional here because — unlike those two modules — this module already has real,
/// non-database callers (the query router/RAG services above) that must keep resolving with zero
/// configuration. Task T02 is the first caller that passes one, from `Contigo.Api.Program`
/// (`ConnectionStrings:Chat`, this story's own council-decided key) — this task deliberately does
/// not touch `Program.cs` itself (see the task's own "Do not touch" list).
///
/// Task E13/F08/US01/T01 (story us-01-capability-catalog) adds <see cref="CapabilityRouting"/> —
/// the phase-2 writer of this file, per that story's own dependency row ("`AddChatModule` file
/// ownership order (T01 phase 1 → this phase 2)"). Registered unconditionally (like the query
/// router/RAG services above, not gated on <paramref name="chatConnectionString"/>): it needs no
/// database, only the static <see cref="CapabilityCatalog"/> it calls directly.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChatModule(
        this IServiceCollection services, string? chatConnectionString = null)
    {
        // TryAdd: any module (or the host) may call this defensively; only the first registration
        // wins, and every module shares the same "now" (IClock) — mirrors
        // Contigo.AiGateway.ServiceCollectionExtensions.AddAiGatewayModule /
        // Contigo.Documents.Contracts.Infrastructure.ServiceCollectionExtensions
        // .AddDocumentsContractsModule.
        services.TryAddSingleton<IClock, SystemClock>();

        services.AddScoped<AskContigoQueryRouter>();
        services.AddScoped<DeterministicQueryPlanner>();
        services.AddScoped<DeterministicQueryHandler>();
        services.AddScoped<AbstainGuard>();
        services.AddScoped<RagAnswerService>();
        services.AddScoped<CapabilityRouting>();

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
        }

        return services;
    }
}
