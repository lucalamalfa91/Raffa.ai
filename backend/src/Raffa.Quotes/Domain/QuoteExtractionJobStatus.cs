namespace Raffa.Quotes.Domain;

/// <summary>Run state of one <see cref="QuoteExtractionJob"/> — mirrors
/// <c>Raffa.Documents.Contracts.Domain.ExtractionJobStatus</c>'s own shape (see that module's
/// own copy for why this is duplicated, not shared).</summary>
public enum QuoteExtractionJobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    NeedsReview,
}
