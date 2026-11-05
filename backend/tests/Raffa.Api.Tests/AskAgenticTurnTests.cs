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
/// Chat:AskTurn:Agentic end to end (ADR-024 amendment 2026-11-05) over the fixture gateway's
/// deterministic orchestrator double: a contract question is answered by reading the portfolio and
/// the contract through the tools, with citations, and the turn's audit row names the tools; a
/// greeting makes no tool call; with the flag off nothing changes (no agent call at all).
/// </summary>
public sealed class AskAgenticTurnTests(RaffaApiFactory factory) : IClassFixture<RaffaApiFactory>
{
    private const string UserId = "alice@example.com";
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static FixtureAiGateway Fixture() =>
        new(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions(), new AskAgentFixtureScript());

    private WebApplicationFactory<Program> Host(IAiGateway gateway, RecordingAuditWriter audit, bool agentic, EntityId supplierId) =>
        factory
            .WithPresentedCallersAsMembers()
            .WithWebHostBuilder(builder => builder.UseSetting(
                "ConnectionStrings:Chat",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true"))
            .WithInMemoryAskEngine(gateway, new FixedClock(Now), auditWriter: audit)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISupplierNameLookup>(new StubSupplierNameLookup(new Dictionary<EntityId, string> { [supplierId] = "Coupa" }));
                services.RemoveAll<AskTurnOptions>();
                services.AddSingleton(new AskTurnOptions { Agentic = agentic });
            }));

    private static Contract CoupaContract(TenantId tenantId, EntityId supplierId) => new()
    {
        TenantId = tenantId,
        SupplierId = supplierId,
        Type = ContractDocumentType.OrderForm,
        Status = "Completed",
        Currency = "CHF",
        AnnualSpend = 281000m,
        AutoRenewal = true,
        EndDate = new DateOnly(2026, 4, 15),
        CancellationDeadline = new DateOnly(2026, 1, 15),
        RenewalTermMonths = 12,
        PaymentTerms = "Net 30",
        CreatedAt = Now,
    };

    private async Task<(JsonDocument Reply, string Detail, RecordingAiGateway Gateway)> AskAsync(bool agentic, string question)
    {
        var tenantId = TenantId.New();
        var supplierId = EntityId.New();
        var audit = new RecordingAuditWriter();
        var gateway = new RecordingAiGateway(Fixture());
        var host = Host(gateway, audit, agentic, supplierId);
        var contract = CoupaContract(tenantId, supplierId);
        await host.SeedContractAsync(contract);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query") { Content = JsonContent.Create(new { question }) };
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", UserId);
        var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {raw}");
        var turn = Assert.Single(audit.Entries, e => e.ResourceType == "ask_raffa_v2");
        return (JsonDocument.Parse(raw), turn.Detail!, gateway);
    }

    [Fact]
    public async Task A_contract_question_is_answered_through_the_tools_with_citations()
    {
        var (reply, detail, gateway) = await AskAsync(agentic: true, "Quando scade il contratto Coupa?");

        Assert.Equal("answer", reply.RootElement.GetProperty("kind").GetString());
        Assert.True(reply.RootElement.GetProperty("citations").GetArrayLength() > 0);
        Assert.Contains("[1]", reply.RootElement.GetProperty("answerMarkdown").GetString());
        Assert.Contains("agentic=true", detail, StringComparison.Ordinal);
        Assert.Matches(@"\bstepsRun=2 steps=list-portfolio,get-contract failures=0", detail);
        Assert.Matches(@"\bturnId=[0-9a-f]{16}\b", detail);
        Assert.Contains("ConverseAsync", gateway.Calls);
        Assert.DoesNotContain("AnswerAsync", gateway.Calls);
    }

    [Fact]
    public async Task A_greeting_makes_no_tool_call()
    {
        var (reply, detail, _) = await AskAsync(agentic: true, "Ciao!");

        Assert.Equal("answer", reply.RootElement.GetProperty("kind").GetString());
        Assert.Contains("Raffa", reply.RootElement.GetProperty("answerMarkdown").GetString());
        Assert.Matches(@"\bstepsRun=0\b", detail);
    }

    [Fact]
    public async Task With_the_flag_off_the_router_answers_and_the_agent_role_is_never_called()
    {
        var (reply, detail, gateway) = await AskAsync(agentic: false, "Quando scade il contratto Coupa?");

        // The router's own reply (the fixture gateway abstains on this wording -- the agentic path above
        // answers it); what matters here is that nothing agentic ran.
        Assert.Contains(reply.RootElement.GetProperty("kind").GetString(), new[] { "answer", "abstain" });
        Assert.DoesNotContain("agentic=true", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("ConverseAsync", gateway.Calls);
    }
}
