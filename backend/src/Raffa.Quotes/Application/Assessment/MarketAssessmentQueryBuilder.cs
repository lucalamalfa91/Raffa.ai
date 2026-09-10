using Raffa.Benchmark.Contracts;
using Raffa.Quotes.Domain;
using Raffa.SharedKernel;

namespace Raffa.Quotes.Application.Assessment;

/// <summary>
/// Builds a <see cref="BenchmarkQuery"/> for one <see cref="QuoteLine"/> — task E05/F02/US01/T01
/// (market-assessment; parent story us-01-market-assessment AC-1 "Match normalized line items to
/// the Benchmark Service (multi-dimensional)"). Pure and synchronous: no database call, no HTTP
/// call, no LLM call (Appendix C rule 6) — the same determinism convention
/// <c>Raffa.Savings.Application.PriceNormalizationCalculator</c> already established.
///
/// <para>
/// <see cref="BenchmarkQuery"/>'s constructor requires <c>Supplier</c>/<c>Product</c>/
/// <c>Geography</c>/<c>Quantity</c>/<c>Term</c>/<c>Currency</c>/<c>PurchaseDate</c> — only
/// <c>Sku</c> is optional (spec §10.4: not every purchase is SKU-level). This builder never
/// fabricates a missing dimension (Appendix C rule 10; ADR-001): a <see cref="Quote"/> with no
/// <see cref="Quote.Supplier"/>/<see cref="Quote.Currency"/>/<see cref="Quote.Geography"/>, or a
/// <see cref="QuoteLine"/> with no usable <see cref="QuoteLine.Description"/>/
/// <see cref="QuoteLine.Quantity"/>/<see cref="QuoteLine.Term"/>/<see cref="QuoteLine.UnitPrice"/>,
/// returns an honest <see cref="Result{T}.Failure"/> naming exactly which dimension is missing —
/// <see cref="Domain.MarketAssessmentStatus.QuoteDataUnresolved"/>, per that enum's own doc
/// comment.
/// </para>
///
/// <para>
/// <b>Term is passed through verbatim, never re-derived</b>: <see cref="QuoteLine.Term"/> is free
/// text with no ADR/spec-fixed vocabulary, so this builder does not attempt to normalize it against
/// <c>Raffa.Quotes.Application.Normalization.QuoteBillingCadence</c>'s own small recognized-word
/// vocabulary ("annual", "monthly", ...) — that vocabulary exists to *annualize a unit price*
/// (<see cref="QuoteLine.NormalizedAnnualUnitPrice"/>), a different concern from *matching a term
/// dimension*, and <c>Raffa.Benchmark.Fixtures.FixtureBenchmarkAdapter</c>'s own catalog keys
/// <c>Term</c> on numeric-commitment phrasing ("12 months", "36 months") that vocabulary
/// deliberately does not recognize (ambiguity — see that type's own doc comment). Mirrors
/// <c>Raffa.Savings.Application.PriceComparisonRequest</c>'s own doc comment: "term alignment...
/// is the Benchmark Service's own matching responsibility... no additional term-arithmetic happens
/// here."
/// </para>
///
/// <para>
/// <b>Price is <see cref="QuoteLine.UnitPrice"/>, not <see cref="QuoteLine.NormalizedAnnualUnitPrice"/></b>:
/// for the identical reason — the annualized figure only exists for a term
/// <c>QuoteBillingCadence</c> recognizes, which is a different, narrower vocabulary than the
/// benchmark's own <c>Term</c> matching text. Once <see cref="BenchmarkQuery.Term"/> has selected a
/// same-term comparable, that comparable's own distribution is already expressed on the same
/// cadence as this line's raw <see cref="QuoteLine.UnitPrice"/> — no additional annualization is
/// needed or correct here (the same trust-the-query's-own-term-match reasoning
/// <c>PriceComparisonRequest</c>'s own doc comment gives).
/// </para>
/// </summary>
public static class MarketAssessmentQueryBuilder
{
    /// <summary>
    /// Builds the <see cref="BenchmarkQuery"/> for <paramref name="line"/>, or an honest failure
    /// naming the first missing dimension found (checked in a fixed, documented order — not
    /// exhaustive of every missing field, the same "name one honest reason, not an exhaustive
    /// validation report" shape
    /// <c>Raffa.Savings.Application.PriceNormalizationCalculator.Compare</c>'s own branches
    /// already take).
    /// </summary>
    public static Result<BenchmarkQuery> Build(Quote quote, QuoteLine line)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(line);

        if (string.IsNullOrWhiteSpace(quote.Supplier))
        {
            return Result<BenchmarkQuery>.Failure(
                "Quote.Supplier is not recorded — this quote was uploaded without a supplier name, " +
                "so it cannot be matched against the Benchmark Service yet (Appendix C rule 10: " +
                "never guess a supplier).");
        }

        if (string.IsNullOrWhiteSpace(quote.Currency))
        {
            return Result<BenchmarkQuery>.Failure(
                "Quote.Currency is not recorded — no currency-conversion service exists in this " +
                "codebase (Appendix C rule 10), so a benchmark comparison cannot be attempted " +
                "without knowing what currency this quote's prices are in.");
        }

        if (string.IsNullOrWhiteSpace(quote.Geography))
        {
            return Result<BenchmarkQuery>.Failure(
                "Quote.Geography is not recorded — spec §10.4 requires geography as one of the " +
                "required multi-dimensional match fields (matching must use more than supplier " +
                "name alone), so a benchmark comparison cannot be attempted without it.");
        }

        if (quote.PurchaseDate is not { } purchaseDate)
        {
            return Result<BenchmarkQuery>.Failure(
                "Quote.PurchaseDate is not recorded — the Benchmark Service uses it to filter " +
                "comparables to a relevant window (spec §10.3), so a benchmark comparison cannot " +
                "be attempted without it.");
        }

        if (string.IsNullOrWhiteSpace(line.Description))
        {
            return Result<BenchmarkQuery>.Failure(
                $"QuoteLine {line.Id} has no description to match as a product name.");
        }

        if (line.Quantity is null || line.Quantity <= 0m)
        {
            return Result<BenchmarkQuery>.Failure(
                $"QuoteLine {line.Id} has no positive Quantity — the current price cannot be " +
                "matched against a quantity-tiered comparable without one (Appendix C rule 10: " +
                "invalid/missing structured data is treated the same as missing data, never " +
                "guessed at).");
        }

        if (string.IsNullOrWhiteSpace(line.Term))
        {
            return Result<BenchmarkQuery>.Failure(
                $"QuoteLine {line.Id} has no Term recorded — spec §10.4 requires contract term as " +
                "one of the required multi-dimensional match fields.");
        }

        if (line.UnitPrice is null || line.UnitPrice <= 0m)
        {
            return Result<BenchmarkQuery>.Failure(
                $"QuoteLine {line.Id} has no positive UnitPrice — there is no current price to " +
                "compare against a benchmark distribution.");
        }

        return Result<BenchmarkQuery>.Success(new BenchmarkQuery(
            Supplier: quote.Supplier,
            Product: line.Description,
            Sku: line.NormalizedSku ?? line.Sku,
            Geography: quote.Geography,
            Quantity: line.Quantity.Value,
            Term: line.Term,
            Currency: quote.Currency,
            PurchaseDate: purchaseDate));
    }
}
