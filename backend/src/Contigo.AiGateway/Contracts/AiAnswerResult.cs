namespace Contigo.AiGateway.Contracts;

/// <summary>
/// Output of the `answer` role. <see cref="CanDetermine"/> is <see langword="false"/> whenever
/// the gateway abstains rather than fabricates (empty/insufficient evidence) — callers must check
/// it before showing <see cref="Answer"/>, which is <see langword="null"/> in that case (spec
/// §8.4 "no evidence, no claim"; Appendix C rule 10).
///
/// <see cref="AnswerMarkdown"/>/<see cref="CitationKeys"/>/<see cref="ActionKeys"/>/
/// <see cref="AbstainReason"/>/<see cref="FollowUps"/> are ADR-024's (epic-13/ask-v2) structured-
/// answer additions (task E13/F01/US01/T02, foundry-gateway) — all optional/nullable, defaulting
/// to <see langword="null"/>, so <see cref="Fixtures.FixtureAiGateway"/>'s existing
/// <see cref="Answer"/>/<see cref="Citations"/> construction and every trailing-4-argument call
/// site this record already had (<c>Contigo.Chat.Application.AbstainGuard</c>,
/// <c>Contigo.Chat.Tests</c>) keep compiling unchanged — "keeping today's Answer/Citations for the
/// fixture path" per that task's own coding objective. A truly non-nullable default (for example
/// <c>= []</c>) is not a legal C# default-parameter value here, and a null-forgiven non-nullable
/// default would silently lie to every one of those existing call sites, which construct this
/// record with only the original four arguments — see that task's own reasoning. Callers reading
/// the new fields must treat <see langword="null"/> as "not populated by this call", not "empty by
/// the provider's own answer" (empty collections and empty/abstained answers still get real,
/// non-null values, populated by <see cref="Foundry.FoundryAnswerClient"/>).
/// </summary>
/// <param name="CanDetermine">Whether a grounded answer could be produced from the given evidence.</param>
/// <param name="Answer">The grounded answer text, or <see langword="null"/> when <see cref="CanDetermine"/> is <see langword="false"/>.</param>
/// <param name="Citations">Source pointers backing <see cref="Answer"/> (spec §8.3/§8.4). Empty when <see cref="CanDetermine"/> is <see langword="false"/>.</param>
/// <param name="Metadata">Reproducibility metadata for this call (ADR-004, ADR-011).</param>
/// <param name="AnswerMarkdown">
/// ADR-024 structured answer body (Markdown). <see langword="null"/> for every call this task did
/// not touch (the fixture path, and every pre-existing test); the Foundry `answer` role always
/// sets it once it actually runs (mirroring <paramref name="Answer"/>'s own value on that path).
/// </param>
/// <param name="CitationKeys">
/// ADR-024 pack-item citation keys ("every `[n]` / citationKey in the pack"), resolved against the
/// caller-assembled pack (<see cref="AiAnswerRequest.PackJson"/>) — this gateway does not resolve
/// them to a document/page/section itself (that mapping needs the pack, which only the caller
/// has); <see cref="Citations"/> remains the resolved-pointer shape for the legacy
/// evidence-only path. <see langword="null"/> when not populated by this call.
/// </param>
/// <param name="ActionKeys">ADR-024 capability-catalog action keys the reply should surface. <see langword="null"/> when not populated by this call.</param>
/// <param name="AbstainReason">ADR-024 human-readable abstain reason, set only alongside <see cref="CanDetermine"/> = <see langword="false"/> on the Foundry path.</param>
/// <param name="FollowUps">ADR-024 suggested follow-up questions. <see langword="null"/> when not populated by this call.</param>
public sealed record AiAnswerResult(
    bool CanDetermine,
    string? Answer,
    IReadOnlyList<AiCitation> Citations,
    AiCallMetadata Metadata,
    string? AnswerMarkdown = null,
    IReadOnlyList<string>? CitationKeys = null,
    IReadOnlyList<string>? ActionKeys = null,
    string? AbstainReason = null,
    IReadOnlyList<string>? FollowUps = null);
