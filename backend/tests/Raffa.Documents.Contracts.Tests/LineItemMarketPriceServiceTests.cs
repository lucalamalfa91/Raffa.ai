using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Market;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// <see cref="LineItemMarketPriceService"/> against a real Postgres under the app role (RLS on):
/// extraction stores one comparison per line (match or no match), a read returns the stored
/// comparison and re-prices only what is missing or stale, and a matcher failure never costs the
/// caller the comparison already stored.
/// </summary>
public sealed class LineItemMarketPriceServiceTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_market_price_app";
    private const string AppRolePassword = "raffa_market_price_app_test_password";

    private static readonly DateTimeOffset T0 = new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new DocumentsContractsDbContext(adminOptions.Options))
        {
            await adminDb.Database.MigrateAsync();

            await adminDb.Database.ExecuteSqlRawAsync(
                $"""
                CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
                GRANT USAGE ON SCHEMA public TO {AppRoleName};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
                """);
        }

        _appConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private DocumentsContractsDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }

    private static MarketPriceMatch UnlimitedBand() => new(
        "MKT-SFDC-UK-UNL", "Sales Cloud Unlimited", "UK", "GBP", 12, 300m, 350m, 410m, 64,
        "representative market data · mock feed · updated 2026-07-01",
        new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

    private async Task<(EntityId ContractId, EntityId Unlimited, EntityId Support)> SeedAsync(
        TenantContext tenantContext, TenantId tenantId, EntityId supplierId)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "needs_review",
            Currency = "GBP",
            AutoRenewal = true,
            RenewalTermMonths = 12,
            SupplierId = supplierId,
            CreatedAt = T0,
        };
        db.Contracts.Add(contract);

        var unlimited = new ContractLineItem
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            Description = "Sales Cloud Unlimited named users / licenses",
            Sku = "named users / licenses",
            Quantity = 640m,
            UnitPrice = 398m,
            CreatedAt = T0,
        };
        var support = new ContractLineItem
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            Description = "Premium Support annual support service",
            Sku = "enterprise package",
            Quantity = 1m,
            UnitPrice = 31_000m,
            CreatedAt = T0.AddSeconds(1),
        };
        db.ContractLineItems.AddRange(unlimited, support);
        await db.SaveChangesAsync();

        return (contract.Id, unlimited.Id, support.Id);
    }

    [Fact]
    public async Task Extraction_stores_a_match_and_a_checked_no_match_and_a_fresh_read_does_not_reprice()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var supplierId = EntityId.New();
        var (contractId, unlimited, support) = await SeedAsync(tenantContext, tenantId, supplierId);

        var clock = new MutableClock(T0);
        var matcher = new FakeMatcher(line => line.Description.Contains("Unlimited", StringComparison.Ordinal) ? UnlimitedBand() : null);
        var names = new FakeSupplierNames(supplierId, "Salesforce, Inc.");

        await using (var db = CreateAppContext(tenantContext))
        {
            var service = new LineItemMarketPriceService(db, tenantContext, clock, matcher, names);
            var priced = await service.PriceContractAsync(tenantId, contractId, CancellationToken.None);

            Assert.Equal(2, priced.Count);
            Assert.True(priced[unlimited].Matched);
            Assert.Equal(350m, priced[unlimited].UnitPriceP50);
            Assert.Equal("MKT-SFDC-UK-UNL", priced[unlimited].RecordId);
            Assert.Equal(64, priced[unlimited].SampleSize);
            Assert.Equal(MarketMatchKind.Exact, priced[unlimited].Kind);
            Assert.False(priced[support].Matched);
            Assert.Null(priced[support].UnitPriceP50);
            Assert.Equal(T0, priced[support].CheckedAt);
        }

        var context = Assert.Single(matcher.Contexts);
        Assert.Equal(new MarketPriceContext("Salesforce, Inc.", "GBP", 12), context);

        clock.Now = T0.AddHours(1);
        await using (var db = CreateAppContext(tenantContext))
        {
            var service = new LineItemMarketPriceService(db, tenantContext, clock, matcher, names);
            var current = await service.GetCurrentAsync(tenantId, contractId, CancellationToken.None);

            Assert.Equal(350m, current[unlimited].UnitPriceP50);
            Assert.False(current[support].Matched);
        }

        Assert.Single(matcher.Contexts);
    }

    [Fact]
    public async Task The_match_kind_is_stored_and_a_match_stored_before_kinds_existed_is_repriced_at_once()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var supplierId = EntityId.New();
        var (contractId, unlimited, support) = await SeedAsync(tenantContext, tenantId, supplierId);
        var names = new FakeSupplierNames(supplierId, "Salesforce");

        var similar = new FakeMatcher(line => line.Description.Contains("Unlimited", StringComparison.Ordinal)
            ? UnlimitedBand() with { Kind = MarketMatchKind.Similar }
            : null);
        await using (var db = CreateAppContext(tenantContext))
        {
            var priced = await new LineItemMarketPriceService(db, tenantContext, new MutableClock(T0), similar, names)
                .PriceContractAsync(tenantId, contractId, CancellationToken.None);
            Assert.Equal(MarketMatchKind.Similar, priced[unlimited].Kind);
            Assert.Null(priced[support].Kind);
        }

        // A row written before the kind was recorded: its band may be one product of a bundle line.
        using (tenantContext.BeginScope(tenantId))
        {
            await using var db = CreateAppContext(tenantContext);
            await db.ContractLineItemMarketPrices
                .Where(p => p.LineItemId == unlimited)
                .ExecuteUpdateAsync(p => p.SetProperty(r => r.MatchKind, (MarketMatchKind?)null));
        }

        var bundle = new FakeMatcher(_ => UnlimitedBand() with { Kind = MarketMatchKind.Bundle });
        await using (var db = CreateAppContext(tenantContext))
        {
            var current = await new LineItemMarketPriceService(db, tenantContext, new MutableClock(T0.AddMinutes(5)), bundle, names)
                .GetCurrentAsync(tenantId, contractId, CancellationToken.None);
            Assert.Equal(MarketMatchKind.Bundle, current[unlimited].Kind);
            Assert.Equal(T0, current[support].CheckedAt);
        }

        Assert.Equal(1, bundle.LinesPriced);
    }

    [Fact]
    public async Task The_buyer_type_is_the_contracts_yearly_value_or_else_its_lines_annual_costs()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var supplierId = EntityId.New();
        var (contractId, unlimited, support) = await SeedAsync(tenantContext, tenantId, supplierId);

        using (tenantContext.BeginScope(tenantId))
        {
            await using var db = CreateAppContext(tenantContext);
            await db.ContractLineItems.Where(l => l.Id == unlimited).ExecuteUpdateAsync(l => l.SetProperty(r => r.AnnualCost, 149_000m));
            await db.ContractLineItems.Where(l => l.Id == support).ExecuteUpdateAsync(l => l.SetProperty(r => r.AnnualCost, 18_000m));
        }

        var matcher = new FakeMatcher(_ => null);
        await using (var db = CreateAppContext(tenantContext))
        {
            await new LineItemMarketPriceService(db, tenantContext, new MutableClock(T0), matcher, new FakeSupplierNames(supplierId, "Salesforce"))
                .PriceContractAsync(tenantId, contractId, CancellationToken.None);
        }

        Assert.Equal(167_000m, Assert.Single(matcher.Contexts).AnnualValue);
    }

    [Fact]
    public async Task A_read_reprices_lines_never_priced_or_older_than_the_refresh_window()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var supplierId = EntityId.New();
        var (contractId, unlimited, _) = await SeedAsync(tenantContext, tenantId, supplierId);

        var clock = new MutableClock(T0);
        var names = new FakeSupplierNames(supplierId, "Salesforce");

        // Never priced: the first read prices both lines.
        var first = new FakeMatcher(_ => null);
        await using (var db = CreateAppContext(tenantContext))
        {
            var current = await new LineItemMarketPriceService(db, tenantContext, clock, first, names)
                .GetCurrentAsync(tenantId, contractId, CancellationToken.None);
            Assert.Equal(2, current.Count);
            Assert.All(current.Values, price => Assert.False(price.Matched));
        }

        Assert.Equal(2, first.LinesPriced);

        // The market gained the product since: once the window passes, the next read picks it up.
        clock.Now = T0 + LineItemMarketPriceService.RefreshAfter + TimeSpan.FromMinutes(1);
        var second = new FakeMatcher(line => line.Description.Contains("Unlimited", StringComparison.Ordinal) ? UnlimitedBand() : null);
        await using (var db = CreateAppContext(tenantContext))
        {
            var current = await new LineItemMarketPriceService(db, tenantContext, clock, second, names)
                .GetCurrentAsync(tenantId, contractId, CancellationToken.None);
            Assert.True(current[unlimited].Matched);
            Assert.Equal(clock.Now, current[unlimited].CheckedAt);
        }

        Assert.Equal(2, second.LinesPriced);

        // One row per line: the refresh updated in place rather than adding rows.
        using (tenantContext.BeginScope(tenantId))
        {
            await using var db = CreateAppContext(tenantContext);
            Assert.Equal(2, await db.ContractLineItemMarketPrices.CountAsync(p => p.ContractId == contractId));
        }
    }

    [Fact]
    public async Task A_matcher_failure_keeps_the_stored_comparison_and_never_throws()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var supplierId = EntityId.New();
        var (contractId, unlimited, _) = await SeedAsync(tenantContext, tenantId, supplierId);
        var names = new FakeSupplierNames(supplierId, "Salesforce");

        await using (var db = CreateAppContext(tenantContext))
        {
            await new LineItemMarketPriceService(db, tenantContext, new MutableClock(T0), new FakeMatcher(_ => UnlimitedBand()), names)
                .PriceContractAsync(tenantId, contractId, CancellationToken.None);
        }

        await using (var db = CreateAppContext(tenantContext))
        {
            var failing = new FakeMatcher(_ => throw new InvalidOperationException("market database unavailable"));
            var current = await new LineItemMarketPriceService(db, tenantContext, new MutableClock(T0.AddDays(2)), failing, names)
                .GetCurrentAsync(tenantId, contractId, CancellationToken.None);

            Assert.True(current[unlimited].Matched);
            Assert.Equal(T0, current[unlimited].CheckedAt);
        }
    }

    [Fact]
    public async Task Without_a_matcher_composed_in_nothing_is_priced_and_nothing_fails()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var (contractId, _, _) = await SeedAsync(tenantContext, tenantId, EntityId.New());

        await using var db = CreateAppContext(tenantContext);
        var current = await new LineItemMarketPriceService(db, tenantContext, new MutableClock(T0))
            .GetCurrentAsync(tenantId, contractId, CancellationToken.None);

        Assert.Empty(current);
    }

    [Fact]
    public async Task Deleting_a_line_item_deletes_its_comparison()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var supplierId = EntityId.New();
        var (contractId, unlimited, _) = await SeedAsync(tenantContext, tenantId, supplierId);

        await using (var db = CreateAppContext(tenantContext))
        {
            await new LineItemMarketPriceService(db, tenantContext, new MutableClock(T0), new FakeMatcher(_ => UnlimitedBand()), new FakeSupplierNames(supplierId, "Salesforce"))
                .PriceContractAsync(tenantId, contractId, CancellationToken.None);
        }

        using var scope = tenantContext.BeginScope(tenantId);
        await using (var db = CreateAppContext(tenantContext))
        {
            await db.ContractLineItems.Where(l => l.Id == unlimited).ExecuteDeleteAsync();
        }

        await using (var db = CreateAppContext(tenantContext))
        {
            Assert.False(await db.ContractLineItemMarketPrices.AnyAsync(p => p.LineItemId == unlimited));
            Assert.Equal(1, await db.ContractLineItemMarketPrices.CountAsync(p => p.ContractId == contractId));
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;

        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeMatcher(Func<MarketPriceLine, MarketPriceMatch?> price) : IMarketPriceMatcher
    {
        public List<MarketPriceContext> Contexts { get; } = [];

        public int LinesPriced { get; private set; }

        public Task<IReadOnlyList<MarketPriceMatch?>> MatchAsync(
            MarketPriceContext context, IReadOnlyList<MarketPriceLine> lines, CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            LinesPriced += lines.Count;
            return Task.FromResult<IReadOnlyList<MarketPriceMatch?>>(lines.Select(price).ToList());
        }
    }

    private sealed class FakeSupplierNames(EntityId supplierId, string name) : ISupplierNameLookup
    {
        public Task<IReadOnlyDictionary<EntityId, string>> GetNamesAsync(
            TenantId tenantId, IReadOnlyCollection<EntityId> supplierIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<EntityId, string>>(
                supplierIds.Contains(supplierId) ? new Dictionary<EntityId, string> { [supplierId] = name } : new Dictionary<EntityId, string>());

        public Task<EntityId?> FindByNormalizedNameAsync(TenantId tenantId, string normalizedName, CancellationToken cancellationToken) =>
            Task.FromResult<EntityId?>(null);
    }
}
