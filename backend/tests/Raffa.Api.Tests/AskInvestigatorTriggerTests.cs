using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Chat.Application.Gaps;
using Raffa.Chat.Application.Reply;
using Raffa.Documents.Contracts.Domain;
using Raffa.Renewals.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Raffa.Api.Tests;

/// <summary>
/// INV-02, INV-03, INV-05 (decision D3): the capability investigator runs <c>Triggered</c> by
/// default. T3 (an operational request) starts it before the answer, in parallel with it; T1 (no
/// intent) and T2 (Raffa could not answer) start it after the reply is built, and its follow-up is
/// still a separate message. <c>Always</c> keeps the old call-on-every-turn behaviour for
/// diagnosis, the kill switch ends it, and a mode change applies without a restart. Every turn
/// leaves an <c>ask.capability_trigger</c> audit row, every started check an
/// <c>ask.capability_outcome</c> row — never any text.
/// </summary>
public sealed class AskInvestigatorTriggerTests(RaffaApiFactory factory) : IClassFixture<RaffaApiFactory>
{
    private const string UserId = "alice@example.com";

    private const string ReportRequest = "puoi scrivere un report per riportare l'anamento del 2026 al CFO?";

    private const string OrdinaryQuestion = "quali leve posso usare per risparmiare 20 k sul rinnovo";

    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private (WebApplicationFactory<Program> Host, RecordingAiGateway Gateway, RecordingAuditWriter Audit, StaticGapInvestigationOptions Options) Host(
        GapInvestigationOptions? options = null,
        Func<IAiGateway, IAiGateway>? wrapInvestigator = null)
    {
        IAiGateway fixture = new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());
        var gateway = new RecordingAiGateway(wrapInvestigator?.Invoke(fixture) ?? fixture);
        var audit = new RecordingAuditWriter();
        var monitor = new StaticGapInvestigationOptions(options ?? new GapInvestigationOptions());
        var amazonId = AmazonId;

        var host = factory
            .WithPresentedCallersAsMembers()
            .WithWebHostBuilder(builder => builder.UseSetting(
                "ConnectionStrings:Chat",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true"))
            .WithInMemoryAskEngine(gateway, new FixedClock(Now), auditWriter: audit)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISupplierNameLookup>(
                    new StubSupplierNameLookup(new Dictionary<EntityId, string> { [amazonId] = "Amazon Web Services" }));
                services.AddSingleton<IOptionsMonitor<GapInvestigationOptions>>(monitor);
            }));

        return (host, gateway, audit, monitor);
    }

    private static readonly EntityId AmazonId = EntityId.New();

    private static Contract AmazonContract(TenantId tenantId) => new()
    {
        TenantId = tenantId,
        SupplierId = AmazonId,
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

    private static async Task<Contract> SeedAsync(WebApplicationFactory<Program> host, TenantId tenantId)
    {
        var contract = AmazonContract(tenantId);
        await host.SeedContractAsync(contract);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));
        return contract;
    }

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
        HttpClient client, TenantId tenantId, string question, Guid? scopeContractId = null, Guid? conversationId = null)
    {
        if (conversationId is null)
        {
            using var createRequest = Request(HttpMethod.Post, "/api/conversations", tenantId, new { scopeContractId = scopeContractId?.ToString() });
            var createResponse = await client.SendAsync(createRequest);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            conversationId = created.RootElement.GetProperty("id").GetGuid();
        }

        using var messageRequest = Request(HttpMethod.Post, $"/api/conversations/{conversationId}/messages", tenantId, new { question });
        var response = await client.SendAsync(messageRequest);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {raw}");

        return (conversationId.Value, JsonDocument.Parse(raw), raw);
    }

    private static IReadOnlyList<AuditEntry> CapabilityRows(RecordingAuditWriter audit, string action) =>
        audit.Entries.Where(e => e.Action == action).ToList();

    private static string Field(AuditEntry entry, string name)
    {
        var token = (entry.Detail ?? string.Empty).Split(' ').FirstOrDefault(part => part.StartsWith(name + "=", StringComparison.Ordinal));
        return token is null
            ? throw new Xunit.Sdk.XunitException($"'{name}' is missing from: {entry.Detail}")
            : token[(name.Length + 1)..];
    }

    private static void AssertNoQuestionTextInAudit(RecordingAuditWriter audit, params string[] fragments)
    {
        foreach (var entry in audit.Entries.Where(e => e.Action.StartsWith("ask.capability_", StringComparison.Ordinal)))
        {
            foreach (var fragment in fragments)
            {
                Assert.DoesNotContain(fragment, entry.Detail, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ----- Triggered (default) -----

    [Fact]
    public async Task An_ordinary_answered_question_costs_no_investigator_call_and_is_audited_as_not_triggered()
    {
        var tenantId = TenantId.New();
        var (host, gateway, audit, _) = Host();
        var contract = await SeedAsync(host, tenantId);

        var (_, reply, _) = await AskAsync(host.CreateClient(), tenantId, OrdinaryQuestion, contract.Id.Value);

        Assert.Equal("answer", reply.RootElement.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, reply.RootElement.GetProperty("capabilityCheck").ValueKind);
        Assert.Equal(0, gateway.CapabilityChecks);

        var trigger = Assert.Single(CapabilityRows(audit, CapabilityCheckDispatcher.TriggerAuditAction));
        Assert.Equal("Triggered", Field(trigger, "mode"));
        Assert.Equal("False", Field(trigger, "t1"));
        Assert.Equal("False", Field(trigger, "t2"));
        Assert.Equal("False", Field(trigger, "t3"));
        Assert.Equal("False", Field(trigger, "ran"));
        Assert.Equal("none", Field(trigger, "reason"));
        Assert.Empty(CapabilityRows(audit, CapabilityCheckDispatcher.OutcomeAuditAction));

        var turn = Assert.Single(audit.Entries, e => e.Action.StartsWith("chat.", StringComparison.Ordinal));
        Assert.Contains("gapInvestigation=not-triggered", turn.Detail, StringComparison.Ordinal);
        AssertNoQuestionTextInAudit(audit, "leve", "risparmiare", "rinnovo");
    }

    [Fact]
    public async Task An_operational_request_starts_the_investigator_beside_the_answer_and_every_audit_row_shares_the_turn_id()
    {
        var tenantId = TenantId.New();
        var (host, gateway, audit, _) = Host();
        await SeedAsync(host, tenantId);

        var (conversationId, reply, _) = await AskAsync(host.CreateClient(), tenantId, ReportRequest);

        // T3 starts the check before the answer, as ADR-031 always did: the fixture finished first.
        Assert.Equal(1, gateway.CapabilityChecks);
        Assert.Equal(JsonValueKind.Null, reply.RootElement.GetProperty("capabilityCheck").ValueKind);
        var followUp = reply.RootElement.GetProperty("followUpMessage");
        Assert.Equal("discovered:management-report", followUp.GetProperty("payload").GetProperty("gap").GetProperty("key").GetString());

        var trigger = Assert.Single(CapabilityRows(audit, CapabilityCheckDispatcher.TriggerAuditAction));
        Assert.Equal("True", Field(trigger, "t3"));
        Assert.Equal("True", Field(trigger, "ran"));
        Assert.Equal("it", Field(trigger, "t3Language"));
        Assert.Contains(InvestigatorTrigger.ReasonOperationalRequest, Field(trigger, "reason"), StringComparison.Ordinal);
        Assert.Matches(@"^\d+\.\d+\.\d+$", Field(trigger, "lexicon"));

        var outcome = Assert.Single(CapabilityRows(audit, CapabilityCheckDispatcher.OutcomeAuditAction));
        Assert.Equal("gap", Field(outcome, "outcome"));
        Assert.Equal("high", Field(outcome, "confidence"));
        Assert.Equal("discovered:management-report", Field(outcome, "gapKey"));

        var offered = Assert.Single(CapabilityRows(audit, CapabilityCheckDispatcher.AuditAction));
        var turnId = Field(trigger, "turnId");
        Assert.Matches("^[0-9a-f]{16}$", turnId);
        Assert.Equal(turnId, Field(outcome, "turnId"));
        Assert.Equal(turnId, Field(offered, "turnId"));

        AssertNoQuestionTextInAudit(audit, "CFO", "anamento", "2026", "puoi", "scrivere");

        // Resume: question, answer, follow-up — the follow-up is a separate, later message.
        using var getRequest = Request(HttpMethod.Get, $"/api/conversations/{conversationId}", tenantId);
        using var detail = JsonDocument.Parse(await (await host.CreateClient().SendAsync(getRequest)).Content.ReadAsStringAsync());
        Assert.Equal(["you", "raffa", "raffa"], detail.RootElement.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("role").GetString()).ToList());
    }

    [Fact]
    public async Task A_turn_Raffa_could_not_answer_starts_the_investigator_after_the_reply_and_the_follow_up_stays_separate()
    {
        var tenantId = TenantId.New();

        // The check is slow, so it cannot have finished with the answer: the reply is returned
        // "pending" and the proposal is appended once it completes. (An empty workspace: the
        // question abstains, trigger T2.)
        var (host, gateway, audit, _) = Host(wrapInvestigator: inner => new ScriptedInvestigatorGateway(inner, GapPayload(), TimeSpan.FromMilliseconds(600)));
        var client = host.CreateClient();

        var (conversationId, reply, _) = await AskAsync(client, tenantId, "When do our contracts expire?");

        Assert.Equal("abstain", reply.RootElement.GetProperty("kind").GetString());
        Assert.Equal("pending", reply.RootElement.GetProperty("capabilityCheck").GetString());
        Assert.Equal(JsonValueKind.Null, reply.RootElement.GetProperty("followUpMessage").ValueKind);

        // The decision was made after the reply: the trigger row says T2, not T3.
        var trigger = Assert.Single(CapabilityRows(audit, CapabilityCheckDispatcher.TriggerAuditAction));
        Assert.Equal("True", Field(trigger, "t2"));
        Assert.Equal("False", Field(trigger, "t3"));
        Assert.Equal("True", Field(trigger, "ran"));
        Assert.Equal(InvestigatorTrigger.ReasonAbstain, Field(trigger, "reason"));

        await host.Services.GetRequiredService<CapabilityCheckDispatcher>().WhenIdleAsync();

        using var getRequest = Request(HttpMethod.Get, $"/api/conversations/{conversationId}", tenantId);
        using var detail = JsonDocument.Parse(await (await client.SendAsync(getRequest)).Content.ReadAsStringAsync());
        var messages = detail.RootElement.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(["you", "raffa", "raffa"], messages.Select(m => m.GetProperty("role").GetString()).ToList());
        Assert.Equal(
            reply.RootElement.GetProperty("messageId").GetGuid().ToString(),
            messages[2].GetProperty("payload").GetProperty("capabilityCheckFor").GetString());
        Assert.Equal(1, gateway.CapabilityChecks);

        var outcome = Assert.Single(CapabilityRows(audit, CapabilityCheckDispatcher.OutcomeAuditAction));
        Assert.Equal("gap", Field(outcome, "outcome"));
        Assert.Equal(Field(trigger, "turnId"), Field(outcome, "turnId"));
    }

    [Fact]
    public async Task A_turn_with_no_recognised_intent_starts_the_investigator_after_the_reply()
    {
        var tenantId = TenantId.New();
        var (host, gateway, audit, _) = Host();
        await SeedAsync(host, tenantId);

        var (_, reply, _) = await AskAsync(host.CreateClient(), tenantId, "Did you over all my contract?");

        Assert.Equal("interview", reply.RootElement.GetProperty("kind").GetString());
        Assert.Equal(1, gateway.CapabilityChecks);

        var trigger = Assert.Single(CapabilityRows(audit, CapabilityCheckDispatcher.TriggerAuditAction));
        Assert.Equal("True", Field(trigger, "t1"));
        Assert.Equal("False", Field(trigger, "t3"));
        Assert.Equal(InvestigatorTrigger.ReasonNoIntent, Field(trigger, "reason"));
        await host.Services.GetRequiredService<CapabilityCheckDispatcher>().WhenIdleAsync();
        Assert.Equal("question", Field(Assert.Single(CapabilityRows(audit, CapabilityCheckDispatcher.OutcomeAuditAction)), "outcome"));
    }

    // ----- Always, kill switch, live reconfiguration -----

    [Fact]
    public async Task Always_mode_runs_the_investigator_on_an_ordinary_turn_and_audits_what_Triggered_would_have_done()
    {
        var tenantId = TenantId.New();
        var (host, gateway, audit, _) = Host(new GapInvestigationOptions { Mode = GapInvestigationMode.Always });
        var contract = await SeedAsync(host, tenantId);

        var (_, reply, _) = await AskAsync(host.CreateClient(), tenantId, OrdinaryQuestion, contract.Id.Value);
        await host.Services.GetRequiredService<CapabilityCheckDispatcher>().WhenIdleAsync();

        Assert.Equal("answer", reply.RootElement.GetProperty("kind").GetString());
        Assert.Equal(1, gateway.CapabilityChecks);

        var trigger = Assert.Single(CapabilityRows(audit, CapabilityCheckDispatcher.TriggerAuditAction));
        Assert.Equal("Always", Field(trigger, "mode"));
        Assert.Equal("True", Field(trigger, "ran"));
        Assert.Equal("False", Field(trigger, "t1"));
        Assert.Equal("False", Field(trigger, "t2"));
        Assert.Equal("False", Field(trigger, "t3"));
        Assert.Equal("question", Field(Assert.Single(CapabilityRows(audit, CapabilityCheckDispatcher.OutcomeAuditAction)), "outcome"));
        Assert.Contains("gapInvestigation=started", Assert.Single(audit.Entries, e => e.Action.StartsWith("chat.", StringComparison.Ordinal)).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_kill_switch_ends_every_check_and_is_audited_without_a_call()
    {
        var tenantId = TenantId.New();
        var (host, gateway, audit, _) = Host(new GapInvestigationOptions { Enabled = false, Mode = GapInvestigationMode.Always });
        await SeedAsync(host, tenantId);

        var (_, reply, _) = await AskAsync(host.CreateClient(), tenantId, ReportRequest);

        Assert.Equal(0, gateway.CapabilityChecks);
        Assert.Equal(JsonValueKind.Null, reply.RootElement.GetProperty("followUpMessage").ValueKind);
        var trigger = Assert.Single(CapabilityRows(audit, CapabilityCheckDispatcher.TriggerAuditAction));
        Assert.Equal("off", Field(trigger, "mode"));
        Assert.Equal("False", Field(trigger, "ran"));
        Assert.Equal(InvestigatorTrigger.ReasonKillSwitch, Field(trigger, "reason"));
        Assert.Empty(CapabilityRows(audit, CapabilityCheckDispatcher.OutcomeAuditAction));
    }

    [Fact]
    public async Task Mode_and_kill_switch_changes_apply_to_the_next_turn_without_a_restart()
    {
        var tenantId = TenantId.New();
        var (host, gateway, audit, options) = Host();
        var contract = await SeedAsync(host, tenantId);
        var client = host.CreateClient();
        var dispatcher = host.Services.GetRequiredService<CapabilityCheckDispatcher>();

        await AskAsync(client, tenantId, OrdinaryQuestion, contract.Id.Value);
        Assert.Equal(0, gateway.CapabilityChecks);

        options.Set(new GapInvestigationOptions { Mode = GapInvestigationMode.Always });
        await AskAsync(client, tenantId, OrdinaryQuestion, contract.Id.Value);
        await dispatcher.WhenIdleAsync();
        Assert.Equal(1, gateway.CapabilityChecks);

        options.Set(new GapInvestigationOptions { Mode = GapInvestigationMode.Always, Enabled = false });
        await AskAsync(client, tenantId, OrdinaryQuestion, contract.Id.Value);
        await dispatcher.WhenIdleAsync();
        Assert.Equal(1, gateway.CapabilityChecks);

        options.Set(new GapInvestigationOptions());
        await AskAsync(client, tenantId, OrdinaryQuestion, contract.Id.Value);
        Assert.Equal(1, gateway.CapabilityChecks);

        Assert.Equal(
            ["Triggered", "Always", "off", "Triggered"],
            CapabilityRows(audit, CapabilityCheckDispatcher.TriggerAuditAction).Select(e => Field(e, "mode")).ToList());
    }

    // ----- INV-05: every outcome in the audit -----

    public static TheoryData<string, string, string, string> Outcomes => new()
    {
        // payload kind, expected outcome, expected confidence, expected gapKey
        { "question", "question", "high", "none" },
        { "supported", "supported", "high", "none" },
        { "known-gap", "known-gap", "high", "export-file" },
        { "gap", "gap", "high", "discovered:management-report" },
        { "low-confidence", "low-confidence", "low", "none" },
        { "unusable", "unusable", "high", "none" },
        { "failed", "failed", "none", "none" },
        { "timeout", "timeout", "none", "none" },
    };

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task Every_outcome_of_a_started_check_is_audited_with_its_confidence_gap_key_and_turn_id(
        string kind, string outcomeWanted, string confidenceWanted, string gapKeyWanted)
    {
        var tenantId = TenantId.New();
        var payload = kind switch
        {
            "question" => VerdictPayload("question", "high"),
            "supported" => VerdictPayload("supported", "high"),
            "known-gap" => VerdictPayload("known-gap", "high", knownGapKey: "export-file"),
            "gap" => GapPayload(),
            "low-confidence" => GapPayload(confidence: "low"),
            "unusable" => VerdictPayload("gap", "high"), // a gap whose feature texts are empty
            _ => null,
        };
        var delay = kind == "timeout" ? TimeSpan.FromSeconds(5) : (TimeSpan?)null;
        var (host, _, audit, _) = Host(
            new GapInvestigationOptions { Mode = GapInvestigationMode.Always, TimeoutSeconds = 1 },
            inner => new ScriptedInvestigatorGateway(inner, payload, delay));
        await SeedAsync(host, tenantId);

        await AskAsync(host.CreateClient(), tenantId, ReportRequest);
        await host.Services.GetRequiredService<CapabilityCheckDispatcher>().WhenIdleAsync();

        var outcome = Assert.Single(CapabilityRows(audit, CapabilityCheckDispatcher.OutcomeAuditAction));
        Assert.Equal(outcomeWanted, Field(outcome, "outcome"));
        Assert.Equal(confidenceWanted, Field(outcome, "confidence"));
        Assert.Equal(gapKeyWanted, Field(outcome, "gapKey"));
        Assert.Equal(Field(Assert.Single(CapabilityRows(audit, CapabilityCheckDispatcher.TriggerAuditAction)), "turnId"), Field(outcome, "turnId"));
        AssertNoQuestionTextInAudit(audit, "CFO", "anamento", "2026", "puoi", "scrivere", "periodic");
    }

    [Fact]
    public async Task A_proposal_that_arrives_after_the_conversation_moved_on_is_audited_as_a_drop()
    {
        var tenantId = TenantId.New();
        var (host, _, audit, _) = Host(wrapInvestigator: inner => new ScriptedInvestigatorGateway(inner, GapPayload(), TimeSpan.FromMilliseconds(800)));
        await SeedAsync(host, tenantId);
        var client = host.CreateClient();

        var (conversationId, first, _) = await AskAsync(client, tenantId, ReportRequest);
        Assert.Equal("pending", first.RootElement.GetProperty("capabilityCheck").GetString());

        await AskAsync(client, tenantId, "Did you over all my contract?", conversationId: conversationId);
        await host.Services.GetRequiredService<CapabilityCheckDispatcher>().WhenIdleAsync();

        // The first turn's check found a gap (outcome row) and then dropped it (second row), both
        // on that turn's id; no follow-up was appended after the first answer.
        var firstTurnId = Field(CapabilityRows(audit, CapabilityCheckDispatcher.TriggerAuditAction)[0], "turnId");
        var firstOutcomes = CapabilityRows(audit, CapabilityCheckDispatcher.OutcomeAuditAction)
            .Where(e => Field(e, "turnId") == firstTurnId)
            .Select(e => Field(e, "outcome"))
            .ToList();
        Assert.Contains(CapabilityCheckDispatcher.OutcomeDrop, firstOutcomes);
        Assert.Contains("gap", firstOutcomes);

        var firstAnswerId = first.RootElement.GetProperty("messageId").GetGuid().ToString();
        Assert.DoesNotContain(
            CapabilityRows(audit, CapabilityCheckDispatcher.AuditAction),
            e => (e.Detail ?? string.Empty).Contains($"answeredMessageId={firstAnswerId}", StringComparison.Ordinal));
    }

    // ----- F1-D07: the drafted email writes nothing -----

    [Fact]
    public async Task A_drafted_email_persists_no_negotiation_todo()
    {
        var tenantId = TenantId.New();
        var (host, _, _, _) = Host();
        var contract = AmazonContract(tenantId);
        contract.CancellationDeadline = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(30);
        contract.EndDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(90);
        await host.SeedContractAsync(contract);
        await host.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        // The same contract, the same short-notice point a live Q3 turn would upsert: it exists
        // for the renewal-strategy question (positive control) and not for the drafted email.
        var (_, draft, _) = await AskAsync(
            host.CreateClient(), tenantId,
            "I have to renegotiate with Amazon Web Services. Can you help me create an email based on the negotiation leverage?",
            contract.Id.Value);
        Assert.Equal("draft", draft.RootElement.GetProperty("kind").GetString());

        Assert.Empty(await GetTodosAsync(host, tenantId, contract.Id));

        await AskAsync(host.CreateClient(), tenantId, "What should we negotiate before the Amazon Web Services renewal?", contract.Id.Value);
        Assert.NotEmpty(await GetTodosAsync(host, tenantId, contract.Id));
    }

    private static async Task<IReadOnlyList<RenewalNegotiationTodoResult>> GetTodosAsync(
        WebApplicationFactory<Program> host, TenantId tenantId, EntityId contractId)
    {
        using var scope = host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RenewalNegotiationTodoService>();
        return await service.GetAsync(tenantId, contractId, CancellationToken.None);
    }

    // ----- scripted investigator -----

    private static string VerdictPayload(string verdict, string confidence, string knownGapKey = "") =>
        JsonSerializer.Serialize(new
        {
            rationale = "scripted",
            verdict,
            confidence,
            knownGapKey,
            nearestCapabilityKey = string.Empty,
            feature = new { key = string.Empty, titleEn = string.Empty, titleIt = string.Empty, operationEn = string.Empty, operationIt = string.Empty, descriptionEn = string.Empty, descriptionIt = string.Empty },
            alternativeQuestions = Array.Empty<string>(),
        });

    private static string GapPayload(string confidence = "high") =>
        JsonSerializer.Serialize(new
        {
            rationale = "scripted",
            verdict = "gap",
            confidence,
            knownGapKey = string.Empty,
            nearestCapabilityKey = "portfolio",
            feature = new
            {
                key = "management-report",
                titleEn = "Management reports",
                titleIt = "Report per il management",
                operationEn = "generate a report for management",
                operationIt = "generare un report per il management",
                descriptionEn = "Generate a periodic report on contracts, spend and savings, ready to share with management.",
                descriptionIt = "Generare un report periodico su contratti, spesa e risparmi, pronto da condividere con il management.",
            },
            alternativeQuestions = new[] { "Qual è la spesa annuale totale dei contratti?" },
        });

    /// <summary>Answers only the capability investigator with a fixed payload (or a failure when
    /// the payload is null), after an optional delay; everything else goes to the fixture.</summary>
    private sealed class ScriptedInvestigatorGateway(IAiGateway inner, string? payload, TimeSpan? delay = null) : IAiGateway
    {
        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => inner.ClassifyAsync(request, cancellationToken);
        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => inner.ExtractAsync(request, cancellationToken);
        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => inner.EmbedAsync(request, cancellationToken);
        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => inner.AnswerAsync(request, cancellationToken);
        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => inner.OcrAsync(request, cancellationToken);
        public Task<Result<AiResearchResult>> ResearchAsync(AiResearchRequest request, CancellationToken cancellationToken = default) => inner.ResearchAsync(request, cancellationToken);

        public async Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            if (request.AgentName != RecordingAiGateway.CapabilityInvestigatorAgent)
            {
                return await inner.AnalyzeAsync(request, cancellationToken);
            }

            if (delay is { } wait)
            {
                await Task.Delay(wait, cancellationToken);
            }

            return payload is null
                ? Result<AiAnalysisResult>.Failure("simulated outage")
                : Result<AiAnalysisResult>.Success(new AiAnalysisResult(
                    payload, new AiCallMetadata("scripted", "1", request.PromptVersion, DateTimeOffset.UtcNow, "hash")));
        }
    }
}
