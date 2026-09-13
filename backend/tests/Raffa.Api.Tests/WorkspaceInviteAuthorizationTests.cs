using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// T3 — "the invite-hole regression (the one that must exist)" — task E14/F02/US01/T01 (wave w14,
/// story us-01-creator-is-workspace-admin; ADR-025 §D.1a/§H). On the wave's base checkout
/// `POST /api/workspaces/{tenantId}/invites` had no authorization at all: no <c>HttpContext</c>, no
/// claim, no membership check — a caller who could reach the API and guess a tenant GUID could
/// grant themselves Admin over another customer's contracts by naming "Admin" in the request body,
/// and <c>WorkspaceMembershipService.InviteAsync</c> wrote a <b>live</b> membership row for it. This
/// class proves the fix end to end, over the real HTTP pipeline and a real (in-memory) membership
/// table, not just the guard logic in isolation: 401 (no identity) -&gt; 404 (non-member, never
/// 403 — a tenant-existence oracle) -&gt; 403 (member, wrong role) -&gt; 201 (Admin).
///
/// <para>
/// Also carries ADR-025 §H's T2(a) — "every task in this wave must carry" — a caller who sends a
/// static <c>X-Role</c>/<c>X-Workspace-Role: Admin</c> header must never be treated as Admin here:
/// the guard reads <c>workspace_membership</c> only, never a header or a claim (see
/// <c>WorkspaceInvitesEndpointExtensions.ResolveMembershipRoleAsync</c>'s own doc comment for why it
/// does not call <c>WorkspaceRoleResolver.ResolveAsync</c>, whose header branch would otherwise run
/// first).
/// </para>
/// </summary>
public sealed class WorkspaceInviteAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _baseFactory;

    public WorkspaceInviteAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task No_identity_returns_401()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/workspaces/{tenantId}/invites", new { email = "new.hire@acme.example", role = "Procurement" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_member_gets_404_never_403()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        // "stranger@acme.example" is a well-formed identity with no membership row anywhere.
        var response = await InviteAsync(client, tenantId, "stranger@acme.example", "new.hire@acme.example", "Procurement");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_procurement_member_gets_403()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedMembershipAsync(factory, tenantId, "buyer@acme.example", WorkspaceRoleName.Procurement);

        var response = await InviteAsync(client, tenantId, "buyer@acme.example", "new.hire@acme.example", "Procurement");

        // ADR-025 §B Rule B1: the caller legitimately sees the tenant (a live member); only the
        // action is denied.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_member_can_invite_including_another_admin()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedMembershipAsync(factory, tenantId, "admin@acme.example", WorkspaceRoleName.Admin);

        // ADR-025 Rule D.1b: an Admin may invite another Admin.
        var response = await InviteAsync(client, tenantId, "admin@acme.example", "second.admin@acme.example", "Admin");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Admin", body.RootElement.GetProperty("role").GetString());
        Assert.Equal("second.admin@acme.example", body.RootElement.GetProperty("email").GetString());

        // 2026-09-13 (E15/F01/US01/T01, phase 3): invite alone no longer writes a
        // membership -- only accept does (ADR-025 Rule D.3). The response-body assertions
        // above already prove the invite itself; the DB-level proof is the live invitation
        // row, not a membership this call no longer creates.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        var tenant = new TenantId(tenantId);
        Assert.True(await db.WorkspaceInvitations.AnyAsync(
            i => i.TenantId == tenant
                && i.Email == "second.admin@acme.example"
                && i.AcceptedAt == null
                && i.RevokedAt == null));
    }

    [Fact]
    public async Task A_membership_in_a_different_tenant_grants_nothing_here()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        var homeTenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        await SeedMembershipAsync(factory, homeTenant, "admin@acme.example", WorkspaceRoleName.Admin);

        // ADR-022 w14 footer clause 3 / ADR-025 Rule D.1c: the tenant is the route value, verified
        // against membership -- an Admin of one tenant is a non-member of another.
        var response = await InviteAsync(client, otherTenant, "admin@acme.example", "new.hire@acme.example", "Procurement");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_x_tenant_id_header_naming_a_foreign_tenant_is_never_consulted()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        var homeTenant = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        await SeedMembershipAsync(factory, homeTenant, "admin@acme.example", WorkspaceRoleName.Admin);
        await SeedMembershipAsync(factory, foreignTenant, "other-admin@contoso.example", WorkspaceRoleName.Admin);

        // The route names homeTenant (a tenant this caller does not belong to); a crafted
        // X-Tenant-Id naming a tenant the caller *does* administer must not leak through as the
        // authorization tenant (ADR-022 w14 footer clause 3: "X-Tenant-Id is not an input to a
        // membership route").
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{foreignTenant}/invites")
        {
            Content = JsonContent.Create(new { email = "new.hire@acme.example", role = "Procurement" }),
        };
        request.Headers.Add("X-User-Id", "admin@acme.example");
        request.Headers.Add("X-Tenant-Id", homeTenant.ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Spoofed_admin_role_header_from_a_non_member_still_gets_404_never_201()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        // ADR-025 §H T2(a) (non-negotiable): "stranger@acme.example" holds no membership row
        // anywhere. A self-declared X-Role/X-Workspace-Role header must not buy Admin — the guard
        // must still answer 404 (never 403, a tenant-existence oracle; never 201).
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{tenantId}/invites")
        {
            Content = JsonContent.Create(new { email = "new.hire@acme.example", role = "Procurement" }),
        };
        request.Headers.Add("X-User-Id", "stranger@acme.example");
        request.Headers.Add("X-Role", "Admin");
        request.Headers.Add("X-Workspace-Role", "Admin");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Spoofed_admin_role_header_from_a_real_procurement_member_still_gets_403_never_201()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedMembershipAsync(factory, tenantId, "buyer@acme.example", WorkspaceRoleName.Procurement);

        // ADR-025 §H T2(a) (non-negotiable): "buyer@acme.example" is a real member, but Procurement,
        // not Admin. A self-declared X-Role/X-Workspace-Role header must not override the caller's
        // own live membership row — the guard must still answer 403, never 201.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{tenantId}/invites")
        {
            Content = JsonContent.Create(new { email = "new.hire@acme.example", role = "Procurement" }),
        };
        request.Headers.Add("X-User-Id", "buyer@acme.example");
        request.Headers.Add("X-Role", "Admin");
        request.Headers.Add("X-Workspace-Role", "Admin");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ----- helpers -----

    private WebApplicationFactory<Program> WithInMemoryIdentity()
    {
        var gateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
        return _baseFactory.WithInMemoryAskEngine(gateway);
    }

    private static async Task<HttpResponseMessage> InviteAsync(
        HttpClient client, Guid tenantId, string callerUserId, string inviteEmail, string role)
    {
        // Must await inside the `using` scope (not return the un-awaited Task): disposing the
        // request/content before SendAsync's own read of it completes throws ObjectDisposedException.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{tenantId}/invites")
        {
            Content = JsonContent.Create(new { email = inviteEmail, role }),
        };
        request.Headers.Add("X-User-Id", callerUserId);
        return await client.SendAsync(request);
    }

    private static async Task SeedMembershipAsync(
        WebApplicationFactory<Program> factory, Guid tenantId, string email, WorkspaceRoleName roleName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();

        var tenant = new TenantId(tenantId);
        var user = new WorkspaceUser { TenantId = tenant, Email = email, CreatedAt = Now };
        var role = new WorkspaceRole { TenantId = tenant, Name = roleName, CreatedAt = Now };
        db.WorkspaceUsers.Add(user);
        db.WorkspaceRoles.Add(role);
        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            TenantId = tenant,
            WorkspaceUserId = user.Id,
            WorkspaceRoleId = role.Id,
            CreatedAt = Now,
        });

        await db.SaveChangesAsync();
    }
}
