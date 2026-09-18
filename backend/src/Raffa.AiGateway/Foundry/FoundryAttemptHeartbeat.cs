namespace Raffa.AiGateway.Foundry;

/// <summary>
/// Ambient per-attempt hook for <see cref="FoundryRetryPolicy"/> and Document Intelligence
/// poll <see cref="FoundryRetryPolicy.SendAsync"/> calls. Extraction binds a durable job
/// heartbeat here so a live Foundry retry or OCR poll moves <c>extraction_job.started_at</c>
/// without the gateway taking a dependency on Documents.
/// </summary>
public static class FoundryAttemptHeartbeat
{
    private static readonly AsyncLocal<Func<CancellationToken, Task>?> Current = new();

    /// <summary>Runs the bound callback, if any. No-op when nothing is bound.</summary>
    public static Task NotifyAsync(CancellationToken cancellationToken) =>
        Current.Value is { } onAttempt ? onAttempt(cancellationToken) : Task.CompletedTask;

    /// <summary>Binds <paramref name="onAttempt"/> for the current async flow until disposed.</summary>
    public static IDisposable Begin(Func<CancellationToken, Task> onAttempt)
    {
        ArgumentNullException.ThrowIfNull(onAttempt);
        var previous = Current.Value;
        Current.Value = onAttempt;
        return new Restore(previous);
    }

    private sealed class Restore(Func<CancellationToken, Task>? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
