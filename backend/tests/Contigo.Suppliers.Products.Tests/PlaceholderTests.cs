namespace Contigo.Suppliers.Products.Tests;

/// <summary>
/// Placeholder for task E13/F01/US01/T01 (v2-scaffold). <c>Contigo.Suppliers.Products</c> is
/// still the bare class library earlier tasks left it as — no <c>Supplier</c> entity, no
/// <c>ISupplierResolver</c> port yet (gap G-SUPPLIER, <c>reports/audit/ask-v2-gaps.md</c>) —
/// so there is nothing behavioural to assert. This test proves the project itself compiles,
/// its reference to <c>Contigo.Suppliers.Products</c> resolves, and it runs under
/// <c>dotnet test Contigo.slnx</c>, rather than leaving the module without a test project at
/// all. Replace with real coverage when the supplier entity / resolver / back-fill tasks
/// (F03/T01, F03/T02) land.
/// </summary>
public class PlaceholderTests
{
    [Fact]
    public void Test_assembly_is_named_after_this_project()
    {
        var assembly = typeof(PlaceholderTests).Assembly;

        Assert.Equal("Contigo.Suppliers.Products.Tests", assembly.GetName().Name);
    }
}
