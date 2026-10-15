using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.AiFlows.WebResearch.Orchestration;
using Raffa.AiFlows.WebResearch.Pipeline;
using Raffa.AiFlows.WebResearch.Query;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Reply;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.WebResearch.Orchestration;

/// <summary>
/// The opt-in refinements of the web flow, all off by default: an abstain that keeps the sources it
/// found as citations (F3-D05), a query without the identifiers the amount filter misses (F3-D10), and
/// the vendor master as tier-1 domains of the source policy (F3-D12).
/// </summary>
public sealed class WebResearchOptInRefinementsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly AiCallMetadata Metadata = new("gpt-5.4-research-dev", "2026-03-17", "research-v1", Now, "abc123");

    private static readonly AiWebSource[] Sources =
    [
        new("https://example.com/procurement/saas-renewals", "SaaS renewal benchmarks", "Typical renewals close with a 5-10% uplift cap and 60 to 90 days of notice."),
        new("https://example.org/negotiation/levers", "Levers", "Multi-year commitments are the lever buyers cite most."),
    ];

    // A figure no cited quote carries: the composer abstains.
    private static AiResearchResult Rejected() => new("Uplift caps of 25% are common [1].", Sources, OffTopic: false, Metadata);

    private static WebResearchComposer Compose(WebResearchOptions options) =>
        new(new FixedResearchGateway(Rejected()), options, new FixedClock(Now));

    // ---------------------------------------------------------------- F3-D05

    [Fact]
    public async Task An_abstain_carries_no_citation_by_default()
    {
        var outcome = await Compose(new WebResearchOptions { Enabled = true }).ComposeAsync("saas renewal uplift", "BenchmarkRange", "en");

        Assert.Equal(WebResearchOutcomeKind.Abstained, outcome.Kind);
        Assert.Empty(outcome.Citations);
    }

    [Fact]
    public async Task With_the_option_on_an_abstain_keeps_the_found_sources_as_clickable_citations_and_not_the_rejected_text()
    {
        var outcome = await Compose(new WebResearchOptions { Enabled = true, CiteSourcesOnAbstain = true })
            .ComposeAsync("saas renewal uplift", "BenchmarkRange", "en");

        Assert.Equal(WebResearchOutcomeKind.Abstained, outcome.Kind);
        Assert.Equal(ReplyKind.Abstain, outcome.AsReplyKind);
        Assert.True(outcome.GuardIntervened);
        Assert.Equal(
            ["https://example.com/procurement/saas-renewals", "https://example.org/negotiation/levers"],
            outcome.Citations.Select(c => c.Href));
        Assert.All(outcome.Citations, c => Assert.Equal(PackCorpus.Web, c.Corpus));
        Assert.DoesNotContain("25%", outcome.Markdown, StringComparison.Ordinal);
        Assert.Empty(outcome.Provenance.Sources);
    }

    [Fact]
    public async Task With_the_option_on_an_abstain_without_sources_has_no_citation()
    {
        var gateway = new FixedResearchGateway(new AiResearchResult("Nothing to say [1].", [], OffTopic: false, Metadata));
        var composer = new WebResearchComposer(gateway, new WebResearchOptions { Enabled = true, CiteSourcesOnAbstain = true }, new FixedClock(Now));

        var outcome = await composer.ComposeAsync("saas renewal uplift", "BenchmarkRange", "en");

        Assert.Equal(WebResearchOutcomeKind.Abstained, outcome.Kind);
        Assert.Empty(outcome.Citations);
    }

    // ---------------------------------------------------------------- F3-D10

    [Theory]
    [InlineData("Salesforce renewal uplift IBAN IT60X0542811101000000123456 market practice", "IT60X05")]
    [InlineData("Salesforce renewal uplift IT12345678901 market practice", "12345678901")]
    [InlineData("Salesforce renewal uplift P.IVA 01234567890 market practice", "01234567890")]
    [InlineData("Salesforce renewal uplift partita IVA: IT 01234567890 market practice", "01234567890")]
    [InlineData("Salesforce renewal uplift RSSMRA80A01H501U market practice", "RSSMRA80A01H501U")]
    [InlineData("Salesforce renewal uplift call +39 02 1234 5678 market practice", "1234")]
    [InlineData("Salesforce renewal uplift tel 02 1234 5678 market practice", "1234")]
    public void With_the_option_on_identifiers_leave_the_query(string question, string fragment)
    {
        var query = WebQuerySanitizer.Sanitize(question, 300, redactSensitivePatterns: true);

        Assert.NotNull(query);
        Assert.DoesNotContain(fragment, query, StringComparison.Ordinal);
        Assert.Contains("Salesforce renewal uplift", query, StringComparison.Ordinal);
        Assert.Contains("market practice", query, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_option_the_query_is_the_one_it_always_was()
    {
        const string question = "Salesforce renewal uplift IBAN IT60X0542811101000000123456 market practice";

        Assert.Equal(WebQuerySanitizer.Sanitize(question, 300), WebQuerySanitizer.Sanitize(question, 300, redactSensitivePatterns: false));
        Assert.Contains("IT60X0542811101000000123456", WebQuerySanitizer.Sanitize(question, 300), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("What do buyers usually get on a Salesforce renewal in Italy")]
    [InlineData("Qual è la pratica di mercato per il rinnovo di SAP in Germania")]
    [InlineData("Is it common to ask for a 3 year price cap with Microsoft")]
    public void With_the_option_on_an_ordinary_question_is_left_alone(string question) =>
        Assert.Equal(WebQuerySanitizer.Sanitize(question, 300), WebQuerySanitizer.Sanitize(question, 300, redactSensitivePatterns: true));

    // ---------------------------------------------------------------- F3-D12

    [Fact]
    public void The_vendor_master_finds_a_vendor_by_whole_word_alias()
    {
        Assert.Equal(["Salesforce", "Atlassian"], WebVendorMaster.FindByText("Salesforce vs JIRA pricing").Select(v => v.Name));
        Assert.Empty(WebVendorMaster.FindByText("sapere se rinnovare"));
        Assert.Empty(WebVendorMaster.FindByText("  "));
        Assert.Empty(WebVendorMaster.FindByText(null));
    }

    [Fact]
    public void Every_vendor_has_an_alias_and_official_domains_and_no_domain_is_listed_twice()
    {
        Assert.All(WebVendorMaster.All, v =>
        {
            Assert.NotEmpty(v.Aliases);
            Assert.NotEmpty(v.OfficialDomains);
            Assert.All(v.Aliases, a => Assert.Equal(a.ToLowerInvariant(), a));
        });
        var domains = WebVendorMaster.All.SelectMany(v => v.OfficialDomains).ToList();
        Assert.Equal(domains.Count, domains.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void The_master_ranks_official_domains_first_but_never_over_a_configured_tier()
    {
        var tiers = WebVendorMaster.WithOfficialDomains(new Dictionary<string, int> { ["salesforce.com"] = 2, ["gartner.com"] = 1 });

        Assert.Equal(2, tiers["salesforce.com"]);
        Assert.Equal(1, tiers["gartner.com"]);
        Assert.Equal(1, tiers["oracle.com"]);
    }

    [Fact]
    public void The_policy_reads_the_master_only_when_the_option_is_on()
    {
        var off = WebSourcePolicy.From(new WebResearchOptions { MaxSourceTier = 2 });
        var on = WebSourcePolicy.From(new WebResearchOptions { MaxSourceTier = 2, VendorMasterAsTier1 = true });

        Assert.Empty(off.DomainTiers);
        Assert.Equal(1, WebSourceNormalizer.TierOf("www.oracle.com", on));
        Assert.Equal(WebSourcePolicy.UnlistedTier, WebSourceNormalizer.TierOf("www.oracle.com", off));
    }

    [Fact]
    public void With_a_tier_cap_a_vendors_own_site_survives_and_an_unlisted_one_is_dropped()
    {
        var policy = WebSourcePolicy.From(new WebResearchOptions { MaxSourceTier = 1, VendorMasterAsTier1 = true });

        Assert.False(WebSourceNormalizer.IsExcluded("https://www.oracle.com/news/price-list", policy));
        Assert.True(WebSourceNormalizer.IsExcluded("https://blog.example.com/oracle", policy));
    }

    private sealed class FixedResearchGateway(AiResearchResult result) : IAiGateway
    {
        public Task<Result<AiResearchResult>> ResearchAsync(AiResearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<AiResearchResult>.Success(result));

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
