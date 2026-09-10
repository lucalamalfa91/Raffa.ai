using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application;

namespace Raffa.Chat.Tests;

/// <summary>
/// Proves the Definition of Done for task E02/F04/US02/T02 (us-02-rag-citations, abstain-guard):
/// <see cref="AbstainGuard.Enforce"/>'s full decision matrix, in isolation — every way a gateway
/// `answer` result can be trusted as-is, and every way it must instead be downgraded to an honest
/// "cannot determine" rather than let an unsupported or fabricated claim through (Appendix C rule 2:
/// "Never show a consequential extracted fact without source evidence"; rule 10: "If data quality is
/// insufficient, return uncertainty instead of fabricated precision"). <see cref="RagAnswerService"/>
/// wiring this guard into its own <c>AnswerAsync</c> call (so the guarded result — not the gateway's
/// raw claim — is what a caller and the audit trail actually see) is proven separately by
/// <c>Raffa.Chat.Tests.RagAnswerServiceTests</c>, not duplicated here.
/// </summary>
public sealed class AbstainGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly AiCallMetadata Metadata = new("fixture-answer-model", "v1", "fixture-v1", Now, "deadbeef");

    private const string GroundedDocumentId = "Document:11111111-1111-1111-1111-111111111111";
    private const string OtherDocumentId = "Document:22222222-2222-2222-2222-222222222222";

    private static AiEvidenceSnippet GroundedEvidence() =>
        new(GroundedDocumentId, Page: null, Section: "chunk 0", Text: "Liability is capped at $1,000,000.");

    [Fact]
    public void Enforce_passes_through_an_already_honest_abstention_unchanged()
    {
        var abstained = new AiAnswerResult(CanDetermine: false, Answer: null, Citations: [], Metadata);
        var guard = new AbstainGuard();

        var outcome = guard.Enforce(abstained, evidence: []);

        Assert.False(outcome.Intervened);
        Assert.Null(outcome.Reason);
        Assert.Equal(abstained, outcome.Result);
    }

    [Fact]
    public void Enforce_passes_through_a_fully_grounded_answer_unchanged()
    {
        var evidence = new[] { GroundedEvidence() };
        var citation = new AiCitation(GroundedDocumentId, Page: null, Section: "chunk 0");
        var grounded = new AiAnswerResult(CanDetermine: true, Answer: "Liability is capped at $1,000,000.", [citation], Metadata);
        var guard = new AbstainGuard();

        var outcome = guard.Enforce(grounded, evidence);

        Assert.False(outcome.Intervened);
        Assert.Null(outcome.Reason);
        Assert.Equal(grounded, outcome.Result);
    }

    [Fact]
    public void Enforce_passes_through_when_every_citation_matches_some_evidence_document_even_out_of_order()
    {
        var evidence = new[]
        {
            GroundedEvidence(),
            new AiEvidenceSnippet(OtherDocumentId, Page: 3, Section: "8.4", Text: "Cross-referenced clause."),
        };
        // Citation order need not match evidence order — grounding is a set-membership check, not
        // a positional one.
        var citations = new[]
        {
            new AiCitation(OtherDocumentId, Page: 3, Section: "8.4"),
            new AiCitation(GroundedDocumentId, Page: null, Section: "chunk 0"),
        };
        var result = new AiAnswerResult(CanDetermine: true, Answer: "Combined answer.", citations, Metadata);
        var guard = new AbstainGuard();

        var outcome = guard.Enforce(result, evidence);

        Assert.False(outcome.Intervened);
        Assert.Same(citations, outcome.Result.Citations);
    }

    [Fact]
    public void Enforce_abstains_when_determined_with_zero_citations()
    {
        var evidence = new[] { GroundedEvidence() };
        var unsupported = new AiAnswerResult(CanDetermine: true, Answer: "Liability is capped at $1,000,000.", Citations: [], Metadata);
        var guard = new AbstainGuard();

        var outcome = guard.Enforce(unsupported, evidence);

        Assert.True(outcome.Intervened);
        Assert.NotNull(outcome.Reason);
        Assert.False(outcome.Result.CanDetermine);
        Assert.Null(outcome.Result.Answer);
        Assert.Empty(outcome.Result.Citations);
        Assert.Equal(Metadata, outcome.Result.Metadata);
    }

    [Fact]
    public void Enforce_abstains_when_a_citation_does_not_match_any_evidence_document()
    {
        var evidence = new[] { GroundedEvidence() };
        var hallucinated = new AiCitation(OtherDocumentId, Page: null, Section: "chunk 0");
        var result = new AiAnswerResult(CanDetermine: true, Answer: "Liability is capped at $1,000,000.", [hallucinated], Metadata);
        var guard = new AbstainGuard();

        var outcome = guard.Enforce(result, evidence);

        Assert.True(outcome.Intervened);
        Assert.Contains(OtherDocumentId, outcome.Reason);
        Assert.False(outcome.Result.CanDetermine);
        Assert.Empty(outcome.Result.Citations);
    }

    [Fact]
    public void Enforce_abstains_when_only_one_of_several_citations_is_ungrounded()
    {
        // Partial fabrication must be caught too — a mostly-grounded answer with one hallucinated
        // citation mixed in is still an unsupported claim, not "mostly fine".
        var evidence = new[] { GroundedEvidence() };
        var citations = new[]
        {
            new AiCitation(GroundedDocumentId, Page: null, Section: "chunk 0"),
            new AiCitation(OtherDocumentId, Page: null, Section: "chunk 1"),
        };
        var result = new AiAnswerResult(CanDetermine: true, Answer: "Combined answer.", citations, Metadata);
        var guard = new AbstainGuard();

        var outcome = guard.Enforce(result, evidence);

        Assert.True(outcome.Intervened);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Enforce_abstains_when_answer_text_is_missing_despite_determined(string? answer)
    {
        var evidence = new[] { GroundedEvidence() };
        var citation = new AiCitation(GroundedDocumentId, Page: null, Section: "chunk 0");
        var result = new AiAnswerResult(CanDetermine: true, answer, [citation], Metadata);
        var guard = new AbstainGuard();

        var outcome = guard.Enforce(result, evidence);

        Assert.True(outcome.Intervened);
        Assert.False(outcome.Result.CanDetermine);
    }

    [Fact]
    public void Enforce_document_id_matching_is_exact_and_case_sensitive()
    {
        // Deliberate strictness: a guard whose whole job is gating fabrication must never silently
        // loosen its own matching rule (Appendix C rule 10 — uncertainty over invented leniency).
        var evidence = new[] { GroundedEvidence() };
        var differentCaseCitation = new AiCitation(GroundedDocumentId.ToUpperInvariant(), Page: null, Section: "chunk 0");
        var result = new AiAnswerResult(CanDetermine: true, Answer: "Liability is capped at $1,000,000.", [differentCaseCitation], Metadata);
        var guard = new AbstainGuard();

        var outcome = guard.Enforce(result, evidence);

        Assert.True(outcome.Intervened);
    }

    [Fact]
    public void Enforce_rejects_a_null_result()
    {
        var guard = new AbstainGuard();

        Assert.Throws<ArgumentNullException>(() => guard.Enforce(null!, evidence: []));
    }

    [Fact]
    public void Enforce_rejects_null_evidence()
    {
        var guard = new AbstainGuard();
        var result = new AiAnswerResult(CanDetermine: false, Answer: null, Citations: [], Metadata);

        Assert.Throws<ArgumentNullException>(() => guard.Enforce(result, evidence: null!));
    }
}
