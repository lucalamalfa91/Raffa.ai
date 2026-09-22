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
using Raffa.SharedKernel.Suppliers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// The live "Where can I save the most this quarter?" turn, end to end over HTTP. It used to come
/// back as "I don't have data I trust enough to answer. actionKey 'raffa:renewals' does not resolve
/// to any capability in the catalog…" with an "Open Ask Raffa" button: the model had echoed a Raffa
/// feature item's citation key as its action key, the grounding guard failed the whole answer, and
/// the downgrade printed the guard's own words. Now the key is repaired and the answer stands, a
/// model that cannot be trusted twice still gets the pack's own facts as an answer, and a savings
/// answer always offers Savings and Renewals.
/// </summary>
public sealed class AskSavingsAnswerRecoveryTests(RaffaApiFactory factory) : IClassFixture<RaffaApiFactory>
{
    private const string UserId = "alice@example.com";
    private const string Question = "Where can I save the most this quarter?";

    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private WebApplicationFactory<Program> Host(IAiGateway gateway, IReadOnlyDictionary<EntityId, string> supplierNames) =>
        factory
            .WithPresentedCallersAsMembers()
            .WithWebHostBuilder(builder => builder.UseSetting(
                "ConnectionStrings:Chat",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true"))
            .WithInMemoryAskEngine(gateway, new FixedClock(Now))
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<ISupplierNameLookup>(new StubSupplierNameLookup(supplierNames))));

    private static FixtureAiGateway Fixture() =>
        new(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());

    private async Task<JsonDocument> AskSeededAsync(IAiGateway gateway, string question = Question, bool withAnnualSpend = true)
    {
        var tenantId = TenantId.New();
        var oracleId = EntityId.New();
        var host = Host(gateway, new Dictionary<EntityId, string> { [oracleId] = "Oracle" });

        var contract = new Contract
        {
            TenantId = tenantId,
            SupplierId = oracleId,
            Type = ContractDocumentType.OrderForm,
            Status = "Completed",
            Currency = "EUR",
            AnnualSpend = withAnnualSpend ? 439000m : null,
            AutoRenewal = true,
            EndDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(29 + 180),
            CancellationDeadline = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(29),
            RenewalTermMonths = 12,
            PaymentTerms = "Net 30",
            CreatedAt = Now,
        };
        await host.SeedContractAsync(contract);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = host.CreateClient();

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
        {
            Content = JsonContent.Create(new { scopeContractId = (string?)null }),
        };
        createRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        createRequest.Headers.Add("X-User-Id", UserId);
        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var conversationId = created.RootElement.GetProperty("id").GetGuid();

        using var messageRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversationId}/messages")
        {
            Content = JsonContent.Create(new { question }),
        };
        messageRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        messageRequest.Headers.Add("X-User-Id", UserId);

        var response = await client.SendAsync(messageRequest);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {raw}");

        return JsonDocument.Parse(raw);
    }

    private static List<string> ActionHrefs(JsonDocument reply) =>
        reply.RootElement.GetProperty("actions").EnumerateArray()
            .Select(a => a.GetProperty("href").GetString() ?? string.Empty)
            .ToList();

    [Fact]
    public async Task A_raffa_prefixed_action_key_keeps_the_answer_and_a_contract_360_key_without_a_contract_is_dropped()
    {
        var gateway = new RecordingAiGateway(new ActionKeyEchoingGateway(Fixture(), ["raffa:renewals", "contract-360"]));

        using var reply = await AskSeededAsync(gateway);

        Assert.Equal("answer", reply.RootElement.GetProperty("kind").GetString());
        Assert.Equal(1, gateway.Calls.Count(c => c == nameof(IAiGateway.AnswerAsync)));

        var hrefs = ActionHrefs(reply);
        Assert.Contains("/savings", hrefs);
        Assert.Contains("/renewals", hrefs);
        Assert.Equal("/savings", hrefs[0]);
        Assert.DoesNotContain(hrefs, href => href.StartsWith("/contracts/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_model_that_cannot_be_trusted_twice_still_gets_the_packs_own_facts_as_an_answer()
    {
        var gateway = new RecordingAiGateway(new FabricatingGateway(Fixture()));

        using var reply = await AskSeededAsync(gateway);

        Assert.Equal("answer", reply.RootElement.GetProperty("kind").GetString());
        Assert.Equal(2, gateway.Calls.Count(c => c == nameof(IAiGateway.AnswerAsync)));

        var markdown = reply.RootElement.GetProperty("answerMarkdown").GetString()!;
        Assert.StartsWith("Here's where you can save and what to negotiate", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(FabricatingGateway.FabricatedFigure, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("actionKey", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("R-SYS", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Showing the pack's own facts", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("calc:", markdown, StringComparison.Ordinal);

        Assert.NotEqual(0, reply.RootElement.GetProperty("citations").GetArrayLength());
        Assert.NotEqual(0, reply.RootElement.GetProperty("followUps").GetArrayLength());

        var hrefs = ActionHrefs(reply);
        Assert.Contains("/savings", hrefs);
        Assert.Contains("/renewals", hrefs);
        Assert.DoesNotContain("/ask", hrefs);
    }

    // Persona v2.4: the live screenshot declines ("Nel pack non risultano leve contrattuali o di
    // mercato attivabili entro la finestra di 90 giorni (candidatesInWindow=0)…") never reach the user.
    [Fact]
    public async Task A_model_that_declines_twice_gets_a_proposal_never_the_i_dont_have_data_banner()
    {
        var declining = new DecliningGateway(Fixture(), declines: 2);
        var gateway = new RecordingAiGateway(declining);

        using var reply = await AskSeededAsync(gateway);

        Assert.Equal(2, gateway.Calls.Count(c => c == nameof(IAiGateway.AnswerAsync)));

        var markdown = reply.RootElement.GetProperty("answerMarkdown").GetString()!;
        Assert.StartsWith("Here's where to start saving", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("I don't have data I trust", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(DecliningGateway.Reason, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("candidatesInWindow", markdown, StringComparison.Ordinal);

        var hrefs = ActionHrefs(reply);
        Assert.Contains("/savings", hrefs);
        Assert.Contains("/renewals", hrefs);
        Assert.NotEqual(0, reply.RootElement.GetProperty("followUps").GetArrayLength());
    }

    [Fact]
    public async Task A_decline_is_regenerated_and_the_retrys_answer_reaches_the_user()
    {
        var gateway = new RecordingAiGateway(new DecliningGateway(Fixture(), declines: 1));

        using var reply = await AskSeededAsync(gateway);

        Assert.Equal("answer", reply.RootElement.GetProperty("kind").GetString());
        Assert.Equal(2, gateway.Calls.Count(c => c == nameof(IAiGateway.AnswerAsync)));
        Assert.NotEqual(0, reply.RootElement.GetProperty("citations").GetArrayLength());
    }

    // "this quarter" gets today's date and the calendar quarter in the pack, so the model can
    // state the quarter's end (2026-09-30 for Now) as a pack value instead of declining.
    [Fact]
    public async Task A_question_about_this_quarter_sends_the_calendar_quarter_to_the_model()
    {
        var declining = new DecliningGateway(Fixture(), declines: 0);

        using var reply = await AskSeededAsync(new RecordingAiGateway(declining));

        var packJson = Assert.Single(declining.PackJsons);
        Assert.Contains("calc:calendar", packJson, StringComparison.Ordinal);
        Assert.Contains("2026-09-30", packJson, StringComparison.Ordinal);
    }

    // Persona v2.5's market safety net: a contract with no annual amounts is named as such, and the
    // model gets the market's narrow estimate for comparable Oracle customers in its place
    // (EUR 250,000–500,000, the band the mock feed's EUR Oracle deals mostly fall in).
    [Fact]
    public async Task A_contract_without_annual_amounts_sends_its_gap_and_the_markets_estimate_to_the_model()
    {
        var declining = new DecliningGateway(Fixture(), declines: 0);

        using var reply = await AskSeededAsync(
            new RecordingAiGateway(declining), "come posso negoziare il rinnovo del contratto Oracle?", withAnnualSpend: false);

        var packJson = Assert.Single(declining.PackJsons);
        Assert.Contains("calc:contract-gaps[", packJson, StringComparison.Ordinal);
        Assert.Contains("no value for: annual spend", packJson, StringComparison.Ordinal);
        Assert.Contains("Market estimate, not a figure from your contract", packJson, StringComparison.Ordinal);
        Assert.Contains("between EUR 250,000 and EUR 500,000", packJson, StringComparison.Ordinal);
        Assert.Contains("what comparable customers negotiated", packJson, StringComparison.Ordinal);

        // Step 2 of the flow: the market researcher's notes from the market RAG, annual value included.
        Assert.Contains("market-researcher", packJson, StringComparison.Ordinal);
        Assert.Contains("annual contract value EUR", packJson, StringComparison.Ordinal);
    }

    // A multi-contract question (a quarter, savings across contracts): the market data check runs
    // over the contracts the pack is about, so the Oracle gap reaches the model there too.
    [Fact]
    public async Task A_quarter_savings_question_checks_the_contracts_it_is_about_and_names_their_gaps()
    {
        var declining = new DecliningGateway(Fixture(), declines: 0);

        using var reply = await AskSeededAsync(new RecordingAiGateway(declining), Question, withAnnualSpend: false);

        var packJson = Assert.Single(declining.PackJsons);
        Assert.Contains("no value for: annual spend", packJson, StringComparison.Ordinal);
        Assert.Contains("Market estimate, not a figure from your contract", packJson, StringComparison.Ordinal);
    }

    // Both attempts decline: the deterministic reply still opens with the honest gap and the
    // market estimate, then the way forward for a renewal.
    [Fact]
    public async Task With_no_model_answer_the_proposal_still_leads_with_the_gap_and_the_market_estimate()
    {
        using var reply = await AskSeededAsync(
            new RecordingAiGateway(new DecliningGateway(Fixture(), declines: 2)),
            "come posso negoziare il rinnovo del contratto Oracle?",
            withAnnualSpend: false);

        var markdown = reply.RootElement.GetProperty("answerMarkdown").GetString()!;
        Assert.StartsWith("Sul contratto Oracle mancano gli importi annuali.", markdown, StringComparison.Ordinal);
        Assert.Contains("il valore annuo tipico è tra EUR 250,000 e EUR 500,000", markdown, StringComparison.Ordinal);
        Assert.Contains("è una stima, non un dato del tuo contratto", markdown, StringComparison.Ordinal);
        Assert.Contains("Ecco come preparare il rinnovo", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("I don't have data I trust", markdown, StringComparison.Ordinal);
    }

    /// <summary>Declines the first <c>declines</c> answer calls the way the live model did before
    /// persona v2.4 (a reason that talks about the pack), then answers like the fixture; records
    /// every call's pack.</summary>
    private sealed class DecliningGateway(IAiGateway inner, int declines) : IAiGateway
    {
        public const string Reason =
            "Nel pack non risultano leve contrattuali o di mercato attivabili entro la finestra di 90 giorni (candidatesInWindow=0).";

        private int _calls;

        public List<string> PackJsons { get; } = [];

        public async Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default)
        {
            PackJsons.Add(request.PackJson ?? string.Empty);
            var result = await inner.AnswerAsync(request, cancellationToken).ConfigureAwait(false);
            if (_calls++ >= declines || result.IsFailure)
            {
                return result;
            }

            return Result<AiAnswerResult>.Success(result.Value with
            {
                CanDetermine = false,
                Answer = null,
                AnswerMarkdown = null,
                CitationKeys = [],
                ActionKeys = [],
                AbstainReason = Reason,
                FollowUps = ["Puoi condividere l'estratto dell'Order dove sono indicate le date di disdetta?"],
            });
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            inner.ClassifyAsync(request, cancellationToken);

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            inner.ExtractAsync(request, cancellationToken);

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            inner.EmbedAsync(request, cancellationToken);

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            inner.OcrAsync(request, cancellationToken);

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
            inner.AnalyzeAsync(request, cancellationToken);
    }

    /// <summary>Returns every grounded answer with the given action keys, the way a live model
    /// following prompt v2.2's rule 7 answered — a citation key where a bare capability key belongs.</summary>
    private sealed class ActionKeyEchoingGateway(IAiGateway inner, IReadOnlyList<string> actionKeys) : IAiGateway
    {
        public async Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default)
        {
            var result = await inner.AnswerAsync(request, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess && result.Value.CanDetermine
                ? Result<AiAnswerResult>.Success(result.Value with { ActionKeys = actionKeys })
                : result;
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            inner.ClassifyAsync(request, cancellationToken);

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            inner.ExtractAsync(request, cancellationToken);

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            inner.EmbedAsync(request, cancellationToken);

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            inner.OcrAsync(request, cancellationToken);

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
            inner.AnalyzeAsync(request, cancellationToken);
    }

    /// <summary>Adds a figure no pack holds to every grounded answer, so both attempts fail the
    /// numeric guard.</summary>
    private sealed class FabricatingGateway(IAiGateway inner) : IAiGateway
    {
        public const string FabricatedFigure = "EUR 987654";

        public async Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default)
        {
            var result = await inner.AnswerAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure || !result.Value.CanDetermine)
            {
                return result;
            }

            var markdown = $"{result.Value.AnswerMarkdown} You can save {FabricatedFigure} this quarter.";
            return Result<AiAnswerResult>.Success(result.Value with { AnswerMarkdown = markdown, Answer = markdown });
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            inner.ClassifyAsync(request, cancellationToken);

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            inner.ExtractAsync(request, cancellationToken);

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            inner.EmbedAsync(request, cancellationToken);

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            inner.OcrAsync(request, cancellationToken);

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
            inner.AnalyzeAsync(request, cancellationToken);
    }
}
