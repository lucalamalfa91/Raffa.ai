namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Scoped <see cref="IExtractionHangWatch"/>: one instance per Service Bus delivery (the consumer
/// opens a DI scope per message), shared by the handler, the admission gate and staged extraction
/// so a stage start resets the same timer the claim started.
/// </summary>
public sealed class ExtractionHangWatch : IExtractionHangWatch, IDisposable
{
    private CancellationTokenSource? _cts;

    public CancellationToken Token => _cts?.Token ?? CancellationToken.None;

    public CancellationToken Start(CancellationToken outer)
    {
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        Heartbeat();
        return _cts.Token;
    }

    public void Heartbeat() => _cts?.CancelAfter(HungProcessingDetector.InactivityWindow);

    public void Dispose() => _cts?.Dispose();
}
