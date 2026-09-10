using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Logging;
using Raffa.AiGateway.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiGateway.Tests.Logging;

/// <summary>
/// Proves task E02/F01/US01/T02's coding objective — "Log model/version/prompt/timestamp/
/// input-hash; no-training config" — against <see cref="LoggingAiGateway"/> directly, independent
/// of which <see cref="IAiGateway"/> it wraps. Uses <see cref="FixtureAiGateway"/> as the inner
/// gateway (the only implementation that exists today) purely as a metadata producer; these tests
/// are about the decorator's own behaviour, not the fixture's classification/extraction logic
/// (already covered by <c>FixtureAiGateway*Tests</c>).
/// </summary>
public class LoggingAiGatewayTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static (LoggingAiGateway Gateway, RecordingAuditWriter AuditWriter, TenantContext TenantContext) CreateGateway(
        AiGatewayModelOptions? modelOptions = null,
        AiGatewayComplianceOptions? complianceOptions = null)
    {
        var inner = new FixtureAiGateway(modelOptions ?? new AiGatewayModelOptions(), new FixedClock(Now));
        var auditWriter = new RecordingAuditWriter();
        var tenantContext = new TenantContext();
        var gateway = new LoggingAiGateway(
            inner, auditWriter, tenantContext, complianceOptions ?? new AiGatewayComplianceOptions());

        return (gateway, auditWriter, tenantContext);
    }

    [Fact]
    public async Task Classify_success_writes_one_audit_entry_with_reproducibility_fields()
    {
        var (gateway, auditWriter, tenantContext) = CreateGateway();
        var tenantId = TenantId.New();

        using (tenantContext.BeginScope(tenantId))
        {
            var result = await gateway.ClassifyAsync(
                new AiClassificationRequest("This MASTER SERVICES AGREEMENT is entered into as of ..."));

            Assert.True(result.IsSuccess);
            Assert.Equal(AiDocumentType.Msa, result.Value.DocumentType);

            var entry = Assert.Single(auditWriter.Written);
            var detail = entry.Detail ?? throw new InvalidOperationException("expected Detail to be set");

            Assert.Equal(tenantId, entry.TenantId);
            Assert.Equal("ai-gateway", entry.Actor);
            Assert.Equal("ai.classified", entry.Action);
            Assert.Equal("ai_call", entry.ResourceType);
            Assert.Equal(result.Value.Metadata.InputHash, entry.ResourceId);
            Assert.Equal(Now, entry.Timestamp);
            Assert.Contains(result.Value.Metadata.ModelId, detail, StringComparison.Ordinal);
            Assert.Contains(result.Value.Metadata.ModelVersion, detail, StringComparison.Ordinal);
            Assert.Contains(result.Value.Metadata.PromptVersion, detail, StringComparison.Ordinal);
            Assert.Contains(result.Value.Metadata.InputHash, detail, StringComparison.Ordinal);
            Assert.Contains("noTraining=True", detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Extract_success_writes_one_audit_entry()
    {
        var (gateway, auditWriter, tenantContext) = CreateGateway();

        using (tenantContext.BeginScope(TenantId.New()))
        {
            var result = await gateway.ExtractAsync(new AiExtractionRequest(
                StageName: "commercial-terms",
                DocumentText: "Auto-renewal for 12 months, 90 days cancellation notice.",
                JsonSchema: """{"type":"object","properties":{"auto_renewal":{"type":"boolean"}}}"""));

            Assert.True(result.IsSuccess);

            var entry = Assert.Single(auditWriter.Written);
            Assert.Equal("ai.extracted", entry.Action);
            Assert.Equal(result.Value.Metadata.InputHash, entry.ResourceId);
        }
    }

    [Fact]
    public async Task Embed_success_writes_one_audit_entry()
    {
        var (gateway, auditWriter, tenantContext) = CreateGateway();

        using (tenantContext.BeginScope(TenantId.New()))
        {
            var result = await gateway.EmbedAsync(new AiEmbeddingRequest("Limitation of liability clause."));

            Assert.True(result.IsSuccess);

            var entry = Assert.Single(auditWriter.Written);
            Assert.Equal("ai.embedded", entry.Action);
            Assert.Equal(result.Value.Metadata.InputHash, entry.ResourceId);
        }
    }

    [Fact]
    public async Task Answer_success_writes_one_audit_entry()
    {
        var (gateway, auditWriter, tenantContext) = CreateGateway();
        var evidence = new AiEvidenceSnippet(
            "doc-123", Page: 12, Section: "8.4", Text: "Liability is capped at 12 months' fees.");

        using (tenantContext.BeginScope(TenantId.New()))
        {
            var result = await gateway.AnswerAsync(
                new AiAnswerRequest("What liability do we have with AWS?", Evidence: [evidence]));

            Assert.True(result.IsSuccess);

            var entry = Assert.Single(auditWriter.Written);
            Assert.Equal("ai.answered", entry.Action);
            Assert.Equal(result.Value.Metadata.InputHash, entry.ResourceId);
        }
    }

    [Fact]
    public async Task Ocr_success_writes_one_audit_entry_including_page_count()
    {
        var (gateway, auditWriter, tenantContext) = CreateGateway();

        using (tenantContext.BeginScope(TenantId.New()))
        {
            var content = System.Text.Encoding.UTF8.GetBytes("page one\fpage two\fpage three");
            var result = await gateway.OcrAsync(new AiOcrRequest("contract.pdf", "application/pdf", content));

            Assert.True(result.IsSuccess);
            Assert.Equal(3, result.Value.Pages.Count);

            var entry = Assert.Single(auditWriter.Written);
            var detail = entry.Detail ?? throw new InvalidOperationException("expected Detail to be set");

            Assert.Equal("ai.ocr", entry.Action);
            Assert.Equal(result.Value.Metadata.InputHash, entry.ResourceId);
            // ADR-017: OCR calls must also log page count so spend is observable — the one field
            // the other four roles' log line does not carry.
            Assert.Contains("pageCount=3", detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Failed_ocr_call_does_not_write_an_audit_entry()
    {
        var (gateway, auditWriter, tenantContext) = CreateGateway();

        using (tenantContext.BeginScope(TenantId.New()))
        {
            var result = await gateway.OcrAsync(
                new AiOcrRequest("empty.pdf", "application/pdf", ReadOnlyMemory<byte>.Empty));

            Assert.True(result.IsFailure);
            Assert.Empty(auditWriter.Written);
        }
    }

    [Fact]
    public async Task Token_usage_is_logged_when_the_provider_reports_it()
    {
        var auditWriter = new RecordingAuditWriter();
        var tenantContext = new TenantContext();
        var gateway = new LoggingAiGateway(
            new UsageReportingGateway(), auditWriter, tenantContext, new AiGatewayComplianceOptions());

        using (tenantContext.BeginScope(TenantId.New()))
        {
            var result = await gateway.ClassifyAsync(new AiClassificationRequest("MASTER SERVICES AGREEMENT text"));
            Assert.True(result.IsSuccess);
        }

        var entry = Assert.Single(auditWriter.Written);
        var detail = entry.Detail ?? throw new InvalidOperationException("expected Detail to be set");
        Assert.Contains("gpt-5.4-nano-dev", detail, StringComparison.Ordinal);
        Assert.Contains("promptTokens=120", detail, StringComparison.Ordinal);
        Assert.Contains("completionTokens=9", detail, StringComparison.Ordinal);
    }

    /// <summary>An inner gateway that reports token usage the way the live Foundry clients do.</summary>
    private sealed class UsageReportingGateway : IAiGateway
    {
        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<AiClassificationResult>.Success(new AiClassificationResult(
                AiDocumentType.Msa,
                0.9,
                new AiCallMetadata("gpt-5.4-nano-dev", "2026-03-17", "foundry-classify-v2", Now, "hash", new AiTokenUsage(120, 9)))));

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Failed_call_does_not_write_an_audit_entry()
    {
        var (gateway, auditWriter, tenantContext) = CreateGateway();

        using (tenantContext.BeginScope(TenantId.New()))
        {
            var result = await gateway.ClassifyAsync(new AiClassificationRequest(""));

            Assert.True(result.IsFailure);
            Assert.Empty(auditWriter.Written);
        }
    }

    [Fact]
    public async Task Logging_without_an_active_tenant_scope_throws_and_writes_nothing()
    {
        var (gateway, auditWriter, _) = CreateGateway();

        // No BeginScope entered on this tenantContext: ITenantContext.Current is null for the
        // duration of this call, same precondition
        // Raffa.Tenancy.Tests.TenantRlsCrossTenantIsolationTests documents for the RLS backstop.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.ClassifyAsync(new AiClassificationRequest("MASTER SERVICES AGREEMENT text")));

        Assert.Empty(auditWriter.Written);
    }

    [Fact]
    public void Constructor_throws_when_no_training_is_disabled()
    {
        var inner = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now));
        var auditWriter = new RecordingAuditWriter();
        var tenantContext = new TenantContext();
        var complianceOptions = new AiGatewayComplianceOptions { NoTraining = false };

        Assert.Throws<InvalidOperationException>(
            () => new LoggingAiGateway(inner, auditWriter, tenantContext, complianceOptions));
    }

    [Fact]
    public async Task Audit_detail_never_contains_the_raw_document_text()
    {
        var (gateway, auditWriter, tenantContext) = CreateGateway();
        const string secretClause = "SECRET-CLAUSE-YOU-MUST-NEVER-LOG-VERBATIM";

        using (tenantContext.BeginScope(TenantId.New()))
        {
            await gateway.ClassifyAsync(
                new AiClassificationRequest($"MASTER SERVICES AGREEMENT {secretClause}"));
        }

        var entry = Assert.Single(auditWriter.Written);
        var detail = entry.Detail ?? throw new InvalidOperationException("expected Detail to be set");

        Assert.DoesNotContain(secretClause, detail, StringComparison.Ordinal);
    }
}
