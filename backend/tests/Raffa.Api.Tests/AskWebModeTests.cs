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
/// ADR-031 — the composer's web-search toggle over `POST /api/conversations/{id}/messages`
/// (<c>webResearch: true</c>): no per-question consent, the procurement-only filters lifted, one
/// open-mode research call beside the contracts-only pipeline, a plainly personal question pointed
/// at Google and Perplexity with zero model calls, and the kill switch, workspace opt-in and budget
/// still in force. Every case runs through the real HTTP pipeline with the fixture gateway.
/// </summary>
public sealed class AskWebModeTests : IClassFixture<RaffaApiFactory>
{
    private const string UserId = "alice@example.com";
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public AskWebModeTests(RaffaApiFactory factory)
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

    private static async Task<(HttpClient Client, Guid ConversationId)> OpenAsync(WebApplicationFactory<Program> factory, TenantId tenantId)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", UserId);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (client, created.RootElement.GetProperty("id").GetGuid());
    }

    private static async Task<JsonDocument> AskAsync(HttpClient client, TenantId tenantId, Guid conversationId, string question, bool webResearch = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversationId}/messages")
        {
            Content = JsonContent.Create(new { question, webResearch }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", UserId);

        var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, raw);
        return JsonDocument.Parse(raw);
    }

    private static int ResearchCalls(RecordingAiGateway gateway) =>
        gateway.Calls.Count(c => c == nameof(RecordingAiGateway.ResearchAsync));

    [Fact]
    public async Task A_personal_question_points_at_google_and_perplexity_and_calls_no_model()
    {
        var gateway = NewGateway();
        var audit = new RecordingAuditWriter();
        var factory = Host(gateway, audit, Enabled());
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var (client, conversationId) = await OpenAsync(factory, tenantId);

        using var body = await AskAsync(client, tenantId, conversationId, "dimmi la ricetta della carbonara");
        var root = body.RootElement;

        Assert.Equal("redirect", root.GetProperty("kind").GetString());
        Assert.Contains("Google", root.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        Assert.Contains("fuori dal perimetro", root.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        var actions = root.GetProperty("actions").EnumerateArray().ToList();
        Assert.Equal(2, actions.Count);
        Assert.All(actions, a => Assert.Equal("external", a.GetProperty("kind").GetString()));
        Assert.Equal("https://www.google.com/search?q=dimmi+la+ricetta+della+carbonara", actions[0].GetProperty("href").GetString());
        Assert.StartsWith("https://www.perplexity.ai/search?q=", actions[1].GetProperty("href").GetString(), StringComparison.Ordinal);
        Assert.Empty(gateway.Calls);

        var refused = Assert.Single(audit.Entries, e => e.Action == "chat.web_research_refused");
        Assert.Contains("gate=OffContext", refused.Detail, StringComparison.Ordinal);
        Assert.Contains("mode=toggle", refused.Detail, StringComparison.Ordinal);
        Assert.Contains(audit.Entries, e => e.Action == "chat.redirected" && e.Detail!.Contains("webMode=True", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_explicit_web_request_searches_at_once_with_no_consent_dialog()
    {
        var gateway = NewGateway();
        var audit = new RecordingAuditWriter();
        var factory = Host(gateway, audit, Enabled());
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var (client, conversationId) = await OpenAsync(factory, tenantId);

        using var body = await AskAsync(client, tenantId, conversationId, "search the web for typical uplift caps on saas renewals");
        var root = body.RootElement;

        Assert.NotEqual("interview", root.GetProperty("kind").GetString());
        Assert.Equal("answer", root.GetProperty("kind").GetString());
        Assert.Equal(1, ResearchCalls(gateway));
        Assert.True(root.GetProperty("provenance").GetProperty("unverified").GetBoolean());
        Assert.Contains(root.GetProperty("citations").EnumerateArray(), c => c.GetProperty("corpus").GetString() == "web");

        var authorized = Assert.Single(audit.Entries, e => e.Action == "chat.web_research_authorized");
        Assert.Contains($"purpose={WebModeLexicon.Purpose}", authorized.Detail, StringComparison.Ordinal);
        Assert.Contains("mode=toggle", authorized.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("uplift", authorized.Detail, StringComparison.OrdinalIgnoreCase);
        var researched = Assert.Single(audit.Entries, e => e.Action == "chat.web_researched");
        Assert.Contains("promptVersion=research-open-v1", researched.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(audit.Entries, e => e.Action == "chat.interviewed");
    }

    [Fact]
    public async Task A_work_question_the_procurement_gate_would_turn_away_is_researched_on_the_web()
    {
        var gateway = NewGateway();
        var factory = Host(gateway, new RecordingAuditWriter(), Enabled());
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var (client, conversationId) = await OpenAsync(factory, tenantId);

        // "pasta" trips the domain gate's off-domain lexicon; with the toggle on it is a work
        // question about a supplier market, answered from the web alone (nothing in the store).
        using var body = await AskAsync(client, tenantId, conversationId, "wholesale pasta price trends for canteen suppliers this year");
        var root = body.RootElement;

        Assert.Equal("answer", root.GetProperty("kind").GetString());
        Assert.Equal(1, ResearchCalls(gateway));
        Assert.DoesNotContain(nameof(RecordingAiGateway.AnswerAsync), gateway.Calls);
        var citations = root.GetProperty("citations").EnumerateArray().ToList();
        Assert.NotEmpty(citations);
        Assert.All(citations, c => Assert.Equal("web", c.GetProperty("corpus").GetString()));
        Assert.Equal(["web"], root.GetProperty("provenance").GetProperty("sources").EnumerateArray().Select(s => s.GetString()).ToList());
        Assert.Equal("research-open-v1", root.GetProperty("provenance").GetProperty("promptVersion").GetString());
    }

    [Fact]
    public async Task A_contract_question_is_answered_from_the_store_and_the_web_side_by_side()
    {
        var gateway = NewGateway();
        var factory = Host(gateway, new RecordingAuditWriter(), Enabled());
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var (client, conversationId) = await OpenAsync(factory, tenantId);

        using var body = await AskAsync(client, tenantId, conversationId, "which contracts renew in the next 120 days?");
        var root = body.RootElement;

        Assert.Equal("answer", root.GetProperty("kind").GetString());
        Assert.Equal(1, ResearchCalls(gateway));
        var markdown = root.GetProperty("answerMarkdown").GetString()!;
        Assert.StartsWith("**From your contracts and Raffa's data**", markdown, StringComparison.Ordinal);
        Assert.Contains("**From the public web · unverified**", markdown, StringComparison.Ordinal);

        var citations = root.GetProperty("citations").EnumerateArray().ToList();
        Assert.Contains(citations, c => c.GetProperty("corpus").GetString() != "web");
        Assert.Contains(citations, c => c.GetProperty("corpus").GetString() == "web");
        Assert.Equal(Enumerable.Range(1, citations.Count).ToList(), citations.Select(c => c.GetProperty("n").GetInt32()).ToList());
        Assert.True(root.GetProperty("provenance").GetProperty("unverified").GetBoolean());
    }

    [Fact]
    public async Task When_the_research_persona_finds_no_work_angle_the_reply_points_at_a_search_engine()
    {
        var gateway = NewGateway();
        var factory = Host(gateway, new RecordingAuditWriter(), Enabled());
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var (client, conversationId) = await OpenAsync(factory, tenantId);

        // A work word keeps it past the lexicon; the fixture's open persona still calls it a joke.
        using var body = await AskAsync(client, tenantId, conversationId, "tell me a funny joke about our suppliers please");
        var root = body.RootElement;

        Assert.Equal("redirect", root.GetProperty("kind").GetString());
        Assert.Equal(1, ResearchCalls(gateway));
        Assert.Contains("outside Raffa's scope", root.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        Assert.Equal("external", root.GetProperty("actions")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_bare_greeting_is_still_a_greeting()
    {
        var gateway = NewGateway();
        var factory = Host(gateway, new RecordingAuditWriter(), Enabled());
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var (client, conversationId) = await OpenAsync(factory, tenantId);

        using var body = await AskAsync(client, tenantId, conversationId, "ciao");

        Assert.Equal("redirect", body.RootElement.GetProperty("kind").GetString());
        Assert.Equal(0, ResearchCalls(gateway));
    }

    [Fact]
    public async Task With_the_kill_switch_off_the_toggle_searches_nothing_and_says_why()
    {
        var gateway = NewGateway();
        var audit = new RecordingAuditWriter();
        var factory = Host(gateway, audit, options: null); // Program's default: Enabled = false
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var (client, conversationId) = await OpenAsync(factory, tenantId);

        using var body = await AskAsync(client, tenantId, conversationId, "latest news on Salesforce price increases");

        Assert.Equal("redirect", body.RootElement.GetProperty("kind").GetString());
        Assert.Contains("switched off", body.RootElement.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, ResearchCalls(gateway));
        var refused = Assert.Single(audit.Entries, e => e.Action == "chat.web_research_refused");
        Assert.Contains("gate=KillSwitch", refused.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_workspace_opt_in_the_toggle_names_the_admin_switch()
    {
        var gateway = NewGateway();
        var factory = Host(gateway, new RecordingAuditWriter(), Enabled());
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: false);
        var (client, conversationId) = await OpenAsync(factory, tenantId);

        using var body = await AskAsync(client, tenantId, conversationId, "latest news on Salesforce price increases");

        Assert.Equal("redirect", body.RootElement.GetProperty("kind").GetString());
        Assert.Contains("Admin", body.RootElement.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, ResearchCalls(gateway));
    }

    [Fact]
    public async Task The_daily_budget_still_caps_the_toggle()
    {
        var gateway = NewGateway();
        var factory = Host(gateway, new RecordingAuditWriter(), Enabled(dailyCalls: 1));
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var (client, conversationId) = await OpenAsync(factory, tenantId);

        using var first = await AskAsync(client, tenantId, conversationId, "latest news on Salesforce price increases");
        using var second = await AskAsync(client, tenantId, conversationId, "latest news on Oracle licence audits");

        Assert.Equal(1, ResearchCalls(gateway));
        Assert.Equal("redirect", second.RootElement.GetProperty("kind").GetString());
        Assert.Contains("allowance", second.RootElement.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_the_toggle_off_the_same_personal_question_gets_the_usual_warm_redirect_and_no_links()
    {
        var gateway = NewGateway();
        var audit = new RecordingAuditWriter();
        var factory = Host(gateway, audit, Enabled());
        var tenantId = await SeedTenantAsync(factory, workspaceOptIn: true);
        var (client, conversationId) = await OpenAsync(factory, tenantId);

        using var body = await AskAsync(client, tenantId, conversationId, "dimmi la ricetta della carbonara", webResearch: false);

        Assert.Equal("redirect", body.RootElement.GetProperty("kind").GetString());
        Assert.DoesNotContain(body.RootElement.GetProperty("actions").EnumerateArray(), a => a.GetProperty("kind").GetString() == "external");
        Assert.DoesNotContain(audit.Entries, e => e.Action.StartsWith("chat.web_research", StringComparison.Ordinal));
        Assert.Contains(audit.Entries, e => e.Action == "chat.redirected" && e.Detail!.Contains("webMode=False", StringComparison.Ordinal));
    }
}
