using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E02/F05/US01/T01 (us-01-correction-history, AC-1) that `PATCH
/// /api/contracts/{id}` is actually mapped in <c>Program.cs</c> (via
/// <c>ContractsEndpointExtensions</c>) and enforces its request-shape guard clauses — mirrors
/// <see cref="DocumentUploadEndpointTests"/>'s own "not just a placeholder" purpose. Only
/// exercises branches that return before any database call is made, so — like
/// <see cref="DocumentUploadEndpointTests"/> — this needs no running Postgres;
/// <c>Raffa.Documents.Contracts.Tests.ContractCorrectionServiceTests</c> (a separate assembly)
/// proves the actual versioning/history behaviour against a real database.
/// </summary>
public sealed class ContractCorrectionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ContractCorrectionEndpointTests(WebApplicationFactory<Program> factory)
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
        using var request = BuildRequest(Guid.NewGuid(), new { corrections = new Dictionary<string, string?> { ["status"] = "active" } });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = BuildRequest(Guid.NewGuid(), new { corrections = new Dictionary<string, string?> { ["status"] = "active" } });
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_contract_id_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/contracts/not-a-guid")
        {
            Content = JsonBody(new { corrections = new Dictionary<string, string?> { ["status"] = "active" } }),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_corrections_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = BuildRequest(Guid.NewGuid(), new { reason = "no corrections supplied" });
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Empty_corrections_map_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = BuildRequest(Guid.NewGuid(), new { corrections = new Dictionary<string, string?>() });
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpRequestMessage BuildRequest(Guid contractId, object body) =>
        new(HttpMethod.Patch, $"/api/contracts/{contractId}") { Content = JsonBody(body) };

    private static StringContent JsonBody(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
}
