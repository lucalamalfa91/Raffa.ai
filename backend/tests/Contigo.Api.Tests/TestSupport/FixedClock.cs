using Contigo.SharedKernel;

namespace Contigo.Api.Tests.TestSupport;

/// <summary>
/// Pins "now" for a test's own <see cref="WebApplicationFactory{TEntryPoint}"/> instance, so a
/// relative-date scenario (e.g. "which contracts renew in the next 120 days?") compares a seeded
/// contract's <c>EndDate</c> against the exact same instant the test itself used to compute that
/// date — never a real, wall-clock <see cref="SystemClock"/> a midnight rollover could flake
/// against (Appendix C rule 6's determinism convention, applied to test setup the same way
/// every fixed-clock test elsewhere in this solution already does it with its own local fake).
/// </summary>
internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}
