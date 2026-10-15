using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.AiFlows.WebResearch.Orchestration;
using Raffa.AiFlows.WebResearch.Pipeline;
using Raffa.AiFlows.WebResearch.Ports;
using Raffa.Chat.Application.WebResearch;
using Raffa.AiGateway.Contracts;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.WebResearch.Pipeline;

/// <summary>A-09: the post-research stages (F3-T08 normaliser, F3-T09/T05 verifier and gatekeeper, citation
/// mapper) and the gatekeeper's gate order for both entries.</summary>
public sealed class WebPipelineStageTests
{
    private static readonly AiWebSource[] Sources =
    [
        new("https://www.example.com/a?utm_source=x&id=7", "A", "", Quote: "Caps of 5-10% are common."),
        new("https://www.example.com/a?id=7&utm_medium=y", "A again", "", Quote: "Caps of 5-10% are common."),
        new("https://blog.bad.test/post", "B", "", Quote: "Anything."),
        new("https://gov.example.org/c", "C", "", Quote: "Notice is 60 days."),
    ];

    private const string Summary = "Caps of 5-10% are common [1][2]. Blogs disagree [3]. Notice is 60 days [4].";

    [Fact]
    public void The_default_policy_returns_the_input_untouched()
    {
        var output = new WebSourceNormalizer().Run(new WebSourceNormalizerInput(Summary, Sources, WebSourcePolicy.None));

        Assert.Same(Sources, output.Sources);
        Assert.Equal(Summary, output.SummaryMarkdown);
        Assert.Equal((0, 0), (output.Dropped, output.Merged));
    }

    [Fact]
    public void Stripping_tracking_parameters_merges_duplicates_and_renumbers_the_markers()
    {
        var policy = new WebSourcePolicy([], true, new Dictionary<string, int>(), 0);

        var output = new WebSourceNormalizer().Run(new WebSourceNormalizerInput(Summary, Sources, policy));

        Assert.Equal(3, output.Sources.Count);
        Assert.Equal("https://www.example.com/a?id=7", output.Sources[0].Url);
        Assert.Equal(1, output.Merged);
        Assert.Equal("Caps of 5-10% are common [1][1]. Blogs disagree [2]. Notice is 60 days [3].", output.SummaryMarkdown);
    }

    [Fact]
    public void A_denied_domain_and_its_subdomains_are_dropped_and_their_markers_removed()
    {
        var policy = new WebSourcePolicy(["bad.test"], false, new Dictionary<string, int>(), 0);

        var output = new WebSourceNormalizer().Run(new WebSourceNormalizerInput(Summary, Sources, policy));

        Assert.Equal(3, output.Sources.Count);
        Assert.DoesNotContain(output.Sources, s => s.Url.Contains("bad.test", StringComparison.Ordinal));
        Assert.Equal(1, output.Dropped);
        Assert.Equal("Caps of 5-10% are common [1][2]. Blogs disagree. Notice is 60 days [3].", output.SummaryMarkdown);
    }

    [Fact]
    public void The_tier_policy_keeps_listed_domains_at_or_under_the_tier_and_drops_unlisted_ones()
    {
        var tiers = new Dictionary<string, int> { ["example.org"] = 1, ["example.com"] = 2 };
        var policy = new WebSourcePolicy([], false, tiers, MaxSourceTier: 2);

        var output = new WebSourceNormalizer().Run(new WebSourceNormalizerInput(Summary, Sources, policy));

        Assert.Equal(["https://www.example.com/a?utm_source=x&id=7", "https://www.example.com/a?id=7&utm_medium=y", "https://gov.example.org/c"],
            output.Sources.Select(s => s.Url));
        Assert.Equal(1, output.Dropped);
    }

    [Fact]
    public void The_verifier_passes_a_cited_verified_figure_and_abstains_on_a_summary_without_sources()
    {
        var verifier = new WebFigureVerifier();
        var good = verifier.Run(new WebVerificationInput("Caps of 5-10% are common [1].", Sources[..1], "en", false));
        var none = verifier.Run(new WebVerificationInput("Caps of 5-10% are common.", [], "en", false));

        Assert.True(good.Passed);
        Assert.Equal(0, good.SentencesRemoved);
        Assert.False(none.Passed);
        Assert.NotNull(none.Violation);
    }

    [Fact]
    public void The_verifier_cuts_a_sentence_with_an_unbacked_figure_and_abstains_when_nothing_cited_is_left()
    {
        var verifier = new WebFigureVerifier();
        var cut = verifier.Run(new WebVerificationInput("Caps of 5-10% are common [1]. Fees are 99% higher [1].", Sources[..1], "en", false));
        var all = verifier.Run(new WebVerificationInput("Fees are 99% higher [1].", Sources[..1], "en", false));

        Assert.True(cut.Passed);
        Assert.True(cut.Trimmed);
        Assert.DoesNotContain("99%", cut.Markdown, StringComparison.Ordinal);
        Assert.False(all.Passed);
        Assert.Equal(1, all.SentencesRemoved);
    }

    [Fact]
    public void The_mapper_keys_the_sources_in_order_and_cites_the_quote()
    {
        var map = new WebCitationMapper().Run(new WebCitationMapperInput(Sources[2..], new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(["web:1", "web:2"], map.Pack.Select(p => p.CitationKey));
        Assert.All(map.Pack, p => Assert.Equal(PackCorpus.Web, p.Corpus));
        Assert.Equal("Anything.", map.Pack[0].Snippet);
        Assert.Equal(2, map.Citations.Count);
        Assert.Contains("fetched 2026-09-22", map.Pack[0].Provenance, StringComparison.Ordinal);
    }

    private sealed class Policy(bool enabled) : IWorkspaceWebResearchPolicy
    {
        public int Calls { get; private set; }

        public Task<bool> IsEnabledAsync(TenantId tenantId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(enabled);
        }
    }

    private sealed class Budget(bool remaining) : IWebResearchBudget
    {
        public Task<bool> HasRemainingAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult(remaining);

        public Task<WebResearchReservation?> TryReserveAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WebResearchReservation?>(null);

        public Task ReleaseAsync(WebResearchReservation reservation, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static WebGatekeeper Gatekeeper(WebResearchOptions options, bool workspace = true, bool budget = true) =>
        new(WebResearchOptionsMonitor.Static(options), new Policy(workspace), new Budget(budget));

    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private const string MarketQuestion = "what uplift cap is common on saas renewals market practice";

    [Fact]
    public async Task The_kill_switch_closes_both_entries_first_and_follows_the_live_options()
    {
        var options = new WebResearchOptions { Enabled = false };
        var gatekeeper = Gatekeeper(options);

        Assert.Equal(WebGate.KillSwitch, (await gatekeeper.CheckConsentedAsync(Tenant, MarketQuestion, default)).Gate);
        Assert.Equal(WebGate.KillSwitch, (await gatekeeper.CheckToggleAsync(Tenant, MarketQuestion, default)).Gate);
        Assert.Equal(WebGate.KillSwitch, await gatekeeper.RecheckBeforeRunAsync(Tenant, default));

        options.Enabled = true;

        Assert.Equal(WebGate.Open, (await gatekeeper.CheckToggleAsync(Tenant, MarketQuestion, default)).Gate);
        Assert.Equal(WebGate.Open, await gatekeeper.RecheckBeforeRunAsync(Tenant, default));
    }

    [Fact]
    public async Task The_entries_keep_their_own_gate_order()
    {
        var options = new WebResearchOptions { Enabled = true };
        var closedWorkspace = Gatekeeper(options, workspace: false);

        // Consented: the topic is judged before the workspace opt-in; toggle: the opt-in before the query.
        Assert.Equal(WebGate.NoTopic, (await closedWorkspace.CheckConsentedAsync(Tenant, "hello there", default)).Gate);
        Assert.Equal(WebGate.WorkspaceOptIn, (await closedWorkspace.CheckConsentedAsync(Tenant, MarketQuestion, default)).Gate);
        Assert.Equal(WebGate.WorkspaceOptIn, (await closedWorkspace.CheckToggleAsync(Tenant, "x", default)).Gate);
        Assert.Equal(WebGate.TooShort, (await Gatekeeper(options).CheckToggleAsync(Tenant, "x", default)).Gate);
        Assert.Equal(WebGate.Budget, (await Gatekeeper(options, budget: false).CheckToggleAsync(Tenant, MarketQuestion, default)).Gate);
    }

    [Fact]
    public async Task Without_the_workspace_requirement_the_policy_is_not_asked()
    {
        var options = new WebResearchOptions { Enabled = true, RequireWorkspaceOptIn = false };
        var policy = new Policy(false);
        var gatekeeper = new WebGatekeeper(WebResearchOptionsMonitor.Static(options), policy, new Budget(true));

        Assert.Equal(WebGate.Open, await gatekeeper.RecheckBeforeRunAsync(Tenant, default));
        Assert.Equal(0, policy.Calls);
    }
}
