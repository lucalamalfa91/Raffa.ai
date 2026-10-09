using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.AiFlows.WebResearch.Consent;
using Raffa.AiFlows.WebResearch.Isolation;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Application.WebResearch;
using Raffa.Chat.Domain.Conversations;
using Raffa.Chat.Infrastructure;
using Raffa.Documents.Contracts.Domain;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Api.Tests;

/// <summary>
/// F3-T03 / F3-D03 over the real HTTP pipeline: the daily web-research budget is reserved before the
/// research call and handed back when the call fails for transport or configuration reasons — never
/// for a content-filter verdict — on both entries (the per-question consent and the composer
/// toggle); and the history a later turn's prompts see never carries a web turn.
/// </summary>
public sealed class AskWebResearchBudgetTests : IClassFixture<RaffaApiFactory>
{
    private const string UserId = "alice@example.com";
    private const string ConsentQuestion = "search the web for typical uplift caps on saas renewals";
    private const string ToggleQuestion = "which contracts renew in the next 120 days?";
    private const string TransportError = "AI provider unavailable: 'openai/v1/responses' still failing after 1 retry (last outcome: 503 ServiceUnavailable).";
    private const string ContentFilterError = "Foundry request to 'openai/v1/responses' failed with 400 BadRequest: content_filter: The response was filtered.";
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public AskWebResearchBudgetTests(RaffaApiFactory factory)
    {
        _factory = factory.WithPresentedCallersAsMembers().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:DocumentsContracts",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
            builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");
        });
    }

    private static RecordingAiGateway NewGateway(params string?[] researchErrors) => new(
        new ScriptedResearchGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()),
            researchErrors));

    private WebApplicationFactory<Program> Host(RecordingAiGateway gateway, RecordingAuditWriter audit, int dailyCalls) =>
        _factory.WithInMemoryAskEngine(gateway, auditWriter: audit).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<WebResearchOptions>();
            services.AddSingleton(new WebResearchOptions { Enabled = true, RequireWorkspaceOptIn = true, DailyCallsPerTenant = dailyCalls });
        }));

    private static async Task<TenantId> SeedTenantAsync(WebApplicationFactory<Program> factory)
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
            WebResearchEnabled = true,
        });
        await db.SaveChangesAsync();
        return tenantId;
    }

    private static async Task<Guid> CreateConversationAsync(HttpClient client, TenantId tenantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/conversations") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", UserId);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return created.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonDocument> PostAsync(HttpClient client, TenantId tenantId, Guid conversationId, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversationId}/messages")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", UserId);
        var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(raw);
    }

    private static object Allow(Guid messageId) => new
    {
        question = "allow",
        interviewAnswer = new { messageId = messageId.ToString(), questionKey = WebConsentInterview.QuestionKey, optionKey = "allow" },
    };

    private static async Task<int> UsedCallsAsync(WebApplicationFactory<Program> factory, TenantId tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        return db.WebResearchUsage.Where(u => u.TenantId == tenantId).Sum(u => (int?)u.Calls) ?? 0;
    }

    private static int ResearchCalls(RecordingAiGateway gateway) => gateway.Calls.Count(c => c == nameof(RecordingAiGateway.ResearchAsync));

    [Fact]
    public async Task A_transport_failure_after_consent_gives_the_budget_back_so_the_retry_still_has_its_call()
    {
        var gateway = NewGateway(TransportError, null); // the first research call fails, the second succeeds
        var audit = new RecordingAuditWriter();
        var factory = Host(gateway, audit, dailyCalls: 1);
        var tenantId = await SeedTenantAsync(factory);
        var client = factory.CreateClient();
        var conversationId = await CreateConversationAsync(client, tenantId);

        using var consent = await PostAsync(client, tenantId, conversationId, new { question = ConsentQuestion });
        using var failed = await PostAsync(client, tenantId, conversationId, Allow(consent.RootElement.GetProperty("messageId").GetGuid()));

        Assert.Equal("abstain", failed.RootElement.GetProperty("kind").GetString());
        Assert.Contains("not available", failed.RootElement.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, await UsedCallsAsync(factory, tenantId));
        var researched = Assert.Single(audit.Entries, e => e.Action == "chat.web_researched");
        Assert.Contains("budgetReleased=True", researched.Detail, StringComparison.Ordinal);

        // The day's single call is still there: asking again is offered, consented and answered.
        using var again = await PostAsync(client, tenantId, conversationId, new { question = ConsentQuestion });
        Assert.Equal("interview", again.RootElement.GetProperty("kind").GetString());
        using var answered = await PostAsync(client, tenantId, conversationId, Allow(again.RootElement.GetProperty("messageId").GetGuid()));
        Assert.Equal("answer", answered.RootElement.GetProperty("kind").GetString());
        Assert.Equal(2, ResearchCalls(gateway));
        Assert.Equal(1, await UsedCallsAsync(factory, tenantId));
    }

    [Fact]
    public async Task A_content_filter_verdict_keeps_the_budget_spent()
    {
        var gateway = NewGateway(ContentFilterError);
        var audit = new RecordingAuditWriter();
        var factory = Host(gateway, audit, dailyCalls: 1);
        var tenantId = await SeedTenantAsync(factory);
        var client = factory.CreateClient();
        var conversationId = await CreateConversationAsync(client, tenantId);

        using var consent = await PostAsync(client, tenantId, conversationId, new { question = ConsentQuestion });
        using var filtered = await PostAsync(client, tenantId, conversationId, Allow(consent.RootElement.GetProperty("messageId").GetGuid()));

        Assert.Equal("abstain", filtered.RootElement.GetProperty("kind").GetString());
        Assert.Equal(1, await UsedCallsAsync(factory, tenantId));
        Assert.Contains("budgetReleased=False", Assert.Single(audit.Entries, e => e.Action == "chat.web_researched").Detail, StringComparison.Ordinal);

        using var closed = await PostAsync(client, tenantId, conversationId, new { question = ConsentQuestion });
        Assert.Equal("redirect", closed.RootElement.GetProperty("kind").GetString());
        Assert.Contains("allowance", closed.RootElement.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_successful_research_keeps_exactly_one_call_spent()
    {
        var gateway = NewGateway();
        var audit = new RecordingAuditWriter();
        var factory = Host(gateway, audit, dailyCalls: 5);
        var tenantId = await SeedTenantAsync(factory);
        var client = factory.CreateClient();
        var conversationId = await CreateConversationAsync(client, tenantId);

        using var consent = await PostAsync(client, tenantId, conversationId, new { question = ConsentQuestion });
        using var answered = await PostAsync(client, tenantId, conversationId, Allow(consent.RootElement.GetProperty("messageId").GetGuid()));

        Assert.Equal("answer", answered.RootElement.GetProperty("kind").GetString());
        Assert.Equal(1, await UsedCallsAsync(factory, tenantId));
        Assert.Contains("budgetReleased=False", Assert.Single(audit.Entries, e => e.Action == "chat.web_researched").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_toggle_entry_also_gives_the_budget_back_on_a_transport_failure_and_the_contracts_half_stands()
    {
        var gateway = NewGateway(TransportError);
        var audit = new RecordingAuditWriter();
        var factory = Host(gateway, audit, dailyCalls: 1);
        var tenantId = await SeedTenantAsync(factory);
        var client = factory.CreateClient();
        var conversationId = await CreateConversationAsync(client, tenantId);

        using var body = await PostAsync(client, tenantId, conversationId, new { question = ToggleQuestion, webResearch = true });

        Assert.Equal("answer", body.RootElement.GetProperty("kind").GetString());
        Assert.Contains("answer comes from Raffa's data only", body.RootElement.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, ResearchCalls(gateway));
        Assert.Equal(0, await UsedCallsAsync(factory, tenantId));
        Assert.Contains("budgetReleased=True", Assert.Single(audit.Entries, e => e.Action == "chat.web_researched").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void F3_D03_the_history_the_endpoint_hands_the_engine_carries_no_web_text()
    {
        const string webSummary = "Public, unverified: a 5-10% uplift cap is common on enterprise SaaS renewals [1].";
        var at = Now;
        ConversationMessageResult Msg(ConversationRole role, string markdown, string citations = "[]", string? prompt = null) =>
            new(EntityId.New(), EntityId.New(), role, ConversationMessageKind.Answer, markdown, citations, "[]", null, prompt, null, at);

        var detail = new ConversationDetailResult(
            EntityId.New(),
            "t",
            null,
            at,
            at,
            [
                Msg(ConversationRole.You, "search the web for typical uplift caps on saas renewals"),
                Msg(ConversationRole.Raffa, webSummary, """[{"n":1,"corpus":"web","href":"https://example.com/a"}]""", "research-v1"),
                Msg(ConversationRole.You, "what is the notice period?"),
                Msg(ConversationRole.Raffa, "The notice period is 90 days [1].", """[{"n":1,"corpus":"documents"}]""", "answer-v3"),
            ]);

        var history = ConversationsEndpointExtensions.BuildPromptHistory(detail);

        Assert.Equal(4, history.Count);
        Assert.DoesNotContain(history, t => t.Markdown.Contains("5-10%", StringComparison.Ordinal));
        Assert.Equal(WebHistoryIsolation.Placeholder, history[1].Markdown);
        Assert.Equal("The notice period is 90 days [1].", history[3].Markdown);
        Assert.Equal(["you", "raffa", "you", "raffa"], history.Select(t => t.Role).ToArray());
    }

    /// <summary>Delegates everything to the fixture gateway except the research role: each entry of
    /// <c>researchErrors</c> scripts one research call (an error string fails it; <c>null</c> lets the
    /// fixture answer); past the script the fixture answers.</summary>
    private sealed class ScriptedResearchGateway(IAiGateway inner, string?[] researchErrors) : IAiGateway
    {
        private int _research;

        public Task<Result<AiResearchResult>> ResearchAsync(AiResearchRequest request, CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _research) - 1;
            return index < researchErrors.Length && researchErrors[index] is { } error
                ? Task.FromResult(Result<AiResearchResult>.Failure(error))
                : inner.ResearchAsync(request, cancellationToken);
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            inner.ClassifyAsync(request, cancellationToken);

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            inner.ExtractAsync(request, cancellationToken);

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            inner.EmbedAsync(request, cancellationToken);

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            inner.AnswerAsync(request, cancellationToken);

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            inner.OcrAsync(request, cancellationToken);

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
            inner.AnalyzeAsync(request, cancellationToken);
    }
}
