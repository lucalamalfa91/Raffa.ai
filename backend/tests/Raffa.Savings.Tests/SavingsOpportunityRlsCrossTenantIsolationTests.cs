using Raffa.Savings.Domain;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Savings.Tests;

/// <summary>
/// Proves the tenant-scoping half of task E04/F02/US02/T01 (savings-opportunity; parent story
/// us-02-savings-opportunity): with the RLS policy from this module's own
/// `AddTenantRowLevelSecurity` migration applied and <see cref="TenantRlsConnectionInterceptor"/>
/// setting the per-connection `app.tenant_id` claim, tenant A's connection genuinely cannot read
/// (or write) tenant B's <see cref="SavingsOpportunity"/> row — the isolation is enforced by
/// Postgres itself, not by <see cref="Application.SavingsOpportunityService"/>'s own
/// application-level `WHERE tenant_id = ...` filter alone (ADR-009's "belt-and-suspenders"). Mirrors
/// <c>Raffa.Renewals.Tests.RenewalActionRlsCrossTenantIsolationTests</c> exactly, scoped to this
/// module's own <see cref="SavingsDbContext"/>.
///
/// Runs all assertions through a dedicated, deliberately unprivileged Postgres role
/// (<see cref="AppRoleName"/>: `NOSUPERUSER NOBYPASSRLS`, not the table owner). The Testcontainers
/// bootstrap role is always a Postgres superuser, and superusers unconditionally bypass row
/// security regardless of policy or `FORCE` — asserting isolation over that connection would pass
/// vacuously. This role stands in for "the application's own database role" (no `BYPASSRLS` in the
/// app path — ADR-009), so a passing test here is a real proof, not a tautology.
/// </summary>
public sealed class SavingsOpportunityRlsCrossTenantIsolationTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_savings_app";
    private const string AppRolePassword = "raffa_savings_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<SavingsDbContext>();
        SavingsDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new SavingsDbContext(adminOptions.Options))
        {
            // Applies Initial + this task's AddTenantRowLevelSecurity migration.
            await adminDb.Database.MigrateAsync();

            // A non-owner, non-superuser, NOBYPASSRLS role: see the type doc comment for why this
            // is required for the test to mean anything.
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

    private SavingsDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SavingsDbContext>();
        SavingsDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new SavingsDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Tenant_cannot_read_another_tenants_savings_opportunity_row()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        await SeedOpportunityAsync(tenantA, type: "owned-by-tenant-a");
        await SeedOpportunityAsync(tenantB, type: "owned-by-tenant-b");

        var tenantContext = new TenantContext();
        using (tenantContext.BeginScope(tenantA))
        {
            await using var db = CreateAppContext(tenantContext);

            // AC-1/ADR-009: tenant B's row exists (seeded above, over the same table) but RLS
            // makes it invisible on a connection scoped to tenant A.
            var visible = await db.SavingsOpportunities.ToListAsync();

            var visibleRow = Assert.Single(visible);
            Assert.Equal("owned-by-tenant-a", visibleRow.Type);
            Assert.Equal(tenantA, visibleRow.TenantId);
        }
    }

    [Fact]
    public async Task No_active_tenant_scope_sees_zero_rows()
    {
        await SeedOpportunityAsync(TenantId.New(), type: "belongs-to-someone-else");

        // No BeginScope entered: ITenantContext.Current is null, so the interceptor leaves
        // app.tenant_id unset. current_setting(..., true) then returns NULL, and
        // `tenant_id = NULL` is never true -- fail closed, zero rows visible to anyone.
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);

        var visible = await db.SavingsOpportunities.ToListAsync();

        Assert.Empty(visible);
    }

    [Fact]
    public async Task Cannot_write_a_row_claiming_a_different_tenant_than_the_active_scope()
    {
        var activeScope = TenantId.New();
        var claimedOnRow = TenantId.New(); // deliberately different from the active scope.

        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(activeScope);
        await using var db = CreateAppContext(tenantContext);

        db.SavingsOpportunities.Add(NewOpportunity(claimedOnRow, "test"));

        // ADR-009: the policy's WITH CHECK, not just USING, must reject a write for a tenant other
        // than the one the connection is scoped to -- the backstop covers writes, not only reads.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private async Task SeedOpportunityAsync(TenantId tenantId, string type)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        db.SavingsOpportunities.Add(NewOpportunity(tenantId, type));
        await db.SaveChangesAsync();
    }

    private static SavingsOpportunity NewOpportunity(TenantId tenantId, string type) => new()
    {
        TenantId = tenantId,
        Type = type,
        CurrentSpend = 1_000m,
        Currency = "USD",
        EstimatedSavingsLow = 100m,
        EstimatedSavingsHigh = 200m,
        Confidence = 0.5,
        Status = SavingsOpportunityStatus.Identified,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
