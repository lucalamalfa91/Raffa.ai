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
/// markers), <see cref="NumericGuard"/> (every figure verbatim in a cited snippet) and
/// <see cref="GroundingGuard"/> (every citation key in the web pack); a failure is an honest
/// abstain that names the sources found, never a retry (each call is budgeted). Results are never
/// indexed and never merged into an <c>answer</c>-role pack.
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
        var lang = italian ? "it" : "en";

        var request = new AiResearchRequest(
            query.Trim(), purpose, lang, options.EffectiveMaxSources, WebResearchPrompt.SystemPrompt, WebResearchPrompt.Version);

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
                Error: result.Error);
        }

        var research = result.Value;
        var provenanceBase = new ReplyProvenance(
            [PackCorpus.Web], research.Metadata.ModelId, research.Metadata.PromptVersion, research.Metadata.InputHash, Unverified: true);

        if (research.OffTopic)
        {
            return new WebResearchOutcome(
                WebResearchOutcomeKind.Refused,
                italian
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

        var numericVerdict = NumericGuard.Validate(research.SummaryMarkdown, pack);
        if (!numericVerdict.Passed)
        {
            return Abstained(sources, provenanceBase, italian, numericVerdict.Violation!);
        }

        var answer = new AiAnswerResult(
            CanDetermine: true,
            Answer: research.SummaryMarkdown,
            Citations: [],
            Metadata: research.Metadata,
            AnswerMarkdown: research.SummaryMarkdown,
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
            research.SummaryMarkdown,
            citations,
            provenanceBase,
            SourceCount: sources.Count,
            GuardIntervened: false,
            GuardViolation: null,
            Error: null);
    }

    /// <summary>One <see cref="PackCorpus.Web"/> item per source, keyed <c>web:n</c> in the tool's
    /// own order so the summary's <c>[n]</c> markers resolve positionally.</summary>
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
                Snippet: string.IsNullOrWhiteSpace(source.Snippet) ? title : source.Snippet,
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
        var named = hosts.Count == 0 ? (italian ? "nessuna" : "none") : string.Join(", ", hosts);

        return new WebResearchOutcome(
            WebResearchOutcomeKind.Abstained,
            italian
                ? $"Ho cercato sul web pubblico, ma la sintesi non ha superato i controlli di Raffa e non la mostro. Fonti trovate: {named}."
                : $"I searched the public web, but the summary did not pass Raffa's grounding checks, so I am not showing it. Sources found: {named}.",
            [],
            provenance with { Sources = [] },
            SourceCount: sources.Count,
            GuardIntervened: true,
            GuardViolation: violation,
            Error: null);
    }
}
