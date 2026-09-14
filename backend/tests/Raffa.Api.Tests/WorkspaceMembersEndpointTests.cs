using System.Net;
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
/// Host-level proof for task E14/F04/US01/T01 (wave w14, story us-01-members-api; ADR-026 §D3,
/// ADR-025 §B/§D.4, ADR-009 w14 footer clause 7, ADR-022 w14 footer) of
/// `GET /api/workspaces/{tenantId}/members` over the real HTTP pipeline and an in-memory
/// <see cref="IdentityWorkspaceDbContext"/> (<see cref="InMemoryAskEngineFactory"/>), the same shape
/// <c>WorkspaceInviteAuthorizationTests</c>/<c>WorkspaceBootstrapEndpointTests</c> already use for
/// this exact table: 401 (no identity) -&gt; 404 (non-member, never 403 — a tenant-existence oracle,
/// **T1b**) -&gt; 200 (live member, any role). Also carries the removed-member proof AC-3 exists to
/// pin: a stray <see cref="WorkspaceUser"/> row with no membership and no invitation (audit
/// continuity — the row is never deleted) must not render as a member, which is exactly the class
/// of bug a `workspace_user`-driven roster would produce. The pure Active/Invited derivation rules
/// themselves (highest-role collapse, expired/revoked/accepted exclusion, name mapping) are proven
/// without a database in <c>Raffa.Identity.Workspace.Tests.WorkspaceRosterTests</c>
/// (<see cref="WorkspaceMembershipService.ComposeRoster"/>).
/// </summary>
public sealed class WorkspaceMembersEndpointTests : IClassFixture<RaffaApiFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _baseFactory;

    public WorkspaceMembersEndpointTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task No_identity_returns_401()
    {
        // NW-05 (2026-09-14): the shared test host runs a caller-less request as an implicit Admin so
        // the pre-NW-05 endpoint tests keep their meaning; THIS test is about no identity at all, so
        // it builds a host without that filter.
        using var bare = new RaffaApiFactory { ImplicitTenantAdmin = false };
        var factory = bare.WithInMemoryAskEngine(new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions())));
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/workspaces/{tenantId}/members");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_member_gets_404_never_403_and_the_body_carries_no_tenant_name()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        // "stranger@acme.example" is a well-formed identity with no membership row anywhere (T1b,
        // ADR-025 Rule D.4b): never 403 (a tenant-existence oracle), never an empty 200 (also an
        // oracle -- "that tenant exists and is empty").
        var response = await GetMembersAsync(client, tenantId, "stranger@acme.example");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Empty(body);
    }

    [Fact]
    public async Task A_membership_in_a_different_tenant_still_gets_404()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        var homeTenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        await SeedMemberAsync(factory, homeTenant, "admin@acme.example", WorkspaceRoleName.Admin);

        // ADR-022 w14 footer clause 3: the tenant is the route value -- an Admin of one tenant is
        // a non-member of another.
        var response = await GetMembersAsync(client, otherTenant, "admin@acme.example");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_x_tenant_id_header_naming_a_tenant_the_caller_belongs_to_is_never_consulted()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        var homeTenant = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        await SeedMemberAsync(factory, homeTenant, "admin@acme.example", WorkspaceRoleName.Admin);
        await SeedMemberAsync(factory, foreignTenant, "other-admin@contoso.example", WorkspaceRoleName.Admin);

        // The route names foreignTenant (a tenant this caller does not belong to); a crafted
        // X-Tenant-Id naming a tenant the caller *does* administer must not leak through as the
        // authorization tenant (ADR-022 w14 footer clause 3; the ADR-026 §D3 amendment corrects
        // that ADR's own header line to match: X-User-Id only, never X-Tenant-Id).
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/workspaces/{foreignTenant}/members");
        request.Headers.Add("X-User-Id", "admin@acme.example");
        request.Headers.Add("X-Tenant-Id", homeTenant.ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_live_member_gets_200_with_the_roster_any_role_may_read()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedMemberAsync(factory, tenantId, "admin@acme.example", WorkspaceRoleName.Admin, displayName: "Ada Admin");
        await SeedMemberAsync(factory, tenantId, "buyer@acme.example", WorkspaceRoleName.Procurement);

        // ADR-025 Rule D.4a: any live member reads, not just Admin -- the non-Admin buyer is the
        // caller here.
        var response = await GetMembersAsync(client, tenantId, "buyer@acme.example");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var members = body.RootElement.GetProperty("members").EnumerateArray().ToList();
        Assert.Equal(2, members.Count);

        var admin = members.Single(m => m.GetProperty("email").GetString() == "admin@acme.example");
        Assert.Equal("Ada Admin", admin.GetProperty("name").GetString());
        Assert.Equal("Admin", admin.GetProperty("role").GetString());
        Assert.Equal("Active", admin.GetProperty("status").GetString());

        var buyer = members.Single(m => m.GetProperty("email").GetString() == "buyer@acme.example");
        // AC-6: never derived from the email address -- no stored DisplayName means a JSON null,
        // not an invented "buyer" label.
        Assert.Equal(JsonValueKind.Null, buyer.GetProperty("name").ValueKind);
        Assert.Equal("Procurement", buyer.GetProperty("role").GetString());
        Assert.Equal("Active", buyer.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_removed_member_has_no_membership_or_invitation_row_and_is_absent_not_active()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedMemberAsync(factory, tenantId, "admin@acme.example", WorkspaceRoleName.Admin);
        // AC-3: a removed member keeps their WorkspaceUser row (audit continuity; WorkspaceSignIn
        // needs it) with ExternalSubjectId still bound -- exactly the shape a
        // workspace_user-driven roster would misrender as Active. No membership row, no
        // invitation row.
        await SeedRemovedUserAsync(factory, tenantId, "removed@acme.example", "entra-sub-removed");

        var response = await GetMembersAsync(client, tenantId, "admin@acme.example");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var emails = body.RootElement.GetProperty("members").EnumerateArray()
            .Select(m => m.GetProperty("email").GetString())
            .ToList();

        Assert.Contains("admin@acme.example", emails);
        Assert.DoesNotContain("removed@acme.example", emails);
    }

    [Fact]
    public async Task A_live_invitation_renders_invited_and_an_expired_one_renders_nothing()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedMemberAsync(factory, tenantId, "admin@acme.example", WorkspaceRoleName.Admin);
        await SeedInvitationAsync(factory, tenantId, "invited@acme.example", WorkspaceRoleName.Procurement, Now.AddDays(7));
        await SeedInvitationAsync(factory, tenantId, "expired@acme.example", WorkspaceRoleName.Legal, Now.AddDays(-1));

        var response = await GetMembersAsync(client, tenantId, "admin@acme.example");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var members = body.RootElement.GetProperty("members").EnumerateArray().ToList();

        var invited = members.Single(m => m.GetProperty("email").GetString() == "invited@acme.example");
        Assert.Equal("Invited", invited.GetProperty("status").GetString());
        Assert.Equal("Procurement", invited.GetProperty("role").GetString());

        Assert.DoesNotContain(members, m => m.GetProperty("email").GetString() == "expired@acme.example");
    }

    // ----- helpers -----

    private WebApplicationFactory<Program> WithInMemoryIdentity()
    {
        var gateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
        return _baseFactory.WithInMemoryAskEngine(gateway, clock: FixedClock.Instance);
    }

    private static async Task<HttpResponseMessage> GetMembersAsync(HttpClient client, Guid tenantId, string callerUserId)
    {
        // Must await inside the `using` scope (not return the un-awaited Task): disposing the
        // request before SendAsync's own read of it completes throws ObjectDisposedException.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/workspaces/{tenantId}/members");
        request.Headers.Add("X-User-Id", callerUserId);
        return await client.SendAsync(request);
    }

    private static async Task SeedMemberAsync(
        WebApplicationFactory<Program> factory,
        Guid tenantId,
        string email,
        WorkspaceRoleName roleName,
        string? displayName = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();

        var tenant = new TenantId(tenantId);
        var user = new WorkspaceUser { TenantId = tenant, Email = email, DisplayName = displayName, CreatedAt = Now };
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

    private static async Task SeedRemovedUserAsync(
        WebApplicationFactory<Program> factory, Guid tenantId, string email, string externalSubjectId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();

        db.WorkspaceUsers.Add(new WorkspaceUser
        {
            TenantId = new TenantId(tenantId),
            Email = email,
            ExternalSubjectId = externalSubjectId,
            CreatedAt = Now,
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedInvitationAsync(
        WebApplicationFactory<Program> factory,
        Guid tenantId,
        string email,
        WorkspaceRoleName roleName,
        DateTimeOffset expiresAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();

        var tenant = new TenantId(tenantId);
        var role = new WorkspaceRole { TenantId = tenant, Name = roleName, CreatedAt = Now };
        db.WorkspaceRoles.Add(role);
        // The phase-2 InviteAsync and the phase-3 redesign both write a WorkspaceUser row for an
        // invited email unconditionally (ListMembersAsync's own doc comment) -- this fixture goes
        // straight through the DbContext rather than InviteAsync (which still also grants a live
        // membership in this phase) for the same reason the task text names for a Procurement
        // fixture: proving the derivation, not today's write path.
        db.WorkspaceUsers.Add(new WorkspaceUser { TenantId = tenant, Email = email, CreatedAt = Now });
        db.WorkspaceInvitations.Add(new WorkspaceInvitation
        {
            TenantId = tenant,
            Email = email,
            WorkspaceRoleId = role.Id,
            TokenHash = $"test-hash-{Guid.NewGuid():N}",
            InvitedBy = "admin@acme.example",
            CreatedAt = Now,
            ExpiresAt = expiresAt,
        });

        await db.SaveChangesAsync();
    }

    private sealed class FixedClock : IClock
    {
        public static readonly FixedClock Instance = new();

        public DateTimeOffset UtcNow => Now;
    }
}
