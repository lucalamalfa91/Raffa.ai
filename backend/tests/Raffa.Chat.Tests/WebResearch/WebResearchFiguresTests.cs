using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.WebResearch;
using Raffa.Chat.Tests.TestSupport;
using Raffa.SharedKernel;

namespace Raffa.Chat.Tests.WebResearch;

/// <summary>
/// F3-T01 / F3-D02 — figures in a web answer are checked against the verbatim quote of the cited source.
/// The doubles here reproduce production: the hosted search tool gives a URL and a title and no page
/// text, so every <see cref="AiWebSource.Snippet"/> is empty and the only text is <see cref="AiWebSource.Quote"/>
/// (what <c>FoundryResearchClient</c> now asks the model for). Before this, every such answer with a
/// percentage or an amount was an abstain after the call was paid for, and a <c>$36</c> or <c>€12</c>
/// passed unchecked. The probe at the bottom runs 30 query shapes over the five languages: not one
/// abstain because of a figure with a verifiable source, not one symbol figure shown unverified.
/// </summary>
public sealed class WebResearchFiguresTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly AiCallMetadata Metadata = new("gpt-5.4-research-dev", "2026-03-17", WebResearchPrompt.Version, Now, "abc123");

    private static AiWebSource Production(string url, string title, string quote) =>
        new(url, title, Snippet: string.Empty, Quote: quote);

    private static WebResearchComposer Compose(IAiGateway gateway, bool allowReported = false) =>
        new(gateway, new WebResearchOptions { Enabled = true, AllowReportedFigures = allowReported }, new FixedClock(Now));

    private static ProductionShapedGateway Gateway(string summary, params AiWebSource[] sources) =>
        new(new AiResearchResult(summary, sources, OffTopic: false, Metadata));

    // ---- The bug, reproduced and fixed ---------------------------------------------------------------

    [Fact]
    public async Task F3_T01_a_percentage_and_a_date_with_a_quote_and_no_snippet_are_an_answer_not_an_abstain()
    {
        var gateway = Gateway(
            "Public, unverified information, not checked against your contracts. Salesforce raised list prices by 9% on 1 August 2025 [1].",
            Production("https://example.com/sf", "Salesforce pricing update", "Effective August 1, 2025, list prices rise by an average of 9% across editions."));

        var outcome = await Compose(gateway).ComposeAsync("salesforce price increase", "SupplierNews", "en");

        Assert.Equal(WebResearchOutcomeKind.Answered, outcome.Kind);
        Assert.False(outcome.GuardIntervened);
        Assert.Contains("9%", outcome.Markdown, StringComparison.Ordinal);
        Assert.Equal(2, outcome.FiguresVerified);
        Assert.All(gateway.Sources, source => Assert.Equal(string.Empty, source.Snippet));
    }

    [Fact]
    public async Task F3_T01_the_citation_shows_the_verbatim_quote_instead_of_the_title()
    {
        const string quote = "Effective August 1, 2025, list prices rise by an average of 9% across editions.";
        var gateway = Gateway(
            "Public, unverified information. Salesforce raised list prices by 9% [1].",
            Production("https://example.com/sf", "Salesforce pricing update", quote));

        var outcome = await Compose(gateway).ComposeAsync("salesforce price increase", "SupplierNews", "en");

        Assert.Equal(quote, Assert.Single(outcome.Citations).Snippet);
    }

    [Fact]
    public async Task F3_T01_a_symbol_price_the_quote_does_not_carry_is_cut_and_the_rest_of_the_answer_stays()
    {
        var gateway = Gateway(
            "Public, unverified information. The Pro plan costs $36 per user [1]. Annual billing usually earns a discount [1].",
            Production("https://example.com/pricing", "Pricing", "The Pro plan costs $63 per user per month. Annual billing earns a discount."));

        var outcome = await Compose(gateway).ComposeAsync("pro plan price", "BenchmarkRange", "en");

        Assert.Equal(WebResearchOutcomeKind.Answered, outcome.Kind);
        Assert.True(outcome.GuardIntervened);
        Assert.Equal(1, outcome.SentencesRemoved);
        Assert.DoesNotContain("$36", outcome.Markdown, StringComparison.Ordinal);
        Assert.Contains("Annual billing usually earns a discount [1].", outcome.Markdown, StringComparison.Ordinal);
        Assert.Contains("$36", outcome.GuardViolation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task F3_T01_when_every_cited_claim_states_a_figure_nothing_backs_the_answer_is_an_abstain_naming_the_hosts()
    {
        var gateway = Gateway(
            "Public, unverified information. The Pro plan costs $36 per user [1].",
            Production("https://example.com/pricing", "Pricing", "The Pro plan costs $63 per user per month."));

        var outcome = await Compose(gateway).ComposeAsync("pro plan price", "BenchmarkRange", "en");

        Assert.Equal(WebResearchOutcomeKind.Abstained, outcome.Kind);
        Assert.True(outcome.GuardIntervened);
        Assert.Contains("$36", outcome.GuardViolation, StringComparison.Ordinal);
        Assert.DoesNotContain("$36", outcome.Markdown, StringComparison.Ordinal);
        Assert.Contains("example.com", outcome.Markdown, StringComparison.Ordinal);
        Assert.Empty(outcome.Citations);
    }

    [Fact]
    public async Task F3_T01_a_figure_without_a_marker_is_not_shown_even_when_a_source_carries_it()
    {
        var gateway = Gateway(
            "Public, unverified information. Caps run 5-10%. Multi-year terms help [1].",
            Production("https://example.com/a", "A", "Caps run 5-10% in most renewals."));

        var outcome = await Compose(gateway).ComposeAsync("uplift cap", "MarketPractice", "en");

        Assert.Equal(WebResearchOutcomeKind.Answered, outcome.Kind);
        Assert.Equal(1, outcome.SentencesRemoved);
        Assert.DoesNotContain("5-10%", outcome.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task F3_D02_a_reported_figure_is_kept_only_when_the_workspace_options_allow_it()
    {
        const string summary = "Public, unverified information. Caps of 25% are common [1].";
        var source = Production("https://example.com/a", "A", string.Empty);

        var strict = await Compose(Gateway(summary, source), allowReported: false).ComposeAsync("uplift cap", "MarketPractice", "en");
        var lenient = await Compose(Gateway(summary, source), allowReported: true).ComposeAsync("uplift cap", "MarketPractice", "en");

        Assert.Equal(WebResearchOutcomeKind.Abstained, strict.Kind);
        Assert.Equal(WebResearchOutcomeKind.Answered, lenient.Kind);
        Assert.Equal(1, lenient.FiguresReported);
        Assert.Equal(0, lenient.FiguresVerified);
        Assert.False(lenient.GuardIntervened);
    }

    [Fact]
    public async Task F3_T01_the_fixture_gateway_now_returns_production_shaped_sources_and_still_answers()
    {
        var fixture = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now));

        var raw = await fixture.ResearchAsync(
            new AiResearchRequest("saas renewal uplift market practice", "MarketPractice", "en", 5, "p", "v"));
        var outcome = await Compose(fixture).ComposeAsync("saas renewal uplift market practice", "MarketPractice", "en");

        Assert.All(raw.Value.Sources, source =>
        {
            Assert.Equal(string.Empty, source.Snippet);
            Assert.NotEqual(string.Empty, source.Quote);
        });
        Assert.Equal(WebResearchOutcomeKind.Answered, outcome.Kind);
        Assert.Contains("5-10%", outcome.Markdown, StringComparison.Ordinal);
        Assert.False(outcome.GuardIntervened);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    [InlineData("it")]
    [InlineData("en")]
    public async Task The_five_languages_reach_the_research_request(string language)
    {
        var gateway = Gateway("Public, unverified information. Buyers trade term for price [1].", Production("https://example.com/a", "A", "Term for price."));

        await Compose(gateway).ComposeAsync("saas renewal", "MarketPractice", language);

        Assert.Equal(language, Assert.Single(gateway.Requests).Language);
    }

    [Fact]
    public async Task An_unsupported_language_asks_for_english()
    {
        var gateway = Gateway("Public, unverified information. Buyers trade term for price [1].", Production("https://example.com/a", "A", "Term for price."));

        await Compose(gateway).ComposeAsync("saas renewal", "MarketPractice", "pt");

        Assert.Equal("en", Assert.Single(gateway.Requests).Language);
    }

    // ---- The probe: 30 query shapes -------------------------------------------------------------------

    /// <param name="Name">What the case exercises.</param>
    /// <param name="Language">The answer language.</param>
    /// <param name="Summary">What the research role wrote.</param>
    /// <param name="Sources">The sources, snippet empty, quote as the model copied it.</param>
    /// <param name="Verifiable">True when every figure has a verifiable source: the answer must be shown whole.</param>
    /// <param name="MustNotAppear">Figures that have no verifiable source: they must never be in the reply.</param>
    public sealed record ProbeCase(
        string Name,
        string Language,
        string Summary,
        AiWebSource[] Sources,
        bool Verifiable,
        string[] MustNotAppear);

    private const string Lead = "Public, unverified information. ";

    private static AiWebSource Src(string quote, int n = 1) =>
        Production($"https://example.com/p{n}", $"Page {n}", quote);

    public static IEnumerable<ProbeCase> Probe()
    {
        // 22 answers whose every figure a quote backs: all must be shown, untouched.
        yield return new("en percent and long date", "en", Lead + "Prices rise by 9% from 1 August 2025 [1].", [Src("Effective August 1, 2025, prices rise by an average of 9%.")], true, []);
        yield return new("en dollar price", "en", Lead + "The Pro plan costs $36 per user per month [1].", [Src("Pro: $36 per user per month, billed annually.")], true, []);
        yield return new("en euro symbol price", "en", Lead + "Seats start at €12 each [1].", [Src("Seats start at 12 euros each.")], true, []);
        yield return new("it euro code with Italian grouping", "it", Lead + "Il canone annuo è di EUR 1.200,00 [1].", [Src("The annual fee is €1,200.00 per site.")], true, []);
        yield return new("it percent range", "it", Lead + "I tetti di aumento vanno dal 5-10% [1].", [Src("Uplift caps run 5-10% in most renewals.")], true, []);
        yield return new("it long date", "it", Lead + "Il rincaro scatta il 15 gennaio 2027 [1].", [Src("The increase takes effect on January 15, 2027.")], true, []);
        yield return new("fr euro with narrow no-break space", "fr", Lead + "Le tarif est de 1 200,00 EUR [1].", [Src("The fee is EUR 1,200.00.")], true, []);
        yield return new("fr percent and 1er date", "fr", Lead + "La hausse est de 7,5 % au 1er août 2025 [1].", [Src("A 7.5% increase applies on August 1, 2025.")], true, []);
        yield return new("es euros and long date", "es", Lead + "El precio es de 1.250 euros desde el 1 de agosto de 2025 [1].", [Src("The price is €1,250 from August 1, 2025.")], true, []);
        yield return new("de percent and dotted date", "de", Lead + "Die Erhöhung beträgt 12 Prozent ab dem 1. August 2025 [1].", [Src("A 12% increase applies from August 1, 2025.")], true, []);
        yield return new("en k shorthand", "en", Lead + "Typical deals reach USD 40k a year [1].", [Src("Typical deals reach USD 40,000 a year.")], true, []);
        yield return new("en M shorthand", "en", Lead + "The average contract is worth $1.5M [1].", [Src("The average contract is worth $1.5 million.")], true, []);
        yield return new("en US numeric date", "en", Lead + "The change applies from 8/1/2025 [1].", [Src("The change applies from August 1, 2025.")], true, []);
        yield return new("en month and year", "en", Lead + "The list price changed in August 2025 [1].", [Src("List prices changed in August 2025.")], true, []);
        yield return new("en spelled percent", "en", Lead + "Caps of ten percent are common [1].", [Src("Uplift caps of 10% are common.")], true, []);
        yield return new("it spelled percent", "it", Lead + "I tetti sono del venticinque per cento [1].", [Src("Caps are 25% in most deals.")], true, []);
        yield return new("en two sources one figure each", "en", Lead + "Caps run 5-10% [1] and notice is 60 days [2].", [Src("Caps run 5-10%."), Src("Notice is 60 days.", 2)], true, []);
        yield return new("en bullets with their own markers", "en", Lead + "\n- Caps run 5-10% [1].\n- The Pro plan costs $36 [2].", [Src("Caps run 5-10%."), Src("Pro costs $36 per user.", 2)], true, []);
        yield return new("en dollar range", "en", Lead + "Plans cost $10-15 per user [1].", [Src("Plans cost $10 to $15 per user.")], true, []);
        yield return new("de Mio shorthand", "de", Lead + "Der Durchschnitt liegt bei 2,5 Mio. EUR [1].", [Src("Der Durchschnitt liegt bei 2.500.000 EUR.")], true, []);
        yield return new("en no figures at all", "en", Lead + "Buyers often trade term for price [1].", [Src("Buyers trade term for price.")], true, []);
        yield return new("es percent with no-break space", "es", Lead + "El límite es del 5 % anual [1].", [Src("El límite es del 5% anual.")], true, []);

        // 8 answers with a figure no quote backs: the figure must not be shown.
        yield return new("en dollar price the quote does not carry", "en", Lead + "The Pro plan costs $36 per user [1]. Discounts exist [1].", [Src("Pro costs $63 per user. Discounts exist.")], false, ["$36"]);
        yield return new("en euro price with no quote", "en", Lead + "Seats start at €12 each [1]. Discounts exist [1].", [Src(string.Empty)], false, ["€12"]);
        yield return new("it ISO amount the quote does not carry", "it", Lead + "Il canone è di EUR 99 [1]. Gli sconti esistono [1].", [Src("Il canone è di EUR 90. Gli sconti esistono.")], false, ["EUR 99"]);
        yield return new("en percent without a marker", "en", Lead + "Caps run 40%. Terms vary by vendor [1].", [Src("Caps run 40% in rare cases. Terms vary by vendor.")], false, ["40%"]);
        yield return new("en word amount against another currency", "en", Lead + "Seats cost 36 dollars [1]. Terms vary [1].", [Src("Seats cost €36. Terms vary.")], false, ["36 dollars"]);
        yield return new("de date the quote does not carry", "de", Lead + "Die Erhöhung gilt ab 2. August 2025 [1]. Die Bedingungen variieren [1].", [Src("Effective August 1, 2025. Terms vary.")], false, ["2. August 2025"]);
        yield return new("fr amount in the wrong source", "fr", Lead + "Le tarif est de 500 EUR [1]. Les remises existent [2].", [Src("Les remises existent."), Src("Le tarif est de 500 EUR.", 2)], false, ["500 EUR"]);
        yield return new("en month and year the quote does not carry", "en", Lead + "Prices changed in September 2025 [1]. Terms vary [1].", [Src("Prices changed in August 2025. Terms vary.")], false, ["September 2025"]);
    }

    public static TheoryData<string> ProbeNames()
    {
        var data = new TheoryData<string>();
        foreach (var probe in Probe())
        {
            data.Add(probe.Name);
        }

        return data;
    }

    [Fact]
    public void The_probe_has_thirty_queries_across_the_five_languages()
    {
        var cases = Probe().ToList();

        Assert.Equal(30, cases.Count);
        Assert.Equal(["de", "en", "es", "fr", "it"], cases.Select(c => c.Language).Distinct().Order().ToArray());
        Assert.All(cases.SelectMany(c => c.Sources), source => Assert.Equal(string.Empty, source.Snippet));
    }

    [Theory]
    [MemberData(nameof(ProbeNames))]
    public async Task F3_T01_probe_no_abstain_from_a_verifiable_figure_and_no_unverified_figure_shown(string name)
    {
        var probe = Probe().Single(p => p.Name == name);
        var gateway = Gateway(probe.Summary, probe.Sources);

        var outcome = await Compose(gateway).ComposeAsync("probe query", "MarketPractice", probe.Language);

        if (probe.Verifiable)
        {
            Assert.Equal(WebResearchOutcomeKind.Answered, outcome.Kind);
            Assert.False(outcome.GuardIntervened);
            Assert.Equal(probe.Summary, outcome.Markdown);
            Assert.Equal(0, outcome.SentencesRemoved);
        }
        else
        {
            Assert.Equal(WebResearchOutcomeKind.Answered, outcome.Kind);
            Assert.True(outcome.GuardIntervened);
            Assert.True(outcome.SentencesRemoved >= 1);
        }

        foreach (var figure in probe.MustNotAppear)
        {
            Assert.DoesNotContain(figure, outcome.Markdown, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task F3_T01_probe_totals_zero_abstains_from_verifiable_figures_and_zero_unverified_symbol_figures_shown()
    {
        var abstainedVerifiable = 0;
        var unverifiedShown = 0;
        foreach (var probe in Probe())
        {
            var outcome = await Compose(Gateway(probe.Summary, probe.Sources)).ComposeAsync("probe query", "MarketPractice", probe.Language);

            if (probe.Verifiable && outcome.Kind != WebResearchOutcomeKind.Answered)
            {
                abstainedVerifiable++;
            }

            unverifiedShown += probe.MustNotAppear.Count(figure => outcome.Markdown.Contains(figure, StringComparison.Ordinal));
        }

        Assert.Equal(0, abstainedVerifiable);
        Assert.Equal(0, unverifiedShown);
    }

    // ---- Doubles -----------------------------------------------------------------------------------------

    private sealed class ProductionShapedGateway(AiResearchResult result) : IAiGateway
    {
        public List<AiResearchRequest> Requests { get; } = [];

        public IReadOnlyList<AiWebSource> Sources => result.Sources;

        public Task<Result<AiResearchResult>> ResearchAsync(AiResearchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result<AiResearchResult>.Success(result));
        }

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The research composer must never call the answer role.");

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
