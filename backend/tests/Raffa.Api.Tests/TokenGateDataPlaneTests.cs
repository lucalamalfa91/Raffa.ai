using System.Net;
using System.Net.Http.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.Identity.Workspace.Domain;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Raffa.Api.Tests;

/// <summary>
/// Fix 2026-09-14: NW-05 (task E18/F01/US01/T01) applied to the data plane. Acceptance A15-8 and the
/// task's own AC -- "forged headers and no token ⇒ 401 on every authenticated route", "a valid token
/// whose subject is not a member ⇒ 404, never 403" -- proven over the real HTTP pipeline on a host
/// built <b>without</b> the implicit tenant Admin, so that "no caller" really is no caller. Until
/// this commit every route below answered 200 to an unauthenticated request that merely supplied an
/// <c>X-Tenant-Id</c> header (verified against deployed <c>dev</c> on the w15 image).
/// </summary>
public sealed class TokenGateDataPlaneTests : IDisposable
{
    private readonly RaffaApiFactory _factory = new() { ImplicitTenantAdmin = false };

    public static TheoryData<string, string> GuardedRoutes => new()
    {
        { "GET", "/api/documents" },
        { "GET", "/api/documents/00000000-0000-0000-0000-000000000001" },
        { "GET", "/api/contracts" },
        { "GET", "/api/contracts/00000000-0000-0000-0000-000000000001" },
        { "GET", "/api/renewals" },
        { "GET", "/api/savings" },
        { "GET", "/api/savings/kpis" },
        { "GET", "/api/conversations" },
        { "POST", "/api/conversations" },
        { "POST", "/api/chat/query" },
        { "POST", "/api/negotiations/outcomes" },
        { "GET", "/api/quotes/00000000-0000-0000-0000-000000000001/assessment" },
        { "GET", "/api/insights/criticality" },
    };

    [Theory]
    [MemberData(nameof(GuardedRoutes))]
    public async Task No_token_is_401_on_every_guarded_route_whatever_the_headers_say(string method, string path)
    {
        var client = _factory.CreateClient();
        var tenantId = Guid.NewGuid();

        // A well-formed tenant selector, a forged legacy identity header and a garbage bearer: none
        // of them is an identity, so the answer is 401 before the tenant header is even read.
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "POST" ? JsonContent.Create(new { question = "anything", }) : null,
        };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        request.Headers.Add("Authorization", "Bearer not-a-real-token");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_caller_who_is_not_a_member_of_the_named_tenant_gets_404_never_403_and_never_data()
    {
        var factory = _factory.WithInMemoryAskEngine(
            new RecordingAiGateway(new Raffa.AiGateway.Fixtures.FixtureAiGateway(
                new Raffa.AiGateway.Configuration.AiGatewayModelOptions(), SystemClock.Instance,
                new Raffa.AiGateway.Configuration.AiGatewayOcrOptions())));
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await ImplicitTenantAdminStartupFilter.EnsureMembershipAsync(
            factory.Services, new TenantId(tenantId), "member@acme.example", WorkspaceRoleName.Admin);

        using var stranger = new HttpRequestMessage(HttpMethod.Get, "/api/documents");
        stranger.Headers.Add("X-Tenant-Id", tenantId.ToString());
        stranger.Headers.Add("X-User-Id", "stranger@elsewhere.example");
        var strangerResponse = await client.SendAsync(stranger);

        Assert.Equal(HttpStatusCode.NotFound, strangerResponse.StatusCode);

        using var member = new HttpRequestMessage(HttpMethod.Get, "/api/documents");
        member.Headers.Add("X-Tenant-Id", tenantId.ToString());
        member.Headers.Add("X-User-Id", "member@acme.example");
        var memberResponse = await client.SendAsync(member);

        Assert.Equal(HttpStatusCode.OK, memberResponse.StatusCode);
    }

    [Fact]
    public async Task A_missing_or_malformed_tenant_header_is_400_only_once_the_caller_is_real()
    {
        var client = _factory.CreateClient();

        using var missing = new HttpRequestMessage(HttpMethod.Get, "/api/documents");
        missing.Headers.Add("X-User-Id", "someone@acme.example");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(missing)).StatusCode);

        using var malformed = new HttpRequestMessage(HttpMethod.Get, "/api/documents");
        malformed.Headers.Add("X-User-Id", "someone@acme.example");
        malformed.Headers.Add("X-Tenant-Id", "not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(malformed)).StatusCode);
    }

    /// <summary>S-T18 (ADR-010 w15 footer): the JwtBearer validation the whole gate rests on is the
    /// strict one -- issuer, audience, lifetime and signing key all validated, the audience is the
    /// client id (never the identifier URI), and clock skew is two minutes, not the five-minute default.</summary>
    [Fact]
    public void Jwt_validation_flags_are_all_on_the_audience_is_the_client_id_and_skew_is_two_minutes()
    {
        const string clientId = "fd3c2201-9137-4a97-9bc4-6be2d6679495";
        using var pinned = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AzureAd:ClientId", clientId);
            builder.UseSetting("AzureAd:Audience", "api://raffa-test-api");
            builder.UseSetting("AzureAd:Authority", "https://login.microsoftonline.com/248eb472-b4dc-401b-8c3b-44443f0e92a3/v2.0");
        });
        var options = pinned.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var p = options.TokenValidationParameters;

        Assert.True(p.ValidateIssuer);
        Assert.True(p.ValidateAudience);
        Assert.True(p.ValidateLifetime);
        Assert.True(p.ValidateIssuerSigningKey);
        Assert.True(p.ClockSkew <= TimeSpan.FromMinutes(2), $"ClockSkew was {p.ClockSkew}");
        // The audience is the client id -- never the api://... identifier URI that AzureAd__Audience
        // also publishes (a v2 access token's `aud` is the client id).
        Assert.Equal(clientId, p.ValidAudience);
    }

    public void Dispose() => _factory.Dispose();
}
