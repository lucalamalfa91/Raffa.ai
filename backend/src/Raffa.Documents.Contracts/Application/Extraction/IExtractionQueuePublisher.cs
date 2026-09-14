namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Outbound port: "this job is ready to be worked" (ADR-027 §D2/§D10; task E16/F02/US02/T01,
/// durable-queue-transport). <see cref="DocumentUploadService"/> calls this — inside its own tenant
/// scope, <b>before</b> <c>SaveChangesAsync</c> commits the <see cref="Domain.Document"/> /
/// <see cref="Domain.DocumentVersion"/> / <see cref="Domain.ExtractionJob"/> rows it just built
/// (ADR-027 §D2: "publish, then commit — the ExtractionJob row is the durable record and a lost
/// commit leaves a harmless phantom message").
///
/// <para>
/// Two implementations in <c>Raffa.Api.Infrastructure</c>, chosen by one predicate on whether a
/// broker namespace is configured (ADR-027 §D10, its w15 round-2 footer §C3): a durable, broker-
/// backed adapter and an in-process fallback. The transport is an adapter concern, never this
/// domain module's, so neither implementation, and no mention of the broker SDK by name, belongs
/// in this module (ADR-002's own "no provider SDK in domain code" rule).
/// </para>
/// </summary>
public interface IExtractionQueuePublisher
{
    Task PublishAsync(ExtractionRequested message, CancellationToken cancellationToken = default);
}
