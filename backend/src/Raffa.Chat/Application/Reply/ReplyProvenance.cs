namespace Raffa.Chat.Application.Reply;

/// <summary>
/// The `provenance` object of the ADR-024 §6 reply contract — `inputs/requirements.md` §6:
/// <c>{ sources, modelId, promptVersion, inputHash }</c>. <see cref="ModelId"/>/
/// <see cref="PromptVersion"/>/<see cref="InputHash"/> are <see langword="null"/> for a fully
/// deterministic reply (redirect/refusal/capability-answer/needs_document — no `answer`-role call
/// was ever made for those; R-ASK-09's own per-turn audit already distinguishes them by
/// <see cref="ReplyKind"/>/audit action, so a null triple here is an honest "no model call", not a
/// missing value).
/// </summary>
/// <param name="Sources">Which corpora contributed at least one citation to this reply — a subset
/// of <c>tenant</c>/<c>market</c>/<c>raffa</c>/<c>calc</c> (<see cref="Pack.PackCorpus"/>), empty
/// for a reply with no citations at all.</param>
/// <param name="ModelId">Echoes <c>Raffa.AiGateway.Contracts.AiCallMetadata.ModelId</c> when an
/// `answer`-role call actually produced this reply.</param>
/// <param name="PromptVersion">Echoes <c>AiCallMetadata.PromptVersion</c> — this story's own
/// versioned persona prompt tag (<c>Answering.AnswerPromptV2.Version</c>, e.g. <c>"answer-v2.1"</c>).</param>
/// <param name="InputHash">Echoes <c>AiCallMetadata.InputHash</c> — a content hash of the pack/prompt,
/// never the confidential input itself (ADR-011).</param>
public sealed record ReplyProvenance(
    IReadOnlyList<string> Sources,
    string? ModelId,
    string? PromptVersion,
    string? InputHash)
{
    /// <summary>No model call was made for this reply (redirect/refusal/deterministic capability
    /// answer) — <see cref="Sources"/> may still be non-empty (a capability answer cites a real
    /// feature card) even though no `answer`-role call happened.</summary>
    public static ReplyProvenance NoModelCall(IReadOnlyList<string> sources) => new(sources, null, null, null);
}
