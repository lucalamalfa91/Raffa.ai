using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Quotes.Application.Normalization;

/// <summary>
/// Implements task E05/F01/US01/T02 (quote-normalization; parent story
/// us-01-quote-line-extraction, the wave-spec's own <c>quote-normalization</c> artifact, depending
/// on <c>quote-extraction</c>) — product spec §11.1's pipeline step "Normalize unit economics",
/// which sits between "Extract" and "Match benchmark". Benchmark matching/assessment itself is a
/// later, not-yet-decomposed task; this type's own job stops at producing a normalized,
/// per-line figure that such a task can read.
///
/// <b>Deterministic, in-process, same unit of work as extraction</b>: like
/// <c>Raffa.Quotes.Application.Extraction.QuoteLineExtractionService.ApplyExtractedLines</c>, this
/// only mutates already-tracked <see cref="QuoteLine"/> entities — it does not call
/// <c>SaveChangesAsync</c> itself. <see cref="NormalizeLines"/> reads
/// <see cref="Microsoft.EntityFrameworkCore.DbContext.ChangeTracker"/>'s own local view (via
/// <c>QuotesDbContext.QuoteLines.Local</c>), not a fresh database query, because its only caller
/// today (<c>Raffa.Api.QuoteExtractionPipeline.ProcessAsync</c>) runs this immediately after
/// <c>ApplyExtractedLines</c> adds those very rows to the same <see cref="QuotesDbContext"/>
/// instance's change tracker, still before the one shared <c>SaveChangesAsync</c> call that persists
/// both extraction and normalization together as a single unit of work — a query against the
/// database would find nothing yet, since those rows do not exist there until that shared save
/// happens. Never calls <c>Raffa.AiGateway</c> or any provider: every input this type reads
/// (<see cref="QuoteLine.UnitPrice"/>, <see cref="QuoteLine.Term"/>) was already extracted and
/// persisted by task-01; Appendix C rule 6 ("prefer deterministic arithmetic... to LLM reasoning")
/// applied a second time, to a second pipeline stage.
/// </summary>
public sealed class QuoteLineNormalizationService(QuotesDbContext dbContext)
{
    /// <summary>
    /// Normalizes every currently-tracked <see cref="QuoteLine"/> for <paramref name="quoteId"/>/
    /// <paramref name="tenantId"/> in place (see this type's own doc comment for why "currently
    /// tracked", not "queried from the database"). Returns a count of how many lines resolved to a
    /// real <see cref="QuoteLine.NormalizedAnnualUnitPrice"/> versus how many did not — mirrors
    /// <c>Raffa.Quotes.Application.Extraction.QuoteLineExtractionOutcome</c>'s own
    /// caller-facing-counts shape.
    /// </summary>
    public QuoteLineNormalizationOutcome NormalizeLines(TenantId tenantId, EntityId quoteId)
    {
        var lines = dbContext.QuoteLines.Local
            .Where(line => line.TenantId == tenantId && line.QuoteId == quoteId)
            .ToList();

        var normalized = 0;
        var unresolved = 0;

        foreach (var line in lines)
        {
            var (annualUnitPrice, termMonths) = NormalizeUnitEconomics(line.UnitPrice, line.Term);
            line.NormalizedAnnualUnitPrice = annualUnitPrice;
            line.NormalizedTermMonths = termMonths;

            if (annualUnitPrice is not null)
            {
                normalized++;
            }
            else
            {
                unresolved++;
            }
        }

        return new QuoteLineNormalizationOutcome(normalized, unresolved);
    }

    /// <summary>
    /// The pure arithmetic, isolated as its own function so it is directly unit-testable
    /// independent of the database or the change tracker — mirrors
    /// <c>QuoteLineExtractionService.ComputePricing</c>'s identical shape/reasoning. Returns
    /// <see langword="null"/>/<see langword="null"/> when <paramref name="unitPrice"/> is unknown or
    /// <paramref name="term"/> does not match <see cref="QuoteBillingCadence.RecognizeMonths"/>'s own
    /// small, fixed vocabulary — see that method's own doc comment for exactly which terms resolve
    /// and why the rest deliberately do not (Appendix C rule 10). Otherwise
    /// <paramref name="unitPrice"/> is rescaled to an annual rate: a recognized cadence of N months
    /// means <paramref name="unitPrice"/> already covers N months, so multiplying by
    /// <c>12 / N</c> annualizes it (N = 12, i.e. already annual, is a no-op ×1).
    /// </summary>
    internal static (decimal? NormalizedAnnualUnitPrice, int? TermMonths) NormalizeUnitEconomics(
        decimal? unitPrice, string? term)
    {
        if (unitPrice is null)
        {
            return (null, null);
        }

        var months = QuoteBillingCadence.RecognizeMonths(term);
        if (months is null)
        {
            return (null, null);
        }

        return (unitPrice.Value * 12m / months.Value, months.Value);
    }
}

/// <summary>Counts <c>Raffa.Api.QuoteExtractionPipeline</c> uses to report
/// <c>normalizedLineItemCount</c>/<c>unresolvedNormalizationCount</c> back over HTTP (see
/// <c>Raffa.Api.QuotesEndpointExtensions</c>) — mirrors
/// <c>Raffa.Quotes.Application.Extraction.QuoteLineExtractionOutcome</c>'s own named-result-crosses-
/// an-assembly-boundary shape. <see cref="UnresolvedCount"/> is spec §11.3's own "line-item
/// normalization is unresolved" guardrail made visible, not enforced — no task yet reads this value
/// back to gate a savings target.</summary>
public sealed record QuoteLineNormalizationOutcome(int NormalizedCount, int UnresolvedCount);
