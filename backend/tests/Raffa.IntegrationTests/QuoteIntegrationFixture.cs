using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Audit.Infrastructure;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.Quotes.Infrastructure;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.IntegrationTests;

/// <summary>
/// Shared fixture for task E05/F01/US01/T01 (quote-extraction) — proves parent story
/// us-01-quote-line-extraction's AC-1/AC-2/AC-4 against a real, migrated Postgres database and the
/// real <c>Raffa.Api</c> composition root, the same "one real host, no hand-rolled container"
/// shape <see cref="R1IntegrationFixture"/> already established for R1. Reuses
/// <see cref="ScriptedR1AiGateway"/> as-is (its own shape is already generic over
/// <c>AiExtractionRequest.StageName</c>, not contract-specific) rather than duplicating a
/// quote-specific scripted gateway, and <see cref="RecordingDocumentStorage"/> as-is (ADR-005/
/// ADR-011: domain code only ever sees <see cref="IDocumentStorage"/>, so one fake serves every
/// module's own upload path).
///
/// Runs every test request through a dedicated, deliberately unprivileged Postgres role (mirrors
/// <see cref="R1IntegrationFixture"/> and every per-module RLS proof in this solution): the
/// Testcontainers bootstrap role is always a superuser, and superusers unconditionally bypass row
/// security, so AC-4's cross-tenant-adjacent claims would otherwise be unverifiable as real
/// isolation (cross-tenant isolation itself is proved separately, at the module level, by
/// <c>Raffa.Quotes.Tests.QuoteRlsCrossTenantIsolationTests</c> — this fixture's own job is
/// proving the wired-up HTTP path, not re-proving RLS itself).
///
/// <para>
/// Task E05/F03/US02/T02 (outcome-propagation) is this fixture's first scenario that actually
/// exercises <c>Raffa.Savings</c> (<see cref="Raffa.Api.NegotiationOutcomePropagationService"/>
/// calls <c>SavingsOpportunityService.UpdateAsync</c>): <see cref="InitializeAsync"/> now also
/// migrates <see cref="SavingsDbContext"/> onto this same Postgres instance, closing the gap this
/// type's own <see cref="ConfigureWebHost"/> already anticipated (its <c>ConnectionStrings:Savings</c>
/// setting pointed at <see cref="_appConnectionString"/> from this fixture's first version onward,
/// but nothing had migrated that schema onto it until now).
/// </para>
/// </summary>
public sealed class QuoteIntegrationFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppRoleName = "raffa_quotes_e2e_app";
    private const string AppRolePassword = "raffa_quotes_e2e_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private string _appConnectionString = string.Empty;

    /// <summary>Fake <see cref="IDocumentStorage"/> the test host is wired with — reused as-is from
    /// <see cref="R1IntegrationFixture"/>'s own sibling type (ADR-005/ADR-011: domain code only
    /// ever sees the interface).</summary>
    public RecordingDocumentStorage DocumentStorage { get; } = new();

    /// <summary>The scripted gateway this fixture's host resolves <see cref="IAiGateway"/> as —
    /// exposed so a test can assert on <see cref="ScriptedR1AiGateway.OcrCallCount"/> (AC-4)
    /// without re-resolving it from the host's service provider.</summary>
    public ScriptedR1AiGateway AiGateway { get; } = new(
        new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()),
        QuoteExtractionScriptedPayloads.PayloadsByStage);

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

        // Task E05/F03/US02/T02 (outcome-propagation) — see this type's own doc comment: the first
        // scenario in this fixture to actually need Raffa.Savings's own schema.
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

            // One unprivileged app role, granted after every module's tables exist — same shape
            // as R1IntegrationFixture.InitializeAsync (see that method's own remarks).
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
        // No scenario in this fixture exercises Renewals — same "point at this run's own
        // Testcontainers instance rather than the static appsettings.Development.json default"
        // rationale as R1IntegrationFixture's own identical lines. Savings *is* now exercised (task
        // E05/F03/US02/T02, outcome-propagation) — its schema is migrated onto this same instance in
        // InitializeAsync above.
        builder.UseSetting("ConnectionStrings:Renewals", _appConnectionString);
        builder.UseSetting("ConnectionStrings:Savings", _appConnectionString);
        // Never actually dialled — IDocumentStorage is replaced with an in-memory fake below.
        builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IDocumentStorage>(DocumentStorage);

            // Overrides AddAiGatewayModule's own TryAddSingleton<IAiGateway, FixtureAiGateway>()
            // — same "append an AddSingleton override after the host's own registration" shape
            // R1IntegrationFixture already uses.
            services.AddSingleton<IAiGateway>(AiGateway);
        });
    }
}
