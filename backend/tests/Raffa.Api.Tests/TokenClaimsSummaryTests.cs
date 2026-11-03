using System.Text;
using Raffa.Api.Infrastructure;

namespace Raffa.Api.Tests;

public sealed class TokenClaimsSummaryTests
{
    private static string Jwt(string payloadJson) =>
        $"{B64("{\"alg\":\"none\"}")}.{B64(payloadJson)}.signature-never-read";

    private static string B64(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void Reports_audience_tenant_object_id_and_roles_and_nothing_else()
    {
        var summary = TokenClaimsSummary.Describe(
            Jwt("""{"aud":"https://graph.microsoft.com","tid":"t-1","oid":"o-1","roles":["User.Invite.All","Other"],"appid":"secret-looking-app","sub":"s"}"""));

        Assert.Equal("token aud=https://graph.microsoft.com tid=t-1 oid=o-1 roles=[User.Invite.All|Other]", summary);
        Assert.DoesNotContain("secret-looking-app", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("signature-never-read", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_without_roles_says_none()
    {
        Assert.Contains("roles=[none]", TokenClaimsSummary.Describe(Jwt("""{"aud":"x","tid":"t","oid":"o"}""")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.%%%.c")]
    public void Garbage_never_throws(string value)
    {
        Assert.False(string.IsNullOrWhiteSpace(TokenClaimsSummary.Describe(value)));
    }
}
