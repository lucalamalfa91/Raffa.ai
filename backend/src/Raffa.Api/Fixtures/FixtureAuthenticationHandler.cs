using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Raffa.Api.Fixtures;

/// <summary>
/// The <c>Fixture</c> authentication scheme (IMPLEMENTATION-PLAN.md §2): registered — and made the
/// default scheme — only when <c>Fixtures:Enabled</c> is true, which the host refuses in
/// Production. <c>X-Raffa-Fixture-User: luca|marta</c> signs the request in as that member of the
/// fixture tenant, with the same <c>oid</c>/<c>email</c>/<c>name</c> claims the API already reads
/// from an Entra token, so <c>ICallerIdentity</c>/<c>ICallerContext</c> resolve the caller and the
/// workspace membership exactly as for a real user. Unknown or absent value: anonymous (the
/// endpoints answer 401 as usual).
/// </summary>
internal sealed class FixtureAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Fixture";

    public const string UserHeaderName = "X-Raffa-Fixture-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeaderName, out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var member = FixtureTenant.Find(values.ToString());
        if (member is null)
        {
            return Task.FromResult(AuthenticateResult.Fail($"Unknown fixture user '{values}'."));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim("oid", member.ExternalSubjectId),
                new Claim("email", member.Email),
                new Claim("name", member.DisplayName),
            ],
            authenticationType: SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
