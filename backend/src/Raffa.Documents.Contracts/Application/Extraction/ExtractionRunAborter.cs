using System.Collections.Concurrent;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Singleton <see cref="IExtractionRunAborter"/>: one dictionary per Worker process, because a
/// hang recovery may run on a different DI scope (the list request, a later delivery) than the
/// scope that claimed the job.
/// </summary>
public sealed class ExtractionRunAborter : IExtractionRunAborter
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runs = new();

    public CancellationToken Register(Guid jobId, CancellationToken outer)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        if (_runs.TryRemove(jobId, out var previous))
        {
            previous.Dispose();
        }

        _runs[jobId] = cts;
        return cts.Token;
    }

    public void Abort(Guid jobId)
    {
        if (!_runs.TryGetValue(jobId, out var cts))
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Unregister raced us; the run is already gone.
        }
    }

    public void Unregister(Guid jobId)
    {
        if (_runs.TryRemove(jobId, out var cts))
        {
            cts.Dispose();
        }
    }
}
