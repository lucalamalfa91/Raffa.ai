using System.Text.Json;
using Raffa.AiFlows.ContractSituation;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;

namespace Raffa.AiFlows.Tests.ContractSituation;

/// <summary>The situation assessment: the model writes the judgement, the guard keeps it to the
/// deterministic facts, the cache keeps Oggi from calling the model on every load.</summary>
public sealed class ContractSituationAssessorTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);
    private static readonly TenantId Tenant = new(Guid.Parse("4a5ec0de-a4a4-4f00-8000-0000000000aa"));

    /// <summary>Splunk: notice deadline 07 Jul 2026, ends 06 Aug 2026, renews for 12 months.</summary>
    private static readonly ContractFacts Splunk = new()
    {
        ContractId = Guid.Parse("5b1e0000-0000-0000-0000-000000000001"),
        Supplier = "Splunk",
        Name = "Splunk Enterprise — Order Form",
        File = "Splunk_Order_2025.pdf",
        StartDate = new DateOnly(2025, 8, 7),
        EndDate = new DateOnly(2026, 8, 6),
        NoticeDeadline = new DateOnly(2026, 7, 7),
        Notice = 30,
        Auto = "Yes",
        RenewalTermMonths = 12,
        Evidence = new Dictionary<string, ContractEvidence>
        {
            ["sp_renew"] = new("Splunk_Order_2025.pdf", 4, "9.2", "This Order ", "renews automatically for successive 12-month terms", " unless notice is given."),
        },
    };

    private static (ContractSituationFacts Facts, ContractAssessment Rules) Guard(ContractFacts facts)
    {
        var term = ContractTermCalculator.Compute(facts, Today);
        var rules = ContractSituationCopy.Rules(facts, term);
        return (AiContractSituationAssessor.BuildInput(facts, term, rules).GuardFacts, rules);
    }

    private static ContractSituationAnswer Answer(string whyEn, string whyIt = "Il contratto si è rinnovato da solo.", string action = SituationAction.PrepareNextNotice, string headlineEn = "Renewed by itself", string sourceKey = "sp_renew") =>
        new(headlineEn, "Rinnovato da solo", whyEn, whyIt, action, SituationUrgency.Later, sourceKey);

    [Fact]
    public void The_guard_accepts_the_computed_dates_and_numbers()
    {
        var (facts, _) = Guard(Splunk);
        Assert.Null(ContractAssessmentGuard.Check(
            Answer("It renewed by itself on 06 Aug 2026 for 12 months; the next notice deadline is 07 Jul 2027, in 248 days. Source: Splunk_Order_2025.pdf, p. 4, § 9.2.",
                "Si è rinnovato da solo il 06 Ago 2026; la prossima disdetta è il 07 Lug 2027."),
            facts));
    }

    [Theory]
    [InlineData("It renewed on 06 Aug 2026; the next notice deadline is 07 Jun 2027.")] // invented date
    [InlineData("The next notice deadline is July 8, 2027.")] // invented date, other format
    [InlineData("The next notice deadline is 2027-07-08.")] // invented ISO date
    [InlineData("You have 200 days left.")] // invented number
    [InlineData("It renewed at CHF 120k per year.")] // an amount
    [InlineData("The price goes up by 7% at renewal.")] // a percentage
    public void The_guard_rejects_invented_dates_numbers_and_amounts(string whyEn)
    {
        var (facts, _) = Guard(Splunk);
        Assert.NotNull(ContractAssessmentGuard.Check(Answer(whyEn), facts));
    }

    [Fact]
    public void The_guard_rejects_an_action_that_contradicts_the_status_and_an_unknown_source()
    {
        var (facts, _) = Guard(Splunk);
        Assert.NotNull(ContractAssessmentGuard.Check(Answer("It renewed by itself.", action: SituationAction.ConfirmTerms), facts));
        Assert.NotNull(ContractAssessmentGuard.Check(Answer("It renewed by itself.", sourceKey: "made_up"), facts));
        Assert.NotNull(ContractAssessmentGuard.Check(Answer("", whyIt: ""), facts));
    }

    [Fact]
    public void The_rules_copy_passes_its_own_guard_for_every_status()
    {
        var cases = new[]
        {
            Splunk,
            Splunk with { RenewalTermMonths = null },
            Splunk with { Auto = "No" },
            Splunk with { Auto = "No", EndDate = new DateOnly(2026, 12, 31), NoticeDeadline = new DateOnly(2026, 10, 2) },
            Splunk with { Evidence = new Dictionary<string, ContractEvidence>() },
        };
        foreach (var facts in cases)
        {
            var (guard, rules) = Guard(facts);
            var answer = new ContractSituationAnswer(rules.Headline.En, rules.Headline.It, rules.Why.En, rules.Why.It, rules.RecommendedAction, rules.Urgency, rules.Source?.Key ?? "");
            Assert.Null(ContractAssessmentGuard.Check(answer, guard));
        }
    }

    [Fact]
    public async Task The_fixture_gateway_answers_deterministically_through_the_guard()
    {
        var gateway = new CountingGateway(new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
        var assessor = new AiContractSituationAssessor(gateway, new ContractAssessmentCache());
        var a = await assessor.AssessAsync(Tenant, Splunk, Today);
        Assert.Equal("ai", a.Origin);
        Assert.Equal(ContractTermStatus.RolledOver, a.Term.Status);
        Assert.Equal(new DateOnly(2027, 7, 7), a.Term.NoticeDeadline);
        Assert.Equal("Renewed by itself on 06 Aug 2026", a.Headline.En);
        Assert.Contains("Source: Splunk_Order_2025.pdf, p. 4, § 9.2.", a.Why.En, StringComparison.Ordinal);
        Assert.Equal("sp_renew", a.Source!.Key);

        // Cached per contract + facts + day: Oggi does not call the model on every load.
        await assessor.AssessAsync(Tenant, Splunk, Today);
        Assert.Equal(1, gateway.Calls);
        await assessor.AssessAsync(Tenant, Splunk, Today.AddDays(1));
        Assert.Equal(2, gateway.Calls);
    }

    [Fact]
    public async Task An_answer_with_an_invented_date_falls_back_to_the_deterministic_copy()
    {
        var invented = JsonSerializer.Serialize(new
        {
            headlineEn = "Renewed by itself",
            headlineIt = "Rinnovato da solo",
            whyEn = "Give notice by 15 Jun 2027.",
            whyIt = "Dai disdetta entro il 15 Giu 2027.",
            recommendedAction = "prepare_next_notice",
            urgency = "later",
            sourceKey = "sp_renew",
        });
        var assessor = new AiContractSituationAssessor(new StubGateway(invented), new ContractAssessmentCache());
        var a = await assessor.AssessAsync(Tenant, Splunk, Today);
        Assert.Equal("rules", a.Origin);
        Assert.DoesNotContain("15 Jun 2027", a.Why.En, StringComparison.Ordinal);
        Assert.Contains("07 Jul 2027", a.Why.En, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_active_term_needs_no_model_call()
    {
        var gateway = new CountingGateway(new StubGateway("{}"));
        var assessor = new AiContractSituationAssessor(gateway, new ContractAssessmentCache());
        var a = await assessor.AssessAsync(Tenant, Splunk, new DateOnly(2026, 6, 1));
        Assert.Equal(ContractTermStatus.Active, a.Term.Status);
        Assert.Equal("rules", a.Origin);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public void Markdown_file_carries_the_prompt_and_the_schema_verbatim()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Raffa.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var version = ContractSituationAgent.Version.Replace("situation-", string.Empty, StringComparison.Ordinal);
        var path = Path.Combine(dir!.FullName, "src", "Raffa.AiFlows", "ContractSituation", "Prompts", "situation", $"{version}.md");
        var markdown = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains($"## {ContractSituationAgent.Name} ({version})", markdown, StringComparison.Ordinal);
        Assert.Contains(ContractSituationAgent.Prompt.Replace("\r\n", "\n", StringComparison.Ordinal), markdown, StringComparison.Ordinal);
        Assert.Contains(ContractSituationAgent.Schema.Replace("\r\n", "\n", StringComparison.Ordinal), markdown, StringComparison.Ordinal);
    }

    private sealed class CountingGateway(IAiGateway inner) : IAiGateway
    {
        public int Calls { get; private set; }

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.AnalyzeAsync(request, cancellationToken);
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => inner.ClassifyAsync(request, cancellationToken);
        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => inner.ExtractAsync(request, cancellationToken);
        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => inner.EmbedAsync(request, cancellationToken);
        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => inner.AnswerAsync(request, cancellationToken);
        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => inner.OcrAsync(request, cancellationToken);
    }

    private sealed class StubGateway(string payload) : IAiGateway
    {
        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(payload,
                new AiCallMetadata("stub", "1", request.PromptVersion, DateTimeOffset.UnixEpoch, "hash"))));

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
