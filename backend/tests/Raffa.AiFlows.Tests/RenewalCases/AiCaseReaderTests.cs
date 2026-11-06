using Raffa.AiFlows.RenewalCases;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Renewals.Application.Cases;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Tests.RenewalCases;

public sealed class AiCaseReaderTests
{
    private static readonly CaseContractFacts Contract = new()
    {
        Supplier = "Acme",
        Name = "Acme Suite — MSA",
        Products = [new CaseProduct("Acme Suite", "AS-1", "seat", 100, 50)],
    };

    [Fact]
    public async Task A_reply_reading_keeps_valid_rows_and_never_an_agreed_term()
    {
        var gateway = new AnalyzeOnlyGateway(
            """
            {"rows":[
              {"label":"Prezzo unitario","said":"CHF 47 per 24 mesi","plan":"−10%","state":"proposed","warn":""},
              {"label":"Bad","said":"x","plan":"","state":"agreed","warn":""}],
             "terms":[
              {"key":"Price","label":"Prezzo unitario","state":"proposed","value":"CHF 47"},
              {"key":"term","label":"Durata","state":"agreed","value":"24"}]}
            """);
        var reader = new AiCaseReader(gateway, new TenantContext());

        var reading = await reader.ReadReplyAsync(TenantId.New(), new RenewalCase(), Contract, "We offer CHF 47 for 24 months.", 2, "it");

        Assert.NotNull(reading);
        var row = Assert.Single(reading!.Rows);
        Assert.Equal("proposed", row.State);
        var term = Assert.Single(reading.Terms);
        Assert.Equal("price", term.Key);
        Assert.Equal("reply 2", term.Ev);
        Assert.Contains("We offer CHF 47", Assert.Single(gateway.Requests).InputJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_off_schema_answer_reads_nothing()
    {
        var reader = new AiCaseReader(new AnalyzeOnlyGateway("""{"verdict":"x"}"""), new TenantContext());

        Assert.Null(await reader.ReadReplyAsync(TenantId.New(), new RenewalCase(), Contract, "text", 1, "en"));
        Assert.Null(await reader.ReadFeaturesAsync(TenantId.New(), Contract, "en"));
    }

    [Fact]
    public async Task Features_are_functions_with_a_lowercase_category()
    {
        var reader = new AiCaseReader(
            new AnalyzeOnlyGateway("""{"categoryEn":"Electronic Signature","functions":["Firma elettronica con audit trail","Modelli riutilizzabili","Firma elettronica con audit trail"]}"""),
            new TenantContext());

        var features = await reader.ReadFeaturesAsync(TenantId.New(), Contract, "it");

        Assert.NotNull(features);
        Assert.Equal("electronic signature", features!.CategoryEn);
        Assert.Equal(2, features.Items.Count);
    }

    [Fact]
    public async Task The_interview_is_worded_for_the_contract_and_the_levers_and_keeps_the_answers_fixed()
    {
        var reader = new AiCaseReader(
            new AnalyzeOnlyGateway(
                """
                {"questions":[
                  {"index":0,"text":"Acme Suite: confermi le 100 postazioni o vuoi ridurle?","hint":"In chat abbiamo visto la riduzione dei volumi.",
                   "options":[{"key":"keep","label":"Mantieni le 100 postazioni di Acme Suite"},{"key":"reduce","label":"Riduci le postazioni"},{"key":"bogus","label":"Altro"}]},
                  {"index":1,"text":"Durata: le leve si basano su 36 mesi, confermi?","hint":"","options":[]},
                  {"index":2,"text":"Obiettivo economico: punti a uno sconto del 99%?","hint":"","options":[]},
                  {"index":7,"text":"Fuori indice, da ignorare","hint":"","options":[]}]}
                """),
            new TenantContext());

        var tailoring = await reader.ReadInterviewAsync(TenantId.New(), Contract, "Pluriennale: 36 mesi contro riduzione dei volumi.", "it");

        Assert.NotNull(tailoring);
        // The invented 99% never reaches the person, nor does an index the interview does not have.
        Assert.Equal([0, 1], tailoring!.Questions.Select(q => q.Index).Order().ToArray());
        var products = tailoring.Questions.Single(q => q.Index == 0);
        Assert.Equal(["keep", "reduce"], products.Options!.Keys.Order().ToArray());
        Assert.Null(tailoring.Questions.Single(q => q.Index == 1).Hint);
    }

    [Fact]
    public async Task An_off_schema_interview_answer_leaves_the_standard_questions()
    {
        var reader = new AiCaseReader(new AnalyzeOnlyGateway("""{"verdict":"x"}"""), new TenantContext());

        Assert.Null(await reader.ReadInterviewAsync(TenantId.New(), Contract, null, "en"));
    }

    private static CaseEmail Draft() => new()
    {
        Kind = "first",
        Subject = "Acme Suite — renewal discussion",
        Body = "Dear Acme team,\n\nOur agreement ends on 21 Oct 2026. Please send a revised proposal by 14 Nov 2026.\n\nKind regards,\nPietro",
    };

    [Fact]
    public async Task A_composer_instruction_that_changes_the_email_returns_a_new_version_with_its_figure()
    {
        var reader = new AiCaseReader(
            new AnalyzeOnlyGateway(
                """{"intent":"revise","subject":"","body":"Dear Acme team,\n\nOur agreement ends on 21 Oct 2026. We plan to reduce to 50 seats. Please send a revised proposal by 14 Nov 2026.\n\nKind regards,\nPietro","note":"Ho indicato 50 postazioni."}"""),
            new TenantContext());

        var revision = await reader.ReviseEmailAsync(TenantId.New(), new RenewalCase(), Contract, Draft(), "riduzione a 50 seats", "it");

        Assert.NotNull(revision);
        Assert.Contains("50 seats", revision!.Body, StringComparison.Ordinal);
        Assert.Equal("Acme Suite — renewal discussion", revision.Subject);
    }

    [Fact]
    public async Task A_question_or_a_figure_the_case_does_not_hold_leaves_the_draft_alone()
    {
        var question = new AiCaseReader(new AnalyzeOnlyGateway("""{"intent":"question","subject":"","body":"","note":""}"""), new TenantContext());
        Assert.Empty((await question.ReviseEmailAsync(TenantId.New(), new RenewalCase(), Contract, Draft(), "what is the notice period?", "en"))!.Body);

        var invented = new AiCaseReader(
            new AnalyzeOnlyGateway(
                """{"intent":"revise","subject":"","body":"Dear Acme team,\n\nOur agreement ends on 21 Oct 2026. Competitors offer 31% less. Please send a revised proposal by 14 Nov 2026.\n\nKind regards,\nPietro","note":"x"}"""),
            new TenantContext());
        Assert.Null(await invented.ReviseEmailAsync(TenantId.New(), new RenewalCase(), Contract, Draft(), "make it firmer", "en"));
    }

    [Fact]
    public async Task A_composed_first_email_negotiates_on_the_interview_target_and_the_market_band()
    {
        var c = new RenewalCase();
        c.Goals.Prod = "add";
        c.Goals.Term = "24";
        c.Goals.Target = "p10";
        var gateway = new AnalyzeOnlyGateway(
            """{"intent":"revise","subject":"Acme Suite renewal — CHF 45 per seat, 24 months","body":"Dear Acme team,\n\nOur agreement ends on 21 Oct 2026. Market references for comparable deals sit at CHF 40-48 per seat, below our CHF 50. We can commit to 24 months at CHF 45 per seat. Please send a revised proposal by 14 Nov 2026.\n\nKind regards,\nPietro","note":""}""");
        var reader = new AiCaseReader(gateway, new TenantContext(), marketMatcher: new FixedMatcher(40, 44, 48));

        var composed = await reader.ReviseEmailAsync(TenantId.New(), c, Contract with { Currency = "CHF" }, Draft(), null, "en");

        Assert.NotNull(composed);
        Assert.Contains("CHF 45 per seat", composed!.Body, StringComparison.Ordinal);
        var input = gateway.Requests.Single().InputJson;
        Assert.Contains("\"targetUnitPrice\":45", input, StringComparison.Ordinal);
        Assert.Contains("\"marketMedian\":44", input, StringComparison.Ordinal);
        Assert.Contains("trade for price", input, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_composed_email_with_an_invented_figure_is_sent_back_once_with_the_reason()
    {
        var gateway = new AnalyzeOnlyGateway(
            """{"intent":"revise","subject":"","body":"Dear Acme team,\n\nOur agreement ends on 21 Oct 2026. Competitors offer 31% less. Please send a revised proposal by 14 Nov 2026.\n\nKind regards,\nPietro","note":""}""",
            """{"intent":"revise","subject":"","body":"Dear Acme team,\n\nOur agreement ends on 21 Oct 2026. We need a sharper price before the notice date. Please send a revised proposal by 14 Nov 2026.\n\nKind regards,\nPietro","note":""}""");
        var reader = new AiCaseReader(gateway, new TenantContext());

        var composed = await reader.ReviseEmailAsync(TenantId.New(), new RenewalCase(), Contract, Draft(), null, "en");

        Assert.NotNull(composed);
        Assert.Contains("sharper price", composed!.Body, StringComparison.Ordinal);
        Assert.Equal(2, gateway.Requests.Count);
        Assert.Contains("31", gateway.Requests[1].InputJson, StringComparison.Ordinal);
    }

    private sealed class FixedMatcher(decimal p25, decimal p50, decimal p75) : Raffa.SharedKernel.Market.IMarketPriceMatcher
    {
        public Task<IReadOnlyList<Raffa.SharedKernel.Market.MarketPriceMatch?>> MatchAsync(
            Raffa.SharedKernel.Market.MarketPriceContext context, IReadOnlyList<Raffa.SharedKernel.Market.MarketPriceLine> lines, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Raffa.SharedKernel.Market.MarketPriceMatch?>>(lines
                .Select(_ => (Raffa.SharedKernel.Market.MarketPriceMatch?)new Raffa.SharedKernel.Market.MarketPriceMatch(
                    "r1", "Acme Suite", "CH", context.Currency, 24, p25, p50, p75, 12, "test", DateTimeOffset.UnixEpoch))
                .ToList());
    }

    private sealed class AnalyzeOnlyGateway(params string[] payloads) : IAiGateway
    {
        public List<AiAnalysisRequest> Requests { get; } = [];

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var payloadJson = payloads[Math.Min(Requests.Count - 1, payloads.Length - 1)];
            return Task.FromResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(
                payloadJson, new AiCallMetadata("fake", "1", request.PromptVersion, DateTimeOffset.UnixEpoch, "hash"))));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
