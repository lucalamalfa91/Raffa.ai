using Raffa.Benchmark.Contracts;

namespace Raffa.Benchmark.Tests;

public class BenchmarkQueryTests
{
    private static readonly DateOnly PurchaseDate = new(2026, 1, 15);

    [Fact]
    public void Constructs_from_the_spec_10_3_signature_with_all_fields_populated()
    {
        var query = new BenchmarkQuery(
            Supplier: "AWS",
            Product: "EC2 Compute",
            Sku: "m5.large",
            Geography: "US",
            Quantity: 100m,
            Term: "12 months",
            Currency: "USD",
            PurchaseDate: PurchaseDate);

        Assert.Equal("AWS", query.Supplier);
        Assert.Equal("EC2 Compute", query.Product);
        Assert.Equal("m5.large", query.Sku);
        Assert.Equal("US", query.Geography);
        Assert.Equal(100m, query.Quantity);
        Assert.Equal("12 months", query.Term);
        Assert.Equal("USD", query.Currency);
        Assert.Equal(PurchaseDate, query.PurchaseDate);
    }

    [Fact]
    public void Sku_is_the_only_optional_dimension()
    {
        var query = new BenchmarkQuery("AWS", "EC2 Compute", null, "US", 100m, "12 months", "USD", PurchaseDate);

        Assert.Null(query.Sku);
    }

    [Fact]
    public void Two_queries_with_the_same_values_are_equal()
    {
        var a = new BenchmarkQuery("AWS", "EC2", "m5.large", "US", 10m, "12 months", "USD", PurchaseDate);
        var b = new BenchmarkQuery("AWS", "EC2", "m5.large", "US", 10m, "12 months", "USD", PurchaseDate);

        Assert.Equal(a, b);
    }

    /// <summary>
    /// us-01-benchmark-interface AC-3: matching must use more than supplier name. Proven here as a
    /// structural fact about the wire contract itself — <see cref="BenchmarkQuery"/>'s constructor
    /// has no overload that accepts supplier alone, so nothing that only names a supplier can be a
    /// valid <c>IBenchmarkService.GetBenchmarkAsync</c> request. This reflection assertion fails
    /// loudly if a future change ever widens the contract back down to a supplier-only lookup.
    /// </summary>
    [Fact]
    public void Contract_requires_more_dimensions_than_supplier_name_alone()
    {
        var requiredNonSupplierParameters = new[]
        {
            "Product", "Geography", "Quantity", "Term", "Currency", "PurchaseDate",
        };

        var constructor = typeof(BenchmarkQuery).GetConstructors().Single();
        var parameterNames = constructor.GetParameters().Select(p => p.Name).ToArray();

        Assert.Contains("Supplier", parameterNames);
        foreach (var required in requiredNonSupplierParameters)
        {
            Assert.Contains(required, parameterNames);
        }
    }
}
