namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Looks on the extraction subscription's dead-letter subqueue for a pointer that belongs to one
/// document, and if it finds one, sends it back to the topic so the Worker can claim it (the
/// stranded-<c>Uploaded</c> recovery the Documents row's "Retry upload" button runs).
///
/// <para>
/// Returns <see langword="true"/> when at least one matching dead-lettered message was
/// resubmitted — the caller must <em>not</em> also <see cref="IExtractionQueuePublisher.PublishAsync"/>
/// in that case, or the same job would be on the topic twice. Returns <see langword="false"/> when
/// the dead-letter subqueue had nothing for this document (or there is no broker): the caller then
/// publishes a fresh pointer.
/// </para>
///
/// <para>
/// Matching is by <see cref="ExtractionRequested.TenantId"/> +
/// <see cref="ExtractionRequested.DocumentId"/> only — never a scan that completes or resubmits
/// another tenant's messages (ADR-009). The in-process adapter always returns
/// <see langword="false"/>; there is no dead-letter on a channel.
/// </para>
/// </summary>
public interface IExtractionDeadLetterResubmitter
{
    Task<bool> TryResubmitAsync(ExtractionRequested pointer, CancellationToken cancellationToken = default);
}
