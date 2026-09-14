namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// The durable queue message for the extraction pipeline (ADR-027 §D2, §C11; task
/// E16/F02/US02/T01, durable-queue-transport). A pointer to the already-written
/// <see cref="Domain.ExtractionJob"/> row, and nothing else — <b>never a storage path</b>.
///
/// <para>
/// <b>Ids only, by construction.</b> <see cref="Raffa.SharedKernel.Storage.DocumentStoragePath.EnsureWithinTenant"/>
/// validates a storage path against <em>the tenant the caller passes</em>; a path travelling
/// beside its own tenant id in one message would make that guard self-referential — it would
/// confirm anything internally consistent, whatever tenant a forged message named. The Worker
/// derives the blob location from the <see cref="Domain.Document"/> row itself, read inside the
/// tenant scope this message's <see cref="TenantId"/> opens (ADR-009 w15 footer §2).
/// </para>
///
/// <para>
/// Plain <see cref="Guid"/> fields, not <see cref="Raffa.SharedKernel.TenantId"/> /
/// <see cref="Raffa.SharedKernel.EntityId"/> — this is a wire contract serialized to JSON and read
/// back by a process that treats every field as untrusted input (ADR-009 w15 footer §1a: "a queue
/// message is untrusted input"), not an in-process value-object hand-off. The publisher and the
/// consumer each convert to/from the strong id types at their own boundary.
/// </para>
/// </summary>
/// <param name="TenantId">Which tenant's row to claim. Parsed and validated by the consumer
/// <em>before</em> any tenant scope is entered (ADR-009 w15 footer §1a).</param>
/// <param name="DocumentId">The <see cref="Domain.Document"/> this job belongs to.</param>
/// <param name="ExtractionJobId">The already-written, <see cref="Domain.ExtractionJobStatus.Queued"/>
/// <see cref="Domain.ExtractionJob"/> row the conditional-<c>UPDATE</c> claim (ADR-027 §D3) targets.</param>
/// <param name="SchemaVersion">Wire-schema version of this message, so a future incompatible change
/// is detectable rather than silently misread.</param>
public sealed record ExtractionRequested(
    Guid TenantId,
    Guid DocumentId,
    Guid ExtractionJobId,
    int SchemaVersion)
{
    /// <summary>Current wire-schema version — the only value any producer in this codebase writes
    /// today.</summary>
    public const int CurrentSchemaVersion = 1;
}
