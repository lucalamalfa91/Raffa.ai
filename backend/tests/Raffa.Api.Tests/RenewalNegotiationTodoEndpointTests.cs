using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.Renewals.Application;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E29/F01/US01/T01 (todo-entity-api; parent story
/// us-01-todo-entity-api AC-2/AC-3; wave w19 NW-85) that <c>GET</c>/<c>PUT
/// /api/renewals/{id}/negotiation-todos</c> are actually mapped in <c>Program.cs</c>, enforce the
/// same <see cref="Raffa.Api.Infrastructure.ICallerContext"/> ladder every other tenant-scoped route
/// in this host already uses, gate the tick to Admin/Procurement ("authz before retrieval"), never
/// invent a point, and audit every write with the caller's resolved token subject as
/// <see cref="AuditEntry.Actor"/>. Mirrors <see cref="AuditEndpointTests"/>'s own shape (the closest
/// precedent: a role-gated, tenant-scoped route proven with real membership rows over the InMemory
/// swap) rather than <see cref="RenewalsEndpointTests"/>'s narrower "pre-DB-call branches only"
/// scope, because this task's own "Tests required" table names "GET/tick routes + audit actor + RLS"
/// as the integration level here. Genuine Postgres-level RLS enforcement for the new table is proved
/// separately, at the Testcontainers level, by
/// <c>Raffa.Renewals.Tests.RenewalNegotiationTodoRlsCrossTenantIsolationTests</c> (and automatically
/// by <c>RenewalActionRlsMigrationCheckTests</c>, which discovers every <c>TenantScopedEntity</c>
/// dynamically) — the cross-tenant test here proves the same invariant functionally, end-to-end
/// through HTTP, the way <see cref="AuditEndpointTests.A_second_tenants_rows_never_appear"/> already
/// does for its own sibling route.
/// </summary>
public sealed class RenewalNegotiationTodoEndpointTests : IClassFixture<RaffaApiFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly RaffaApiFactory _baseFactory;

    public RenewalNegotiationTodoEndpointTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    // ----- GET: guard clauses -----

    [Fact]
    public async Task Get_missing_tenant_header_returns_400()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/renewals/{Guid.NewGuid()}/negotiation-todos");

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task Get_invalid_tenant_header_returns_400()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/renewals/{Guid.NewGuid()}/negotiation-todos");
        request.Headers.Add("X-Tenant-Id", "not-a-guid");
        request.Headers.Add("X-User-Id", "admin@acme.example");

        var response = await client.SendAsync(request);

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task Get_invalid_route_id_returns_400()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", "admin@acme.example");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/renewals/not-a-guid/negotiation-todos");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        request.Headers.Add("X-User-Id", "admin@acme.example");

        var response = await client.SendAsync(request);

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task Get_non_member_gets_404_never_403()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", "admin@acme.example");

        // "ghost" is a real, authenticated caller but holds no membership row anywhere -- ADR-025
        // Rule B1: a non-member is 404, never 403.
        var response = await GetTodosAsync(client, tenantId, "ghost@acme.example");

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    // ----- GET: the read-back -----

    [Fact]
    public async Task Get_returns_an_empty_list_when_nothing_was_ever_upserted()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", "admin@acme.example");
        var contractId = Guid.NewGuid();

        var response = await GetTodosAsync(client, tenantId, "admin@acme.example", contractId);

        await AssertStatusAsync(HttpStatusCode.OK, response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Empty(body.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Get_returns_upserted_todos_ordered_by_rank_any_live_member_may_read()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", "admin@acme.example");
        await SeedMembershipAsync(factory, tenantId, "buyer@acme.example", WorkspaceRoleName.Procurement);
        var contractId = Guid.NewGuid();

        await UpsertTodosAsync(
            factory, tenantId, contractId,
            [
                new RenewalNegotiationTodoPoint("payment_terms", "Payment terms", 2, "Net 30", "Net 60", "cash flow", []),
                new RenewalNegotiationTodoPoint("above_band_price", "Above-band price", 1, "current", "target", "rationale", ["fact:1:x"]),
            ]);

        // A different, merely-Procurement live member reads -- GET carries no extra role gate.
        var response = await GetTodosAsync(client, tenantId, "buyer@acme.example", contractId);

        await AssertStatusAsync(HttpStatusCode.OK, response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = body.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal("above_band_price", items[0].GetProperty("pointKey").GetString()); // rank 1 first.
        Assert.Equal("Open", items[0].GetProperty("status").GetString());
        Assert.Equal("ask", items[0].GetProperty("source").GetString());
        Assert.Equal("payment_terms", items[1].GetProperty("pointKey").GetString());
    }

    [Fact]
    public async Task A_second_tenants_todos_never_appear()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        var tenantA = await CreateWorkspaceAsync(client, "Tenant A Co", "admin-a@acme.example");
        var tenantB = await CreateWorkspaceAsync(client, "Tenant B Co", "admin-b@acme.example");
        var contractA = Guid.NewGuid();
        var contractB = Guid.NewGuid();

        await UpsertTodosAsync(
            factory, tenantA, contractA,
            [new RenewalNegotiationTodoPoint("point-a", "Topic", 1, "c", "t", "r", [])]);
        await UpsertTodosAsync(
            factory, tenantB, contractB,
            [new RenewalNegotiationTodoPoint("point-b", "Topic", 1, "c", "t", "r", [])]);

        var ownResponse = await GetTodosAsync(client, tenantA, "admin-a@acme.example", contractA);
        await AssertStatusAsync(HttpStatusCode.OK, ownResponse);
        using var ownBody = JsonDocument.Parse(await ownResponse.Content.ReadAsStringAsync());
        var ownItem = Assert.Single(ownBody.RootElement.EnumerateArray());
        Assert.Equal("point-a", ownItem.GetProperty("pointKey").GetString());

        // Tenant A's own Admin, naming Tenant B's contract, holds no membership in Tenant B -- 404,
        // and (a 404 carries no body) zero of Tenant B's rows in any form.
        var crossResponse = await GetTodosAsync(client, tenantB, "admin-a@acme.example", contractB);
        await AssertStatusAsync(HttpStatusCode.NotFound, crossResponse);
        var crossBody = await crossResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("point-b", crossBody, StringComparison.Ordinal);
    }

    // ----- PUT (tick): guard clauses -----

    [Fact]
    public async Task Tick_missing_tenant_header_returns_400()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        using var request = TickRequest(Guid.NewGuid(), "above_band_price");

        var response = await client.SendAsync(request);

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task Tick_invalid_tenant_header_returns_400()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        using var request = TickRequest(Guid.NewGuid(), "above_band_price");
        request.Headers.Add("X-Tenant-Id", "not-a-guid");
        request.Headers.Add("X-User-Id", "admin@acme.example");

        var response = await client.SendAsync(request);

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task Tick_no_identity_returns_401()
    {
        // ImplicitTenantAdmin = false: the default filter otherwise synthesizes an implicit caller
        // for any request naming no X-User-Id, which would hide the 401 this test exists to prove.
        using var bare = new RaffaApiFactory { ImplicitTenantAdmin = false };
        var factory = WithInMemoryHost(baseFactory: bare);
        var client = factory.CreateClient();
        using var request = TickRequest(Guid.NewGuid(), "above_band_price");
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        await AssertStatusAsync(HttpStatusCode.Unauthorized, response);
    }

    [Fact]
    public async Task Tick_non_member_gets_404_never_403()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", "admin@acme.example");

        var response = await TickAsync(client, tenantId, Guid.NewGuid(), "above_band_price", "ghost@acme.example");

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task Tick_readonly_member_is_refused_with_403_never_404()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", "admin@acme.example");
        await SeedMembershipAsync(factory, tenantId, "reader@acme.example", WorkspaceRoleName.ReadOnly);

        var response = await TickAsync(client, tenantId, Guid.NewGuid(), "above_band_price", "reader@acme.example");

        await AssertStatusAsync(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Tick_invalid_route_id_returns_400_only_after_the_role_gate_passes()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", "admin@acme.example");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/renewals/not-a-guid/negotiation-todos")
        {
            Content = JsonContent.Create(new { pointKey = "above_band_price" }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        request.Headers.Add("X-User-Id", "admin@acme.example");

        var response = await client.SendAsync(request);

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task Tick_blank_point_key_returns_400()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", "admin@acme.example");

        var response = await TickAsync(client, tenantId, Guid.NewGuid(), pointKey: null, "admin@acme.example");

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task Tick_unknown_point_key_returns_404_and_never_invents_a_row()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", "admin@acme.example");
        var contractId = Guid.NewGuid();

        var response = await TickAsync(client, tenantId, contractId, "no_such_point", "admin@acme.example");

        await AssertStatusAsync(HttpStatusCode.NotFound, response);

        var getResponse = await GetTodosAsync(client, tenantId, "admin@acme.example", contractId);
        using var body = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.Empty(body.RootElement.EnumerateArray());
    }

    // ----- PUT (tick): success + audit actor -----

    [Fact]
    public async Task Tick_admin_marks_the_row_done_and_audits_the_resolved_token_subject_as_actor()
    {
        var auditWriter = new RecordingAuditWriter();
        var factory = WithInMemoryHost(auditWriter: auditWriter);
        var client = factory.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", "admin@acme.example");
        var contractId = Guid.NewGuid();

        await UpsertTodosAsync(
            factory, tenantId, contractId,
            [new RenewalNegotiationTodoPoint("above_band_price", "Above-band price", 1, "c", "t", "r", [])]);

        var response = await TickAsync(client, tenantId, contractId, "above_band_price", "admin@acme.example");

        await AssertStatusAsync(HttpStatusCode.OK, response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Done", body.RootElement.GetProperty("status").GetString());

        // Two entries total: the seed upsert's, then the tick's -- assert the tick's own (the last
        // one), rather than requiring the writer to support clearing between the two writes.
        Assert.Equal(2, auditWriter.Entries.Count);
        var entry = auditWriter.Entries[^1];
        Assert.Equal("renewal.negotiation_todos_written", entry.Action);
        Assert.Equal("renewal", entry.ResourceType);
        Assert.Equal(contractId.ToString(), entry.ResourceId);
        // ADR-011 w16 §15: actor is the caller's resolved token subject (the oid
        // TestUserIdAuthenticationHandler put on the request from X-User-Id), never the model and
        // never a default.
        Assert.Equal("admin@acme.example", entry.Actor);
    }

    [Fact]
    public async Task Tick_procurement_member_may_also_tick()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", "admin@acme.example");
        await SeedMembershipAsync(factory, tenantId, "buyer@acme.example", WorkspaceRoleName.Procurement);
        var contractId = Guid.NewGuid();

        await UpsertTodosAsync(
            factory, tenantId, contractId,
            [new RenewalNegotiationTodoPoint("above_band_price", "Above-band price", 1, "c", "t", "r", [])]);

        var response = await TickAsync(client, tenantId, contractId, "above_band_price", "buyer@acme.example");

        await AssertStatusAsync(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task Tick_never_un_ticks_done_on_a_later_call()
    {
        var factory = WithInMemoryHost();
        var client = factory.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", "admin@acme.example");
        var contractId = Guid.NewGuid();

        await UpsertTodosAsync(
            factory, tenantId, contractId,
            [new RenewalNegotiationTodoPoint("above_band_price", "Above-band price", 1, "c", "t", "r", [])]);
        await TickAsync(client, tenantId, contractId, "above_band_price", "admin@acme.example");

        // A later ask re-ranks the same point -- the tick must survive it (epic's own "Success looks
        // like": Done survives a repeat ask).
        await UpsertTodosAsync(
            factory, tenantId, contractId,
            [new RenewalNegotiationTodoPoint("above_band_price", "Above-band price", 1, "changed", "changed", "changed", [])]);

        var response = await GetTodosAsync(client, tenantId, "admin@acme.example", contractId);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal("Done", item.GetProperty("status").GetString());
    }

    // ----- helpers -----

    private WebApplicationFactory<Program> WithInMemoryHost(
        IAuditWriter? auditWriter = null, WebApplicationFactory<Program>? baseFactory = null)
    {
        var gateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
        return (baseFactory ?? _baseFactory)
            .WithInMemoryAskEngine(gateway, clock: new FixedClock(Now), auditWriter: auditWriter);
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

    /// <summary>Same shape as <c>AuditEndpointTests.SeedMembershipAsync</c> -- seeds a membership
    /// directly through the DbContext, bypassing the invite/accept HTTP round trip this test does
    /// not need.</summary>
    private static async Task SeedMembershipAsync(
        WebApplicationFactory<Program> factory, Guid tenantId, string email, WorkspaceRoleName roleName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();

        var tenant = new TenantId(tenantId);
        var user = new WorkspaceUser { TenantId = tenant, Email = email, ExternalSubjectId = email, CreatedAt = Now };
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

    /// <summary>Seeds negotiation TODOs by resolving the real
    /// <see cref="RenewalNegotiationTodoService"/> from the host's own container and calling
    /// <see cref="RenewalNegotiationTodoService.UpsertAsync"/> directly -- the same "resolve the real
    /// service, skip HTTP" shape <c>InMemoryAskEngineFactory.SeedContractAsync</c> already uses,
    /// since there is no HTTP-exposed way to create a row yet (that is epic-29/feature-02's host
    /// upsert, not this task's scope).</summary>
    private static async Task UpsertTodosAsync(
        WebApplicationFactory<Program> factory,
        Guid tenantId,
        Guid contractId,
        IReadOnlyCollection<RenewalNegotiationTodoPoint> points)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RenewalNegotiationTodoService>();

        var result = await service.UpsertAsync(
            new TenantId(tenantId), new EntityId(contractId), points, "seed-actor@acme.example");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
    }

    private static async Task<HttpResponseMessage> GetTodosAsync(
        HttpClient client, Guid tenantId, string userId, Guid? contractId = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/renewals/{contractId ?? Guid.NewGuid()}/negotiation-todos");
        request.Headers.Add("X-User-Id", userId);
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> TickAsync(
        HttpClient client, Guid tenantId, Guid contractId, string? pointKey, string userId)
    {
        using var request = TickRequest(contractId, pointKey);
        request.Headers.Add("X-User-Id", userId);
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage TickRequest(Guid contractId, string? pointKey) =>
        new(HttpMethod.Put, $"/api/renewals/{contractId}/negotiation-todos")
        {
            Content = JsonContent.Create(new { pointKey }),
        };

    /// <summary>Reads the response body before asserting, so a failure prints the server's own
    /// error text instead of a bare "Expected OK, actual InternalServerError".</summary>
    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == expected,
            $"HTTP {(int)response.StatusCode}: {body[..Math.Min(1000, body.Length)]}");
    }
}
