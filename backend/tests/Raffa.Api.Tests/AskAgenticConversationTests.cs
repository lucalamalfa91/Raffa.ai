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

        public async Task StartAsync()
        {
            using var create = new HttpRequestMessage(HttpMethod.Post, "/api/conversations") { Content = JsonContent.Create(new { }) };
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

    private const string UiActionPayloadOpen = "OPEN_ACTION_PANEL";
}
