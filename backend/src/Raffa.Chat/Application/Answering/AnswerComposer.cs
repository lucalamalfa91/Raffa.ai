using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Pack;
using Raffa.SharedKernel;

namespace Raffa.Chat.Application.Answering;

/// <summary>
/// Calls the `answer` role with the assembled context pack, the versioned V2 persona prompt and
/// the conversation's recent turns (task E13/F06/US01/T01, ask-engine coding objective point 4:
/// "AnswerComposer calling IAiGateway.AnswerAsync with the pack + last N turns"; R-ASK-05), then
/// runs the full guard pipeline (<see cref="GroundingGuard"/> + <see cref="NumericGuard"/>) and the
/// one-retry policy (<see cref="RegenerateOnce"/>) before ever returning a result a caller may
/// show or persist.
///
/// <para>
/// Never retrieves anything itself — <paramref name="pack"/> (see <see cref="AnswerAsync"/>) must
/// already be authorized, tenant-scoped context assembled by the composition root (ADR-011
/// "authorization before retrieval" — the same "operate on caller-supplied data" shape
/// <c>RagAnswerService</c> and <c>DeterministicQueryHandler</c> already use, generalized here from
/// an evidence list to a context pack).
/// </para>
///
/// <para>
/// A model's action keys are repaired (<see cref="ActionKeyNormalizer"/>) before any guard runs —
/// an optional button never sinks a grounded answer. When both attempts still fail and the model
/// had tried to answer, the reply is <see cref="GroundedFallbackAnswer"/>'s answer composed from
/// the pack's own facts, not an abstain.
/// </para>
///
/// <para>
/// Persona v2.4 (never decline): a first attempt that declines (<c>canDetermine</c> false) is
/// regenerated once with <see cref="RegenerateOnce.BuildDeclineRetryInstruction"/>, exactly like a
/// guard violation — unless the caller's <c>keepDecline</c> asks to keep it (an ambiguous question
/// the caller turns into an interpretation menu instead). An answer that relies on no pack item (a
/// draft, a plan) passes without a citation, still under <see cref="NumericGuard"/>. A result
/// that still declines after all that is returned as such; the composition root turns it into a
/// proposal the user can act on, never a bare "cannot determine".
/// </para>
/// </summary>
public sealed class AnswerComposer(IAiGateway aiGateway)
{
    // JsonStringEnumConverter: PackValue.Kind (PackValueKind) serializes as "Amount"/"Percentage"/
    // "Date"/"Number", never the numeric default — both a human/model reading the raw pack JSON
    // and Raffa.AiGateway.Fixtures.FixtureAiGateway's own pack-aware echo (which cannot
    // reference this project's PackValueKind type at all — ADR-002 dependency direction — and so
    // deserializes the same JSON into its own string-typed shape) need the readable form.
    private static readonly JsonSerializerOptions PackJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Answers <paramref name="question"/> against <paramref name="pack"/>. Pack size is not
    /// re-checked here — the composition root already ran it through
    /// <see cref="Pack.PackBudget.Apply"/> before calling this method.
    /// </summary>
    /// <param name="question">The user's question, in its own language (OQ-askv2-006).</param>
    /// <param name="pack">The already-assembled, already-budgeted context pack (R-ASK-04).</param>
    /// <param name="recentTurns">The conversation's last N turns (role, rendered markdown), oldest
    /// first — an empty list for a brand-new conversation.</param>
    /// <param name="keepDecline">Given the first attempt's decline reason, whether to keep that
    /// decline as is instead of regenerating it (the caller has a better reply for it, such as an
    /// interpretation menu). <see langword="null"/> regenerates every decline.</param>
    /// <exception cref="ArgumentException"><paramref name="question"/> is null/blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="pack"/> or
    /// <paramref name="recentTurns"/> is <see langword="null"/>.</exception>
    public async Task<Result<AnswerComposerResult>> AnswerAsync(
        string question,
        IReadOnlyList<PackItem> pack,
        IReadOnlyList<(string Role, string Markdown)> recentTurns,
        Func<string?, bool>? keepDecline = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(recentTurns);

        var packJson = JsonSerializer.Serialize(pack, PackJsonOptions);
        var promptedQuestion = BuildQuestionWithHistory(question, recentTurns);

        var first = await CallGatewayAsync(promptedQuestion, packJson, AnswerPromptV2.SystemPrompt, cancellationToken)
            .ConfigureAwait(false);
        if (first.IsFailure)
        {
            return Result<AnswerComposerResult>.Failure(first.Error);
        }

        var firstResult = NormalizeActionKeys(first.Value);
        var firstVerdict = Validate(firstResult, pack);
        var firstDeclined = firstVerdict.Passed && !firstResult.CanDetermine;
        if (firstVerdict.Passed && (firstResult.CanDetermine || keepDecline?.Invoke(firstResult.AbstainReason) == true))
        {
            return Result<AnswerComposerResult>.Success(
                new AnswerComposerResult(firstResult, GuardIntervened: false, GuardViolation: null));
        }

        // R-ASK-06: "regenerated once with the violation named" — and persona v2.4: a decline is
        // regenerated once too, with the never-decline rule named.
        var retrySystemPrompt = AnswerPromptV2.SystemPrompt + Environment.NewLine + Environment.NewLine +
            (firstDeclined
                ? RegenerateOnce.BuildDeclineRetryInstruction()
                : RegenerateOnce.BuildRetryInstruction(firstVerdict.Violation!));

        var retry = await CallGatewayAsync(promptedQuestion, packJson, retrySystemPrompt, cancellationToken)
            .ConfigureAwait(false);

        if (retry.IsFailure)
        {
            // A decline has no violation to downgrade over: it goes back as it is, and the
            // composition root answers it with a proposal.
            return Result<AnswerComposerResult>.Success(firstDeclined
                ? new AnswerComposerResult(firstResult, GuardIntervened: false, GuardViolation: null)
                : Downgrade(question, pack, first.Value.Metadata, firstVerdict.Violation!, modelTriedToAnswer: firstResult.CanDetermine));
        }

        var retryResult = NormalizeActionKeys(retry.Value);
        var retryVerdict = Validate(retryResult, pack);
        if (firstDeclined)
        {
            return Result<AnswerComposerResult>.Success(retryVerdict.Passed
                ? new AnswerComposerResult(retryResult, GuardIntervened: false, GuardViolation: null)
                : Downgrade(question, pack, retryResult.Metadata, retryVerdict.Violation!, modelTriedToAnswer: retryResult.CanDetermine));
        }

        if (retryVerdict.Passed)
        {
            // The first attempt judged a savings pack sufficient; a retry that now retreats to
            // "cannot determine" is answering the retry instruction, not the pack — the calculators'
            // and the council's own facts still make a better reply than an abstain. On a clause or
            // fact pack the retreat goes back as a decline, which the composition root answers
            // with a proposal rather than pack facts that may not be about the question.
            if (firstResult.CanDetermine && !retryResult.CanDetermine && GroundedFallbackAnswer.IsSavingsShaped(pack) &&
                ComposeGrounded(question, pack, retryResult.Metadata) is { } grounded)
            {
                return Result<AnswerComposerResult>.Success(
                    new AnswerComposerResult(grounded, GuardIntervened: true, firstVerdict.Violation, FallbackUsed: true));
            }

            return Result<AnswerComposerResult>.Success(
                new AnswerComposerResult(retryResult, GuardIntervened: true, firstVerdict.Violation));
        }

        return Result<AnswerComposerResult>.Success(
            Downgrade(question, pack, retry.Value.Metadata, retryVerdict.Violation!, modelTriedToAnswer: retryResult.CanDetermine));
    }

    /// <summary>
    /// Both attempts failed a guard (or the retry call itself failed). When the model tried to
    /// answer, the pack held something worth saying — <see cref="GroundedFallbackAnswer"/> says it,
    /// quoting only the pack's own facts; otherwise, or when not even that passes the guards, the
    /// honest abstain of <see cref="RegenerateOnce.DowngradeToAbstain"/>. Either way the violation
    /// is kept for the audit row, never shown.
    /// </summary>
    private static AnswerComposerResult Downgrade(
        string question, IReadOnlyList<PackItem> pack, AiCallMetadata metadata, string violation, bool modelTriedToAnswer)
    {
        if (modelTriedToAnswer && ComposeGrounded(question, pack, metadata) is { } grounded)
        {
            return new AnswerComposerResult(grounded, GuardIntervened: true, violation, FallbackUsed: true);
        }

        return new AnswerComposerResult(
            RegenerateOnce.DowngradeToAbstain(metadata, pack, violation), GuardIntervened: true, violation);
    }

    /// <summary><see cref="GroundedFallbackAnswer.Compose"/>, kept only when it passes the very same
    /// guard pipeline a model answer must pass — never trusted just because it was built in code.</summary>
    private static AiAnswerResult? ComposeGrounded(string question, IReadOnlyList<PackItem> pack, AiCallMetadata metadata)
    {
        var grounded = GroundedFallbackAnswer.Compose(question, pack, metadata);
        return grounded is not null && Validate(grounded, pack).Passed ? grounded : null;
    }

    /// <summary>A model's action keys, repaired before any guard sees them — see
    /// <see cref="ActionKeyNormalizer"/>: a <c>raffa:renewals</c> becomes <c>renewals</c>, and a key
    /// that is no capability at all loses its button instead of failing the whole answer.</summary>
    private static AiAnswerResult NormalizeActionKeys(AiAnswerResult result) =>
        result with { ActionKeys = ActionKeyNormalizer.Normalize(result.ActionKeys) };

    private Task<Result<AiAnswerResult>> CallGatewayAsync(
        string question, string packJson, string systemPrompt, CancellationToken cancellationToken) =>
        aiGateway.AnswerAsync(new AiAnswerRequest(question, Evidence: [], systemPrompt, packJson), cancellationToken);

    // allowUncitedGuidance: persona v2.4 lets a draft or a plan that relies on no pack item carry no
    // citation; NumericGuard below still rejects any figure the pack does not hold.
    private static GuardVerdict Validate(AiAnswerResult result, IReadOnlyList<PackItem> pack)
    {
        var grounding = GroundingGuard.Validate(result, pack, allowUncitedGuidance: true);
        return grounding.Passed ? NumericGuard.Validate(result.AnswerMarkdown, pack) : grounding;
    }

    private static string BuildQuestionWithHistory(
        string question, IReadOnlyList<(string Role, string Markdown)> recentTurns)
    {
        if (recentTurns.Count == 0)
        {
            return question;
        }

        var history = string.Join(
            Environment.NewLine, recentTurns.Select(turn => $"{turn.Role}: {turn.Markdown}"));

        return $"Conversation so far:{Environment.NewLine}{history}{Environment.NewLine}{Environment.NewLine}" +
            $"Current question: {question}";
    }
}
