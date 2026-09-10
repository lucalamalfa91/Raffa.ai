using Raffa.Renewals.Domain;
using Raffa.Renewals.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Renewals.Tests;

/// <summary>
/// Proves the "+ tenant scoping" half of task E03/F03/US01/T02's own title (us-01-renewal-dashboard-api
/// AC-3): with the RLS policy from this module's own `AddTenantRowLevelSecurity` migration applied
/// and <see cref="TenantRlsConnectionInterceptor"/> setting the per-connection `app.tenant_id`
/// claim, tenant A's connection genuinely cannot read (or write) tenant B's <see cref="RenewalAction"/>
/// row — the isolation is enforced by Postgres itself, not by <see cref="RenewalActionService"/>'s
/// own application-level `WHERE tenant_id = ...` filter alone (ADR-009's "belt-and-suspenders").
/// Mirrors <c>Raffa.Tenancy.Tests.TenantRlsCrossTenantIsolationTests</c> /
/// <c>Raffa.Identity.Workspace.Tests.WorkspaceRlsCrossTenantIsolationTests</c> exactly, scoped to
/// this module's own <see cref="RenewalsDbContext"/>.
///
/// Runs all assertions through a dedicated, deliberately unprivileged Postgres role
/// (<see cref="AppRoleName"/>: `NOSUPERUSER NOBYPASSRLS`, not the table owner). The Testcontainers
/// bootstrap role is always a Postgres superuser, and superusers unconditionally bypass row
/// security regardless of policy or `FORCE` — asserting isolation over that connection would pass
/// vacuously. This role stands in for "the application's own database role" (no `BYPASSRLS` in the
/// app path — ADR-009), so a passing test here is a real proof, not a tautology.
/// </summary>
public sealed class RenewalActionRlsCrossTenantIsolationTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_app";
    private const string AppRolePassword = "raffa_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<RenewalsDbContext>();
        RenewalsDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new RenewalsDbContext(adminOptions.Options))
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

    private RenewalsDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RenewalsDbContext>();
        RenewalsDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new RenewalsDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Tenant_cannot_read_another_tenants_renewal_action_row()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        await SeedActionAsync(tenantA, owner: "owned-by-tenant-a");
        await SeedActionAsync(tenantB, owner: "owned-by-tenant-b");

        var tenantContext = new TenantContext();
        using (tenantContext.BeginScope(tenantA))
        {
            await using var db = CreateAppContext(tenantContext);

            // AC-3/ADR-009: tenant B's row exists (seeded above, over the same table) but RLS
            // makes it invisible on a connection scoped to tenant A.
            var visible = await db.RenewalActions.ToListAsync();

            var visibleRow = Assert.Single(visible);
            Assert.Equal("owned-by-tenant-a", visibleRow.Owner);
            Assert.Equal(tenantA, visibleRow.TenantId);
        }
    }

    [Fact]
    public async Task No_active_tenant_scope_sees_zero_rows()
    {
        await SeedActionAsync(TenantId.New(), owner: "belongs-to-someone-else");

        // No BeginScope entered: ITenantContext.Current is null, so the interceptor leaves
        // app.tenant_id unset. current_setting(..., true) then returns NULL, and
        // `tenant_id = NULL` is never true -- fail closed, zero rows visible to anyone.
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);

        var visible = await db.RenewalActions.ToListAsync();

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

        db.RenewalActions.Add(new RenewalAction
        {
            TenantId = claimedOnRow,
            ContractId = EntityId.New(),
            Owner = "test",
            Status = RenewalActionStatus.NotStarted,
            Action = "test",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        // AC-3/ADR-009: the policy's WITH CHECK, not just USING, must reject a write for a tenant
        // other than the one the connection is scoped to -- the backstop covers writes, not only
        // reads.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private async Task SeedActionAsync(TenantId tenantId, string owner)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        db.RenewalActions.Add(new RenewalAction
        {
            TenantId = tenantId,
            ContractId = EntityId.New(),
            Owner = owner,
            Status = RenewalActionStatus.NotStarted,
            Action = "seed",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
