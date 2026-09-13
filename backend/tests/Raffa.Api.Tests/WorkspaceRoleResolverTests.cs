using System.Security.Claims;
using Raffa.Api.Infrastructure;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Api.Tests;

/// <summary>
/// Unit-level proof for task E14/F02/US02/T01 (wave w14, story us-02-admin-role-from-membership;
/// ADR-022 w14 footer clause 1; ADR-025 §E "a client-declared role is never an authorization
/// source"): <see cref="WorkspaceRoleResolver.ResolveAsync"/>'s order is claims then
/// <c>workspace_membership</c>, and nothing else — deleting the header branch means a header can no
/// longer grant a role a real membership row does not hold, and cannot revoke one a real membership
/// row does hold (Rule E2, "membership wins in both directions"). <see cref="DocumentAdminActionsAuthorizationTests"/>
/// carries the same proof over the real HTTP surface (N9); this class exercises the resolver
/// directly.
///
/// <para>
/// Constructs <see cref="WorkspaceRoleResolver"/> itself against an EF Core InMemory
/// <see cref="IdentityWorkspaceDbContext"/> and the real <see cref="HeaderCallerIdentity"/> (wired to
/// the same <see cref="HttpContext"/> under test, exactly like the production DI graph wires it) —
/// no fake stand-in for the identity seam, so the spoofed-header scenarios below exercise the actual
/// header-reading code the SPA's requests go through. This requires
/// <c>Raffa.Api/AssemblyInfo.cs</c>'s <c>InternalsVisibleTo("Raffa.Api.Tests")</c> grant, added by
/// this same task, the same shape <c>Raffa.Worker/AssemblyInfo.cs</c> already uses for
/// <c>Raffa.Worker.Tests</c>.
/// </para>
/// </summary>
public sealed class WorkspaceRoleResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task T2a_a_procurement_member_sending_a_spoofed_admin_header_resolves_to_procurement()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        const string email = "buyer@acme.example";
        using var db = CreateDb();
        await SeedMembershipAsync(db, tenantId, email, WorkspaceRoleName.Procurement);
        var resolver = CreateResolver(db, out var httpContext);
        httpContext.Request.Headers["X-User-Id"] = email;
        httpContext.Request.Headers["X-Role"] = "Admin";
        httpContext.Request.Headers["X-Workspace-Role"] = "Admin";

        var role = await resolver.ResolveAsync(httpContext, tenantId);

        Assert.Equal(WorkspaceRoleName.Procurement, role);
    }

    [Fact]
    public async Task A_header_claiming_procurement_does_not_revoke_a_real_admin()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        const string email = "admin@acme.example";
        using var db = CreateDb();
        await SeedMembershipAsync(db, tenantId, email, WorkspaceRoleName.Admin);
        var resolver = CreateResolver(db, out var httpContext);
        httpContext.Request.Headers["X-User-Id"] = email;
        httpContext.Request.Headers["X-Role"] = "Procurement";
        httpContext.Request.Headers["X-Workspace-Role"] = "Procurement";

        var role = await resolver.ResolveAsync(httpContext, tenantId);

        Assert.Equal(WorkspaceRoleName.Admin, role);
    }

    [Fact]
    public async Task A_spoofed_admin_header_with_no_membership_anywhere_resolves_to_no_role()
    {
        // ADR-025 §E "nothing else": a well-formed identity with no live membership row in this
        // tenant must not have the header manufacture a role for it either.
        var tenantId = new TenantId(Guid.NewGuid());
        using var db = CreateDb();
        var resolver = CreateResolver(db, out var httpContext);
        httpContext.Request.Headers["X-User-Id"] = "stranger@acme.example";
        httpContext.Request.Headers["X-Role"] = "Admin";
        httpContext.Request.Headers["X-Workspace-Role"] = "Admin";

        var role = await resolver.ResolveAsync(httpContext, tenantId);

        Assert.Null(role);
    }

    [Fact]
    public async Task An_authenticated_role_claim_still_resolves_ahead_of_membership()
    {
        // Claims stay the first, ADR-010 end-state source -- untouched by this deletion. No
        // X-User-Id/membership row exists at all here, so this only passes if the claim branch
        // still runs first.
        var tenantId = new TenantId(Guid.NewGuid());
        using var db = CreateDb();
        var resolver = CreateResolver(db, out var httpContext);
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Procurement")], authenticationType: "Test"));

        var role = await resolver.ResolveAsync(httpContext, tenantId);

        Assert.Equal(WorkspaceRoleName.Procurement, role);
    }

    // ----- helpers -----

    private static IdentityWorkspaceDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new IdentityWorkspaceDbContext(options);
    }

    private static WorkspaceRoleResolver CreateResolver(IdentityWorkspaceDbContext db, out HttpContext httpContext)
    {
        httpContext = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new WorkspaceRoleResolver(db, new TenantContext(), new HeaderCallerIdentity(accessor));
    }

    /// <summary>Seeds the membership row directly through the DbContext -- the same "not through
    /// WorkspaceMembershipService.InviteAsync" fixture shape this task's own sibling integration
    /// test uses, since InviteAsync's live-grant-at-invite-time behaviour is phase-3 (E15/F01/US01/T01)
    /// territory, not this resolver's concern.</summary>
    private static async Task SeedMembershipAsync(
        IdentityWorkspaceDbContext db, TenantId tenantId, string email, WorkspaceRoleName roleName)
    {
        var user = new WorkspaceUser { TenantId = tenantId, Email = email, CreatedAt = Now };
        var role = new WorkspaceRole { TenantId = tenantId, Name = roleName, CreatedAt = Now };
        db.WorkspaceUsers.Add(user);
        db.WorkspaceRoles.Add(role);
        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            TenantId = tenantId,
            WorkspaceUserId = user.Id,
            WorkspaceRoleId = role.Id,
            CreatedAt = Now,
        });

        await db.SaveChangesAsync();
    }
}
