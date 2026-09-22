using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Answering;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Tests.TestSupport;
using Raffa.SharedKernel;

namespace Raffa.Chat.Tests.Answering;

/// <summary>
/// Proves <see cref="AnswerComposer"/>'s guard pipeline end to end over a scripted gateway: a
/// model echoing a feature item's citation key as its action key (<c>raffa:renewals</c> — the live
/// "Where can I save the most this quarter?" abstain) is answered on the first attempt; a model
/// that tried to answer but failed the guards twice gets the pack's own facts as an answer, never
/// the old "Showing the pack's own facts instead" abstain; a model that declines is regenerated
/// once with persona v2.4's never-decline rule named (unless the caller keeps the decline for an
/// interview); and a draft that relies on no pack item passes without a citation, still under the
/// numeric guard.
/// </summary>
public sealed class AnswerComposerTests
{
    private const string Question = "Where can I save the most this quarter?";

    private static readonly AiCallMetadata Metadata =
        new("scripted", "v1", AnswerPromptV2.Version, new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero), "00");

    private static AiAnswerResult Answer(string markdown, IReadOnlyList<string> citationKeys, IReadOnlyList<string>? actionKeys = null) =>
        new(
            CanDetermine: true,
            Answer: markdown,
            Citations: [],
            Metadata: Metadata,
            AnswerMarkdown: markdown,
            CitationKeys: citationKeys,
            ActionKeys: actionKeys ?? [],
            AbstainReason: null,
            FollowUps: ["Which should we start first?"]);

    private static AiAnswerResult Abstain(string reason) =>
        new(
            CanDetermine: false,
            Answer: null,
            Citations: [],
            Metadata: Metadata,
            AnswerMarkdown: null,
            CitationKeys: [],
            ActionKeys: [],
            AbstainReason: reason,
            FollowUps: []);

    // A grounded model answer: every figure is a pack value, every marker a listed key.
    private static AiAnswerResult GroundedModelAnswer(IReadOnlyList<string> actionKeys) =>
        Answer(
            "Oracle is the biggest lever: EUR 15365 to EUR 30730 inside the window [1].",
            ["calc:candidate[1]"],
            actionKeys);

    // EUR 99999 is in no pack value and no snippet — NumericGuard rejects it.
    private static AiAnswerResult FabricatedModelAnswer() =>
        Answer("You can save EUR 99999 with Oracle [1].", ["calc:candidate[1]"]);

    [Fact]
    public async Task A_raffa_prefixed_action_key_no_longer_fails_a_grounded_answer()
    {
        var gateway = new ScriptedAnswerGateway(GroundedModelAnswer(["raffa:renewals"]));

        var result = await new AnswerComposer(gateway).AnswerAsync(Question, ScreenshotSavingsPack.Build(), []);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, gateway.Calls);
        Assert.False(result.Value.GuardIntervened);
        Assert.False(result.Value.FallbackUsed);
        Assert.True(result.Value.Result.CanDetermine);
        Assert.Equal([CapabilityCatalog.RenewalsKey], result.Value.Result.ActionKeys);
    }

    [Fact]
    public async Task An_unknown_action_key_loses_its_button_not_the_answer()
    {
        var gateway = new ScriptedAnswerGateway(GroundedModelAnswer(["negotiate-now", "/savings"]));

        var result = await new AnswerComposer(gateway).AnswerAsync(Question, ScreenshotSavingsPack.Build(), []);

        Assert.Equal(1, gateway.Calls);
        Assert.True(result.Value.Result.CanDetermine);
        Assert.Equal([CapabilityCatalog.SavingsKey], result.Value.Result.ActionKeys);
    }

    [Fact]
    public async Task Two_guard_failures_give_the_packs_own_facts_as_an_answer()
    {
        var gateway = new ScriptedAnswerGateway(FabricatedModelAnswer(), FabricatedModelAnswer());
        var pack = ScreenshotSavingsPack.Build();

        var result = await new AnswerComposer(gateway).AnswerAsync(Question, pack, []);

        Assert.Equal(2, gateway.Calls);
        Assert.True(result.Value.GuardIntervened);
        Assert.True(result.Value.FallbackUsed);
        Assert.NotNull(result.Value.GuardViolation);

        var answer = result.Value.Result;
        Assert.True(answer.CanDetermine);
        Assert.StartsWith("Here's where you can save and what to negotiate", answer.AnswerMarkdown!, StringComparison.Ordinal);
        Assert.DoesNotContain("99999", answer.AnswerMarkdown!, StringComparison.Ordinal);
        Assert.True(GroundingGuard.Validate(answer, pack).Passed);
        Assert.True(NumericGuard.Validate(answer.AnswerMarkdown, pack).Passed);
    }

    [Fact]
    public async Task A_failed_retry_call_after_a_guard_failure_still_answers_from_the_pack()
    {
        var gateway = new ScriptedAnswerGateway(FabricatedModelAnswer());

        var result = await new AnswerComposer(gateway).AnswerAsync(Question, ScreenshotSavingsPack.Build(), []);

        Assert.Equal(2, gateway.Calls);
        Assert.True(result.Value.FallbackUsed);
        Assert.True(result.Value.Result.CanDetermine);
    }

    [Fact]
    public async Task A_retry_that_retreats_to_an_abstain_after_trying_to_answer_gets_the_packs_facts()
    {
        var gateway = new ScriptedAnswerGateway(FabricatedModelAnswer(), Abstain("The pack does not support a figure."));

        var result = await new AnswerComposer(gateway).AnswerAsync(Question, ScreenshotSavingsPack.Build(), []);

        Assert.True(result.Value.FallbackUsed);
        Assert.True(result.Value.Result.CanDetermine);
    }

    // The live "mi aiuti a creare una mail che posso inviare per il rinnovo?" decline, answered on
    // the retry with a ready-to-send draft that relies on no pack item: every contract detail is a
    // bracketed placeholder, so there is nothing to cite.
    private const string EmailQuestion = "mi aiuti a creare una mail che posso inviare per il rinnovo?";

    private static AiAnswerResult UncitedDraft(string markdown) => Answer(markdown, []);

    [Fact]
    public async Task A_decline_is_regenerated_once_with_the_never_decline_rule_named()
    {
        var gateway = new ScriptedAnswerGateway(
            Abstain("Nel pack attuale c'è solo il dato di criticità e non ci sono informazioni di rinnovo."),
            UncitedDraft("**Oggetto:** Rinnovo del contratto [nome del contratto]\n\nGentile [nome del referente], ..."));

        var result = await new AnswerComposer(gateway).AnswerAsync(EmailQuestion, ScreenshotSavingsPack.Build(), []);

        Assert.Equal(2, gateway.Calls);
        Assert.DoesNotContain("never declines", gateway.SystemPrompts[0], StringComparison.Ordinal);
        Assert.Contains("Ask Raffa never declines", gateway.SystemPrompts[1], StringComparison.Ordinal);
        Assert.False(result.Value.GuardIntervened);
        Assert.False(result.Value.FallbackUsed);
        Assert.True(result.Value.Result.CanDetermine);
        Assert.StartsWith("**Oggetto:**", result.Value.Result.AnswerMarkdown!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_decline_the_caller_keeps_is_returned_without_a_retry()
    {
        var gateway = new ScriptedAnswerGateway(Abstain("The question is ambiguous: which contract do you mean?"));

        var result = await new AnswerComposer(gateway).AnswerAsync(
            Question, ScreenshotSavingsPack.Build(), [], keepDecline: reason => reason!.Contains("ambiguous", StringComparison.Ordinal));

        Assert.Equal(1, gateway.Calls);
        Assert.False(result.Value.Result.CanDetermine);
        Assert.Equal("The question is ambiguous: which contract do you mean?", result.Value.Result.AbstainReason);
    }

    [Fact]
    public async Task A_decline_that_survives_the_retry_goes_back_as_a_decline_for_the_caller_to_answer()
    {
        var gateway = new ScriptedAnswerGateway(
            Abstain("No validated contract records legal fees."), Abstain("Still nothing on legal fees."));

        var result = await new AnswerComposer(gateway).AnswerAsync("how much did we pay in legal fees last year?", ScreenshotSavingsPack.Build(), []);

        Assert.Equal(2, gateway.Calls);
        Assert.False(result.Value.GuardIntervened);
        Assert.False(result.Value.FallbackUsed);
        Assert.False(result.Value.Result.CanDetermine);
    }

    [Fact]
    public async Task A_decline_whose_retry_call_fails_goes_back_as_the_first_decline()
    {
        var gateway = new ScriptedAnswerGateway(Abstain("No validated contract records legal fees."));

        var result = await new AnswerComposer(gateway).AnswerAsync("how much did we pay in legal fees last year?", ScreenshotSavingsPack.Build(), []);

        Assert.Equal(2, gateway.Calls);
        Assert.False(result.Value.GuardIntervened);
        Assert.False(result.Value.Result.CanDetermine);
        Assert.Equal("No validated contract records legal fees.", result.Value.Result.AbstainReason);
    }

    [Fact]
    public async Task An_uncited_draft_passes_on_the_first_attempt()
    {
        var gateway = new ScriptedAnswerGateway(
            UncitedDraft("Gentile [nome del referente],\n\nvi chiediamo una proposta di rinnovo entro il [data di risposta]."));

        var result = await new AnswerComposer(gateway).AnswerAsync(EmailQuestion, ScreenshotSavingsPack.Build(), []);

        Assert.Equal(1, gateway.Calls);
        Assert.False(result.Value.GuardIntervened);
        Assert.True(result.Value.Result.CanDetermine);
        Assert.Empty(result.Value.Result.CitationKeys!);
    }

    [Fact]
    public async Task An_uncited_draft_still_fails_on_an_invented_figure_or_a_dangling_marker()
    {
        var gateway = new ScriptedAnswerGateway(
            UncitedDraft("Vi chiediamo uno sconto di EUR 99999 sul rinnovo."),
            UncitedDraft("Vi chiediamo uno sconto sul rinnovo [1]."));
        var pack = ScreenshotSavingsPack.Build();

        var result = await new AnswerComposer(gateway).AnswerAsync(EmailQuestion, pack, []);

        Assert.Equal(2, gateway.Calls);
        Assert.True(result.Value.GuardIntervened);
        Assert.NotNull(result.Value.GuardViolation);
        Assert.DoesNotContain("99999", result.Value.Result.AnswerMarkdown ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_nothing_answer_bearing_in_the_pack_two_failures_end_in_a_clean_abstain()
    {
        var gateway = new ScriptedAnswerGateway(FabricatedModelAnswer(), FabricatedModelAnswer());
        var raffaOnly = ScreenshotSavingsPack.Build()
            .Where(item => item.Corpus == PackCorpus.Raffa)
            .ToList();

        var result = await new AnswerComposer(gateway).AnswerAsync(Question, raffaOnly, []);

        Assert.True(result.Value.GuardIntervened);
        Assert.False(result.Value.FallbackUsed);
        Assert.False(result.Value.Result.CanDetermine);
        Assert.DoesNotContain("does not", result.Value.Result.AbstainReason!, StringComparison.Ordinal);
        Assert.DoesNotContain("=", result.Value.Result.AbstainReason!, StringComparison.Ordinal);
    }

    /// <summary>Replays the given results in order, one per <c>AnswerAsync</c> call; a call past
    /// the end fails like an unreachable gateway would.</summary>
    private sealed class ScriptedAnswerGateway(params AiAnswerResult[] results) : IAiGateway
    {
        public int Calls { get; private set; }

        /// <summary>The system prompt of every call, in order — the retry's addendum included.</summary>
        public List<string> SystemPrompts { get; } = [];

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default)
        {
            SystemPrompts.Add(request.SystemPrompt ?? string.Empty);
            var index = Calls++;
            return Task.FromResult(index < results.Length
                ? Result<AiAnswerResult>.Success(results[index])
                : Result<AiAnswerResult>.Failure("scripted gateway: no more answers"));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
