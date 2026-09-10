namespace Raffa.Documents.Contracts.Domain;

/// <summary>Run state of one <see cref="ExtractionJob"/>.</summary>
public enum ExtractionJobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    NeedsReview,
}
