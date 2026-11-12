using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.Api.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// The 2026-11-05 Coupa transcript, turn by turn, through the orchestrator (Chat:AskTurn:Agentic)
/// on one conversation: the summary reads the contract; "crea un report … con i dati di mercato"
/// builds a document on Coupa with the market scope (where the router answered "I have no market
/// benchmark"); "crea un kpi" builds the KPI on Coupa, the conversation's primary supplier (where
/// the router built it on Zscaler); and the server-owned state keeps Coupa as the primary supplier
/// throughout. The agent is the fixture gateway's deterministic double, so what is proved is the
/// plumbing -- tools, state, payloads, audit -- not the model's judgement.
/// </summary>
public sealed class AskAgenticConversationTests(RaffaApiFactory factory) : IClassFixture<RaffaApiFactory>
{
    private const string UserId = "alice@example.com";
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private sealed record Turn(JsonDocument Response, string Detail);

    private sealed class Conversation : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> _host;
        private readonly HttpClient _client;
        private readonly RecordingAuditWriter _audit;
        private readonly TenantId _tenantId;
        private Guid _conversationId;

        public Conversation(WebApplicationFactory<Program> host, RecordingAuditWriter audit, TenantId tenantId)
        {
            _host = host;
            _audit = audit;
            _tenantId = tenantId;
            _client = host.CreateClient();
        }

        public Guid Id => _conversationId;

        /// <summary>The interface language the app sends with every call (X-Raffa-Lang), or null for none.</summary>
        public string? Language { get; init; }

        public async Task StartAsync(Guid? scopeContractId = null)
        {
            using var create = new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
            {
                Content = scopeContractId is { } scope ? JsonContent.Create(new { scopeContractId = scope.ToString() }) : JsonContent.Create(new { }),
            };
            Headers(create);
            var response = await _client.SendAsync(create);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            _conversationId = created.RootElement.GetProperty("id").GetGuid();
        }

        public async Task<Turn> AskAsync(string question)
        {
            var before = _audit.Entries.Count;
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{_conversationId}/messages")
            {
                Content = JsonContent.Create(new { question }),
            };
            Headers(request);
            var response = await _client.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {raw}");
            await _host.Services.GetRequiredService<CapabilityFollowUpAppender>().WhenIdleAsync();
            var turn = Assert.Single(_audit.Entries.Skip(before), e => e.ResourceType == "ask_raffa_v2");
            return new Turn(JsonDocument.Parse(raw), turn.Detail!);
        }

        public async Task<JsonDocument> DetailAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{_conversationId}");
            Headers(request);
            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }

        private void Headers(HttpRequestMessage request)
        {
            request.Headers.Add("X-Tenant-Id", _tenantId.Value.ToString());
            request.Headers.Add("X-User-Id", UserId);
            if (Language is not null)
            {
                request.Headers.Add("X-Raffa-Lang", Language);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _host.DisposeAsync();
        }
    }

    private async Task<Conversation> StartAsync()
    {
        var tenantId = TenantId.New();
        var coupaId = EntityId.New();
        var zscalerId = EntityId.New();
        var audit = new RecordingAuditWriter();
        var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions(), new AskAgentFixtureScript());
        var host = factory
            .WithPresentedCallersAsMembers()
            .WithWebHostBuilder(builder => builder.UseSetting(
                "ConnectionStrings:Chat",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true"))
            .WithInMemoryAskEngine(gateway, new FixedClock(Now), auditWriter: audit)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISupplierNameLookup>(new StubSupplierNameLookup(new Dictionary<EntityId, string>
                {
                    [coupaId] = "Coupa",
                    [zscalerId] = "Zscaler",
                }));
                services.RemoveAll<AskTurnOptions>();
                services.AddSingleton(new AskTurnOptions { Agentic = true });
            }));

        // Zscaler first and larger, so a "first supplier in the portfolio" slip would pick it.
        var zscaler = Contract(tenantId, zscalerId, "Zscaler", 258000m, new DateOnly(2027, 3, 31), new DateOnly(2027, 2, 14));
        var coupa = Contract(tenantId, coupaId, "Coupa", 281000m, new DateOnly(2026, 4, 15), new DateOnly(2026, 1, 15));
        await host.SeedContractAsync(zscaler);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, zscaler.Id));
        await host.SeedContractAsync(coupa);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, coupa.Id));

        var conversation = new Conversation(host, audit, tenantId);
        await conversation.StartAsync();
        return conversation;
    }

    private static Contract Contract(TenantId tenantId, EntityId supplierId, string name, decimal spend, DateOnly end, DateOnly notice) => new()
    {
        TenantId = tenantId,
        SupplierId = supplierId,
        Type = ContractDocumentType.OrderForm,
        Status = "Completed",
        Currency = "CHF",
        AnnualSpend = spend,
        AutoRenewal = true,
        StartDate = end.AddYears(-1).AddDays(1),
        EndDate = end,
        CancellationDeadline = notice,
        RenewalTermMonths = 12,
        PaymentTerms = "Net 30",
        CreatedAt = Now,
    };

    /// <summary>The payload of the last Raffa answer (never a capability follow-up appended after it).</summary>
    private static JsonElement? LastRaffaPayload(JsonDocument detail) =>
        detail.RootElement.GetProperty("messages").EnumerateArray()
            .Where(m => m.GetProperty("role").GetString() == "raffa")
            .Select(m => m.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.Object ? p : (JsonElement?)null)
            .LastOrDefault(p => p is { } x && (!x.TryGetProperty("capabilityCheckFor", out var f) || f.ValueKind == JsonValueKind.Null));

    [Fact]
    public async Task The_Coupa_transcript_keeps_its_contract_in_focus_through_the_tools()
    {
        await using var chat = await StartAsync();

        // 1. The summary reads Coupa through the tools and cites it.
        var summary = await chat.AskAsync("Riassumi il contratto Coupa");
        Assert.Equal("answer", summary.Response.RootElement.GetProperty("kind").GetString());
        Assert.True(summary.Response.RootElement.GetProperty("citations").GetArrayLength() > 0);
        Assert.Matches(@"\bagentic=true\b", summary.Detail);
        Assert.Matches(@"\bsteps=list-portfolio,get-contract\b", summary.Detail);

        // 2. "crea un report … con i dati di mercato": a document on Coupa with the market scope --
        //    no "I have no market benchmark" refusal, no portfolio-wide subject.
        var report = await chat.AskAsync("crea un report per capire a che punto è questo contratto con i dati di mercato");
        Assert.Equal("answer", report.Response.RootElement.GetProperty("kind").GetString());
        Assert.DoesNotContain("non lo invento", report.Response.RootElement.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        Assert.Contains("compose-artifact", report.Detail, StringComparison.Ordinal);
        Assert.Contains("get-market-benchmark", report.Detail, StringComparison.Ordinal);
        var reportDetail = await chat.DetailAsync();
        var reportPayload = LastRaffaPayload(reportDetail);
        Assert.NotNull(reportPayload);
        var reportArtifact = reportPayload.Value.GetProperty("artifact");
        Assert.Equal("document", reportArtifact.GetProperty("kind").GetString());
        Assert.Equal("Coupa", reportArtifact.GetProperty("focusSupplier").GetString());
        Assert.True(reportArtifact.GetProperty("usedMarket").GetBoolean());
        // The capability investigator must not contradict the orchestrator: no "I cannot generate a
        // report" follow-up after the report was just built.
        Assert.DoesNotContain(reportDetail.RootElement.GetProperty("messages").EnumerateArray(),
            m => m.GetProperty("markdown").GetString()?.Contains("non posso generare", StringComparison.OrdinalIgnoreCase) == true);

        // 3. "crea un kpi": the KPI is on Coupa, the conversation's primary supplier -- never on
        //    Zscaler, the portfolio's first and largest contract.
        var kpi = await chat.AskAsync("crea un kpi");
        Assert.Equal("answer", kpi.Response.RootElement.GetProperty("kind").GetString());
        var kpiPayload = LastRaffaPayload(await chat.DetailAsync());
        Assert.NotNull(kpiPayload);
        var kpiArtifact = kpiPayload.Value.GetProperty("artifact");
        Assert.Equal("kpi", kpiArtifact.GetProperty("kind").GetString());
        Assert.Equal("Coupa", kpiArtifact.GetProperty("focusSupplier").GetString());
        Assert.Contains("Coupa", kpiArtifact.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Zscaler", kpiArtifact.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal(UiActionPayloadOpen, kpiPayload.Value.GetProperty("uiActions")[0].GetProperty("type").GetString());

        // 4. A market question in the same chat is answered from the tools, never refused.
        var market = await chat.AskAsync("come è messo Coupa rispetto al mercato?");
        Assert.Equal("answer", market.Response.RootElement.GetProperty("kind").GetString());
        Assert.Contains("get-market-benchmark", market.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("benchmark di mercato affidabile", market.Response.RootElement.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The 2026-11-12 Workday transcript: a chat opened on the Workday contract asks for the renewal strategy, then
    /// "avvia il rinnovo con questi termini, tenendo conto che possiamo ridurre anche il numero di lecenze del 10 %" and
    /// "aprilo in rinnovi". Both commands start the renewal of Workday at once (no email drafted in the chat, no button to
    /// Documents, no confirmation to click), and the brief the renewal case receives from this chat holds the strategy and
    /// the user's own 10% licence reduction.
    /// </summary>
    [Fact]
    public async Task The_Workday_transcript_starts_the_renewal_with_the_chat_context()
    {
        var tenantId = TenantId.New();
        var workdayId = EntityId.New();
        var splunkId = EntityId.New();
        var audit = new RecordingAuditWriter();
        var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions(), new AskAgentFixtureScript());
        var host = factory
            .WithPresentedCallersAsMembers()
            .WithWebHostBuilder(builder => builder.UseSetting(
                "ConnectionStrings:Chat",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true"))
            .WithInMemoryAskEngine(gateway, new FixedClock(Now), auditWriter: audit)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISupplierNameLookup>(new StubSupplierNameLookup(new Dictionary<EntityId, string>
                {
                    [workdayId] = "Workday",
                    [splunkId] = "Splunk",
                }));
                services.RemoveAll<AskTurnOptions>();
                services.AddSingleton(new AskTurnOptions { Agentic = true });
            }));
        var splunk = Contract(tenantId, splunkId, "Splunk", 400000m, new DateOnly(2027, 3, 31), new DateOnly(2027, 2, 14));
        var workday = Contract(tenantId, workdayId, "Workday", 344000m, new DateOnly(2027, 7, 17), new DateOnly(2027, 6, 17));
        await host.SeedContractAsync(splunk);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, splunk.Id));
        await host.SeedContractAsync(workday);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, workday.Id));
        // The transcript's interface is in English: Raffa answered the Italian messages in English.
        await using var chat = new Conversation(host, audit, tenantId) { Language = "en" };
        await chat.StartAsync(workday.Id.Value);

        // 1. The strategy question is a question: answered in the chat, nothing started.
        var strategy = await chat.AskAsync("How should we approach this renewal? Give me the strategy and the negotiation points.");
        Assert.Equal("answer", strategy.Response.RootElement.GetProperty("kind").GetString());
        Assert.False(LastRaffaPayload(await chat.DetailAsync()) is { } first && first.TryGetProperty("actionProposal", out var p0) && p0.ValueKind == JsonValueKind.Object);

        // 2. "avvia il rinnovo con questi termini …": the renewal of Workday starts at once -- no email in the chat.
        await chat.AskAsync("avvia il rinnovo con questi termini, tenendo conto che possiamo ridurre anche il numero di lecenze del 10 %");
        AssertStartsWorkdayRenewal(await chat.DetailAsync(), workday.Id.Value);

        // 3. "aprilo in rinnovi": the same, never a button to Documents.
        await chat.AskAsync("aprilo in rinnovi");
        AssertStartsWorkdayRenewal(await chat.DetailAsync(), workday.Id.Value);

        // 4. What the renewal case receives from this chat (POST /api/renewals with this conversationId): the strategy and the
        //    user's own constraint, never the start commands themselves.
        using var scope = host.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<Raffa.Chat.Application.Conversations.ConversationService>()
            .GetAsync(tenantId, UserId, new EntityId(chat.Id), CancellationToken.None);
        var brief = ChatBrief.From(Assert.IsType<Raffa.Chat.Application.Conversations.ConversationDetailResult>(stored), italian: false);
        Assert.NotNull(brief);
        Assert.Contains("ridurre anche il numero di lecenze del 10 %", brief, StringComparison.Ordinal);
        Assert.StartsWith("What you set in chat:", brief, StringComparison.Ordinal);
        Assert.Contains("Levers and strategy:", brief, StringComparison.Ordinal);
        Assert.Contains("How should we approach this renewal?", brief, StringComparison.Ordinal);
        Assert.DoesNotContain("aprilo in rinnovi", brief, StringComparison.Ordinal);
        Assert.DoesNotContain("Starting the renewal of", brief, StringComparison.Ordinal);
    }

    private static void AssertStartsWorkdayRenewal(JsonDocument detail, Guid workdayContractId)
    {
        var last = detail.RootElement.GetProperty("messages").EnumerateArray().Last(m => m.GetProperty("role").GetString() == "raffa");
        Assert.StartsWith("Starting the renewal of **Workday**", last.GetProperty("markdown").GetString(), StringComparison.Ordinal);
        var payload = last.GetProperty("payload");
        var proposal = payload.GetProperty("actionProposal");
        Assert.Equal("start_renewal", proposal.GetProperty("kind").GetString());
        Assert.Equal(workdayContractId.ToString(), proposal.GetProperty("contractId").GetString());
        Assert.True(proposal.GetProperty("autoRun").GetBoolean());
        Assert.False(payload.TryGetProperty("artifact", out var artifact) && artifact.ValueKind == JsonValueKind.Object);
        Assert.False(payload.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.Object);
        Assert.Equal(0, last.GetProperty("actions").GetArrayLength());
    }

    private const string UiActionPayloadOpen = "OPEN_ACTION_PANEL";
}
