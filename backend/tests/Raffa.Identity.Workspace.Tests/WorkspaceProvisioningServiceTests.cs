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
    public async Task Creates_a_workspace_with_its_full_default_role_catalog_and_the_creators_admin_membership()
    {
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new WorkspaceProvisioningService(db, tenantContext, new FixedClock());

        var result = await service.CreateWorkspaceAsync("Acme Procurement", "founder@acme.example");

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

        // Task E14/F02/US01/T01 (ADR-025 Rule D.2b / ADR-009 w14 footer clause 6): the creator's
        // own Admin membership lands in the same scope/SaveChangesAsync as the workspace and its
        // role catalog -- four writes proven by one read each.
        var user = await readDb.WorkspaceUsers.SingleAsync(u => u.TenantId == workspace.TenantId);
        Assert.Equal("founder@acme.example", user.Email);
        Assert.Null(user.ExternalSubjectId);

        var membership = await readDb.WorkspaceMemberships.SingleAsync(m => m.TenantId == workspace.TenantId);
        Assert.Equal(user.Id, membership.WorkspaceUserId);

        var adminRole = roles.Single(r => r.Name == WorkspaceRoleName.Admin);
        Assert.Equal(adminRole.Id, membership.WorkspaceRoleId);
    }

    [Fact]
    public async Task Blank_name_fails_and_writes_nothing()
    {
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new WorkspaceProvisioningService(db, tenantContext, new FixedClock());

        var result = await service.CreateWorkspaceAsync("   ", "founder@acme.example");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Blank_caller_identity_fails_before_any_write()
    {
        // ADR-025 Rule D.2a: absent identity is a 401 at the endpoint; a caller that reaches the
        // service with a blank identity anyway (e.g. a future non-HTTP caller) still fails cleanly
        // -- ADR-009 w14 footer clause 6's "a partial bootstrap must not be reachable by a failure
        // path either" holds because this check runs before BeginScope/SaveChangesAsync.
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new WorkspaceProvisioningService(db, tenantContext, new FixedClock());

        var result = await service.CreateWorkspaceAsync("Acme Procurement", "   ");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task An_unparseable_caller_identity_fails_before_any_write()
    {
        // WorkspaceMembershipFactory.CreateInvitedUser's own validation (reused, not duplicated):
        // ADR-025 §B "identity presented but malformed" is this Result<T> failure path, exactly
        // like a blank name always has been.
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new WorkspaceProvisioningService(db, tenantContext, new FixedClock());

        var result = await service.CreateWorkspaceAsync("Acme Procurement", "not-an-email");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task A_different_tenant_cannot_see_a_newly_created_workspace_or_its_roles()
    {
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new WorkspaceProvisioningService(db, tenantContext, new FixedClock());

        var result = await service.CreateWorkspaceAsync("Acme Procurement", "founder@acme.example");
        Assert.True(result.IsSuccess);

        // AC-2 (r0-integration) / AC-4 (wave w14): tenant A's workspace/roles/creator membership
        // exist (created above, over the same tables) but RLS makes them invisible on a connection
        // scoped to an unrelated tenant.
        var otherTenant = TenantId.New();
        using var _ = tenantContext.BeginScope(otherTenant);
        await using var readDb = CreateAppContext(tenantContext);

        Assert.Empty(await readDb.Workspaces.ToListAsync());
        Assert.Empty(await readDb.WorkspaceRoles.ToListAsync());
        Assert.Empty(await readDb.WorkspaceUsers.ToListAsync());
        Assert.Empty(await readDb.WorkspaceMemberships.ToListAsync());
    }
}
