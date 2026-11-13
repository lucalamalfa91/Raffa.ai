using Raffa.Documents.Contracts.Application.V4;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>Revisione: Raffa's reasoning behind a flagged field (stated / derived / not stated).</summary>
public sealed class ReadingBasisBuilderTests
{
    private static readonly ReadingQuote NoticeQuote =
        new("this Order will renew automatically unless Customer gives non-renewal notice at least ", "105 days", " before expiry.");

    private static Dictionary<string, ReadingBasisBuilder.Evidence> Evidence(params ReadingBasisBuilder.Evidence[] rows) =>
        rows.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Notice_deadline_is_explained_as_end_of_term_minus_notice_period()
    {
        var all = Evidence(
            new("endDate", "2028-07-20", 1, new ReadingQuote("Term ends on ", "20 July 2028", "."), new ReadingBox(1, 2, 3, 4)),
            new("noticePeriodDays", "105", 1, NoticeQuote, null));

        var basis = ReadingBasisBuilder.For("cancellationDeadline", "2028-04-06", NoticeQuote, 1, all);

        Assert.Equal(BasisKinds.Derived, basis.Kind);
        Assert.StartsWith("The contract does not write this date: I worked it out", basis.Explanation.Id);
        Assert.Equal("2028-07-20", basis.Explanation.Vars!["end"]);
        Assert.Equal(105, basis.Explanation.Vars!["days"]);
        Assert.Equal(["endDate", "noticePeriodDays"], basis.Sources.Select(s => s.Key));
        Assert.Equal(new ReadingBox(1, 2, 3, 4), basis.Sources[0].Box);
    }

    [Fact]
    public void A_notice_deadline_that_does_not_add_up_says_so()
    {
        var all = Evidence(
            new("endDate", "2028-07-20", 1, null, null),
            new("noticePeriodDays", "90 days", 1, NoticeQuote, null));

        var basis = ReadingBasisBuilder.For("cancellationDeadline", "2028-04-06", NoticeQuote, 1, all);

        Assert.Equal(BasisKinds.Derived, basis.Kind);
        Assert.Contains("check which one is right", basis.Explanation.Id, StringComparison.Ordinal);
        Assert.Equal("2028-04-21", basis.Explanation.Vars!["expected"]);
    }

    [Fact]
    public void A_notice_deadline_with_only_the_notice_period_counts_back_from_the_end_of_term()
    {
        var all = Evidence(new ReadingBasisBuilder.Evidence("noticePeriodDays", "105", 1, NoticeQuote, null));

        var basis = ReadingBasisBuilder.For("cancellationDeadline", "2028-04-06", NoticeQuote, 1, all);

        Assert.Equal(BasisKinds.Derived, basis.Kind);
        Assert.Single(basis.Sources);
    }

    [Fact]
    public void A_field_with_a_quoted_passage_is_stated_there()
    {
        var basis = ReadingBasisBuilder.For("currency", "EUR", new ReadingQuote("Fees are in ", "EUR", "."), 3, Evidence());

        Assert.Equal(BasisKinds.Stated, basis.Kind);
        Assert.Equal(3, basis.Explanation.Vars!["page"]);
        Assert.Empty(basis.Sources);
    }

    [Fact]
    public void A_field_without_any_passage_is_not_stated()
    {
        var basis = ReadingBasisBuilder.For("governingLaw", "Swiss law", null, null, Evidence());

        Assert.Equal(BasisKinds.NotStated, basis.Kind);
    }
}
