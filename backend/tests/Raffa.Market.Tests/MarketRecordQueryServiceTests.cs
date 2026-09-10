using System.Text.Json;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.Market.Infrastructure.Entities;
using Raffa.Market.Retrieval;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Market.Tests;

/// <summary>
/// Proves parent story us-01-market-intelligence's AC-5 ("returns one record with its provenance
/// label and updatedAt for the citation panel"): <see cref="MarketRecordQueryService.GetByIdAsync"/>
/// reads a seeded <c>market_record</c> row back out of a real Postgres+pgvector database and
/// reconstructs its <see cref="MarketDeal"/> payload and precomputed provenance label. The service's
/// own doc comment claims it is "directly unit-testable against a real Postgres+pgvector database
/// with no HTTP host in the loop -- mirrors
/// <c>Raffa.Documents.Contracts.Application.DocumentQueryService</c>'s identical split"; this
/// class is that test, following
/// <c>Raffa.Documents.Contracts.Tests.DocumentQueryServiceTests</c>'s own field-by-field assertion
/// style rather than one aggregate <c>Assert.Equal</c> against <see cref="MarketDeal"/> --
/// <see cref="MarketDeal.NegotiatedClauses"/> is typed <see cref="IReadOnlyList{T}"/>, which has no
/// structural equality, so the compiler-generated record equality would compare list references
/// (always different across a JSON serialize/deserialize round-trip) rather than contents, making a
/// whole-record comparison a false negative here.
/// </summary>
public sealed class MarketRecordQueryServiceTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(options, _postgres.GetConnectionString());
        await using var db = new MarketDbContext(options.Options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private MarketDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(options, _postgres.GetConnectionString());
        return new MarketDbContext(options.Options);
    }

    /// <summary>Directly seeds a <c>market_record</c> row from a <see cref="MarketDeal"/> --
    /// identical technique to <c>MarketFeedBenchmarkAdapterDbBackedTests.SeedRecordAsync</c>,
    /// deliberately bypassing <c>MarketIngestionService</c> (and therefore <c>IAiGateway</c>)
    /// entirely: <see cref="MarketRecordQueryService"/> only ever reads <c>market_record</c>, never
    /// <c>market_embedding</c>, so a test of it needs no embedding at all.</summary>
    private async Task SeedRecordAsync(MarketDeal deal)
    {
        await using var db = CreateDbContext();

        db.MarketRecords.Add(new MarketRecordEntity
        {
            RecordId = deal.RecordId,
            FeedVersion = "v1",
            Provider = deal.Provider,
            PayloadJson = JsonSerializer.Serialize(deal, JsonOptions),
            ProvenanceLabel = MarketProvenance.Label(deal),
            UpdatedAt = deal.UpdatedAt,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Existing_record_id_returns_its_deal_and_provenance_label()
    {
        var deal = SampleDeal.Create(
            recordId: "MKT-QRY-0001",
            supplier: "Salesforce",
            product: "Sales Cloud Enterprise",
            geography: "CH",
            currency: "CHF",
            unitPriceP25: 118m,
            unitPriceP50: 132m,
            unitPriceP75: 149m,
            negotiatedClauses:
            [
                new NegotiatedClause("Uplift cap", "4% annual"),
                new NegotiatedClause("Notice", "90 days"),
            ],
            updatedAt: new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero));
        await SeedRecordAsync(deal);

        await using var db = CreateDbContext();
        var queryService = new MarketRecordQueryService(db);

        // AC-5: "returns one record with its provenance label and updatedAt for the citation panel".
        var detail = await queryService.GetByIdAsync("MKT-QRY-0001");

        Assert.NotNull(detail);
        var returnedDeal = detail!.Deal;
        Assert.Equal(deal.RecordId, returnedDeal.RecordId);
        Assert.Equal(deal.Provider, returnedDeal.Provider);
        Assert.Equal(deal.Supplier, returnedDeal.Supplier);
        Assert.Equal(deal.Product, returnedDeal.Product);
        Assert.Equal(deal.Geography, returnedDeal.Geography);
        Assert.Equal(deal.Currency, returnedDeal.Currency);
        Assert.Equal(deal.UnitPriceP25, returnedDeal.UnitPriceP25);
        Assert.Equal(deal.UnitPriceP50, returnedDeal.UnitPriceP50);
        Assert.Equal(deal.UnitPriceP75, returnedDeal.UnitPriceP75);
        Assert.Equal(deal.NegotiatedClauses, returnedDeal.NegotiatedClauses);
        Assert.Equal(deal.UpdatedAt, returnedDeal.UpdatedAt);
        Assert.Equal(MarketProvenance.Label(deal), detail.ProvenanceLabel);
    }

    [Fact]
    public async Task Unknown_record_id_returns_null()
    {
        await using var db = CreateDbContext();
        var queryService = new MarketRecordQueryService(db);

        // The endpoint's own 404 signal (Raffa.Api.MarketEndpointExtensions.GetRecordAsync).
        var detail = await queryService.GetByIdAsync("MKT-QRY-DOES-NOT-EXIST");

        Assert.Null(detail);
    }
}
