using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// End-to-end proof for task E01/F05/US01/T02 (story us-01-workspace-roles AC-3; produces the
/// `workspace-membership` artifact): <see cref="WorkspaceMembershipService"/> against a real,
/// migrated Postgres instance — the invite/sign-in flow actually persists through EF Core, not
/// just the pure decision logic <c>WorkspaceRoleClaimResolverTests</c>/
/// <c>WorkspaceMembershipFactoryTests</c>/<c>WorkspaceSignInTests</c> already cover in memory.
///
/// Uses the same default (Testcontainers superuser) connection <c>IdentityWorkspaceMigrationTests</c>
/// uses rather than the dedicated unprivileged role
/// <c>WorkspaceRlsCrossTenantIsolationTests</c> stands up: this class proves the invite/sign-in
/// *business logic* (dedup, idempotency, multi-role, linking), not RLS cross-tenant enforcement,
/// which task E01/F05/US01/T01's own tests already cover exhaustively.
/// </summary>
public sealed class WorkspaceMembershipServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly TenantContext _tenantContext = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private IdentityWorkspaceDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString(), _tenantContext);
        return new IdentityWorkspaceDbContext(optionsBuilder.Options);
    }

    private async Task<TenantId> SeedWorkspaceAsync()
    {
        var (workspace, roles) = WorkspaceFactory.CreateWorkspaceWithDefaultRoles(
            "Acme Procurement", FixedClock.Instance);

        await using var db = CreateContext();
        using var _ = _tenantContext.BeginScope(workspace.TenantId);
        db.Workspaces.Add(workspace);
        db.WorkspaceRoles.AddRange(roles);
        await db.SaveChangesAsync();

        return workspace.TenantId;
    }

    [Fact]
    public async Task Invites_a_brand_new_email_by_resolved_oidc_role_claim()
    {
        var tenantId = await SeedWorkspaceAsync();
        var service = new WorkspaceMembershipService(CreateContext(), _tenantContext, FixedClock.Instance);

        var result = await service.InviteFromOidcClaimsAsync(
            tenantId, "new.hire@acme.example", roleClaimValues: ["Raffa.Procurement"]);

        Assert.True(result.IsSuccess);

        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        var user = await readDb.WorkspaceUsers.SingleAsync(u => u.Email == "new.hire@acme.example");
        Assert.Null(user.ExternalSubjectId);

        var membership = await readDb.WorkspaceMemberships.SingleAsync(m => m.WorkspaceUserId == user.Id);
        var role = await readDb.WorkspaceRoles.SingleAsync(r => r.Id == membership.WorkspaceRoleId);
        Assert.Equal(WorkspaceRoleName.Procurement, role.Name);
    }

    [Fact]
    public async Task Re_inviting_the_same_email_and_role_fails_instead_of_duplicating()
    {
        var tenantId = await SeedWorkspaceAsync();
        var service = new WorkspaceMembershipService(CreateContext(), _tenantContext, FixedClock.Instance);

        var first = await service.InviteAsync(tenantId, "dup@acme.example", WorkspaceRoleName.Finance);
        Assert.True(first.IsSuccess);

        var second = await service.InviteAsync(tenantId, "dup@acme.example", WorkspaceRoleName.Finance);
        Assert.True(second.IsFailure);

        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        var memberships = await readDb.WorkspaceMemberships
            .Where(m => m.WorkspaceUserId == first.Value.WorkspaceUserId)
            .ToListAsync();
        Assert.Single(memberships);
    }

    [Fact]
    public async Task Inviting_the_same_email_with_a_second_role_adds_a_second_membership()
    {
        var tenantId = await SeedWorkspaceAsync();
        var service = new WorkspaceMembershipService(CreateContext(), _tenantContext, FixedClock.Instance);

        var first = await service.InviteAsync(tenantId, "multi@acme.example", WorkspaceRoleName.Legal);
        var second = await service.InviteAsync(tenantId, "multi@acme.example", WorkspaceRoleName.Finance);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.WorkspaceUserId, second.Value.WorkspaceUserId);

        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        var memberships = await readDb.WorkspaceMemberships
            .Where(m => m.WorkspaceUserId == first.Value.WorkspaceUserId)
            .ToListAsync();
        Assert.Equal(2, memberships.Count);
    }

    [Fact]
    public async Task Invite_fails_when_no_recognized_role_claim_is_present()
    {
        var tenantId = await SeedWorkspaceAsync();
        var service = new WorkspaceMembershipService(CreateContext(), _tenantContext, FixedClock.Instance);

        var result = await service.InviteFromOidcClaimsAsync(tenantId, "nope@acme.example", roleClaimValues: ["Owner"]);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Sign_in_links_an_invited_users_external_subject_on_first_login()
    {
        var tenantId = await SeedWorkspaceAsync();
        var service = new WorkspaceMembershipService(CreateContext(), _tenantContext, FixedClock.Instance);
        var invite = await service.InviteAsync(tenantId, "signin@acme.example", WorkspaceRoleName.ReadOnly);
        Assert.True(invite.IsSuccess);

        var firstSignIn = await service.LinkSignInAsync(tenantId, "entra-sub-123", "signin@acme.example");
        Assert.True(firstSignIn.IsSuccess);
        Assert.Equal("entra-sub-123", firstSignIn.Value.ExternalSubjectId);

        var secondSignIn = await service.LinkSignInAsync(tenantId, "entra-sub-123", "signin@acme.example");
        Assert.True(secondSignIn.IsSuccess);
        Assert.Equal(firstSignIn.Value.Id, secondSignIn.Value.Id);
    }

    [Fact]
    public async Task Sign_in_fails_for_an_email_that_was_never_invited()
    {
        var tenantId = await SeedWorkspaceAsync();
        var service = new WorkspaceMembershipService(CreateContext(), _tenantContext, FixedClock.Instance);

        var result = await service.LinkSignInAsync(tenantId, "entra-sub-999", "stranger@acme.example");

        Assert.True(result.IsFailure);
    }

    private sealed class FixedClock : IClock
    {
        public static readonly FixedClock Instance = new();

        public DateTimeOffset UtcNow => new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    }
}
