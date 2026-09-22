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

    private const string TenantItemContractId = "11111111-1111-1111-1111-111111111111";
    private const string TenantItemDocumentId = "22222222-2222-2222-2222-222222222222";

    private static readonly PackItem TenantItem = new(
        "tenant:contract-1",
        PackCorpus.Tenant,
        "Salesforce · MSA 2024",
        "p.12 §8.4",
        Page: 12,
        Section: "8.4",
        Snippet: "Liability is capped at CHF 1,000,000.",
        Href: "/documents/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/viewer?page=12",
        PreviewUrl: "/api/documents/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/preview?page=12",
        RecordId: null,
        Provenance: "validated contract",
        Values: [],
        ContractId: TenantItemContractId,
        DocumentId: TenantItemDocumentId);

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

        // Ignored by the Answer branch (only the abstain branch ever reads recoveryActions) — a
        // non-empty, deliberately different list here would prove nothing extra this test doesn't
        // already prove via Assert.Same(actions, ...) below.
        var reply = CopilotReplyBuilder.FromGuardedResult(guarded, [TenantItem], actions, []);

        Assert.Equal(ReplyKind.Answer, reply.Kind);
        Assert.Equal("Liability is capped at CHF 1,000,000 [1].", reply.AnswerMarkdown);
        var citation = Assert.Single(reply.Citations);
        Assert.Equal(1, citation.N);
        Assert.Equal(PackCorpus.Tenant, citation.Corpus);

        // Task E28/F03/US01/T01 (NW-83; ADR-024 w19 cl. 17): real ids echoed from the pack item,
        // never PackItem.CitationKey standing in for DocumentId and never a null ContractId.
        Assert.Equal(TenantItemDocumentId, citation.DocumentId);
        Assert.Equal(TenantItemContractId, citation.ContractId);
        Assert.Same(actions, reply.Actions);
        Assert.Equal(["What is the notice period?"], reply.FollowUps);
        Assert.Equal("fixture-answer-model", reply.Provenance.ModelId);
        Assert.Equal("answer-v2.1", reply.Provenance.PromptVersion);
        Assert.Contains(PackCorpus.Tenant, reply.Provenance.Sources);
    }

    private static AiAnswerResult AbstainWith(string? reason, IReadOnlyList<string> followUps) =>
        new(
            CanDetermine: false,
            Answer: null,
            Citations: [],
            Metadata,
            AnswerMarkdown: null,
            CitationKeys: [],
            ActionKeys: [],
            AbstainReason: reason,
            FollowUps: followUps);

    [Fact]
    public void An_abstain_with_no_follow_ups_of_its_own_carries_the_hosts_next_step_questions()
    {
        var reply = CopilotReplyBuilder.FromGuardedResult(
            AbstainWith("No validated contract records legal fees.", []),
            [TenantItem],
            [],
            [],
            abstainFollowUps: ["Which contracts are most critical?", "Where can we save?"]);

        Assert.Equal(ReplyKind.Abstain, reply.Kind);
        Assert.Equal(["Which contracts are most critical?", "Where can we save?"], reply.FollowUps);
        Assert.Empty(reply.Citations);
    }

    [Fact]
    public void An_abstains_own_follow_ups_win_over_the_hosts()
    {
        var reply = CopilotReplyBuilder.FromGuardedResult(
            AbstainWith("No validated contract records legal fees.", ["Which invoices do we have?"]),
            [TenantItem],
            [],
            [],
            abstainFollowUps: ["Where can we save?"]);

        Assert.Equal(["Which invoices do we have?"], reply.FollowUps);
    }

    [Theory]
    [InlineData("The context pack does not contain a legal-fee figure.")]
    [InlineData("No citationKey in the pack supports this.")]
    [InlineData("actionKey 'raffa:renewals' does not resolve to any capability in the catalog (R-SYS-02).")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_abstain_reason_that_talks_about_the_machinery_becomes_plain_copy(string? reason)
    {
        var reply = CopilotReplyBuilder.FromGuardedResult(AbstainWith(reason, []), [TenantItem], [], []);

        Assert.Equal(CopilotReplyBuilder.DefaultAbstainReason, reply.AnswerMarkdown);
    }

    [Fact]
    public void A_plain_abstain_reason_is_shown_as_the_model_wrote_it()
    {
        var reply = CopilotReplyBuilder.FromGuardedResult(
            AbstainWith("No validated contract records legal fees.", []), [TenantItem], [], []);

        Assert.Equal("No validated contract records legal fees.", reply.AnswerMarkdown);
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

        // E25/F05/US01/T01 (ADR-024 "every abstain has a clickable next step"; AC-1/AC-2): the
        // abstain branch's own actions[] come from recoveryActions, resolved here the same
        // production way (CapabilityRouting.ResolveActions, not a hand-typed fake) — never from
        // the model's own (empty, above) ActionKeys.
        var routing = new CapabilityRouting();
        var recoveryActions = routing.ResolveActions(
            [CapabilityIntent.HowTo(CapabilityCatalog.AskKey)],
            new RoutingContext(ValidatedContractCount: 1, Role: CapabilityCallerRole.Standard));

        var reply = CopilotReplyBuilder.FromGuardedResult(guarded, [TenantItem], [], recoveryActions);

        Assert.Equal(ReplyKind.Abstain, reply.Kind);
        Assert.Equal("Nothing in the validated contracts supports a reliable answer.", reply.AnswerMarkdown);
        Assert.Empty(reply.Citations);
        Assert.NotEmpty(reply.Actions);
        Assert.Equal(recoveryActions, reply.Actions);
        Assert.All(reply.Actions, action => Assert.DoesNotContain('{', action.Href));
        Assert.Empty(reply.FollowUps);

        // Unlike RedirectReplyBuilder's deterministic replies (which never call the model at all —
        // see RedirectReplyBuilderTests), an abstain that came from a guarded/downgraded `answer`
        // role result did make a real model call; its reproducibility metadata is preserved rather
        // than nulled out (ADR-011 — see RegenerateOnce.DowngradeToAbstain's own doc comment).
        Assert.Equal("fixture-answer-model", reply.Provenance.ModelId);
        Assert.Equal("answer-v2.1", reply.Provenance.PromptVersion);
    }

    /// <summary>
    /// AC-2, concretely: even when the caller resolved a non-empty <c>actions</c> list from the
    /// model's own <see cref="AiAnswerResult.ActionKeys"/> (the shape the Answer branch would use),
    /// the abstain branch must ignore it entirely and surface only <c>recoveryActions</c> — an
    /// abstaining model has nothing grounded to suggest, so its action keys, resolved or not, must
    /// never reach the user.
    /// </summary>
    [Fact]
    public void An_abstain_reply_never_surfaces_the_answer_branchs_actions()
    {
        var guarded = new AiAnswerResult(
            CanDetermine: false,
            Answer: null,
            Citations: [],
            Metadata,
            AnswerMarkdown: null,
            CitationKeys: [],
            ActionKeys: ["quote-check"],
            AbstainReason: "Nothing in the validated contracts supports a reliable answer.",
            FollowUps: []);

        var modelAuthoredActions = new[] { new CopilotAction("Quote check →", "/quotes", CopilotActionKind.Navigate) };
        var recoveryActions = new[] { new CopilotAction("Upload a contract", "/documents", CopilotActionKind.Upload) };

        var reply = CopilotReplyBuilder.FromGuardedResult(guarded, [TenantItem], modelAuthoredActions, recoveryActions);

        Assert.Equal(ReplyKind.Abstain, reply.Kind);
        Assert.Same(recoveryActions, reply.Actions);
        Assert.DoesNotContain(modelAuthoredActions[0], reply.Actions);
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

        var reply = CopilotReplyBuilder.FromGuardedResult(guarded, [TenantItem, MarketItem], [], []);

        Assert.Equal(2, reply.Citations.Count);
        Assert.Equal(1, reply.Citations[0].N);
        Assert.Equal(PackCorpus.Market, reply.Citations[0].Corpus);
        Assert.Equal(2, reply.Citations[1].N);
        Assert.Equal(PackCorpus.Tenant, reply.Citations[1].Corpus);
    }

    [Fact]
    public void A_citation_key_left_in_the_prose_is_rendered_as_the_markers_the_reader_can_click()
    {
        var guarded = new AiAnswerResult(
            CanDetermine: true,
            Answer: null,
            Citations: [],
            Metadata,
            AnswerMarkdown: "Liability is capped at CHF 1,000,000.[tenant:contract-1] The market agrees [market:deal-1].",
            CitationKeys: ["tenant:contract-1"],
            ActionKeys: [],
            AbstainReason: null,
            FollowUps: []);

        var reply = CopilotReplyBuilder.FromGuardedResult(guarded, [TenantItem, MarketItem], [], []);

        Assert.Equal("Liability is capped at CHF 1,000,000. [1] The market agrees [2].", reply.AnswerMarkdown);
        Assert.Equal(2, reply.Citations.Count);
        Assert.Equal(PackCorpus.Market, reply.Citations[1].Corpus);
        Assert.Equal(2, reply.Citations[1].N);
    }

    [Fact]
    public void BuildCitations_silently_skips_a_key_the_pack_does_not_contain()
    {
        // Defensive only (GroundingGuard is what actually enforces this upstream) — one bad key
        // must not break every other, already-grounded citation in the same reply.
        var citations = CopilotReplyBuilder.BuildCitations(["tenant:contract-1", "not-in-pack"], [TenantItem]);

        var citation = Assert.Single(citations);
        Assert.Equal(TenantItemDocumentId, citation.DocumentId);
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

        var reply = CopilotReplyBuilder.FromGuardedResult(guarded, [TenantItem], actions, []);

        Assert.NotEmpty(reply.Actions);
        Assert.All(reply.Actions, action => Assert.DoesNotContain('{', action.Href));
    }

    [Fact]
    public void FromGuardedResult_rejects_null_arguments()
    {
        var guarded = new AiAnswerResult(false, null, [], Metadata);

        Assert.Throws<ArgumentNullException>(() => CopilotReplyBuilder.FromGuardedResult(null!, [], [], []));
        Assert.Throws<ArgumentNullException>(() => CopilotReplyBuilder.FromGuardedResult(guarded, null!, [], []));
        Assert.Throws<ArgumentNullException>(() => CopilotReplyBuilder.FromGuardedResult(guarded, [], null!, []));
        Assert.Throws<ArgumentNullException>(() => CopilotReplyBuilder.FromGuardedResult(guarded, [], [], null!));
    }
}
