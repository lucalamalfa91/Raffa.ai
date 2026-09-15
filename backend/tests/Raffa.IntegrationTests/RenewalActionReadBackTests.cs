using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.Identity.Workspace.Domain;
using Raffa.SharedKernel;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E19/F01/US01/T01 (renewal-action-api; ADR-028 §D1) and its
/// parent story us-01-renewal-action-api: AC-1 (`GET /api/renewals/{id}/action` 200/404), AC-2 (every
/// `GET /api/renewals` row carries `savedAction` beside the unchanged `action`), AC-3 (cross-tenant
/// negative -- zero tenant-A rows in any form), AC-4 (401/400/404 caller ladder) and AC-5 (no
/// `DELETE` route; a `NotStarted` post survives) -- driven over real HTTP through the real
/// `Raffa.Api` composition root, against a real, migrated Postgres+pgvector+RLS database.
///
/// <para>
/// Reuses <see cref="R2IntegrationFixture"/> rather than a new fixture: it already migrates
/// `Raffa.Renewals` (including this module's own `AddTenantRowLevelSecurity` migration -- ADR-028
/// §D1 makes no schema change, so nothing new needs migrating), already wires the NW-05 test-only
/// caller bridge (<see cref="TestIdentityAuthenticationHandler"/> /
/// <see cref="ImplicitTenantAdminStartupFilter"/>) and already exposes
/// <see cref="R2IntegrationFixture.SeedContractAsync"/> for the auto-renewing contracts
/// `GET /api/renewals` needs in order to have a row to embed `savedAction` onto -- the same "reuse,
/// don't duplicate a fixture" convention <see cref="R2CrossTenantIsolationTests"/> already
/// established. Deliberately not <see cref="R0IntegrationFixture"/>: a sibling w16 task rewrites
/// that file this same phase.
/// </para>
///
/// <para>
/// Reuses <see cref="R1EndToEndTests"/>'s <c>GetAsync</c>/<c>PostAsync</c>/<c>ParseAsync</c> helpers
/// for the plain-tenant-header requests (they resolve as the fixture's implicit-Admin caller via
/// <see cref="ImplicitTenantAdminStartupFilter"/>) -- the same cross-class reuse
/// <see cref="R2EndToEndTests"/>/<see cref="R2CrossTenantIsolationTests"/> already established for
/// generic HTTP plumbing. <see cref="PostAsActorAsync"/>/<see cref="GetAsActorAsync"/> below cover
/// the requests that need a specific, non-implicit caller identity instead -- mirroring
/// <see cref="R1EndToEndTests"/>'s own <c>PostAsync(R1IntegrationFixture, ...)</c> overload, adapted
/// to this fixture's own type since that overload is declared against <c>R1IntegrationFixture</c>
/// specifically.
/// </para>
/// </summary>
public sealed class RenewalActionReadBackTests : IClassFixture<R2IntegrationFixture>
{
    private readonly R2IntegrationFixture _fixture;

    public RenewalActionReadBackTests(R2IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Get_action_returns_the_persisted_row_and_404_when_none_exists()
    {
        var client = _fixture.CreateClient();
        var tenantId = TenantId.New();
        var contract = await _fixture.SeedContractAsync(tenantId, autoRenewal: true);

        // AC-1, 404 half: nothing persisted yet for this contract.
        var beforeResponse = await R1EndToEndTests.GetAsync(
            client, $"/api/renewals/{contract.Id.Value}/action", tenantId.Value);
        Assert.Equal(HttpStatusCode.NotFound, beforeResponse.StatusCode);

        var postResponse = await R1EndToEndTests.PostAsync(
            client, $"/api/renewals/{contract.Id.Value}/action", tenantId.Value,
            new { owner = "procurement@acme.example", status = "InProgress", action = "Started negotiation" });
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        // AC-1, 200 half: the dedicated route reads back exactly the row the POST above upserted --
        // {id} keeps the same contract-id meaning it has on the POST (ADR-028 assumption 1).
        var afterResponse = await R1EndToEndTests.GetAsync(
            client, $"/api/renewals/{contract.Id.Value}/action", tenantId.Value);
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        var body = await R1EndToEndTests.ParseAsync(afterResponse);
        Assert.Equal(contract.Id.Value, body.GetProperty("contractId").GetGuid());
        Assert.Equal("procurement@acme.example", body.GetProperty("owner").GetString());
        Assert.Equal("InProgress", body.GetProperty("status").GetString());
        Assert.Equal("Started negotiation", body.GetProperty("action").GetString());
        Assert.True(body.TryGetProperty("updatedAt", out _));
    }

    /// <summary>
    /// Parent story us-01-renewal-action-api's own motivation: "a reload, a second device or a
    /// colleague sees the same action instead of an empty tracker" -- a named colleague writes, a
    /// different named colleague (a genuinely different caller identity, not just a second
    /// HttpClient) reads, and gets the identical persisted fact, because the row is tenant-scoped,
    /// not caller-scoped.
    /// </summary>
    [Fact]
    public async Task A_colleague_in_the_same_tenant_reads_back_the_same_action_a_different_colleague_wrote()
    {
        var client = _fixture.CreateClient();
        var tenantId = TenantId.New();
        var contract = await _fixture.SeedContractAsync(tenantId, autoRenewal: true);

        var writeResponse = await PostAsActorAsync(
            client, $"/api/renewals/{contract.Id.Value}/action", tenantId.Value, "colleague-a@acme.example",
            new { owner = "colleague-a@acme.example", status = "InProgress", action = "Started negotiation" });
        Assert.Equal(HttpStatusCode.OK, writeResponse.StatusCode);

        var readResponse = await GetAsActorAsync(
            client, $"/api/renewals/{contract.Id.Value}/action", tenantId.Value, "colleague-b@acme.example");
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        var body = await R1EndToEndTests.ParseAsync(readResponse);
        Assert.Equal("InProgress", body.GetProperty("status").GetString());
        Assert.Equal("Started negotiation", body.GetProperty("action").GetString());
    }

    /// <summary>
    /// AC-2 (`savedAction` beside the unchanged `action`) and AC-5 (no `DELETE`; a `NotStarted` post
    /// survives and the list still reports it).
    /// </summary>
    [Fact]
    public async Task List_rows_carry_savedAction_beside_the_unchanged_action_and_a_notstarted_undo_survives()
    {
        var client = _fixture.CreateClient();
        var tenantId = TenantId.New();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        var withAction = await _fixture.SeedContractAsync(
            tenantId, annualSpend: 50_000m, endDate: today.AddDays(90), autoRenewal: true);
        var withoutAction = await _fixture.SeedContractAsync(
            tenantId, annualSpend: 20_000m, endDate: today.AddDays(120), autoRenewal: true);

        var postResponse = await R1EndToEndTests.PostAsync(
            client, $"/api/renewals/{withAction.Id.Value}/action", tenantId.Value,
            new { owner = "procurement@acme.example", status = "InProgress", action = "Started negotiation" });
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        var listResponse = await R1EndToEndTests.GetAsync(client, "/api/renewals", tenantId.Value);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listBody = await R1EndToEndTests.ParseAsync(listResponse);
        var items = listBody.GetProperty("items").EnumerateArray().ToList();

        var rowWithAction = items.Single(i => i.GetProperty("contractId").GetGuid() == withAction.Id.Value);
        var rowWithoutAction = items.Single(i => i.GetProperty("contractId").GetGuid() == withoutAction.Id.Value);

        // AC-2: `action` is untouched -- still the deterministic calculator's own RecommendedAction
        // -- and `savedAction` carries the user's own recorded fact alongside it, never overwriting
        // it (ADR-028 §D1's binding name).
        Assert.False(string.IsNullOrEmpty(rowWithAction.GetProperty("action").GetString()));
        var savedAction = rowWithAction.GetProperty("savedAction");
        Assert.Equal(JsonValueKind.Object, savedAction.ValueKind);
        Assert.Equal("InProgress", savedAction.GetProperty("status").GetString());
        Assert.Equal("Started negotiation", savedAction.GetProperty("action").GetString());
        Assert.Equal("procurement@acme.example", savedAction.GetProperty("owner").GetString());

        // A contract with nothing recorded carries savedAction: null, not an omitted field.
        Assert.Equal(JsonValueKind.Null, rowWithoutAction.GetProperty("savedAction").ValueKind);

        // AC-5: "Undo" is a NotStarted POST, never a DELETE -- the upserted row survives (the table's
        // upsert on (tenant_id, contract_id)) and the list still reports it as savedAction, not null.
        var undoResponse = await R1EndToEndTests.PostAsync(
            client, $"/api/renewals/{withAction.Id.Value}/action", tenantId.Value,
            new { owner = "procurement@acme.example", status = "NotStarted", action = "Undo" });
        Assert.Equal(HttpStatusCode.OK, undoResponse.StatusCode);

        var afterUndoListResponse = await R1EndToEndTests.GetAsync(client, "/api/renewals", tenantId.Value);
        var afterUndoBody = await R1EndToEndTests.ParseAsync(afterUndoListResponse);
        var rowAfterUndo = afterUndoBody.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("contractId").GetGuid() == withAction.Id.Value);
        var savedActionAfterUndo = rowAfterUndo.GetProperty("savedAction");
        Assert.Equal(JsonValueKind.Object, savedActionAfterUndo.ValueKind);
        Assert.Equal("NotStarted", savedActionAfterUndo.GetProperty("status").GetString());

        // Same fact, read via the dedicated route too, not just the embedded field.
        var directResponse = await R1EndToEndTests.GetAsync(
            client, $"/api/renewals/{withAction.Id.Value}/action", tenantId.Value);
        Assert.Equal(HttpStatusCode.OK, directResponse.StatusCode);
        var directBody = await R1EndToEndTests.ParseAsync(directResponse);
        Assert.Equal("NotStarted", directBody.GetProperty("status").GetString());
    }

    /// <summary>
    /// AC-3: a different tenant asking for the same contract id gets 404 on the dedicated route, and
    /// its own (otherwise non-empty) list never carries so much as a null placeholder for a contract
    /// it does not own -- the row simply is not there at all.
    /// </summary>
    [Fact]
    public async Task A_different_tenant_gets_404_on_the_route_and_never_sees_the_row_in_its_own_list()
    {
        var client = _fixture.CreateClient();
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        var contractA = await _fixture.SeedContractAsync(
            tenantA, annualSpend: 50_000m, endDate: today.AddDays(90), autoRenewal: true);
        // Tenant B has its own, unrelated contract and no recorded action on it -- so tenant B's own
        // list is not vacuously empty, and its one row must not carry tenant A's fact either.
        var contractB = await _fixture.SeedContractAsync(
            tenantB, annualSpend: 30_000m, endDate: today.AddDays(60), autoRenewal: true);

        var postAsA = await R1EndToEndTests.PostAsync(
            client, $"/api/renewals/{contractA.Id.Value}/action", tenantA.Value,
            new { owner = "procurement@acme.example", status = "InProgress", action = "Started negotiation" });
        Assert.Equal(HttpStatusCode.OK, postAsA.StatusCode);

        // AC-3, the dedicated route: tenant B naming tenant A's contract id gets 404, never tenant
        // A's row.
        var routeAsB = await R1EndToEndTests.GetAsync(
            client, $"/api/renewals/{contractA.Id.Value}/action", tenantB.Value);
        Assert.Equal(HttpStatusCode.NotFound, routeAsB.StatusCode);

        // AC-3, the list: tenant B's own GET /api/renewals carries exactly its own contract, with no
        // trace of tenant A's action anywhere in the payload.
        var listAsB = await R1EndToEndTests.GetAsync(client, "/api/renewals", tenantB.Value);
        Assert.Equal(HttpStatusCode.OK, listAsB.StatusCode);
        var listBodyB = await R1EndToEndTests.ParseAsync(listAsB);
        var itemsB = listBodyB.GetProperty("items").EnumerateArray().ToList();
        var rowB = Assert.Single(itemsB);
        Assert.Equal(contractB.Id.Value, rowB.GetProperty("contractId").GetGuid());
        Assert.Equal(JsonValueKind.Null, rowB.GetProperty("savedAction").ValueKind);

        // Sanity, both directions: tenant A's own read still works (mirrors every existing
        // R*CrossTenantIsolationTests' own "both directions" check).
        var routeAsA = await R1EndToEndTests.GetAsync(
            client, $"/api/renewals/{contractA.Id.Value}/action", tenantA.Value);
        Assert.Equal(HttpStatusCode.OK, routeAsA.StatusCode);
    }

    /// <summary>
    /// AC-4: the same <c>ICallerContext</c> ladder every tenant-scoped route in this host uses -- no
    /// validated identity at all -&gt; 401 regardless of what any header says; a real identity with a
    /// non-GUID <c>X-Tenant-Id</c> -&gt; 400; a real identity naming a well-formed tenant it has no
    /// membership in -&gt; 404, never a 403 (ADR-025 Rule B1: a 403 would be a tenant-existence
    /// oracle).
    /// </summary>
    [Fact]
    public async Task The_caller_ladder_is_401_then_400_then_404_before_any_row_is_read()
    {
        var client = _fixture.CreateClient();
        var tenantId = TenantId.New();
        var contract = await _fixture.SeedContractAsync(tenantId, autoRenewal: true);

        // 401: no headers at all -- no X-Tenant-Id, no X-User-Id -- so ImplicitTenantAdminStartupFilter
        // never fires and TestIdentityAuthenticationHandler resolves no identity at all, the same
        // "unauthenticated caller" shape R0EndToEndTests' own audit-trail test uses.
        var noTokenResponse = await client.GetAsync($"/api/renewals/{contract.Id.Value}/action");
        Assert.Equal(HttpStatusCode.Unauthorized, noTokenResponse.StatusCode);

        // 400: a real identity (so step 1 passes), a tenant header that is not a GUID.
        using var badTenantRequest = new HttpRequestMessage(
            HttpMethod.Get, $"/api/renewals/{contract.Id.Value}/action");
        badTenantRequest.Headers.Add("X-Tenant-Id", "not-a-guid");
        badTenantRequest.Headers.Add("X-User-Id", "someone@example.test");
        var badTenantResponse = await client.SendAsync(badTenantRequest);
        Assert.Equal(HttpStatusCode.BadRequest, badTenantResponse.StatusCode);

        // 404: a real identity, a well-formed tenant, but that identity has never been granted
        // membership in it -- never 403 (there is no role check to fail here; the tenant is simply
        // unreachable to a stranger).
        using var nonMemberRequest = new HttpRequestMessage(
            HttpMethod.Get, $"/api/renewals/{contract.Id.Value}/action");
        nonMemberRequest.Headers.Add("X-Tenant-Id", TenantId.New().Value.ToString());
        nonMemberRequest.Headers.Add("X-User-Id", "stranger@example.test");
        var nonMemberResponse = await client.SendAsync(nonMemberRequest);
        Assert.Equal(HttpStatusCode.NotFound, nonMemberResponse.StatusCode);
    }

    /// <summary>
    /// Mirrors <see cref="R1EndToEndTests"/>'s own <c>PostAsync(R1IntegrationFixture, HttpClient,
    /// string, Guid, string, object)</c> overload, adapted to <see cref="R2IntegrationFixture"/>'s
    /// own type (that overload is declared against <c>R1IntegrationFixture</c> specifically, so it
    /// cannot be called with this file's fixture): NW-05 requires a presented caller to already be a
    /// member of the tenant it names, or <c>ICallerContext</c> answers 404 before the handler runs,
    /// so this grants that membership first, exactly as the invite flow would have.
    /// </summary>
    private async Task<HttpResponseMessage> PostAsActorAsync(
        HttpClient client, string url, Guid tenantId, string userId, object body)
    {
        await ImplicitTenantAdminStartupFilter.EnsureMembershipAsync(
            _fixture.Services, new TenantId(tenantId), userId, WorkspaceRoleName.Admin);

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        request.Headers.Add("X-User-Id", userId);
        return await client.SendAsync(request);
    }

    /// <summary>GET counterpart to <see cref="PostAsActorAsync"/> -- same membership pre-grant, same
    /// explicit, non-implicit caller identity.</summary>
    private async Task<HttpResponseMessage> GetAsActorAsync(
        HttpClient client, string url, Guid tenantId, string userId)
    {
        await ImplicitTenantAdminStartupFilter.EnsureMembershipAsync(
            _fixture.Services, new TenantId(tenantId), userId, WorkspaceRoleName.Admin);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        request.Headers.Add("X-User-Id", userId);
        return await client.SendAsync(request);
    }
}
