using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.Api.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
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
/// F1 Ask split PR-9/10/11, end to end over the conversation endpoints with the fixture gateway: the
/// <c>Chat:AskTurn</c> switches off are today's turn; the budget-before-agents, play revalidation and
/// memoization switches on give the same reply for a pack that fits; a turn cap that fires degrades
/// explicitly (an agent phase) or ends in the existing service-busy abstain (an answer phase).
/// </summary>
public sealed class AskTurnOptionsTests(RaffaApiFactory factory) : IClassFixture<RaffaApiFactory>
{
    private const string UserId = "alice@example.com";
    private const string SavingsQuestion = "which levers can I use to save 20k on the renewal?";

    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The fixture gateway, except it never answers the agents in <c>hangAnalysts</c> (and, when
    /// <c>hangAnswer</c>, the answer role) until the token it is handed is cancelled; every answer
    /// request's pack is kept.</summary>
    private sealed class SlowGateway(IAiGateway inner, bool hangAnswer, params string[] hangAnalysts) : IAiGateway
    {
        public List<string?> AnswerPacks { get; } = [];

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            inner.ClassifyAsync(request, cancellationToken);

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            inner.ExtractAsync(request, cancellationToken);

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            inner.EmbedAsync(request, cancellationToken);

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            inner.OcrAsync(request, cancellationToken);

        public async Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default)
        {
            lock (AnswerPacks)
            {
                AnswerPacks.Add(request.PackJson);
            }

            if (hangAnswer)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return await inner.AnswerAsync(request, cancellationToken);
        }

        public async Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            if (hangAnalysts.Contains(request.AgentName))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return await inner.AnalyzeAsync(request, cancellationToken);
        }
    }

    private static FixtureAiGateway Fixture() =>
        new(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());

    private WebApplicationFactory<Program> Host(IAiGateway gateway, RecordingAuditWriter audit, AskTurnOptions? options, EntityId supplierId)
    {
        var host = factory
            .WithPresentedCallersAsMembers()
            .WithWebHostBuilder(builder => builder.UseSetting(
                "ConnectionStrings:Chat",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true"))
            .WithInMemoryAskEngine(gateway, new FixedClock(Now), auditWriter: audit)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISupplierNameLookup>(new StubSupplierNameLookup(new Dictionary<EntityId, string> { [supplierId] = "ServiceNow" }));
                if (options is not null)
                {
                    services.RemoveAll<AskTurnOptions>();
                    services.AddSingleton(options);
                }
            }));

        return host;
    }

    private static Contract ServiceNowContract(TenantId tenantId, EntityId supplierId) => new()
    {
        TenantId = tenantId,
        SupplierId = supplierId,
        Type = ContractDocumentType.OrderForm,
        Status = "Completed",
        Currency = "EUR",
        AnnualSpend = 230000m,
        AutoRenewal = true,
        EndDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(300),
        CancellationDeadline = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(120),
        RenewalTermMonths = 12,
        PaymentTerms = "Net 30",
        CreatedAt = Now,
    };

    /// <summary>One scoped savings turn on a fresh host; returns the raw reply, the turn's audit detail and the gateway.</summary>
    private async Task<(string Raw, JsonDocument Reply, string Detail)> AskSavingsAsync(IAiGateway gateway, AskTurnOptions? options)
    {
        var tenantId = TenantId.New();
        var supplierId = EntityId.New();
        var audit = new RecordingAuditWriter();
        var host = Host(gateway, audit, options, supplierId);

        var contract = ServiceNowContract(tenantId, supplierId);
        await host.SeedContractAsync(contract);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = host.CreateClient();
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
        {
            Content = JsonContent.Create(new { scopeContractId = contract.Id.Value.ToString() }),
        };
        createRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        createRequest.Headers.Add("X-User-Id", UserId);
        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var conversationId = created.RootElement.GetProperty("id").GetGuid();

        using var messageRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversationId}/messages")
        {
            Content = JsonContent.Create(new { question = SavingsQuestion }),
        };
        messageRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        messageRequest.Headers.Add("X-User-Id", UserId);
        var response = await client.SendAsync(messageRequest);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {raw}");

        await host.Services.GetRequiredService<CapabilityFollowUpAppender>().WhenIdleAsync();

        var turn = Assert.Single(audit.Entries, e => e.ResourceType == "ask_raffa_v2");
        return (raw, JsonDocument.Parse(raw), turn.Detail!);
    }

    /// <summary>The reply with its per-run identity removed (guids, 16-hex ids, timestamps).</summary>
    private static string Normalize(string raw) =>
        Regex.Replace(
            Regex.Replace(
                Regex.Replace(raw, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "<guid>"),
                @"\b[0-9a-f]{16}\b", "<id>"),
            @"\d{4}-\d{2}-\d{2}T[\d:.]+(Z|[+-]\d{2}:\d{2})?", "<ts>");

    // ---------------------------------------------------------------- parity: off, and every switch on

    [Fact]
    public async Task Every_switch_on_gives_the_same_savings_reply_and_the_only_audit_difference_is_the_added_fragments()
    {
        var off = await AskSavingsAsync(Fixture(), options: null);
        var on = await AskSavingsAsync(
            Fixture(),
            new AskTurnOptions
            {
                BudgetBeforeAgents = true,
                AgentPackReserveTokens = 1500,
                RevalidatePlaysAfterBudget = true,
                MemoizeTurnReads = true,
                TurnDeadline = TimeSpan.FromMinutes(5),
                AgentPhaseDeadline = TimeSpan.FromMinutes(5),
                AnswerDeadline = TimeSpan.FromMinutes(5),
            });

        Assert.Equal("answer", off.Reply.RootElement.GetProperty("kind").GetString());
        Assert.Equal(Normalize(off.Raw), Normalize(on.Raw));

        // Off: the row is exactly today's. On: the same row plus the new fragments, none of them a decision.
        Assert.DoesNotContain("budgetBeforeAgents", off.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("deadline=", off.Detail, StringComparison.Ordinal);
        Assert.Contains("budgetBeforeAgents=on budgetDroppedBeforeAgents=0 revalidatePlays=on playsUngrounded=0", on.Detail, StringComparison.Ordinal);
        Assert.Matches(@"\bdeadline=none turnMs=\d+", on.Detail);
        var withoutFragments = Regex.Replace(
            Regex.Replace(on.Detail, @" budgetBeforeAgents=\S+ budgetDroppedBeforeAgents=\d+ revalidatePlays=\S+ playsUngrounded=\d+", string.Empty),
            @" deadline=\S+ turnMs=\d+", string.Empty);
        Assert.Equal(Normalize(off.Detail), Normalize(withoutFragments));
    }

    // ---------------------------------------------------------------- A-04: the caps

    [Fact]
    public async Task An_agent_phase_cap_degrades_explicitly_and_the_answer_still_runs_on_the_contracts_own_data()
    {
        var gateway = new SlowGateway(Fixture(), hangAnswer: false, "contract-analyst", "market-analyst");

        var (_, reply, detail) = await AskSavingsAsync(gateway, new AskTurnOptions { AgentPhaseDeadline = TimeSpan.FromMilliseconds(400) });

        Assert.Equal("answer", reply.RootElement.GetProperty("kind").GetString());
        Assert.Matches(@"\bdeadline=agents turnMs=\d+", detail);
        Assert.Contains("failedSteps=", detail, StringComparison.Ordinal);
        Assert.Contains("deadline", Regex.Match(detail, @"failedSteps=(\S+)").Groups[1].Value, StringComparison.Ordinal);
        // The composer was shown the explicit gap, not a silently shorter pack.
        var pack = Assert.Single(gateway.AnswerPacks);
        Assert.Contains("calc:deadline:market-analysis", pack, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_phase_cap_ends_in_the_service_busy_abstain_never_a_truncated_answer()
    {
        var gateway = new SlowGateway(Fixture(), hangAnswer: true);

        var (raw, reply, detail) = await AskSavingsAsync(gateway, new AskTurnOptions { AnswerDeadline = TimeSpan.FromMilliseconds(400) });

        Assert.Equal("abstain", reply.RootElement.GetProperty("kind").GetString());
        Assert.Contains("taking longer than expected", raw, StringComparison.Ordinal);
        Assert.Matches(@"\bdeadline=answer turnMs=\d+", detail);
    }

    [Fact]
    public async Task Default_options_leave_the_turn_row_without_deadline_or_budget_fragments()
    {
        // The default object: no cap and no extra fragment on the row.
        var (_, reply, detail) = await AskSavingsAsync(Fixture(), new AskTurnOptions());

        Assert.Equal("answer", reply.RootElement.GetProperty("kind").GetString());
        Assert.DoesNotContain("deadline=", detail, StringComparison.Ordinal);
    }
}
