using Raffa.AiGateway.Foundry;

namespace Raffa.AiGateway.Tests.Foundry;

/// <summary>F3-T02 — the canonical form of a web source URL (host lower-case, https, no fragment,
/// no tracking parameters, no trailing slash).</summary>
public class WebSourceUrlTests
{
    [Theory]
    [InlineData("https://example.com/a", "https://example.com/a")]
    [InlineData("HTTPS://Example.COM/A", "https://example.com/A")]
    [InlineData("http://example.com/a", "https://example.com/a")]
    [InlineData("https://example.com/a/", "https://example.com/a")]
    [InlineData("https://example.com/", "https://example.com")]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("https://example.com/a#section-2", "https://example.com/a")]
    [InlineData("https://example.com/a?utm_source=x&utm_medium=y", "https://example.com/a")]
    [InlineData("https://example.com/a?id=7&utm_campaign=x", "https://example.com/a?id=7")]
    [InlineData("https://example.com/a?gclid=abc&id=7&fbclid=zzz", "https://example.com/a?id=7")]
    [InlineData("https://example.com:443/a", "https://example.com/a")]
    [InlineData("http://example.com:80/a", "https://example.com/a")]
    [InlineData("https://example.com:8443/a", "https://example.com:8443/a")]
    [InlineData("https://user:pass@example.com/a", "https://example.com/a")]
    [InlineData("  https://example.com/a  ", "https://example.com/a")]
    public void Normalises_to_one_canonical_form(string input, string expected) =>
        Assert.Equal(expected, WebSourceUrl.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/file")]
    [InlineData("javascript:alert(1)")]
    public void Anything_that_is_not_an_absolute_http_url_is_null(string? input) =>
        Assert.Null(WebSourceUrl.Normalize(input));

    [Fact]
    public void Two_spellings_of_one_page_normalise_to_the_same_string() =>
        Assert.Equal(
            WebSourceUrl.Normalize("HTTP://Example.com/a/?utm_source=n#top"),
            WebSourceUrl.Normalize("https://example.com/a"));
}
