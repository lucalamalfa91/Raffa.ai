using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E02/F05/US01/T02 (us-01-correction-history, AC-2) that `GET
/// /api/contracts/{id}/corrections` is actually mapped in <c>Program.cs</c> (via
/// <c>ContractsEndpointExtensions</c>) and enforces its request-shape guard clauses — mirrors
/// <see cref="ContractCorrectionEndpointTests"/>'s own "not just a placeholder" purpose. Only
/// exercises branches that return before any database call is made, so — like
/// <see cref="ContractCorrectionEndpointTests"/> — this needs no running Postgres;
/// <c>Raffa.Documents.Contracts.Tests.ContractCorrectionHistoryQueryServiceTests</c> (a separate
/// assembly) proves the actual query behaviour against a real database.
/// </summary>
public sealed class ContractCorrectionHistoryEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ContractCorrectionHistoryEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:DocumentsContracts",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
            builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");
        });
    }

    [Fact]
    public async Task Missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/contracts/{Guid.NewGuid()}/corrections");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/contracts/{Guid.NewGuid()}/corrections");
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_contract_id_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/contracts/not-a-guid/corrections");
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
