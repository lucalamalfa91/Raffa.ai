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
    public void The_logo_is_one_inline_image_with_its_name_as_alt_and_a_url_fallback()
    {
        var inline = AcsInvitationMailer.ComposeProspectHtml("https://demo.example.com/prospect/accept#abc", Expiry, "cid:raffa-logo");
        Assert.Contains("src=\"cid:raffa-logo\"", inline, StringComparison.Ordinal);
        Assert.Contains("alt=\"Raffa.ai\"", inline, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(inline, "<img "));

        var hosted = AcsInvitationMailer.ComposeProspectHtml("https://demo.example.com/prospect/accept#abc", Expiry);
        Assert.Contains("src=\"https://demo.example.com/brand/raffa-logo.png\"", hosted, StringComparison.Ordinal);
        Assert.Contains("<title>", hosted, StringComparison.Ordinal);
    }

    [Fact]
    public void The_invitation_sells_the_product_and_states_who_sends_it_and_why()
    {
        var html = AcsInvitationMailer.ComposeProspectHtml("https://demo.example.com/prospect/accept#abc", Expiry, "cid:raffa-logo");

        Assert.Contains("Never miss a contract renewal again.", html, StringComparison.Ordinal);
        Assert.Contains("Know every deadline", html, StringComparison.Ordinal);
        Assert.Contains("Stay in control", html, StringComparison.Ordinal);
        Assert.Contains("administrator of Raffa.ai entered your address", html, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "Create my workspace"));
        Assert.Contains("Never miss a contract renewal again.", AcsInvitationMailer.ComposeProspectBody("https://demo.example.com/x", Expiry), StringComparison.Ordinal);
        Assert.Contains("Your invitation to Raffa.ai", AcsInvitationMailer.ComposeProspectSubject(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_logo_is_embedded_in_the_assembly()
    {
        var logo = AcsInvitationMailer.Logo();
        Assert.NotNull(logo);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, logo!.Take(4).ToArray());
    }

    [Fact]
    public void The_plain_text_still_has_the_link_and_the_expiry()
    {
        var text = AcsInvitationMailer.ComposeProspectBody("https://app.example/prospect/accept#abc", Expiry);

        Assert.Contains("https://app.example/prospect/accept#abc", text, StringComparison.Ordinal);
        Assert.Contains("10/11/2026", text, StringComparison.Ordinal);
    }
}
