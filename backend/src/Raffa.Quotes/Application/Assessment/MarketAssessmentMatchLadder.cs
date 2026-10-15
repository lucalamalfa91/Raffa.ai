using Raffa.Benchmark.Contracts;
using Raffa.Quotes.Domain;

namespace Raffa.Quotes.Application.Assessment;

/// <summary>How a line's benchmark was found (task F6-T09). Ordered from the strictest.</summary>
public enum MarketMatchLevel
{
    /// <summary>With the quote's own supplier, the line's own product and SKU: the query as built.</summary>
    Direct = 0,

    /// <summary>Only after replacing the product and/or SKU with what the tenant confirmed for this SKU
    /// (<see cref="SkuProductMapping"/>).</summary>
    ProductAlias = 1,

    /// <summary>Only after replacing the supplier with another name the tenant has recorded for it.</summary>
    SupplierAlias = 2,

    /// <summary>Only after both.</summary>
    SupplierAndProductAlias = 3,
}

/// <summary>One retry of the benchmark with the tenant's own names, and the level it stands for.</summary>
internal sealed record MarketMatchCandidate(BenchmarkQuery Query, MarketMatchLevel Level);

/// <summary>
/// Task F6-T09: the ordered retries a line's benchmark query gets when the query as built found nothing.
/// Pure and deterministic; the caller stops at the first retry that finds a distribution and, when none
/// does, keeps the original (abstains).
///
/// <para>
/// Why a ladder and not a replacement. The benchmark matches names by equality, so "Salesforce, Inc." and
/// "Sales Cloud Enterprise Edition (annual)" never meet the corpus's "Salesforce" and "Sales Cloud
/// Enterprise". The tenant already says what they mean: the supplier record in
/// <c>Raffa.Suppliers.Products</c> holds the names it is known by, and the manual SKU mapping
/// (<see cref="SkuProductMapping"/>) holds the confirmed canonical product and SKU. Using them only
/// <em>after</em> the direct query failed means the match rate can only go up: a line that matched
/// before matches the same way, with the same figures. Every level is a name the tenant recorded, so
/// nothing is guessed, and the level is reported with the result.
/// </para>
/// </summary>
internal static class MarketAssessmentMatchLadder
{
    /// <summary>Upper bound on extra benchmark calls for one line.</summary>
    public const int MaxRetries = 6;

    public static IReadOnlyList<MarketMatchCandidate> Retries(
        BenchmarkQuery direct, IReadOnlyList<string> supplierNames, SkuProductMapping? mapping)
    {
        ArgumentNullException.ThrowIfNull(direct);

        var productQuery = ProductAlias(direct, mapping);
        var suppliers = supplierNames
            .Where(name => !string.IsNullOrWhiteSpace(name)
                && !string.Equals(name, direct.Supplier, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var candidates = new List<MarketMatchCandidate>();

        if (productQuery is not null)
        {
            candidates.Add(new MarketMatchCandidate(productQuery, MarketMatchLevel.ProductAlias));
        }

        candidates.AddRange(suppliers.Select(name =>
            new MarketMatchCandidate(direct with { Supplier = name }, MarketMatchLevel.SupplierAlias)));

        if (productQuery is not null)
        {
            candidates.AddRange(suppliers.Select(name =>
                new MarketMatchCandidate(productQuery with { Supplier = name }, MarketMatchLevel.SupplierAndProductAlias)));
        }

        return candidates
            .DistinctBy(c => c.Query)
            .Take(MaxRetries)
            .ToList();
    }

    /// <summary>The query with the confirmed canonical product name and/or SKU, or null when the line has
    /// no mapping or the mapping changes nothing.</summary>
    private static BenchmarkQuery? ProductAlias(BenchmarkQuery direct, SkuProductMapping? mapping)
    {
        if (mapping is null)
        {
            return null;
        }

        var product = string.IsNullOrWhiteSpace(mapping.CanonicalProductName)
            ? direct.Product
            : mapping.CanonicalProductName.Trim();
        var sku = string.IsNullOrWhiteSpace(mapping.CanonicalSku) ? direct.Sku : mapping.CanonicalSku.Trim();

        var query = direct with { Product = product, Sku = sku };
        return query == direct ? null : query;
    }
}
