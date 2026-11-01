using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Raffa.Api.Fixtures;

namespace Raffa.Api.Tests;

/// <summary>AskRaffa 4.0 fixture mode (IMPLEMENTATION-PLAN.md §2): refused in Production, the
/// scenario clock honours <c>X-Raffa-Now</c>, the <c>Fixture</c> scheme signs in only the two
/// fixture members, and the endpoints/scheme exist only when enabled.</summary>
public sealed class FixtureModeTests
{
    [Theory]
    [InlineData("Production", true)]
    [InlineData("production", true)]
    [InlineData("Parity", false)]
    [InlineData("Development", false)]
    public void Enabled_fixtures_are_refused_only_in_production(string environment, bool throws)
    {
        var options = new FixtureOptions { Enabled = true };

        var error = Record.Exception(() => options.ValidateOrThrow(environment));

        Assert.Equal(throws, error is InvalidOperationException);
    }

    [Fact]
    public void Disabled_fixtures_are_fine_in_production()
    {
        new FixtureOptions().ValidateOrThrow("Production");
    }

    [Fact]
    public void Clock_reads_the_header_date_else_the_configured_date()
    {
        var options = new FixtureOptions();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var clock = new FixtureClock(accessor, options);

        Assert.Equal(new DateOnly(2026, 9, 8), DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));

        accessor.HttpContext.Request.Headers[FixtureClock.HeaderName] = "2026-10-20";
        Assert.Equal(new DateOnly(2026, 10, 20), DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));

        accessor.HttpContext.Request.Headers[FixtureClock.HeaderName] = "not a date";
        Assert.Equal(new DateOnly(2026, 9, 8), DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));
    }

    [Fact]
    public void Fixture_users_are_luca_the_approver_and_marta_the_buyer()
    {
        var luca = FixtureTenant.Find("LUCA")!;
        var marta = FixtureTenant.Find("marta")!;

        Assert.Equal(("Luca Lamalfa", "admin", true), (luca.DisplayName, luca.Role, luca.IsApprover));
        Assert.Equal(("Marta Keller", "procurement", false), (marta.DisplayName, marta.Role, marta.IsApprover));
        Assert.NotEqual(luca.ExternalSubjectId, marta.ExternalSubjectId);
        Assert.Null(FixtureTenant.Find("eve"));
    }

    [Theory]
    [InlineData("luca", HttpStatusCode.OK)]
    [InlineData("marta", HttpStatusCode.OK)]
    [InlineData("eve", HttpStatusCode.Unauthorized)]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    public async Task Fixture_scheme_signs_in_only_the_fixture_members(string? user, HttpStatusCode expected)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddAuthentication(FixtureAuthenticationHandler.SchemeName)
            .AddFixtureScheme(new FixtureOptions { Enabled = true });
        await using var app = builder.Build();
        app.UseAuthentication();
        app.MapGet("/whoami", (ClaimsPrincipal principal) =>
            principal.Identity?.IsAuthenticated == true ? Results.Ok(principal.FindFirst("oid")?.Value) : Results.Unauthorized());
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        if (user is not null)
        {
            request.Headers.Add(FixtureAuthenticationHandler.UserHeaderName, user);
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Contains(FixtureTenant.Find(user)!.ExternalSubjectId, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        await app.StopAsync();
    }

    [Fact]
    public async Task Fixture_endpoints_are_not_mapped_when_disabled()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapFixtureMode(new FixtureOptions { Enabled = false });
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/fixtures/session");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await app.StopAsync();
    }
}
