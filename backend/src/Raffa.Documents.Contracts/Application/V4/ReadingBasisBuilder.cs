using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.SharedKernel.Contracts;

namespace Raffa.Documents.Contracts.Application.V4;

/// <summary>
/// Explains, for Revisione, how Raffa arrived at a field's value so the user can check it against the
/// contract instead of taking a bare value on trust: written in the highlighted passage, worked out
/// from other passages (a notice deadline is the end of the term minus the notice period), or not
/// stated anywhere. The derivation mirrors the extraction rule (<c>CrossStageRules</c>:
/// <c>cancellationDeadline == endDate − noticePeriodDays</c>); it never invents a passage.
/// </summary>
public static partial class ReadingBasisBuilder
{
    private const string CancellationDeadline = "cancellationDeadline";
    private const string EndDate = "endDate";
    private const string NoticePeriodDays = "noticePeriodDays";

    /// <summary>The value of a field as read (the reviewer's override first), with its passage and box.</summary>
    public sealed record Evidence(string Key, string? Value, int? Page, ReadingQuote? Quote, ReadingBox? Box);

    public static ReadingBasis For(
        string key, string value, ReadingQuote? quote, int? page, IReadOnlyDictionary<string, Evidence> all)
    {
        ArgumentNullException.ThrowIfNull(all);
        if (string.Equals(key, CancellationDeadline, StringComparison.OrdinalIgnoreCase)
            && NoticeDeadline(value, all) is { } derived)
        {
            return derived;
        }

        if (quote is not null && !string.IsNullOrWhiteSpace(quote.Mark))
        {
            return new ReadingBasis(
                BasisKinds.Stated,
                page is { } p
                    ? ContractMessage.Of("Read in the highlighted passage on page {page}: compare the value with the words in yellow.", ("page", p))
                    : ContractMessage.Of("Read in the highlighted passage: compare the value with the words in yellow."),
                []);
        }

        return new ReadingBasis(
            BasisKinds.NotStated,
            ContractMessage.Of("No passage of the contract states this value: Raffa inferred it from the rest of the document. Confirm it only if you know it is right, otherwise correct it."),
            []);
    }

    private static ReadingBasis? NoticeDeadline(string value, IReadOnlyDictionary<string, Evidence> all)
    {
        all.TryGetValue(EndDate, out var endEvidence);
        all.TryGetValue(NoticePeriodDays, out var daysEvidence);
        var end = ParseDate(endEvidence?.Value);
        var days = ParseDays(daysEvidence?.Value);
        var sources = new List<ReadingSource>();
        if (end is not null && endEvidence is not null)
        {
            sources.Add(Source(endEvidence));
        }

        if (days is not null && daysEvidence is not null)
        {
            sources.Add(Source(daysEvidence));
        }

        if (end is { } e && days is { } d)
        {
            var expected = e.AddDays(-d);
            var endText = Iso(e);
            var expectedText = Iso(expected);
            return ParseDate(value) is { } read && read != expected
                ? new ReadingBasis(
                    BasisKinds.Derived,
                    ContractMessage.Of(
                        "The contract does not write this date. I read {value}, but the end of the term ({end}) minus the {days}-day notice period gives {expected}: check which one is right.",
                        ("value", value), ("end", endText), ("days", d), ("expected", expectedText)),
                    sources)
                : new ReadingBasis(
                    BasisKinds.Derived,
                    ContractMessage.Of(
                        "The contract does not write this date: I worked it out as the end of the term ({end}) minus the {days}-day notice period. Check both passages below.",
                        ("end", endText), ("days", d)),
                    sources);
        }

        if (days is { } onlyDays)
        {
            return new ReadingBasis(
                BasisKinds.Derived,
                ContractMessage.Of(
                    "The contract does not write this date: it states a {days}-day notice period, so I counted back {days} days from the end of the term. Check the end of the term too.",
                    ("days", onlyDays)),
                sources);
        }

        if (end is { } onlyEnd)
        {
            return new ReadingBasis(
                BasisKinds.Derived,
                ContractMessage.Of(
                    "The contract does not write this date: I worked it out from the end of the term ({end}), but the notice period is not clearly stated. Check the notice clause.",
                    ("end", Iso(onlyEnd))),
                sources);
        }

        return null;
    }

    private static ReadingSource Source(Evidence evidence) =>
        new(evidence.Key, ReadingFieldLabels.For(evidence.Key), evidence.Value ?? "", evidence.Page, evidence.Quote, evidence.Box);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static DateOnly? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (trimmed.Length >= 10
            && DateOnly.TryParseExact(trimmed[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return null;
    }

    internal static int? ParseDays(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = LeadingNumber().Match(text);
        return match.Success && int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days > 0
            ? days
            : null;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex LeadingNumber();
}
