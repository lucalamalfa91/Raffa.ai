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

/// <summary>F5-T03: document-first layout behind an option (default off), its hashing and the recorded version.</summary>
public sealed class DocumentFirstPromptTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<DocumentPageText> Pages = [new DocumentPageText(1, "Supplier: ACME")];

    private static readonly string Text = PageMarkedText.Build(Pages);

    private const string MetadataPayload =
        """{"facts":[{"field":"currency","value":"EUR","sourcePage":1,"sourceSpan":"EUR","confidence":0.99}]}""";

    private sealed class Gateway : IAiGateway
    {
        public List<AiExtractionRequest> Requests { get; } = [];

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            return Task.FromResult(Result<AiExtractionResult>.Success(new AiExtractionResult(
                request.StageName == "Metadata" ? MetadataPayload : "{}",
                new AiCallMetadata("m", "1", "foundry-extract-test", Start, "h"))));
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

    private static async Task<(Gateway Gateway, List<ExtractionJob> Jobs, RecordingAuditWriter Audit)> RunAsync(
        ExtractionOrchestrationOptions? options)
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
                FileName = "c.pdf",
                MimeType = "application/pdf",
                StoragePath = $"{tenantId.Value:D}/documents/c.pdf",
                Checksum = "c",
                ProcessingStatus = DocumentProcessingStatus.Uploaded,
                CreatedAt = Start,
            };
            seed.Documents.Add(document);
            await seed.SaveChangesAsync();
            documentId = document.Id;
        }

        var gateway = new Gateway();
        var audit = new RecordingAuditWriter();
        await using (var db = InMemoryDocumentsDb.Create(database))
        {
            var orchestrator = new ExtractionOrchestrator(
                new ExtractionRunStore(db, tenantContext, new FixedClock(Start), audit), gateway, ExtractionStages.Default, tenantContext, options);
            var result = await orchestrator.RunAsync(tenantId, documentId, Pages);
            Assert.True(result.IsSuccess);
        }

        await using var read = InMemoryDocumentsDb.Create(database);
        var jobs = await read.ExtractionJobs.AsNoTracking().Where(j => j.Stage != ExtractionStage.Classification).ToListAsync();
        return (gateway, jobs, audit);
    }

    [Fact]
    public async Task Off_by_default_the_requests_the_hash_and_the_audit_row_are_the_historic_ones()
    {
        Assert.False(new ExtractionOrchestrationOptions().DocumentFirstPrompt);

        var (gateway, jobs, audit) = await RunAsync(null);

        Assert.All(gateway.Requests, r =>
        {
            Assert.False(r.DocumentFirst);
            Assert.Equal(Text, r.DocumentText);
        });
        Assert.All(jobs, j => Assert.Equal(ExtractionInputHash.Compute(Text), j.InputHash));
        Assert.DoesNotContain(audit.Written, e => e.Detail!.Contains("pipeline=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task On_every_request_asks_for_the_document_first_layout_and_the_hash_and_audit_carry_the_new_version()
    {
        var (gateway, jobs, audit) = await RunAsync(new ExtractionOrchestrationOptions { DocumentFirstPrompt = true });

        Assert.Equal(7, gateway.Requests.Count);
        Assert.All(gateway.Requests, r =>
        {
            Assert.True(r.DocumentFirst);
            Assert.Equal(Text, r.DocumentText); // the document leads and nothing precedes it
        });
        var expected = ExtractionInputHash.Compute(Text, documentFirst: true);
        Assert.NotEqual(ExtractionInputHash.Compute(Text), expected);
        Assert.All(jobs, j => Assert.Equal(expected, j.InputHash));
        Assert.Contains(audit.Written, e => e.Detail!.Contains("pipeline=staged-v2-docfirst/foundry-extract-test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_context_dependencies_the_context_block_follows_the_document_and_the_hash_is_of_the_effective_text()
    {
        var (gateway, jobs, _) = await RunAsync(new ExtractionOrchestrationOptions { DocumentFirstPrompt = true, UseContextDependencies = true });

        var dependent = gateway.Requests.Single(r => r.StageName == "Risk");
        Assert.StartsWith(Text, dependent.DocumentText, StringComparison.Ordinal);
        Assert.Contains("[[CONTEXT]]\ncurrency: EUR\n[[/CONTEXT]]", dependent.DocumentText, StringComparison.Ordinal);
        var riskJob = jobs.Single(j => j.Stage == ExtractionStage.Risk);
        Assert.Equal(ExtractionInputHash.Compute(dependent.DocumentText, documentFirst: true), riskJob.InputHash);
    }

    [Fact]
    public void The_two_layouts_never_share_a_hash()
    {
        Assert.Equal(ExtractionInputHash.Compute("x"), ExtractionInputHash.Compute("x", documentFirst: false));
        Assert.NotEqual(ExtractionInputHash.Compute("x"), ExtractionInputHash.Compute("x", documentFirst: true));
    }
}
