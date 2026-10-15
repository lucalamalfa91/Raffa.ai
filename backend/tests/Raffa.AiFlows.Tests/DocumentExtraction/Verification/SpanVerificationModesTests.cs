using Microsoft.EntityFrameworkCore;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.AiFlows.DocumentExtraction.Stages;
using Raffa.AiFlows.DocumentExtraction.Verification;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure.Extraction;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Tests.DocumentExtraction.Verification;

/// <summary>
/// F5-T07 through the orchestrator: Off and Shadow leave the persisted data and the gateway requests exactly
/// as they are without a verifier; Gate sends the unverifiable facts (not found, or found on another page) to
/// review and nothing else, never rewriting a value or a page.
/// </summary>
public sealed class SpanVerificationModesTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<DocumentPageText> Pages =
    [
        new DocumentPageText(1, "This Agreement is governed by Italian law. Prices are in EUR. The Supplier is Acme S.r.l."),
        new DocumentPageText(2, "Payment is due within 30 days of invoice. The Supplier may be liable for delays."),
    ];

    private static readonly Dictionary<string, string> Payloads = new()
    {
        // supplier: verified; currency: span invented; governingLaw: real span but on page 2 instead of 1.
        ["Metadata"] =
            """{"facts":[{"field":"supplier","value":"Acme S.r.l.","sourcePage":1,"sourceSpan":"The Supplier is Acme S.r.l.","confidence":0.97},{"field":"currency","value":"EUR","sourcePage":1,"sourceSpan":"all fees are quoted in euro cents","confidence":0.97},{"field":"governingLaw","value":"Italian law","sourcePage":2,"sourceSpan":"governed by Italian law","confidence":0.97}]}""",
        ["CommercialTerms"] =
            """{"facts":[{"field":"paymentTerms","value":"30 days","sourcePage":2,"sourceSpan":"Payment is due within 30 days of invoice","confidence":0.97}]}""",
        ["Risk"] =
            """{"items":[{"riskType":"delay","severity":"low","description":"Delay liability","status":"open","sourcePage":2,"sourceSpan":"nothing like this in the text at all","confidence":0.97}]}""",
    };

    private sealed class AdvancingClock : IClock
    {
        private long _ticks;

        public DateTimeOffset UtcNow => Start.AddSeconds(Interlocked.Increment(ref _ticks));
    }

    private sealed class NullAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ScriptedGateway : IAiGateway
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
                Payloads.TryGetValue(request.StageName, out var json) ? json : "{}",
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

    private sealed record Outcome(
        StagedExtractionSummary Summary,
        string[] Evidence,
        string[] Requests,
        string[] Jobs,
        string[] Risks,
        Contract Contract);

    private static async Task<Outcome> RunAsync(ExtractionOrchestrationOptions? options)
    {
        var database = Guid.NewGuid().ToString("N");
        var tenantContext = new TenantContext();
        var tenantId = TenantId.New();
        var clock = new AdvancingClock();

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

        var gateway = new ScriptedGateway();
        StagedExtractionSummary summary;
        await using (var db = InMemoryDocumentsDb.Create(database))
        {
            var orchestrator = new ExtractionOrchestrator(
                new ExtractionRunStore(db, tenantContext, clock, new NullAuditWriter()),
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
        var risks = (await read.Risks.AsNoTracking().ToListAsync())
            .Select(r => $"{r.Description}|{r.SourcePage}|{r.SourceSpan}|{r.Confidence}")
            .ToArray();
        var contract = await read.Contracts.AsNoTracking().SingleAsync();

        return new Outcome(
            summary,
            evidence,
            gateway.Requests.OrderBy(r => r.StageName).Select(r => $"{r.StageName}|{r.DocumentText}|{r.JsonSchema}").ToArray(),
            jobs,
            risks,
            contract);
    }

    private static ExtractionOrchestrationOptions Mode(SpanVerificationMode mode) => new() { SpanVerification = mode };

    [Fact]
    public void Shadow_is_the_default_and_the_threshold_defaults_to_085()
    {
        var options = new ExtractionOrchestrationOptions();

        Assert.Equal(SpanVerificationMode.Shadow, options.SpanVerification);
        Assert.Equal(0.85, options.SpanFuzzyThreshold);
    }

    [Fact]
    public async Task Off_and_Shadow_persist_exactly_the_same_data_and_send_exactly_the_same_requests()
    {
        var off = await RunAsync(Mode(SpanVerificationMode.Off));
        var shadow = await RunAsync(Mode(SpanVerificationMode.Shadow));
        var defaults = await RunAsync(null);

        foreach (var other in new[] { shadow, defaults })
        {
            Assert.Equal(off.Evidence, other.Evidence);
            Assert.Equal(off.Requests, other.Requests);
            Assert.Equal(off.Jobs, other.Jobs);
            Assert.Equal(off.Risks, other.Risks);
            Assert.Equal(off.Summary.DocumentProcessingStatus, other.Summary.DocumentProcessingStatus);
            Assert.Equal(
                off.Summary.Stages.Select(s => (s.Stage, s.Status, s.ExtractedCount, s.SkippedCount)),
                other.Summary.Stages.Select(s => (s.Stage, s.Status, s.ExtractedCount, s.SkippedCount)));
            Assert.Equal(off.Contract.Currency, other.Contract.Currency);
        }

        // Shadow does not demote the unverifiable facts: they keep their auto-accepted decision.
        Assert.Contains(shadow.Evidence, e => e.StartsWith("currency|EUR|auto_accepted|1|", StringComparison.Ordinal));
        Assert.Contains(shadow.Evidence, e => e.StartsWith("governingLaw|Italian law|auto_accepted|2|", StringComparison.Ordinal));
        Assert.Equal(7, shadow.Requests.Length);
    }

    [Fact]
    public async Task Gate_routes_only_the_unverifiable_facts_to_review_and_never_rewrites_a_value_or_a_page()
    {
        var shadow = await RunAsync(Mode(SpanVerificationMode.Shadow));
        var gate = await RunAsync(Mode(SpanVerificationMode.Gate));

        // Same facts, same values, same pages, same requests and rows; only two decisions change.
        Assert.Equal(shadow.Requests, gate.Requests);
        Assert.Equal(shadow.Risks, gate.Risks);
        Assert.Equal(shadow.Evidence.Length, gate.Evidence.Length);

        var changed = shadow.Evidence.Zip(gate.Evidence).Where(pair => pair.First != pair.Second).ToList();
        Assert.Equal(2, changed.Count);
        Assert.Contains(gate.Evidence, e => e.StartsWith("currency|EUR|review_required|1|all fees are quoted in euro cents|", StringComparison.Ordinal));
        Assert.Contains(gate.Evidence, e => e.StartsWith("governingLaw|Italian law|review_required|2|governed by Italian law|", StringComparison.Ordinal));
        Assert.Contains(gate.Evidence, e => e.StartsWith("supplier|Acme S.r.l.|auto_accepted|1|", StringComparison.Ordinal));
        Assert.Contains(gate.Evidence, e => e.StartsWith("paymentTerms|30 days|auto_accepted|2|", StringComparison.Ordinal));

        // The values stay on the contract.
        Assert.Equal(shadow.Contract.Currency, gate.Contract.Currency);
        Assert.Equal("EUR", gate.Contract.Currency);
    }

    [Fact]
    public async Task Gate_flags_a_list_stage_whose_row_span_is_unverifiable_for_review_without_dropping_the_row()
    {
        var shadow = await RunAsync(Mode(SpanVerificationMode.Shadow));
        var gate = await RunAsync(Mode(SpanVerificationMode.Gate));

        var shadowRisk = shadow.Summary.Stages.Single(s => s.Stage == ExtractionStage.Risk);
        var gateRisk = gate.Summary.Stages.Single(s => s.Stage == ExtractionStage.Risk);

        Assert.Equal(ExtractionJobStatus.Completed, shadowRisk.Status);
        Assert.Equal(ExtractionJobStatus.NeedsReview, gateRisk.Status);
        Assert.Equal(1, gateRisk.ExtractedCount);
        Assert.Single(gate.Risks);
    }
}
