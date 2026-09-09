namespace Contigo.Documents.Contracts.Tests;

/// <summary>
/// The one behaviour this module's test doubles need to borrow from
/// <c>Contigo.Suppliers.Products.Application.SupplierNameNormalizer</c>: "Salesforce, Inc." and
/// "salesforce" have to collapse to the same key, or a fake <c>ISupplierResolver</c> would mint two
/// rows where the real resolver returns one, and the tests built on it (a re-typed supplier is a
/// no-op; one contract keeps one supplier id) would prove the opposite of production behaviour.
///
/// <para>
/// Deliberately a small, local re-implementation rather than a reference to the real normalizer:
/// ADR-002 keeps <c>Contigo.Documents.Contracts</c> — and therefore its test project — from
/// referencing <c>Contigo.Suppliers.Products</c> at all. The real normalizer's exact suffix list and
/// punctuation rules are proven by <c>Contigo.Suppliers.Products.Tests</c>; this only needs to be
/// faithful enough for the equivalences the tests here actually assert on.
/// </para>
/// </summary>
internal static class SupplierTestNames
{
    private static readonly HashSet<string> LegalSuffixes = new(StringComparer.Ordinal)
    {
        "inc", "ltd", "limited", "gmbh", "ag", "sa", "spa", "srl", "llc", "corp", "corporation", "co",
    };

    private static readonly char[] Separators = [' ', ',', '.', '-', '\t'];

    public static string Simplify(string rawName) =>
        string.Concat(rawName
            .ToLowerInvariant()
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => !LegalSuffixes.Contains(word)));
}
