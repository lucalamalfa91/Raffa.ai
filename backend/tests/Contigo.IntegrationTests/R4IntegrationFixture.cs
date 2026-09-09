using Contigo.AiGateway;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Fixtures;
using Contigo.Audit.Infrastructure;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.Identity.Workspace.Infrastructure;
using Contigo.Quotes.Infrastructure;
using Contigo.Savings.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Contigo.IntegrationTests;

/// <summary>
/// Shared fixture for task E05/F04/US01/T01 (r4-integration) — proves the parent story
/// us-01-final-integration's R4 definition of success ("a new proposal can be assessed in minutes")
/// against a real, migrated Postgres+pgvector+RLS database and the real <c>Contigo.Api</c>
/// composition root, the same "one real host, no hand-rolled container" shape
/// <see cref="R0IntegrationFixture"/>/<see cref="R1IntegrationFixture"/>/<see cref="R2IntegrationFixture"/>/
/// <see cref="R3IntegrationFixture"/>/<see cref="QuoteIntegrationFixture"/> already established. A
/// distinct type rather than a reuse of <see cref="QuoteIntegrationFixture"/> (each is a different,
/// already-merged task's own artifact, left untouched per this task's own "do not touch unrelated
/// wave artifacts" instruction) — mirrors that type's own DbContext/role/gateway wiring exactly, since
/// this task's own dependency set (quote-normalization, sku-recalculate, target-saving,
/// strategy-evidence, outcome-propagation) needs every module <see cref="QuoteIntegrationFixture"/>
/// already migrates, plus its own R4-specific scripted extraction payload
/// (<see cref="R4ExtractionFixtures"/>, not <see cref="QuoteExtractionScriptedPayloads"/>) so the
/// quote line this fixture's tests upload actually matches a real
/// <c>Contigo.Benchmark.Fixtures.FixtureBenchmarkAdapter</c> catalog row (task E05/F01/US01/T01's own
/// scripted quote never sets a <c>supplier</c>/<c>currency</c>/<c>geography</c>/<c>purchaseDate</c>,
/// so it is not reusable for an assessment/negotiation-strategy proof).
///
/// Runs every test request through a dedicated, deliberately unprivileged Postgres role (mirrors
/// every prior R*IntegrationFixture): the Testcontainers bootstrap role is always a superuser, and
/// superusers unconditionally bypass row security — this fixture's own real-HTTP proof is exactly
/// what surfaced task E05/F04/US01/T01's own <c>MarketAssessmentService</c>/
/// <c>NegotiationStrategyService</c> tenant-scope fix (see those types' own doc comments): under a
/// superuser connection the missing <c>ITenantContext.BeginScope</c> call would have been invisible.
/// </summary>
public sealed class R4IntegrationFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppRoleName = "contigo_r4_app";
    private const string AppRolePassword = "contigo_r4_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private string _appConnectionString = string.Empty;

    /// <summary>Fake <see cref="IDocumentStorage"/> the test host is wired with — never dialled by any
    /// R4 test (no R4 test uploads a document, only a quote — see <see cref="RecordingDocumentStorage"/>'s
    /// own reuse across every R*IntegrationFixture), but <c>Program.cs</c> constructs the real Azure
    /// Blob adapter unconditionally at startup, so this override exists for the same reason
    /// <see cref="R3IntegrationFixture.DocumentStorage"/> does (ADR-005/ADR-011: domain code only ever
    /// sees the interface).</summary>
    public RecordingDocumentStorage DocumentStorage { get; } = new();

    /// <summary>The scripted gateway this fixture's host resolves <see cref="IAiGateway"/> as —
    /// scripted with <see cref="R4ExtractionFixtures.PayloadsByStage"/> (not
    /// <see cref="QuoteExtractionScriptedPayloads.PayloadsByStage"/>) so the one quote
    /// <see cref="R4EndToEndTests"/> uploads extracts the Salesforce/Sales-Cloud-Enterprise line that
    /// fixture is built to match. Reuses <see cref="ScriptedR1AiGateway"/> as-is (its own shape is
    /// already generic over <c>AiExtractionRequest.StageName</c>, not contract- or quote-specific).</summary>
    public ScriptedR1AiGateway AiGateway { get; } = new(
        new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()),
        R4ExtractionFixtures.PayloadsByStage);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var superuserConnectionString = _postgres.GetConnectionString();

        var identityOptions = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(identityOptions, superuserConnectionString);
        await using (var db = new IdentityWorkspaceDbContext(identityOptions.Options))
        {
            await db.Database.MigrateAsync();
        }

        var documentsOptions = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(documentsOptions, superuserConnectionString);
        await using (var db = new DocumentsContractsDbContext(documentsOptions.Options))
        {
            await db.Database.MigrateAsync();
        }

        var quotesOptions = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(quotesOptions, superuserConnectionString);
        await using (var db = new QuotesDbContext(quotesOptions.Options))
        {
            await db.Database.MigrateAsync();
        }

        // This task's own AC-3 ("Record final outcome -> realized savings tracked") needs
        // Contigo.Savings's real schema migrated onto this same instance — same "first fixture to
        // actually need it" shape QuoteIntegrationFixture's own doc comment documents for task
        // E05/F03/US02/T02 (outcome-propagation).
        var savingsOptions = new DbContextOptionsBuilder<SavingsDbContext>();
        SavingsDbContextOptions.Configure(savingsOptions, superuserConnectionString);
        await using (var db = new SavingsDbContext(savingsOptions.Options))
        {
            await db.Database.MigrateAsync();
        }

        var auditOptions = new DbContextOptionsBuilder<AuditDbContext>();
        AuditDbContextOptions.Configure(auditOptions, superuserConnectionString);
        await using (var db = new AuditDbContext(auditOptions.Options))
        {
            await db.Database.MigrateAsync();

            // One unprivileged app role, granted after every module's tables exist — same shape as
            // every prior R*IntegrationFixture/QuoteIntegrationFixture.
            await db.Database.ExecuteSqlRawAsync(
                $"""
                CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
                GRANT USAGE ON SCHEMA public TO {AppRoleName};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
                """);
        }

        _appConnectionString = new NpgsqlConnectionStringBuilder(superuserConnectionString)
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;
    }

    // Explicit implementation — see R0IntegrationFixture's own doc comment on why (xunit's
    // IAsyncLifetime.DisposeAsync and WebApplicationFactory<T>'s own IAsyncDisposable.DisposeAsync
    // must be disambiguated).
    Task IAsyncLifetime.DisposeAsync() => _postgres.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:IdentityWorkspace", _appConnectionString);
        builder.UseSetting("ConnectionStrings:DocumentsContracts", _appConnectionString);
        builder.UseSetting("ConnectionStrings:Audit", _appConnectionString);
        builder.UseSetting("ConnectionStrings:Quotes", _appConnectionString);
        builder.UseSetting("ConnectionStrings:Savings", _appConnectionString);
        // No R4 scenario exercises Renewals, but Program.cs's own fail-fast connection string check
        // requires it regardless (same "point every module at this run's own Testcontainers instance"
        // rationale every prior R*IntegrationFixture gives).
        builder.UseSetting("ConnectionStrings:Renewals", _appConnectionString);
        // Never actually dialled — IDocumentStorage is replaced with an in-memory fake below — but
        // Program.cs's own startup check requires a non-null configuration value to be present (same
        // syntactically-valid-value approach every prior R*IntegrationFixture uses).
        builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");

        // Task E13/F06/US01/T01 (ask-engine) wires AddMarketModule() into every Contigo.Api host,
        // which makes "market-feed" the container's *default* active Benchmark Service adapter
        // (backend/README.md "Market Intelligence"; Contigo.Market's own ServiceCollectionExtensions
        // doc comment) — but this fixture's own R4ExtractionFixtures scripted quote line is built to
        // match Contigo.Benchmark.Fixtures.FixtureBenchmarkAdapter's own catalog row exactly (P25/P50/
        // P75 = 1500/1800/2100, "per seat / year", sample size 512, source "fixture" — see
        // R4EndToEndTests' own assertions), not market-feed's independent mock deals. Pinned back to
        // "fixture" here so this fixture's real, composed HTTP host keeps resolving the deterministic
        // catalog data its own tests were written against, regardless of what a real deployment's
        // default now is.
        //
        // Reconciled with R3EndToEndTests' own divergent-looking fix for the identical root cause
        // (that file's own comment, right above its FixtureBenchmarkAdapter.Single() call): R3 has
        // no dedicated HTTP endpoint to drive for its benchmark-comparison scenario, so it resolves
        // FixtureBenchmarkAdapter directly from the container instead. R4EndToEndTests, by contrast,
        // drives its assessment scenario through a real GET endpoint whose handler resolves
        // IBenchmarkService itself — this fixture cannot reach into that call from the outside, so
        // pinning the *host's* active-adapter config here is the only lever available. Two different
        // mechanisms, one deliberate reason: whether the test reaches the adapter through HTTP or
        // in-process.
        builder.UseSetting(
            $"{Contigo.Benchmark.Configuration.BenchmarkAdapterOptions.SectionName}:" +
            $"{nameof(Contigo.Benchmark.Configuration.BenchmarkAdapterOptions.ActiveAdapter)}",
            "fixture");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IDocumentStorage>(DocumentStorage);

            // Overrides AddAiGatewayModule's own TryAddSingleton<IAiGateway, FixtureAiGateway>() —
            // same "append an AddSingleton override after the host's own registration" shape every
            // prior R*IntegrationFixture uses.
            services.AddSingleton<IAiGateway>(AiGateway);
        });
    }
}
