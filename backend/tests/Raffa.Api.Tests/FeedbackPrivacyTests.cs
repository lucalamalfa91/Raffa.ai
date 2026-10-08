using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Raffa.Api.Infrastructure;
using Raffa.Api.Tests.TestSupport;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Chat.Application.Feedback;
using Raffa.Chat.Application.Gaps;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Raffa.Api.Tests;

/// <summary>
/// F4-T01 and F4-D02: the text that leaves the tenant for the public GitHub repository carries no
/// supplier, person or identifier (end to end, through the real publisher), a second submission of
/// the same offer is a 409, and an exposed environment never opens the channel.
/// </summary>
public sealed class FeedbackPrivacyTests(RaffaApiFactory factory) : IClassFixture<RaffaApiFactory>
{
    private const string UserId = "alice.bianchi@example.com";

    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly EntityId AmazonId = EntityId.New();

    private static FeedbackHostOptions GitHubOptions(bool exposed = false, string? token = "test-token") => new()
    {
        Environment = "test",
        ExposedEnvironment = exposed,
        GitHub = new FeedbackHostOptions.GitHubOptions { Enabled = true, Token = token, Repository = "owner/repo" },
    };

    private WebApplicationFactory<Program> Host(IFeatureRequestPublisher? publisher)
    {
        var gateway = new RecordingAiGateway(new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        return factory
            .WithPresentedCallersAsMembers()
            .WithWebHostBuilder(builder => builder.UseSetting(
                "ConnectionStrings:Chat",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true"))
            .WithInMemoryAskEngine(gateway, new FixedClock(Now), auditWriter: new RecordingAuditWriter())
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISupplierNameLookup>(
                    new StubSupplierNameLookup(new Dictionary<EntityId, string> { [AmazonId] = "Amazon Web Services" }));
                services.AddSingleton(new FeedbackOptions { Environment = "test" });
                if (publisher is not null)
                {
                    services.AddScoped(_ => publisher);
                }
            }));
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, TenantId tenantId, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", UserId);
        return request;
    }

    private static async Task<(Guid ConversationId, Guid MessageId)> OpenGapTurnAsync(HttpClient client, TenantId tenantId)
    {
        using var createRequest = Request(HttpMethod.Post, "/api/conversations", tenantId, new { });
        using var created = JsonDocument.Parse(await (await client.SendAsync(createRequest)).Content.ReadAsStringAsync());
        var conversationId = created.RootElement.GetProperty("id").GetGuid();

        using var messageRequest = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/messages", tenantId, new { question = "Export my contracts to Excel" });
        using var reply = JsonDocument.Parse(await (await client.SendAsync(messageRequest)).Content.ReadAsStringAsync());
        return (conversationId, reply.RootElement.GetProperty("messageId").GetGuid());
    }

    [Fact]
    public async Task The_issue_the_real_publisher_sends_to_GitHub_carries_no_name_identifier_or_amount()
    {
        var handler = new CapturingHandler();
        var publisher = new GitHubIssueFeatureRequestPublisher(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test/") },
            GitHubOptions(),
            NullLogger<GitHubIssueFeatureRequestPublisher>.Instance);
        var host = Host(publisher);

        // A tenant with a contract for Amazon Web Services: its name must be known to the scrub.
        var tenantId = TenantId.New();
        var contract = new Contract
        {
            TenantId = tenantId,
            SupplierId = AmazonId,
            Type = ContractDocumentType.OrderForm,
            Status = "Completed",
            Currency = "EUR",
            AnnualSpend = 612000m,
            CreatedAt = Now,
        };
        await host.SeedContractAsync(contract);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = host.CreateClient();
        var (conversationId, messageId) = await OpenGapTurnAsync(client, tenantId);

        const string typed =
            "Esporta i contratti di amazon web services (CT-2024-0042) e mandali a Mario Rossi, " +
            "mario.rossi@acme-corp.com, tel +39 02 1234567, per 20.000 euro, vedi https://portal.acme-corp.com/x";
        using var submit = Request(
            HttpMethod.Post, $"/api/conversations/{conversationId}/feedback", tenantId,
            new { messageId, answers = new { what = typed, frequency = "sometimes", importance = "very-useful" } });
        var response = await client.SendAsync(submit);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sent = Assert.Single(handler.Bodies);
        using var json = JsonDocument.Parse(sent);
        var published = json.RootElement.GetProperty("title").GetString() + "\n" + json.RootElement.GetProperty("body").GetString();

        foreach (var forbidden in new[]
                 {
                     "amazon", "Amazon", "CT-2024", "0042", "Mario", "Rossi", "mario.rossi", "acme-corp", "1234567", "20.000", "euro",
                     "https://portal", UserId, "alice", "bianchi",
                 })
        {
            Assert.DoesNotContain(forbidden, published, StringComparison.OrdinalIgnoreCase);
        }

        // The request itself survives, readable.
        Assert.Contains("> Esporta i contratti di [name]", published, StringComparison.Ordinal);
        Assert.Contains("## Human approval", published, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_submission_of_the_same_offer_is_a_409_and_publishes_nothing_more()
    {
        var handler = new CapturingHandler();
        var publisher = new GitHubIssueFeatureRequestPublisher(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test/") },
            GitHubOptions(),
            NullLogger<GitHubIssueFeatureRequestPublisher>.Instance);
        var host = Host(publisher);
        var client = host.CreateClient();
        var tenantId = TenantId.New();
        var (conversationId, messageId) = await OpenGapTurnAsync(client, tenantId);

        var body = new { messageId, answers = new { what = "Export the portfolio", frequency = "sometimes", importance = "very-useful" } };
        using var first = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/feedback", tenantId, body);
        using var second = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/feedback", tenantId, body);

        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(second)).StatusCode);
        Assert.Single(handler.Bodies);
    }

    // ----- F4-D02: exposed environments -----

    [Fact]
    public async Task An_exposed_environment_never_opens_the_GitHub_channel_even_with_a_token()
    {
        var handler = new CapturingHandler();
        var exposed = new GitHubIssueFeatureRequestPublisher(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test/") },
            GitHubOptions(exposed: true),
            NullLogger<GitHubIssueFeatureRequestPublisher>.Instance);

        Assert.False(exposed.IsConfigured);
        var result = await exposed.TryPublishAsync(new FeatureRequestIssue(
            "export-file", "Export", "en", "test", "0123abcd",
            new FeedbackAnswers("x", FeedbackQuestions.FrequencySometimes, FeedbackQuestions.ImportanceNiceToHave)));
        Assert.False(result.Published);
        Assert.Empty(handler.Bodies);

        // The same options, not exposed: configured.
        Assert.True(new GitHubIssueFeatureRequestPublisher(
            new HttpClient(handler), GitHubOptions(), NullLogger<GitHubIssueFeatureRequestPublisher>.Instance).IsConfigured);
    }

    [Fact]
    public void An_exposed_environment_needs_no_token_and_the_channel_is_off()
    {
        var exposed = GitHubOptions(exposed: true, token: null);
        exposed.ValidateOrThrow();
        Assert.False(exposed.GitHubPublishingActive);

        // Not exposed: enabled without a token still refuses to start (unchanged).
        Assert.Throws<InvalidOperationException>(() => GitHubOptions(exposed: false, token: null).ValidateOrThrow());
        Assert.True(GitHubOptions().GitHubPublishingActive);
    }

    [Theory]
    [InlineData(false, "GitHubIssueFeatureRequestPublisher")]
    [InlineData(true, "NullFeatureRequestPublisher")]
    public void The_host_registers_the_GitHub_publisher_only_when_the_environment_is_not_exposed(bool exposed, string expectedPublisher)
    {
        var host = factory
            .WithPresentedCallersAsMembers()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Chat", "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
                builder.UseSetting("Feedback:GitHub:Enabled", "true");
                builder.UseSetting("Feedback:GitHub:Token", "test-token");
                builder.UseSetting("Feedback:GitHub:Repository", "owner/repo");
                builder.UseSetting("Feedback:ExposedEnvironment", exposed ? "true" : "false");
            });

        using var scope = host.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFeatureRequestPublisher>();

        Assert.Equal(expectedPublisher, publisher.GetType().Name);
        Assert.Equal(!exposed, publisher.IsConfigured);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly List<string> _bodies = [];

        public IReadOnlyList<string> Bodies
        {
            get
            {
                lock (_bodies)
                {
                    return [.. _bodies];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_bodies)
            {
                _bodies.Add(body);
            }

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"number":7,"html_url":"https://github.com/owner/repo/issues/7"}""", Encoding.UTF8, "application/json"),
            };
        }
    }
}
