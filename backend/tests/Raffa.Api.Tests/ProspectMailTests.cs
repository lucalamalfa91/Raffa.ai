using Raffa.Api.Infrastructure;

namespace Raffa.Api.Tests;

public sealed class ProspectMailTests
{
    private static readonly DateTimeOffset Expiry = new(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_html_carries_the_link_as_a_button_and_as_plain_text_and_encodes_it()
    {
        var html = AcsInvitationMailer.ComposeProspectHtml("https://app.example/prospect/accept#abc&x=\"1\"", Expiry);

        Assert.Contains("href=\"https://app.example/prospect/accept#abc&amp;x=&quot;1&quot;\"", html, StringComparison.Ordinal);
        Assert.Contains("Create my workspace", html, StringComparison.Ordinal);
        Assert.Contains("10/11/2026", html, StringComparison.Ordinal);
        Assert.DoesNotContain("x=\"1\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_logo_is_one_image_hosted_on_the_links_own_host_with_its_name_as_alt()
    {
        var html = AcsInvitationMailer.ComposeProspectHtml("https://demo.example.com/prospect/accept#abc", Expiry);

        Assert.Contains("src=\"https://demo.example.com/brand/raffa-logo.png\"", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"Raffa.ai\"", html, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<img "));
        Assert.Contains("<title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("!", html.Replace("<!doctype html>", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("Your invitation to Raffa.ai", AcsInvitationMailer.ComposeProspectSubject(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_plain_text_still_has_the_link_and_the_expiry()
    {
        var text = AcsInvitationMailer.ComposeProspectBody("https://app.example/prospect/accept#abc", Expiry);

        Assert.Contains("https://app.example/prospect/accept#abc", text, StringComparison.Ordinal);
        Assert.Contains("10/11/2026", text, StringComparison.Ordinal);
    }
}
