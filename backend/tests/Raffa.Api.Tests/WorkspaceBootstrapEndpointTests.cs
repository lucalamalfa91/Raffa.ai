using System.Net;
using System.Net.Http.Json;
using System.Text;
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
/// Host-level proof for task E14/F02/US01/T01 (wave w14, story us-01-creator-is-workspace-admin;
/// ADR-025 §D.2, ADR-009 w14 footer clause 6, ADR-026 §D5) — T4: `POST /api/workspaces` writes the
/// creator's own Admin membership in the same request that creates the tenant, requires a
/// presented identity (401 when absent), and ignores any `role` the request body tries to carry
/// (AC-1/AC-2/AC-3/AC-4).
/// <c>Raffa.Identity.Workspace.Tests.WorkspaceProvisioningServiceTests</c> proves the same four
/// writes land in one scope/one `SaveChangesAsync` against a real Postgres; this class proves the
/// HTTP contract on top of it — the 401 posture change, the 201 body's `role: "Admin"`, and that a
/// `role` field in the request body is inert — over the real host with the in-memory
/// <see cref="IdentityWorkspaceDbContext"/> swap <see cref="InMemoryAskEngineFactory"/> already
/// wires up for this exact table (see that helper's own doc comment).
/// </summary>
public sealed class WorkspaceBootstrapEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public WorkspaceBootstrapEndpointTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task Missing_identity_returns_401_and_writes_nothing()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/workspaces", new { name = "Acme Procurement" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNoWorkspaceWasWrittenAsync(factory);
    }

    [Fact]
    public async Task Blank_identity_header_returns_401_and_writes_nothing()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
        {
            Content = JsonContent.Create(new { name = "Acme Procurement" }),
        };
        request.Headers.Add("X-User-Id", "   ");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNoWorkspaceWasWrittenAsync(factory);
    }

    [Fact]
    public async Task Blank_name_is_400_not_401_when_identity_is_present()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
        {
            Content = JsonContent.Create(new { name = "   " }),
        };
        request.Headers.Add("X-User-Id", "admin@acme.example");

        var response = await client.SendAsync(request);

        // ADR-025 §B: identity absent is 401 (an authentication failure); identity present but the
        // body is malformed stays 400 through the existing Result<T> failure path.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_writes_the_creators_admin_membership_and_the_201_carries_the_role()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
        {
            // AC-3/ADR-025 Rule D.2b: a `role` field in the body is ignored, never honoured.
            // CreateWorkspaceRequest has no Role property at all, so this must be silently dropped
            // by body binding rather than producing "Procurement" anywhere in the response.
            Content = new StringContent(
                """{"name":"Acme Procurement","role":"Procurement"}""", Encoding.UTF8, "application/json"),
        };
        // Task E17/F01/US01/T01 (wave w15): no longer mixed-case/padded. NW-05
        // (E18/F01/US01/T01) replaced the header-reading ICallerIdentity this comment used to
        // describe with TokenCallerIdentity, whose own doc comment records the change as
        // deliberate: an Entra `oid` is "opaque, case-sensitive... lower-casing it here would
        // silently stop matching every row already bound at invite time" (ADR-010 w15 footer
        // §2.1). Trim/lower-case defence belonged to a header a human or the SPA could type;
        // it does not belong to a validated token claim, so this test no longer exercises it.
        request.Headers.Add("X-User-Id", "founder@acme.example");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tenantId = body.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("Acme Procurement", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("createdAt").ValueKind);
        Assert.Equal("Admin", body.RootElement.GetProperty("role").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        var tenant = new TenantId(tenantId);

        var workspace = await db.Workspaces.SingleAsync(w => w.TenantId == tenant);
        Assert.Equal("Acme Procurement", workspace.Name);

        var user = await db.WorkspaceUsers.SingleAsync(u => u.TenantId == tenant);
        Assert.Equal("founder@acme.example", user.Email);
        Assert.Null(user.ExternalSubjectId);

        var membership = await db.WorkspaceMemberships.SingleAsync(m => m.TenantId == tenant);
        Assert.Equal(user.Id, membership.WorkspaceUserId);

        var role = await db.WorkspaceRoles.SingleAsync(r => r.Id == membership.WorkspaceRoleId);
        Assert.Equal(WorkspaceRoleName.Admin, role.Name);

        // AC-4: creating a tenant grants no read of any existing tenant -- and the default role
        // catalog is still all five roles, not just the one the creator holds.
        var allRoles = await db.WorkspaceRoles.Where(r => r.TenantId == tenant).ToListAsync();
        Assert.Equal(Enum.GetValues<WorkspaceRoleName>().Length, allRoles.Count);
    }

    [Fact]
    public async Task Two_creators_get_two_independent_tenants()
    {
        var factory = WithInMemoryIdentity();
        var client = factory.CreateClient();

        var firstId = await CreateAsync(client, "Acme Procurement", "founder-a@acme.example");
        var secondId = await CreateAsync(client, "Contoso Buying", "founder-b@contoso.example");

        Assert.NotEqual(firstId, secondId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();

        // AC-4: the second creator's own membership is theirs alone -- no cross-tenant row.
        var secondTenant = new TenantId(secondId);
        var secondUser = await db.WorkspaceUsers.SingleAsync(u => u.TenantId == secondTenant);
        Assert.Equal("founder-b@contoso.example", secondUser.Email);
    }

    private WebApplicationFactory<Program> WithInMemoryIdentity()
    {
        var gateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
        return _baseFactory.WithInMemoryAskEngine(gateway);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string name, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
        {
            Content = JsonContent.Create(new { name }),
        };
        request.Headers.Add("X-User-Id", userId);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task AssertNoWorkspaceWasWrittenAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        Assert.Empty(await db.Workspaces.ToListAsync());
    }
}
