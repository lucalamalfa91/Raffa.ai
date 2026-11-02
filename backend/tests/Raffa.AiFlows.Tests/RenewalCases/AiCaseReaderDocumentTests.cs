using Raffa.AiFlows.RenewalCases;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Renewals.Application.Cases;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Tests.RenewalCases;

public sealed class AiCaseReaderDocumentTests
{
    private static readonly CaseContractFacts Contract = new() { Supplier = "Acme", Name = "Acme Suite" };

    private static AiCaseReader Reader(string json) => new(new Gateway(json), new TenantContext());

    [Fact]
    public async Task A_draft_reading_marks_what_it_could_not_read_as_unread()
    {
        var reader = Reader("""
            {"rows":[
              {"label":"Prezzo","agreed":"CHF 47","draft":"CHF 49","result":"diff","source":"p.3"},
              {"label":"Penale","agreed":"18 mesi","draft":"qualcosa","result":"unread","source":""},
              {"label":"X","agreed":"1","draft":"1","result":"maybe","source":""}]}
            """);
        var c = new RenewalCase { Agreed = new CaseAgreed { Price = 47, Term = 24, Cap = 3, Liab = 18 } };

        var rows = await reader.ReadDraftAsync(TenantId.New(), c, Contract, "bozza.pdf", "testo", "it");

        Assert.NotNull(rows);
        Assert.Equal(["diff", "unread"], rows!.Select(r => r.Res).ToArray());
        Assert.Equal("—", rows[1].Draft.Render());
    }

    [Fact]
    public async Task A_signed_reading_needs_consistent_counts()
    {
        var c = new RenewalCase { Agreed = new CaseAgreed { Price = 47, Term = 24 } };

        Assert.Equal(
            new CaseSignedCheck(6, 1),
            await Reader("""{"points":6,"differences":1}""").ReadSignedAsync(TenantId.New(), c, Contract, "f.pdf", "t", "it"));
        Assert.Null(await Reader("""{"points":2,"differences":5}""").ReadSignedAsync(TenantId.New(), c, Contract, "f.pdf", "t", "it"));
    }

    [Fact]
    public async Task An_offer_reading_of_nothing_is_no_offer()
    {
        var empty = Reader("""{"price":0,"total":0,"term":0,"cover":"","migration":"","gap":""}""");
        var full = Reader("""{"price":41.5,"total":49800,"term":24,"cover":"Copre 6 funzioni su 7","migration":"6 settimane","gap":"Manca l'audit trail"}""");

        Assert.Null(await empty.ReadOfferAsync(TenantId.New(), new RenewalCase(), Contract, "Vendor", "t", "Luca", "it"));
        var offer = await full.ReadOfferAsync(TenantId.New(), new RenewalCase(), Contract, "Vendor", "t", "Luca", "it");
        Assert.Equal(41.5, offer!.Price);
        Assert.Equal("Luca", offer.By);
    }

    [Fact]
    public async Task The_prices_a_reply_names_reach_the_analysis()
    {
        var reader = Reader("""
            {"rows":[{"label":"Prezzo","said":"CHF 47 / 24 mesi","plan":"-10%","state":"proposed","warn":""}],
             "terms":[],"price12":0,"price24":47}
            """);

        var reading = await reader.ReadReplyAsync(TenantId.New(), new RenewalCase(), Contract, "offer", 1, "it");

        Assert.Equal(47, Assert.Single(reading!.Prices!).Value);
    }

    [Fact]
    public async Task The_benchmark_keeps_only_what_the_reply_states()
    {
        var reader = Reader("""
            {"rows":[{"label":"Prezzo","said":"CHF 47","plan":"-10%","state":"proposed","warn":""}],"terms":[],
             "price12":0,"price24":47,
             "benchmark":{"discountPct":8,"upliftCapPct":0,"noticeDays":0,"termMonths":24,"paymentTerms":"  quarterly in advance "}}
            """);

        var b = (await reader.ReadReplyAsync(TenantId.New(), new RenewalCase(), Contract, "offer", 1, "it"))!.Benchmark!;

        Assert.Equal(8, b.DiscountPct);
        Assert.Null(b.UpliftCapPct);   // 0 means "not stated"
        Assert.Null(b.NoticeDays);
        Assert.Equal(24, b.TermMonths);
        Assert.Equal("quarterly in advance", b.PaymentTerms);
    }

    private sealed class Gateway(string payloadJson) : IAiGateway
    {
        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(
                payloadJson, new AiCallMetadata("fake", "1", request.PromptVersion, DateTimeOffset.UnixEpoch, "hash"))));

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
