using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Fixtures;
using Contigo.Api.Tests.TestSupport;
using Contigo.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Contigo.Api.Tests;

/// <summary>
/// Host-level proof for task E13/F05/US01/T02 (story us-01-conversations, AC-2/AC-3) that
/// `GET/POST /api/conversations` and `GET /api/conversations/{id}` are actually mapped in
/// `Program.cs` (via <see cref="ConversationsEndpointExtensions"/>) and enforce their
/// request-shape guard clauses — mirrors <see cref="ChatEndpointTests"/>'s own "not just a
/// placeholder" purpose. Most cases here only exercise branches that return before any database
/// call is made (the tenant-header check, the caller-identity check, and each endpoint's own
/// route/body validation), so — like every sibling class in this project — those do not need a
/// running Postgres. "Last N conversations"/201/conversation+messages persistence detail is proven
/// by <c>Contigo.Chat.Tests.Conversations.ConversationServiceTests</c> (service level, task
/// E13/F05/US01/T01) instead; cross-tenant/cross-user isolation over real HTTP against a real
/// Postgres+RLS Testcontainer is proven by
/// <c>Contigo.IntegrationTests.ConversationsCrossTenantIsolationTests</c> (this task).
///
/// <para>
/// <b>Review-pass addition</b>: task E13/F06/US01/T01's own Definition of Done names this
/// endpoint specifically — "`POST /api/conversations/{id}/messages` returns the §6 contract for
/// answer / abstain / redirect / refusal" — see
/// <see cref="Post_message_runs_the_ask_engine_and_returns_the_reply_contract"/>, proven via
/// <see cref="InMemoryAskEngineFactory.WithInMemoryAskEngine"/> the same way
/// <see cref="ChatEndpointTests"/>' own review-pass additions prove its alias.
/// </para>
/// </summary>
public sealed class ConversationsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ConversationsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:Chat",
                "Host=localhost;Port=5432;Database=contigo_dev;Username=contigo;Password=contigo;Include Error Detail=true");
        });
    }

    [Fact]
    public async Task List_missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/conversations");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/conversations");
        request.Headers.Add("X-Tenant-Id", "not-a-guid");
        request.Headers.Add("X-User-Id", "alice@example.com");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_missing_user_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/conversations");
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_blank_user_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/conversations");
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-User-Id", "   ");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_non_positive_take_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/conversations?take=0");
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-User-Id", "alice@example.com");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/conversations", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_missing_user_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_non_guid_scope_contract_id_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
        {
            Content = JsonContent.Create(new { scopeContractId = "not-a-guid" }),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-User-Id", "alice@example.com");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/conversations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_missing_user_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{Guid.NewGuid()}");
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_invalid_conversation_id_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/conversations/not-a-guid");
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-User-Id", "alice@example.com");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Task E13/F06/US01/T01 (ask-engine, AC-8): `POST /api/conversations/{id}/messages` guard
    /// clauses — same "runs before any database call" scope as every other test in this class
    /// (tenant header, user header, route-id GUID, blank question, all before
    /// <c>ConversationService.GetAsync</c> or <c>AskCopilotService.AskAsync</c> ever run). The
    /// success path (a real reply against a real conversation) needs a real Postgres for
    /// Chat/Documents/Suppliers and is proven instead by
    /// <c>Contigo.IntegrationTests.AskContigoRagCrossTenantIsolationTests</c>.
    /// </summary>
    [Fact]
    public async Task Post_message_missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{Guid.NewGuid()}/messages", new { question = "When does Salesforce expire?" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_message_missing_user_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{Guid.NewGuid()}/messages")
        {
            Content = JsonContent.Create(new { question = "When does Salesforce expire?" }),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_message_invalid_conversation_id_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/conversations/not-a-guid/messages")
        {
            Content = JsonContent.Create(new { question = "When does Salesforce expire?" }),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-User-Id", "alice@example.com");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_message_missing_or_blank_question_returns_400(string? question)
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{Guid.NewGuid()}/messages")
        {
            Content = JsonContent.Create(new { question }),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-User-Id", "alice@example.com");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// This task's own Definition of Done line, verbatim: "`POST /api/conversations/{id}/messages`
    /// returns the §6 contract for answer / abstain / redirect / refusal". Only the redirect shape
    /// is exercised directly here (a "ciao" greeting needs no seeded portfolio data, unlike an
    /// answer, which <see cref="ChatEndpointTests.Renewal_window_question_is_answered_with_per_contract_citations_and_no_engineer_chrome"/>
    /// already proves through the `/api/chat/query` alias that shares this exact same
    /// implementation, <c>ConversationsEndpointExtensions.AskAndAppendAsync</c>) — this test's own
    /// job is proving the contract's full field list comes back through *this* route specifically,
    /// against a real, freshly created conversation.
    /// </summary>
    [Fact]
    public async Task Post_message_runs_the_ask_engine_and_returns_the_reply_contract()
    {
        var recordingGateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var factory = _factory.WithInMemoryAskEngine(recordingGateway);
        var client = factory.CreateClient();

        var tenantId = TenantId.New();
        const string userId = "alice@example.com";

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
        {
            Content = JsonContent.Create(new { }),
        };
        createRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        createRequest.Headers.Add("X-User-Id", userId);

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var conversationId = created.RootElement.GetProperty("id").GetGuid();

        using var messageRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/conversations/{conversationId}/messages")
        {
            Content = JsonContent.Create(new { question = "ciao" }),
        };
        messageRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        messageRequest.Headers.Add("X-User-Id", userId);

        var response = await client.SendAsync(messageRequest);
        var rawBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(rawBody);
        var root = body.RootElement;

        // ADR-024 §6's own reply contract, verbatim field list — `{ kind, answerMarkdown,
        // citations[], actions[], provenance { sources, modelId, promptVersion, inputHash },
        // followUps[] }` — plus the two identifiers this endpoint adds on top (conversationId,
        // messageId).
        Assert.Equal("redirect", root.GetProperty("kind").GetString());
        Assert.True(root.TryGetProperty("answerMarkdown", out _));
        Assert.True(root.TryGetProperty("citations", out _));
        Assert.True(root.TryGetProperty("actions", out _));
        Assert.True(root.TryGetProperty("provenance", out _));
        Assert.True(root.TryGetProperty("followUps", out _));
        Assert.Equal(conversationId, root.GetProperty("conversationId").GetGuid());
        Assert.NotEqual(Guid.Empty, root.GetProperty("messageId").GetGuid());

        Assert.DoesNotContain("Structured query", rawBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Document:", rawBody, StringComparison.Ordinal);
    }
}
