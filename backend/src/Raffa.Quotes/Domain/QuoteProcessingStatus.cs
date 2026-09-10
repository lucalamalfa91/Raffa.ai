namespace Raffa.Quotes.Domain;

/// <summary>Quote lifecycle status — mirrors
/// <c>Raffa.Documents.Contracts.Domain.DocumentProcessingStatus</c>'s own shape (spec §7.1
/// asynchronous ingestion pipeline applies equally to a quote upload, module-map.md "Quotes"
/// row) rather than referencing that type directly (ADR-002: this module may not reference
/// <c>Raffa.Documents.Contracts</c>).</summary>
public enum QuoteProcessingStatus
{
    Uploaded,
    Processing,
    NeedsReview,
    Completed,
    Failed,
}
