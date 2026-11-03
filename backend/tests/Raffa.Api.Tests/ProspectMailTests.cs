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
        Assert.Contains("Accept your invitation", html, StringComparison.Ordinal);
        Assert.Contains("10/11/2026", html, StringComparison.Ordinal);
        Assert.DoesNotContain("x=\"1\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_plain_text_still_has_the_link_and_the_expiry()
    {
        var text = AcsInvitationMailer.ComposeProspectBody("https://app.example/prospect/accept#abc", Expiry);

        Assert.Contains("https://app.example/prospect/accept#abc", text, StringComparison.Ordinal);
        Assert.Contains("10/11/2026", text, StringComparison.Ordinal);
    }
}
