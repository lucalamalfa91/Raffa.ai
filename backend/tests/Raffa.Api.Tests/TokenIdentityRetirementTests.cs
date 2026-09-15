using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// T14 (ADR-025 §H, §I): "the ADR-010 retirement test — written now and activated by NW-05/NW-08
/// in W15." With a validated token present, <c>X-User-Id</c> is <b>ignored</b>, not merely
/// overridden (token <c>A</c> + <c>X-User-Id: B</c> acts as <c>A</c>, never as <c>B</c>); a token
/// carrying <c>roles</c>/<c>tenant_id</c> claims does not grant that role or tenant — resolution
/// stays database-only (§E/§I).
///
/// <para>
/// <b>Why <c>Skip</c>, not delete:</b> <c>Raffa.Api.Infrastructure.HeaderCallerIdentity.Resolve()</c>
/// reads only the <c>X-User-Id</c> header today and never consults <c>HttpContext.User</c> at all
/// (ADR-025 §A1/§A3: "NW-05 (W15) replaces the header with the validated token subject by editing
/// one file"). Until that edit lands, this class's own <see cref="TestTokenStartupFilter"/>
/// simulates the token's principal, but nothing downstream reads it — so every assertion below is
/// written for the post-NW-05 world and would fail if actually run today. Activating this test is
/// exactly that one-file <c>HeaderCallerIdentity</c> change; nothing here should need to change
/// with it.
/// </para>
/// </summary>
public sealed class TokenIdentityRetirementTests : IClassFixture<RaffaApiFactory>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public TokenIdentityRetirementTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    [Fact] // activated 2026-09-14: NW-05 landed (TokenCallerIdentity) and is now applied on every route
    public async Task T14_a_validated_token_ignores_x_user_id_entirely_never_merely_overriding_it()
    {
        var factory = WithInMemoryIdentityAndTestToken();
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedAdminMembershipAsync(factory, tenantId, "real-admin@acme.example");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{tenantId}/invites")
        {
            Content = JsonContent.Create(new { email = "new.hire@acme.example", role = "Procurement" }),
        };
        // Token subject is the real Admin; the header names someone else entirely -- ADR-025 Rule
        // A3 requires the header to be ignored outright, not merely lose a tie-break.
        request.Headers.Add(TestTokenStartupFilter.TokenSubjectHeaderName, "real-admin@acme.example");
        request.Headers.Add("X-User-Id", "attacker@acme.example");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact] // activated 2026-09-14: NW-05 landed (TokenCallerIdentity) and is now applied on every route
    public async Task T14_a_token_carrying_role_and_tenant_claims_grants_neither()
    {
        var factory = WithInMemoryIdentityAndTestToken();
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        // "stranger" holds no real membership anywhere -- only a self-asserted token claim.

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{tenantId}/invites")
        {
            Content = JsonContent.Create(new { email = "new.hire@acme.example", role = "Procurement" }),
        };
        request.Headers.Add(TestTokenStartupFilter.TokenSubjectHeaderName, "stranger@acme.example");
        request.Headers.Add(TestTokenStartupFilter.TokenRoleHeaderName, "Admin");
        request.Headers.Add(TestTokenStartupFilter.TokenTenantIdHeaderName, tenantId.ToString());

        var response = await client.SendAsync(request);

        // ADR-025 §E/§I: role and tenant are resolved from workspace_membership, never from a
        // token claim -- a self-claimed Admin/tenant_id buys nothing. Non-membership is 404, never
        // 403 (Rule B1) and never 201.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact] // NW-08 (wave w16): GET /api/audit's own deletion proof (ADR-025 §K.3, S-T25).
    public async Task S_T25_a_token_carrying_tenant_and_admin_claims_with_no_real_membership_gets_404_on_audit()
    {
        var factory = WithInMemoryIdentityAndTestToken();
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        // No membership is ever seeded for this tenant -- the whole point is that the claims below
        // must not substitute for one.

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/audit");
        // The token carries an authenticated subject plus tenant_id/roles:Admin claims naming this
        // tenant -- exactly the shape the deleted WorkspacePrincipalAuthorization used to accept.
        request.Headers.Add(TestTokenStartupFilter.TokenSubjectHeaderName, "stranger@acme.example");
        request.Headers.Add(TestTokenStartupFilter.TokenRoleHeaderName, "Admin");
        request.Headers.Add(TestTokenStartupFilter.TokenTenantIdHeaderName, tenantId.ToString());
        // ICallerContext reads the tenant from this header, never from a claim -- send the real
        // selector too, or the request would 400 before ever reaching the membership check this
        // test exists to prove.
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);

        // ADR-025 §K.3: a token claiming tenant_id + roles:Admin for a tenant with no live
        // membership must still be 404, never 200 and never 403 -- proof that
        // WorkspacePrincipalAuthorization's claims path is gone, not merely unreachable from this
        // route's own normal traffic.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private WebApplicationFactory<Program> WithInMemoryIdentityAndTestToken()
    {
        var gateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
        return _baseFactory.WithInMemoryAskEngine(gateway).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, TestTokenStartupFilter>()));
    }

    private static async Task SeedMembershipAsync(
        WebApplicationFactory<Program> factory, Guid tenantId, string email, Raffa.Identity.Workspace.Domain.WorkspaceRoleName roleName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Raffa.Identity.Workspace.Infrastructure.IdentityWorkspaceDbContext>();

        var tenant = new TenantId(tenantId);
        var now = new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);
        var user = new Raffa.Identity.Workspace.Domain.WorkspaceUser { TenantId = tenant, Email = email, CreatedAt = now };
        var role = new Raffa.Identity.Workspace.Domain.WorkspaceRole { TenantId = tenant, Name = roleName, CreatedAt = now };
        // 2026-09-14: the invite path now needs the workspace row (its name goes into the mail and the
        // guest invitation) and the role being granted to exist -- the same seed
        // WorkspaceInviteOutcomeTests uses.
        db.Workspaces.Add(new Raffa.Identity.Workspace.Domain.WorkspaceTenant { TenantId = tenant, Name = "Acme Procurement", CreatedAt = now });
        db.WorkspaceRoles.Add(new Raffa.Identity.Workspace.Domain.WorkspaceRole { TenantId = tenant, Name = Raffa.Identity.Workspace.Domain.WorkspaceRoleName.Procurement, CreatedAt = now });
        db.WorkspaceUsers.Add(user);
        db.WorkspaceRoles.Add(role);
        db.WorkspaceMemberships.Add(new Raffa.Identity.Workspace.Domain.WorkspaceMembership
        {
            TenantId = tenant,
            WorkspaceUserId = user.Id,
            WorkspaceRoleId = role.Id,
            CreatedAt = now,
        });

        await db.SaveChangesAsync();
    }

    private static Task SeedAdminMembershipAsync(WebApplicationFactory<Program> factory, Guid tenantId, string email) =>
        SeedMembershipAsync(factory, tenantId, email, Raffa.Identity.Workspace.Domain.WorkspaceRoleName.Admin);

    /// <summary>
    /// Simulates "a validated token present" for this file's tests — never registered by
    /// production <c>Raffa.Api.Program</c>. Reads three test-only headers and synthesizes an
    /// authenticated <see cref="ClaimsPrincipal"/> carrying a <c>sub</c> claim plus, optionally,
    /// <c>roles</c>/<c>tenant_id</c> — the exact shape a real ADR-010 bearer token would carry, and
    /// also the shape wave w16's NW-08 deletion proof needs (S-T25, ADR-025 §K.3): those two claims
    /// must grant nothing now that the claims-based <c>WorkspacePrincipalAuthorization</c> guard is
    /// gone. Nothing in production code reads this principal's <c>roles</c>/<c>tenant_id</c> claims
    /// — <c>TokenCallerIdentity</c> reads only <c>oid</c>.
    /// </summary>
    private sealed class TestTokenStartupFilter : IStartupFilter
    {
        public const string TokenSubjectHeaderName = "X-Test-Token-Subject";
        public const string TokenRoleHeaderName = "X-Test-Token-Role";
        public const string TokenTenantIdHeaderName = "X-Test-Token-Tenant-Id";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(TokenSubjectHeaderName, out var subjectValues))
                {
                    var claims = new List<Claim>
                    {
                        new(ClaimTypes.NameIdentifier, subjectValues.ToString()),
                        // 2026-09-14: TokenCallerIdentity resolves the subject from `oid` (ADR-010 w15 footer
                        // �2.1), so the simulated token carries it too -- the same value, as a real token would.
                        new("oid", subjectValues.ToString()),
                    };

                    if (context.Request.Headers.TryGetValue(TokenRoleHeaderName, out var roleValues))
                    {
                        claims.Add(new Claim(ClaimTypes.Role, roleValues.ToString()));
                    }

                    if (context.Request.Headers.TryGetValue(TokenTenantIdHeaderName, out var tenantValues))
                    {
                        claims.Add(new Claim("tenant_id", tenantValues.ToString()));
                    }

                    context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
                }

                await nextMiddleware().ConfigureAwait(false);
            });

            next(app);
        };
    }
}
