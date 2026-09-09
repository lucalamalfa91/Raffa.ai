using Contigo.AiGateway;
using Contigo.Chat.Infrastructure;
using Contigo.Documents.Contracts.Domain;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Contigo.Api.Tests.TestSupport;

/// <summary>
/// Swaps the two DB-backed pieces of <c>Contigo.Api.AskCopilotService</c>'s own dependency graph
/// that this project's sibling test classes have always said need a real Postgres —
/// <see cref="DocumentsContractsDbContext"/> (<c>PortfolioQueryService</c>,
/// <c>Contract360QueryService</c>) and <see cref="ChatDbContext"/> (<c>ConversationService</c>) —
/// for the EF Core InMemory provider, plus a recording <see cref="IAiGateway"/> and a no-op
/// <see cref="IAuditWriter"/>, so task E13/F06/US01/T01's own Definition of Done line can be
/// proven by a real HTTP round trip through <c>Program.cs</c>, without a Testcontainer
/// (<c>Contigo.IntegrationTests</c> already proves the RLS-backed, real-Postgres path — this
/// helper proves the reply-contract/pipeline behaviour <em>this</em> project's classes were never
/// able to reach before).
///
/// <para>
/// <c>EmbeddingRetrievalService.SearchAsync</c>'s own pgvector <c>CosineDistance</c> LINQ does not
/// translate under the InMemory provider — harmless here because every scenario this helper serves
/// (greeting/off-domain, structured-fact) never reaches clause retrieval at all (the domain
/// gate/planner decide that before any pack is built); a clause-intent success path stays
/// <c>Contigo.IntegrationTests</c>' job.
/// </para>
/// </summary>
internal static class InMemoryAskEngineFactory
{
    // A dedicated, isolated internal service provider for the InMemory provider only (built once,
    // reused by both swapped DbContexts below — the InMemory provider's own internal services are
    // generic machinery, not tied to a specific `.UseInMemoryDatabase(name)` database name).
    // Required: Program.cs registers six-plus *other* DbContexts (Renewals/Savings/Quotes/Audit/
    // IdentityWorkspace/Suppliers) via the `(sp, options) => ...Configure(...)` AddDbContext
    // overload, which hands EF Core the app's own root IServiceProvider as a fallback source for
    // provider-specific internal services. Left to that default, EF Core's internal-service
    // resolution for these two swapped contexts finds *both* Npgsql's provider markers (from every
    // other module) and InMemory's (from this swap) in that one shared container and refuses to
    // pick one ("Only a single database provider can be registered in a service provider") — the
    // exact fix its own error message names: "maintaining one service provider per database
    // provider".
    // AddSingleton<IModelCustomizer> after AddEntityFrameworkInMemoryDatabase(): plain
    // Microsoft.Extensions.DependencyInjection "last registration wins" for a single
    // GetService<T>() call (this is a freshly built, non-ASP.NET-Core ServiceProvider, so no
    // TryAdd/Add ambiguity to worry about) — overrides the InMemory provider's own default
    // IModelCustomizer with InMemoryModelCustomizer's "strip Embedding.Vector" version (see that
    // type's own doc comment).
    private static readonly IServiceProvider InMemoryProviderServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, InMemoryModelCustomizer>()
        .BuildServiceProvider();

    /// <param name="aiGateway">Usually a <see cref="RecordingAiGateway"/> wrapping a
    /// <c>FixtureAiGateway</c> — replaces the host's own Foundry/fixture registration.</param>
    /// <param name="clock">Defaults to a real <see cref="SystemClock"/>-equivalent when omitted;
    /// pass a <see cref="FixedClock"/> for a scenario whose seeded data is relative to "now" (e.g.
    /// a renewal-window question).</param>
    public static WebApplicationFactory<Program> WithInMemoryAskEngine(
        this WebApplicationFactory<Program> factory, IAiGateway aiGateway, IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(aiGateway);

        var documentsContractsDbName = $"documents-contracts-{Guid.NewGuid()}";
        var chatDbName = $"chat-{Guid.NewGuid()}";

        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // AddDbContext's own core-services registration is TryAdd (first registration wins) —
            // unlike a plain AddSingleton<TInterface> override (see IAiGateway/IAuditWriter below),
            // the existing Npgsql-configured DbContextOptions<T>/T registrations from
            // AddDocumentsContractsModule/AddChatModule must be removed first, or this InMemory
            // swap would be silently ignored and every query would still try (and fail/hang)
            // against the real, unreachable Postgres host those modules' own connection strings
            // name.
            services.RemoveAll<DbContextOptions<DocumentsContractsDbContext>>();
            services.RemoveAll<DocumentsContractsDbContext>();
            services.AddDbContext<DocumentsContractsDbContext>(o => o
                .UseInMemoryDatabase(documentsContractsDbName)
                .UseInternalServiceProvider(InMemoryProviderServices));

            services.RemoveAll<DbContextOptions<ChatDbContext>>();
            services.RemoveAll<ChatDbContext>();
            services.AddDbContext<ChatDbContext>(o => o
                .UseInMemoryDatabase(chatDbName)
                .UseInternalServiceProvider(InMemoryProviderServices));

            // Same "append an AddSingleton override after the host's own registration" shape
            // Contigo.IntegrationTests.AskContigoRagCrossTenantIsolationTests already uses for
            // IAiGateway/IAuditWriter — a plain interface registration (unlike AddDbContext's own
            // TryAdd-based core services above) really does let the last one added win for a
            // single GetRequiredService<T>() call.
            services.AddSingleton<IAiGateway>(aiGateway);
            services.AddSingleton<IAuditWriter>(new NoOpAuditWriter());

            if (clock is not null)
            {
                services.AddSingleton(clock);
            }
        }));
    }

    /// <summary>Writes <paramref name="contract"/> straight into the InMemory
    /// <see cref="DocumentsContractsDbContext"/> <see cref="WithInMemoryAskEngine"/> already wired
    /// — the same "resolve the real service from the host's own container, skip HTTP" shape
    /// <c>Contigo.IntegrationTests.AskContigoRagCrossTenantIsolationTests.IndexChunkAsync</c>
    /// already uses for embeddings (there is no HTTP-exposed "create a contract" endpoint this task
    /// could seed through instead).</summary>
    public static async Task SeedContractAsync(this WebApplicationFactory<Program> factory, Contract contract)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(contract);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        dbContext.Contracts.Add(contract);
        await dbContext.SaveChangesAsync().ConfigureAwait(false);
    }
}
