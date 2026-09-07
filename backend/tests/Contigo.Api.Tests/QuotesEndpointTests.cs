using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Contigo.Api.Tests;

/// <summary>
/// Host-level proof for task E05/F01/US01/T01 (quote-extraction; parent story
/// us-01-quote-line-extraction AC-1) that `POST /api/quotes` is actually mapped in
/// <c>Program.cs</c> and enforces its request-shape guard clauses — mirrors
/// <see cref="DocumentUploadEndpointTests"/>'s own purpose and shape exactly. Only exercises
/// branches that return before any database/storage/AI-Gateway call is made, so this needs no
/// running Postgres, Azurite, or Foundry endpoint — <c>appsettings.Development.json</c>'s own
/// syntactically-valid `ConnectionStrings:Quotes` default (loaded automatically in the
/// `WebApplicationFactory`'s "Development" environment, the same way every other required
/// connection string this host now checks at startup already is) is enough to satisfy
/// <c>Program.cs</c>'s fail-fast startup check.
/// </summary>
public sealed class QuotesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public QuotesEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "quote.pdf" },
        };

        var response = await client.PostAsync("/api/quotes", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "quote.pdf" },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes") { Content = content };
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_file_field_returns_400()
    {
        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent
        {
            { new StringContent("not-a-file"), "note" },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes") { Content = content };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Non_multipart_body_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ----- Task E05/F02/US01/T01 (market-assessment) AC-3: GET /api/quotes/{id}/assessment -----
    //
    // Only exercises branches that return before any database/Benchmark-Service call is made (same
    // reasoning as this class's own doc comment for the POST tests above) — a real "quote found,
    // assessed against a real benchmark" proof lives in
    // Contigo.Quotes.Tests.MarketAssessmentServiceTests, against a real Postgres.

    [Fact]
    public async Task GetAssessment_missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/quotes/{Guid.NewGuid()}/assessment");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAssessment_invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/quotes/{Guid.NewGuid()}/assessment");
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAssessment_non_guid_quote_id_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/quotes/not-a-guid/assessment");
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ----- Task E05/F01/US02/T02 (sku-recalculate) AC-2/AC-3:
    // POST /api/quotes/{id}/assessment/recalculate -----
    //
    // Same reasoning as the GetAssessment_* tests above: only exercises branches that return
    // before any database/Benchmark-Service call is made. A real "correction resolves an unmatched
    // line, then a fresh assessment comes back" proof lives in
    // Contigo.Quotes.Tests.SkuMappingServiceTests, against a real Postgres.

    private static StringContent JsonBody(string json) =>
        new(json, System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Recalculate_missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/quotes/{Guid.NewGuid()}/assessment/recalculate", JsonBody("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Recalculate_invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/quotes/{Guid.NewGuid()}/assessment/recalculate")
        {
            Content = JsonBody("{}"),
        };
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Recalculate_non_guid_quote_id_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/quotes/not-a-guid/assessment/recalculate")
        {
            Content = JsonBody("{}"),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
