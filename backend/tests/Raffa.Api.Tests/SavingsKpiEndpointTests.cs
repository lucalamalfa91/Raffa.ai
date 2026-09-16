using Raffa.Api.Tests.TestSupport;
using System.Net;
using System.Text.Json;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Savings.Domain;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E04/F03/US01/T01 (savings-kpis) and task E20/F01/US01/T01 (NW-72)
/// that `GET /api/savings/kpis` is mapped, enforces its request-shape guard clause, and emits
/// verified money as <c>savingsRealized[].amount</c> while <c>savingsIdentified</c> stays a
/// range. Guard-clause cases return before either query service is called, so they need no
/// running Postgres. The success path swaps Documents/Contracts and Savings onto the EF
/// InMemory provider — the same "HTTP round trip without a Testcontainer" shape
/// <see cref="ChatEndpointTests"/> already uses; tenant isolation of the realized rows is
/// application-level here (the RLS backstop stays in <c>Raffa.Savings.Tests</c>).
/// </summary>
public sealed class SavingsKpiEndpointTests : IClassFixture<RaffaApiFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SavingsKpiEndpointTests(RaffaApiFactory factory)
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

    [Fact]
    public async Task SavingsRealized_carries_amount_and_count_and_a_second_tenant_sees_none()
    {
        var factory = WithInMemoryStores();
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var now = DateTimeOffset.UtcNow;
        var opportunityId = EntityId.New();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();
            db.SavingsOpportunities.Add(new SavingsOpportunity
            {
                TenantId = tenantA,
                Type = "price-renegotiation",
                CurrentSpend = 50_000m,
                Currency = "CHF",
                EstimatedSavingsLow = 80_000m,
                EstimatedSavingsHigh = 120_000m,
                Confidence = 0.82,
                Status = SavingsOpportunityStatus.Identified,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.RealizedSavingsRecords.Add(new RealizedSavings
            {
                TenantId = tenantA,
                SavingsOpportunityId = opportunityId,
                Amount = 85_000m,
                Currency = "CHF",
                RealizedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();

        using var requestA = new HttpRequestMessage(HttpMethod.Get, "/api/savings/kpis");
        requestA.Headers.Add("X-Tenant-Id", tenantA.Value.ToString());
        var responseA = await client.SendAsync(requestA);
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);

        using var bodyA = JsonDocument.Parse(await responseA.Content.ReadAsStringAsync());
        var realized = Assert.Single(bodyA.RootElement.GetProperty("savingsRealized").EnumerateArray());
        Assert.Equal("CHF", realized.GetProperty("currency").GetString());
        Assert.Equal(85_000m, realized.GetProperty("amount").GetDecimal());
        Assert.Equal(1, realized.GetProperty("count").GetInt32());
        Assert.False(realized.TryGetProperty("low", out _));
        Assert.False(realized.TryGetProperty("high", out _));
        Assert.False(realized.TryGetProperty("averageConfidence", out _));

        var identified = Assert.Single(bodyA.RootElement.GetProperty("savingsIdentified").EnumerateArray());
        Assert.Equal("CHF", identified.GetProperty("currency").GetString());
        Assert.Equal(80_000m, identified.GetProperty("low").GetDecimal());
        Assert.Equal(120_000m, identified.GetProperty("high").GetDecimal());

        using var requestB = new HttpRequestMessage(HttpMethod.Get, "/api/savings/kpis");
        requestB.Headers.Add("X-Tenant-Id", tenantB.Value.ToString());
        var responseB = await client.SendAsync(requestB);
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);

        using var bodyB = JsonDocument.Parse(await responseB.Content.ReadAsStringAsync());
        Assert.Empty(bodyB.RootElement.GetProperty("savingsRealized").EnumerateArray());
        Assert.Empty(bodyB.RootElement.GetProperty("savingsIdentified").EnumerateArray());
    }

    private WebApplicationFactory<Program> WithInMemoryStores()
    {
        var documentsDb = $"documents-contracts-{Guid.NewGuid()}";
        var savingsDb = $"savings-{Guid.NewGuid()}";

        return _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<DocumentsContractsDbContext>>();
            services.RemoveAll<DocumentsContractsDbContext>();
            services.AddDbContext<DocumentsContractsDbContext>(o => o
                .UseInMemoryDatabase(documentsDb)
                .UseInternalServiceProvider(InMemoryAskEngineFactory.InMemoryProviderServices));

            services.RemoveAll<DbContextOptions<SavingsDbContext>>();
            services.RemoveAll<SavingsDbContext>();
            services.AddDbContext<SavingsDbContext>(o => o
                .UseInMemoryDatabase(savingsDb)
                .UseInternalServiceProvider(InMemoryAskEngineFactory.InMemoryProviderServices));
        }));
    }
}
