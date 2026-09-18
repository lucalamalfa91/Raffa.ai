using Raffa.Documents.Contracts.Domain;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Decides whether a <see cref="DocumentProcessingStatus.Processing"/> document has made no
/// durable progress for <see cref="InactivityWindow"/> — the hang the Documents list used to
/// sit on forever ("Uploading…", "Validating schema…") after the Worker died with the claim
/// still held. Progress is the newest of <c>claimed_at</c> / <c>started_at</c> / <c>completed_at</c>
/// on the document's extraction jobs (the only heartbeats the pipeline already writes).
/// </summary>
public static class HungProcessingDetector
{
    /// <summary>How long a processing document may sit on the same durable step before the
    /// run is aborted and re-enqueued from scratch. Matches the list's own three-minute
    /// auto-reprocess window for a never-claimed <c>Uploaded</c> row.</summary>
    public static readonly TimeSpan InactivityWindow = TimeSpan.FromMinutes(3);

    public static DateTimeOffset? LastProgressAt(IEnumerable<ExtractionJobProgress> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        DateTimeOffset? last = null;
        foreach (var job in jobs)
        {
            last = Later(last, job.ClaimedAt);
            last = Later(last, job.StartedAt);
            last = Later(last, job.CompletedAt);
        }

        return last;
    }

    /// <summary>
    /// True when the document is still <see cref="DocumentProcessingStatus.Processing"/> and its
    /// last job heartbeat is missing or older than <see cref="InactivityWindow"/>. Other statuses
    /// are never hung — <c>Uploaded</c> has its own never-claimed recovery.
    /// </summary>
    public static bool IsHung(
        DocumentProcessingStatus status,
        DateTimeOffset now,
        IEnumerable<ExtractionJobProgress> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        if (status != DocumentProcessingStatus.Processing)
        {
            return false;
        }

        var last = LastProgressAt(jobs);
        if (last is null)
        {
            // No heartbeat yet — the Worker has not claimed (or tests seeded Processing without
            // jobs). Not a hang: a real claim always writes claimed_at before the row goes
            // Processing.
            return false;
        }

        return now - last.Value >= InactivityWindow;
    }

    private static DateTimeOffset? Later(DateTimeOffset? current, DateTimeOffset? candidate)
    {
        if (candidate is null)
        {
            return current;
        }

        return current is null || candidate.Value > current.Value ? candidate : current;
    }
}

/// <summary>The three timestamps <see cref="HungProcessingDetector"/> reads off an
/// <see cref="ExtractionJob"/>, so the detector stays a pure function over projected data.</summary>
public readonly record struct ExtractionJobProgress(
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);
