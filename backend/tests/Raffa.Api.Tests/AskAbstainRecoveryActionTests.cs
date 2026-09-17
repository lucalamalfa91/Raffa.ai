using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E25/F05/US01/T01 (abstain-recovery-backend; parent story
/// us-01-abstain-recovery-backend AC-1/AC-3; ADR-024 "every abstain has a clickable next step"):
/// the two abstain paths <c>AskCopilotService.BuildInDomainReplyAsync</c> can reach without ever
/// calling the guard pipeline — an empty context pack, and the `answer` role gateway call itself
/// failing — both attach a real, <c>CapabilityRouting.ResolveActions</c>-sourced recovery action,
/// never an empty <c>actions[]</c>. The third abstain path (a guard-downgraded/model-abstained
/// `answer` result — <c>CopilotReplyBuilder.FromGuardedResult</c>'s own <c>!CanDetermine</c>
/// branch) is proven at unit level (<c>Raffa.Chat.Tests.Reply.CopilotReplyBuilderTests</c>);
/// reaching it here would need a guard violation staged through a real Foundry-shaped answer,
/// which this project's InMemory host cannot do (see <see cref="InMemoryAskEngineFactory"/>'s own
/// doc comment).
///
/// <para>
/// Same "InMemory EF Core + RecordingAiGateway, no Testcontainer" shape <see cref="ChatEndpointTests"/>
/// already establishes. Both scenarios below ask the same question, "When do our contracts
/// expire?" — <c>AskRaffaQueryRouter</c> classifies it <c>Structured</c> (the "expir" keyword,
/// never a legal/clause keyword), so <c>IntentPlanner</c> plans <c>AskIntent.StructuredFact</c>
/// rather than <c>AskIntent.Clause</c> (which would need pgvector search the InMemory provider
/// cannot run). Inside <c>AskCopilotService.BuildStructuredFactPackAsync</c>,
/// <c>DeterministicQueryPlanner</c> has no concrete handler for this phrasing (it is neither a
/// "next N days" window nor a "spend" question), so the deterministic query comes back
/// <c>Unsupported</c> and the method falls through to its own "honest portfolio snapshot"
/// fallback — the pack's only genuinely *empty* shape (every recognized deterministic query, by
/// contrast, always appends its own aggregate <c>PackItem</c> regardless of match count, so it is
/// never empty). That fallback is empty for a contract-free tenant and one item for a tenant with
/// a validated contract — exactly the two fixtures these tests need, with no dependency on "now"
/// or on any specific renewal date.
/// </para>
/// </summary>
public sealed class AskAbstainRecoveryActionTests : IClassFixture<RaffaApiFactory>
{
    private const string ExpiryQuestion = "When do our contracts expire?";

    private readonly WebApplicationFactory<Program> _factory;

    public AskAbstainRecoveryActionTests(RaffaApiFactory factory)
    {
        _factory = factory.WithPresentedCallersAsMembers().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:DocumentsContracts",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
            builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");
        });
    }

    /// <summary>AC-1/AC-3: the empty-pack abstain (`boundedPack.Count == 0`) for a tenant with zero
    /// validated contracts carries the Documents upload action — AC-2's real catalog href, never an
    /// empty `actions[]`.</summary>
    [Fact]
    public async Task Empty_pack_abstain_for_a_contract_free_tenant_offers_the_documents_upload_action()
    {
        var recordingGateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var client = _factory.WithInMemoryAskEngine(recordingGateway).CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query")
        {
            Content = JsonContent.Create(new { question = ExpiryQuestion }),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-User-Id", "alice@example.com");

        var response = await client.SendAsync(request);
        var rawBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(rawBody);
        Assert.Equal("abstain", body.RootElement.GetProperty("kind").GetString());

        var actions = body.RootElement.GetProperty("actions").EnumerateArray().ToList();
        var action = Assert.Single(actions);
        Assert.Equal("upload", action.GetProperty("kind").GetString());
        Assert.Equal("/documents", action.GetProperty("href").GetString());
        Assert.False(string.IsNullOrWhiteSpace(action.GetProperty("label").GetString()));

        // The empty-pack branch returns before AnswerComposer ever calls the gateway — same "zero
        // AI Gateway calls" proof ChatEndpointTests.Greeting_question... makes for the deterministic
        // redirect branches, here proven for the deterministic abstain branch instead.
        Assert.Empty(recordingGateway.Calls);
    }

    /// <summary>AC-1/AC-3: the composer-failure abstain (`composed.IsFailure`) for a tenant that
    /// already has a validated contract still carries a recovery action — here the Ask capability's
    /// own "ask about dates, spend, notice periods and clauses" hint, since Documents upload is not
    /// this tenant's unblocking step.</summary>
    [Fact]
    public async Task Composer_failure_abstain_for_a_tenant_with_a_contract_offers_the_ask_hint_action()
    {
        var failingGateway = new FailingAnswerAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var factory = _factory.WithInMemoryAskEngine(failingGateway);
        var tenantId = TenantId.New();

        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "Completed",
            Currency = "CHF",
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
            AutoRenewal = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query")
        {
            Content = JsonContent.Create(new { question = ExpiryQuestion }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", "alice@example.com");

        var response = await client.SendAsync(request);
        var rawBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(rawBody);
        Assert.Equal("abstain", body.RootElement.GetProperty("kind").GetString());
        Assert.Contains(
            "could not reach the answer service",
            body.RootElement.GetProperty("answerMarkdown").GetString(),
            StringComparison.Ordinal);

        var actions = body.RootElement.GetProperty("actions").EnumerateArray().ToList();
        var action = Assert.Single(actions);
        Assert.Equal("navigate", action.GetProperty("kind").GetString());
        Assert.Equal("/ask", action.GetProperty("href").GetString());
        Assert.False(string.IsNullOrWhiteSpace(action.GetProperty("label").GetString()));
    }

    /// <summary>Delegates every role except <c>AnswerAsync</c> to <paramref name="inner"/> —
    /// same "decorate, fail just one role" shape as
    /// <c>Raffa.IntegrationTests.AskRaffaRagCrossTenantIsolationTests.GuardViolatingAiGateway</c>,
    /// so a domain-gate/classify path this task did not touch still runs for real if some future
    /// change ever starts calling it for this question.</summary>
    private sealed class FailingAnswerAiGateway(IAiGateway inner) : IAiGateway
    {
        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            inner.ClassifyAsync(request, cancellationToken);

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            inner.ExtractAsync(request, cancellationToken);

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            inner.EmbedAsync(request, cancellationToken);

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<AiAnswerResult>.Failure("simulated Foundry outage."));

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            inner.OcrAsync(request, cancellationToken);
    }
}
