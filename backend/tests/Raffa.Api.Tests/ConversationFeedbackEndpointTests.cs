using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Chat.Application.Feedback;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>ADR-030 D5: <c>POST /api/conversations/{id}/feedback</c> stores first, publishes
/// best-effort, appends the confirmation turn, and never leaks the answers into the audit trail.</summary>
public sealed class ConversationFeedbackEndpointTests(RaffaApiFactory factory) : IClassFixture<RaffaApiFactory>
{
    private const string UserId = "alice@example.com";

    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private (WebApplicationFactory<Program> Factory, StubPublisher Publisher, RecordingAuditWriter Audit) Host(bool configured, bool fail = false, bool throws = false)
    {
        var publisher = new StubPublisher(configured, fail, throws);
        var audit = new RecordingAuditWriter();
        var gateway = new RecordingAiGateway(new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var host = factory
            .WithPresentedCallersAsMembers()
            .WithWebHostBuilder(builder => builder.UseSetting(
                "ConnectionStrings:Chat",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true"))
            .WithInMemoryAskEngine(gateway, new FixedClock(Now), auditWriter: audit)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISupplierNameLookup>(new StubSupplierNameLookup(new Dictionary<EntityId, string>()));
                services.AddScoped<IFeatureRequestPublisher>(_ => publisher);
                services.AddSingleton(new FeedbackOptions { Environment = "test" });
            }));

        return (host, publisher, audit);
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, TenantId tenantId, object? body = null, string userId = UserId)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", userId);
        return request;
    }

    /// <summary>A gap turn to answer: the export request needs no contract at all.</summary>
    private static async Task<(Guid ConversationId, Guid MessageId)> OpenGapTurnAsync(HttpClient client, TenantId tenantId)
    {
        using var createRequest = Request(HttpMethod.Post, "/api/conversations", tenantId, new { });
        var createResponse = await client.SendAsync(createRequest);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var conversationId = created.RootElement.GetProperty("id").GetGuid();

        using var messageRequest = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/messages", tenantId, new { question = "Export my contracts to Excel" });
        var response = await client.SendAsync(messageRequest);
        using var reply = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("redirect", reply.RootElement.GetProperty("kind").GetString());
        return (conversationId, reply.RootElement.GetProperty("messageId").GetGuid());
    }

    private static object Answers(string what = "Export the portfolio to Excel every quarter") =>
        new { what, frequency = "sometimes", importance = "very-useful" };

    [Fact]
    public async Task Submits_and_returns_201_with_the_confirmation_turn_and_external_action()
    {
        var tenantId = TenantId.New();
        var (host, publisher, audit) = Host(configured: true);
        var client = host.CreateClient();
        var (conversationId, messageId) = await OpenGapTurnAsync(client, tenantId);

        using var request = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/feedback", tenantId, new { messageId, answers = Answers() });
        var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{response.StatusCode}: {raw}");
        using var body = JsonDocument.Parse(raw);
        var root = body.RootElement;
        Assert.Equal("issue_opened", root.GetProperty("status").GetString());
        Assert.Equal(42, root.GetProperty("issueNumber").GetInt32());
        Assert.Equal("https://github.com/lucalamalfa91/Raffa.ai/issues/42", root.GetProperty("issueUrl").GetString());

        var message = root.GetProperty("message");
        Assert.Equal("raffa", message.GetProperty("role").GetString());
        Assert.Equal("answer", message.GetProperty("kind").GetString());
        Assert.StartsWith("Thanks, I opened issue #42", message.GetProperty("markdown").GetString(), StringComparison.Ordinal);
        var action = Assert.Single(message.GetProperty("actions").EnumerateArray());
        Assert.Equal("external", action.GetProperty("kind").GetString());
        Assert.Equal("Open issue #42 →", action.GetProperty("label").GetString());
        Assert.Equal(messageId.ToString(), message.GetProperty("payload").GetProperty("feedbackResult").GetProperty("forMessageId").GetString());

        var issue = Assert.Single(publisher.Published);
        Assert.Equal("export-file", issue.GapKey);
        Assert.Equal("test", issue.Environment);
        Assert.Equal(8, issue.WorkspaceHash.Length);
        Assert.Equal("Export the portfolio to Excel every quarter", issue.Answers.What);

        var auditRow = Assert.Single(audit.Entries, e => e.Action == FeedbackService.AuditSubmittedAction);
        Assert.Contains("gapKey=export-file", auditRow.Detail, StringComparison.Ordinal);
        Assert.Contains("issueNumber=42", auditRow.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("every quarter", auditRow.Detail, StringComparison.Ordinal);

        // Resume: the confirmation turn is part of the thread.
        using var getRequest = Request(HttpMethod.Get, $"/api/conversations/{conversationId}", tenantId);
        using var detail = JsonDocument.Parse(await (await client.SendAsync(getRequest)).Content.ReadAsStringAsync());
        Assert.Equal(3, detail.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task Without_a_publisher_status_is_recorded_and_no_action()
    {
        var tenantId = TenantId.New();
        var (host, _, _) = Host(configured: false);
        var client = host.CreateClient();
        var (conversationId, messageId) = await OpenGapTurnAsync(client, tenantId);

        using var request = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/feedback", tenantId, new { messageId, answers = Answers() });
        var response = await client.SendAsync(request);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("recorded", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("issueNumber").ValueKind);
        Assert.Empty(body.RootElement.GetProperty("message").GetProperty("actions").EnumerateArray());
        Assert.StartsWith("Thanks, your report", body.RootElement.GetProperty("message").GetProperty("markdown").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publisher_failure_or_exception_still_returns_201_recorded(bool throws)
    {
        var tenantId = TenantId.New();
        var (host, _, _) = Host(configured: true, fail: !throws, throws: throws);
        var client = host.CreateClient();
        var (conversationId, messageId) = await OpenGapTurnAsync(client, tenantId);

        using var request = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/feedback", tenantId, new { messageId, answers = Answers() });
        var response = await client.SendAsync(request);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("recorded", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Second_submit_returns_409_with_the_existing_request()
    {
        var tenantId = TenantId.New();
        var (host, publisher, _) = Host(configured: true);
        var client = host.CreateClient();
        var (conversationId, messageId) = await OpenGapTurnAsync(client, tenantId);

        using var first = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/feedback", tenantId, new { messageId, answers = Answers() });
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(first)).StatusCode);

        using var second = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/feedback", tenantId, new { messageId, answers = Answers("again") });
        var response = await client.SendAsync(second);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("issue_opened", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("message").ValueKind);
        Assert.Single(publisher.Published);
    }

    [Fact]
    public async Task Message_without_a_gap_payload_is_400()
    {
        var tenantId = TenantId.New();
        var (host, _, _) = Host(configured: true);
        var client = host.CreateClient();

        using var createRequest = Request(HttpMethod.Post, "/api/conversations", tenantId, new { });
        using var created = JsonDocument.Parse(await (await client.SendAsync(createRequest)).Content.ReadAsStringAsync());
        var conversationId = created.RootElement.GetProperty("id").GetGuid();
        using var messageRequest = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/messages", tenantId, new { question = "ciao" });
        using var reply = JsonDocument.Parse(await (await client.SendAsync(messageRequest)).Content.ReadAsStringAsync());
        var greetingId = reply.RootElement.GetProperty("messageId").GetGuid();

        using var request = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/feedback", tenantId, new { messageId = greetingId, answers = Answers() });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bad_choice_key_and_missing_message_id_are_400()
    {
        var tenantId = TenantId.New();
        var (host, _, _) = Host(configured: true);
        var client = host.CreateClient();
        var (conversationId, messageId) = await OpenGapTurnAsync(client, tenantId);

        using var badChoice = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/feedback", tenantId,
            new { messageId, answers = new { what = "x", frequency = "daily", importance = "blocking" } });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(badChoice)).StatusCode);

        using var noMessage = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/feedback", tenantId, new { answers = Answers() });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(noMessage)).StatusCode);
    }

    [Fact]
    public async Task Other_users_conversation_is_404()
    {
        var tenantId = TenantId.New();
        var (host, _, _) = Host(configured: true);
        var client = host.CreateClient();
        var (conversationId, messageId) = await OpenGapTurnAsync(client, tenantId);

        using var request = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/feedback", tenantId, new { messageId, answers = Answers() }, userId: "bob@example.com");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class StubPublisher(bool configured, bool fail, bool throws) : IFeatureRequestPublisher
    {
        public List<FeatureRequestIssue> Published { get; } = [];

        public bool IsConfigured => configured;

        public Task<FeatureRequestPublishResult> TryPublishAsync(FeatureRequestIssue issue, CancellationToken cancellationToken = default)
        {
            if (throws)
            {
                throw new HttpRequestException("simulated outage");
            }

            if (fail)
            {
                return Task.FromResult(FeatureRequestPublishResult.Failed("GitHub 503: unavailable"));
            }

            Published.Add(issue);
            return Task.FromResult(FeatureRequestPublishResult.Opened(42, "https://github.com/lucalamalfa91/Raffa.ai/issues/42"));
        }
    }
}
