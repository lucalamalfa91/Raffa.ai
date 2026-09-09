using Contigo.AiGateway.Contracts;
using Contigo.Chat.Application.Pack;

namespace Contigo.Chat.Application.Guards;

/// <summary>
/// The ADR-024 regenerate-once policy (task E13/F06/US01/T01, ask-engine coding objective point 5:
/// "one retry naming the violation, then downgrade to abstain with the pack's own facts";
/// `inputs/requirements.md` R-ASK-06: "the reply is regenerated once with the violation named,
/// then downgraded to an abstain that shows the pack's own facts"). Two small, pure building
/// blocks — the actual retry call belongs to the caller
/// (<c>Answering.AnswerComposer</c>, the one place that holds an <c>IAiGateway</c>): this type only
/// decides what to say on retry and what an eventual downgrade looks like.
/// </summary>
public static class RegenerateOnce
{
    /// <summary>
    /// Builds the addendum <c>AnswerComposer</c> appends to the persona system prompt for the one
    /// permitted retry — names <paramref name="violation"/> exactly (R-ASK-06's own "with the
    /// violation named") so the model has a concrete instruction to correct, not just "try again".
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="violation"/> is null/blank.</exception>
    public static string BuildRetryInstruction(string violation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(violation);

        return
            "Your previous answer was rejected by an automated grounding check: " + violation +
            " Answer again using ONLY the citationKeys and values already present in the context " +
            "pack given to you — do not invent, restate a different figure, or cite anything the " +
            "pack does not contain. If the pack genuinely does not support an answer, set " +
            "canDetermine to false instead.";
    }

    /// <summary>
    /// The final, honest fallback when even the retry still violates a guard (or the retry call
    /// itself failed): an explicit "cannot determine" carrying the pack's own facts in
    /// <see cref="AiAnswerResult.AbstainReason"/> — never the violating text (R-ASK-06; Appendix C
    /// rule 10). Preserves <paramref name="metadata"/> (model id/version/prompt version/timestamp/
    /// input hash) so the call stays fully reproducible/auditable (ADR-011) even though it
    /// downgraded.
    /// </summary>
    /// <param name="metadata">The last attempt's own reproducibility metadata.</param>
    /// <param name="pack">The pack the model should have grounded its answer in — up to five
    /// items are named verbatim in the abstain reason so the user still sees real facts even
    /// though no narrated answer could be trusted.</param>
    /// <param name="violation">The guard violation that triggered this downgrade.</param>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> or <paramref name="pack"/>
    /// is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="violation"/> is null/blank.</exception>
    public static AiAnswerResult DowngradeToAbstain(AiCallMetadata metadata, IReadOnlyList<PackItem> pack, string violation)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentException.ThrowIfNullOrWhiteSpace(violation);

        var reason = pack.Count == 0
            ? $"{violation} Nothing in the validated contracts supports a reliable answer."
            : $"{violation} Showing the pack's own facts instead: " +
              string.Join(
                  "; ",
                  pack.Take(5).Select(item => item.Values.Count == 0
                      ? item.Title
                      : $"{item.Title} ({string.Join(", ", item.Values.Select(v => $"{v.Key}={v.Value}"))})"));

        return new AiAnswerResult(
            CanDetermine: false,
            Answer: null,
            Citations: [],
            Metadata: metadata,
            AnswerMarkdown: null,
            CitationKeys: [],
            ActionKeys: [],
            AbstainReason: reason,
            FollowUps: []);
    }
}
