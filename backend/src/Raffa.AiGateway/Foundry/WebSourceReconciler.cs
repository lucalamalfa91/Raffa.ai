using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.AiGateway.Contracts;

namespace Raffa.AiGateway.Foundry;

/// <summary>One entry of the model's own <c>sources[]</c> list: the number its <c>[n]</c> markers use.</summary>
public sealed record ResearchModelSource(int N, string? Url, string? Title, string? Quote = null);

/// <summary>One <c>url_citation</c> annotation the web-search tool itself attached to the output.</summary>
public sealed record ResearchToolCitation(string? Url, string? Title);

/// <summary>The reconciled summary (markers renumbered) and its final source list, in marker order.</summary>
public sealed record ReconciledResearch(string SummaryMarkdown, IReadOnlyList<AiWebSource> Sources);

/// <summary>
/// F3-T02 — which source does <c>[n]</c> point at. The summary's markers were written against the
/// model's own <c>sources[]</c> list, so that list (ordered by <c>n</c>) is the only thing a marker
/// may be resolved against — never the position in the tool's annotations, which are deduplicated,
/// reordered and truncated. A source survives only if its normalised URL (<see cref="WebSourceUrl"/>)
/// is one the tool really cited, so a URL the model merely typed is not a source. The surviving
/// sources are renumbered 1..k and every marker in the summary is rewritten to match; a marker that
/// names no surviving source (an invented URL, an <c>n</c> given to two different URLs, an unknown
/// number) becomes <c>[0]</c>, which <c>WebGuard</c> rejects — the summary is then an honest abstain,
/// never a wrong attribution. <c>maxSources</c> trims only sources nothing cites: a cited source is
/// never cut before validation. Pure.
/// </summary>
public static class WebSourceReconciler
{
    /// <summary>The marker a summary carries when it names no surviving source.</summary>
    public const string UnresolvedMarker = "[0]";

    private static readonly Regex MarkerPattern = new(@"\[(\d+)\]", RegexOptions.Compiled);

    public static ReconciledResearch Reconcile(
        string? summaryMarkdown,
        IReadOnlyList<ResearchModelSource>? modelSources,
        IReadOnlyList<ResearchToolCitation>? toolCitations,
        int maxSources)
    {
        var summary = summaryMarkdown ?? string.Empty;
        var cap = Math.Max(1, maxSources);

        // What the tool really cited: normalised URL -> its annotation title (the first annotation wins; a blank title borrows the model's).
        var cited = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var citation in toolCitations ?? [])
        {
            var url = WebSourceUrl.Normalize(citation.Url);
            if (url is null)
            {
                continue;
            }

            cited.TryAdd(url, citation.Title);
        }

        var entries = (modelSources ?? [])
            .Select((source, index) => (source.N, Url: WebSourceUrl.Normalize(source.Url), source.Title, source.Quote, Index: index))
            .Where(e => e.Url is not null)
            .OrderBy(e => e.N)
            .ThenBy(e => e.Index)
            .ToList();

        // An n the model gave to two different URLs cannot be resolved: it names nothing.
        var ambiguous = entries
            .GroupBy(e => e.N)
            .Where(g => g.Select(e => e.Url).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        var numberToUrl = new Dictionary<int, string>();
        var candidates = new List<string>();
        var modelTitles = new Dictionary<string, string?>(StringComparer.Ordinal);
        var modelQuotes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var url = entry.Url!;
            if (ambiguous.Contains(entry.N) || !cited.ContainsKey(url))
            {
                continue;
            }

            numberToUrl.TryAdd(entry.N, url);
            if (!candidates.Contains(url, StringComparer.Ordinal))
            {
                candidates.Add(url);
            }

            if (!modelTitles.TryGetValue(url, out var title) || string.IsNullOrWhiteSpace(title))
            {
                modelTitles[url] = entry.Title;
            }

            // F3-T01: the same page listed under two numbers keeps both passages (deduplicated).
            if (CleanQuote(entry.Quote) is { Length: > 0 } quote)
            {
                if (!modelQuotes.TryGetValue(url, out var quotes))
                {
                    quotes = [];
                    modelQuotes[url] = quotes;
                }

                if (!quotes.Contains(quote, StringComparer.Ordinal))
                {
                    quotes.Add(quote);
                }
            }
        }

        if (candidates.Count == 0)
        {
            // The model listed nothing the tool cited. With no marker in the text there is nothing to
            // misattribute, so the tool's own citations are shown as they came; with markers there is
            // no list to resolve them against and WebGuard abstains on the empty source list.
            if (MarkerPattern.IsMatch(summary))
            {
                return new ReconciledResearch(summary, []);
            }

            var fallback = cited
                .Take(cap)
                .Select(pair => new AiWebSource(pair.Key, Title(pair.Value, null, pair.Key), Snippet: string.Empty))
                .ToList();
            return new ReconciledResearch(summary, fallback);
        }

        var referenced = MarkerPattern.Matches(summary)
            .Select(m => TryNumber(m, out var n) && numberToUrl.TryGetValue(n, out var url) ? url : null)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        // Cited sources are never cut; the cap only trims what nothing cites.
        var selected = new HashSet<string>(referenced, StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (selected.Count >= cap)
            {
                break;
            }

            selected.Add(candidate);
        }

        var final = candidates.Where(selected.Contains).ToList();
        var newNumber = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var url in final)
        {
            newNumber[url] = final.IndexOf(url) + 1;
        }

        var rewritten = MarkerPattern.Replace(summary, match =>
        {
            return TryNumber(match, out var n)
                && numberToUrl.TryGetValue(n, out var url)
                && newNumber.TryGetValue(url, out var number)
                ? $"[{number.ToString(CultureInfo.InvariantCulture)}]"
                : UnresolvedMarker;
        });

        var sources = final
            .Select(url => new AiWebSource(
                url,
                Title(cited[url], modelTitles.GetValueOrDefault(url), url),
                Snippet: string.Empty,
                Quote: JoinQuotes(modelQuotes.GetValueOrDefault(url))))
            .ToList();

        return new ReconciledResearch(rewritten, sources);
    }

    /// <summary>The longest quote kept per source: a verbatim passage, not a copy of the page.</summary>
    public const int MaxQuoteChars = 800;

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Whitespace collapsed to single spaces and the text capped at <see cref="MaxQuoteChars"/>.
    /// Nothing else is changed: the quote must stay the characters the model copied.</summary>
    private static string CleanQuote(string? quote)
    {
        if (string.IsNullOrWhiteSpace(quote))
        {
            return string.Empty;
        }

        var collapsed = WhitespaceRun.Replace(quote.Trim(), " ");
        return collapsed.Length <= MaxQuoteChars ? collapsed : collapsed[..MaxQuoteChars];
    }

    private static string JoinQuotes(List<string>? quotes) =>
        quotes is null ? string.Empty : string.Join(" ", quotes);

    private static bool TryNumber(Match marker, out int number) =>
        int.TryParse(marker.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number);

    private static string Title(string? annotationTitle, string? modelTitle, string url) =>
        !string.IsNullOrWhiteSpace(annotationTitle) ? annotationTitle
        : !string.IsNullOrWhiteSpace(modelTitle) ? modelTitle
        : url;
}
