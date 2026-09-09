using System.Net.Http.Json;
using Contigo.AiGateway;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Fixtures;
using Contigo.Chat.Infrastructure;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.Savings.Infrastructure;
using Contigo.SharedKernel;
using Contigo.Suppliers.Products.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Contigo.AiEval.TenantFixtures;

/// <summary>
/// One running Contigo API host per tenant fixture, wired for a deterministic, network-free
/// golden-set run (task E13/F06/US01/T02, ask-golden-set; R-EVD-03 "run in CI against the fixture
/// gateway").
///
/// <para>
/// <b>The real pipeline, not a re-implementation</b>: every case goes over HTTP through
/// <c>POST /api/chat/query</c>, which is the host's own thin alias — it creates a conversation and
/// delegates into <c>Contigo.Api.AskCopilotService.AskAsync</c>, the same entry point
/// <c>POST /api/conversations/{id}/messages</c> uses. So the set exercises the whole engine as
/// shipped: domain gate, intent planner, pack composition from the three corpora, the versioned
/// persona prompt, both guards, the reply builders and the per-turn audit row. Nothing about the
/// engine is stubbed — only its <em>stores</em> and its <em>model</em> are.
/// </para>
///
/// <para>
/// <b>What is substituted, and why</b>:
/// <list type="bullet">
/// <item>Four DbContexts move to EF Core InMemory — <see cref="DocumentsContractsDbContext"/>
/// (portfolio + Contract 360), <see cref="SuppliersDbContext"/> (supplier names, i.e. whether a
/// named supplier is known at all), <see cref="SavingsDbContext"/> (the opportunity list the
/// portfolio-strategy and savings packs read) and <see cref="ChatDbContext"/> (conversation
/// persistence). Testcontainers would work, but the task's own instruction is to prefer in-memory
/// wiring, and RLS-backed behaviour against a real Postgres is already
/// <c>Contigo.IntegrationTests</c>' job.</item>
/// <item><see cref="IAiGateway"/> becomes <see cref="FixtureAiGateway"/> behind a
/// <see cref="RecordingAiGateway"/> — unless <c>AiEval__UseFoundry=true</c>, in which case the
/// host's own registration is left alone so a manual run reaches a real Foundry deployment
/// (R-AI-01 picks Foundry when <c>AiGateway:Endpoint</c> is set, fixture otherwise).</item>
/// <item><see cref="IClock"/> is pinned (<see cref="AiEvalOptions.EvaluationInstant"/>).</item>
/// <item><see cref="IAuditWriter"/> becomes a <see cref="RecordingAuditWriter"/> — the engine's
/// own per-turn audit row is where <c>abstainGuardIntervened</c> is reported, which is R-EVD-03's
/// headline assertion.</item>
/// </list>
/// </para>
///
/// <para>
/// The <see cref="InMemoryProviderServices"/> singleton is required, not decorative: the host
/// registers a further six Npgsql-configured DbContexts through the
/// <c>(sp, options) =&gt; ...</c> overload, which hands EF Core the app's root
/// <see cref="IServiceProvider"/> as a fallback source of provider internals. Left to that default,
/// EF Core finds both Npgsql's and InMemory's provider markers in one container and refuses to pick
/// ("Only a single database provider can be registered in a service provider") — the fix its own
/// error message names is one service provider per database provider.
/// </para>
/// </summary>
internal sealed class AskEvalHost : IAsyncDisposable
{
    private static readonly IServiceProvider InMemoryProviderServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, InMemoryModelCustomizer>()
        .BuildServiceProvider();

    // Every module the host requires a connection string for. Never dialled for the four contexts
    // swapped below; the remaining ones (Identity/Workspace, Audit, Renewals, Quotes) are only
    // resolved by endpoints the golden set never calls, so no connection is ever opened.
    private static readonly string[] RequiredConnectionStringKeys =
    [
        "IdentityWorkspace", "DocumentsContracts", "Audit", "Chat", "Suppliers", "Renewals", "Savings", "Quotes",
    ];

    /// <summary><c>Contigo.Api.AskCopilotService</c>'s own <c>AuditResourceType</c> — the marker
    /// that separates the engine's one per-turn row from the conversation store's own rows.</summary>
    private const string AskAuditResourceType = "ask_contigo_v2";

    /// <summary>The <c>X-User-Id</c> every golden turn is asked under (ADR-022's interim,
    /// non-authoritative caller identity). Fixed, so a conversation row written by one run is
    /// indistinguishable from the next — nothing in the set depends on who asked.</summary>
    private const string GoldenSetCallerId = "golden-set@contigo.test";

    private const string UnreachablePlaceholderConnectionString =
        "Host=localhost;Port=5432;Database=contigo_aieval;Username=contigo;Password=contigo;Include Error Detail=true";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private AskEvalHost(
        TenantFixture fixture,
        WebApplicationFactory<Program> factory,
        RecordingAiGateway? gateway,
        RecordingAuditWriter auditWriter)
    {
        Fixture = fixture;
        _factory = factory;
        Gateway = gateway;
        AuditWriter = auditWriter;
        _client = factory.CreateClient();
    }

    public TenantFixture Fixture { get; }

    /// <summary><see langword="null"/> in the manual Foundry mode, where the host's own gateway
    /// registration is deliberately left in place and therefore cannot be observed — the
    /// "zero gateway calls" assertions are skipped in that mode (see
    /// <c>GoldenSetRunner</c>).</summary>
    public RecordingAiGateway? Gateway { get; }

    public RecordingAuditWriter AuditWriter { get; }

    public static async Task<AskEvalHost> StartAsync(TenantFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var documentsDbName = $"aieval-documents-{fixture.Key}";
        var suppliersDbName = $"aieval-suppliers-{fixture.Key}";
        var savingsDbName = $"aieval-savings-{fixture.Key}";
        var chatDbName = $"aieval-chat-{fixture.Key}";

        var auditWriter = new RecordingAuditWriter();
        RecordingAiGateway? gateway = AiEvalOptions.UseFoundry
            ? null
            : new RecordingAiGateway(
                new FixtureAiGateway(
                    new AiGatewayModelOptions(),
                    new FixedClock(AiEvalOptions.EvaluationInstant),
                    new AiGatewayOcrOptions()));

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            ConfigureSettings(builder);

            builder.ConfigureTestServices(services =>
            {
                SwapToInMemory<DocumentsContractsDbContext>(services, documentsDbName);
                SwapToInMemory<SuppliersDbContext>(services, suppliersDbName);
                SwapToInMemory<SavingsDbContext>(services, savingsDbName);
                SwapToInMemory<ChatDbContext>(services, chatDbName);

                // Plain interface registrations: unlike AddDbContext's TryAdd-based core services,
                // "last registration wins" really does hold for a single GetRequiredService<T>()
                // call, so appending is enough to override the host's own registration.
                services.AddSingleton<IClock>(new FixedClock(AiEvalOptions.EvaluationInstant));
                services.AddSingleton<IAuditWriter>(auditWriter);

                if (gateway is not null)
                {
                    services.AddSingleton<IAiGateway>(gateway);
                }
            });
        });

        var host = new AskEvalHost(fixture, factory, gateway, auditWriter);
        await host.SeedAsync().ConfigureAwait(false);
        return host;
    }

    /// <summary>
    /// Asks one question as this fixture's tenant and returns the raw JSON body of the ADR-024 §6
    /// reply. Resets the recording doubles first, so the gateway-call count and the audit row both
    /// belong to exactly this turn.
    /// </summary>
    public async Task<AskTurnResult> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        Gateway?.Reset();
        AuditWriter.Reset();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query")
        {
            Content = JsonContent.Create(new { question }),
        };
        request.Headers.Add("X-Tenant-Id", Fixture.TenantId.Value.ToString());
        request.Headers.Add("X-User-Id", GoldenSetCallerId);

        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // Only the Ask turn's own audit row (R-ASK-09) — the same HTTP call also persists the
        // conversation and both messages, and ConversationService writes its own rows for those.
        // Filtering on the resource type AskCopilotService stamps keeps "exactly one audit row per
        // turn" an assertion about the engine rather than about the conversation store.
        var askAuditEntries = AuditWriter.Entries
            .Where(entry => string.Equals(entry.ResourceType, AskAuditResourceType, StringComparison.Ordinal))
            .Select(entry => (Action: entry.Action, Detail: entry.Detail ?? string.Empty))
            .ToList();

        return new AskTurnResult(
            (int)response.StatusCode, body, Gateway?.Calls.ToList(), askAuditEntries);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync().ConfigureAwait(false);
    }

    private static void ConfigureSettings(IWebHostBuilder builder)
    {
        foreach (var key in RequiredConnectionStringKeys)
        {
            builder.UseSetting($"ConnectionStrings:{key}", UnreachablePlaceholderConnectionString);
        }

        // The blob adapter is constructed at registration time but never dialled — the golden set
        // uploads nothing. The Azurite development shorthand keeps that construction valid without
        // any running emulator.
        builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");
    }

    private static void SwapToInMemory<TContext>(IServiceCollection services, string databaseName)
        where TContext : DbContext
    {
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<TContext>();
        services.AddDbContext<TContext>(options => options
            .UseInMemoryDatabase(databaseName)
            .UseInternalServiceProvider(InMemoryProviderServices));
    }

    /// <summary>
    /// Writes this fixture's rows straight into the InMemory stores through the host's own
    /// registered DbContexts — the same "resolve the real service, skip HTTP" shape
    /// <c>Contigo.Api.Tests.TestSupport.InMemoryAskEngineFactory.SeedContractAsync</c> already
    /// uses, and necessary for the same reason: there is no HTTP-exposed "create a contract" or
    /// "create a supplier" endpoint to seed through.
    /// </summary>
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();

        if (Fixture.Suppliers.Count > 0)
        {
            var suppliers = scope.ServiceProvider.GetRequiredService<SuppliersDbContext>();
            suppliers.Suppliers.AddRange(Fixture.Suppliers);
            await suppliers.SaveChangesAsync().ConfigureAwait(false);
        }

        if (Fixture.Contracts.Count > 0)
        {
            var documents = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            documents.Contracts.AddRange(Fixture.Contracts);
            await documents.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// What one golden case's HTTP turn produced: the reply body plus the two observability channels
/// the assertions need — every <see cref="IAiGateway"/> call the turn made (<see langword="null"/>
/// in the manual Foundry mode, where the gateway is not wrapped) and the engine's own per-turn
/// audit rows.
/// </summary>
internal sealed record AskTurnResult(
    int StatusCode,
    string Body,
    IReadOnlyList<string>? GatewayCalls,
    IReadOnlyList<(string Action, string Detail)> AuditEntries);
