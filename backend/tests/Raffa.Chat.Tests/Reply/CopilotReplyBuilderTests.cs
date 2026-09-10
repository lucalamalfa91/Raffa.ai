using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Reply;
using Raffa.SharedKernel;

namespace Raffa.Chat.Tests.Reply;

/// <summary>
/// Proves task E13/F06/US01/T01's reply builder (ask-engine coding objective point 6; ADR-024 §6):
/// a guarded <see cref="AiAnswerResult"/> becomes the exact <c>{ kind, answerMarkdown, citations[],
/// actions[], provenance, followUps[] }</c> reply contract. Includes this task's own Definition of
/// Done scenario — "actions never contain `{`" — proven here against a real,
/// <see cref="CapabilityRouting.ResolveActions"/>-resolved action list (not a hand-typed fake),
/// since that is exactly what the composition root hands this builder in production.
/// </summary>
public sealed class CopilotReplyBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly AiCallMetadata Metadata = new("fixture-answer-model", "v1", "answer-v2.1", Now, "deadbeef");

    private static readonly PackItem TenantItem = new(
        "tenant:contract-1",
        PackCorpus.Tenant,
        "Salesforce · MSA 2024",
        "p.12 §8.4",
        Page: 12,
        Section: "8.4",
        Snippet: "Liability is capped at CHF 1,000,000.",
        Href: "/contracts/11111111-1111-1111-1111-111111111111",
        PreviewUrl: "/documents/preview/1",
        RecordId: null,
        Provenance: "validated contract",
        Values: []);

    private static readonly PackItem MarketItem = new(
        "market:deal-1",
        PackCorpus.Market,
        "Category · Market feed",
        "Representative market data",
        Page: null,
        Section: null,
        Snippet: "Market P50 unit price is CHF 132.",
        Href: null,
        PreviewUrl: null,
        RecordId: "record-1",
        Provenance: "representative market data · mock feed",
        Values: []);

    [Fact]
    public void A_determined_result_becomes_an_answer_reply_with_citations_and_actions()
    {
        var guarded = new AiAnswerResult(
            CanDetermine: true,
            Answer: "Liability is capped at CHF 1,000,000 [1].",
            Citations: [],
            Metadata,
            AnswerMarkdown: "Liability is capped at CHF 1,000,000 [1].",
            CitationKeys: ["tenant:contract-1"],
            ActionKeys: [],
            AbstainReason: null,
            FollowUps: ["What is the notice period?"]);

        var actions = new[] { new CopilotAction("Open Contract 360", "/contracts/1", CopilotActionKind.Navigate) };

        var reply = CopilotReplyBuilder.FromGuardedResult(guarded, [TenantItem], actions);

        Assert.Equal(ReplyKind.Answer, reply.Kind);
        Assert.Equal("Liability is capped at CHF 1,000,000 [1].", reply.AnswerMarkdown);
        var citation = Assert.Single(reply.Citations);
        Assert.Equal(1, citation.N);
        Assert.Equal(PackCorpus.Tenant, citation.Corpus);
        Assert.Equal("tenant:contract-1", citation.DocumentId);
        Assert.Same(actions, reply.Actions);
        Assert.Equal(["What is the notice period?"], reply.FollowUps);
        Assert.Equal("fixture-answer-model", reply.Provenance.ModelId);
        Assert.Equal("answer-v2.1", reply.Provenance.PromptVersion);
        Assert.Contains(PackCorpus.Tenant, reply.Provenance.Sources);
    }

    [Fact]
    public void An_undetermined_result_becomes_an_abstain_reply_with_no_citations_or_follow_ups()
    {
        var guarded = new AiAnswerResult(
            CanDetermine: false,
            Answer: null,
            Citations: [],
            Metadata,
            AnswerMarkdown: null,
            CitationKeys: [],
            ActionKeys: [],
            AbstainReason: "Nothing in the validated contracts supports a reliable answer.",
            FollowUps: []);

        var reply = CopilotReplyBuilder.FromGuardedResult(guarded, [TenantItem], []);

        Assert.Equal(ReplyKind.Abstain, reply.Kind);
        Assert.Equal("Nothing in the validated contracts supports a reliable answer.", reply.AnswerMarkdown);
        Assert.Empty(reply.Citations);
        Assert.Empty(reply.Actions);
        Assert.Empty(reply.FollowUps);

        // Unlike RedirectReplyBuilder's deterministic replies (which never call the model at all —
        // see RedirectReplyBuilderTests), an abstain that came from a guarded/downgraded `answer`
        // role result did make a real model call; its reproducibility metadata is preserved rather
        // than nulled out (ADR-011 — see RegenerateOnce.DowngradeToAbstain's own doc comment).
        Assert.Equal("fixture-answer-model", reply.Provenance.ModelId);
        Assert.Equal("answer-v2.1", reply.Provenance.PromptVersion);
    }

    [Fact]
    public void Citations_are_numbered_in_citation_key_order_not_pack_order()
    {
        var guarded = new AiAnswerResult(
            CanDetermine: true,
            Answer: "Combined [1] and [2].",
            Citations: [],
            Metadata,
            AnswerMarkdown: "Combined [1] and [2].",
            CitationKeys: ["market:deal-1", "tenant:contract-1"],
            ActionKeys: [],
            AbstainReason: null,
            FollowUps: []);

        var reply = CopilotReplyBuilder.FromGuardedResult(guarded, [TenantItem, MarketItem], []);

        Assert.Equal(2, reply.Citations.Count);
        Assert.Equal(1, reply.Citations[0].N);
        Assert.Equal(PackCorpus.Market, reply.Citations[0].Corpus);
        Assert.Equal(2, reply.Citations[1].N);
        Assert.Equal(PackCorpus.Tenant, reply.Citations[1].Corpus);
    }

    [Fact]
    public void BuildCitations_silently_skips_a_key_the_pack_does_not_contain()
    {
        // Defensive only (GroundingGuard is what actually enforces this upstream) — one bad key
        // must not break every other, already-grounded citation in the same reply.
        var citations = CopilotReplyBuilder.BuildCitations(["tenant:contract-1", "not-in-pack"], [TenantItem]);

        var citation = Assert.Single(citations);
        Assert.Equal("tenant:contract-1", citation.DocumentId);
    }

    [Fact]
    public void Actions_resolved_by_the_real_capability_router_never_contain_an_unresolved_placeholder()
    {
        // This task's own Definition of Done: "actions never contain `{`" — proven against the
        // production CapabilityRouting.ResolveActions path, the only real producer of actions[].
        var routing = new CapabilityRouting();
        var context = new RoutingContext(
            ValidatedContractCount: 1, Role: CapabilityCallerRole.Standard, ContractId: EntityId.New());
        var actions = routing.ResolveActions(
            [CapabilityIntent.Benchmark, CapabilityIntent.Deadline, CapabilityIntent.HowTo("contract-360")],
            context);

        var guarded = new AiAnswerResult(
            CanDetermine: true,
            Answer: "Answer [1].",
            Citations: [],
            Metadata,
            AnswerMarkdown: "Answer [1].",
            CitationKeys: ["tenant:contract-1"],
            ActionKeys: [],
            AbstainReason: null,
            FollowUps: []);

        var reply = CopilotReplyBuilder.FromGuardedResult(guarded, [TenantItem], actions);

        Assert.NotEmpty(reply.Actions);
        Assert.All(reply.Actions, action => Assert.DoesNotContain('{', action.Href));
    }

    [Fact]
    public void FromGuardedResult_rejects_null_arguments()
    {
        var guarded = new AiAnswerResult(false, null, [], Metadata);

        Assert.Throws<ArgumentNullException>(() => CopilotReplyBuilder.FromGuardedResult(null!, [], []));
        Assert.Throws<ArgumentNullException>(() => CopilotReplyBuilder.FromGuardedResult(guarded, null!, []));
        Assert.Throws<ArgumentNullException>(() => CopilotReplyBuilder.FromGuardedResult(guarded, [], null!));
    }
}
