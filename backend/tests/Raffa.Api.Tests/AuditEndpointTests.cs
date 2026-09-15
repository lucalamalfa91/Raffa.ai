using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.Audit.Domain;
using Raffa.Audit.Infrastructure;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// Task E18/F02/US02/T01 (wave w16, NW-08; story us-02-audit-read-for-a-real-admin AC-1..AC-5;
/// ADR-025 §K): proves the ladder <c>GET /api/audit</c> adopted once the claims-based
/// <c>WorkspacePrincipalAuthorization</c> guard was deleted — the same <see cref="ICallerContext"/>
/// → <see cref="WorkspaceRoleResolver"/> shape <c>DocumentsEndpointExtensions</c>'s reprocess/delete
/// handlers already use. A live <c>Admin</c> membership in the selected tenant, resolved from
/// <c>workspace_membership</c>, and nobody else.
///
/// <para>
/// <see cref="AuditDbContext"/> is not swapped to the InMemory provider by the shared
/// <see cref="RaffaApiFactory"/> — no other class in this project ever queried it, since this route
/// was unreachable until this task. This class carries its own host that adds the swap and seeds
/// rows directly into it, the same "own DbContext, bypass the HTTP write path" shape
/// <c>InMemoryAskEngineFactory.SeedContractAsync</c> already uses for contracts.
/// </para>
/// </summary>
public sealed class AuditEndpointTests : IClassFixture<RaffaApiFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly RaffaApiFactory _baseFactory;

    public AuditEndpointTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task Admin_reads_their_tenants_audit_rows()
    {
        var factory = WithInMemoryAudit(_baseFactory);
        var client = factory.CreateClient();
        const string adminEmail = "admin@acme.example";

        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", adminEmail);
        await SeedAuditEventAsync(factory, tenantId, adminEmail, "document.uploaded", "Document", "doc-1");

        var response = await GetAuditAsync(client, tenantId, adminEmail);

        await AssertStatusAsync(HttpStatusCode.OK, response);
        var events = await ReadEventsAsync(response);
        Assert.Contains(events, e =>
            e.GetProperty("resourceId").GetString() == "doc-1" &&
            e.GetProperty("action").GetString() == "document.uploaded");
    }

    [Fact]
    public async Task Procurement_member_is_refused_with_403_never_404()
    {
        var factory = WithInMemoryAudit(_baseFactory);
        var client = factory.CreateClient();
        const string adminEmail = "founder@acme.example";
        const string buyerEmail = "buyer@acme.example";

        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", adminEmail);
        await SeedMembershipAsync(factory, tenantId, buyerEmail, WorkspaceRoleName.Procurement);

        var response = await GetAuditAsync(client, tenantId, buyerEmail);

        await AssertStatusAsync(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Non_member_gets_404_never_403()
    {
        var factory = WithInMemoryAudit(_baseFactory);
        var client = factory.CreateClient();
        const string adminEmail = "captain@acme.example";

        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", adminEmail);

        // "ghost" is a real, authenticated caller (a validated identity) but holds no membership
        // row anywhere -- ADR-025 Rule B1: a non-member is 404, never 403.
        var response = await GetAuditAsync(client, tenantId, "ghost@acme.example");

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task A_second_tenants_rows_never_appear()
    {
        var factory = WithInMemoryAudit(_baseFactory);
        var client = factory.CreateClient();
        const string adminA = "admin-a@acme.example";
        const string adminB = "admin-b@acme.example";

        var tenantA = await CreateWorkspaceAsync(client, "Tenant A Co", adminA);
        var tenantB = await CreateWorkspaceAsync(client, "Tenant B Co", adminB);
        await SeedAuditEventAsync(factory, tenantA, adminA, "document.uploaded", "Document", "doc-a");
        await SeedAuditEventAsync(factory, tenantB, adminB, "document.uploaded", "Document", "doc-b");

        var ownResponse = await GetAuditAsync(client, tenantA, adminA);
        await AssertStatusAsync(HttpStatusCode.OK, ownResponse);
        var ownEvents = await ReadEventsAsync(ownResponse);
        Assert.Contains(ownEvents, e => e.GetProperty("resourceId").GetString() == "doc-a");
        Assert.DoesNotContain(ownEvents, e => e.GetProperty("resourceId").GetString() == "doc-b");

        // S-T26: Tenant A's own Admin, naming Tenant B's id, holds no membership there -- 404, and
        // (since a 404 carries no body) zero T2 rows in any form, not even a filtered empty list.
        var crossResponse = await GetAuditAsync(client, tenantB, adminA);
        await AssertStatusAsync(HttpStatusCode.NotFound, crossResponse);
        var crossBody = await crossResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("doc-b", crossBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_tenant_header_returns_400()
    {
        var factory = WithInMemoryAudit(_baseFactory);
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/audit");
        request.Headers.Add("X-User-Id", "admin@acme.example");
        // No X-Tenant-Id at all -- the caller is real, the request is malformed.

        var response = await client.SendAsync(request);

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task Non_guid_tenant_header_returns_400()
    {
        var factory = WithInMemoryAudit(_baseFactory);
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/audit");
        request.Headers.Add("X-User-Id", "admin@acme.example");
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task No_identity_returns_401()
    {
        // ImplicitTenantAdmin = false: RaffaApiFactory's default filter otherwise synthesizes an
        // implicit caller for any request naming no X-User-Id, which would hide the 401 this test
        // exists to prove -- see that type's own doc comment.
        using var bare = new RaffaApiFactory { ImplicitTenantAdmin = false };
        var factory = WithInMemoryAudit(bare);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/audit");

        await AssertStatusAsync(HttpStatusCode.Unauthorized, response);
    }

    // ----- helpers -----

    /// <summary>Swaps <see cref="AuditDbContext"/> onto the shared InMemory provider -- the same
    /// "RemoveAll then re-add on the shared InMemory internal service provider" shape
    /// <c>InMemoryAskEngineFactory</c> already uses for the other per-module DbContexts.
    /// AddDbContext's own core-services registration is TryAdd, so the host's real
    /// Npgsql-configured <see cref="AuditDbContext"/> must be removed first or this swap is
    /// silently ignored and every query would try (and fail/hang) against the real, unreachable
    /// Postgres host the connection string names.</summary>
    private static WebApplicationFactory<Program> WithInMemoryAudit(WebApplicationFactory<Program> factory)
    {
        var auditDbName = $"audit-{Guid.NewGuid()}";
        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AuditDbContext>>();
            services.RemoveAll<AuditDbContext>();
            services.AddDbContext<AuditDbContext>(o => o
                .UseInMemoryDatabase(auditDbName)
                .UseInternalServiceProvider(InMemoryAskEngineFactory.InMemoryProviderServices));
        }));
    }

    private static async Task<Guid> CreateWorkspaceAsync(HttpClient client, string name, string creatorEmail)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
        {
            Content = JsonContent.Create(new { name }),
        };
        request.Headers.Add("X-User-Id", creatorEmail);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> GetAuditAsync(HttpClient client, Guid tenantId, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/audit");
        request.Headers.Add("X-User-Id", userId);
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        return await client.SendAsync(request);
    }

    /// <summary>Deliberately does not dispose the parsed <see cref="JsonDocument"/>: every
    /// <see cref="JsonElement"/> in the returned list is a view over its buffer, and both callers
    /// inspect the elements after this method returns -- disposing here first would throw
    /// <see cref="ObjectDisposedException"/> on the caller's own first <c>GetProperty</c> call.</summary>
    private static async Task<List<JsonElement>> ReadEventsAsync(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.EnumerateArray().ToList();
    }

    /// <summary>Seeds a membership directly through the DbContext -- the same shape
    /// <c>DocumentAdminActionsAuthorizationTests</c> uses, for the same reason (see that class's own
    /// doc comment: <c>WorkspaceMembershipService.InviteAsync</c> grants at invite time only until
    /// phase 3 moves the grant to accept, and a fixture built on that seam would silently break
    /// then).</summary>
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

    /// <summary>Seeds one <see cref="AuditEvent"/> straight into the InMemory
    /// <see cref="AuditDbContext"/> <see cref="WithInMemoryAudit"/> wires -- bypassing
    /// <see cref="IAuditWriter"/> entirely, the same "resolve the real service from the host's own
    /// container, skip HTTP" shape <c>InMemoryAskEngineFactory.SeedContractAsync</c> already uses.</summary>
    private static async Task SeedAuditEventAsync(
        WebApplicationFactory<Program> factory,
        Guid tenantId,
        string actor,
        string action,
        string resourceType,
        string resourceId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        db.AuditEvents.Add(new AuditEvent
        {
            TenantId = new TenantId(tenantId),
            Actor = actor,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            OccurredAt = Now,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Reads the response body before asserting, so a failure prints the server's own
    /// error text instead of a bare "Expected Forbidden, actual InternalServerError".</summary>
    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == expected,
            $"HTTP {(int)response.StatusCode}: {body[..Math.Min(1000, body.Length)]}");
    }
}
