using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E01/F06/US01/T02 (us-01-document-upload, AC-3) that
/// `GET /api/documents/{id}` is actually mapped in <c>Program.cs</c> and enforces its
/// request-shape guard clauses — mirrors <see cref="DocumentUploadEndpointTests"/>'s own "not
/// just a placeholder" purpose. Only exercises branches that return before any database call is
/// made, so — like <see cref="DocumentUploadEndpointTests"/> — this needs no running Postgres.
/// </summary>
public sealed class DocumentMetadataEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DocumentMetadataEndpointTests(WebApplicationFactory<Program> factory)
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

        var response = await client.GetAsync($"/api/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{Guid.NewGuid()}");
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_document_id_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/documents/not-a-guid");
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
