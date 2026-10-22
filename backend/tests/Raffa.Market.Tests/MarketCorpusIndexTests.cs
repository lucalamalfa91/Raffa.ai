using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure.Entities;
using Raffa.Market.Ingestion;
using Raffa.Market.Mock;
using Raffa.Market.Retrieval;

namespace Raffa.Market.Tests;

/// <summary>
/// The in-memory lookups of <see cref="MarketCorpusIndex"/> against the SQL they replace, written out
/// as LINQ-to-objects in <see cref="MarketCorpusTestKit"/> (the same predicate on the same stored
/// columns -- NULL kept as "match anything" -- the same ORDER BY, then the same exact check): same
/// elements in the same order, over the checked-in 25.7k-record fixture and over hand-made deals that
/// include NULL filter columns, ties and inconsistent columns. No database.
/// </summary>
public sealed class MarketCorpusIndexTests
{
    private static readonly Lazy<IReadOnlyList<MarketDeal>> Fixture = new(() =>
        new MockMarketIntelligenceProvider().GetDealsAsync().GetAwaiter().GetResult().Value.Deals);

    private static readonly DateTimeOffset T = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static MarketDeal Deal(
        string id, string supplier, string category = "Enterprise Software", string product = "Sales Cloud",
        int days = 0) =>
        SampleDeal.Create(recordId: id, supplier: supplier, category: category, product: product, updatedAt: T.AddDays(days));

    private static List<(MarketRecordEntity Row, MarketDeal Deal)> HandMade() => MarketCorpusTestKit.Rows(
    [
        Deal("R-07", "Salesforce", days: 3),
        Deal("R-02", "Salesforce, Inc.", days: 3),
        Deal("R-03", "salesforce.com Ltd", days: 1),
        Deal("R-04", "Sales Force Automation GmbH", "Cloud", "CRM", days: 2),
        Deal("R-05", "Amazon Web Services", "Cloud", "EC2"),
        Deal("R-06", "Web Services Ltd", "cloud", "EC2", days: 5),
        Deal("R-01", "AWS", "Cloud", "S3", days: 5),
        Deal("R-08", "Allianz SE", "Insurance", "Property"),
        Deal("R-09", "Allianz", "Insurance", "Property", days: 3),
        Deal("R-10", "Micro", "Enterprise Software", "Edge", days: 3),
        Deal("R-11", "Microsoft", "Enterprise Software", "Edge", days: 3),
        Deal("R-12", "Inc", "Enterprise Software", "Nothing"),
    ]);

    private static readonly string[] SupplierQueries =
    [
        "Salesforce", "salesforce inc", "Salesforce Inc.", "salesforce.com", "Sales", "Force", "Sales Force Automation",
        "Web Services", "Amazon Web Services Inc", "AWS", "aws ltd", "Allianz", "Allianz SE", "Micro", "Microsoft Corp",
        "Nobody", "Inc", "Ltd", "  ", "", "Cloud", "R", "salesforce  inc  gmbh",
    ];

    private static readonly string[] CategoryQueries =
        ["Cloud", "cloud", "  CLOUD ", "Enterprise Software", "enterprise software", "Insurance", "Unknown", "", "   ", "Support & Services"];

    private static IEnumerable<string> Ids(IEnumerable<MarketDeal> deals) => deals.Select(d => d.RecordId);

    [Fact]
    public void Supplier_lookup_equals_the_sql_over_hand_made_deals_including_ties_and_null_columns()
    {
        var table = HandMade();
        var index = MarketCorpusTestKit.Index(table);

        foreach (var query in SupplierQueries)
        {
            Assert.Equal(MarketCorpusTestKit.OldBySupplier(table, query), Ids(index.BySupplier(query)));
        }

        // Ties on updated_at are broken by record id, as ORDER BY ... record_id did.
        Assert.Equal(["R-02", "R-07", "R-03"], Ids(index.BySupplier("Salesforce")).Take(3));
        Assert.Empty(index.BySupplier(null!));
    }

    [Fact]
    public void Supplier_lookup_equals_the_sql_when_the_filter_columns_are_missing_or_disagree_with_the_payload()
    {
        var table = HandMade();
        // Rows that predate the columns: NULL means "decided by the exact check".
        MarketCorpusTestKit.BlankFilterColumns(table[0].Row);
        MarketCorpusTestKit.BlankFilterColumns(table[3].Row);
        MarketCorpusTestKit.BlankFilterColumns(table[8].Row);
        // A column the SQL predicate rejects although the payload would match: the row must stay out.
        table[1].Row.SupplierNorm = "zzz";
        // And one the predicate admits for a different reason than the payload would (contained word).
        table[4].Row.SupplierNorm = "amazon web services extra";
        var index = MarketCorpusTestKit.Index(table);

        foreach (var query in SupplierQueries)
        {
            Assert.Equal(MarketCorpusTestKit.OldBySupplier(table, query), Ids(index.BySupplier(query)));
        }

        Assert.DoesNotContain("R-02", Ids(index.BySupplier("Salesforce")));
        Assert.Contains("R-07", Ids(index.BySupplier("Salesforce")));
    }

    [Fact]
    public void Category_lookup_equals_the_sql_over_hand_made_deals_with_null_columns()
    {
        var table = HandMade();
        var plain = MarketCorpusTestKit.Index(table);
        foreach (var query in CategoryQueries)
        {
            Assert.Equal(MarketCorpusTestKit.OldByCategory(table, query), Ids(plain.ByCategory(query)));
        }

        MarketCorpusTestKit.BlankFilterColumns(table[4].Row);
        MarketCorpusTestKit.BlankFilterColumns(table[7].Row);
        table[5].Row.Category = "something else";
        var mixed = MarketCorpusTestKit.Index(table);
        foreach (var query in CategoryQueries)
        {
            Assert.Equal(MarketCorpusTestKit.OldByCategory(table, query), Ids(mixed.ByCategory(query)));
        }

        Assert.Equal(["R-01", "R-04", "R-05", "R-06"], Ids(plain.ByCategory("cloud")).Order());
    }

    [Fact]
    public void Candidates_equal_the_sql_in_table_order_with_null_columns_matching_anything()
    {
        var table = HandMade();
        MarketCorpusTestKit.BlankFilterColumns(table[2].Row);
        table[6].Row.ProductNorm = null;
        table[9].Row.SupplierNorm = null;
        table[10].Row.SupplierNorm = null;
        table[10].Row.ProductNorm = null;
        var index = MarketCorpusTestKit.Index(table);

        var suppliers = table.Select(r => r.Row.SupplierNorm ?? "x").Append("").Append("nobody").Distinct().ToList();
        var products = table.Select(r => r.Row.ProductNorm ?? "x").Append("").Append("nothing").Distinct().ToList();
        foreach (var supplier in suppliers)
        {
            foreach (var product in products)
            {
                Assert.Equal(
                    MarketCorpusTestKit.OldCandidates(table, supplier, product),
                    Ids(index.Candidates(supplier, product)));
            }
        }
    }

    [Fact]
    public void The_rank_comes_from_the_database_order_not_from_ordinal_string_comparison()
    {
        // Same updated_at; ordinal order is B < a, the database's collation said a < B.
        var table = MarketCorpusTestKit.Rows([Deal("B", "Acme"), Deal("a", "Acme"), Deal("b", "Acme")]);
        var index = MarketCorpusIndex.Build(table, ["a", "b", "B"], T);

        Assert.Equal(["a", "b", "B"], Ids(index.BySupplier("Acme")));
        Assert.Equal(["a", "b", "B"], Ids(index.ByCategory("Enterprise Software")));
    }

    [Fact]
    public void A_row_the_ordering_read_did_not_return_sorts_after_the_ranked_ones()
    {
        var table = MarketCorpusTestKit.Rows([Deal("N2", "Acme", days: 9), Deal("N1", "Acme", days: 9), Deal("A", "Acme")]);
        var index = MarketCorpusIndex.Build(table, ["A"], T.AddDays(9));

        Assert.Equal(["A", "N1", "N2"], Ids(index.BySupplier("Acme")));
    }

    [Fact]
    public void A_deal_without_supplier_or_category_is_skipped_not_fatal()
    {
        var table = MarketCorpusTestKit.Rows([Deal("OK", "Acme")]);
        var broken = Deal("BROKEN", "x") with { Supplier = null!, Category = null! };
        table.Add((table[0].Row, broken));

        var index = MarketCorpusTestKit.Index(table);

        Assert.Equal(["OK"], Ids(index.BySupplier("Acme")));
        Assert.Equal(["OK"], Ids(index.ByCategory("enterprise software")));
    }

    [Fact]
    public void Lookups_return_the_deserialized_deal_instances_the_snapshot_holds()
    {
        var table = HandMade();
        var index = MarketCorpusTestKit.Index(table);

        var found = Assert.Single(index.BySupplier("Allianz SE"), d => d.RecordId == "R-08");

        Assert.Same(table.Single(r => r.Deal.RecordId == "R-08").Deal, found);
    }

    [Fact]
    public void An_empty_corpus_answers_empty()
    {
        var index = MarketCorpusIndex.Build([], [], null);

        Assert.Empty(index.BySupplier("Acme"));
        Assert.Empty(index.ByCategory("Cloud"));
        Assert.Empty(index.Candidates("acme", "x"));
        Assert.Null(index.LatestUpdatedAt);
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void Lookups_equal_the_sql_over_the_whole_25k_fixture()
    {
        var table = MarketCorpusTestKit.Rows(Fixture.Value);
        // A tenth of a percent of rows predate the filter columns, scattered across the table.
        for (var i = 0; i < table.Count; i += 997)
        {
            MarketCorpusTestKit.BlankFilterColumns(table[i].Row);
        }

        var index = MarketCorpusTestKit.Index(table);
        Assert.Equal(Fixture.Value.Count, index.Count);

        var suppliers = Fixture.Value.Select(d => d.Supplier).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        var queries = suppliers
            .SelectMany(s => new[] { s, s + " Inc", s.ToUpperInvariant(), s.Split(' ')[0] })
            .Concat(["Nobody", "", "Inc"])
            .Distinct()
            .ToList();
        foreach (var query in queries)
        {
            Assert.Equal(MarketCorpusTestKit.OldBySupplier(table, query), Ids(index.BySupplier(query)));
        }

        var categories = Fixture.Value.Select(d => d.Category).Distinct().ToList();
        foreach (var category in categories.SelectMany(c => new[] { c, c.ToUpperInvariant(), "  " + c.ToLowerInvariant() + " " }).Append("Unknown"))
        {
            Assert.Equal(MarketCorpusTestKit.OldByCategory(table, category), Ids(index.ByCategory(category)));
        }

        // The benchmark adapter's candidate query for a spread of (supplier, product) pairs.
        foreach (var deal in Fixture.Value.Where((_, i) => i % 211 == 0))
        {
            var supplier = MarketFilterColumns.SupplierNorm(deal.Supplier);
            var product = MarketFilterColumns.ProductNorm(deal.Product);
            Assert.Equal(MarketCorpusTestKit.OldCandidates(table, supplier, product), Ids(index.Candidates(supplier, product)));
        }

        Assert.Equal(Fixture.Value.Max(d => d.UpdatedAt), index.LatestUpdatedAt);
    }
}
