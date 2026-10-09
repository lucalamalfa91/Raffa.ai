using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Logging;
using Raffa.AiGateway.Fixtures;
using Raffa.Chat.Application.Gaps;
using Raffa.Documents.Contracts.Domain;
using Raffa.Savings.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// The two questions that motivated Ask Raffa's savings-consultant rework, end to end over
/// <c>POST /api/conversations</c> + <c>POST /api/conversations/{id}/messages</c> with the fixture
/// gateway: a scoped "quali leve per risparmiare 20k sul rinnovo" gets a lever pack with the
/// target verdict and the council's plays and persists the levers as savings opportunities; an
/// unscoped "salvare 40K sul prossimo quarterly" is no longer read as supplier "K" and gets the
/// portfolio target pack; a bare follow-up in the same conversation stays on topic.
/// </summary>
public sealed class AskSavingsConsultantTests(RaffaApiFactory factory) : IClassFixture<RaffaApiFactory>
{
    private const string UserId = "alice@example.com";

    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private (WebApplicationFactory<Program> Factory, RecordingAiGateway Gateway) Host(
        IReadOnlyDictionary<EntityId, string> supplierNames, RecordingAuditWriter? audit = null)
    {
        var gateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var host = factory
            .WithPresentedCallersAsMembers()
            .WithWebHostBuilder(builder => builder.UseSetting(
                "ConnectionStrings:Chat",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true"))
            .WithInMemoryAskEngine(gateway, new FixedClock(Now), auditWriter: audit)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<ISupplierNameLookup>(new StubSupplierNameLookup(supplierNames))));

        return (host, gateway);
    }

    private static Contract ServiceNowContract(TenantId tenantId, EntityId supplierId, int deadlineInDays) => new()
    {
        TenantId = tenantId,
        SupplierId = supplierId,
        Type = ContractDocumentType.OrderForm,
        Status = "Completed",
        Currency = "EUR",
        AnnualSpend = 230000m,
        AutoRenewal = true,
        EndDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(deadlineInDays + 180),
        CancellationDeadline = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(deadlineInDays),
        RenewalTermMonths = 12,
        PaymentTerms = "Net 30",
        CreatedAt = Now,
    };

    private static async Task<(Guid ConversationId, JsonDocument Reply, string Raw)> AskAsync(
        HttpClient client, TenantId tenantId, string question, Guid? scopeContractId = null, Guid? conversationId = null)
    {
        if (conversationId is null)
        {
            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
            {
                Content = JsonContent.Create(new { scopeContractId = scopeContractId?.ToString() }),
            };
            createRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
            createRequest.Headers.Add("X-User-Id", UserId);
            var createResponse = await client.SendAsync(createRequest);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            conversationId = created.RootElement.GetProperty("id").GetGuid();
        }

        using var messageRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversationId}/messages")
        {
            Content = JsonContent.Create(new { question }),
        };
        messageRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        messageRequest.Headers.Add("X-User-Id", UserId);

        var response = await client.SendAsync(messageRequest);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {raw}");

        return (conversationId.Value, JsonDocument.Parse(raw), raw);
    }

    private static List<string> CitationTitles(JsonDocument reply) =>
        reply.RootElement.GetProperty("citations").EnumerateArray()
            .Select(c => c.GetProperty("title").GetString() ?? string.Empty)
            .ToList();

    [Fact]
    public async Task Scoped_savings_question_gets_the_lever_pack_the_council_and_persisted_opportunities()
    {
        var tenantId = TenantId.New();
        var serviceNowId = EntityId.New();
        var (host, gateway) = Host(new Dictionary<EntityId, string> { [serviceNowId] = "ServiceNow" });

        var contract = ServiceNowContract(tenantId, serviceNowId, deadlineInDays: 120);
        await host.SeedContractAsync(contract);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = host.CreateClient();
        var (_, reply, raw) = await AskAsync(client, tenantId, "quali leve posso usare per risparmiare 20 k sul rinnovo", contract.Id.Value);

        Assert.Equal("answer", reply.RootElement.GetProperty("kind").GetString());

        var titles = CitationTitles(reply);
        Assert.Contains(titles, t => t.Contains("saving target and lever coverage", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.StartsWith("Play 1 —", StringComparison.Ordinal));
        Assert.Contains("20000", raw, StringComparison.Ordinal);

        // Ask's agentic flow: the market researcher, then two analysts + one strategist, then the
        // answer role. INV-02: a savings question that gets its answer is neither T1, T2 nor T3, so
        // the Triggered capability investigator (ADR-031) makes no call of its own on this turn.
        Assert.Equal("market-researcher", gateway.Agents[0]);
        Assert.Equal(0, gateway.CapabilityChecks);
        Assert.Equal(4, gateway.Calls.Count(c => c == "AnalyzeAsync"));
        Assert.Equal(1, gateway.Calls.Count(c => c == "AnswerAsync"));

        using var scope = host.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenantContext.BeginScope(tenantId);
        var opportunities = await scope.ServiceProvider.GetRequiredService<SavingsOpportunityService>().ListAsync(tenantId);

        Assert.NotEmpty(opportunities);
        Assert.All(opportunities, o => Assert.Equal(contract.Id, o.ContractId));
        Assert.Contains(opportunities, o => o.OpportunityKey == "market-discount" && o.EstimatedSavingsHigh > 0m);
    }

    /// <summary>Plan T-01 / F2-T01 / F2-D03: the savings turn's one audit row names its turn and what
    /// its agentic flow did (steps, failures, market queries -- names and counts), never a name or
    /// a text. (The verdict's content is proved in CouncilVerdictTests.)</summary>
    [Fact]
    public async Task A_savings_turn_audits_its_turn_and_flow()
    {
        var tenantId = TenantId.New();
        var serviceNowId = EntityId.New();
        var audit = new RecordingAuditWriter();
        var (host, _) = Host(new Dictionary<EntityId, string> { [serviceNowId] = "ServiceNow" }, audit);

        var contract = ServiceNowContract(tenantId, serviceNowId, deadlineInDays: 120);
        await host.SeedContractAsync(contract);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        await AskAsync(
            host.CreateClient(), tenantId, "which levers can I use to save 20k on the renewal?", contract.Id.Value);

        var turn = Assert.Single(audit.Entries, e => e.ResourceType == "ask_raffa_v2");
        Assert.Equal("chat.answered", turn.Action);
        var detail = turn.Detail!;
        Assert.Matches(@"\bturnId=[0-9a-f]{16}\b", detail);
        Assert.Matches(@"\brunId=[0-9a-f]{16}\b", detail);
        Assert.Matches(@"\bstepsRun=\d+ steps=\S*market-researcher\S*lever-strategist\S* failures=0 failedSteps=none marketQueries=\d+", detail);
        Assert.DoesNotContain("ServiceNow", detail, StringComparison.Ordinal);
    }

    /// <summary>Plan T-01, end to end: with the real audit decorator in front of the fixture gateway,
    /// every <c>ai.*</c> row of a savings turn carries run=, step= and the turn's own id, the council's
    /// agents each under their own step, all in the one run the turn's audit row names.</summary>
    [Fact]
    public async Task Every_ai_row_of_a_savings_turn_carries_run_step_and_the_turn_id()
    {
        var tenantId = TenantId.New();
        var serviceNowId = EntityId.New();
        var audit = new RecordingAuditWriter();
        var (host, _) = Host(new Dictionary<EntityId, string> { [serviceNowId] = "ServiceNow" }, audit);
        host = host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Triggered mode (default) makes no investigator call on a savings turn; this test
            // audits the investigator's own run, so it runs in Always mode.
            services.AddSingleton<Microsoft.Extensions.Options.IOptionsMonitor<GapInvestigationOptions>>(
                new StaticGapInvestigationOptions(new GapInvestigationOptions { Mode = GapInvestigationMode.Always }));
            services.AddScoped<IAiGateway>(sp => new LoggingAiGateway(
                new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()),
                audit,
                sp.GetRequiredService<ITenantContext>(),
                new AiGatewayComplianceOptions()));
        }));

        var contract = ServiceNowContract(tenantId, serviceNowId, deadlineInDays: 120);
        await host.SeedContractAsync(contract);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        await AskAsync(
            host.CreateClient(), tenantId, "which levers can I use to save 20k on the renewal?", contract.Id.Value);
        await host.Services.GetRequiredService<CapabilityFollowUpAppender>().WhenIdleAsync();

        var turn = Assert.Single(audit.Entries, e => e.ResourceType == "ask_raffa_v2");
        var turnId = System.Text.RegularExpressions.Regex.Match(turn.Detail!, @"turnId=([0-9a-f]{16})").Groups[1].Value;
        var runId = System.Text.RegularExpressions.Regex.Match(turn.Detail!, @"runId=([0-9a-f]{16})").Groups[1].Value;

        var aiRows = audit.Entries.Where(e => e.Action.StartsWith("ai.", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(aiRows);
        Assert.All(aiRows, row =>
        {
            Assert.Matches(@"\bagent=\S+ run=[0-9a-f]{16} turn=" + turnId + @" step=\S+ latencyMs=\d+ outcome=ok", row.Detail!);
        });

        var turnRunRows = aiRows.Where(r => r.Detail!.Contains($"run={runId} ", StringComparison.Ordinal)).ToList();
        foreach (var step in new[] { "market-researcher", "contract-analyst", "market-analyst", "lever-strategist" })
        {
            Assert.Contains(turnRunRows, r => r.Detail!.Contains($" step={step} ", StringComparison.Ordinal));
        }

        // The answer role is audited under the same run, and the capability check under its own.
        Assert.Contains(turnRunRows, r => r.Action == "ai.answered");
        Assert.Contains(aiRows, r => r.Detail!.Contains("step=capability-investigator", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Portfolio_target_question_is_not_read_as_supplier_K_and_ranks_contracts_inside_the_window()
    {
        var tenantId = TenantId.New();
        var serviceNowId = EntityId.New();
        var salesforceId = EntityId.New();
        var (host, gateway) = Host(new Dictionary<EntityId, string>
        {
            [serviceNowId] = "ServiceNow",
            [salesforceId] = "Salesforce",
        });

        var soon = ServiceNowContract(tenantId, serviceNowId, deadlineInDays: 30);
        var later = ServiceNowContract(tenantId, salesforceId, deadlineInDays: 200);
        later.SupplierId = salesforceId;
        later.Currency = "USD";
        later.AnnualSpend = 640000m;
        await host.SeedContractAsync(soon);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, soon.Id));
        await host.SeedContractAsync(later);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, later.Id));

        var client = host.CreateClient();
        var (_, reply, raw) = await AskAsync(
            client, tenantId, "come posso salvare 40K sul prossimo quarterly basandoti sui contratti attivi? su quali contratto posso lavorare?");

        Assert.Equal("answer", reply.RootElement.GetProperty("kind").GetString());
        Assert.DoesNotContain("No K contract", raw, StringComparison.Ordinal);

        var titles = CitationTitles(reply);
        Assert.Contains(titles, t => t.StartsWith("Portfolio — saving target", StringComparison.Ordinal));
        Assert.Contains("40000", raw, StringComparison.Ordinal);
        Assert.Contains(gateway.Calls, c => c == "AnalyzeAsync");
    }

    [Fact]
    public async Task A_bare_follow_up_stays_a_savings_turn_with_the_same_goal()
    {
        var tenantId = TenantId.New();
        var serviceNowId = EntityId.New();
        var (host, gateway) = Host(new Dictionary<EntityId, string> { [serviceNowId] = "ServiceNow" });

        var contract = ServiceNowContract(tenantId, serviceNowId, deadlineInDays: 120);
        await host.SeedContractAsync(contract);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = host.CreateClient();
        var (conversationId, _, _) = await AskAsync(client, tenantId, "quali leve posso usare per risparmiare 20 k sul rinnovo", contract.Id.Value);
        var analyzeCallsAfterFirstTurn = gateway.Calls.Count(c => c == "AnalyzeAsync");

        var (_, reply, raw) = await AskAsync(
            client, tenantId, "si ma come risparmio almeno 20K? non mi hai risposto", conversationId: conversationId);

        Assert.Equal("answer", reply.RootElement.GetProperty("kind").GetString());
        Assert.Contains(CitationTitles(reply), t => t.Contains("saving target and lever coverage", StringComparison.Ordinal));
        Assert.Contains("20000", raw, StringComparison.Ordinal);
        // The same four agents again (INV-02: a bare follow-up answered from the pack starts no
        // capability check).
        Assert.Equal(analyzeCallsAfterFirstTurn + 4, gateway.Calls.Count(c => c == "AnalyzeAsync"));
    }
}
