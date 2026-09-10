namespace Raffa.SharedKernel;

/// <summary>
/// Production <see cref="IClock"/> implementation backed by <see cref="DateTimeOffset.UtcNow"/>.
/// Composition roots register it either via the shared singleton
/// (<c>services.TryAddSingleton&lt;IClock&gt;(SystemClock.Instance)</c>, used by
/// <c>Raffa.Identity.Workspace.Infrastructure.WorkspaceMembershipService</c> and friends) or by
/// letting DI construct it directly (<c>services.TryAddSingleton&lt;IClock, SystemClock&gt;()</c>,
/// used by <c>Raffa.Documents.Contracts</c>). Tests use a fixed/fake <see cref="IClock"/>
/// instead, so time-dependent assertions stay deterministic — every test project in this solution
/// already follows that pattern with its own local fake rather than this type.
/// </summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
