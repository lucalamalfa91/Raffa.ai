using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E25/F03/US01/T01 (story us-01-scoped-ask-backend, NW-56; ADR-024 "the
/// gate resolves the scope id before the R-ASK-10 check") that a conversation's own persisted
/// <c>ScopeContractId</c> — set on <c>POST /api/conversations</c> the way Contract 360's "Ask about
/// it" does (ADR-024 "citation landing... → /ask?scope=") — actually reaches
/// <c>AskCopilotService.AskAsync</c> and changes what the turn answers, over a real HTTP round trip
/// through `POST /api/conversations` + `POST /api/conversations/{id}/messages`
/// (<c>ConversationsEndpointExtensions.AskAndAppendAsync</c>, this task's own second file), the same
/// <see cref="InMemoryAskEngineFactory.WithInMemoryAskEngine"/> shape
/// <see cref="ChatEndpointTests"/>/<see cref="ConversationsEndpointTests"/> already use to prove a
/// success path without a Testcontainer.
///
/// <para>
/// Both tests below ask a question that names <b>no</b> known supplier the way the "Ask about it"
/// entry point itself never requires one — the whole point of AC-1/AC-2/AC-3 is that scope alone,
/// not the question text, decides which contract the turn is about.
/// </para>
/// </summary>
public sealed class ScopedAskEndpointTests : IClassFixture<RaffaApiFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ScopedAskEndpointTests(RaffaApiFactory factory)
    {
        _factory = factory.WithPresentedCallersAsMembers().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:DocumentsContracts",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
            builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");
            builder.UseSetting(
                "ConnectionStrings:Chat",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
        });
    }

    /// <summary>
    /// AC-2 ("the gate resolves the scope before the R-ASK-10 check") made concrete: without scope,
    /// a question naming an unrelated, unknown capitalized word ("Oracle's") trips
    /// <c>DomainGate</c>'s own deterministic supplier-resolution rule
    /// (<c>GateLabel.NeedsDocument</c>) and answers with the generic "no document for X" redirect —
    /// the "generic gate" this story's own Definition of Done names. Scoped to a contract this
    /// tenant already validated, the same question must answer about that contract instead.
    /// "cancellation deadline" keeps the eventual intent <c>StructuredFact</c> — a safe, InMemory
    /// -provider path (see <see cref="InMemoryAskEngineFactory"/>'s own doc comment on why clause/
    /// benchmark retrieval cannot run under this test's swapped DbContexts) — so nothing here can be
    /// exercising a different intent branch than the gate/pack scoping this task adds.
    /// </summary>
    [Fact]
    public async Task Scoped_conversation_answers_about_the_contract_instead_of_the_needs_document_redirect()
    {
        var recordingGateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var tenantId = TenantId.New();
        const string userId = "alice@example.com";
        var salesforceId = EntityId.New();

        var factory = _factory
            .WithInMemoryAskEngine(recordingGateway)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<ISupplierNameLookup>(
                    new StubSupplierNameLookup(new Dictionary<EntityId, string> { [salesforceId] = "Salesforce, Inc." }))));

        var scopedContract = new Contract
        {
            TenantId = tenantId,
            SupplierId = salesforceId,
            Type = ContractDocumentType.Msa,
            Status = "Completed",
            Currency = "CHF",
            AnnualSpend = 120000m,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await factory.SeedContractAsync(scopedContract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, scopedContract.Id));

        var client = factory.CreateClient();

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
        {
            Content = JsonContent.Create(new { scopeContractId = scopedContract.Id.Value.ToString() }),
        };
        createRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        createRequest.Headers.Add("X-User-Id", userId);

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var conversationId = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(scopedContract.Id.Value, created.RootElement.GetProperty("scopeContractId").GetGuid());

        using var messageRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversationId}/messages")
        {
            Content = JsonContent.Create(new { question = "Does Oracle's cancellation deadline apply here?" }),
        };
        messageRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        messageRequest.Headers.Add("X-User-Id", userId);

        var response = await client.SendAsync(messageRequest);
        var rawBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(rawBody);
        var root = body.RootElement;

        // AC-2: answers -- never the GateLabel.NeedsDocument redirect a free-text-only reading of
        // this question would produce (the capitalized "Oracle's" does not match any known
        // supplier).
        Assert.Equal("answer", root.GetProperty("kind").GetString());

        // AC-3: the pack/citations are scoped to the named contract's own supplier. Task
        // E28/F03/US01/T01 (NW-83) stamps a real contractId now (BuildContractFactItem's own
        // PackItem has no source document, so contractId -- not documentId -- is the real id these
        // per-contract citations carry).
        var citedContractIds = root.GetProperty("citations").EnumerateArray()
            .Select(c => c.TryGetProperty("contractId", out var id) ? id.GetString() : null)
            .Where(id => id is not null)
            .ToList();
        Assert.Contains(scopedContract.Id.ToString(), citedContractIds);

        // The redirect this fix removes is the only place "Oracle" could otherwise ever surface
        // (RedirectReplyBuilder.NeedsDocument's own copy: "No {namedSupplier} contract has been
        // uploaded and validated...") -- proving the gate never took that branch, not just that it
        // returned 200.
        Assert.DoesNotContain("Oracle", rawBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// AC-1/AC-3 made concrete against a two-contract tenant: "Which contracts renew in the next
    /// 120 days?" names no supplier at all — unscoped, <c>BuildStructuredFactPackAsync</c> would run
    /// the deterministic renewal-window query and cite every matching contract (both, here — see
    /// <see cref="ChatEndpointTests.Renewal_window_question_is_answered_with_per_contract_citations_and_no_engineer_chrome"/>
    /// for the identical unscoped aggregate proof). Scoped to one of the two, the reply must cite
    /// that contract alone: <see cref="Gate.DomainGate"/>'s own free-text extraction finds no
    /// candidate at all for this question, so only <c>GateLabel.InDomain</c>'s branch of this task's
    /// override (not <c>NeedsDocument</c>'s) is what makes this pass — the complementary case to
    /// <see cref="Scoped_conversation_answers_about_the_contract_instead_of_the_needs_document_redirect"/>.
    /// </summary>
    [Fact]
    public async Task Scoped_conversation_cites_only_the_scoped_contract_not_the_portfolio_wide_aggregate()
    {
        var recordingGateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var tenantId = TenantId.New();
        const string userId = "alice@example.com";
        var salesforceId = EntityId.New();
        var databricksId = EntityId.New();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var factory = _factory
            .WithInMemoryAskEngine(recordingGateway)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<ISupplierNameLookup>(new StubSupplierNameLookup(
                    new Dictionary<EntityId, string>
                    {
                        [salesforceId] = "Salesforce, Inc.",
                        [databricksId] = "Databricks Inc.",
                    }))));

        // Distinct CreatedAt so the InMemory provider's own ORDER BY fallback never ties (same
        // reason ChatEndpointTests' own seeded contracts never do).
        var now = DateTimeOffset.UtcNow;
        var scopedContract = new Contract
        {
            TenantId = tenantId,
            SupplierId = salesforceId,
            Type = ContractDocumentType.Msa,
            Status = "Completed",
            Currency = "CHF",
            EndDate = today.AddDays(30),
            AutoRenewal = true,
            CreatedAt = now,
        };
        var otherContract = new Contract
        {
            TenantId = tenantId,
            SupplierId = databricksId,
            Type = ContractDocumentType.OrderForm,
            Status = "Completed",
            Currency = "CHF",
            EndDate = today.AddDays(45),
            AutoRenewal = true,
            CreatedAt = now.AddSeconds(-1),
        };
        await factory.SeedContractAsync(scopedContract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, scopedContract.Id));
        await factory.SeedContractAsync(otherContract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, otherContract.Id));

        var client = factory.CreateClient();

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
        {
            Content = JsonContent.Create(new { scopeContractId = scopedContract.Id.Value.ToString() }),
        };
        createRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        createRequest.Headers.Add("X-User-Id", userId);

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var conversationId = created.RootElement.GetProperty("id").GetGuid();

        using var messageRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversationId}/messages")
        {
            Content = JsonContent.Create(new { question = "Which contracts renew in the next 120 days?" }),
        };
        messageRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        messageRequest.Headers.Add("X-User-Id", userId);

        var response = await client.SendAsync(messageRequest);
        var rawBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(rawBody);
        var root = body.RootElement;

        Assert.Equal("answer", root.GetProperty("kind").GetString());

        // Task E28/F03/US01/T01 (NW-83): contractId, not documentId, is the real stamped id for a
        // contract-level fact citation (see this file's other test for why).
        var citedContractIds = root.GetProperty("citations").EnumerateArray()
            .Select(c => c.TryGetProperty("contractId", out var id) ? id.GetString() : null)
            .Where(id => id is not null)
            .ToList();

        // AC-1/AC-3: scoped to the named contract's own supplier -- not the deterministic
        // renewal-window aggregate both contracts would otherwise match.
        Assert.Contains(scopedContract.Id.ToString(), citedContractIds);
        Assert.DoesNotContain(otherContract.Id.ToString(), citedContractIds);
    }
}
