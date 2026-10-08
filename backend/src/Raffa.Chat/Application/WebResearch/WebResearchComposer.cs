using System.Globalization;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Reply;
using Raffa.SharedKernel;

namespace Raffa.Chat.Application.WebResearch;

/// <summary>
/// The only caller of <see cref="IAiGateway.ResearchAsync"/> and the only producer of
/// <see cref="PackCorpus.Web"/> items (ADR-030; a source-scan test in <c>Raffa.Chat.Tests</c>
/// holds both). Its input is deliberately three strings — the already-sanitised query, the purpose
/// and the language — so no context pack, evidence or tenant text can reach the web by
/// construction. On the way back the summary goes through <see cref="WebGuard"/> (sources and
/// markers), <see cref="WebFigureGuard"/> (F3-T01: every figure shares its sentence with a marker and
/// appears in the verbatim quote of a source that sentence cites; a sentence stating a figure nothing
/// backs is removed, not the whole answer) and <see cref="GroundingGuard"/> (every citation key in the
/// web pack); a failure, or a summary left with no cited claim, is an honest abstain that names the
/// sources found, never a retry (each call is budgeted). Results are never indexed and never merged
/// into an <c>answer</c>-role pack.
/// </summary>
public sealed class WebResearchComposer(IAiGateway aiGateway, WebResearchOptions options, IClock clock)
{
    public const string CitationKeyPrefix = "web:";

    public async Task<WebResearchOutcome> ComposeAsync(
        string query, string purpose, string language, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        var italian = string.Equals(language, "it", StringComparison.OrdinalIgnoreCase);
        var lang = SupportedLanguage(language);

        // ADR-032: a web-mode turn (the composer toggle) runs the open persona; every other
        // purpose is one of the four procurement purposes of ADR-030.
        var open = string.Equals(purpose, WebModeLexicon.Purpose, StringComparison.Ordinal);
        var request = new AiResearchRequest(
            query.Trim(),
            purpose,
            lang,
            options.EffectiveMaxSources,
            open ? WebResearchPrompt.OpenSystemPrompt : WebResearchPrompt.SystemPrompt,
            open ? WebResearchPrompt.OpenVersion : WebResearchPrompt.Version);

        var result = await aiGateway.ResearchAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return new WebResearchOutcome(
                WebResearchOutcomeKind.Failed,
                italian
                    ? "La ricerca sul web non è disponibile in questo momento. Posso continuare solo con i tuoi contratti."
                    : "Web research is not available right now. I can continue with your contracts only.",
                [],
                ReplyProvenance.NoModelCall([]),
                SourceCount: 0,
                GuardIntervened: false,
                GuardViolation: null,
                Error: result.Error,
                ReleaseBudget: AiGatewayErrors.ResearchFailureReleasesBudget(result.Error));
        }

        var research = result.Value;
        var provenanceBase = new ReplyProvenance(
            [PackCorpus.Web], research.Metadata.ModelId, research.Metadata.PromptVersion, research.Metadata.InputHash, Unverified: true);

        if (research.OffTopic)
        {
            return new WebResearchOutcome(
                WebResearchOutcomeKind.Refused,
                open
                    ? WebModeReplyBuilder.OffContextMarkdown(italian)
                    : italian
                        ? "L'agente di ricerca web copre solo temi procurement (pratiche di mercato, notizie sui fornitori, range pubblici, tattiche di negoziazione) e ha rifiutato questa ricerca."
                        : "The web research agent only covers procurement topics (market practice, supplier news, public benchmark ranges, negotiation tactics) and declined this search.",
                [],
                provenanceBase with { Sources = [] },
                SourceCount: 0,
                GuardIntervened: false,
                GuardViolation: null,
                Error: null);
        }

        var sources = research.Sources;
        var pack = BuildWebPack(sources);

        var webVerdict = WebGuard.Validate(research.SummaryMarkdown, sources);
        if (!webVerdict.Passed)
        {
            return Abstained(sources, provenanceBase, italian, webVerdict.Violation!);
        }

        // F3-T01: the figures are judged one sentence at a time against the quotes of the sources that
        // sentence cites. A sentence stating a figure nothing backs is dropped; if that leaves no
        // sentence a source stands behind, the answer is an abstain.
        var figures = WebFigureGuard.Verify(research.SummaryMarkdown, sources, lang, options.AllowReportedFigures);
        if (figures.SentencesRemoved > 0 && (!figures.HasCitedClaim || string.IsNullOrWhiteSpace(figures.Markdown)))
        {
            return Abstained(sources, provenanceBase, italian, figures.FirstRemovalReason ?? "no figure of the summary could be verified.");
        }

        var summary = figures.Markdown;
        var answer = new AiAnswerResult(
            CanDetermine: true,
            Answer: summary,
            Citations: [],
            Metadata: research.Metadata,
            AnswerMarkdown: summary,
            CitationKeys: pack.Select(item => item.CitationKey).ToList(),
            ActionKeys: [],
            AbstainReason: null,
            FollowUps: []);

        var groundingVerdict = GroundingGuard.Validate(answer, pack);
        if (!groundingVerdict.Passed)
        {
            return Abstained(sources, provenanceBase, italian, groundingVerdict.Violation!);
        }

        var citations = CopilotReplyBuilder.BuildCitations(answer.CitationKeys!, pack);

        return new WebResearchOutcome(
            WebResearchOutcomeKind.Answered,
            summary,
            citations,
            provenanceBase,
            SourceCount: sources.Count,
            GuardIntervened: figures.SentencesRemoved > 0,
            GuardViolation: figures.SentencesRemoved > 0 ? figures.FirstRemovalReason : null,
            Error: null,
            FiguresVerified: figures.Verified,
            FiguresReported: figures.Reported,
            SentencesRemoved: figures.SentencesRemoved);
    }

    /// <summary>The request's language: one of the five supported (D6), English for anything else.</summary>
    private static string SupportedLanguage(string? language)
    {
        var tag = language?.Trim() ?? string.Empty;
        var cut = tag.IndexOfAny(['-', '_']);
        if (cut > 0)
        {
            tag = tag[..cut];
        }

        return NumericLocale.SupportedLanguages.FirstOrDefault(l => string.Equals(l, tag, StringComparison.OrdinalIgnoreCase)) ?? "en";
    }

    /// <summary>One <see cref="PackCorpus.Web"/> item per source, keyed <c>web:n</c> in list order.
    /// The research gateway has already reconciled the list with the summary's markers (F3-T02:
    /// sources resolved by URL, renumbered 1..k, <c>[n]</c> rewritten to match), so position
    /// <c>n</c> is the source marker <c>[n]</c> means.</summary>
    private IReadOnlyList<PackItem> BuildWebPack(IReadOnlyList<AiWebSource> sources)
    {
        var fetched = clock.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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

        return pack;
    }

    private static WebResearchOutcome Abstained(
        IReadOnlyList<AiWebSource> sources, ReplyProvenance provenance, bool italian, string violation)
    {
        var hosts = sources
            .Select(s => Uri.TryCreate(s.Url, UriKind.Absolute, out var uri) ? uri.Host : s.Url)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        // Never a bare "not showing it": the sources found, then the two ways to a usable answer.
        var lead = hosts.Count == 0
            ? (italian ? "Il web pubblico non offre ancora fonti chiare su questo tema." : "The public web has no clear sources on this topic yet.")
            : (italian
                ? $"Ho trovato queste fonti pubbliche sul tema: {string.Join(", ", hosts)}."
                : $"I found these public sources on the topic: {string.Join(", ", hosts)}.");

        return new WebResearchOutcome(
            WebResearchOutcomeKind.Abstained,
            lead + (italian
                ? " Per un confronto su cui puoi contare partiamo dai tuoi contratti: dimmi il fornitore, oppure restringi la ricerca a un prodotto, un servizio o un Paese e la rilancio."
                : " For a comparison you can rely on, let's start from your contracts: name the supplier, or narrow the search to a product, a service or a country and I'll run it again."),
            [],
            provenance with { Sources = [] },
            SourceCount: sources.Count,
            GuardIntervened: true,
            GuardViolation: violation,
            Error: null);
    }
}
