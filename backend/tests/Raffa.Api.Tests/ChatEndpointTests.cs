using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E13/F06/US01/T01 (ask-engine, ADR-024 §6) that `POST /api/chat/query`
/// is actually mapped in <c>Program.cs</c> (via <see cref="ChatEndpointExtensions"/>) and enforces
/// its request-shape guard clauses — mirrors <see cref="ConversationsEndpointTests"/>'s own "not
/// just a placeholder" purpose. Most cases here only exercise branches that return before any
/// database call is made (the tenant-header check, the caller-identity check, and the
/// blank-question check), the same "no running Postgres needed" shape every sibling class in this
/// project already follows.
///
/// <para>
/// <b>Review-pass addition</b>: a successful call creates a conversation and runs the full Ask
/// engine (<c>AskCopilotService</c>), which this project's own <c>ConnectionStrings:*</c> test
/// settings never point at a real, reachable Postgres — so the three success-path proof points
/// this task's own Definition of Done names ("zero retrieval calls on 'ciao'"; "120-day renewal
/// question answered with per-contract citations"; "no `Document:` guid and no 'Structured query'
/// substring in any reply") are proven here via <see cref="InMemoryAskEngineFactory.WithInMemoryAskEngine"/>
/// (EF Core InMemory in place of Npgsql for <c>DocumentsContractsDbContext</c>/<c>ChatDbContext</c>,
/// a <see cref="RecordingAiGateway"/>, a no-op <c>IAuditWriter</c>) rather than a Testcontainer —
/// <c>Raffa.IntegrationTests.AskRaffaRagCrossTenantIsolationTests</c> still separately proves
/// the same pipeline against a real Postgres+pgvector+RLS instance, including the one intent
/// (clause retrieval) the InMemory provider cannot run (see that helper's own doc comment).
/// </para>
///
/// Supersedes this file's own previous content (task E02/F04/US02/T01): the old
/// `Structured`/`Semantic`/`canDetermine` response shape this endpoint used to return no longer
/// exists — ADR-024 replaced it with the reply contract every other `kind`-bearing endpoint in this
/// host now returns.
/// </summary>
public sealed class ChatEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ChatEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:DocumentsContracts",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
            builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");
        });
    }

    [Fact]
    public async Task Missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat/query", new { question = "What liability do we have with AWS?" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query")
        {
            Content = JsonContent.Create(new { question = "What liability do we have with AWS?" }),
        };
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_user_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query")
        {
            Content = JsonContent.Create(new { question = "What liability do we have with AWS?" }),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_or_blank_question_returns_400(string? question)
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query")
        {
            Content = JsonContent.Create(new { question }),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-User-Id", "alice@example.com");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// R-ASK-02/ADR-011 "off-domain never reaches retrieval", made concrete: the greeting gate
    /// label is answered entirely by <c>Raffa.Chat.Application.Reply.RedirectReplyBuilder
    /// .GreetingOrOffDomain</c> (that method's own doc comment: "No Raffa.AiGateway call happens
    /// for any of these"), so a <see cref="RecordingAiGateway"/> standing in for the host's own
    /// gateway must see zero calls of any kind for "ciao" — strictly stronger than, and therefore
    /// proof of, this task's own Definition of Done line ("zero retrieval calls on 'ciao'
    /// (recording retrieval fake)").
    /// </summary>
    [Fact]
    public async Task Greeting_question_never_reaches_the_ai_gateway_and_returns_a_redirect()
    {
        var recordingGateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var client = _factory.WithInMemoryAskEngine(recordingGateway).CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query")
        {
            Content = JsonContent.Create(new { question = "ciao" }),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-User-Id", "alice@example.com");

        var response = await client.SendAsync(request);
        var rawBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(rawBody);
        Assert.Equal("redirect", body.RootElement.GetProperty("kind").GetString());

        // Strictly stronger than the DoD's own "zero retrieval calls" — zero AI Gateway calls of
        // any kind, matching RedirectReplyBuilder.GreetingOrOffDomain's own doc comment.
        Assert.Empty(recordingGateway.Calls);

        Assert.DoesNotContain("Structured query", rawBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Document:", rawBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// This task's own Definition of Done line, verbatim: "120-day renewal question answered with
    /// per-contract citations; no `Document:` guid and no 'Structured query' substring in any
    /// reply" — the spec §8.3 worked example
    /// (<c>Raffa.Chat.Application.AskRaffaQueryRouter</c>'s own "renew"/"next N days" keyword
    /// match), routed by <c>Raffa.Chat.Application.Planning.IntentPlanner</c> to
    /// <c>AskIntent.StructuredFact</c> and answered by
    /// <c>Raffa.Api.AskCopilotService.BuildStructuredFactPackAsync</c> — the exact method this
    /// review pass fixed (it used to title its own aggregate pack item "Structured query result",
    /// which both <c>FixtureAiGateway.AnswerFromPack</c>'s own echo and
    /// <c>Raffa.Chat.Application.Reply.CopilotReplyBuilder.BuildCitations</c> would fold straight
    /// into <c>answerMarkdown</c>/a citation title).
    /// </summary>
    [Fact]
    public async Task Renewal_window_question_is_answered_with_per_contract_citations_and_no_engineer_chrome()
    {
        var recordingGateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var factory = _factory.WithInMemoryAskEngine(recordingGateway, new FixedClock(now));

        var tenantId = TenantId.New();

        // CreatedAt is deliberately distinct across all three (never a three-way tie):
        // PortfolioQueryService.GetPortfolioAsync orders by `OrderByDescending(c => c.CreatedAt)
        // .ThenBy(c => c.Id)` — against real Postgres that ORDER BY runs in SQL, but the InMemory
        // provider falls back to LINQ-to-Objects, whose secondary key (EntityId, a plain record
        // struct with no IComparable) only gets compared when two rows tie on CreatedAt. Distinct
        // timestamps mean CreatedAt alone always decides the order, so that fallback path never
        // has to compare an EntityId at all.
        var renewingSoon = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "Completed",
            Currency = "CHF",
            EndDate = today.AddDays(30),
            AutoRenewal = true,
            CreatedAt = now,
        };

        var alsoRenewingSoon = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.OrderForm,
            Status = "Completed",
            Currency = "CHF",
            EndDate = today.AddDays(90),
            AutoRenewal = true,
            CreatedAt = now.AddSeconds(-1),
        };

        var notInWindow = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Sow,
            Status = "Completed",
            Currency = "CHF",
            EndDate = today.AddDays(400),
            AutoRenewal = true,
            CreatedAt = now.AddSeconds(-2),
        };

        await factory.SeedContractAsync(renewingSoon);
        await factory.SeedContractAsync(alsoRenewingSoon);
        await factory.SeedContractAsync(notInWindow);

        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query")
        {
            Content = JsonContent.Create(new { question = "Which contracts renew in the next 120 days?" }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", "alice@example.com");

        var response = await client.SendAsync(request);
        var rawBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(rawBody);
        Assert.Equal("answer", body.RootElement.GetProperty("kind").GetString());

        var citedDocumentIds = body.RootElement.GetProperty("citations").EnumerateArray()
            .Select(c => c.TryGetProperty("documentId", out var id) ? id.GetString() : null)
            .Where(id => id is not null)
            .ToList();

        Assert.Contains($"fact:{renewingSoon.Id}:renewal", citedDocumentIds);
        Assert.Contains($"fact:{alsoRenewingSoon.Id}:renewal", citedDocumentIds);
        Assert.DoesNotContain($"fact:{notInWindow.Id}:renewal", citedDocumentIds);

        Assert.DoesNotContain("Structured query", rawBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Document:", rawBody, StringComparison.Ordinal);
    }
}
