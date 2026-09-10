using Raffa.Savings.Domain;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Savings.Tests;

/// <summary>
/// Proves the tenant-scoping half of task E04/F02/US02/T02 (realized-savings; parent story
/// us-02-savings-opportunity AC-3, "honours ... ADR-009") for this module's second tenant-scoped
/// table: with the RLS policy from this task's own `AddRealizedSavingsRowLevelSecurity` migration
/// applied and <see cref="TenantRlsConnectionInterceptor"/> setting the per-connection
/// `app.tenant_id` claim, tenant A's connection genuinely cannot read (or write) tenant B's
/// <see cref="RealizedSavings"/> row — the isolation is enforced by Postgres itself, not by
/// <see cref="Application.SavingsOpportunityService"/>'s own application-level
/// `WHERE tenant_id = ...` filter alone (ADR-009's "belt-and-suspenders"). Mirrors
/// <see cref="SavingsOpportunityRlsCrossTenantIsolationTests"/> exactly, scoped to this module's
/// own <see cref="SavingsDbContext.RealizedSavingsRecords"/>; see that class's own doc comment for
/// why every assertion below runs through a dedicated, deliberately unprivileged Postgres role
/// rather than the Testcontainers bootstrap superuser.
/// </summary>
public sealed class RealizedSavingsRlsCrossTenantIsolationTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_realized_savings_app";
    private const string AppRolePassword = "raffa_realized_savings_app_test_password";

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
            // Applies Initial + AddTenantRowLevelSecurity + AddRealizedSavings +
            // AddRealizedSavingsRowLevelSecurity.
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

    private SavingsDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SavingsDbContext>();
        SavingsDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new SavingsDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Tenant_cannot_read_another_tenants_realized_savings_row()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        var opportunityA = await SeedRealizedSavingsAsync(tenantA, amount: 100m);
        await SeedRealizedSavingsAsync(tenantB, amount: 200m);

        var tenantContext = new TenantContext();
        using (tenantContext.BeginScope(tenantA))
        {
            await using var db = CreateAppContext(tenantContext);

            // Tenant B's row exists (seeded above, over the same table) but RLS makes it invisible
            // on a connection scoped to tenant A.
            var visible = await db.RealizedSavingsRecords.ToListAsync();

            var visibleRow = Assert.Single(visible);
            Assert.Equal(opportunityA, visibleRow.SavingsOpportunityId);
            Assert.Equal(100m, visibleRow.Amount);
            Assert.Equal(tenantA, visibleRow.TenantId);
        }
    }

    [Fact]
    public async Task No_active_tenant_scope_sees_zero_realized_savings_rows()
    {
        await SeedRealizedSavingsAsync(TenantId.New(), amount: 50m);

        // No BeginScope entered: ITenantContext.Current is null, so the interceptor leaves
        // app.tenant_id unset -- fail closed, zero rows visible to anyone (same as
        // SavingsOpportunityRlsCrossTenantIsolationTests's own equivalent test).
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);

        var visible = await db.RealizedSavingsRecords.ToListAsync();

        Assert.Empty(visible);
    }

    [Fact]
    public async Task Cannot_write_a_realized_savings_row_claiming_a_different_tenant_than_the_active_scope()
    {
        var activeScope = TenantId.New();
        var claimedOnRow = TenantId.New(); // deliberately different from the active scope.

        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(activeScope);
        await using var db = CreateAppContext(tenantContext);

        db.RealizedSavingsRecords.Add(new RealizedSavings
        {
            TenantId = claimedOnRow,
            SavingsOpportunityId = EntityId.New(),
            Amount = 10m,
            Currency = "USD",
            RealizedAt = DateTimeOffset.UtcNow,
        });

        // ADR-009: the policy's WITH CHECK, not just USING, must reject a write for a tenant other
        // than the one the connection is scoped to.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>Seeds one <see cref="RealizedSavings"/> row for <paramref name="tenantId"/>,
    /// against an arbitrary <see cref="EntityId"/> (no real <c>SavingsOpportunity</c> row needs to
    /// exist for this table's own RLS policy to apply — the policy is keyed on this row's own
    /// <c>tenant_id</c> column, same as every other tenant-scoped table). Returns the
    /// <c>SavingsOpportunityId</c> used, so a caller can assert on it.</summary>
    private async Task<EntityId> SeedRealizedSavingsAsync(TenantId tenantId, decimal amount)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        var opportunityId = EntityId.New();
        db.RealizedSavingsRecords.Add(new RealizedSavings
        {
            TenantId = tenantId,
            SavingsOpportunityId = opportunityId,
            Amount = amount,
            Currency = "USD",
            RealizedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return opportunityId;
    }
}
