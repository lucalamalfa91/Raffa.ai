using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// Proves the Definition of Done for task E01/F05/US01/T01 (story us-01-workspace-roles, AC-1):
/// with the RLS policies from migration `AddTenantRowLevelSecurity` applied and
/// <see cref="TenantRlsConnectionInterceptor"/> setting the per-connection `app.tenant_id` claim,
/// one workspace's connection genuinely cannot read (or write) another workspace's rows -- the
/// isolation is enforced by Postgres itself, not by an application-level `WHERE` clause. Mirrors
/// task E01/F04/US03/T01's own proof for Documents/Contracts
/// (`Raffa.Tenancy.Tests.TenantRlsCrossTenantIsolationTests`), applied here to
/// <see cref="WorkspaceUser"/>.
///
/// Runs all assertions through a dedicated, deliberately unprivileged Postgres role
/// (<see cref="AppRoleName"/>: `NOSUPERUSER NOBYPASSRLS`, not the table owner). The Testcontainers
/// bootstrap role is always a Postgres superuser, and superusers unconditionally bypass row
/// security regardless of policy or `FORCE` -- asserting isolation over that connection would pass
/// vacuously. This role stands in for "the application's own database role", so a passing test
/// here is a real proof, not a tautology.
/// </summary>
public sealed class WorkspaceRlsCrossTenantIsolationTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_identity_app";
    private const string AppRolePassword = "raffa_identity_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new IdentityWorkspaceDbContext(adminOptions.Options))
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

    private IdentityWorkspaceDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new IdentityWorkspaceDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Tenant_cannot_read_another_tenants_rows()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        await SeedUserAsync(tenantA, email: "owned-by-tenant-a@example.com");
        await SeedUserAsync(tenantB, email: "owned-by-tenant-b@example.com");

        var tenantContext = new TenantContext();
        using (tenantContext.BeginScope(tenantA))
        {
            await using var db = CreateAppContext(tenantContext);

            // AC-1: tenant B's row exists (seeded above, over the same table) but RLS makes it
            // invisible on a connection scoped to tenant A.
            var visible = await db.WorkspaceUsers.ToListAsync();

            var visibleRow = Assert.Single(visible);
            Assert.Equal("owned-by-tenant-a@example.com", visibleRow.Email);
            Assert.Equal(tenantA, visibleRow.TenantId);
        }
    }

    [Fact]
    public async Task No_active_tenant_scope_sees_zero_rows()
    {
        await SeedUserAsync(TenantId.New(), email: "belongs-to-someone-else@example.com");

        // No BeginScope entered: ITenantContext.Current is null, so the interceptor leaves
        // app.tenant_id unset. current_setting(..., true) then returns NULL, and
        // `tenant_id = NULL` is never true -- fail closed, zero rows visible to anyone.
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);

        var visible = await db.WorkspaceUsers.ToListAsync();

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

        db.WorkspaceUsers.Add(new WorkspaceUser
        {
            TenantId = claimedOnRow,
            Email = "cross-tenant-write@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // AC-1/ADR-009: the policy's WITH CHECK, not just USING, must reject a write for a tenant
        // other than the one the connection is scoped to -- the backstop covers writes, not only
        // reads.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private async Task SeedUserAsync(TenantId tenantId, string email)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        db.WorkspaceUsers.Add(new WorkspaceUser
        {
            TenantId = tenantId,
            Email = email,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
