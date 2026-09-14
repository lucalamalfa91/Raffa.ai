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
/// Unit-level proof for <see cref="WorkspaceRoleResolver.ResolveAsync"/>'s current (post-NW-06,
/// wave w15; ADR-010 w15 footer §3/S15-13; ADR-025 §I) shape: <c>workspace_membership</c> is the
/// <b>only</b> source of a workspace role — no claims branch, no header branch. Rewritten by task
/// E17/F01/US01/T01: the wave base carried this class still constructing the deleted
/// <c>Raffa.Api.Infrastructure.HeaderCallerIdentity</c> (a build break — that type no longer
/// exists) and asserting the precedence NW-06's own doc comment calls "a deletion, not a
/// tidy-up" (an authenticated claim resolving ahead of membership). Both are gone in this
/// rewrite; the four cases below instead pin the shape NW-06 actually shipped.
///
/// <para>
/// Constructs <see cref="WorkspaceRoleResolver"/> against an EF Core InMemory
/// <see cref="IdentityWorkspaceDbContext"/> and the real <see cref="TokenCallerIdentity"/> (wired to
/// the same <see cref="HttpContext"/> under test, exactly like the production DI graph wires it) —
/// no fake stand-in for the identity seam, the same discipline the original file established.
/// <c>Raffa.Api/AssemblyInfo.cs</c>'s <c>InternalsVisibleTo("Raffa.Api.Tests")</c> grant (unchanged
/// by this task) is what makes both internal types reachable here.
/// </para>
/// </summary>
public sealed class WorkspaceRoleResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_real_membership_resolves_and_stray_role_headers_change_nothing()
    {
        // ADR-025 §E/§I: WorkspaceRoleResolver reads no X-Role/X-Workspace-Role header at all any
        // more -- proven by setting both to a lie ("Admin") on a caller who is really Procurement.
        var tenantId = new TenantId(Guid.NewGuid());
        const string subject = "buyer-oid-123";
        using var db = CreateDb();
        await SeedMembershipAsync(db, tenantId, subject, WorkspaceRoleName.Procurement);
        var resolver = CreateResolver(db, subject, out var httpContext);
        httpContext.Request.Headers["X-Role"] = "Admin";
        httpContext.Request.Headers["X-Workspace-Role"] = "Admin";

        var role = await resolver.ResolveAsync(httpContext, tenantId);

        Assert.Equal(WorkspaceRoleName.Procurement, role);
    }

    [Fact]
    public async Task A_validated_identity_with_no_membership_anywhere_resolves_to_no_role()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        using var db = CreateDb();
        var resolver = CreateResolver(db, "stranger-oid-999", out var httpContext);

        var role = await resolver.ResolveAsync(httpContext, tenantId);

        Assert.Null(role);
    }

    [Fact]
    public async Task No_identity_at_all_resolves_to_no_role()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        using var db = CreateDb();
        var resolver = CreateResolver(db, subject: null, out var httpContext);

        var role = await resolver.ResolveAsync(httpContext, tenantId);

        Assert.Null(role);
    }

    [Fact]
    public async Task A_role_and_tenant_claim_on_the_token_grant_nothing_without_a_real_membership_row()
    {
        // ADR-010 w15 footer §3/S15-13, ADR-025 §I ("a tenant_id or roles claim is never the
        // authorization source"): NW-06 deleted the claims branch entirely, so a token carrying
        // roles/tenant_id claims -- exactly the shape a real Entra app-role assignment produces --
        // must not resolve to that role absent a live workspace_membership row.
        var tenantId = new TenantId(Guid.NewGuid());
        using var db = CreateDb();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("oid", "claims-only-oid"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim("tenant_id", tenantId.Value.ToString()),
            ],
            authenticationType: "Test"));
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var resolver = new WorkspaceRoleResolver(db, new TenantContext(), new TokenCallerIdentity(accessor));

        var role = await resolver.ResolveAsync(httpContext, tenantId);

        Assert.Null(role);
    }

    // ----- helpers -----

    private static IdentityWorkspaceDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new IdentityWorkspaceDbContext(options);
    }

    /// <summary><paramref name="subject"/> becomes the token's validated <c>oid</c> claim
    /// (<see cref="TokenCallerIdentity"/>'s own source, via <c>GetObjectId()</c>) — a
    /// <see langword="null"/> subject leaves <paramref name="httpContext"/> with its default,
    /// unauthenticated principal, matching "no bearer token presented".</summary>
    private static WorkspaceRoleResolver CreateResolver(
        IdentityWorkspaceDbContext db, string? subject, out HttpContext httpContext)
    {
        httpContext = new DefaultHttpContext();
        if (subject is not null)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("oid", subject)], authenticationType: "Test"));
        }

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new WorkspaceRoleResolver(db, new TenantContext(), new TokenCallerIdentity(accessor));
    }

    /// <summary>Seeds the membership row directly through the DbContext, keyed on
    /// <see cref="WorkspaceUser.ExternalSubjectId"/> (the token <c>oid</c> this resolver now
    /// matches, ADR-010 w15 footer §2.1) rather than email.</summary>
    private static async Task SeedMembershipAsync(
        IdentityWorkspaceDbContext db, TenantId tenantId, string externalSubjectId, WorkspaceRoleName roleName)
    {
        var user = new WorkspaceUser
        {
            TenantId = tenantId,
            Email = $"{externalSubjectId}@acme.example",
            ExternalSubjectId = externalSubjectId,
            CreatedAt = Now,
        };
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
