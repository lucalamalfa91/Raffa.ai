using Microsoft.EntityFrameworkCore;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.AiFlows.DocumentExtraction.Stages;
using Raffa.AiFlows.Shared.Parsing;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure.Extraction;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Tests.DocumentExtraction.Orchestration;

/// <summary>F5-T04: chunking of long documents, the map-reduce merge and the explicit truncated flag.</summary>
public sealed class ChunkingTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static string Pages(int count, int charsPerPage) =>
        PageMarkedText.Build(Enumerable.Range(1, count)
            .Select(n => new DocumentPageText(n, string.Join("\n", Enumerable.Repeat(new string('a', 49), charsPerPage / 50))))
            .ToList());

    // ---- chunker ------------------------------------------------------------------------------------

    [Fact]
    public void Pages_are_grouped_up_to_the_budget_and_rejoin_to_the_original_text()
    {
        var text = Pages(10, 1000); // ~1017 chars each

        var chunks = DocumentChunker.Split(text, 3000, 100);

        Assert.False(chunks.Truncated);
        Assert.True(chunks.Texts.Count > 1);
        Assert.All(chunks.Texts, c => Assert.StartsWith("[[PAGE ", c, StringComparison.Ordinal));
        Assert.All(chunks.Texts, c => Assert.True(c.Length <= 3000));
        Assert.Equal(text, string.Concat(chunks.Texts));
    }

    [Fact]
    public void A_page_longer_than_the_budget_is_split_and_each_piece_keeps_its_page_marker()
    {
        var text = Pages(2, 5000);

        var chunks = DocumentChunker.Split(text, 2000, 100);

        Assert.False(chunks.Truncated);
        Assert.All(chunks.Texts, c => Assert.True(c.Length <= 2000, $"chunk of {c.Length}"));
        Assert.All(chunks.Texts, c => Assert.StartsWith("[[PAGE ", c, StringComparison.Ordinal));
        Assert.Contains(chunks.Texts, c => c.StartsWith("[[PAGE 2]]\n", StringComparison.Ordinal));
        // Every original line survives (markers added, nothing lost).
        var original = text.Split('\n').Count(l => l.StartsWith('a'));
        var rejoined = string.Concat(chunks.Texts).Split('\n').Count(l => l.StartsWith('a'));
        Assert.Equal(original, rejoined);
    }

    [Fact]
    public void More_chunks_than_the_cap_are_reported_truncated_and_the_cap_is_respected()
    {
        var text = Pages(30, 1000);

        var chunks = DocumentChunker.Split(text, 3000, 4);

        Assert.True(chunks.Truncated);
        Assert.Equal(4, chunks.Texts.Count);
        Assert.StartsWith(string.Concat(chunks.Texts), text, StringComparison.Ordinal);
        Assert.True(string.Concat(chunks.Texts).Length < text.Length);
    }

    [Fact]
    public void A_document_that_fits_in_the_cap_is_never_reported_truncated()
    {
        var chunks = DocumentChunker.Split(Pages(6, 1000), 3000, chunkCountThatJustFits());
        Assert.False(chunks.Truncated);

        static int chunkCountThatJustFits() => 3;
    }

    // ---- merge --------------------------------------------------------------------------------------

    [Fact]
    public void Scalar_facts_keep_the_highest_confidence_non_empty_reading_per_field()
    {
        var merged = (StageFacts.Scalars)StageFactsMerger.Merge(
        [
            new StageFacts.Scalars(ExtractionStage.DatesAndRenewalTerms,
                [new ExtractedFact("endDate", "2027-01-01", 1, "a", 0.6), new ExtractedFact("startDate", null, null, null, null)]),
            new StageFacts.Scalars(ExtractionStage.DatesAndRenewalTerms,
                [new ExtractedFact("endDate", "2027-06-30", 9, "b", 0.9), new ExtractedFact("startDate", "2026-01-01", 8, "c", 0.5)]),
        ]);

        Assert.Equal(2, merged.Facts.Count);
        Assert.Equal("2027-06-30", merged.Facts.Single(f => f.Field == "endDate").Value);
        Assert.Equal(9, merged.Facts.Single(f => f.Field == "endDate").SourcePage);
        Assert.Equal("2026-01-01", merged.Facts.Single(f => f.Field == "startDate").Value);
    }

    [Fact]
    public void List_facts_concatenate_in_chunk_order_without_exact_duplicates_and_round_trip_through_the_stage()
    {
        var clause = (string type, int page) => new ExtractedClauseFact(type, "raw", null, null, page, "span", 0.9);
        var merged = StageFactsMerger.Merge(
        [
            new StageFacts.Clauses([clause("Termination", 1), clause("Liability", 2)]),
            new StageFacts.Clauses([clause("Liability", 2), clause("Payment", 7)]),
        ]);

        var stage = ExtractionStages.Default.Single(s => s.Stage == ExtractionStage.LegalClauses);
        var parsed = (StageFacts.Clauses)stage.Parse(StageFactsMerger.ToPayloadJson(merged));

        Assert.Equal(["Termination", "Liability", "Payment"], parsed.Items.Select(i => i.ClauseType));
    }

    // ---- scheduler ----------------------------------------------------------------------------------

    private sealed class PayloadGateway : IAiGateway
    {
        public List<AiExtractionRequest> Requests { get; } = [];

        public Func<AiExtractionRequest, Result<AiExtractionResult>>? Respond { get; set; }

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            return Task.FromResult(Respond?.Invoke(request) ?? Result<AiExtractionResult>.Success(new AiExtractionResult(
                "{}", new AiCallMetadata("m", "1", "p", Start, "h", new AiTokenUsage(10, 5)))));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static readonly IReadOnlyDictionary<ExtractionStage, StageFacts> NoContext = new Dictionary<ExtractionStage, StageFacts>();

    private static StageScheduler Scheduler(IAiGateway gateway, ExtractionOrchestrationOptions options) =>
        new(gateway, ExtractionStages.Default.ToDictionary(s => s.Stage), options);

    private static Result<AiExtractionResult> Ok(string payload) =>
        Result<AiExtractionResult>.Success(new AiExtractionResult(payload, new AiCallMetadata("m", "1", "p", Start, "h", new AiTokenUsage(100, 20))));

    [Fact]
    public async Task Below_the_threshold_the_request_is_the_single_unchunked_one()
    {
        var gateway = new PayloadGateway();
        var text = Pages(5, 1000);
        var options = new ExtractionOrchestrationOptions { ChunkThresholdChars = 400_000 };

        var calls = await Task.WhenAll(Scheduler(gateway, options)
            .Start([ExtractionStage.Metadata], text, 5, NoContext, CancellationToken.None).Values);

        Assert.Single(gateway.Requests);
        Assert.Equal(text, gateway.Requests[0].DocumentText);
        Assert.False(calls[0].Truncated);
    }

    [Fact]
    public async Task Above_the_threshold_each_chunk_is_a_call_and_the_facts_are_merged_with_summed_usage()
    {
        var text = Pages(12, 1000);
        var gateway = new PayloadGateway
        {
            Respond = r => r.DocumentText.Contains("[[PAGE 1]]", StringComparison.Ordinal)
                ? Ok("""{"facts":[{"field":"currency","value":"EUR","sourcePage":1,"sourceSpan":"EUR","confidence":0.7}]}""")
                : r.DocumentText.Contains("[[PAGE 12]]", StringComparison.Ordinal)
                    ? Ok("""{"facts":[{"field":"currency","value":"USD","sourcePage":12,"sourceSpan":"USD","confidence":0.95}]}""")
                    : Ok("""{"facts":[]}"""),
        };
        var options = new ExtractionOrchestrationOptions { ChunkThresholdChars = 5000, ChunkSizeChars = 4000, MaxChunks = 50 };

        var call = (await Task.WhenAll(Scheduler(gateway, options)
            .Start([ExtractionStage.Metadata], text, 12, NoContext, CancellationToken.None).Values)).Single();

        Assert.True(gateway.Requests.Count > 1);
        Assert.Equal(text, string.Concat(gateway.Requests.Select(r => r.DocumentText)));
        Assert.False(call.Truncated);
        var facts = (StageFacts.Scalars)ExtractionStages.Default.Single(s => s.Stage == ExtractionStage.Metadata)
            .Parse(call.Result.Value.PayloadJson);
        Assert.Equal("USD", facts.Facts.Single().Value);
        Assert.Equal(100 * gateway.Requests.Count, call.Result.Value.Metadata.Usage!.PromptTokens);
    }

    [Fact]
    public async Task A_failed_chunk_fails_the_stage_instead_of_applying_a_partial_read()
    {
        var text = Pages(12, 1000);
        var seen = 0;
        var gateway = new PayloadGateway
        {
            Respond = _ => Interlocked.Increment(ref seen) == 2
                ? Result<AiExtractionResult>.Failure("Simulated rejected request.")
                : Ok("""{"facts":[]}"""),
        };
        var options = new ExtractionOrchestrationOptions { ChunkThresholdChars = 5000, ChunkSizeChars = 4000, MaxChunks = 50 };

        var call = (await Task.WhenAll(Scheduler(gateway, options)
            .Start([ExtractionStage.Metadata], text, 12, NoContext, CancellationToken.None).Values)).Single();

        Assert.True(call.Result.IsFailure);
        Assert.Equal(2, gateway.Requests.Count);
    }

    [Fact]
    public async Task A_document_beyond_the_chunk_cap_marks_the_stage_call_truncated()
    {
        var gateway = new PayloadGateway();
        var options = new ExtractionOrchestrationOptions { ChunkThresholdChars = 5000, ChunkSizeChars = 4000, MaxChunks = 2 };

        var call = (await Task.WhenAll(Scheduler(gateway, options)
            .Start([ExtractionStage.Risk], Pages(30, 1000), 30, NoContext, CancellationToken.None).Values)).Single();

        Assert.True(call.Truncated);
        Assert.Equal(2, gateway.Requests.Count);
    }

    // ---- end to end: a truncated document is never Completed -----------------------------------------

    [Fact]
    public async Task A_truncated_run_is_flagged_on_the_summary_and_the_audit_row_and_never_completes()
    {
        var database = Guid.NewGuid().ToString("N");
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        EntityId documentId;
        await using (var seed = InMemoryDocumentsDb.Create(database))
        {
            var document = new Document
            {
                TenantId = tenantId,
                FileName = "long.pdf",
                MimeType = "application/pdf",
                StoragePath = $"{tenantId.Value:D}/documents/long.pdf",
                Checksum = "c",
                ProcessingStatus = DocumentProcessingStatus.Uploaded,
                CreatedAt = Start,
            };
            seed.Documents.Add(document);
            await seed.SaveChangesAsync();
            documentId = document.Id;
        }

        var pages = Enumerable.Range(1, 40).Select(n => new DocumentPageText(n, new string('b', 1000))).ToList();
        var audit = new RecordingAuditWriter();
        await using var db = InMemoryDocumentsDb.Create(database);
        var orchestrator = new ExtractionOrchestrator(
            new ExtractionRunStore(db, tenantContext, new FixedClock(Start), audit),
            new PayloadGateway(),
            ExtractionStages.Default,
            tenantContext,
            new ExtractionOrchestrationOptions { ChunkThresholdChars = 5000, ChunkSizeChars = 4000, MaxChunks = 2 });

        var result = await orchestrator.RunAsync(tenantId, documentId, pages);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Truncated);
        Assert.NotEqual(DocumentProcessingStatus.Completed, result.Value.DocumentProcessingStatus);
        Assert.Contains(audit.Written, e => e.Detail!.Contains("truncated=true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_run_below_the_threshold_is_not_truncated()
    {
        var database = Guid.NewGuid().ToString("N");
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        EntityId documentId;
        await using (var seed = InMemoryDocumentsDb.Create(database))
        {
            var document = new Document
            {
                TenantId = tenantId,
                FileName = "short.pdf",
                MimeType = "application/pdf",
                StoragePath = $"{tenantId.Value:D}/documents/short.pdf",
                Checksum = "c",
                ProcessingStatus = DocumentProcessingStatus.Uploaded,
                CreatedAt = Start,
            };
            seed.Documents.Add(document);
            await seed.SaveChangesAsync();
            documentId = document.Id;
        }

        await using var db = InMemoryDocumentsDb.Create(database);
        var orchestrator = new ExtractionOrchestrator(
            new ExtractionRunStore(db, tenantContext, new FixedClock(Start), new RecordingAuditWriter()),
            new PayloadGateway(),
            ExtractionStages.Default,
            tenantContext);

        var result = await orchestrator.RunAsync(tenantId, documentId, [new DocumentPageText(1, "short")]);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Truncated);
    }
}
