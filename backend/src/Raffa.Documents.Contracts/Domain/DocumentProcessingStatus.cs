namespace Raffa.Documents.Contracts.Domain;

/// <summary>Document lifecycle status (product spec §7.1 asynchronous ingestion pipeline).</summary>
public enum DocumentProcessingStatus
{
    Uploaded,
    Processing,
    NeedsReview,
    Completed,
    Failed,
}
