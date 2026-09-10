using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// Proves the Definition of Done for task E01/F05/US01/T01 (story us-01-workspace-roles, AC-1):
/// `dotnet ef migrations add` + `database update` succeed against a real Postgres instance, and
/// the full Workspace/User/Role/Membership graph this task adds is genuinely persistable and
/// readable through EF Core -- not just declared in the model.
///
/// Spins up its own disposable Postgres container per test run (Testcontainers), so this test
/// needs nothing but a running Docker daemon; no shared/external database to stand up by hand.
/// </summary>
public sealed class IdentityWorkspaceMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private IdentityWorkspaceDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString());
        return new IdentityWorkspaceDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Migrate_applies_the_code_first_schema_against_a_real_postgres()
    {
        await using var db = CreateContext();

        await db.Database.MigrateAsync();

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains(appliedMigrations, id => id.EndsWith("_Initial", StringComparison.Ordinal));
        Assert.Contains(
            appliedMigrations, id => id.EndsWith("_AddTenantRowLevelSecurity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Workspace_user_role_and_membership_round_trip_through_ef_core()
    {
        await using (var migrateDb = CreateContext())
        {
            await migrateDb.Database.MigrateAsync();
        }

        var (workspace, roles) = WorkspaceFactory.CreateWorkspaceWithDefaultRoles(
            "Acme Procurement", SystemClock.Instance);
        var adminRole = roles.Single(r => r.Name == WorkspaceRoleName.Admin);

        var user = new WorkspaceUser
        {
            TenantId = workspace.TenantId,
            Email = "admin@acme.example",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var membership = new WorkspaceMembership
        {
            TenantId = workspace.TenantId,
            WorkspaceUserId = user.Id,
            WorkspaceRoleId = adminRole.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using (var writeDb = CreateContext())
        {
            writeDb.Workspaces.Add(workspace);
            writeDb.WorkspaceRoles.AddRange(roles);
            writeDb.WorkspaceUsers.Add(user);
            writeDb.WorkspaceMemberships.Add(membership);
            await writeDb.SaveChangesAsync();
        }

        // Fresh context/connection: this reads back from Postgres, not the change tracker.
        await using var readDb = CreateContext();

        var storedWorkspace = await readDb.Workspaces.SingleAsync(w => w.Id == workspace.Id);
        Assert.Equal(storedWorkspace.Id.Value, storedWorkspace.TenantId.Value);

        var storedRoles = await readDb.WorkspaceRoles
            .Where(r => r.TenantId == workspace.TenantId)
            .ToListAsync();
        Assert.Equal(Enum.GetValues<WorkspaceRoleName>().Length, storedRoles.Count);

        var storedMembership = await readDb.WorkspaceMemberships.SingleAsync(m => m.Id == membership.Id);
        Assert.Equal(user.Id, storedMembership.WorkspaceUserId);
        Assert.Equal(adminRole.Id, storedMembership.WorkspaceRoleId);
        Assert.Equal(workspace.TenantId, storedMembership.TenantId);
    }

    private sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new();

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
