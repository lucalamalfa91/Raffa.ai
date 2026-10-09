using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Shared.Guards;
using Raffa.AiGateway.Contracts;

namespace Raffa.AiFlows.WebResearch.Guards;

/// <summary>What kind of figure a web summary stated.</summary>
public enum WebFigureKind
{
    Percentage,
    Amount,
    Date,
    MonthYear,
}

/// <summary>
/// The three states of a figure in a web summary (F3-D02).
/// </summary>
public enum WebFigureState
{
    /// <summary>The figure appears (same value, same currency or unit, any of the five languages'
    /// formats) in the verbatim quote of a source cited in its own sentence.</summary>
    Verified,

    /// <summary>The figure is explicit (a percentage, an amount with its ISO currency code, a full date)
    /// and attributed to a cited source, but none of the cited sources gave a passage to check it
    /// against. Shown only when the caller allows it.</summary>
    Reported,

    /// <summary>The figure has no marker in its sentence, or a cited source's quote does not carry it, or it
    /// is a symbol, word or shorthand figure nothing can check. Its sentence is removed.</summary>
    Rejected,
}

/// <summary>One figure found in a web summary and how it was judged.</summary>
/// <param name="Raw">The figure as written in the summary.</param>
/// <param name="Kind">See <see cref="WebFigureKind"/>.</param>
/// <param name="State">See <see cref="WebFigureState"/>.</param>
/// <param name="Reason">Diagnostics only, never shown: why the figure got its state.</param>
public sealed record WebFigureFinding(string Raw, WebFigureKind Kind, WebFigureState State, string Reason);

/// <summary>The result of <see cref="WebFigureGuard.Verify"/>.</summary>
/// <param name="Markdown">The summary with the sentences that stated a rejected figure removed (equal to
/// the input when nothing was).</param>
/// <param name="Findings">Every figure found, in reading order, with its state.</param>
/// <param name="SentencesRemoved">How many sentences were removed.</param>
/// <param name="HasCitedClaim">Whether at least one sentence of <see cref="Markdown"/> carries a marker, i.e.
/// the answer still says something a source backs.</param>
/// <param name="FirstRemovalReason">The diagnostic of the first figure that cost a sentence, or
/// <see langword="null"/>.</param>
public sealed record WebFigureReport(
    string Markdown,
    IReadOnlyList<WebFigureFinding> Findings,
    int SentencesRemoved,
    bool HasCitedClaim,
    string? FirstRemovalReason = null)
{
    public int Verified => Findings.Count(f => f.State == WebFigureState.Verified);

    public int Reported => Findings.Count(f => f.State == WebFigureState.Reported);
}

/// <summary>
/// F3-T01 / F3-D02 — the figure check of a web-research summary. The hosted search tool returns a URL and
/// a title for each source and no page text, so a figure can only be checked against the passage the
/// model copied verbatim from the page (<see cref="AiWebSource.Quote"/>), plus the page title and any
/// provider snippet. The rules:
///
/// <list type="number">
/// <item>A figure (percentage, currency amount in symbol, ISO-code or word form, magnitude shorthand
/// <c>40k</c> / <c>1,5M</c>, full date in any of the five languages or US order, month and year, a
/// percentage spelled out, both bounds of a range) must share its sentence with a <c>[n]</c> marker.</item>
/// <item>It is <see cref="WebFigureState.Verified"/> when the quote (or title or snippet) of a source that
/// sentence cites carries the same value, read locale-aware (<see cref="FigureEvidence"/>): <c>EUR 1.200,00</c>
/// in the summary matches <c>€1,200</c> in the quote, and a currency never matches another's.</item>
/// <item>It is <see cref="WebFigureState.Reported"/> only when no cited source gave a passage at all and
/// the figure is explicit (percentage, ISO-coded amount, full date) — never a symbol, a word or a shorthand.</item>
/// <item>Otherwise it is <see cref="WebFigureState.Rejected"/> and the sentence that states it is removed,
/// not the whole answer; when no cited claim is left the caller abstains.</item>
/// </list>
/// Pure, no I/O.
/// </summary>
public static class WebFigureGuard
{
    private static readonly Regex MarkerPattern = new(@"\[(\d+)\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IsoCurrencyPattern = new(
        @"(?<![\p{L}\d])(?:CHF|EUR|USD|GBP)(?![\p{L}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BlankLines = new(@"(?:[ \t]*\r?\n){3,}", RegexOptions.Compiled);

    /// <summary>
    /// Judges every figure of <paramref name="summaryMarkdown"/> against the sources its sentence cites.
    /// </summary>
    /// <param name="summaryMarkdown">The research summary, markers already validated against
    /// <paramref name="sources"/> by <see cref="WebGuard"/>.</param>
    /// <param name="sources">The sources in marker order: <c>[n]</c> is <c>sources[n - 1]</c>.</param>
    /// <param name="language">The summary's language (<c>it</c>, <c>en</c>, <c>fr</c>, <c>es</c>,
    /// <c>de</c>), when known: it settles <c>1.250</c> and the day/month order of <c>03/04/2027</c>.</param>
    /// <param name="allowReported">Keep a sentence whose figures are only <see cref="WebFigureState.Reported"/>.
    /// Off, such a sentence is removed like a rejected one.</param>
    public static WebFigureReport Verify(
        string? summaryMarkdown,
        IReadOnlyList<AiWebSource> sources,
        string? language = null,
        bool allowReported = false)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var markdown = summaryMarkdown ?? string.Empty;
        var convention = NumericLocale.ConventionFor(language);
        var evidence = sources.Select(SourceEvidence.From).ToList();
        var lines = WebSentenceSplitter.Split(markdown);

        var findings = new List<WebFigureFinding>();
        var removed = new HashSet<WebSentence>();
        string? firstReason = null;

        foreach (var line in lines)
        {
            foreach (var sentence in line.Sentences)
            {
                var markers = MarkerPattern.Matches(sentence.Text)
                    .Select(m => int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
                    .Where(n => n >= 1 && n <= sources.Count)
                    .Distinct()
                    .ToList();
                var cited = markers.Select(n => evidence[n - 1]).ToList();

                var drop = false;
                foreach (var figure in FiguresIn(sentence.Text, convention))
                {
                    var (state, reason) = Judge(figure, markers.Count > 0, cited);
                    var kind = KindOf(figure);
                    findings.Add(new WebFigureFinding(figure.Raw, kind, state, reason));

                    if (state == WebFigureState.Rejected || (state == WebFigureState.Reported && !allowReported))
                    {
                        drop = true;
                        firstReason ??= $"{DescribeKind(kind)} '{figure.Raw}' {reason}.";
                    }
                }

                if (drop)
                {
                    removed.Add(sentence);
                }
            }
        }

        var cleaned = removed.Count == 0 ? markdown : Rebuild(markdown, lines, removed);
        var hasClaim = WebSentenceSplitter.Split(cleaned).Any(l => l.Sentences.Any(s => MarkerPattern.IsMatch(s.Text)));

        return new WebFigureReport(cleaned, findings, removed.Count, hasClaim, firstReason);
    }

    private static (WebFigureState State, string Reason) Judge(NumericToken figure, bool hasMarker, IReadOnlyList<SourceEvidence> cited)
    {
        if (!hasMarker || cited.Count == 0)
        {
            return (WebFigureState.Rejected, "has no [n] marker in its sentence, so no source stands behind it");
        }

        if (cited.Any(e => e.Text.Supports(figure)))
        {
            return (WebFigureState.Verified, "appears in the quote of a cited source");
        }

        if (cited.Any(e => e.HasPassage))
        {
            return (WebFigureState.Rejected, "does not appear in the quote of any source cited in its sentence");
        }

        return IsExplicit(figure)
            ? (WebFigureState.Reported, "is explicit and cited, but no cited source gave a quote to check it against")
            : (WebFigureState.Rejected, "cannot be checked: no cited source gave a quote, and a symbol, word or shorthand figure is never taken on trust");
    }

    // Explicit: stated so it cannot be misread. A percentage, an amount with its ISO currency code, a full date.
    private static bool IsExplicit(NumericToken figure) => figure switch
    {
        PercentToken or DateToken => true,
        MoneyToken => IsoCurrencyPattern.IsMatch(figure.Raw),
        _ => false,
    };

    private static WebFigureKind KindOf(NumericToken figure) => figure switch
    {
        PercentToken => WebFigureKind.Percentage,
        MoneyToken => WebFigureKind.Amount,
        DateToken => WebFigureKind.Date,
        _ => WebFigureKind.MonthYear,
    };

    private static string DescribeKind(WebFigureKind kind) => kind switch
    {
        WebFigureKind.Percentage => "percentage",
        WebFigureKind.Amount => "amount",
        WebFigureKind.Date => "date",
        _ => "month and year",
    };

    // Rebuilds the markdown without the removed sentences: a line left with none of its sentences goes
    // entirely (list prefix and newline included); a line that keeps some is rejoined with single spaces.
    private static string Rebuild(string markdown, IReadOnlyList<WebLine> lines, HashSet<WebSentence> removed)
    {
        var builder = new StringBuilder(markdown.Length);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var kept = line.Sentences.Where(s => !removed.Contains(s)).ToList();
            var touched = kept.Count != line.Sentences.Count;

            if (!touched)
            {
                builder.Append(markdown, line.Start, line.End - line.Start);
            }
            else if (kept.Count > 0)
            {
                builder.Append(markdown, line.Start, line.ContentStart - line.Start);
                builder.Append(string.Join(" ", kept.Select(s => s.Text)));
            }
            else
            {
                continue;
            }

            if (index < lines.Count - 1)
            {
                builder.Append('\n');
            }
        }

        var text = BlankLines.Replace(builder.ToString(), "\n\n").Trim();
        return text;
    }

    // Every figure of a sentence, each once (same shape, text and value).
    private static IEnumerable<NumericToken> FiguresIn(string sentence, DecimalConvention convention)
    {
        List<NumericToken> all =
        [
            .. NumericTokenExtractor.Percentages(sentence, convention),
            .. WebFigureTokens.SpelledPercentages(sentence),
            .. NumericTokenExtractor.Money(sentence, convention),
            .. NumericTokenExtractor.Dates(sentence, convention),
            .. WebFigureTokens.MonthYears(sentence),
        ];

        // The lower bound of "5-10%" or "$10-15" is a figure too; it carries the range as its raw text.
        foreach (var range in WebFigureTokens.RangeBounds(sentence))
        {
            all.AddRange(NumericTokenExtractor.Percentages(range.Synthetic, convention).Select(p => p with { Raw = range.Raw }));
            all.AddRange(NumericTokenExtractor.Money(range.Synthetic, convention).Select(m => m with { Raw = range.Raw }));
        }

        return all.DistinctBy(f => (f.GetType(), f.Raw, (f as PercentToken)?.Value ?? (f as MoneyToken)?.Value));
    }

    /// <summary>What one source lets a figure be checked against: its quote, snippet and title, read once.</summary>
    private sealed record SourceEvidence(FigureEvidence Text, bool HasPassage)
    {
        public static SourceEvidence From(AiWebSource source)
        {
            var quote = source.Quote?.Trim() ?? string.Empty;
            var snippet = source.Snippet?.Trim() ?? string.Empty;
            var title = source.Title?.Trim() ?? string.Empty;
            var text = string.Join("\n", new[] { title, quote, snippet }.Where(t => t.Length > 0));
            return new SourceEvidence(new FigureEvidence(text, web: true), quote.Length > 0 || snippet.Length > 0);
        }
    }
}
