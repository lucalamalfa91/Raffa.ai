using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E04/F03/US01/T01 (savings-kpis) that `GET /api/savings/kpis` is
/// actually mapped in <c>Program.cs</c> and enforces its request-shape guard clause — mirrors
/// <see cref="SavingsEndpointTests"/>/<see cref="RenewalsEndpointTests"/>'s own "not just a
/// placeholder" purpose. Only exercises the tenant-header check, which returns before either
/// <c>Raffa.Documents.Contracts.Application.PortfolioQueryService</c> or
/// <c>Raffa.Savings.Application.SavingsKpiQueryService</c> is ever called, so this needs no
/// running Postgres. The success path (real per-currency grouping, real "completed processing"
/// filtering, real tenant scoping) is proven at the plain-unit-test level instead —
/// <c>Raffa.Savings.Tests.SavingsKpiCalculatorTests</c> and
/// <c>Raffa.Documents.Contracts.Tests.PortfolioAnalysisCalculatorTests</c> — per this task's own
/// "Tests required" level (unit, no database).
/// </summary>
public sealed class SavingsKpiEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SavingsKpiEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:DocumentsContracts",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
            builder.UseSetting(
                "ConnectionStrings:Savings",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
        });
    }

    [Fact]
    public async Task Missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/savings/kpis");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/savings/kpis");
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
