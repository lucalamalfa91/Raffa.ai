using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.AiFlows.DocumentExtraction.Schemas;
using Raffa.AiFlows.DocumentExtraction.Stages;
using Raffa.AiFlows.Shared.Parsing;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Logging;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Documents.Contracts.Infrastructure.Extraction;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Tests.DocumentExtraction.Orchestration;

/// <summary>
/// F5 PR-2: what the move of the AI side into <c>Raffa.AiFlows</c> must keep true at the seam between the
/// orchestrator, its stages and the store: a malformed payload never reaches the store, a missing tenant
/// scope is loud, the gateway request is byte-for-byte what the pipeline always sent, and the audit rows of
/// the gateway carry the run and the step.
/// </summary>
public sealed class ExtractionOrchestratorWiringTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<DocumentPageText> Pages =
    [
        new DocumentPageText(1, "abc"),
        new DocumentPageText(2, "def"),
    ];

    private sealed class FixedTestClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    /// <summary>Records every `extract` request; scripted per stage; can throw or return a failure for a stage.</summary>
    private sealed class RecordingGateway : IAiGateway
    {
        public ConcurrentQueue<AiExtractionRequest> Requests { get; } = new();

        public Dictionary<string, string> Payloads { get; } = new();

        public Dictionary<string, Exception> Throws { get; } = new();

        public int TotalCalls => Requests.Count;

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);

            if (Throws.TryGetValue(request.StageName, out var exception))
            {
                throw exception;
            }

            var payload = Payloads.TryGetValue(request.StageName, out var json) ? json : "{}";
            var metadata = new AiCallMetadata("test-model", "1", "test-v1", Now, "hash-" + request.StageName);
            return Task.FromResult(Result<AiExtractionResult>.Success(new AiExtractionResult(payload, metadata)));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Forwards to the real store and records what the orchestrator asked of it.</summary>
    private sealed class RecordingStore(IExtractionRunStore inner) : IExtractionRunStore
    {
        public List<(ExtractionStage Stage, StageFacts Facts)> Applied { get; } = [];

        public List<(ExtractionStage Stage, string Error, ExtractionStageFailureKind Kind)> Failed { get; } = [];

        public Task<Result<ExtractionRun>> OpenAsync(ExtractionRunRequest request, CancellationToken cancellationToken) =>
            inner.OpenAsync(request, cancellationToken);

        public Task<IReadOnlyDictionary<ExtractionStage, EntityId>> StartStagesAsync(
            ExtractionRun run, IReadOnlyList<ExtractionStage> stages, CancellationToken cancellationToken) =>
            inner.StartStagesAsync(run, stages, cancellationToken);

        public Task<StagedExtractionStageResult> ReuseCheckpointAsync(
            ExtractionRun run, ExtractionStage stage, CancellationToken cancellationToken) =>
            inner.ReuseCheckpointAsync(run, stage, cancellationToken);

        public Task<StagedExtractionStageResult> ApplyAsync(
            ExtractionRun run, ExtractionStage stage, StageFacts facts, string? modelId, CancellationToken cancellationToken)
        {
            Applied.Add((stage, facts));
            return inner.ApplyAsync(run, stage, facts, modelId, cancellationToken);
        }

        public Task<StagedExtractionStageResult> FailAsync(
            ExtractionRun run,
            ExtractionStage stage,
            string error,
            ExtractionStageFailureKind kind,
            CancellationToken cancellationToken,
            string? modelId = null)
        {
            Failed.Add((stage, error, kind));
            return inner.FailAsync(run, stage, error, kind, cancellationToken, modelId);
        }

        public Task<StagedExtractionSummary> CloseAsync(
            ExtractionRun run, IReadOnlyList<StagedExtractionStageResult> stageResults, CancellationToken cancellationToken) =>
            inner.CloseAsync(run, stageResults, cancellationToken);
    }

    /// <summary>A tenant context whose <c>BeginScope</c> does nothing: <c>Current</c> stays as configured.</summary>
    private sealed class InertTenantContext(TenantId? current) : ITenantContext
    {
        public TenantId? Current => current;

        public IDisposable BeginScope(TenantId tenantId) => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private static async Task<(string Database, TenantId TenantId, EntityId DocumentId)> SeedAsync()
    {
        var database = Guid.NewGuid().ToString("N");
        var tenantId = TenantId.New();
        await using var db = InMemoryDocumentsDb.Create(database);
        var document = new Document
        {
            TenantId = tenantId,
            FileName = "contract.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value:D}/documents/contract.pdf",
            Checksum = "checksum-1",
            ProcessingStatus = DocumentProcessingStatus.Uploaded,
            CreatedAt = Now,
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return (database, tenantId, document.Id);
    }

    [Fact]
    public async Task A_malformed_payload_fails_only_its_stage_as_permanent_and_never_reaches_the_store()
    {
        var (database, tenantId, documentId) = await SeedAsync();
        var tenantContext = new TenantContext();
        var gateway = new RecordingGateway();
        gateway.Payloads[nameof(ExtractionStage.Risk)] = "{not json";
        await using var db = InMemoryDocumentsDb.Create(database);
        var store = new RecordingStore(new ExtractionRunStore(db, tenantContext, new FixedTestClock(), new RecordingAuditWriter()));
        var orchestrator = new ExtractionOrchestrator(store, gateway, ExtractionStages.Default, tenantContext);

        var result = await orchestrator.RunAsync(tenantId, documentId, Pages);

        Assert.True(result.IsSuccess);
        var failure = Assert.Single(store.Failed);
        Assert.Equal(ExtractionStage.Risk, failure.Stage);
        Assert.StartsWith("Malformed extraction payload: ", failure.Error);
        Assert.Equal(ExtractionStageFailureKind.Permanent, failure.Kind);
        Assert.DoesNotContain(store.Applied, applied => applied.Stage == ExtractionStage.Risk);
        Assert.Equal(6, store.Applied.Count);
        Assert.Equal(7, gateway.TotalCalls);
    }

    [Fact]
    public async Task A_thrown_transport_fault_is_recorded_transient_for_its_stage_only()
    {
        var (database, tenantId, documentId) = await SeedAsync();
        var tenantContext = new TenantContext();
        var gateway = new RecordingGateway();
        gateway.Throws[nameof(ExtractionStage.Obligations)] = new HttpRequestException("connection reset");
        await using var db = InMemoryDocumentsDb.Create(database);
        var store = new RecordingStore(new ExtractionRunStore(db, tenantContext, new FixedTestClock(), new RecordingAuditWriter()));
        var orchestrator = new ExtractionOrchestrator(store, gateway, ExtractionStages.Default, tenantContext);

        var result = await orchestrator.RunAsync(tenantId, documentId, Pages);

        Assert.True(result.IsSuccess);
        var failure = Assert.Single(store.Failed);
        Assert.Equal(ExtractionStage.Obligations, failure.Stage);
        Assert.StartsWith(AiGatewayErrors.UnavailablePrefix, failure.Error);
        Assert.Equal(ExtractionStageFailureKind.Transient, failure.Kind);
        Assert.Equal(6, store.Applied.Count);
    }

    [Fact]
    public async Task Run_without_a_working_tenant_scope_throws_before_any_gateway_call_or_row()
    {
        var (database, tenantId, documentId) = await SeedAsync();
        var gateway = new RecordingGateway();
        var tenantContext = new InertTenantContext(current: null);
        await using var db = InMemoryDocumentsDb.Create(database);
        var orchestrator = new ExtractionOrchestrator(
            new ExtractionRunStore(db, tenantContext, new FixedTestClock(), new RecordingAuditWriter()),
            gateway,
            ExtractionStages.Default,
            tenantContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.RunAsync(tenantId, documentId, Pages));

        Assert.Equal(0, gateway.TotalCalls);
        await using var readDb = InMemoryDocumentsDb.Create(database);
        Assert.Empty(await readDb.ExtractionJobs.ToListAsync());
    }

    [Fact]
    public async Task Run_inside_another_tenants_scope_throws_before_any_gateway_call_or_row()
    {
        var (database, tenantId, documentId) = await SeedAsync();
        var gateway = new RecordingGateway();
        var tenantContext = new InertTenantContext(current: TenantId.New());
        await using var db = InMemoryDocumentsDb.Create(database);
        var orchestrator = new ExtractionOrchestrator(
            new ExtractionRunStore(db, tenantContext, new FixedTestClock(), new RecordingAuditWriter()),
            gateway,
            ExtractionStages.Default,
            tenantContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.RunAsync(tenantId, documentId, Pages));

        Assert.Equal(0, gateway.TotalCalls);
        await using var readDb = InMemoryDocumentsDb.Create(database);
        Assert.Empty(await readDb.ExtractionJobs.ToListAsync());
    }

    [Fact]
    public async Task Empty_pages_fail_before_any_scope_or_store_call()
    {
        var tenantContext = new InertTenantContext(current: null);
        var gateway = new RecordingGateway();
        await using var db = InMemoryDocumentsDb.Create(Guid.NewGuid().ToString("N"));
        var orchestrator = new ExtractionOrchestrator(
            new ExtractionRunStore(db, tenantContext, new FixedTestClock(), new RecordingAuditWriter()),
            gateway,
            ExtractionStages.Default,
            tenantContext);

        var result = await orchestrator.RunAsync(TenantId.New(), EntityId.New(), []);

        Assert.True(result.IsFailure);
        Assert.Equal("Staged extraction requires at least one page of document text.", result.Error);
        Assert.Equal(0, gateway.TotalCalls);
    }

    [Fact]
    public async Task The_gateway_requests_are_byte_equal_to_the_pinned_snapshot()
    {
        var (database, tenantId, documentId) = await SeedAsync();
        var tenantContext = new TenantContext();
        var gateway = new RecordingGateway();
        await using var db = InMemoryDocumentsDb.Create(database);
        var orchestrator = new ExtractionOrchestrator(
            new ExtractionRunStore(db, tenantContext, new FixedTestClock(), new RecordingAuditWriter()),
            gateway,
            ExtractionStages.Default,
            tenantContext);

        var result = await orchestrator.RunAsync(tenantId, documentId, Pages);

        Assert.True(result.IsSuccess);
        const string text = "[[PAGE 1]]\nabc\n\n[[PAGE 2]]\ndef\n\n";
        var expected = new Dictionary<string, string>
        {
            ["Metadata"] = StagedExtractionJsonSchemas.Facts(["supplier", "currency", "governingLaw", "status"]),
            ["CommercialTerms"] = StagedExtractionJsonSchemas.Facts(["annualSpend", "totalContractValue", "paymentTerms"]),
            ["DatesAndRenewalTerms"] = StagedExtractionJsonSchemas.Facts(
                ["startDate", "endDate", "effectiveDate", "cancellationDeadline", "autoRenewal", "renewalTermMonths", "noticePeriodDays"]),
            ["LineItems"] = StagedExtractionJsonSchemas.LineItems(),
            ["LegalClauses"] = StagedExtractionJsonSchemas.Clauses(),
            ["Obligations"] = StagedExtractionJsonSchemas.Obligations(),
            ["Risk"] = StagedExtractionJsonSchemas.Risks(),
        };

        Assert.Equal(7, gateway.Requests.Count);
        foreach (var request in gateway.Requests)
        {
            Assert.Equal(new AiExtractionRequest(request.StageName, text, expected[request.StageName]), request);
        }

        Assert.Equal(expected.Keys.Order(), gateway.Requests.Select(r => r.StageName).Order());
    }

    [Fact]
    public async Task A_run_stamps_run_and_step_on_the_gateway_audit_rows()
    {
        var (database, tenantId, documentId) = await SeedAsync();
        var tenantContext = new TenantContext();
        var gatewayAudit = new RecordingAuditWriter();
        var logging = new LoggingAiGateway(new RecordingGateway(), gatewayAudit, tenantContext, new AiGatewayComplianceOptions());
        await using var db = InMemoryDocumentsDb.Create(database);
        var orchestrator = new ExtractionOrchestrator(
            new ExtractionRunStore(db, tenantContext, new FixedTestClock(), new RecordingAuditWriter()),
            logging,
            ExtractionStages.Default,
            tenantContext);

        var result = await orchestrator.RunAsync(tenantId, documentId, Pages);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, gatewayAudit.Written.Count);
        Assert.All(gatewayAudit.Written, entry => Assert.DoesNotContain("run=none", entry.Detail));
        foreach (var stage in ExtractionPipeline.Stages)
        {
            Assert.Contains(gatewayAudit.Written, entry => entry.Detail!.Contains($"step={stage} ", StringComparison.Ordinal));
        }

        Assert.Null(RunContext_Current_RunId());
    }

    private static string? RunContext_Current_RunId() => Raffa.AiGateway.Telemetry.RunContext.Current?.RunId;
}
