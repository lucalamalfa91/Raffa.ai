using Raffa.Quotes.Application.Assessment;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Quotes.Application.Strategy;

/// <summary>
/// Implements task E05/F03/US01/T01 (negotiation-strategy; parent story
/// us-01-negotiation-strategy AC-1 "Generate opening target, acceptable range, walk-away threshold,
/// levers, rationale"). Composes on top of
/// <see cref="MarketAssessmentService.AssessAsync"/> (task E05/F02/US01/T01/T02) rather than
/// re-implementing the quote/line/benchmark fetch — the exact "reuse the already-computed
/// assessment, do not re-derive it" relationship <c>LineMarketAssessment.TargetSaving</c> itself
/// already has to <see cref="Assessment.MarketAssessmentCalculator"/>'s own output, one layer up.
///
/// <para>
/// Still re-reads the raw <c>QuoteLine</c> rows directly (a second, small query) because
/// <see cref="Assessment.LineMarketAssessment"/> only echoes the subset of line fields the market
/// -position/target-saving computation itself needed (<c>UnitPrice</c>, <c>Quantity</c>) — it does
/// not echo <c>Term</c>, <c>NormalizedTermMonths</c> or <c>Unit</c>, which
/// <see cref="NegotiationStrategyCalculator"/>'s own lever rationale needs. Extending
/// <see cref="Assessment.LineMarketAssessment"/> to also echo those would touch task
/// E05/F02/US01/T01's already-accepted file, outside this task's own "do not touch unrelated wave
/// artifacts" scope — the same "computed fresh, not shared mutable state" posture
/// <see cref="Assessment.MarketAssessmentService"/> itself already takes for the identical
/// Quote/QuoteLine data (it re-fetches from the database on every call rather than caching), just
/// one layer further out.
/// </para>
///
/// <para>
/// Read-only and computed fresh on every call, nothing here is persisted — the same posture
/// <see cref="Assessment.MarketAssessmentService"/>'s own doc comment already establishes for the
/// identical reason (no ADR/spec names a "NegotiationStrategy" table, and a live benchmark
/// distribution — and therefore the target/saving range this strategy anchors on — can change
/// between two calls).
/// </para>
///
/// <para>
/// <b>Task E05/F04/US01/T01 (r4-integration) fix</b>: this type never opened its own
/// <see cref="ITenantContext.BeginScope"/> either, for the identical reason and with the identical
/// real-HTTP consequence <see cref="Assessment.MarketAssessmentService"/>'s own doc comment now
/// documents — the second, direct <c>dbContext.QuoteLines</c> query below ran with no tenant claim
/// set on a real, RLS-enforced connection, regardless of whether the composed
/// <see cref="Assessment.MarketAssessmentService.AssessAsync"/> call above ever fixed its own half of
/// the gap. This method now opens its own scope around its entire body (nesting harmlessly with the
/// identical scope <see cref="Assessment.MarketAssessmentService.AssessAsync"/> now also opens for
/// the same <paramref name="tenantId"/> — <c>Raffa.SharedKernel.Tenancy.TenantContext.BeginScope</c>
/// restores the previous, already-correct value on the inner dispose), so this service is correct
/// with no caller-side change required, the same fix shape as its own composed dependency.
/// </para>
/// </summary>
public sealed class NegotiationStrategyService(
    MarketAssessmentService marketAssessmentService,
    QuotesDbContext dbContext,
    ITenantContext tenantContext,
    IClock clock)
{
    /// <summary>
    /// Generates a negotiation strategy for every <c>QuoteLine</c> on <paramref name="quoteId"/>/
    /// <paramref name="tenantId"/>, anchored on that same call's own market assessment. Propagates
    /// <see cref="MarketAssessmentService.AssessAsync"/>'s own failure verbatim when the quote
    /// itself cannot be found for this tenant (the same "quote not found" reason, not a second,
    /// differently-worded one) — never re-validates a check that method already made.
    /// </summary>
    public async Task<Result<QuoteNegotiationStrategy>> GenerateAsync(
        TenantId tenantId, EntityId quoteId, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var assessmentResult = await marketAssessmentService
            .AssessAsync(tenantId, quoteId, cancellationToken)
            .ConfigureAwait(false);

        if (assessmentResult.IsFailure)
        {
            return Result<QuoteNegotiationStrategy>.Failure(assessmentResult.Error);
        }

        var assessment = assessmentResult.Value;

        var lines = await dbContext.QuoteLines
            .Where(l => l.TenantId == tenantId && l.QuoteId == quoteId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var linesById = lines.ToDictionary(l => l.Id);
        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var strategies = new List<LineNegotiationStrategy>(assessment.Lines.Count);

        foreach (var lineAssessment in assessment.Lines)
        {
            if (!linesById.TryGetValue(lineAssessment.QuoteLineId, out var line))
            {
                // The assessment read this line a moment ago; it is gone from this second read
                // (concurrent delete). An honest, named abstain rather than a
                // KeyNotFoundException/NullReferenceException (Appendix C rule 10).
                strategies.Add(new LineNegotiationStrategy(
                    lineAssessment.QuoteLineId, null, null, null, null, [],
                    "QuoteLine could not be re-read (concurrent modification) — a negotiation " +
                    "strategy cannot be computed for a line that disappeared between the assessment " +
                    "read and this one."));
                continue;
            }

            strategies.Add(NegotiationStrategyCalculator.Compute(
                line, lines.Count, lineAssessment.TargetSaving, asOfDate));
        }

        return Result<QuoteNegotiationStrategy>.Success(new QuoteNegotiationStrategy(quoteId, strategies));
    }
}
