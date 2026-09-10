using Raffa.Benchmark.Contracts;

namespace Raffa.Benchmark.Tests;

public class BenchmarkComparisonDimensionTests
{
    /// <summary>
    /// Guards the enum against silent drift from product spec §10.4's own dimension list
    /// ("supplier, product, SKU, edition, geography, currency, quantity tier, contract term,
    /// customer size, purchase date and billing metric"), verbatim and in the same order.
    /// </summary>
    [Fact]
    public void Defines_every_dimension_named_in_spec_section_10_4_in_order()
    {
        var expected = new[]
        {
            "Supplier", "Product", "Sku", "Edition", "Geography", "Currency",
            "QuantityTier", "ContractTerm", "CustomerSize", "PurchaseDate", "BillingMetric",
        };

        var actual = Enum.GetNames<BenchmarkComparisonDimension>();

        Assert.Equal(expected, actual);
    }

    /// <summary>us-01-benchmark-interface AC-3: the contract's matching vocabulary offers more
    /// than supplier name.</summary>
    [Fact]
    public void Offers_more_than_supplier_name_as_a_matching_dimension()
    {
        var dimensions = Enum.GetValues<BenchmarkComparisonDimension>();

        Assert.True(dimensions.Length > 1);
        Assert.Contains(BenchmarkComparisonDimension.Product, dimensions);
    }
}
