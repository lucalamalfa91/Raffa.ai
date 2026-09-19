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

    /// <summary>
    /// Runs the bound callback, if any. A durable heartbeat write (SQL) must never abort the
    /// Foundry HTTP attempt — live classify outages were <see cref="InvalidOperationException"/>
    /// retry storms from <c>extraction_job.started_at</c> updates, wrapped as "classify could
    /// not be reached". Cancellation still propagates.
    /// </summary>
    public static async Task NotifyAsync(CancellationToken cancellationToken)
    {
        if (Current.Value is not { } onAttempt)
        {
            return;
        }

        try
        {
            await onAttempt(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Best-effort pulse; the hang watch still has in-memory ticks.
        }
    }

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
