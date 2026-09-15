namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Outbound port: "this document is ready for the full 7-stage enrich pipeline"
/// (instant-identity-ingest, ADR-027 §D2). Published by <c>ExtractionRequestedHandler</c>
/// (the intake handler) after the fast headline pass, so the Service Bus lock on the intake
/// message does not cover the slow StagedExtractionService run.
///
/// <para>
/// Two implementations chosen by the same predicate as <see cref="IExtractionQueuePublisher"/>:
/// a Service Bus adapter and an in-process fallback for tests / single-process local runs.
/// </para>
/// </summary>
public interface IEnrichQueuePublisher
{
    Task PublishAsync(EnrichRequested message, CancellationToken cancellationToken = default);
}
