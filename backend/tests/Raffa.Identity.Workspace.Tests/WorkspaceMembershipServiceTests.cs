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
///
/// <para>
/// Task E15/F01/US01/T01 (wave w14, ADR-025 §D.1d) redesigned <see cref="WorkspaceMembershipService.InviteAsync"/>
/// to write a <see cref="WorkspaceInvitation"/> row (never a membership) and to take the
/// already-minted token hash/expiry/inviter this service no longer computes itself — the tests
/// below are updated in place (not deleted) to prove the new shape: no membership exists until
/// <see cref="WorkspaceMembershipService.AcceptInvitationAsync"/> runs, and at most one *live*
/// invitation may exist per email regardless of role (ADR-026 §D4's partial unique index). A
/// fixed, arbitrary token hash/expiry stands in for <see cref="WorkspaceInvitationService"/>'s own
/// minting in every test here — proving that logic is <c>WorkspaceInvitationServiceTests</c>' own
/// job.
/// </para>
/// </summary>
public sealed class WorkspaceMembershipServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset ExpiresAt = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly TenantContext _tenantContext = new();
    private int _tokenHashCounter;

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

    private static WorkspaceMembershipService CreateService(IdentityWorkspaceDbContext db, TenantContext tenantContext) =>
        new(db, tenantContext, FixedClock.Instance, new NoOpAuditWriter());

    /// <summary>A fresh, arbitrary hash per call -- these tests never accept a token, so its exact
    /// value is irrelevant, only that two different invitations never collide on it (the unique
    /// index is keyed on (tenant, hash) too).</summary>
    private string NextTokenHash() => $"test-hash-{++_tokenHashCounter}";

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
    public async Task Invites_a_brand_new_email_by_resolved_oidc_role_claim_and_writes_no_membership()
    {
        var tenantId = await SeedWorkspaceAsync();
        var service = CreateService(CreateContext(), _tenantContext);

        var result = await service.InviteFromOidcClaimsAsync(
            tenantId, "new.hire@acme.example", roleClaimValues: ["Raffa.Procurement"],
            "admin@acme.example", NextTokenHash(), ExpiresAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkspaceRoleName.Procurement, (await ReadRoleAsync(tenantId, result.Invitation!)).Name);

        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        var user = await readDb.WorkspaceUsers.SingleAsync(u => u.Email == "new.hire@acme.example");
        Assert.Null(user.ExternalSubjectId);

        // ADR-025 Rule D.1d: invite writes the user + the invitation, never a membership -- the
        // grant only happens at AcceptInvitationAsync.
        Assert.False(await readDb.WorkspaceMemberships.AnyAsync(m => m.WorkspaceUserId == user.Id));
        Assert.True(await readDb.WorkspaceInvitations.AnyAsync(
            i => i.Email == "new.hire@acme.example" && i.AcceptedAt == null && i.RevokedAt == null));
    }

    [Fact]
    public async Task Re_inviting_the_same_email_and_role_conflicts_instead_of_duplicating()
    {
        var tenantId = await SeedWorkspaceAsync();
        var service = CreateService(CreateContext(), _tenantContext);

        var first = await service.InviteAsync(
            tenantId, "dup@acme.example", WorkspaceRoleName.Finance, "admin@acme.example", NextTokenHash(), ExpiresAt);
        Assert.True(first.IsSuccess);

        var second = await service.InviteAsync(
            tenantId, "dup@acme.example", WorkspaceRoleName.Finance, "admin@acme.example", NextTokenHash(), ExpiresAt);
        Assert.True(second.IsFailure);
        Assert.Equal(MembershipOperationStatus.Conflict, second.Status);

        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        var invitations = await readDb.WorkspaceInvitations.Where(i => i.Email == "dup@acme.example").ToListAsync();
        Assert.Single(invitations);
    }

    /// <summary>
    /// ADR-026 §D4's partial unique index is keyed on <c>(tenant_id, lower(email))</c> alone, not
    /// role -- at most one *live* invitation may exist per email, regardless of which role it
    /// offers. This replaces the pre-w14 premise ("a second invite at a different role adds a
    /// second membership"), which no longer holds now that invite never writes a membership at
    /// all: two still-pending invitations for one email would violate that index the moment the
    /// second one is accepted, so the conflict is correctly surfaced at issue time instead
    /// (see <see cref="Multi_role_membership_is_still_possible_once_the_first_invitation_is_accepted"/>
    /// for the shape that *does* still work).
    /// </summary>
    [Fact]
    public async Task Inviting_the_same_email_at_a_different_role_conflicts_while_the_first_invitation_is_still_live()
    {
        var tenantId = await SeedWorkspaceAsync();
        var service = CreateService(CreateContext(), _tenantContext);

        var first = await service.InviteAsync(
            tenantId, "multi@acme.example", WorkspaceRoleName.Legal, "admin@acme.example", NextTokenHash(), ExpiresAt);
        Assert.True(first.IsSuccess);

        var second = await service.InviteAsync(
            tenantId, "multi@acme.example", WorkspaceRoleName.Finance, "admin@acme.example", NextTokenHash(), ExpiresAt);

        Assert.True(second.IsFailure);
        Assert.Equal(MembershipOperationStatus.Conflict, second.Status);
    }

    /// <summary>
    /// The multi-role shape the pre-w14 test above used to prove, re-proven through the new
    /// accept-time grant: once the first invitation is accepted (a membership now exists at Legal,
    /// and its invitation row is no longer "live"), a second invite at a different role succeeds,
    /// and accepting that one too leaves the same person holding two memberships.
    /// </summary>
    [Fact]
    public async Task Multi_role_membership_is_still_possible_once_the_first_invitation_is_accepted()
    {
        var tenantId = await SeedWorkspaceAsync();
        var service = CreateService(CreateContext(), _tenantContext);

        var first = await service.InviteAsync(
            tenantId, "multi2@acme.example", WorkspaceRoleName.Legal, "admin@acme.example", NextTokenHash(), ExpiresAt);
        Assert.True(first.IsSuccess);

        var firstAccept = await service.AcceptInvitationAsync(tenantId, first.Invitation!, "multi2@acme.example");
        Assert.True(firstAccept.IsSuccess);

        var second = await service.InviteAsync(
            tenantId, "multi2@acme.example", WorkspaceRoleName.Finance, "admin@acme.example", NextTokenHash(), ExpiresAt);
        Assert.True(second.IsSuccess);

        var secondAccept = await service.AcceptInvitationAsync(tenantId, second.Invitation!, "multi2@acme.example");
        Assert.True(secondAccept.IsSuccess);
        Assert.Equal(firstAccept.User!.Id, secondAccept.User!.Id);

        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        var memberships = await readDb.WorkspaceMemberships
            .Where(m => m.WorkspaceUserId == firstAccept.User!.Id)
            .ToListAsync();
        Assert.Equal(2, memberships.Count);
    }

    [Fact]
    public async Task Invite_fails_when_no_recognized_role_claim_is_present()
    {
        var tenantId = await SeedWorkspaceAsync();
        var service = CreateService(CreateContext(), _tenantContext);

        var result = await service.InviteFromOidcClaimsAsync(
            tenantId, "nope@acme.example", roleClaimValues: ["Owner"], "admin@acme.example", NextTokenHash(), ExpiresAt);

        Assert.True(result.IsFailure);
        Assert.Equal(MembershipOperationStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task Sign_in_links_an_invited_users_external_subject_on_first_login()
    {
        var tenantId = await SeedWorkspaceAsync();
        var service = CreateService(CreateContext(), _tenantContext);
        var invite = await service.InviteAsync(
            tenantId, "signin@acme.example", WorkspaceRoleName.ReadOnly, "admin@acme.example", NextTokenHash(), ExpiresAt);
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
        var service = CreateService(CreateContext(), _tenantContext);

        var result = await service.LinkSignInAsync(tenantId, "entra-sub-999", "stranger@acme.example");

        Assert.True(result.IsFailure);
    }

    private async Task<WorkspaceRole> ReadRoleAsync(TenantId tenantId, WorkspaceInvitation invitation)
    {
        using var _ = _tenantContext.BeginScope(tenantId);
        await using var db = CreateContext();
        return await db.WorkspaceRoles.SingleAsync(r => r.Id == invitation.WorkspaceRoleId);
    }

    private sealed class FixedClock : IClock
    {
        public static readonly FixedClock Instance = new();

        public DateTimeOffset UtcNow => new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
