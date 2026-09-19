using Raffa.AiGateway.Foundry;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Durable hang-detector heartbeat: <see cref="IExtractionHangWatch.Heartbeat"/> plus
/// <c>UPDATE extraction_job.started_at = now()</c>. In-memory pulses every
/// <see cref="PulseInterval"/> keep the watch from cancelling a live Foundry call;
/// Foundry retries, OCR polls and preview pages persist <see cref="ExtractionJob.StartedAt"/>
/// so <see cref="HungProcessingDetector"/> (and GET recovery) see progress.
/// </summary>
public sealed class ExtractionProgressHeartbeat(
    DocumentsContractsDbContext dbContext,
    IClock clock,
    IExtractionHangWatch? hangWatch = null)
{
    /// <summary>How often a live run refreshes the in-memory hang watch during a long await.</summary>
    public static readonly TimeSpan PulseInterval = TimeSpan.FromSeconds(30);

    private static readonly AsyncLocal<ExtractionJob?> BoundJob = new();

    /// <summary>The job whose <see cref="ExtractionJob.StartedAt"/> subsequent pulses update.</summary>
    public IDisposable Bind(ExtractionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var previous = BoundJob.Value;
        BoundJob.Value = job;
        return new Restore(() => BoundJob.Value = previous);
    }

    public async Task PulseAsync(ExtractionJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        hangWatch?.Heartbeat();
        job.StartedAt = clock.UtcNow;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A SQL blip on started_at must not abort classify/extract. The in-memory hang
            // watch already ticked above; GET recovery still has ClaimedAt/StartedAt from
            // the last successful write.
        }
    }

    public Task PulseBoundAsync(CancellationToken cancellationToken)
    {
        var job = BoundJob.Value;
        if (job is null)
        {
            hangWatch?.Heartbeat();
            return Task.CompletedTask;
        }

        return PulseAsync(job, cancellationToken);
    }

    /// <summary>Each Foundry HTTP attempt (retry or OCR poll) persists the bound job's heartbeat.</summary>
    public IDisposable BeginFoundryAttempts() => FoundryAttemptHeartbeat.Begin(PulseBoundAsync);

    /// <summary>In-memory <see cref="IExtractionHangWatch.Heartbeat"/> every
    /// <see cref="PulseInterval"/> so <c>CancelAfter</c> cannot fire while I/O is still in flight.
    /// Durable writes stay on the sequential Foundry/preview/stage path (DbContext is not
    /// thread-safe to share with this timer).</summary>
    public IDisposable BeginMemoryPulses() => HangWatchMemoryPulser.Start(hangWatch, PulseInterval);

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

internal sealed class HangWatchMemoryPulser : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    private HangWatchMemoryPulser(IExtractionHangWatch hangWatch, TimeSpan interval)
    {
        _ = RunAsync(hangWatch, interval, _cts.Token);
    }

    public static IDisposable Start(IExtractionHangWatch? hangWatch, TimeSpan interval) =>
        hangWatch is null ? NullDisposable.Instance : new HangWatchMemoryPulser(hangWatch, interval);

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private static async Task RunAsync(IExtractionHangWatch hangWatch, TimeSpan interval, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                hangWatch.Heartbeat();
            }
        }
        catch (OperationCanceledException)
        {
            // Timer stopped because the long await finished.
        }
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
