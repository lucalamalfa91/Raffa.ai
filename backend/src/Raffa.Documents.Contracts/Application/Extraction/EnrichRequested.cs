namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// The durable queue message for the full 7-stage enrich pipeline (instant-identity-ingest slice,
/// ADR-027 §D2). Published by the intake handler after the fast headline pass has set provisional
/// identity fields, so the Service Bus lock on the intake message does not cover the slow
/// 7-stage StagedExtractionService run.
///
/// <para>
/// Same shape as <see cref="ExtractionRequested"/> but carries a different <c>Subject</c> header
/// on Service Bus (<c>"EnrichRequested"</c> vs <c>"ExtractionRequested"</c>) so the consumer can
/// route the two without a second subscription. The in-memory channel carries both types in a
/// single untyped wrapper (<see cref="QueuedMessage"/>).
/// </para>
/// </summary>
/// <param name="TenantId">Which tenant's document to enrich.</param>
/// <param name="DocumentId">The <see cref="Domain.Document"/> to enrich.</param>
/// <param name="SchemaVersion">Wire-schema version of this message.</param>
public sealed record EnrichRequested(
    Guid TenantId,
    Guid DocumentId,
    int SchemaVersion)
{
    public const int CurrentSchemaVersion = 1;
}
