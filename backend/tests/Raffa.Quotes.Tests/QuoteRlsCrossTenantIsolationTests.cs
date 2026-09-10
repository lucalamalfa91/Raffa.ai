using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Proves the tenant-scoping half of task E05/F01/US01/T01 (quote-extraction): with the RLS
/// policy from this module's own `AddTenantRowLevelSecurity` migration applied and
/// <see cref="TenantRlsConnectionInterceptor"/> setting the per-connection `app.tenant_id` claim,
/// tenant A's connection genuinely cannot read (or write) tenant B's <see cref="QuoteLine"/> row —
/// the isolation is enforced by Postgres itself, not by application-level filtering alone
/// (ADR-009's "belt-and-suspenders"). Mirrors
/// <c>Raffa.Savings.Tests.SavingsOpportunityRlsCrossTenantIsolationTests</c> exactly, scoped to
/// this module's own <see cref="QuotesDbContext"/> and its three tenant-scoped tables.
///
/// Runs all assertions through a dedicated, deliberately unprivileged Postgres role
/// (<see cref="AppRoleName"/>: `NOSUPERUSER NOBYPASSRLS`, not the table owner) — the Testcontainers
/// bootstrap role is always a superuser, and superusers unconditionally bypass row security
/// regardless of policy or `FORCE`, so asserting isolation over that connection would pass
/// vacuously.
/// </summary>
public sealed class QuoteRlsCrossTenantIsolationTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_quotes_app";
    private const string AppRolePassword = "raffa_quotes_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new QuotesDbContext(adminOptions.Options))
        {
            await adminDb.Database.MigrateAsync();

            await adminDb.Database.ExecuteSqlRawAsync(
                $"""
                CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
                GRANT USAGE ON SCHEMA public TO {AppRoleName};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
                """);
        }

        _appConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private QuotesDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new QuotesDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Tenant_cannot_read_another_tenants_quote_line()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        await SeedQuoteWithLineAsync(tenantA, sku: "owned-by-tenant-a");
        await SeedQuoteWithLineAsync(tenantB, sku: "owned-by-tenant-b");

        var tenantContext = new TenantContext();
        using (tenantContext.BeginScope(tenantA))
        {
            await using var db = CreateAppContext(tenantContext);

            var visibleQuotes = await db.Quotes.ToListAsync();
            var visibleLines = await db.QuoteLines.ToListAsync();
            var visibleJobs = await db.QuoteExtractionJobs.ToListAsync();

            Assert.Single(visibleQuotes);
            Assert.Equal(tenantA, Assert.Single(visibleQuotes).TenantId);
            Assert.Single(visibleLines);
            Assert.Equal("owned-by-tenant-a", Assert.Single(visibleLines).Sku);
            Assert.Single(visibleJobs);
        }
    }

    [Fact]
    public async Task No_active_tenant_scope_sees_zero_rows()
    {
        await SeedQuoteWithLineAsync(TenantId.New(), sku: "belongs-to-someone-else");

        // No BeginScope entered: ITenantContext.Current is null, so the interceptor leaves
        // app.tenant_id unset — fail closed, zero rows visible to anyone.
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);

        Assert.Empty(await db.Quotes.ToListAsync());
        Assert.Empty(await db.QuoteLines.ToListAsync());
        Assert.Empty(await db.QuoteExtractionJobs.ToListAsync());
    }

    [Fact]
    public async Task Cannot_write_a_quote_line_claiming_a_different_tenant_than_the_active_scope()
    {
        var activeScope = TenantId.New();
        var claimedOnRow = TenantId.New(); // deliberately different from the active scope.

        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(activeScope);
        await using var db = CreateAppContext(tenantContext);

        db.QuoteLines.Add(new QuoteLine
        {
            TenantId = claimedOnRow,
            QuoteId = EntityId.New(),
            Description = "cross-tenant write attempt",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // ADR-009: the policy's WITH CHECK, not just USING, must reject a write for a tenant other
        // than the one the connection is scoped to.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private async Task SeedQuoteWithLineAsync(TenantId tenantId, string sku)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        var quoteId = EntityId.New();
        var now = DateTimeOffset.UtcNow;

        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            TenantId = tenantId,
            FileName = "quote.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value:D}/documents/{quoteId.Value:D}/v1/quote.pdf",
            Checksum = "deadbeef",
            CreatedAt = now,
        });
        db.QuoteExtractionJobs.Add(new QuoteExtractionJob
        {
            TenantId = tenantId,
            QuoteId = quoteId,
            Status = QuoteExtractionJobStatus.Completed,
            QueuedAt = now,
        });
        db.QuoteLines.Add(new QuoteLine
        {
            TenantId = tenantId,
            QuoteId = quoteId,
            Sku = sku,
            Description = "seeded line",
            CreatedAt = now,
        });

        await db.SaveChangesAsync();
    }
}
