using Raffa.SharedKernel;

namespace Raffa.Savings.Tests.TestSupport;

/// <summary>
/// Fixed <see cref="IClock"/> for deterministic <see cref="Raffa.Savings.Application
/// .SavingsOpportunityService"/> timestamp assertions. This project's own copy of the pattern
/// every other test project in this solution already follows with its own local fake rather than
/// <c>SystemClock</c> (mirrors <c>Raffa.Renewals.Tests.TestSupport.FixedClock</c>).
/// </summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}
