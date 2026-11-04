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
        Assert.Null(await question.ReviseEmailAsync(TenantId.New(), new RenewalCase(), Contract, Draft(), "what is the notice period?", "en"));

        var invented = new AiCaseReader(
            new AnalyzeOnlyGateway(
                """{"intent":"revise","subject":"","body":"Dear Acme team,\n\nOur agreement ends on 21 Oct 2026. Competitors offer 31% less. Please send a revised proposal by 14 Nov 2026.\n\nKind regards,\nPietro","note":"x"}"""),
            new TenantContext());
        Assert.Null(await invented.ReviseEmailAsync(TenantId.New(), new RenewalCase(), Contract, Draft(), "make it firmer", "en"));
    }

    private sealed class AnalyzeOnlyGateway(string payloadJson) : IAiGateway
    {
        public List<AiAnalysisRequest> Requests { get; } = [];

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
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
