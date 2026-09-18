namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Process-local registry of in-flight extraction runs, keyed by classification job id. A hang
/// recovery (list poll, ClaimLost of a stale claim, inactivity watch) calls <see cref="Abort"/>
/// so the zombie Worker is cancelled before a fresh pointer is published — otherwise the dead
/// run would still write over the restart.
/// </summary>
public interface IExtractionRunAborter
{
    /// <summary>Links <paramref name="outer"/> into a cancellable token for this job and
    /// remembers it until <see cref="Unregister"/>.</summary>
    CancellationToken Register(Guid jobId, CancellationToken outer);

    /// <summary>Cancels the in-process run for <paramref name="jobId"/>, if any. No-op when
    /// the Worker that held the claim is already gone (the usual crash case).</summary>
    void Abort(Guid jobId);

    void Unregister(Guid jobId);
}
