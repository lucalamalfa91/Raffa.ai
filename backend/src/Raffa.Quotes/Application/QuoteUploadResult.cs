using Raffa.Quotes.Domain;
using Raffa.SharedKernel;

namespace Raffa.Quotes.Application;

/// <summary>Result of <see cref="QuoteUploadService.UploadAsync"/> — mirrors
/// <c>Raffa.Documents.Contracts.Application.DocumentUploadResult</c>'s own shape.</summary>
/// <param name="Supplier">Task E05/F02/US01/T01 (market-assessment): echoes what
/// <see cref="QuoteUploadService.UploadAsync"/> actually recorded on the new
/// <see cref="Quote.Supplier"/> column (including a null, when the caller did not supply one), so a
/// caller can see immediately whether this quote is matchable yet — see that column's own doc
/// comment.</param>
/// <param name="Currency">Echoes <see cref="Quote.Currency"/>.</param>
/// <param name="Geography">Echoes <see cref="Quote.Geography"/>.</param>
/// <param name="PurchaseDate">Echoes <see cref="Quote.PurchaseDate"/> — never null in practice
/// (defaulted from <see cref="CreatedAt"/> when not supplied), but typed nullable to match the
/// column it echoes exactly.</param>
public sealed record QuoteUploadResult(
    EntityId QuoteId,
    string FileName,
    string MimeType,
    QuoteProcessingStatus ProcessingStatus,
    DateTimeOffset CreatedAt,
    string? Supplier,
    string? Currency,
    string? Geography,
    DateOnly? PurchaseDate);
