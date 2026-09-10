using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E04/F02/US02/T01 (savings-opportunity) that `GET /api/savings` and
/// `PATCH /api/savings/{id}` are actually mapped in <c>Program.cs</c> and enforce their
/// request-shape guard clauses — mirrors <see cref="RenewalsEndpointTests"/>'s own "not just a
/// placeholder" purpose. Only exercises branches that return before any database call is made (the
/// tenant-header check, and for PATCH the route-id parse, both run before
/// <c>Raffa.Savings.Application.SavingsOpportunityService</c> is ever called), so this needs no
/// running Postgres. The success path (real persistence, real audit entries, real partial-update
/// semantics) is proven at the Testcontainers level instead —
/// <c>Raffa.Savings.Tests.SavingsOpportunityServiceTests</c> — per this task's own "Tests
/// required" level (unit, no database).
/// </summary>
public sealed class SavingsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SavingsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:Savings",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
        });
    }

    [Fact]
    public async Task Get_missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/savings");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/savings");
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/savings/{Guid.NewGuid()}")
        {
            Content = JsonContent(new { owner = "alice@acme.example" }),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/savings/{Guid.NewGuid()}")
        {
            Content = JsonContent(new { owner = "alice@acme.example" }),
        };
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_invalid_route_id_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/savings/not-a-guid")
        {
            Content = JsonContent(new { owner = "alice@acme.example" }),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static System.Net.Http.Json.JsonContent JsonContent(object value) =>
        System.Net.Http.Json.JsonContent.Create(value);
}
