using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E05/F03/US02/T01 (negotiation-outcome; parent story
/// us-02-outcome-capture AC-1) that `POST /api/negotiations/outcomes` is actually mapped in
/// <c>Program.cs</c> and enforces its request-shape guard clause — mirrors
/// <see cref="SavingsEndpointTests"/>/<see cref="QuotesEndpointTests"/>'s own purpose and shape.
/// Only exercises the branch that returns before any database call is made (the tenant-header
/// check, which runs before <c>Raffa.Quotes.Application.Outcome.NegotiationOutcomeService</c> is
/// ever called), so this needs no running Postgres — <c>appsettings.Development.json</c>'s own
/// syntactically-valid `ConnectionStrings:Quotes` default (loaded automatically in the
/// `WebApplicationFactory`'s "Development" environment, same as <see cref="QuotesEndpointTests"/>)
/// is enough to satisfy <c>Program.cs</c>'s fail-fast startup check. The success path (real
/// persistence, real audit entry, real "quote not found"/validation failures) is proven at the
/// Testcontainers level instead — <c>Raffa.Quotes.Tests.NegotiationOutcomeServiceTests</c> — per
/// this task's own "Tests required" level (unit, no database).
/// </summary>
public sealed class NegotiationsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public NegotiationsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/negotiations/outcomes")
        {
            Content = JsonContent(ValidBody()),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/negotiations/outcomes")
        {
            Content = JsonContent(ValidBody()),
        };
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // spec §12.2's own worked example — a syntactically valid body so the tenant-header guard
    // clause, not JSON model binding, is what the two tests above actually exercise.
    private static object ValidBody() => new
    {
        quoteId = Guid.NewGuid(),
        originalQuoteTotal = 520_000m,
        targetPrice = 420_000m,
        finalPrice = 435_000m,
        negotiationDurationDays = 24,
        leversUsed = new[] { "Term", "QuarterEnd" },
    };

    private static System.Net.Http.Json.JsonContent JsonContent(object value) =>
        System.Net.Http.Json.JsonContent.Create(value);
}
