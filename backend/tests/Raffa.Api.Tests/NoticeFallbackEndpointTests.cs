using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Chat.Application.Interview;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E30/F02/US01/T01 (NW-94; parent story us-01-notice-fallbacks, "a
/// scoped notice turn never asks 'which supplier'"): the five server-decided notice outcomes
/// <c>AskCopilotService.BuildNoticeFallbackReplyAsync</c> now short-circuits to, before any pack
/// reaches <c>AnswerComposer</c>/the AI gateway. Same "InMemory EF Core + RecordingAiGateway, no
/// Testcontainer" shape <see cref="ScopedAskEndpointTests"/>/<see cref="AskAbstainRecoveryActionTests"/>
/// already establish -- <see cref="InMemoryAskEngineFactory.SeedClauseAsync"/> is this task's own
/// addition to that shared fixture, so a "matching clause" scenario no longer needs a real Postgres
/// (<c>Contract360QueryService.GetByIdAsync</c>'s <c>Clauses</c> read is a plain EF Core query, not
/// pgvector search). Every test asks the same phrasing, "What's the cancellation deadline for this
/// contract?" (or the unscoped sibling without "for this contract") -- it matches both
/// <c>IntentPlanner</c>'s notice lexicon (keeping the eventual intent <c>StructuredFact</c>, the
/// same safe InMemory-provider path <see cref="ScopedAskEndpointTests"/>'s own doc comment already
/// relies on) and <c>AskCopilotService.NoticeQuestionPattern</c>'s local mirror of it.
/// </summary>
public sealed class NoticeFallbackEndpointTests : IClassFixture<RaffaApiFactory>
{
    private const string ScopedNoticeQuestion = "What's the cancellation deadline for this contract?";
    private const string UnscopedNoticeQuestion = "What's the cancellation deadline?";

    private readonly WebApplicationFactory<Program> _factory;

    public NoticeFallbackEndpointTests(RaffaApiFactory factory)
    {
        // Same three-connection-string override ScopedAskEndpointTests uses -- this class mixes
        // conversation-scoped calls (cases 1-4) with one direct /api/chat/query call (case 5), so
        // the superset keeps every test in this file on one identical host shape.
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

    /// <summary>Case 1 (AC-1, happy path): a known cancellation deadline plus a matching clause
    /// that resolves to a real document page (tier 1, <c>AskCopilotService.ResolveTenantClauseLinks</c>)
    /// answers, citing both the dated fact and the clause -- the clause citation alone carries the
    /// real contractId+documentId+page+href (NW-83) the client needs to build the two-CTA card
    /// (NW-93) without this test depending on any web code.</summary>
    [Fact]
    public async Task Deadline_with_a_spanned_clause_answers_citing_the_fact_and_the_deep_linked_clause()
    {
        var recordingGateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var tenantId = TenantId.New();
        const string userId = "alice@example.com";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var factory = _factory.WithInMemoryAskEngine(recordingGateway);

        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "Completed",
            Currency = "CHF",
            EndDate = today.AddDays(400),
            CancellationDeadline = today.AddDays(30),
            AutoRenewal = true,
            RenewalTermMonths = 12,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await factory.SeedContractAsync(contract);
        var document = InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id);
        await factory.SeedDocumentAsync(document);
        await factory.SeedClauseAsync(new Clause
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            ClauseType = "Termination",
            RawText = "Either party may terminate by giving 60 days written notice before the renewal date.",
            SourceDocumentId = document.Id,
            SourceSpan = "§9.1",
            SourcePage = 4,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var client = factory.CreateClient();
        var conversationId = await CreateScopedConversationAsync(client, tenantId, userId, contract.Id.Value);

        var response = await AskAsync(client, tenantId, userId, conversationId, ScopedNoticeQuestion);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(rawBody);
        var root = body.RootElement;

        Assert.Equal("answer", root.GetProperty("kind").GetString());

        var citations = root.GetProperty("citations").EnumerateArray().ToList();
        Assert.Equal(2, citations.Count);

        var clauseCitation = citations.Single(c =>
            (c.GetProperty("href").GetString() ?? string.Empty).Contains("/viewer?page=", StringComparison.Ordinal));
        Assert.Equal(4, clauseCitation.GetProperty("page").GetInt32());
        Assert.Equal(contract.Id.ToString(), clauseCitation.GetProperty("contractId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(clauseCitation.GetProperty("documentId").GetString()));

        var factCitation = citations.Single(c => c.GetProperty("href").GetString() == $"/contracts/{contract.Id}");
        Assert.Equal(contract.Id.ToString(), factCitation.GetProperty("contractId").GetString());

        var actions = root.GetProperty("actions").EnumerateArray().ToList();
        var reviewAction = Assert.Single(actions);
        Assert.Equal("navigate", reviewAction.GetProperty("kind").GetString());
        Assert.Equal($"/contracts/{contract.Id}", reviewAction.GetProperty("href").GetString());

        // NW-94: fully server-decided -- the AI gateway is never called for a notice question.
        Assert.Empty(recordingGateway.Calls);
    }

    /// <summary>Case 2 (AC-2): a known cancellation deadline but no matching clause at all -- answers
    /// the date, citing the fact alone (never a fabricated page/span).</summary>
    [Fact]
    public async Task Deadline_with_no_matching_clause_answers_the_date_with_one_unspanned_citation()
    {
        var recordingGateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var tenantId = TenantId.New();
        const string userId = "alice@example.com";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var deadline = today.AddDays(45);

        var factory = _factory.WithInMemoryAskEngine(recordingGateway);

        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "Completed",
            Currency = "CHF",
            EndDate = today.AddDays(400),
            CancellationDeadline = deadline,
            AutoRenewal = true,
            RenewalTermMonths = 12,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = factory.CreateClient();
        var conversationId = await CreateScopedConversationAsync(client, tenantId, userId, contract.Id.Value);

        var response = await AskAsync(client, tenantId, userId, conversationId, ScopedNoticeQuestion);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(rawBody);
        var root = body.RootElement;

        Assert.Equal("answer", root.GetProperty("kind").GetString());
        Assert.Contains(deadline.ToString("yyyy-MM-dd"), root.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);

        var citations = root.GetProperty("citations").EnumerateArray().ToList();
        var citation = Assert.Single(citations);
        Assert.Equal($"/contracts/{contract.Id}", citation.GetProperty("href").GetString());

        var actions = root.GetProperty("actions").EnumerateArray().ToList();
        var reviewAction = Assert.Single(actions);
        Assert.Equal($"/contracts/{contract.Id}", reviewAction.GetProperty("href").GetString());

        Assert.Empty(recordingGateway.Calls);
    }

    /// <summary>Case 3 (AC-3 first clause): no known cancellation deadline, but a matching clause
    /// names one in its own words -- quotes the clause text verbatim as the whole answer.</summary>
    [Fact]
    public async Task No_deadline_with_a_matching_clause_quotes_the_clause_text()
    {
        var recordingGateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var tenantId = TenantId.New();
        const string userId = "alice@example.com";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        const string clauseText =
            "Notice of non-renewal must be given in writing at least 90 days prior to the then-current term's expiration.";

        var factory = _factory.WithInMemoryAskEngine(recordingGateway);

        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "Completed",
            Currency = "CHF",
            EndDate = today.AddDays(400),
            CancellationDeadline = null,
            AutoRenewal = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        // Deliberately no SourceDocumentId/SourcePage: case 3 quotes the clause regardless of
        // whether it has a real span -- only case 1/2's own "deadline known" branch cares about
        // hasSpan at all.
        await factory.SeedClauseAsync(new Clause
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            ClauseType = "Auto-renewal",
            RawText = clauseText,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var client = factory.CreateClient();
        var conversationId = await CreateScopedConversationAsync(client, tenantId, userId, contract.Id.Value);

        var response = await AskAsync(client, tenantId, userId, conversationId, ScopedNoticeQuestion);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(rawBody);
        var root = body.RootElement;

        Assert.Equal("answer", root.GetProperty("kind").GetString());
        Assert.Contains(clauseText, root.GetProperty("answerMarkdown").GetString(), StringComparison.Ordinal);

        var citations = root.GetProperty("citations").EnumerateArray().ToList();
        var citation = Assert.Single(citations);
        Assert.Equal(clauseText, citation.GetProperty("snippet").GetString());

        var actions = root.GetProperty("actions").EnumerateArray().ToList();
        var reviewAction = Assert.Single(actions);
        Assert.Equal($"/contracts/{contract.Id}", reviewAction.GetProperty("href").GetString());

        Assert.Empty(recordingGateway.Calls);
    }

    /// <summary>Case 4 (AC-3 second clause): neither a deadline nor a matching clause -- an honest
    /// abstain that names this contract's own supplier and offers the Contract 360 action, never the
    /// generic ask-hint recovery and never a "which supplier" question (the story's own title) even
    /// though the turn ultimately could not ground an answer.</summary>
    [Fact]
    public async Task No_deadline_and_no_clause_abstains_naming_the_contract_not_which_supplier()
    {
        var recordingGateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var tenantId = TenantId.New();
        const string userId = "alice@example.com";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var factory = _factory.WithInMemoryAskEngine(recordingGateway);

        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "Completed",
            Currency = "CHF",
            EndDate = today.AddDays(400),
            CancellationDeadline = null,
            AutoRenewal = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = factory.CreateClient();
        var conversationId = await CreateScopedConversationAsync(client, tenantId, userId, contract.Id.Value);

        var response = await AskAsync(client, tenantId, userId, conversationId, ScopedNoticeQuestion);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(rawBody);
        var root = body.RootElement;

        Assert.Equal("abstain", root.GetProperty("kind").GetString());
        Assert.Equal(0, root.GetProperty("citations").GetArrayLength());

        var answerMarkdown = root.GetProperty("answerMarkdown").GetString() ?? string.Empty;
        Assert.DoesNotContain("which supplier", answerMarkdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Msa", answerMarkdown, StringComparison.Ordinal); // no SupplierId seeded -> falls back to the type

        var actions = root.GetProperty("actions").EnumerateArray().ToList();
        var reviewAction = Assert.Single(actions);
        Assert.Equal("navigate", reviewAction.GetProperty("kind").GetString());
        Assert.Equal($"/contracts/{contract.Id}", reviewAction.GetProperty("href").GetString());

        Assert.Empty(recordingGateway.Calls);
    }

    /// <summary>Case 5 (AC-3 third clause): no contract in scope at all (no conversation scope, no
    /// resolvable named supplier) -- abstains to Portfolio rather than guessing a contract or asking
    /// "which supplier". One validated contract is seeded (with no SupplierId, so no
    /// <c>ISupplierNameLookup</c> stub is needed) purely so <c>ValidatedContractCount &gt; 0</c> and
    /// the Portfolio action resolves as a real navigate, not R-SYS-04's zero-contracts Upload
    /// replacement.</summary>
    [Fact]
    public async Task Unscoped_notice_question_abstains_to_portfolio_never_a_which_supplier_guess()
    {
        var recordingGateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var tenantId = TenantId.New();
        const string userId = "alice@example.com";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // ADR-030 supersedes case 5 with a "which contract?" interview while
        // Chat:Interview:AskOnUnscopedNotice is on (the default); this test pins the NW-94 abstain
        // that still runs with the switch off.
        var factory = _factory
            .WithInMemoryAskEngine(recordingGateway)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton(new InterviewOptions { AskOnUnscopedNotice = false })));

        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "Completed",
            Currency = "CHF",
            EndDate = today.AddDays(400),
            CancellationDeadline = today.AddDays(30),
            AutoRenewal = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query")
        {
            Content = JsonContent.Create(new { question = UnscopedNoticeQuestion }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", userId);

        var response = await client.SendAsync(request);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(rawBody);
        var root = body.RootElement;

        Assert.Equal("abstain", root.GetProperty("kind").GetString());
        Assert.Equal(0, root.GetProperty("citations").GetArrayLength());

        var answerMarkdown = root.GetProperty("answerMarkdown").GetString() ?? string.Empty;
        Assert.DoesNotContain("which supplier", answerMarkdown, StringComparison.OrdinalIgnoreCase);

        var actions = root.GetProperty("actions").EnumerateArray().ToList();
        var portfolioAction = Assert.Single(actions);
        Assert.Equal("navigate", portfolioAction.GetProperty("kind").GetString());
        Assert.Equal("/contracts", portfolioAction.GetProperty("href").GetString());

        // Never cites or names the seeded contract either -- this is the honest "cannot resolve
        // which contract at all" case, not a guess dressed up as an answer.
        Assert.DoesNotContain(contract.Id.ToString(), rawBody, StringComparison.Ordinal);

        Assert.Empty(recordingGateway.Calls);
    }

    private static async Task<Guid> CreateScopedConversationAsync(
        HttpClient client, TenantId tenantId, string userId, Guid scopeContractId)
    {
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
        {
            Content = JsonContent.Create(new { scopeContractId = scopeContractId.ToString() }),
        };
        createRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        createRequest.Headers.Add("X-User-Id", userId);

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        return created.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> AskAsync(
        HttpClient client, TenantId tenantId, string userId, Guid conversationId, string question)
    {
        using var messageRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversationId}/messages")
        {
            Content = JsonContent.Create(new { question }),
        };
        messageRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        messageRequest.Headers.Add("X-User-Id", userId);

        return await client.SendAsync(messageRequest);
    }
}
