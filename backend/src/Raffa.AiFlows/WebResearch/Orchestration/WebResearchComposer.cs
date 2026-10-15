using System.Globalization;
using Microsoft.Extensions.Options;
using Raffa.AiFlows.Shared.Guards;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Reply;
using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.AiFlows.WebResearch.Guards;
using Raffa.AiFlows.WebResearch.Lexicons;
using Raffa.AiFlows.WebResearch.Pipeline;
using Raffa.AiFlows.WebResearch.Prompts;
using Raffa.AiFlows.WebResearch.Replies;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Reply;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.WebResearch.Orchestration;

/// <summary>
/// The only caller of <see cref="IAiGateway.ResearchAsync"/> and the only producer of
/// <see cref="PackCorpus.Web"/> items (ADR-030; a source-scan test in <c>Raffa.Chat.Tests</c>
/// holds both). Its input is deliberately three strings — the already-sanitised query, the purpose
/// and the language — so no context pack, evidence or tenant text can reach the web by
/// construction. On the way back the summary goes through <see cref="WebGuard"/> (sources and
/// markers) and <see cref="WebFigureGuard"/> (F3-T01: every figure shares its sentence with a marker and
/// appears in the verbatim quote of a source that sentence cites; a sentence stating a figure nothing
/// backs is removed, not the whole answer). The web pack is built from those same sources and its keys
/// are the only keys cited, so a further citation-key check would be a tautology and is not run; a
/// failure, or a summary left with no cited claim, is an honest abstain that names the sources found,
/// never a retry (each call is budgeted). Results are never indexed and never merged into an
/// <c>answer</c>-role pack.
/// </summary>
public sealed class WebResearchComposer(IAiGateway aiGateway, IOptionsMonitor<WebResearchOptions> optionsMonitor, IClock clock)
{
    /// <summary>A fixed options instance (unit tests, tools): never re-read.</summary>
    public WebResearchComposer(IAiGateway aiGateway, WebResearchOptions options, IClock clock)
        : this(aiGateway, WebResearchOptionsMonitor.Static(options), clock)
    {
    }

    public const string CitationKeyPrefix = WebCitationMapper.CitationKeyPrefix;

    private static readonly WebSourceNormalizer Normalizer = new();
    private static readonly WebFigureVerifier Verifier = new();
    private static readonly WebCitationMapper Mapper = new();

    public async Task<WebResearchOutcome> ComposeAsync(
        string query, string purpose, string language, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        var options = optionsMonitor.CurrentValue;
        var italian = string.Equals(language, "it", StringComparison.OrdinalIgnoreCase);
        var lang = NumericLocale.SupportedLanguage(language) ?? "en"; // one of the five supported (D6)

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

        // A-09: after the research call the flow is a short chain of pure stages. Source normaliser
        // (F3-T08: deny-list, tiers, tracking strip; a no-op by default), the composite figure verifier
        // that is the one gatekeeper of the verdict (F3-T05/T09), and the citation mapper.
        var normalized = Normalizer.Run(new WebSourceNormalizerInput(
            research.SummaryMarkdown, research.Sources, WebSourcePolicy.From(options)));
        var sources = normalized.Sources;

        var verdict = Verifier.Run(new WebVerificationInput(
            normalized.SummaryMarkdown, sources, lang, options.AllowReportedFigures));
        if (!verdict.Passed)
        {
            // F3-D05 (opt-in): the sources the search found stay clickable, built by the same mapper as an answer's.
            IReadOnlyList<ReplyCitation> found = options.CiteSourcesOnAbstain && sources.Count > 0
                ? Mapper.Run(new WebCitationMapperInput(sources, clock.UtcNow)).Citations
                : [];
            return Abstained(sources, found, provenanceBase, italian, verdict.Violation!);
        }

        // Every pack key is cited: the pack is made of exactly the sources the verifier just accepted
        // (non-empty, markers inside the list), so there is no key left for a grounding check to catch.
        var map = Mapper.Run(new WebCitationMapperInput(sources, clock.UtcNow));

        return new WebResearchOutcome(
            WebResearchOutcomeKind.Answered,
            verdict.Markdown,
            map.Citations,
            provenanceBase,
            SourceCount: sources.Count,
            GuardIntervened: verdict.SentencesRemoved > 0,
            GuardViolation: verdict.Violation,
            Error: null,
            FiguresVerified: verdict.FiguresVerified,
            FiguresReported: verdict.FiguresReported,
            SentencesRemoved: verdict.SentencesRemoved);
    }

    private static WebResearchOutcome Abstained(
        IReadOnlyList<AiWebSource> sources, IReadOnlyList<ReplyCitation> citations, ReplyProvenance provenance, bool italian, string violation)
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
            citations,
            provenance with { Sources = [] },
            SourceCount: sources.Count,
            GuardIntervened: true,
            GuardViolation: violation,
            Error: null);
    }
}
