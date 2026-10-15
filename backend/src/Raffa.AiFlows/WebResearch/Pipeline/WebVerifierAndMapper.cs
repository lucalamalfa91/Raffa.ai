using System.Globalization;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Reply;
using Raffa.AiFlows.WebResearch.Guards;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Reply;

namespace Raffa.AiFlows.WebResearch.Pipeline;

public sealed record WebVerificationInput(
    string SummaryMarkdown, IReadOnlyList<AiWebSource> Sources, string Language, bool AllowReportedFigures);

/// <summary>The one verdict on a web summary (F3-T05): shown (<see cref="Passed"/>, with the possibly
/// shortened <see cref="Markdown"/>) or an abstain with the diagnostic <see cref="Violation"/>.</summary>
public sealed record WebVerdict(
    bool Passed,
    string Markdown,
    string? Violation,
    int FiguresVerified,
    int FiguresReported,
    int SentencesRemoved)
{
    /// <summary>A guard intervened without sinking the answer: sentences were cut.</summary>
    public bool Trimmed => Passed && SentencesRemoved > 0;

    public static WebVerdict Abstain(string violation, int sentencesRemoved = 0) =>
        new(false, string.Empty, violation, 0, 0, sentencesRemoved);
}

/// <summary>
/// F3-T09 / F3-T05 — the composite guard and the single gatekeeper of the verdict: <see cref="WebGuard"/>
/// (sources, markers, URLs) then <see cref="WebFigureGuard"/> (every figure in a cited, verbatim quote),
/// folded into one <see cref="WebVerdict"/> that the composer reads and nothing else decides. Same
/// rules, same order and same messages as the composer applied inline before: a failed source check
/// abstains; a sentence stating an unbacked figure is cut; if that leaves no cited claim the answer
/// is an abstain.
/// </summary>
public sealed class WebFigureVerifier : IWebStage<WebVerificationInput, WebVerdict>
{
    public string Name => "web-figure-verifier";

    public WebVerdict Run(WebVerificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var webVerdict = WebGuard.Validate(input.SummaryMarkdown, input.Sources);
        if (!webVerdict.Passed)
        {
            return WebVerdict.Abstain(webVerdict.Violation!);
        }

        var figures = WebFigureGuard.Verify(input.SummaryMarkdown, input.Sources, input.Language, input.AllowReportedFigures);
        if (figures.SentencesRemoved > 0 && (!figures.HasCitedClaim || string.IsNullOrWhiteSpace(figures.Markdown)))
        {
            return WebVerdict.Abstain(
                figures.FirstRemovalReason ?? "no figure of the summary could be verified.", figures.SentencesRemoved);
        }

        return new WebVerdict(
            true, figures.Markdown, figures.SentencesRemoved > 0 ? figures.FirstRemovalReason : null,
            figures.Verified, figures.Reported, figures.SentencesRemoved);
    }
}

public sealed record WebCitationMapperInput(IReadOnlyList<AiWebSource> Sources, DateTimeOffset FetchedAt);

/// <param name="Pack">One <see cref="PackCorpus.Web"/> item per source, keyed <c>web:n</c>.</param>
/// <param name="Citations">One citation per pack item, in marker order.</param>
public sealed record WebCitationMap(IReadOnlyList<PackItem> Pack, IReadOnlyList<ReplyCitation> Citations);

/// <summary>
/// The citation mapper: the pack and citations of a web answer, built from the reconciled source list
/// (F3-T02: position <c>n</c> is the source marker <c>[n]</c> means). Moved out of the composer unchanged.
/// </summary>
public sealed class WebCitationMapper : IWebStage<WebCitationMapperInput, WebCitationMap>
{
    public const string CitationKeyPrefix = "web:";

    public string Name => "web-citation-mapper";

    public WebCitationMap Run(WebCitationMapperInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sources = input.Sources;
        var fetched = input.FetchedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var pack = new List<PackItem>(sources.Count);

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var host = Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) ? uri.Host : "web";
            var title = string.IsNullOrWhiteSpace(source.Title) ? source.Url : source.Title.Trim();

            pack.Add(new PackItem(
                CitationKey: CitationKeyPrefix + (i + 1).ToString(CultureInfo.InvariantCulture),
                Corpus: PackCorpus.Web,
                Title: $"{host} · {title}",
                Subtitle: source.PublishedAt is { } published
                    ? published.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null,
                Page: null,
                Section: null,
                // F3-T01: the passage the figures were checked against (production gives no snippet),
                // then the provider's snippet, then the title.
                Snippet: !string.IsNullOrWhiteSpace(source.Quote) ? source.Quote
                    : !string.IsNullOrWhiteSpace(source.Snippet) ? source.Snippet
                    : title,
                Href: source.Url,
                PreviewUrl: null,
                RecordId: null,
                Provenance: $"public web · unverified · fetched {fetched}",
                Values: []));
        }

        var citations = CopilotReplyBuilder.BuildCitations(pack.Select(item => item.CitationKey).ToList(), pack);
        return new WebCitationMap(pack, citations);
    }
}
