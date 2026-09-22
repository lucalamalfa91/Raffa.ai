using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// ADR-030 D1–D3 end to end over <c>POST /api/conversations</c> + <c>POST
/// /api/conversations/{id}/messages</c> with the fixture gateway: the question from the
/// motivating screenshot gets a drafted email (never an abstain), an unscoped request asks which
/// contract, a reminder/export request redirects to the screen that already has the answer, and
/// a "help me" phrasing is no longer the feature tour.
/// </summary>
public sealed class AskCapabilityGapTests(RaffaApiFactory factory) : IClassFixture<RaffaApiFactory>
{
    private const string UserId = "alice@example.com";

    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private (WebApplicationFactory<Program> Factory, RecordingAiGateway Gateway, RecordingAuditWriter Audit) Host(
        IReadOnlyDictionary<EntityId, string> supplierNames)
    {
        var gateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
        var audit = new RecordingAuditWriter();

        var host = factory
            .WithPresentedCallersAsMembers()
            .WithWebHostBuilder(builder => builder.UseSetting(
                "ConnectionStrings:Chat",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true"))
            .WithInMemoryAskEngine(gateway, new FixedClock(Now), auditWriter: audit)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<ISupplierNameLookup>(new StubSupplierNameLookup(supplierNames))));

        return (host, gateway, audit);
    }

    private static Contract AmazonContract(TenantId tenantId, EntityId supplierId) => new()
    {
        TenantId = tenantId,
        SupplierId = supplierId,
        Type = ContractDocumentType.OrderForm,
        Status = "Completed",
        Currency = "EUR",
        AnnualSpend = 612000m,
        AutoRenewal = true,
        EndDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(300),
        CancellationDeadline = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(120),
        RenewalTermMonths = 12,
        PaymentTerms = "Net 30",
        CreatedAt = Now,
    };

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

    private static async Task<(Guid ConversationId, JsonDocument Reply, string Raw)> AskAsync(
        HttpClient client, TenantId tenantId, string question, Guid? scopeContractId = null)
    {
        using var createRequest = Request(HttpMethod.Post, "/api/conversations", tenantId, new { scopeContractId = scopeContractId?.ToString() });
        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var conversationId = created.RootElement.GetProperty("id").GetGuid();

        using var messageRequest = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/messages", tenantId, new { question });
        var response = await client.SendAsync(messageRequest);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {raw}");

        return (conversationId, JsonDocument.Parse(raw), raw);
    }

    [Fact]
    public async Task Scoped_email_request_returns_a_draft_grounded_in_the_contract()
    {
        var tenantId = TenantId.New();
        var amazonId = EntityId.New();
        var (host, gateway, audit) = Host(new Dictionary<EntityId, string> { [amazonId] = "Amazon Web Services" });

        var contract = AmazonContract(tenantId, amazonId);
        await host.SeedContractAsync(contract);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = host.CreateClient();
        var (conversationId, reply, raw) = await AskAsync(
            client, tenantId, "I have to renegotiate with Amazon. Can you help me create an email based on the negotiation leverage?", contract.Id.Value);

        var root = reply.RootElement;
        Assert.Equal("draft", root.GetProperty("kind").GetString());
        Assert.StartsWith("I can't create or send emails from Raffa.ai yet, but", root.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("I don't have data", raw, StringComparison.Ordinal);

        var payload = root.GetProperty("payload");
        Assert.Equal("email-draft", payload.GetProperty("gap").GetProperty("key").GetString());
        Assert.Equal("en", payload.GetProperty("gap").GetProperty("language").GetString());
        var draft = payload.GetProperty("draft");
        var body = draft.GetProperty("body").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(draft.GetProperty("subject").GetString()));
        Assert.Contains("Amazon Web Services", body, StringComparison.Ordinal);
        Assert.Contains(contract.CancellationDeadline!.Value.ToString("yyyy-MM-dd"), body, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\[\d+\]", body);
        Assert.Equal(3, payload.GetProperty("feedbackOffer").GetProperty("questions").GetArrayLength());

        Assert.NotEmpty(root.GetProperty("citations").EnumerateArray());
        var hrefs = root.GetProperty("actions").EnumerateArray().Select(a => a.GetProperty("href").GetString()).ToList();
        Assert.Contains(hrefs, h => h!.StartsWith("/renewals?select=", StringComparison.Ordinal));
        Assert.Contains(hrefs, h => h == $"/contracts/{contract.Id.Value}");
        Assert.Equal(2, root.GetProperty("followUps").GetArrayLength());

        // Council (two or three agents -- the InMemory host has no clause evidence for the
        // contract analyst) + offer planner + negotiation writer; never the answer role.
        Assert.InRange(gateway.Calls.Count(c => c == "AnalyzeAsync"), 4, 5);
        Assert.DoesNotContain("AnswerAsync", gateway.Calls);

        var turn = Assert.Single(audit.Entries, e => e.Action.StartsWith("chat.", StringComparison.Ordinal));
        Assert.Equal("chat.drafted", turn.Action);
        Assert.Contains("abstainGuardIntervened=False", turn.Detail, StringComparison.Ordinal);

        // Resume: the stored turn carries the same draft byte for byte.
        using var getRequest = Request(HttpMethod.Get, $"/api/conversations/{conversationId}", tenantId);
        var getResponse = await client.SendAsync(getRequest);
        using var detail = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        var stored = detail.RootElement.GetProperty("messages").EnumerateArray().Last();
        Assert.Equal("draft", stored.GetProperty("kind").GetString());
        Assert.Equal(body, stored.GetProperty("payload").GetProperty("draft").GetProperty("body").GetString());
    }

    [Fact]
    public async Task Unscoped_email_request_asks_which_contract_with_supplier_follow_ups()
    {
        var tenantId = TenantId.New();
        var amazonId = EntityId.New();
        var (host, gateway, _) = Host(new Dictionary<EntityId, string> { [amazonId] = "Amazon Web Services" });

        var contract = AmazonContract(tenantId, amazonId);
        await host.SeedContractAsync(contract);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = host.CreateClient();
        var (_, reply, _) = await AskAsync(client, tenantId, "Puoi aiutarmi a scrivere la mail per il rinnovo?");

        var root = reply.RootElement;
        Assert.Equal("redirect", root.GetProperty("kind").GetString());
        Assert.StartsWith("Al momento non posso creare o inviare email da Raffa.ai", root.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        Assert.Equal(["Scrivi la mail per il rinnovo Amazon Web Services"], root.GetProperty("followUps").EnumerateArray().Select(f => f.GetString()).ToList());
        Assert.Equal("/contracts", root.GetProperty("actions")[0].GetProperty("href").GetString());
        Assert.Equal("it", root.GetProperty("payload").GetProperty("gap").GetProperty("language").GetString());
        Assert.NotEqual(JsonValueKind.Null, root.GetProperty("payload").GetProperty("feedbackOffer").ValueKind);
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task Email_request_naming_an_unknown_supplier_names_it_and_offers_the_portfolio()
    {
        var tenantId = TenantId.New();
        var amazonId = EntityId.New();
        var (host, _, _) = Host(new Dictionary<EntityId, string> { [amazonId] = "Amazon Web Services" });

        var contract = AmazonContract(tenantId, amazonId);
        await host.SeedContractAsync(contract);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = host.CreateClient();
        var (_, reply, _) = await AskAsync(client, tenantId, "Write the renewal email for Databricks");

        var root = reply.RootElement;
        Assert.Equal("redirect", root.GetProperty("kind").GetString());
        Assert.Contains("I have no validated Databricks contract", root.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reminder_request_redirects_to_renewals_with_the_feedback_offer_in_italian()
    {
        var tenantId = TenantId.New();
        var amazonId = EntityId.New();
        var (host, gateway, audit) = Host(new Dictionary<EntityId, string> { [amazonId] = "Amazon Web Services" });

        var contract = AmazonContract(tenantId, amazonId);
        await host.SeedContractAsync(contract);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = host.CreateClient();
        var (_, reply, _) = await AskAsync(client, tenantId, "Mettimi un promemoria per la disdetta Amazon Web Services");

        var root = reply.RootElement;
        Assert.Equal("redirect", root.GetProperty("kind").GetString());
        Assert.StartsWith("Al momento non posso impostare promemoria", root.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        Assert.Equal($"/renewals?select={contract.Id.Value}", root.GetProperty("actions")[0].GetProperty("href").GetString());
        Assert.Equal("reminder", root.GetProperty("payload").GetProperty("gap").GetProperty("key").GetString());
        Assert.Equal("Vuoi segnalarlo al team Raffa.ai perché lo implementi?", root.GetProperty("payload").GetProperty("feedbackOffer").GetProperty("prompt").GetString());
        Assert.Empty(gateway.Calls);
        Assert.Equal("chat.redirected", Assert.Single(audit.Entries, e => e.Action.StartsWith("chat.", StringComparison.Ordinal)).Action);
    }

    [Fact]
    public async Task Export_request_redirects_to_portfolio_in_english()
    {
        var tenantId = TenantId.New();
        var (host, gateway, _) = Host(new Dictionary<EntityId, string>());

        var client = host.CreateClient();
        var (_, reply, _) = await AskAsync(client, tenantId, "Export my contracts to Excel");

        var root = reply.RootElement;
        Assert.Equal("redirect", root.GetProperty("kind").GetString());
        Assert.StartsWith("I can't export files from Raffa.ai yet, but", root.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);
        // An empty portfolio swaps the greyed Portfolio capability for the upload action (R-SYS-04).
        Assert.Equal("upload", root.GetProperty("actions")[0].GetProperty("kind").GetString());
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task Help_me_send_an_email_is_not_the_capability_tour()
    {
        var tenantId = TenantId.New();
        var (host, _, _) = Host(new Dictionary<EntityId, string>());

        var client = host.CreateClient();
        var (_, reply, raw) = await AskAsync(client, tenantId, "can you help me send an email to the supplier?");

        Assert.Equal("redirect", reply.RootElement.GetProperty("kind").GetString());
        Assert.Contains("upload a contract in Documents", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Raffa can", raw, StringComparison.Ordinal);
    }
}
