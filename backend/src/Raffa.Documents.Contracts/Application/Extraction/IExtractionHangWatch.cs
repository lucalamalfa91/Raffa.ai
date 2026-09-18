namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Inactivity timer for one extraction delivery. <see cref="Start"/> links the Service Bus
/// token; <see cref="Heartbeat"/> resets the <see cref="HungProcessingDetector.InactivityWindow"/>
/// countdown after each durable step (claim, parse, classify, each extraction stage). Silence
/// for that window cancels the run so hang recovery can abort it and re-enqueue from scratch.
/// </summary>
public interface IExtractionHangWatch
{
    CancellationToken Token { get; }

    CancellationToken Start(CancellationToken outer);

    void Heartbeat();
}
