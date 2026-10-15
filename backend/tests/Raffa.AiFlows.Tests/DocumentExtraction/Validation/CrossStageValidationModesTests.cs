using Microsoft.EntityFrameworkCore;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.AiFlows.DocumentExtraction.Stages;
using Raffa.AiFlows.DocumentExtraction.Validation;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure.Extraction;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Tests.DocumentExtraction.Validation;

/// <summary>
/// F5-D19 through the orchestrator: Off and Shadow leave the persisted data and the gateway requests exactly as
/// they are without the rules; Gate sends only the fields a violated rule involves to review, never rewriting a
/// value.
/// </summary>
public sealed class CrossStageValidationModesTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<DocumentPageText> Pages = [new DocumentPageText(1, "Services agreement text.")];

    private static Dictionary<string, string> Payloads(string annualSpend, string totalValue, string cancellation) => new()
    {
        ["Metadata"] =
            """{"facts":[{"field":"supplier","value":"Acme S.r.l.","sourcePage":1,"sourceSpan":null,"confidence":0.97},{"field":"currency","value":"EUR","sourcePage":1,"sourceSpan":null,"confidence":0.97}]}""",
        ["CommercialTerms"] =
            $$"""{"facts":[{"field":"annualSpend","value":"{{annualSpend}}","sourcePage":1,"sourceSpan":null,"confidence":0.97},{"field":"totalContractValue","value":"{{totalValue}}","sourcePage":1,"sourceSpan":null,"confidence":0.97}]}""",
        ["DatesAndRenewalTerms"] =
            $$"""{"facts":[{"field":"startDate","value":"2026-01-01","sourcePage":1,"sourceSpan":null,"confidence":0.97},{"field":"endDate","value":"2028-12-31","sourcePage":1,"sourceSpan":null,"confidence":0.97},{"field":"cancellationDeadline","value":"{{cancellation}}","sourcePage":1,"sourceSpan":null,"confidence":0.97},{"field":"autoRenewal","value":"true","sourcePage":1,"sourceSpan":null,"confidence":0.97}]}""",
    };

    // Total below annual spend over three years, and a cancellation deadline after the end: two violations.
    private static readonly Dictionary<string, string> Violating = Payloads("100000", "50000", "2029-01-15");

    // Everything coherent.
    private static readonly Dictionary<string, string> Coherent = Payloads("100000", "300000", "2028-10-02");

    private sealed class AdvancingClock : IClock
    {
        private long _ticks;

        public DateTimeOffset UtcNow => Start.AddSeconds(Interlocked.Increment(ref _ticks));
    }

    private sealed class NullAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ScriptedGateway(Dictionary<string, string> payloads) : IAiGateway
    {
        public List<AiExtractionRequest> Requests { get; } = [];

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            return Task.FromResult(Result<AiExtractionResult>.Success(new AiExtractionResult(
                payloads.TryGetValue(request.StageName, out var json) ? json : "{}",
                new AiCallMetadata("test-model", "1", "test-v1", Start, "hash-" + request.StageName))));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record Outcome(StagedExtractionSummary Summary, string[] Evidence, string[] Requests, string[] Jobs, Contract Contract);

    private static async Task<Outcome> RunAsync(Dictionary<string, string> payloads, ExtractionOrchestrationOptions? options)
    {
        var database = Guid.NewGuid().ToString("N");
        var tenantContext = new TenantContext();
        var tenantId = TenantId.New();

        EntityId documentId;
        await using (var seed = InMemoryDocumentsDb.Create(database))
        {
            var document = new Document
            {
                TenantId = tenantId,
                FileName = "contract.pdf",
                MimeType = "application/pdf",
                StoragePath = $"{tenantId.Value:D}/documents/contract.pdf",
                Checksum = "checksum-1",
                ProcessingStatus = DocumentProcessingStatus.Uploaded,
                CreatedAt = Start,
            };
            seed.Documents.Add(document);
            await seed.SaveChangesAsync();
            documentId = document.Id;
        }

        var gateway = new ScriptedGateway(payloads);
        StagedExtractionSummary summary;
        await using (var db = InMemoryDocumentsDb.Create(database))
        {
            var orchestrator = new ExtractionOrchestrator(
                new ExtractionRunStore(db, tenantContext, new AdvancingClock(), new NullAuditWriter()),
                gateway,
                ExtractionStages.Default,
                tenantContext,
                options);
            var result = await orchestrator.RunAsync(tenantId, documentId, Pages);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            summary = result.Value;
        }

        await using var read = InMemoryDocumentsDb.Create(database);
        var evidence = (await read.ExtractionEvidences.AsNoTracking().ToListAsync())
            .Where(e => e.FieldName != "type")
            .OrderBy(e => e.FieldName)
            .Select(e => $"{e.FieldName}|{e.Value}|{e.Decision}|{e.SourcePage}|{e.SourceSpan}|{e.Confidence}")
            .ToArray();
        var jobs = (await read.ExtractionJobs.AsNoTracking().ToListAsync())
            .Where(j => j.Stage != ExtractionStage.Classification)
            .OrderBy(j => j.Stage)
            .Select(j => $"{j.Stage}|{j.Status}|{j.ExtractedCount}|{j.SkippedCount}|{j.InputHash}")
            .ToArray();
        var contract = await read.Contracts.AsNoTracking().SingleAsync();

        return new Outcome(
            summary,
            evidence,
            gateway.Requests.OrderBy(r => r.StageName).Select(r => $"{r.StageName}|{r.DocumentText}|{r.JsonSchema}").ToArray(),
            jobs,
            contract);
    }

    private static ExtractionOrchestrationOptions Mode(ValidationMode mode) => new() { CrossStageValidation = mode };

    [Fact]
    public void Shadow_is_the_default()
    {
        Assert.Equal(ValidationMode.Shadow, new ExtractionOrchestrationOptions().CrossStageValidation);
    }

    [Fact]
    public async Task Off_and_Shadow_persist_exactly_the_same_data_and_send_exactly_the_same_requests_even_with_violations()
    {
        var off = await RunAsync(Violating, Mode(ValidationMode.Off));
        var shadow = await RunAsync(Violating, Mode(ValidationMode.Shadow));
        var defaults = await RunAsync(Violating, null);

        foreach (var other in new[] { shadow, defaults })
        {
            Assert.Equal(off.Evidence, other.Evidence);
            Assert.Equal(off.Requests, other.Requests);
            Assert.Equal(off.Jobs, other.Jobs);
            Assert.Equal(off.Summary.DocumentProcessingStatus, other.Summary.DocumentProcessingStatus);
            Assert.Equal(
                off.Summary.Stages.Select(s => (s.Stage, s.Status, s.ExtractedCount, s.SkippedCount)),
                other.Summary.Stages.Select(s => (s.Stage, s.Status, s.ExtractedCount, s.SkippedCount)));
            Assert.Equal(off.Contract.TotalContractValue, other.Contract.TotalContractValue);
            Assert.Equal(off.Contract.CancellationDeadline, other.Contract.CancellationDeadline);
        }

        // Shadow does not demote anything: the violating fields keep their auto-accepted decision.
        Assert.Contains(shadow.Evidence, e => e.StartsWith("totalContractValue|50000|auto_accepted|", StringComparison.Ordinal));
        Assert.Contains(shadow.Evidence, e => e.StartsWith("cancellationDeadline|2029-01-15|auto_accepted|", StringComparison.Ordinal));
        Assert.Equal(7, shadow.Requests.Length);
    }

    [Fact]
    public async Task Gate_changes_nothing_when_the_stages_are_coherent()
    {
        var shadow = await RunAsync(Coherent, Mode(ValidationMode.Shadow));
        var gate = await RunAsync(Coherent, Mode(ValidationMode.Gate));

        Assert.Equal(shadow.Evidence, gate.Evidence);
        Assert.Equal(shadow.Jobs, gate.Jobs);
        Assert.Equal(shadow.Summary.DocumentProcessingStatus, gate.Summary.DocumentProcessingStatus);
    }

    [Fact]
    public async Task Gate_routes_only_the_involved_fields_to_review_and_never_edits_a_value()
    {
        var shadow = await RunAsync(Violating, Mode(ValidationMode.Shadow));
        var gate = await RunAsync(Violating, Mode(ValidationMode.Gate));

        Assert.Equal(shadow.Requests, gate.Requests);
        Assert.Equal(shadow.Evidence.Length, gate.Evidence.Length);

        // TCV rule: totalContractValue, annualSpend, startDate, endDate; deadline rule: cancellationDeadline, endDate.
        var changed = shadow.Evidence.Zip(gate.Evidence).Where(pair => pair.First != pair.Second).Select(pair => pair.Second).ToList();
        Assert.Equal(5, changed.Count);
        foreach (var field in new[] { "totalContractValue|50000", "annualSpend|100000", "startDate|2026-01-01", "endDate|2028-12-31", "cancellationDeadline|2029-01-15" })
        {
            Assert.Contains(gate.Evidence, e => e.StartsWith(field + "|review_required|", StringComparison.Ordinal));
        }

        // Untouched fields keep their decision.
        Assert.Contains(gate.Evidence, e => e.StartsWith("supplier|Acme S.r.l.|auto_accepted|", StringComparison.Ordinal));
        Assert.Contains(gate.Evidence, e => e.StartsWith("currency|EUR|auto_accepted|", StringComparison.Ordinal));
        Assert.Contains(gate.Evidence, e => e.StartsWith("autoRenewal|true|auto_accepted|", StringComparison.Ordinal));

        // Values on the contract are exactly the extracted ones.
        Assert.Equal(shadow.Contract.TotalContractValue, gate.Contract.TotalContractValue);
        Assert.Equal(50000m, gate.Contract.TotalContractValue);
        Assert.Equal(shadow.Contract.AnnualSpend, gate.Contract.AnnualSpend);
        Assert.Equal(shadow.Contract.EndDate, gate.Contract.EndDate);
        Assert.Equal(shadow.Contract.CancellationDeadline, gate.Contract.CancellationDeadline);

        // The affected scalar stages need review; the document does not complete on its own.
        Assert.Equal(ExtractionJobStatus.NeedsReview, gate.Summary.Stages.Single(s => s.Stage == ExtractionStage.CommercialTerms).Status);
        Assert.Equal(ExtractionJobStatus.NeedsReview, gate.Summary.Stages.Single(s => s.Stage == ExtractionStage.DatesAndRenewalTerms).Status);
        Assert.Equal(ExtractionJobStatus.Completed, gate.Summary.Stages.Single(s => s.Stage == ExtractionStage.Metadata).Status);
        Assert.Equal(DocumentProcessingStatus.NeedsReview, gate.Summary.DocumentProcessingStatus);
    }
}
