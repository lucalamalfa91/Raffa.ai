using System.Security.Claims;
using Raffa.Identity.Workspace.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Raffa.IntegrationTests;

/// <summary>
/// Test-only <see cref="ClaimsPrincipal"/> simulation for `GET /api/audit`
/// (<see cref="WorkspacePrincipalAuthorization"/>). Registered only by
/// <see cref="R0IntegrationFixture"/>'s test host, never by production
/// <c>Raffa.Api.Program</c> — ADR-010 (Entra ID/OIDC) is deliberately not wired there by this
/// task (see <see cref="WorkspacePrincipalAuthorization"/>'s own doc comment: "a real JWT bearer
/// handler later (or a test today) both work the same way").
///
/// Reads two request headers this test project controls (<see cref="TenantIdHeaderName"/>/
/// <see cref="RoleHeaderName"/>) and turns them into exactly the claim shape
/// <see cref="WorkspacePrincipalAuthorization"/> already expects. Implemented as an
/// <see cref="IStartupFilter"/> — which wraps the pipeline the host itself builds — rather than a
/// change to <c>Program.cs</c>, so production composition stays exactly as ADR-010-deferred as it
/// already documents itself to be; only requests carrying both test headers get a synthesized
/// authenticated principal, every other request is unaffected (falls through to the default
/// anonymous <see cref="ClaimsPrincipal"/> ASP.NET Core already assigns).
/// </summary>
public sealed class TestPrincipalStartupFilter : IStartupFilter
{
    public const string TenantIdHeaderName = "X-Test-Tenant-Id";
    public const string RoleHeaderName = "X-Test-Role";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(TenantIdHeaderName, out var tenantIdValues) &&
                context.Request.Headers.TryGetValue(RoleHeaderName, out var roleValues))
            {
                var claims = new List<Claim>
                {
                    new(WorkspacePrincipalAuthorization.TenantIdClaimType, tenantIdValues.ToString()),
                    new(ClaimTypes.Role, roleValues.ToString()),
                };
                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
            }

            await nextMiddleware().ConfigureAwait(false);
        });

        next(app);
    };
}
