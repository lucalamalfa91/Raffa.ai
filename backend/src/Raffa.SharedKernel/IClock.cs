namespace Raffa.SharedKernel;

/// <summary>
/// Clock abstraction. Domain code uses this for "now" instead of
/// <see cref="DateTime.UtcNow"/> directly, enabling deterministic testing.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
