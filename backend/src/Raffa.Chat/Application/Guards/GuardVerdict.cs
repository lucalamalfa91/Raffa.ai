namespace Raffa.Chat.Application.Guards;

/// <summary>
/// The outcome of one guard check (<see cref="GroundingGuard"/>/<see cref="NumericGuard"/>) —
/// whether the checked <c>Raffa.AiGateway.Contracts.AiAnswerResult</c> may be trusted as-is, and
/// if not, why (so <see cref="RegenerateOnce"/> can name the exact violation in its retry
/// instruction — R-ASK-06: "regenerated once with the violation named").
/// </summary>
/// <param name="Passed"><see langword="true"/> when no violation was found.</param>
/// <param name="Violation">Human-readable description of the first violation found;
/// <see langword="null"/> when <see cref="Passed"/> is <see langword="true"/>.</param>
public sealed record GuardVerdict(bool Passed, string? Violation)
{
    public static readonly GuardVerdict Ok = new(true, null);

    public static GuardVerdict Fail(string violation) => new(false, violation);
}
