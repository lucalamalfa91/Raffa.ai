using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Market.Contracts;
using Raffa.SharedKernel.Market;

namespace Raffa.Market.Retrieval;

/// <summary>
/// <see cref="IMarketPriceEstimator"/>: the last resort behind the market column, asked only for the
/// lines <see cref="MarketPriceMatcher"/> could not price. Two steps, the more grounded first:
/// <list type="number">
/// <item><b>Converted</b> (<see cref="MarketEstimateKind.Converted"/>): the corpus records in
/// <i>other</i> currencies, converted into the contract's own at an indicative fixed rate
/// (<see cref="UsdPerUnit"/>), then run through the matcher's own rules — so the line's own
/// product, a bundle or a similar product is found exactly as it would be in its own currency.</item>
/// <item><b>AI estimate</b> (<see cref="MarketEstimateKind.AiEstimate"/>): the gateway's analyst role
/// is asked what customers typically pay per unit, given the line, the supplier and a handful of
/// corpus reference prices in the contract's currency — never the price the customer pays, so the
/// estimate is not anchored on it. A band that is not a sane P25 ≤ P50 ≤ P75 is discarded.</item>
/// </list>
/// Neither ever reaches the matched band: the caller stores estimates apart from it.
/// </summary>
public sealed class MarketPriceEstimator(
    IMarketDealLookup dealLookup,
    IAiGateway? aiGateway = null,
    ILogger<MarketPriceEstimator>? logger = null) : IMarketPriceEstimator
{
    public const string AgentName = "market-price-estimator";
    public const string PromptVersion = "market-price-estimator-v2";

    /// <summary>How many corpus reference prices the AI step is shown.</summary>
    private const int MaxReferences = 40;

    /// <summary>Indicative fixed rates (USD per unit of the currency) — an estimate's conversion is
    /// labelled with the rate it used, never presented as a market price.</summary>
    public static readonly IReadOnlyDictionary<string, decimal> UsdPerUnit = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = 1.00m,
        ["EUR"] = 1.08m,
        ["GBP"] = 1.27m,
        ["CHF"] = 1.13m,
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger _logger = logger ?? NullLogger<MarketPriceEstimator>.Instance;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketPriceEstimate?>> EstimateAsync(
        MarketPriceContext context,
        IReadOnlyList<MarketPriceLine> lines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lines);

        var results = new MarketPriceEstimate?[lines.Count];
        if (lines.Count == 0 || string.IsNullOrWhiteSpace(context.Currency))
        {
            return results;
        }

        var corpus = await dealLookup.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var converted = Convert(context, lines, corpus);
        for (var i = 0; i < lines.Count; i++)
        {
            results[i] = converted[i];
        }

        var open = Enumerable.Range(0, lines.Count).Where(i => results[i] is null).ToList();
        if (open.Count > 0 && aiGateway is not null)
        {
            var estimates = await EstimateWithAiAsync(context, open.Select(i => lines[i]).ToList(), corpus, cancellationToken)
                .ConfigureAwait(false);
            for (var k = 0; k < open.Count; k++)
            {
                results[open[k]] = estimates[k];
            }
        }

        return results;
    }

    /// <summary>
    /// The converted step, pure: every corpus record in a currency other than the contract's (and
    /// one <see cref="UsdPerUnit"/> knows) is converted into the contract's currency and matched with
    /// <see cref="MarketPriceMatcher.Match"/>'s own rules, the whole corpus as the wider market.
    /// </summary>
    public static IReadOnlyList<MarketPriceEstimate?> Convert(
        MarketPriceContext context,
        IReadOnlyList<MarketPriceLine> lines,
        IReadOnlyList<MarketDeal> corpus)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(corpus);

        var results = new MarketPriceEstimate?[lines.Count];
        var currency = context.Currency.Trim().ToUpperInvariant();
        if (!UsdPerUnit.TryGetValue(currency, out var targetUsd))
        {
            return results;
        }

        var originals = new Dictionary<string, MarketDeal>(StringComparer.Ordinal);
        var foreign = new List<MarketDeal>();
        foreach (var deal in corpus)
        {
            if (string.Equals(deal.Currency, currency, StringComparison.OrdinalIgnoreCase)
                || !UsdPerUnit.TryGetValue(deal.Currency, out var sourceUsd))
            {
                continue;
            }

            var rate = sourceUsd / targetUsd;
            originals[deal.RecordId] = deal;
            foreign.Add(deal with
            {
                Currency = currency,
                UnitPriceP25 = Math.Round(deal.UnitPriceP25 * rate, 2),
                UnitPriceP50 = Math.Round(deal.UnitPriceP50 * rate, 2),
                UnitPriceP75 = Math.Round(deal.UnitPriceP75 * rate, 2),
            });
        }

        if (foreign.Count == 0)
        {
            return results;
        }

        var supplierDeals = string.IsNullOrWhiteSpace(context.SupplierName)
            ? []
            : foreign.Where(d => MarketSupplierMatch.Matches(d.Supplier, context.SupplierName)).ToList();
        var matches = MarketPriceMatcher.Match(context with { Currency = currency }, lines, supplierDeals, foreign);

        for (var i = 0; i < lines.Count; i++)
        {
            if (matches[i] is not { } match)
            {
                continue;
            }

            var sources = match.RecordId.Split('+')
                .Select(id => originals.GetValueOrDefault(id))
                .OfType<MarketDeal>()
                .ToList();
            if (sources.Count == 0)
            {
                continue;
            }

            var how = match.Kind switch
            {
                MarketMatchKind.Similar => $"a similar product ({match.Product})",
                MarketMatchKind.Bundle => $"{match.Product}, added up",
                _ => match.Product,
            };
            var from = string.Join(", ", sources
                .Select(d => $"{d.Currency.ToUpperInvariant()} {d.UnitPriceP50.ToString("0.##", CultureInfo.InvariantCulture)} ({d.Geography})"));
            var rates = string.Join(", ", sources
                .Select(d => d.Currency.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .Select(c => $"1 {c} = {(UsdPerUnit[c] / targetUsd).ToString("0.00", CultureInfo.InvariantCulture)} {currency}"));

            results[i] = new MarketPriceEstimate(
                MarketEstimateKind.Converted,
                currency,
                match.UnitPriceP25,
                match.UnitPriceP50,
                match.UnitPriceP75,
                $"{how}: median {from}, converted at an indicative fixed rate ({rates}); no record in {currency}.",
                match.Product);
        }

        return results;
    }

    private async Task<IReadOnlyList<MarketPriceEstimate?>> EstimateWithAiAsync(
        MarketPriceContext context,
        IReadOnlyList<MarketPriceLine> lines,
        IReadOnlyList<MarketDeal> corpus,
        CancellationToken cancellationToken)
    {
        var results = new MarketPriceEstimate?[lines.Count];
        var currency = context.Currency.Trim().ToUpperInvariant();

        var references = corpus
            .Where(d => string.Equals(d.Currency, currency, StringComparison.OrdinalIgnoreCase))
            .GroupBy(d => $"{d.Supplier}|{d.Product}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(d => d.SampleSize).First())
            .OrderBy(d => d.Category, StringComparer.Ordinal)
            .ThenBy(d => d.Supplier, StringComparer.Ordinal)
            .Take(MaxReferences)
            .Select(d => new { supplier = d.Supplier, category = d.Category, product = d.Product, unitPriceP50 = d.UnitPriceP50, termMonths = d.TermMonths })
            .ToList();

        var input = JsonSerializer.Serialize(
            new
            {
                supplier = context.SupplierName,
                currency,
                termMonths = context.TermMonths,
                annualContractValue = context.AnnualValue,
                // Quantity sets the volume tier; the other lines' spend is what a support or
                // service line priced as a share of licences is a share of. The line's own price is
                // never shown, so the estimate is not anchored on what the customer pays.
                lines = lines.Select((l, i) => new
                {
                    index = i,
                    description = l.Description,
                    sku = l.Sku,
                    quantity = l.Quantity,
                    otherLinesAnnualCost = OtherLinesAnnualCost(lines, i),
                }),
                referencePrices = references,
            },
            JsonOptions);

        try
        {
            var response = await aiGateway!
                .AnalyzeAsync(new AiAnalysisRequest(AgentName, SystemPrompt, input, ResponseSchema, PromptVersion), cancellationToken)
                .ConfigureAwait(false);
            if (response.IsFailure)
            {
                _logger.LogInformation("Market price estimate unavailable: {Error}", response.Error);
                return results;
            }

            foreach (var estimate in Parse(response.Value.PayloadJson))
            {
                if (estimate.Index < 0 || estimate.Index >= lines.Count || !estimate.CanEstimate)
                {
                    continue;
                }

                if (estimate.P25 <= 0 || estimate.P25 > estimate.P50 || estimate.P50 > estimate.P75)
                {
                    continue;
                }

                results[estimate.Index] = new MarketPriceEstimate(
                    MarketEstimateKind.AiEstimate,
                    currency,
                    Math.Round(estimate.P25, 2),
                    Math.Round(estimate.P50, 2),
                    Math.Round(estimate.P75, 2),
                    Truncate(string.IsNullOrWhiteSpace(estimate.Rationale) ? "AI estimate, no market record." : estimate.Rationale.Trim(), 500),
                    string.IsNullOrWhiteSpace(estimate.Product) ? null : Truncate(estimate.Product.Trim(), 300));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Market price estimate failed; the lines keep no estimate.");
        }

        return results;
    }

    /// <summary>The estimates in <paramref name="payloadJson"/>; empty for anything that is not the
    /// expected shape (a fixture or a model answering something else).</summary>
    public static IReadOnlyList<AiLineEstimate> Parse(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<AiEstimatePayload>(payloadJson, JsonOptions)?.Estimates ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static decimal? OtherLinesAnnualCost(IReadOnlyList<MarketPriceLine> lines, int index)
    {
        var others = lines.Where((l, i) => i != index && l.AnnualCost is > 0).ToList();
        return others.Count == 0 ? null : others.Sum(l => l.AnnualCost!.Value);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    public sealed record AiLineEstimate(int Index, bool CanEstimate, string? Product, decimal P25, decimal P50, decimal P75, string? Rationale);

    private sealed record AiEstimatePayload(IReadOnlyList<AiLineEstimate>? Estimates);

    private const string SystemPrompt =
        "You are a procurement pricing analyst. For each contract line, estimate the unit price that " +
        "customers of this supplier (or of directly comparable products) typically pay, in the given " +
        "currency and per the same unit the line is sold in (per user/licence per year for a subscription, " +
        "a one-off amount for a one-off fee). Give a P25-P50-P75 band of NEGOTIATED prices, not list prices. " +
        "Rules: (1) quantity sets the volume tier - enterprise deals with hundreds or thousands of seats " +
        "are discounted well below list (often 30-70% off), so scale the band to the line's quantity and " +
        "to annualContractValue. (2) A support, premium support, success or maintenance plan sold as one " +
        "package is usually priced as a percentage of the licences it covers (typically 15-25% of their " +
        "annual cost for premium tiers): when otherLinesAnnualCost is given, estimate from it, not from " +
        "a small-customer package price. (3) A one-off implementation or onboarding fee without scope: " +
        "estimate only if the supplier publishes a typical fee, otherwise canEstimate false. " +
        "(4) Keep units consistent: never compare a monthly list price with an annual line - convert. " +
        "Use referencePrices only as calibration for the market's price level, never copy one unless it " +
        "is the same product. Set canEstimate to false when the line is not a priced product (a discount, " +
        "a tax) or when you have no sound basis - no estimate is better than a misleading one. " +
        "rationale: one short sentence a buyer can check (which product, which public list or typical " +
        "price, and the volume or percentage assumption applied). Never invent precision: round to " +
        "sensible figures.";

    private const string ResponseSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["estimates"],
          "properties": {
            "estimates": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["index", "canEstimate", "product", "p25", "p50", "p75", "rationale"],
                "properties": {
                  "index": { "type": "integer" },
                  "canEstimate": { "type": "boolean" },
                  "product": { "type": "string" },
                  "p25": { "type": "number" },
                  "p50": { "type": "number" },
                  "p75": { "type": "number" },
                  "rationale": { "type": "string" }
                }
              }
            }
          }
        }
        """;
}
