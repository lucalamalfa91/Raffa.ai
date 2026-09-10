using Raffa.SharedKernel;

namespace Raffa.AiGateway.Tests.TestSupport;

/// <summary>
/// Fixed <see cref="IClock"/> for deterministic <c>AiCallMetadata.RespondedAtUtc</c> assertions.
/// <c>SystemClock</c>'s own doc comment notes "every test project in this solution already
/// follows that pattern with its own local fake rather than [SystemClock]" — this is this
/// project's copy of that pattern.
/// </summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}
