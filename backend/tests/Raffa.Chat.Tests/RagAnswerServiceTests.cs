using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application;
using Raffa.Chat.Domain;
using Raffa.Chat.Tests.TestSupport;
using Raffa.SharedKernel;

namespace Raffa.Chat.Tests;

/// <summary>
/// Proves the Definition of Done for task E02/F04/US02/T01 (us-02-rag-citations): grounded answers
/// carry citations or an explicit "cannot determine" (AC-2), no evidence text/question text is
/// ever written to the audit trail (ADR-011), and a <see cref="QueryIntent.Structured"/> decision
/// is rejected rather than silently "answered" from evidence it never asked for. Retrieval itself
/// (AC-1/AC-3) is not exercised here — <see cref="RagAnswerService"/> has no database dependency at
/// all (see its own doc comment); that half of the story is proven end-to-end against a real
/// Postgres+RLS Testcontainer by
/// <c>Raffa.IntegrationTests.AskRaffaRagCrossTenantIsolationTests</c>.
///
/// <para>
/// Also proves task E02/F04/US02/T02 (abstain-guard) is actually wired into
/// <see cref="RagAnswerService.AnswerAsync"/> — that a "determined" gateway result with an
/// ungrounded citation or zero citations is downgraded to "cannot determine" before it ever reaches
/// a caller or the audit trail (<see cref="Answer_forces_cannot_determine_when_the_gateway_returns_an_ungrounded_citation"/>,
/// <see cref="Answer_forces_cannot_determine_when_the_gateway_claims_determined_with_zero_citations"/>).
/// <see cref="AbstainGuard"/>'s own exhaustive decision matrix (every branch, in isolation) is
/// covered by <c>Raffa.Chat.Tests.AbstainGuardTests</c> instead of being duplicated here.
/// </para>
/// </summary>
public sealed class RagAnswerServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static QueryRouteDecision SemanticDecision(string question = "What liability do we have with AWS?") =>
        new(question, QueryIntent.Semantic, "test fixture");

    private static AiEvidenceSnippet OneEvidenceSnippet() =>
        new("Document:11111111-1111-1111-1111-111111111111", Page: null, Section: "chunk 0", Text: "Liability is capped at $1,000,000.");

    [Fact]
    public async Task Answer_returns_citations_and_writes_one_audit_entry_without_leaking_content()
    {
        var tenantId = TenantId.New();
        var decision = SemanticDecision();
        var evidence = new[] { OneEvidenceSnippet() };
        var citation = new AiCitation(evidence[0].DocumentId, evidence[0].Page, evidence[0].Section);
        var metadata = new AiCallMetadata("fixture-answer-model", "v1", "fixture-v1", Now, "deadbeef");
        var gateway = new StubAnswerGateway
        {
            ResultToReturn = Result<AiAnswerResult>.Success(
                new AiAnswerResult(CanDetermine: true, Answer: "Liability is capped at $1,000,000.", [citation], metadata)),
        };
        var auditWriter = new RecordingAuditWriter();
        var service = new RagAnswerService(gateway, auditWriter, new FixedClock(Now), new AbstainGuard());

        var result = await service.AnswerAsync(tenantId, decision, evidence);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.CanDetermine);
        Assert.Single(result.Value.Citations);
        Assert.Equal(evidence[0].DocumentId, result.Value.Citations[0].DocumentId);

        // The gateway received exactly the caller-supplied question and evidence — no retrieval,
        // no rewriting, happened inside this service (see the type doc comment).
        Assert.Equal(decision.Question, gateway.LastRequest!.Question);
        Assert.Same(evidence, gateway.LastRequest.Evidence);

        var entry = Assert.Single(auditWriter.Written);
        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal("unattributed", entry.Actor);
        Assert.Equal("chat.answered", entry.Action);
        Assert.Equal("ask_raffa", entry.ResourceType);
        Assert.Equal(metadata.InputHash, entry.ResourceId);
        Assert.Equal(Now, entry.Timestamp);
        Assert.Contains("canDetermine=True", entry.Detail);
        Assert.Contains("citationCount=1", entry.Detail);
        Assert.Contains("evidenceCount=1", entry.Detail);
        Assert.Contains("abstainGuardIntervened=False", entry.Detail);

        // ADR-011: never write raw prompt or retrieved contract text to logs.
        Assert.DoesNotContain(decision.Question, entry.Detail);
        Assert.DoesNotContain(evidence[0].Text, entry.Detail);
        Assert.DoesNotContain(result.Value.Answer!, entry.Detail);
    }

    [Fact]
    public async Task Answer_reports_cannot_determine_for_empty_evidence_without_failing()
    {
        var tenantId = TenantId.New();
        var decision = SemanticDecision();
        var metadata = new AiCallMetadata("fixture-answer-model", "v1", "fixture-v1", Now, "cafebabe");
        var gateway = new StubAnswerGateway
        {
            ResultToReturn = Result<AiAnswerResult>.Success(
                new AiAnswerResult(CanDetermine: false, Answer: null, Citations: [], metadata)),
        };
        var auditWriter = new RecordingAuditWriter();
        var service = new RagAnswerService(gateway, auditWriter, new FixedClock(Now), new AbstainGuard());

        var result = await service.AnswerAsync(tenantId, decision, evidence: []);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.CanDetermine);
        Assert.Null(result.Value.Answer);
        Assert.Empty(result.Value.Citations);

        // Still a successful call from the gateway's point of view (an honest abstention, not an
        // error) — still audited, same as any other successful answer.
        var entry = Assert.Single(auditWriter.Written);
        Assert.Contains("canDetermine=False", entry.Detail);
        Assert.Contains("evidenceCount=0", entry.Detail);
        // Already an honest abstention from the gateway itself — the guard had nothing to correct.
        Assert.Contains("abstainGuardIntervened=False", entry.Detail);
    }

    [Fact]
    public async Task Answer_forces_cannot_determine_when_the_gateway_returns_an_ungrounded_citation()
    {
        var tenantId = TenantId.New();
        var decision = SemanticDecision();
        var evidence = new[] { OneEvidenceSnippet() };
        // A model that hallucinates a citation to a document never in the supplied evidence — the
        // exact failure mode AbstainGuard exists to catch (see its own doc comment); FixtureAiGateway
        // itself can never produce this (it only ever echoes the evidence's own DocumentId back).
        var hallucinatedCitation = new AiCitation("Document:99999999-9999-9999-9999-999999999999", Page: null, Section: "chunk 0");
        var metadata = new AiCallMetadata("fixture-answer-model", "v1", "fixture-v1", Now, "fabricated");
        var gateway = new StubAnswerGateway
        {
            ResultToReturn = Result<AiAnswerResult>.Success(
                new AiAnswerResult(CanDetermine: true, Answer: "Liability is capped at $1,000,000.", [hallucinatedCitation], metadata)),
        };
        var auditWriter = new RecordingAuditWriter();
        var service = new RagAnswerService(gateway, auditWriter, new FixedClock(Now), new AbstainGuard());

        var result = await service.AnswerAsync(tenantId, decision, evidence);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.CanDetermine);
        Assert.Null(result.Value.Answer);
        Assert.Empty(result.Value.Citations);
        // Reproducibility metadata survives the guard's intervention (ADR-011).
        Assert.Equal(metadata, result.Value.Metadata);

        var entry = Assert.Single(auditWriter.Written);
        Assert.Contains("canDetermine=False", entry.Detail);
        Assert.Contains("citationCount=0", entry.Detail);
        Assert.Contains("abstainGuardIntervened=True", entry.Detail);
    }

    [Fact]
    public async Task Answer_forces_cannot_determine_when_the_gateway_claims_determined_with_zero_citations()
    {
        var tenantId = TenantId.New();
        var decision = SemanticDecision();
        var evidence = new[] { OneEvidenceSnippet() };
        var metadata = new AiCallMetadata("fixture-answer-model", "v1", "fixture-v1", Now, "unsupported");
        var gateway = new StubAnswerGateway
        {
            // Determined, but nothing backs it — an unsupported claim (Appendix C rule 2).
            ResultToReturn = Result<AiAnswerResult>.Success(
                new AiAnswerResult(CanDetermine: true, Answer: "Liability is capped at $1,000,000.", Citations: [], metadata)),
        };
        var auditWriter = new RecordingAuditWriter();
        var service = new RagAnswerService(gateway, auditWriter, new FixedClock(Now), new AbstainGuard());

        var result = await service.AnswerAsync(tenantId, decision, evidence);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.CanDetermine);
        Assert.Null(result.Value.Answer);

        var entry = Assert.Single(auditWriter.Written);
        Assert.Contains("abstainGuardIntervened=True", entry.Detail);
    }

    [Fact]
    public async Task Answer_does_not_audit_a_failed_gateway_call()
    {
        var tenantId = TenantId.New();
        var decision = SemanticDecision();
        var gateway = new StubAnswerGateway { ResultToReturn = Result<AiAnswerResult>.Failure("gateway exploded") };
        var auditWriter = new RecordingAuditWriter();
        var service = new RagAnswerService(gateway, auditWriter, new FixedClock(Now), new AbstainGuard());

        var result = await service.AnswerAsync(tenantId, decision, evidence: []);

        Assert.True(result.IsFailure);
        Assert.Equal("gateway exploded", result.Error);
        Assert.Empty(auditWriter.Written);
    }

    [Fact]
    public async Task Answer_rejects_a_structured_decision()
    {
        var tenantId = TenantId.New();
        var structuredDecision = new QueryRouteDecision("What is our annual spend?", QueryIntent.Structured, "test fixture");
        var gateway = new StubAnswerGateway();
        var service = new RagAnswerService(gateway, new RecordingAuditWriter(), new FixedClock(Now), new AbstainGuard());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AnswerAsync(tenantId, structuredDecision, evidence: []));
    }

    [Fact]
    public async Task Answer_rejects_a_null_decision()
    {
        var service = new RagAnswerService(new StubAnswerGateway(), new RecordingAuditWriter(), new FixedClock(Now), new AbstainGuard());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.AnswerAsync(TenantId.New(), null!, evidence: []));
    }

    [Fact]
    public async Task Answer_rejects_null_evidence()
    {
        var service = new RagAnswerService(new StubAnswerGateway(), new RecordingAuditWriter(), new FixedClock(Now), new AbstainGuard());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.AnswerAsync(TenantId.New(), SemanticDecision(), evidence: null!));
    }

    /// <summary>
    /// Minimal, fully-controllable <see cref="IAiGateway"/> fake — only <see cref="AnswerAsync"/>
    /// is exercised by <see cref="RagAnswerService"/>; every other role throws so a test that
    /// accidentally exercises one fails loudly instead of silently returning a default. Mirrors
    /// <c>Raffa.Documents.Contracts.Tests.EmbeddingRetrievalServiceTests.StubEmbeddingGateway</c>'s
    /// identical "throw on unexercised roles" shape.
    /// </summary>
    private sealed class StubAnswerGateway : IAiGateway
    {
        public Result<AiAnswerResult> ResultToReturn { get; set; } = Result<AiAnswerResult>.Failure("not configured");

        public AiAnswerRequest? LastRequest { get; private set; }

        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by RagAnswerService.");

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by RagAnswerService.");

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by RagAnswerService.");

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(ResultToReturn);
        }

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by RagAnswerService.");
    }
}
