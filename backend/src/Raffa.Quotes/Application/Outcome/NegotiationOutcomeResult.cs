using Raffa.Quotes.Application.Strategy;
using Raffa.SharedKernel;

namespace Raffa.Quotes.Application.Outcome;

/// <summary>
/// The persisted result of <see cref="NegotiationOutcomeService.CaptureAsync"/> — echoes
/// <c>Domain.NegotiationOutcome</c> verbatim (task E05/F03/US02/T01, negotiation-outcome). Kept
/// distinct from the domain entity itself, same "application-layer read shape, not the EF Core
/// entity" convention every other <c>...Result</c>/<c>...Response</c> type in this codebase already
/// follows (e.g. <c>Raffa.Savings.Application.SavingsOpportunityResult</c>).
/// </summary>
public sealed record NegotiationOutcomeResult(
    EntityId Id,
    EntityId QuoteId,
    decimal OriginalQuoteTotal,
    decimal? TargetPrice,
    decimal FinalPrice,
    decimal RealizedSaving,
    decimal DiscountPercent,
    int NegotiationDurationDays,
    IReadOnlyList<NegotiationLeverType> LeversUsed,
    DateTimeOffset CapturedAt,
    EntityId? SavingsOpportunityId = null);
