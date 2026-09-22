using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Guards;

namespace Raffa.Chat.Tests.Guards;

/// <summary>ADR-030: a web summary is shown only when every marker and every URL point at a source
/// the search tool itself returned, and every source is a public https host.</summary>
public sealed class WebGuardTests
{
    private static readonly AiWebSource[] TwoSources =
    [
        new("https://example.com/procurement/saas-renewals", "SaaS renewal benchmarks", "5-10% uplift cap"),
        new("https://example.org/negotiation/levers", "Levers", "multi-year commitments"),
    ];

    [Fact]
    public void Passes_a_summary_whose_markers_are_within_the_source_list()
    {
        var verdict = WebGuard.Validate("Caps of 5-10% are common [1]; multi-year commitments help [2].", TwoSources);

        Assert.True(verdict.Passed, verdict.Violation);
    }

    [Fact]
    public void Fails_without_any_source()
    {
        var verdict = WebGuard.Validate("Something [1].", []);

        Assert.False(verdict.Passed);
        Assert.Contains("no web source", verdict.Violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Fails_a_marker_past_the_source_list()
    {
        var verdict = WebGuard.Validate("Caps are common [3].", TwoSources);

        Assert.False(verdict.Passed);
        Assert.Contains("[3]", verdict.Violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Fails_a_url_in_the_prose_that_is_not_a_source()
    {
        var verdict = WebGuard.Validate("See https://made-up.example/page [1].", TwoSources);

        Assert.False(verdict.Passed);
        Assert.Contains("made-up.example", verdict.Violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Allows_a_source_url_repeated_in_the_prose()
    {
        var verdict = WebGuard.Validate("From https://example.org/negotiation/levers/ [2].", TwoSources);

        Assert.True(verdict.Passed, verdict.Violation);
    }

    [Theory]
    [InlineData("http://example.com/a", "only https")]
    [InlineData("https://10.0.0.1/a", "IP address")]
    [InlineData("https://localhost/a", "local")]
    [InlineData("https://intranet.corp/a", "local")]
    [InlineData("https://raffa/a", "local")]
    [InlineData("not a url", "absolute")]
    public void Rejects_non_public_sources(string url, string reasonFragment)
    {
        var verdict = WebGuard.Validate("Text [1].", [new AiWebSource(url, "t", "s")]);

        Assert.False(verdict.Passed);
        Assert.Contains(reasonFragment, verdict.Violation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fails_an_empty_summary()
    {
        var verdict = WebGuard.Validate("  ", TwoSources);

        Assert.False(verdict.Passed);
    }
}
