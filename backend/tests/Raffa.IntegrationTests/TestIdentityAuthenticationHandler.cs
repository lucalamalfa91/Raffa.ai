using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Raffa.IntegrationTests;

/// <summary>
/// Fix 2026-09-14. Test-only bridge from the interim <c>X-User-Id</c> header (ADR-022) to the
/// authenticated <c>oid</c> claim <c>Raffa.Api.Infrastructure.TokenCallerIdentity</c> actually
/// reads (ADR-010 w15 footer §2.1), for this project's own dedicated-host fixtures.
///
/// <para>
/// Wave w15's NW-05 (task E18/F01/US01/T01) retired the header-reading <c>ICallerIdentity</c> in
/// favour of the validated bearer token. Task E17/F01/US01/T01 registered an equivalent bridge in
/// <c>Raffa.Api.Tests.TestSupport.InMemoryAskEngineFactory</c>, but this project builds its own
/// hosts and never saw it, so every R0/R1 request started authenticating nobody and answering 401
/// where a real guard used to answer 2xx/403/404. That kept <c>main</c> red and, because
/// <c>backend.yml</c> gates the deploy on the test job, the w15 API image was never deployed.
/// Minting a real signed Entra token in a test is not possible, so this is the standard ASP.NET
/// Core substitute: a scheme that turns the header these tests already send into the claim the
/// production seam reads. <c>Raffa.Api.Program</c>'s own JwtBearer registration is untouched and
/// never runs here.
/// </para>
///
/// <para>
/// A request with no <c>X-User-Id</c> header is <see cref="AuthenticateResult.NoResult"/>, never
/// <see cref="AuthenticateResult.Fail(string)"/>: the "no identity" tests expect to reach the
/// endpoint unauthenticated and be turned into 401 by <c>ICallerIdentity.Resolve()</c> itself --
/// the same path a missing or invalid bearer token takes in production -- not short-circuited by
/// the authentication middleware.
/// </para>
/// </summary>
public sealed class TestIdentityAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestUserId";

    public const string UserIdHeaderName = "X-User-Id";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // ADR-025 Rule A3 (T14): when a validated token principal is already on the request -- put
        // there by a filter that simulates the bearer token -- the header is ignored outright, not
        // merely out-ranked: NoResult leaves that principal in place untouched.
        if (Context.User?.Identity is { IsAuthenticated: true })
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>();

        // No trim, no case change: TokenCallerIdentity.Resolve() applies neither (an `oid` is an
        // opaque, case-sensitive Entra object id), so this bridge does not either -- a header that
        // is only whitespace is treated exactly like a missing one.
        if (Request.Headers.TryGetValue(UserIdHeaderName, out var userIdValues) &&
            !string.IsNullOrWhiteSpace(userIdValues.ToString()))
        {
            // "oid": the exact claim type Microsoft.Identity.Web's ClaimsPrincipal.GetObjectId()
            // resolves, which is TokenCallerIdentity.Resolve()'s own source.
            claims.Add(new Claim("oid", userIdValues.ToString()));

            // Fix 2026-09-14: a real access token also carries `email` (ResolveEmail()'s source),
            // and POST /api/workspaces needs it for the creator's Email column now that the
            // subject is an `oid`. An X-User-Id that already is an address doubles as the email,
            // which keeps every existing R0/R1 flow exactly as it was.
            if (userIdValues.ToString().Contains('@'))
            {
                claims.Add(new Claim("email", userIdValues.ToString()));
            }
        }

        if (claims.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(claims, authenticationType: SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
