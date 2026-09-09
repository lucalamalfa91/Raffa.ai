using System.Net;
using System.Text.Json;
using Contigo.Benchmark;
using Contigo.Benchmark.Adapters;
using Contigo.Benchmark.Contracts;
using Contigo.Benchmark.Fixtures;
using Contigo.Savings.Application;
using Contigo.Savings.Domain;
using Contigo.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Contigo.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E04/F04/US01/T01 (r3-integration) and its parent story
/// us-01-final-integration: AC-1 ("Matched contracts show current price + P25/P50/P75 +
/// percentile/target/saving/confidence/provenance"), AC-2 ("SavingsOpportunity lifecycle works
/// end-to-end on `demo`") and AC-3 ("No paid benchmark provider is called") — driven against the
/// real, composed <c>Contigo.Api</c> host (see <see cref="R3IntegrationFixture"/>) and a real,
/// migrated Postgres+pgvector+RLS database, the same "one real host" shape
/// <see cref="R0EndToEndTests"/>/<see cref="R1EndToEndTests"/>/<see cref="R2EndToEndTests"/> already
/// established for R0-R2. Reuses <see cref="R1EndToEndTests"/>'s <c>GetAsync</c>/<c>PostAsync</c>/
/// <c>PatchAsync</c>/<c>ParseAsync</c> helpers rather than duplicating them — the same cross-class
/// reuse <see cref="R2EndToEndTests"/>/<see cref="R2CrossTenantIsolationTests"/> already established
/// (they are generic HTTP plumbing, not R1-specific).
///
/// <para>
/// <b>Honest scope note (how AC-1/AC-2 are driven):</b> this task's own wave-spec <c>depends_on</c>
/// (benchmark-registry, fixture-confidence, savings-provenance, realized-savings, savings-list) never
/// included a task that maps a real, extracted <c>Contigo.Documents.Contracts.Domain.Contract</c>'s
/// line items into a <see cref="BenchmarkQuery"/> — no supplier-name or geography field exists on
/// <c>Contract</c> today, and `backend/README.md`'s own "Savings Intelligence" section already
/// documents that composition as a still-open, later follow-up ("wiring lands with the first real
/// caller"). Inventing that mapping here would fabricate contract fields this codebase does not
/// actually have (Appendix C rule 10), so this test instead builds a <see cref="BenchmarkQuery"/> by
/// hand that matches one of <c>FixtureBenchmarkAdapter</c>'s own catalog rows — a "matched contract"
/// stand-in identical in shape to what a real caller would eventually build — and resolves
/// <see cref="IBenchmarkService"/>/<see cref="SavingsOpportunityService"/> directly from the real
/// host's own container (<see cref="R3IntegrationFixture"/>'s own <c>Services</c>, inherited from
/// <c>WebApplicationFactory&lt;Program&gt;</c>), the same "no dedicated route exists yet, exercise
/// the service the host resolves" convention
/// <see cref="R2EndToEndTests"/>/<see cref="R2CrossTenantIsolationTests"/> already established for
/// <c>Contigo.Renewals.Application.RenewalActionService.GetActionAsync</c>. This is also this task's
/// own production-code change made provable at all: before task E04/F04/US01/T01,
/// <c>Contigo.Savings.Infrastructure.ServiceCollectionExtensions.AddSavingsModule</c> never called
/// <c>Contigo.Benchmark.ServiceCollectionExtensions.AddBenchmarkModule</c>, so
/// <see cref="IBenchmarkService"/> could not be resolved from any host, including this one.
/// </para>
/// </summary>
public sealed class R3EndToEndTests : IClassFixture<R3IntegrationFixture>
{
    private readonly R3IntegrationFixture _fixture;

    public R3EndToEndTests(R3IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Benchmark_comparison_reports_percentile_target_saving_confidence_and_provenance_for_a_confident_match()
    {
        using var scope = _fixture.Services.CreateScope();

        // AC-3 "No paid benchmark provider is called": every Contigo.Benchmark.Adapters
        // .IBenchmarkProviderAdapter registered anywhere in this real, composed host is either the
        // fixture (ADR-001) or task E13/F06/US01/T01's own market-feed mock (R-MKT-02, registered
        // by that task's AddMarketModule() — see backend/README.md "Market Intelligence") — there is
        // no paid-provider adapter in this solution at all to accidentally register or dial, and
        // this is the structural proof of that, not an assumption.
        var adapters = scope.ServiceProvider.GetServices<IBenchmarkProviderAdapter>().ToList();
        Assert.Equal(2, adapters.Count);
        Assert.All(adapters, a => Assert.Contains(a.Name, new[] { "fixture", "market-feed" }));
        var adapter = adapters.OfType<FixtureBenchmarkAdapter>().Single();
        Assert.Equal("fixture", adapter.Name);

        // benchmark-registry (task E04/F01/US01/T02): IBenchmarkService itself still resolves to
        // the registry, but task E13/F06/US01/T01's AddMarketModule() now makes "market-feed" its
        // configured *active* adapter (backend/README.md "Market Intelligence") — this story's own
        // definition of success is specifically "savings from **fixture** benchmark", so this test
        // (and its two siblings below) call the fixture adapter directly rather than through
        // IBenchmarkService, exercising the identical deterministic catalog data regardless of
        // which adapter production traffic is routed to (AddMarketModule's own doc comment:
        // "FixtureBenchmarkAdapter stays registered, still directly testable").
        //
        // Reconciled with R4IntegrationFixture's own divergent-looking fix for the identical root
        // cause (that type's own ConfigureWebHost doc comment, right above its
        // BenchmarkAdapterOptions.ActiveAdapter UseSetting): R4EndToEndTests drives its assessment
        // scenario through a real HTTP endpoint whose handler resolves IBenchmarkService from the
        // host's own container — it cannot reach into the pipeline the way this test does, so R4
        // has to pin the *host's* active-adapter config instead. This test suite has no such
        // endpoint to drive (see this type's own doc comment, "no dedicated route exists yet") and
        // resolves the adapter straight from the container, so pinning host config here would be
        // unnecessary indirection for a service this test already holds a direct reference to. Two
        // different mechanisms, one deliberate reason: whether the test reaches the adapter through
        // HTTP or in-process.
        var benchmarkService = scope.ServiceProvider.GetRequiredService<IBenchmarkService>();
        Assert.IsType<BenchmarkAdapterRegistry>(benchmarkService);

        // "Matched contract" stand-in (see type doc comment): Salesforce Sales Cloud Enterprise,
        // 100 seats, 12-month US/USD — matches FixtureBenchmarkAdapter's own catalog row exactly
        // (P25/P50/P75 = 1500/1800/2100 per seat/year, sample size 512).
        var query = new BenchmarkQuery(
            Supplier: "Salesforce",
            Product: "Sales Cloud Enterprise",
            Sku: null,
            Geography: "US",
            Quantity: 100m,
            Term: "12 months",
            Currency: "USD",
            PurchaseDate: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));

        var benchmarkOutcome = await adapter.GetBenchmarkAsync(query);
        Assert.True(benchmarkOutcome.IsSuccess);
        var benchmark = benchmarkOutcome.Value;

        Assert.True(benchmark.HasSufficientData);
        Assert.Equal(1500m, benchmark.Distribution!.P25);
        Assert.Equal(1800m, benchmark.Distribution.P50);
        Assert.Equal(2100m, benchmark.Distribution.P75);
        Assert.Equal("fixture", benchmark.Source);
        Assert.Equal(1.0, benchmark.Confidence);
        Assert.Equal(512, benchmark.SampleSize);
        Assert.DoesNotContain(BenchmarkComparisonDimension.Sku, benchmark.ComparisonDimensions);
        // AC-3 (us-01-benchmark-interface): matching never uses supplier name alone.
        Assert.True(benchmark.ComparisonDimensions.Count > 1);
        Assert.Contains(BenchmarkComparisonDimension.Supplier, benchmark.ComparisonDimensions);

        // savings-normalization + savings-provenance (AC-1 "current price + P25/P50/P75 +
        // percentile/target/saving/confidence/provenance"): a 100-seat contract paying 1950/seat/
        // year (195,000 total) sits exactly halfway between P50 and P75.
        const decimal currentTotalCost = 195_000m;
        var comparison = new PriceNormalizationCalculator().Compare(
            new PriceComparisonRequest(query, currentTotalCost, benchmark));

        Assert.Equal(PriceComparisonStatus.Compared, comparison.Status);
        Assert.Equal(1950m, comparison.NormalizedUnitPrice);
        Assert.Equal(62.5m, comparison.PercentileRank);
        Assert.Equal(1500m, comparison.RecommendedTargetLow);
        Assert.Equal(1800m, comparison.RecommendedTargetHigh);
        Assert.Equal(150m, comparison.SavingsRangeLow);
        Assert.Equal(450m, comparison.SavingsRangeHigh);
        Assert.Equal(15_000m, comparison.TotalSavingsRangeLow);
        Assert.Equal(45_000m, comparison.TotalSavingsRangeHigh);

        // savings-provenance (task E04/F02/US01/T02): confidence + provenance on the comparison,
        // never a bare decimal.
        var provenance = comparison.Provenance;
        Assert.Equal(SavingsConfidenceLevel.High, provenance.ConfidenceLevel);
        Assert.Equal(1.0, provenance.ConfidenceScore);
        Assert.Equal("fixture", provenance.Source);
        Assert.Equal(512, provenance.SampleSize);
    }

    [Fact]
    public async Task Benchmark_comparison_honestly_abstains_with_provenance_when_market_data_is_too_thin()
    {
        using var scope = _fixture.Services.CreateScope();

        // Calls the fixture adapter directly, not through IBenchmarkService — see the sibling test
        // above for why (task E13/F06/US01/T01's AddMarketModule() made "market-feed" the
        // container's active adapter; this test needs FixtureBenchmarkAdapter's own Notion catalog
        // row specifically).
        var adapter = scope.ServiceProvider.GetServices<IBenchmarkProviderAdapter>()
            .OfType<FixtureBenchmarkAdapter>().Single();

        // fixture-confidence (task E04/F01/US02/T02): Notion's own catalog row clears every
        // baseline dimension (supplier/product/geography/currency/term/quantity/purchase-date) but
        // its sample size (4) is below FixtureBenchmarkAdapter.MinimumViableSampleSize (10) —
        // dimensionally strong, statistically too thin to trust with a distribution.
        var query = new BenchmarkQuery(
            Supplier: "Notion",
            Product: "Enterprise Plan",
            Sku: null,
            Geography: "US",
            Quantity: 50m,
            Term: "12 months",
            Currency: "USD",
            PurchaseDate: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));

        var benchmarkOutcome = await adapter.GetBenchmarkAsync(query);
        Assert.True(benchmarkOutcome.IsSuccess);
        var benchmark = benchmarkOutcome.Value;

        // ADR-001 / spec §10.4: an explicit "insufficient market data" outcome, never a
        // precise-looking number from a comparable this thin — but still real provenance (source,
        // sample size, updated-at), never a bare failure.
        Assert.False(benchmark.HasSufficientData);
        Assert.Null(benchmark.Distribution);
        Assert.Equal("fixture", benchmark.Source);
        Assert.Equal(4, benchmark.SampleSize);
        Assert.Equal(
            new[] { BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Product },
            benchmark.ComparisonDimensions.ToArray());

        var comparison = new PriceNormalizationCalculator().Compare(
            new PriceComparisonRequest(Query: query, CurrentTotalCost: 10_500m, Benchmark: benchmark));

        // AC-1's honest half: the normalized unit price is still reported (it does not depend on
        // the benchmark), but percentile/target/savings are not fabricated from data that cannot
        // support them.
        Assert.Equal(PriceComparisonStatus.InsufficientBenchmarkData, comparison.Status);
        Assert.Equal(210m, comparison.NormalizedUnitPrice);
        Assert.Null(comparison.PercentileRank);
        Assert.Null(comparison.RecommendedTargetLow);
        Assert.Null(comparison.TotalSavingsRangeLow);

        // Confidence + provenance are still shown — never withheld just because the comparison
        // itself abstained (spec §11.3 benchmark-trust rule).
        var provenance = comparison.Provenance;
        Assert.Equal(SavingsConfidenceLevel.Low, provenance.ConfidenceLevel);
        Assert.Equal("fixture", provenance.Source);
        Assert.Equal(4, provenance.SampleSize);
    }

    [Fact]
    public async Task Savings_opportunity_lifecycle_identify_own_and_realize_works_end_to_end_through_the_real_host()
    {
        var client = _fixture.CreateClient();
        var tenantId = TenantId.New();

        // Re-derive the same confident comparison as the first test above, then identify a
        // SavingsOpportunity from it — SavingsOpportunityService.CreateAsync has no dedicated HTTP
        // route yet (CreateSavingsOpportunityRequest's own doc comment), so this test exercises it
        // directly via the real host's own container, the same convention this type's doc comment
        // describes.
        var query = new BenchmarkQuery(
            "Salesforce", "Sales Cloud Enterprise", null, "US", 100m, "12 months", "USD",
            DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));

        PriceComparisonResult comparison;
        using (var setupScope = _fixture.Services.CreateScope())
        {
            // Fixture adapter directly, not IBenchmarkService — see the first test above for why.
            var adapter = setupScope.ServiceProvider.GetServices<IBenchmarkProviderAdapter>()
                .OfType<FixtureBenchmarkAdapter>().Single();
            var benchmark = (await adapter.GetBenchmarkAsync(query)).Value;
            comparison = new PriceNormalizationCalculator().Compare(
                new PriceComparisonRequest(Query: query, CurrentTotalCost: 195_000m, Benchmark: benchmark));
        }

        Assert.Equal(PriceComparisonStatus.Compared, comparison.Status);

        EntityId opportunityId;
        using (var createScope = _fixture.Services.CreateScope())
        {
            var savingsOpportunityService = createScope.ServiceProvider.GetRequiredService<SavingsOpportunityService>();
            var created = await savingsOpportunityService.CreateAsync(
                tenantId,
                new CreateSavingsOpportunityRequest(
                    SupplierId: EntityId.New(),
                    ContractId: EntityId.New(),
                    Type: "benchmark-price-comparison",
                    CurrentSpend: 195_000m,
                    Currency: "USD",
                    EstimatedSavingsLow: comparison.TotalSavingsRangeLow!.Value,
                    EstimatedSavingsHigh: comparison.TotalSavingsRangeHigh!.Value,
                    Confidence: comparison.Benchmark.Confidence));

            Assert.True(created.IsSuccess);
            Assert.Equal(SavingsOpportunityStatus.Identified, created.Value.Status);
            Assert.Equal(SavingsConfidenceLevel.High, created.Value.ConfidenceLevel);
            opportunityId = created.Value.Id;
        }

        // ----- "owned": PATCH /api/savings/{id} assigns an owner and moves it InProgress -----

        var ownResponse = await R1EndToEndTests.PatchAsync(
            client, $"/api/savings/{opportunityId.Value}", tenantId.Value,
            new { owner = "procurement@acme.example", status = "InProgress" });
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        var ownBody = await R1EndToEndTests.ParseAsync(ownResponse);
        Assert.Equal("procurement@acme.example", ownBody.GetProperty("owner").GetString());
        Assert.Equal("InProgress", ownBody.GetProperty("status").GetString());

        // ----- GET /api/savings: savings-list (confidence tier, never a bare decimal) -----

        var listResponse = await R1EndToEndTests.GetAsync(client, "/api/savings", tenantId.Value);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listBody = await R1EndToEndTests.ParseAsync(listResponse);
        var listedItem = Assert.Single(listBody.GetProperty("items").EnumerateArray());
        Assert.Equal(opportunityId.Value, listedItem.GetProperty("id").GetGuid());
        Assert.Equal("High", listedItem.GetProperty("confidenceLevel").GetString());
        Assert.Equal(15_000m, listedItem.GetProperty("estimatedSavingsLow").GetDecimal());
        Assert.Equal(45_000m, listedItem.GetProperty("estimatedSavingsHigh").GetDecimal());

        // ----- "marked realized": PATCH .../{id} with realizedAmount (realized-savings) -----

        var realizeResponse = await R1EndToEndTests.PatchAsync(
            client, $"/api/savings/{opportunityId.Value}", tenantId.Value,
            new { realizedAmount = 20_000m });
        Assert.Equal(HttpStatusCode.OK, realizeResponse.StatusCode);
        var realizeBody = await R1EndToEndTests.ParseAsync(realizeResponse);
        Assert.Equal("Realized", realizeBody.GetProperty("status").GetString());
        Assert.Equal(20_000m, realizeBody.GetProperty("realizedAmount").GetDecimal());
        // Owner set by the earlier PATCH survives an unrelated later partial update.
        Assert.Equal("procurement@acme.example", realizeBody.GetProperty("owner").GetString());

        // ----- GET /api/savings/kpis: the KPI rollup reflects the now-Realized opportunity -----

        var kpisResponse = await R1EndToEndTests.GetAsync(client, "/api/savings/kpis", tenantId.Value);
        Assert.Equal(HttpStatusCode.OK, kpisResponse.StatusCode);
        var kpisBody = await R1EndToEndTests.ParseAsync(kpisResponse);

        Assert.Empty(kpisBody.GetProperty("savingsIdentified").EnumerateArray());
        Assert.Empty(kpisBody.GetProperty("savingsInProgress").EnumerateArray());
        var realizedBucket = Assert.Single(kpisBody.GetProperty("savingsRealized").EnumerateArray());
        Assert.Equal("USD", realizedBucket.GetProperty("currency").GetString());
        Assert.Equal(15_000m, realizedBucket.GetProperty("low").GetDecimal());
        Assert.Equal(45_000m, realizedBucket.GetProperty("high").GetDecimal());
        Assert.Equal(1, realizedBucket.GetProperty("count").GetInt32());
        Assert.Equal(1.0, realizedBucket.GetProperty("averageConfidence").GetDouble());
    }
}
