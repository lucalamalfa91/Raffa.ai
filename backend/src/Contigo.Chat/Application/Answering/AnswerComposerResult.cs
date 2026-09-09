using Contigo.AiGateway.Contracts;

namespace Contigo.Chat.Application.Answering;

/// <summary>
/// The outcome of <see cref="AnswerComposer.AnswerAsync"/> — the final, guard-approved (or guard
/// -downgraded) result, plus whether a guard ever intervened for this turn (folded into the
/// per-turn audit entry as <c>abstainGuardIntervened</c>, mirroring
/// <c>Application.RagAnswerService</c>'s identical audit field for the older, evidence-only path).
/// </summary>
/// <param name="Result">The result callers should actually use — the first attempt when it passed
/// both guards, the retry when the first failed but the retry passed, or an honest abstain built
/// by <c>Guards.RegenerateOnce.DowngradeToAbstain</c> when both attempts failed.</param>
/// <param name="GuardIntervened">Whether either guard rejected the first attempt (a retry was
/// spent regardless of whether it ultimately succeeded).</param>
/// <param name="GuardViolation">The guard violation that triggered the retry, present only when
/// <see cref="GuardIntervened"/> is <see langword="true"/> — audit/diagnostics only, never shown to
/// the end user (same "never in the audit trail" boundary
/// <c>Application.AbstainGuardOutcome.Reason</c>'s own doc comment already draws).</param>
public sealed record AnswerComposerResult(AiAnswerResult Result, bool GuardIntervened, string? GuardViolation);
