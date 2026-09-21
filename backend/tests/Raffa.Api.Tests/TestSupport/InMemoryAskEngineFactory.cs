using System.Security.Claims;
using System.Text.Encodings.Web;
using Raffa.AiGateway;
using Raffa.Chat.Infrastructure;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.Renewals.Infrastructure;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Raffa.Api.Tests.TestSupport;

/// <summary>
/// Swaps the two DB-backed pieces of <c>Raffa.Api.AskCopilotService</c>'s own dependency graph
/// that this project's sibling test classes have always said need a real Postgres —
/// <see cref="DocumentsContractsDbContext"/> (<c>PortfolioQueryService</c>,
/// <c>Contract360QueryService</c>) and <see cref="ChatDbContext"/> (<c>ConversationService</c>) —
/// for the EF Core InMemory provider, plus a recording <see cref="IAiGateway"/> and a no-op
/// <see cref="IAuditWriter"/>, so task E13/F06/US01/T01's own Definition of Done line can be
/// proven by a real HTTP round trip through <c>Program.cs</c>, without a Testcontainer
/// (<c>Raffa.IntegrationTests</c> already proves the RLS-backed, real-Postgres path — this
/// helper proves the reply-contract/pipeline behaviour <em>this</em> project's classes were never
/// able to reach before).
///
/// <para>
/// <c>EmbeddingRetrievalService.SearchAsync</c>'s own pgvector <c>CosineDistance</c> LINQ does not
/// translate under the InMemory provider — harmless here because every scenario this helper serves
/// (greeting/off-domain, structured-fact) never reaches clause retrieval at all (the domain
/// gate/planner decide that before any pack is built); a clause-intent success path stays
/// <c>Raffa.IntegrationTests</c>' job.
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
    internal static readonly IServiceProvider InMemoryProviderServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, InMemoryModelCustomizer>()
        .BuildServiceProvider();

    /// <param name="aiGateway">Usually a <see cref="RecordingAiGateway"/> wrapping a
    /// <c>FixtureAiGateway</c> — replaces the host's own Foundry/fixture registration.</param>
    /// <param name="clock">Defaults to a real <see cref="SystemClock"/>-equivalent when omitted;
    /// pass a <see cref="FixedClock"/> for a scenario whose seeded data is relative to "now" (e.g.
    /// a renewal-window question).</param>
    /// <param name="documentStorage">Task E13/F04/US01/T01: pass a
    /// <see cref="RecordingDocumentStorage"/> to prove what the upload path did (and did not)
    /// write to blob storage; omitted, the host keeps its own Azure-backed registration, which is
    /// never dialled as long as the test does not upload.</param>
    /// <param name="auditWriter">Task E13/F04/US01/T01: pass a <see cref="RecordingAuditWriter"/> to
    /// assert on the admission gate's single <c>document.rejected</c> row; omitted, audit writes
    /// go to <see cref="NoOpAuditWriter"/> as before.</param>
    public static WebApplicationFactory<Program> WithInMemoryAskEngine(
        this WebApplicationFactory<Program> factory,
        IAiGateway aiGateway,
        IClock? clock = null,
        IDocumentStorage? documentStorage = null,
        IAuditWriter? auditWriter = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(aiGateway);

        var documentsContractsDbName = $"documents-contracts-{Guid.NewGuid()}";
        var chatDbName = $"chat-{Guid.NewGuid()}";
        var identityDbName = $"identity-workspace-{Guid.NewGuid()}";
        var renewalsDbName = $"renewals-{Guid.NewGuid()}";
        var savingsDbName = $"savings-{Guid.NewGuid()}";

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
            // Raffa.IntegrationTests.AskRaffaRagCrossTenantIsolationTests already uses for
            // IAiGateway/IAuditWriter — a plain interface registration (unlike AddDbContext's own
            // TryAdd-based core services above) really does let the last one added win for a
            // single GetRequiredService<T>() call.
            // Task E13/F04/US01/T02: WorkspaceRoleResolver reads workspace_membership when the
            // caller sent no role claim and no role header, so this host needs an identity store
            // it can actually query - otherwise an Admin-only endpoint answers 500 (a dead Npgsql
            // connection) instead of the 403 the contract promises.
            services.RemoveAll<DbContextOptions<IdentityWorkspaceDbContext>>();
            services.RemoveAll<IdentityWorkspaceDbContext>();
            services.AddDbContext<IdentityWorkspaceDbContext>(o => o
                .UseInMemoryDatabase(identityDbName)
                .UseInternalServiceProvider(InMemoryProviderServices));

            // Task E19/F01/US01/T01 (ADR-028 §D1): GET /api/renewals now always reads
            // RenewalActionService.GetActionsAsync for the page's savedAction embedding. Left on
            // the host's Npgsql registration that query 500s against an unreachable Postgres
            // (this helper never swapped RenewalsDbContext before -- the pipeline used to return
            // before that round trip existed).
            services.RemoveAll<DbContextOptions<RenewalsDbContext>>();
            services.RemoveAll<RenewalsDbContext>();
            services.AddDbContext<RenewalsDbContext>(o => o
                .UseInMemoryDatabase(renewalsDbName)
                .UseInternalServiceProvider(InMemoryProviderServices));

            // Savings: the savings-consultant packs read recorded opportunities and persist the
            // lever-generated ones (AskCopilotService.Savings.cs), so the Ask pipeline now opens
            // this context too.
            services.RemoveAll<DbContextOptions<SavingsDbContext>>();
            services.RemoveAll<SavingsDbContext>();
            services.AddDbContext<SavingsDbContext>(o => o
                .UseInMemoryDatabase(savingsDbName)
                .UseInternalServiceProvider(InMemoryProviderServices));

            // Fix 2026-09-14: the X-User-Id -> `oid` bridge and the implicit tenant Admin both moved to
            // RaffaApiFactory.ConfigureWebHost, which every host in this project derives from, so they are
            // no longer registered here (registering the scheme twice throws at startup).

            services.AddSingleton<IAiGateway>(aiGateway);
            services.AddSingleton(auditWriter ?? new NoOpAuditWriter());

            // Task E16/F02/US03/T01 (wave w15, ADR-027 D1-D3): the upload now returns at the store
            // and the content gate + pipeline run on the Worker. This host has no Worker, so a test
            // that needs a *processed* document drains the in-process queue itself
            // (DrainExtractionQueueAsync below) through the real ExtractionRequestedHandler. Two
            // swaps make that possible on the InMemory provider: the raw-SQL claim store cannot
            // execute here (no relational provider), and the handler is otherwise only registered by
            // the Worker's AddExtractionQueueConsumer.
            services.RemoveAll<IExtractionJobClaimStore>();
            services.AddScoped<IExtractionJobClaimStore, InMemoryExtractionJobClaimStore>();
            services.AddSingleton<ClaimLog>();
            services.AddScoped<ExtractionRequestedHandler>();

            if (documentStorage is not null)
            {
                services.AddSingleton(documentStorage);
            }

            if (clock is not null)
            {
                services.AddSingleton(clock);
            }
        }));
    }

    /// <summary>Writes <paramref name="contract"/> straight into the InMemory
    /// <see cref="DocumentsContractsDbContext"/> <see cref="WithInMemoryAskEngine"/> already wired
    /// — the same "resolve the real service from the host's own container, skip HTTP" shape
    /// <c>Raffa.IntegrationTests.AskRaffaRagCrossTenantIsolationTests.IndexChunkAsync</c>
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

    /// <summary>Writes <paramref name="document"/> straight into the InMemory
    /// <see cref="DocumentsContractsDbContext"/>, same shape as <see cref="SeedContractAsync"/>.
    /// Portfolio / Renewals only list contracts that still have a linked document
    /// (<see cref="Raffa.Documents.Contracts.Application.PortfolioQueryService.GetPortfolioAsync"/>),
    /// so list-surface tests seed one next to the contract.</summary>
    public static async Task SeedDocumentAsync(this WebApplicationFactory<Program> factory, Document document)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(document);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        dbContext.Documents.Add(document);
        await dbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>Writes <paramref name="clause"/> straight into the InMemory
    /// <see cref="DocumentsContractsDbContext"/>, same shape as <see cref="SeedContractAsync"/> --
    /// task E30/F02/US01/T01's own notice-evidence fixtures need a real <c>Clause</c> row for
    /// <c>Contract360QueryService.GetByIdAsync</c>'s own Clauses tab, which
    /// <c>AskCopilotService.BuildMatchingClauseItem</c> reads (a plain EF Core query against this
    /// same swapped <see cref="DocumentsContractsDbContext"/> -- unlike embedding/pgvector search,
    /// nothing here needs a real Postgres).</summary>
    public static async Task SeedClauseAsync(this WebApplicationFactory<Program> factory, Clause clause)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clause);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        dbContext.Clauses.Add(clause);
        await dbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>A linked document row so a seeded contract is not a dead leftover
    /// (no documents pointing at it). Defaults to <see cref="DocumentProcessingStatus.Completed"/>
    /// — the state Portfolio/Renewals/Ask's validated surfaces expect.</summary>
    public static Document NewLinkedDocument(
        TenantId tenantId,
        EntityId contractId,
        DocumentProcessingStatus processingStatus = DocumentProcessingStatus.Completed) =>
        new()
        {
            TenantId = tenantId,
            ContractId = contractId,
            FileName = $"{contractId.Value}.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value}/{contractId.Value}.pdf",
            Checksum = $"sha256:{contractId.Value:N}",
            ProcessingStatus = processingStatus,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>
    /// Plays the Worker for this host (task E16/F02/US03/T01): takes every
    /// <see cref="ExtractionRequested"/> the upload path published to the in-process queue and runs
    /// the real <see cref="ExtractionRequestedHandler"/> on it — content gate, then pipeline — in
    /// its own DI scope, in order. Called by a test right after the upload's 201 and before it
    /// asserts on anything processing produces (document type, contract id, evidence, the
    /// <c>document.rejected</c> row). Messages are consumed from the channel, so a second drain is
    /// a no-op, and a message the handler cannot claim (already processed) is skipped by the
    /// handler itself, never by this helper. Returns how many messages were handled.
    /// </summary>
    public static Task<int> DrainExtractionQueueAsync(this WebApplicationFactory<Program> factory) =>
        factory.DrainExtractionQueueAsync(int.MaxValue);

    /// <summary>Same, but stops after <paramref name="maxMessages"/> messages -- a priority test drains ONE
    /// message and looks at what that single delivery did before the rest of the channel runs.</summary>
    public static async Task<int> DrainExtractionQueueAsync(this WebApplicationFactory<Program> factory, int maxMessages)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var queue = factory.Services.GetRequiredService<InMemoryExtractionQueue>();
        var handled = 0;
        while (handled < maxMessages && queue.Reader.TryRead(out var message))
        {
            using var scope = factory.Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ExtractionRequestedHandler>();
            await handler.HandleAsync(message).ConfigureAwait(false);
            handled++;
        }

        return handled;
    }
}

/// <summary>
/// Test-only bridge from the interim <c>X-User-Id</c> header (ADR-022) to the authenticated
/// <c>oid</c> claim <see cref="Raffa.Api.Infrastructure.TokenCallerIdentity"/> actually reads
/// (ADR-010 w15 footer §2.1). Registered as the default authentication scheme by
/// <see cref="InMemoryAskEngineFactory.WithInMemoryAskEngine"/> only — <c>Raffa.Api.Program</c>
/// never sees this type. A missing or blank header is <see cref="AuthenticateResult.NoResult"/>
/// (not <see cref="AuthenticateResult.Fail(string)"/>): this project's own
/// <c>Missing_identity_returns_401</c>/<c>Blank_identity_header_returns_401</c>-shaped tests expect
/// the request to reach the endpoint unauthenticated (<see cref="ClaimsPrincipal.Identity"/>'s
/// <c>IsAuthenticated: false</c>) and be turned into 401 by <c>ICallerIdentity.Resolve()</c> itself,
/// the same failure path a real missing/invalid bearer token takes in production — not short-circuited
/// by the authentication middleware.
/// </summary>
internal sealed class TestUserIdAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestUserId";

    public const string UserEmailHeaderName = "X-User-Email";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No trim, no case change: TokenCallerIdentity.Resolve() applies neither (its own doc
        // comment records that as deliberate for an opaque, case-sensitive `oid`), so this bridge
        // does not either — a header value only whitespace is treated exactly like a missing one,
        // the same "IsNullOrWhiteSpace -> unauthenticated" rule TokenCallerIdentity itself applies.
        // ADR-025 Rule A3 (T14): when a validated token principal is already on the request -- put
        // there by a filter that simulates the bearer token -- the header is ignored outright, not
        // merely out-ranked: NoResult leaves that principal in place untouched.
        if (Context.User?.Identity is { IsAuthenticated: true })
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Request.Headers.TryGetValue("X-User-Id", out var values) ||
            string.IsNullOrWhiteSpace(values.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // "oid": the exact claim type Microsoft.Identity.Web's ClaimsPrincipal.GetObjectId()
        // resolves (TokenCallerIdentity.Resolve()'s own source) — never ClaimTypes.NameIdentifier
        // or another URI form, which GetObjectId() does not recognize.
        var claims = new List<Claim> { new("oid", values.ToString()) };

        // Fix 2026-09-14: a real access token also carries an `email` claim (TokenCallerIdentity
        // .ResolveEmail()'s source; requested as an optional claim by infra/modules/identity), and
        // POST /api/workspaces now needs it for the creator's Email column since the subject
        // became an `oid`. An explicit X-User-Email header wins so a test can model the real
        // shape (GUID subject, separate address); otherwise an X-User-Id that already is an address
        // doubles as the email, which keeps every pre-existing X-User-Id-only test exactly as it
        // was. Neither header, no claim -- the "token without email" branch stays testable.
        if (Request.Headers.TryGetValue(UserEmailHeaderName, out var emailValues) &&
            !string.IsNullOrWhiteSpace(emailValues.ToString()))
        {
            claims.Add(new Claim("email", emailValues.ToString()));
        }
        else if (values.ToString().Contains('@'))
        {
            claims.Add(new Claim("email", values.ToString()));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
