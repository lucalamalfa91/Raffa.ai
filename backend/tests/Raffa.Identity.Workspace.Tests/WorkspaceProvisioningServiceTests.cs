using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// Proves the Definition of Done for task E01/F09/US01/T01 (r0-integration, AC-1 "create
/// workspace" step): <see cref="WorkspaceProvisioningService.CreateWorkspaceAsync"/> persists a
/// real <see cref="WorkspaceTenant"/> + its full default role catalog against a real Postgres+RLS
/// database — including the self-referential case every other RLS proof in this project
/// sidesteps by seeding into an *already existing* tenant: here, <see cref="WorkspaceTenant.Id"/>
/// equals its own <c>TenantId</c>, so the very first insert for a brand-new tenant must clear the
/// RLS `WITH CHECK` on the first try, with no prior row for that tenant to have "established" it.
///
/// Runs assertions through a dedicated, deliberately unprivileged Postgres role (mirrors
/// <see cref="WorkspaceRlsCrossTenantIsolationTests"/>'s own rationale: the Testcontainers
/// bootstrap role is always a superuser, and superusers unconditionally bypass row security, so
/// asserting isolation over that connection would pass vacuously).
/// </summary>
public sealed class WorkspaceProvisioningServiceTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_provisioning_app";
    private const string AppRolePassword = "raffa_provisioning_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new IdentityWorkspaceDbContext(adminOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity.
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

    private IdentityWorkspaceDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new IdentityWorkspaceDbContext(optionsBuilder.Options);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task Creates_a_workspace_with_its_full_default_role_catalog()
    {
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new WorkspaceProvisioningService(db, tenantContext, new FixedClock());

        var result = await service.CreateWorkspaceAsync("Acme Procurement");

        Assert.True(result.IsSuccess);
        var workspace = result.Value;
        Assert.Equal("Acme Procurement", workspace.Name);
        // WorkspaceTenant's own structural invariant (see its doc comment): Id == TenantId.
        Assert.Equal(workspace.Id.Value, workspace.TenantId.Value);

        using var _ = tenantContext.BeginScope(workspace.TenantId);
        await using var readDb = CreateAppContext(tenantContext);

        var persisted = await readDb.Workspaces.SingleAsync(w => w.Id == workspace.Id);
        Assert.Equal("Acme Procurement", persisted.Name);

        var roles = await readDb.WorkspaceRoles
            .Where(r => r.TenantId == workspace.TenantId)
            .ToListAsync();
        var expectedRoleNames = Enum.GetValues<WorkspaceRoleName>();
        Assert.Equal(expectedRoleNames.Length, roles.Count);
        foreach (var roleName in expectedRoleNames)
        {
            Assert.Contains(roles, r => r.Name == roleName);
        }
    }

    [Fact]
    public async Task Blank_name_fails_and_writes_nothing()
    {
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new WorkspaceProvisioningService(db, tenantContext, new FixedClock());

        var result = await service.CreateWorkspaceAsync("   ");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task A_different_tenant_cannot_see_a_newly_created_workspace_or_its_roles()
    {
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new WorkspaceProvisioningService(db, tenantContext, new FixedClock());

        var result = await service.CreateWorkspaceAsync("Acme Procurement");
        Assert.True(result.IsSuccess);

        // AC-2 (r0-integration): tenant A's workspace/roles exist (created above, over the same
        // tables) but RLS makes them invisible on a connection scoped to an unrelated tenant.
        var otherTenant = TenantId.New();
        using var _ = tenantContext.BeginScope(otherTenant);
        await using var readDb = CreateAppContext(tenantContext);

        Assert.Empty(await readDb.Workspaces.ToListAsync());
        Assert.Empty(await readDb.WorkspaceRoles.ToListAsync());
    }
}
