namespace Raffa.SharedKernel.Jobs;

/// <summary>
/// The shared job-transport message (task F6-T05; plan decision 9: share the job transport, not the
/// entities). A pointer to an already-written, queued job row of some module, and nothing else --
/// never a storage path, never content (ADR-009 w15 footer: a queue message is untrusted input and
/// the worker derives everything else from the row it reads inside the tenant scope this message
/// names).
///
/// <para>
/// One transport, several job kinds: the document pipeline keeps its own
/// <c>ExtractionRequested</c> contract unchanged (it is the F5 stream's), and every other module
/// that wants durable, at-least-once processing publishes this message instead of inventing a
/// second topic, a second subscription, a second consumer and a second settlement policy. The
/// <see cref="Kind"/> says which <see cref="IJobHandler"/> owns the job; the kernel knows nothing
/// about quotes, contracts or any other entity.
/// </para>
///
/// <para>
/// Plain <see cref="Guid"/> fields, not <see cref="TenantId"/> / <see cref="EntityId"/>: this is a wire
/// contract that a process reads back as untrusted input. The publisher and the handler convert to
/// and from the strong id types at their own boundary.
/// </para>
/// </summary>
/// <param name="Kind">Which handler owns the job; one of <see cref="JobKinds"/>.</param>
/// <param name="TenantId">Which tenant's row to claim. Validated by the handler before any tenant scope is entered.</param>
/// <param name="ResourceId">The thing the job works on (for a quote extraction, the quote).</param>
/// <param name="JobId">The already-written job row the claim targets.</param>
/// <param name="SchemaVersion">Wire-schema version, so an incompatible change is detectable.</param>
public sealed record JobRequested(
    string Kind,
    Guid TenantId,
    Guid ResourceId,
    Guid JobId,
    int SchemaVersion)
{
    /// <summary>Current wire-schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Message subject on the topic. The consumer routes on it: a message with this subject is a
    /// <see cref="JobRequested"/>, any other subject is the document pipeline's own contract.</summary>
    public const string MessageSubject = "JobRequested";
}

/// <summary>The job kinds the shared transport carries.</summary>
public static class JobKinds
{
    /// <summary>Line-item extraction of one uploaded quote (task F6-T01).</summary>
    public const string QuoteExtraction = "quote-extraction";
}
