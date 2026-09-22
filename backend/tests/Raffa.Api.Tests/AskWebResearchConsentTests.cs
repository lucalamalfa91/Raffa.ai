using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Chat.Application.WebResearch;
using Raffa.Documents.Contracts.Domain;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// ADR-030 — web research over `POST /api/conversations/{id}/messages`: three gates before any
/// offer, a per-question consent, one research call per consumed consent, a single-use
/// authorisation (409 on replay), and a decline that stays in the tenant's contracts. Every case
/// runs through the real HTTP pipeline with the fixture gateway (its research double returns two
/// public sources) and a recording audit writer.
/// </summary>
public sealed class AskWebResearchConsentTests : IClassFixture<RaffaApiFactory>
{
    private const string ExplicitQuestion = "search the web for typical uplift caps on saas renewals";
    private const string UserId = "alice@example.com";
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public AskWebResearchConsentTests(RaffaApiFactory factory)
    {
        _factory = factory.WithPresentedCallersAsMembers().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:DocumentsContracts",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
            builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");
        });
    }

    private static RecordingAiGateway NewGateway() => new(
        new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

    private WebApplicationFactory<Program> Host(RecordingAiGateway gateway, RecordingAuditWriter audit, WebResearchOptions? options)
    {
        var factory = _factory.WithInMemoryAskEngine(gateway, auditWriter: audit);
        if (options is null)
        {
            return factory;
        }

        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<WebResearchOptions>();
            services.AddSingleton(options);
        }));
    }

    private static WebResearchOptions Enabled(int dailyCalls = 20) => new()
    {
        Enabled = true,
        RequireWorkspaceOptIn = true,
        DailyCallsPerTenant = dailyCalls,
    };

    private static async Task<TenantId> SeedTenantAsync(WebApplicationFactory<Program> factory, bool workspaceOptIn)
    {
        var tenantId = TenantId.New();
        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "Completed",
            Currency = "EUR",
            AnnualSpend = 120_000m,
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
            AutoRenewal = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        db.Workspaces.Add(new WorkspaceTenant
        {
            Id = new EntityId(tenantId.Value),
            TenantId = tenantId,
            Name = "Acme",
            CreatedAt = Now,
            WebResearchEnabled = workspaceOptIn,
        });
        await db.SaveChangesAsync();

        return tenantId;
    }

    private static async Task<Guid> CreateConversationAsync(HttpClient client, TenantId tenantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", UserId);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return created.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, TenantId tenantId, Guid conversationId, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversationId}/messages")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", UserId);
        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(raw);
    }

    private static object Answer(Guid messageId, string optionKey) => new
    {
        question = optionKey,
        interviewAnswer = new { messageId = messageId.ToString(), questionKey = WebConsentInterview.QuestionKey, optionKey },
    };

    [Fact]
    public async Task With_the_kill_switch_off_an_explicit_request_is_redirected_and_nothing_is_searched()
    {
        var gateway = NewGateway();
        var audit = new RecordingAuditWriter();
        var factory = Host(gateway, audit, options: null); // Program's default: Enabled = false
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var client = factory.CreateClient();
        var conversationId = await CreateConversationAsync(client, tenantId);

        using var body = await ReadAsync(await PostAsync(client, tenantId, conversationId, new { question = ExplicitQuestion }));

        Assert.Equal("redirect", body.RootElement.GetProperty("kind").GetString());
        Assert.Contains("switched off", body.RootElement.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(RecordingAiGateway.ResearchAsync), gateway.Calls);
        var refused = Assert.Single(audit.Entries, e => e.Action == "chat.web_research_refused");
        Assert.Contains("gate=KillSwitch", refused.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_workspace_opt_in_the_redirect_names_the_admin_switch()
    {
        var gateway = NewGateway();
        var audit = new RecordingAuditWriter();
        var factory = Host(gateway, audit, Enabled());
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: false);
        var client = factory.CreateClient();
        var conversationId = await CreateConversationAsync(client, tenantId);

        using var body = await ReadAsync(await PostAsync(client, tenantId, conversationId, new { question = ExplicitQuestion }));

        Assert.Equal("redirect", body.RootElement.GetProperty("kind").GetString());
        Assert.Contains("Admin", body.RootElement.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(RecordingAiGateway.ResearchAsync), gateway.Calls);
        Assert.Contains(audit.Entries, e => e.Action == "chat.web_research_refused" && e.Detail!.Contains("gate=WorkspaceOptIn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_every_gate_open_an_explicit_request_asks_consent_and_searches_nothing_yet()
    {
        var gateway = NewGateway();
        var audit = new RecordingAuditWriter();
        var factory = Host(gateway, audit, Enabled());
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var client = factory.CreateClient();
        var conversationId = await CreateConversationAsync(client, tenantId);

        using var body = await ReadAsync(await PostAsync(client, tenantId, conversationId, new { question = ExplicitQuestion }));
        var root = body.RootElement;

        Assert.Equal("interview", root.GetProperty("kind").GetString());
        var question = Assert.Single(root.GetProperty("interview").GetProperty("questions").EnumerateArray());
        Assert.Equal(WebConsentInterview.QuestionKey, question.GetProperty("key").GetString());
        Assert.Equal("consent", question.GetProperty("presentation").GetString());
        Assert.False(question.GetProperty("allowFreeText").GetBoolean());
        var keys = question.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("key").GetString()).ToList();
        Assert.Equal(["allow", "decline"], keys);
        // The server-authored query is shown verbatim so the user knows exactly what leaves Raffa.
        Assert.Contains("typical uplift caps on saas renewals", question.GetProperty("prompt").GetString(), StringComparison.Ordinal);
        Assert.Empty(gateway.Calls);
        var interviewed = Assert.Single(audit.Entries, e => e.Action == "chat.interviewed");
        Assert.Contains("webConsent=True", interviewed.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allow_runs_one_research_call_and_returns_an_unverified_web_answer()
    {
        var gateway = NewGateway();
        var audit = new RecordingAuditWriter();
        var factory = Host(gateway, audit, Enabled());
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var client = factory.CreateClient();
        var conversationId = await CreateConversationAsync(client, tenantId);

        using var consent = await ReadAsync(await PostAsync(client, tenantId, conversationId, new { question = ExplicitQuestion }));
        var messageId = consent.RootElement.GetProperty("messageId").GetGuid();

        using var body = await ReadAsync(await PostAsync(client, tenantId, conversationId, Answer(messageId, "allow")));
        var root = body.RootElement;

        Assert.Equal("answer", root.GetProperty("kind").GetString());
        var citations = root.GetProperty("citations").EnumerateArray().ToList();
        Assert.Equal(2, citations.Count);
        Assert.All(citations, c => Assert.Equal("web", c.GetProperty("corpus").GetString()));
        Assert.All(citations, c => Assert.StartsWith("https://", c.GetProperty("href").GetString(), StringComparison.Ordinal));
        Assert.Equal(["web"], root.GetProperty("provenance").GetProperty("sources").EnumerateArray().Select(s => s.GetString()).ToList());
        Assert.True(root.GetProperty("provenance").GetProperty("unverified").GetBoolean());
        Assert.Equal("research-v1", root.GetProperty("provenance").GetProperty("promptVersion").GetString());
        Assert.NotEmpty(root.GetProperty("actions").EnumerateArray());

        Assert.Equal(1, gateway.Calls.Count(c => c == nameof(RecordingAiGateway.ResearchAsync)));
        Assert.DoesNotContain(nameof(RecordingAiGateway.AnswerAsync), gateway.Calls);
        Assert.DoesNotContain(nameof(RecordingAiGateway.EmbedAsync), gateway.Calls);

        var authorized = Assert.Single(audit.Entries, e => e.Action == "chat.web_research_authorized");
        Assert.Contains("queryHash=", authorized.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("uplift", authorized.Detail, StringComparison.OrdinalIgnoreCase);
        var researched = Assert.Single(audit.Entries, e => e.Action == "chat.web_researched");
        Assert.Contains("outcome=Answered", researched.Detail, StringComparison.Ordinal);
        Assert.Contains("sourceCount=2", researched.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", researched.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_consent_is_single_use_so_a_replay_is_409_and_searches_nothing_more()
    {
        var gateway = NewGateway();
        var factory = Host(gateway, new RecordingAuditWriter(), Enabled());
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var client = factory.CreateClient();
        var conversationId = await CreateConversationAsync(client, tenantId);

        using var consent = await ReadAsync(await PostAsync(client, tenantId, conversationId, new { question = ExplicitQuestion }));
        var messageId = consent.RootElement.GetProperty("messageId").GetGuid();

        using var first = await ReadAsync(await PostAsync(client, tenantId, conversationId, Answer(messageId, "allow")));
        var replay = await PostAsync(client, tenantId, conversationId, Answer(messageId, "allow"));

        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(1, gateway.Calls.Count(c => c == nameof(RecordingAiGateway.ResearchAsync)));
    }

    [Fact]
    public async Task Decline_answers_from_the_contracts_only_and_is_audited_as_a_decline()
    {
        var gateway = NewGateway();
        var audit = new RecordingAuditWriter();
        var factory = Host(gateway, audit, Enabled());
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var client = factory.CreateClient();
        var conversationId = await CreateConversationAsync(client, tenantId);

        // A structured question, so the declined turn stays on a path the in-memory host can run.
        using var consent = await ReadAsync(await PostAsync(client, tenantId, conversationId,
            new { question = "search the web: which contracts renew in the next 120 days?" }));
        Assert.Equal("interview", consent.RootElement.GetProperty("kind").GetString());
        var messageId = consent.RootElement.GetProperty("messageId").GetGuid();

        using var body = await ReadAsync(await PostAsync(client, tenantId, conversationId, Answer(messageId, "decline")));

        Assert.NotEqual("interview", body.RootElement.GetProperty("kind").GetString());
        Assert.DoesNotContain(nameof(RecordingAiGateway.ResearchAsync), gateway.Calls);
        Assert.All(
            body.RootElement.GetProperty("citations").EnumerateArray(),
            c => Assert.NotEqual("web", c.GetProperty("corpus").GetString()));
        Assert.Single(audit.Entries, e => e.Action == "chat.web_research_declined");
        Assert.DoesNotContain(audit.Entries, e => e.Action == "chat.web_research_authorized");
    }

    [Fact]
    public async Task The_daily_budget_closes_the_offer_after_the_last_call()
    {
        var gateway = NewGateway();
        var audit = new RecordingAuditWriter();
        var factory = Host(gateway, audit, Enabled(dailyCalls: 1));
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var client = factory.CreateClient();
        var conversationId = await CreateConversationAsync(client, tenantId);

        using var consent = await ReadAsync(await PostAsync(client, tenantId, conversationId, new { question = ExplicitQuestion }));
        var messageId = consent.RootElement.GetProperty("messageId").GetGuid();
        using var first = await ReadAsync(await PostAsync(client, tenantId, conversationId, Answer(messageId, "allow")));
        Assert.Equal("answer", first.RootElement.GetProperty("kind").GetString());

        using var second = await ReadAsync(await PostAsync(client, tenantId, conversationId, new { question = ExplicitQuestion }));

        Assert.Equal("redirect", second.RootElement.GetProperty("kind").GetString());
        Assert.Contains("allowance", second.RootElement.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, gateway.Calls.Count(c => c == nameof(RecordingAiGateway.ResearchAsync)));
        Assert.Contains(audit.Entries, e => e.Action == "chat.web_research_refused" && e.Detail!.Contains("gate=Budget", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_interpretation_menu_offers_the_web_only_when_the_gates_are_open_and_the_offer_asks_consent()
    {
        var gateway = NewGateway();
        var factory = Host(gateway, new RecordingAuditWriter(), Enabled());
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var client = factory.CreateClient();
        var conversationId = await CreateConversationAsync(client, tenantId);

        using var menu = await ReadAsync(await PostAsync(client, tenantId, conversationId, new { question = "Did you over all my contract?" }));
        Assert.Equal("interview", menu.RootElement.GetProperty("kind").GetString());
        var menuQuestion = menu.RootElement.GetProperty("interview").GetProperty("questions")[0];
        Assert.Equal("choice", menuQuestion.GetProperty("presentation").GetString());
        var webOption = menuQuestion.GetProperty("options").EnumerateArray().Last();
        Assert.Equal("web-research", webOption.GetProperty("key").GetString());
        var menuMessageId = menu.RootElement.GetProperty("messageId").GetGuid();

        using var consent = await ReadAsync(await PostAsync(client, tenantId, conversationId, new
        {
            question = webOption.GetProperty("label").GetString(),
            interviewAnswer = new { messageId = menuMessageId.ToString(), questionKey = "interpretation", optionKey = "web-research" },
        }));

        // The menu option is not a consent: it leads to the consent question, never to a search.
        Assert.Equal("interview", consent.RootElement.GetProperty("kind").GetString());
        Assert.Equal("consent", consent.RootElement.GetProperty("interview").GetProperty("questions")[0].GetProperty("presentation").GetString());
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task The_interpretation_menu_has_no_web_option_when_the_kill_switch_is_off()
    {
        var factory = Host(NewGateway(), new RecordingAuditWriter(), options: null);
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var client = factory.CreateClient();
        var conversationId = await CreateConversationAsync(client, tenantId);

        using var menu = await ReadAsync(await PostAsync(client, tenantId, conversationId, new { question = "Did you over all my contract?" }));

        Assert.Equal("interview", menu.RootElement.GetProperty("kind").GetString());
        var keys = menu.RootElement.GetProperty("interview").GetProperty("questions")[0]
            .GetProperty("options").EnumerateArray().Select(o => o.GetProperty("key").GetString()).ToList();
        Assert.DoesNotContain("web-research", keys);
    }
}
