using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E13/F05/US01/T02 (story us-01-conversations) and its
/// parent story's AC-1: "another tenant cannot read a conversation with a guessed id (RLS
/// integration test), and another user of the same workspace gets 404/403 on it" — end to end,
/// through the real `GET/POST /api/conversations`/`GET /api/conversations/{id}` endpoints
/// (<c>Raffa.Api.ConversationsEndpointExtensions</c>), against a real Postgres+RLS Testcontainer
/// (<see cref="ConversationsIntegrationFixture"/>) — the same "one real host, no hand-rolled
/// container" shape <see cref="R0CrossTenantIsolationTests"/>/
/// <see cref="AskRaffaRagCrossTenantIsolationTests"/> already use.
///
/// <b>RLS half</b> (<see cref="Tenant_b_cannot_read_tenant_as_conversation_by_id"/>): tenant B,
/// with its own genuinely valid `X-Tenant-Id`, gets 404 for tenant A's conversation id — even
/// though nothing about the id itself reveals which tenant owns it (a guessed-id attack, not just
/// "a tenant that never created anything"). <b>Per-user half</b>
/// (<see cref="Another_user_in_the_same_tenant_gets_404_on_someone_elses_conversation"/>): a
/// second user *of the same tenant* also gets 404 — <c>ConversationService</c>'s own
/// application-level filter, since RLS has no per-user predicate (see that type's own doc
/// comment). Both read back identically as 404, never a distinguishing 403 — the endpoint's own
/// "404, not 403" rule (<c>ConversationsEndpointExtensions</c>'s own doc comment).
/// <see cref="Each_tenant_only_lists_its_own_conversations"/> proves the same isolation on the
/// list endpoint, with two tenants that coincidentally share the exact same `X-User-Id` string —
/// so the isolation demonstrably comes from tenant scoping, not from the user id happening to
/// differ.
/// </summary>
public sealed class ConversationsCrossTenantIsolationTests : IClassFixture<ConversationsIntegrationFixture>
{
    private readonly ConversationsIntegrationFixture _fixture;

    public ConversationsCrossTenantIsolationTests(ConversationsIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Tenant_b_cannot_read_tenant_as_conversation_by_id()
    {
        var client = _fixture.CreateClient();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        const string userA = "alice@tenant-a.example";

        var conversationId = await CreateConversationAsync(client, tenantA, userA);

        // AC-1: tenant B, reading with its own (different, genuinely valid) tenant claim and a
        // guessed id, gets nothing back -- not a 200 with someone else's data.
        using var getAsTenantB = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversationId}");
        getAsTenantB.Headers.Add("X-Tenant-Id", tenantB.ToString());
        getAsTenantB.Headers.Add("X-User-Id", userA);
        var getAsTenantBResponse = await client.SendAsync(getAsTenantB);
        Assert.Equal(HttpStatusCode.NotFound, getAsTenantBResponse.StatusCode);

        // Sanity check: tenant A itself can still read it back -- proves the 404 above is
        // cross-tenant isolation, not a broken conversation id.
        using var getAsTenantA = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversationId}");
        getAsTenantA.Headers.Add("X-Tenant-Id", tenantA.ToString());
        getAsTenantA.Headers.Add("X-User-Id", userA);
        var getAsTenantAResponse = await client.SendAsync(getAsTenantA);
        Assert.Equal(HttpStatusCode.OK, getAsTenantAResponse.StatusCode);
    }

    [Fact]
    public async Task Another_user_in_the_same_tenant_gets_404_on_someone_elses_conversation()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();
        const string userOne = "alice@example.com";
        const string userTwo = "bob@example.com";

        var conversationId = await CreateConversationAsync(client, tenantId, userOne);

        // AC-1: user 2, same tenant as the owner, gets 404 -- RLS has no per-user predicate, so
        // this is ConversationService's own application-level filter being proven, not RLS.
        using var getAsUserTwo = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversationId}");
        getAsUserTwo.Headers.Add("X-Tenant-Id", tenantId.ToString());
        getAsUserTwo.Headers.Add("X-User-Id", userTwo);
        var getAsUserTwoResponse = await client.SendAsync(getAsUserTwo);
        Assert.Equal(HttpStatusCode.NotFound, getAsUserTwoResponse.StatusCode);

        // Sanity check: user 1 (the owner) still reads it back fine -- proves the 404 above is
        // per-user isolation, not a broken conversation id.
        using var getAsUserOne = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversationId}");
        getAsUserOne.Headers.Add("X-Tenant-Id", tenantId.ToString());
        getAsUserOne.Headers.Add("X-User-Id", userOne);
        var getAsUserOneResponse = await client.SendAsync(getAsUserOne);
        Assert.Equal(HttpStatusCode.OK, getAsUserOneResponse.StatusCode);
    }

    [Fact]
    public async Task Each_tenant_only_lists_its_own_conversations()
    {
        var client = _fixture.CreateClient();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        // Deliberately the SAME literal X-User-Id string for both tenants: if tenant scoping were
        // broken, this is exactly the setup that would leak tenant B's conversation into tenant
        // A's list (a wrong `WHERE user_id = ...` alone, without a tenant predicate too, would
        // still "work" here).
        const string sharedUserId = "shared-username@example.com";

        var tenantAConversationId = await CreateConversationAsync(client, tenantA, sharedUserId);
        await CreateConversationAsync(client, tenantB, sharedUserId);

        using var listAsTenantA = new HttpRequestMessage(HttpMethod.Get, "/api/conversations");
        listAsTenantA.Headers.Add("X-Tenant-Id", tenantA.ToString());
        listAsTenantA.Headers.Add("X-User-Id", sharedUserId);
        var listResponse = await client.SendAsync(listAsTenantA);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var document = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var items = document.RootElement.EnumerateArray().ToList();

        var item = Assert.Single(items);
        Assert.Equal(tenantAConversationId, item.GetProperty("id").GetGuid());
    }

    private static async Task<Guid> CreateConversationAsync(HttpClient client, Guid tenantId, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        request.Headers.Add("X-User-Id", userId);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }
}
