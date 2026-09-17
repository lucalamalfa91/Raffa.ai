using System.Globalization;
using System.Net;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application.Pack;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// Task E28/F01/US01/T01 (NW-82, story us-01-priced-lines-parity): proves
/// <c>AskCopilotService.BuildRenewalStrategyPackAsync</c>/<c>BuildMarketComparePackAsync</c> now
/// resolve priced-line bands through the same async
/// <c>InsightsEndpointExtensions.ToPricedLines</c> overload + <c>BenchmarkKeyResolution</c> key
/// <c>GET /api/contracts/{id}/strategy</c> already uses (ADR-024 w17 clause 7, "one resolution per
/// screen"), rather than the sync, always-band-less overload Ask used to call.
///
/// <list type="bullet">
/// <item>AC-1: the pack composition calls the async overload with a <c>BenchmarkKeyResolution</c>
/// -resolved (supplier name, workspace country) key -- proved indirectly by AC-2 below: the *sync*
/// overload can never produce a non-empty <c>Values</c> list (every band stays unset by
/// construction), so a passing AC-2 assertion is only possible once the async path is wired.</item>
/// <item>AC-2: the renewal-strategy pack's opening/acceptable-range/walk-away numbers equal
/// <c>/strategy</c>'s own JSON for the same contract when a band exists.</item>
/// <item>AC-3: when the key resolver abstains (no workspace country seeded), both packs narrate
/// "insufficient market data", never a fabricated percentile, and still carry a citable
/// provenance-bearing entry rather than silently dropping the line.</item>
/// </list>
///
/// <see cref="AskCopilotService.BuildRenewalStrategyPackAsync"/>/
/// <see cref="AskCopilotService.BuildMarketComparePackAsync"/> are called directly (both made
/// <c>internal</c> by this task, <c>Raffa.Api.Tests</c>' own <c>InternalsVisibleTo</c> grant makes
/// them reachable -- same precedent as <c>AskCopilotServiceTests.ResolveTenantClauseLinks</c>)
/// rather than through a full <c>AskAsync</c>/HTTP round trip:
/// <c>Raffa.AiGateway.Fixtures.FixtureAiGateway.AnswerFromPack</c> only ever echoes a pack's first
/// five items into the HTTP reply, and the renewal-strategy pack's own fixed item order
/// (when-you-must-move, then up to seven levers *per priced line*, then one target per line) never
/// puts a target that early -- an HTTP-round-trip assertion could not observe AC-2 without first
/// depending on that unrelated cap.
/// </summary>
public sealed class AskPricedLinesParityTests : IClassFixture<RaffaApiFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AskPricedLinesParityTests(RaffaApiFactory factory)
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
    public async Task Renewal_strategy_pack_targets_equal_the_strategy_endpoint_when_a_band_exists()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var salesforceId = EntityId.New();

        var stubResult = new BenchmarkResult(
            Distribution: new BenchmarkDistribution(P25: 1000m, P50: 1200m, P75: 1500m),
            Metric: "per seat / year",
            Currency: "USD",
            Confidence: 0.9,
            Source: "stub-fixture",
            UpdatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ComparisonDimensions: [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Geography],
            SampleSize: 15);

        var factory = BuildFactory(salesforceId, "Salesforce", stubResult);
        await SeedWorkspaceCountryAsync(factory, tenantId, "GB");

        var contract = PortfolioEndpointTests.NewContract(
            tenantId, now, salesforceId, autoRenewal: true,
            endDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(90));
        contract.CancellationDeadline = DateOnly.FromDateTime(now.UtcDateTime).AddDays(30);
        contract.RenewalTermMonths = 12;
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));
        await SeedLineItemAsync(factory, tenantId, contract.Id);

        // The oracle AC-2 compares against: GET /api/contracts/{id}/strategy, already wired to the
        // async ToPricedLines + BenchmarkKeyResolution path (task E21/F03/US01/T01, NW-62).
        var client = factory.CreateClient();
        using var strategyRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/contracts/{contract.Id.Value}/strategy");
        strategyRequest.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        var strategyResponse = await client.SendAsync(strategyRequest);
        Assert.Equal(HttpStatusCode.OK, strategyResponse.StatusCode);

        using var strategyBody = JsonDocument.Parse(await strategyResponse.Content.ReadAsStringAsync());
        var strategyTarget = Assert.Single(strategyBody.RootElement.GetProperty("targets").EnumerateArray());
        var expectedOpening = strategyTarget.GetProperty("openingTarget").GetDecimal();
        var expectedRangeLow = strategyTarget.GetProperty("acceptableRangeLow").GetDecimal();
        var expectedRangeHigh = strategyTarget.GetProperty("acceptableRangeHigh").GetDecimal();
        var expectedWalkAway = strategyTarget.GetProperty("walkAwayThreshold").GetDecimal();

        var targetItem = await BuildSingleRenewalTargetAsync(factory, tenantId);

        Assert.Equal(expectedOpening, GetAmount(targetItem, "openingTarget"));
        Assert.Equal(expectedRangeLow, GetAmount(targetItem, "acceptableRangeLow"));
        Assert.Equal(expectedRangeHigh, GetAmount(targetItem, "acceptableRangeHigh"));
        Assert.Equal(expectedWalkAway, GetAmount(targetItem, "walkAwayThreshold"));

        // AC-3's positive half, restated: a real band never narrates "insufficient market data".
        Assert.DoesNotContain("insufficient market data", targetItem.Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("representative", targetItem.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renewal_strategy_pack_target_states_insufficient_market_data_when_the_key_resolver_abstains()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var salesforceId = EntityId.New();

        var factory = BuildFactory(salesforceId, "Salesforce", stubResult: null);

        // No WorkspaceTenant row -> BenchmarkKeyResolution.ResolveAsync returns Incomplete -> no
        // adapter call, every line's band stays unset (mirrors ContractStrategyEndpointTests' own
        // When_key_resolver_abstains scenario for the /strategy endpoint).
        var contract = PortfolioEndpointTests.NewContract(
            tenantId, now, salesforceId, autoRenewal: true,
            endDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(90));
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));
        await SeedLineItemAsync(factory, tenantId, contract.Id);

        var targetItem = await BuildSingleRenewalTargetAsync(factory, tenantId);

        Assert.Contains("insufficient market data", targetItem.Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("representative", targetItem.Snippet, StringComparison.Ordinal);
        Assert.Empty(targetItem.Values);
    }

    [Fact]
    public async Task Market_compare_pack_carries_a_representative_band_matching_the_stub()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var salesforceId = EntityId.New();

        var stubResult = new BenchmarkResult(
            Distribution: new BenchmarkDistribution(P25: 1000m, P50: 1200m, P75: 1500m),
            Metric: "per seat / year",
            Currency: "USD",
            Confidence: 0.9,
            Source: "stub-fixture",
            UpdatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ComparisonDimensions: [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Geography],
            SampleSize: 15);

        var factory = BuildFactory(salesforceId, "Salesforce", stubResult);
        await SeedWorkspaceCountryAsync(factory, tenantId, "GB");

        var contract = PortfolioEndpointTests.NewContract(tenantId, now, salesforceId);
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));
        await SeedLineItemAsync(factory, tenantId, contract.Id);

        var pack = await BuildMarketComparePackAsync(factory, tenantId, contract.Id);
        var marketItem = Assert.Single(pack, item => item.Corpus == PackCorpus.Market);

        Assert.DoesNotContain("insufficient market data", marketItem.Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("representative", marketItem.Provenance, StringComparison.Ordinal);
        Assert.Contains("stub-fixture", marketItem.Provenance, StringComparison.Ordinal);
        Assert.Contains("n=15", marketItem.Provenance, StringComparison.Ordinal);
        Assert.Equal(1000m, GetAmount(marketItem, "p25"));
        Assert.Equal(1200m, GetAmount(marketItem, "p50"));
        Assert.Equal(1500m, GetAmount(marketItem, "p75"));

        // AC-1/AC-2 restated for this pack: the "current unit price" tenant fact still lands next to
        // the market band, unconditionally, exactly as before this task.
        Assert.Contains(pack, item => item.Corpus == PackCorpus.Tenant && item.Snippet.Contains("Current unit price", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Market_compare_pack_states_insufficient_market_data_for_a_line_with_no_band()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var salesforceId = EntityId.New();

        var factory = BuildFactory(salesforceId, "Salesforce", stubResult: null);

        // No WorkspaceTenant row -> BenchmarkKeyResolution.ResolveAsync returns Incomplete.
        var contract = PortfolioEndpointTests.NewContract(tenantId, now, salesforceId);
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));
        await SeedLineItemAsync(factory, tenantId, contract.Id);

        var pack = await BuildMarketComparePackAsync(factory, tenantId, contract.Id);

        // AC-3: previously a missing/failed band silently dropped the market card altogether (the
        // adapter-failure and insufficient-data branches both just `continue`d past it) -- this task
        // makes the abstain an entry, never an omission, so a client/model always has something
        // citable to say "insufficient market data" with.
        var marketItem = Assert.Single(pack, item => item.Corpus == PackCorpus.Market);
        Assert.Contains("insufficient market data", marketItem.Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(marketItem.Values);

        // The tenant "current unit price" fact still lands -- a missing market band is never a
        // reason to hide the tenant's own validated data.
        Assert.Contains(pack, item => item.Corpus == PackCorpus.Tenant && item.Snippet.Contains("Current unit price", StringComparison.Ordinal));
    }

    // ----- Shared scaffolding -----

    private WebApplicationFactory<Program> BuildFactory(
        EntityId supplierId, string supplierName, BenchmarkResult? stubResult) =>
        PortfolioEndpointTests
            .WithSupplierNames(_factory, new Dictionary<EntityId, string> { [supplierId] = supplierName })
            .WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBenchmarkService>();
                services.AddSingleton<IBenchmarkService>(new StubBenchmarkService(stubResult));
            }));

    /// <summary>Resolves <see cref="AskCopilotService"/> and its own tenant scope from
    /// <paramref name="factory"/>'s container, fetches the one seeded contract's
    /// <see cref="PortfolioListItem"/>, calls <see cref="AskCopilotService.BuildRenewalStrategyPackAsync"/>
    /// directly, and returns the single <c>calc:target[...]</c> item -- see this type's own doc
    /// comment for why this bypasses <c>AskAsync</c>/HTTP.</summary>
    private static async Task<PackItem> BuildSingleRenewalTargetAsync(WebApplicationFactory<Program> factory, TenantId tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var portfolioQueryService = scope.ServiceProvider.GetRequiredService<PortfolioQueryService>();
        var askCopilotService = scope.ServiceProvider.GetRequiredService<AskCopilotService>();

        using var tenantScope = tenantContext.BeginScope(tenantId);
        var portfolio = await portfolioQueryService.GetPortfolioAsync(
            tenantId, PortfolioFilter.None, new PortfolioPageRequest(1, PortfolioPageRequest.MaxPageSize), CancellationToken.None);
        var namedContractItem = Assert.Single(portfolio.Items);

        var pack = await askCopilotService.BuildRenewalStrategyPackAsync(namedContractItem, CancellationToken.None);
        return Assert.Single(pack, item => item.CitationKey.StartsWith("calc:target[", StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyList<PackItem>> BuildMarketComparePackAsync(
        WebApplicationFactory<Program> factory, TenantId tenantId, EntityId contractId)
    {
        using var scope = factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var portfolioQueryService = scope.ServiceProvider.GetRequiredService<PortfolioQueryService>();
        var askCopilotService = scope.ServiceProvider.GetRequiredService<AskCopilotService>();

        using var tenantScope = tenantContext.BeginScope(tenantId);
        var portfolio = await portfolioQueryService.GetPortfolioAsync(
            tenantId, PortfolioFilter.None, new PortfolioPageRequest(1, PortfolioPageRequest.MaxPageSize), CancellationToken.None);
        var namedContractItem = Assert.Single(portfolio.Items, item => item.ContractId == contractId.Value);

        return await askCopilotService.BuildMarketComparePackAsync(namedContractItem, CancellationToken.None);
    }

    private static decimal GetAmount(PackItem item, string key) =>
        decimal.Parse(item.Values.Single(v => v.Key == key).Value, CultureInfo.InvariantCulture);

    private static async Task SeedWorkspaceCountryAsync(
        WebApplicationFactory<Program> factory, TenantId tenantId, string country)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        db.Workspaces.Add(new WorkspaceTenant
        {
            Id = new EntityId(tenantId.Value),
            TenantId = tenantId,
            Name = "Test Workspace",
            CreatedAt = DateTimeOffset.UtcNow,
            Country = country,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedLineItemAsync(
        WebApplicationFactory<Program> factory, TenantId tenantId, EntityId contractId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        db.ContractLineItems.Add(new ContractLineItem
        {
            TenantId = tenantId,
            ContractId = contractId,
            Sku = "SKU-1",
            Description = "Sales Cloud Enterprise",
            Quantity = 100m,
            Unit = "seats",
            UnitPrice = 2300m,
            BillingPeriod = "Annual",
            Confidence = 0.9,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private sealed class StubBenchmarkService(BenchmarkResult? result) : IBenchmarkService
    {
        public Task<Result<BenchmarkResult>> GetBenchmarkAsync(
            BenchmarkQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(result is not null
                ? Result<BenchmarkResult>.Success(result)
                : Result<BenchmarkResult>.Failure("stub: no result configured"));
    }
}
