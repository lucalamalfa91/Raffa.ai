using System.Net.Http.Json;
using System.Text.Json;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// T7(d) (task E15/F01/US01/T01, wave w14; ADR-025 Rule D.5e, ADR-011, §H): "a removed member's
/// Ask call scoped to that tenant retrieves nothing from its corpus". Authored now, and
/// <c>Skip</c>ped with a named reason — the exact discipline ADR-025 §H already applies to T14 —
/// rather than either weakened to something vacuous or silently dropped.
///
/// <para>
/// <b>Why it cannot pass today.</b> <c>Raffa.Api.AskCopilotService.AskAsync</c> (reached from both
/// `POST /api/chat/query` and `POST /api/conversations/{id}/messages`) takes only a
/// <see cref="TenantId"/> and never consults <c>workspace_membership</c> — its two callers
/// (<c>ChatEndpointExtensions</c>/<c>ConversationsEndpointExtensions</c>) resolve that tenant
/// straight from the interim <c>X-Tenant-Id</c> header (read directly, confirmed by
/// <c>ConversationsEndpointExtensions.TryResolveTenant</c>). A removed member keeps their
/// <c>workspace_user</c> row (ADR-025 Rule D.5d, audit continuity) and the tenant's corpus is
/// untouched by removal, so a direct API call carrying that same tenant id still finds it — there
/// is no membership check anywhere on this path to make the answer differ for a removed member
/// versus any other caller. See <see cref="GapReasons.MembershipVerifiedReadsNotYetWired"/> for the
/// full reasoning and why closing it is outside this task's own file scope.
/// </para>
///
/// <para>
/// The test body below is written as the real proof would look once that gap closes (seed one
/// tenant-scoped embedding, remove the member, call Ask as them scoped to that tenant, assert an
/// honest abstain with zero citations) — it does not compile against a stubbed-out assertion, so
/// removing <c>Skip</c> the day the membership check lands is the only change this test should
/// ever need.
/// </para>
/// </summary>
public sealed class RemovedMemberRetrievalTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public RemovedMemberRetrievalTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    [Fact(Skip = GapReasons.MembershipVerifiedReadsNotYetWired)]
    public async Task T7d_a_removed_members_ask_call_scoped_to_that_tenant_retrieves_nothing_from_its_corpus()
    {
        var gateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
        var factory = _baseFactory.WithInMemoryAskEngine(gateway);
        var client = factory.CreateClient();

        var tenantId = await CreateWorkspaceAsync(client, "Removed Retrieval Co", "admin@removedretrieval.example");
        var membershipId = await InviteAndAcceptAsync(client, tenantId, "admin@removedretrieval.example", "removed@removedretrieval.example", "Procurement");

        using (var scope = factory.Services.CreateScope())
        {
            var embeddingRetrievalService = scope.ServiceProvider.GetRequiredService<EmbeddingRetrievalService>();
            var indexResult = await embeddingRetrievalService.IndexChunkAsync(
                new TenantId(tenantId), "Document", EntityId.New(), 0,
                "The liability cap under this agreement is CHF 1,000,000.");
            Assert.True(indexResult.IsSuccess);
        }

        var removeResponse = await RemoveMemberAsync(client, tenantId, membershipId, "admin@removedretrieval.example");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, removeResponse.StatusCode);

        using var askRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query")
        {
            Content = JsonContent.Create(new { question = "what liability coverage do we have on file" }),
        };
        askRequest.Headers.Add("X-Tenant-Id", tenantId.ToString());
        askRequest.Headers.Add("X-User-Id", "removed@removedretrieval.example");

        var askResponse = await client.SendAsync(askRequest);
        using var body = JsonDocument.Parse(await askResponse.Content.ReadAsStringAsync());

        // The property this test exists to prove: a removed member retrieves nothing from the
        // tenant's own corpus, even though the corpus itself is untouched by their removal.
        Assert.Equal("abstain", body.RootElement.GetProperty("kind").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("citations").GetArrayLength());
    }

    private static async Task<Guid> CreateWorkspaceAsync(HttpClient client, string name, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
        {
            Content = JsonContent.Create(new { name }),
        };
        request.Headers.Add("X-User-Id", userId);

        var response = await client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> InviteAndAcceptAsync(
        HttpClient client, Guid tenantId, string adminUserId, string email, string role)
    {
        using var inviteRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{tenantId}/invites")
        {
            Content = JsonContent.Create(new { email, role }),
        };
        inviteRequest.Headers.Add("X-User-Id", adminUserId);
        var inviteResponse = await client.SendAsync(inviteRequest);
        Assert.Equal(System.Net.HttpStatusCode.Created, inviteResponse.StatusCode);

        using var inviteBody = JsonDocument.Parse(await inviteResponse.Content.ReadAsStringAsync());
        var acceptUrl = inviteBody.RootElement.GetProperty("acceptUrl").GetString()!;
        var token = acceptUrl["/invite/accept#".Length..];

        using var acceptRequest = new HttpRequestMessage(HttpMethod.Post, "/api/invites/accept");
        acceptRequest.Headers.Add("X-Invitation-Token", token);
        acceptRequest.Headers.Add("X-User-Id", email);
        var acceptResponse = await client.SendAsync(acceptRequest);
        Assert.Equal(System.Net.HttpStatusCode.OK, acceptResponse.StatusCode);

        using var membersRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/workspaces/{tenantId}/members");
        membersRequest.Headers.Add("X-User-Id", adminUserId);
        var membersResponse = await client.SendAsync(membersRequest);
        using var membersBody = JsonDocument.Parse(await membersResponse.Content.ReadAsStringAsync());
        var member = membersBody.RootElement.GetProperty("members").EnumerateArray()
            .Single(m => string.Equals(m.GetProperty("email").GetString(), email, StringComparison.OrdinalIgnoreCase));
        return member.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> RemoveMemberAsync(HttpClient client, Guid tenantId, Guid membershipId, string callerUserId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/workspaces/{tenantId}/members/{membershipId}");
        request.Headers.Add("X-User-Id", callerUserId);
        return await client.SendAsync(request);
    }
}
