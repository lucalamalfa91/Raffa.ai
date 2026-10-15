using Microsoft.EntityFrameworkCore;
using Npgsql;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Quotes.Application.Assessment;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Testcontainers.PostgreSql;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Task F6-T09 (matcher with <c>Raffa.Suppliers.Products</c>): a line the benchmark could not match under
/// the names the quote wrote is retried with the names the tenant recorded (the supplier's other names,
/// the confirmed canonical product and SKU of its SKU mapping), in a fixed order; the first retry that
/// finds a distribution wins, and when none does the original answer stands. The ladder itself is
/// pure; the service tests run on a real Postgres with RLS like the other assessment tests.
/// </summary>
public sealed class MarketAssessmentAliasMatchingTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_alias_match_app";
    private const string AppRolePassword = "raffa_alias_match_app_test_password";

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());
        await using var adminDb = new QuotesDbContext(adminOptions.Options);
        await adminDb.Database.MigrateAsync();
        await adminDb.Database.ExecuteSqlRawAsync(
            $"""
            CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
            GRANT USAGE ON SCHEMA public TO {AppRoleName};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
            """);

        _appConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private QuotesDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new QuotesDbContext(optionsBuilder.Options);
    }

    // ----- the ladder (pure) -----

    private static BenchmarkQuery Direct { get; } = new(
        "Salesforce, Inc.", "Sales Cloud Enterprise Edition", "SC-ENT", "US", 100m, "12 months", "USD", new DateOnly(2026, 9, 1));

    private static SkuProductMapping Mapping(string? product = "Sales Cloud Enterprise", string sku = "SALES-CLOUD-ENT") => new()
    {
        TenantId = TenantId.New(),
        NormalizedSku = "SC-ENT",
        CanonicalSku = sku,
        CanonicalProductName = product,
        CreatedAt = Now,
    };

    [Fact]
    public void Nothing_recorded_means_no_retries()
    {
        Assert.Empty(MarketAssessmentMatchLadder.Retries(Direct, [], mapping: null));
    }

    [Fact]
    public void The_order_is_product_alias_then_supplier_aliases_then_both()
    {
        var retries = MarketAssessmentMatchLadder.Retries(Direct, ["Salesforce", "SFDC"], Mapping());

        Assert.Equal(
            [
                MarketMatchLevel.ProductAlias,
                MarketMatchLevel.SupplierAlias,
                MarketMatchLevel.SupplierAlias,
                MarketMatchLevel.SupplierAndProductAlias,
                MarketMatchLevel.SupplierAndProductAlias,
            ],
            retries.Select(r => r.Level).ToArray());
        Assert.Equal("Salesforce, Inc.", retries[0].Query.Supplier);
        Assert.Equal("Sales Cloud Enterprise", retries[0].Query.Product);
        Assert.Equal("SALES-CLOUD-ENT", retries[0].Query.Sku);
        Assert.Equal("Salesforce", retries[1].Query.Supplier);
        Assert.Equal(Direct.Product, retries[1].Query.Product);
        Assert.Equal("SFDC", retries[2].Query.Supplier);
        Assert.Equal("Salesforce", retries[3].Query.Supplier);
        Assert.Equal("Sales Cloud Enterprise", retries[3].Query.Product);
        // Only the names change: quantity, term, currency, geography and date are the quote's own.
        Assert.All(retries, r =>
        {
            Assert.Equal(Direct.Quantity, r.Query.Quantity);
            Assert.Equal(Direct.Term, r.Query.Term);
            Assert.Equal(Direct.Currency, r.Query.Currency);
            Assert.Equal(Direct.Geography, r.Query.Geography);
            Assert.Equal(Direct.PurchaseDate, r.Query.PurchaseDate);
        });
    }

    [Fact]
    public void A_mapping_that_changes_nothing_and_the_quotes_own_supplier_name_are_not_retries()
    {
        var sameAsDirect = Mapping(product: Direct.Product, sku: "SC-ENT");

        var retries = MarketAssessmentMatchLadder.Retries(Direct, ["salesforce, inc.", " "], sameAsDirect);

        Assert.Empty(retries);
    }

    [Fact]
    public void A_mapping_without_a_product_name_still_retries_with_the_canonical_sku()
    {
        var retries = MarketAssessmentMatchLadder.Retries(Direct, [], Mapping(product: null));

        var retry = Assert.Single(retries);
        Assert.Equal(Direct.Product, retry.Query.Product);
        Assert.Equal("SALES-CLOUD-ENT", retry.Query.Sku);
    }

    [Fact]
    public void Retries_are_capped()
    {
        var names = Enumerable.Range(0, 20).Select(i => $"Alias {i}").ToList();

        Assert.Equal(MarketAssessmentMatchLadder.MaxRetries, MarketAssessmentMatchLadder.Retries(Direct, names, Mapping()).Count);
    }

    // ----- the service -----

    private sealed class KnowsOnly(string supplier, string product) : IBenchmarkService
    {
        public List<BenchmarkQuery> Queries { get; } = [];

        public Task<Result<BenchmarkResult>> GetBenchmarkAsync(BenchmarkQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            var found = string.Equals(query.Supplier, supplier, StringComparison.OrdinalIgnoreCase)
                && string.Equals(query.Product, product, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(Result<BenchmarkResult>.Success(new BenchmarkResult(
                found ? new BenchmarkDistribution(100m, 150m, 200m) : null,
                "per seat / year",
                query.Currency,
                found ? 0.9 : 0d,
                "test",
                Now,
                found ? [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Product] : [],
                found ? 40 : null)));
        }
    }

    private sealed class FixedAliases(params string[] names) : ISupplierAliasLookup
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> GetAlternateNamesAsync(
            TenantId tenantId, string rawName, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<string>>(names);
        }
    }

    private sealed class FailingAliases : ISupplierAliasLookup
    {
        public Task<IReadOnlyList<string>> GetAlternateNamesAsync(
            TenantId tenantId, string rawName, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("suppliers database unreachable");
    }

    private async Task<(TenantId TenantId, EntityId QuoteId)> SeedAsync(
        string supplier = "Salesforce, Inc.", string product = "Sales Cloud Enterprise Edition", string? sku = "SC-ENT",
        SkuProductMapping? mappingForOtherTenant = null, bool mappingForThisTenant = false)
    {
        var tenantId = TenantId.New();
        var quoteId = EntityId.New();
        var tenantContext = new TenantContext();

        await using var db = CreateAppContext(tenantContext);
        using var scope = tenantContext.BeginScope(tenantId);
        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            TenantId = tenantId,
            FileName = "q.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value:D}/q.pdf",
            Checksum = "c",
            Supplier = supplier,
            Currency = "USD",
            Geography = "US",
            PurchaseDate = new DateOnly(2026, 9, 1),
            CreatedAt = Now,
        });
        db.QuoteLines.Add(new QuoteLine
        {
            TenantId = tenantId,
            QuoteId = quoteId,
            Description = product,
            Sku = sku,
            NormalizedSku = sku,
            Quantity = 100m,
            Term = "12 months",
            UnitPrice = 300m,
            CreatedAt = Now,
        });
        if (mappingForThisTenant)
        {
            db.SkuProductMappings.Add(new SkuProductMapping
            {
                TenantId = tenantId,
                NormalizedSku = "SC-ENT",
                CanonicalSku = "SALES-CLOUD-ENT",
                CanonicalProductName = "Sales Cloud Enterprise",
                CreatedAt = Now,
            });
        }

        await db.SaveChangesAsync();

        if (mappingForOtherTenant is not null)
        {
            var other = new TenantContext();
            await using var otherDb = CreateAppContext(other);
            using var otherScope = other.BeginScope(mappingForOtherTenant.TenantId);
            otherDb.SkuProductMappings.Add(mappingForOtherTenant);
            await otherDb.SaveChangesAsync();
        }

        return (tenantId, quoteId);
    }

    private async Task<LineMarketAssessment> AssessAsync(
        TenantId tenantId, EntityId quoteId, IBenchmarkService benchmark, ISupplierAliasLookup? aliases)
    {
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var result = await new MarketAssessmentService(db, benchmark, tenantContext, aliases).AssessAsync(tenantId, quoteId);
        Assert.True(result.IsSuccess);
        return Assert.Single(result.Value.Lines);
    }

    [Fact]
    public async Task A_line_that_matches_directly_is_assessed_exactly_as_before_with_one_call()
    {
        var (tenantId, quoteId) = await SeedAsync();
        var benchmark = new KnowsOnly("Salesforce, Inc.", "Sales Cloud Enterprise Edition");
        var aliases = new FixedAliases("Salesforce");

        var line = await AssessAsync(tenantId, quoteId, benchmark, aliases);

        Assert.Equal(MarketAssessmentStatus.Assessed, line.Status);
        Assert.Equal(MarketMatchLevel.Direct, line.MatchLevel);
        Assert.DoesNotContain("Matched through", line.Explanation);
        Assert.Single(benchmark.Queries);
        Assert.Equal(0, aliases.Calls);
    }

    [Fact]
    public async Task A_supplier_the_tenant_knows_under_another_name_is_matched_through_that_name()
    {
        var (tenantId, quoteId) = await SeedAsync();
        var benchmark = new KnowsOnly("Salesforce", "Sales Cloud Enterprise Edition");

        var line = await AssessAsync(tenantId, quoteId, benchmark, new FixedAliases("Salesforce"));

        Assert.Equal(MarketAssessmentStatus.Assessed, line.Status);
        Assert.Equal(MarketMatchLevel.SupplierAlias, line.MatchLevel);
        Assert.Contains("Matched through another name recorded for this supplier", line.Explanation);
        Assert.Equal(150m, line.Benchmark!.Distribution!.P50);
        Assert.Equal(2, benchmark.Queries.Count);
    }

    [Fact]
    public async Task A_confirmed_sku_mapping_now_feeds_the_query()
    {
        var (tenantId, quoteId) = await SeedAsync(mappingForThisTenant: true);
        var benchmark = new KnowsOnly("Salesforce, Inc.", "Sales Cloud Enterprise");

        var line = await AssessAsync(tenantId, quoteId, benchmark, aliases: null);

        Assert.Equal(MarketAssessmentStatus.Assessed, line.Status);
        Assert.Equal(MarketMatchLevel.ProductAlias, line.MatchLevel);
        Assert.Equal("SALES-CLOUD-ENT", benchmark.Queries.Last().Sku);
    }

    [Fact]
    public async Task Both_aliases_can_be_needed_together()
    {
        var (tenantId, quoteId) = await SeedAsync(mappingForThisTenant: true);
        var benchmark = new KnowsOnly("Salesforce", "Sales Cloud Enterprise");

        var line = await AssessAsync(tenantId, quoteId, benchmark, new FixedAliases("Salesforce"));

        Assert.Equal(MarketMatchLevel.SupplierAndProductAlias, line.MatchLevel);
        Assert.Equal(MarketAssessmentStatus.Assessed, line.Status);
    }

    [Fact]
    public async Task When_no_name_matches_the_original_answer_stands_and_the_matcher_abstains()
    {
        var (tenantId, quoteId) = await SeedAsync(mappingForThisTenant: true);
        var benchmark = new KnowsOnly("Somebody Else", "Another Product");

        var line = await AssessAsync(tenantId, quoteId, benchmark, new FixedAliases("Salesforce", "SFDC"));

        Assert.Equal(MarketAssessmentStatus.InsufficientBenchmarkData, line.Status);
        Assert.Equal(MarketMatchLevel.Direct, line.MatchLevel);
        Assert.Null(line.Position);
        Assert.False(line.Benchmark!.HasSufficientData);
        Assert.DoesNotContain("Matched through", line.Explanation);
        // 1 direct + at most the cap in retries.
        Assert.Equal(1 + 5, benchmark.Queries.Count);
    }

    [Fact]
    public async Task A_supplier_alias_lookup_that_fails_does_not_fail_the_assessment()
    {
        var (tenantId, quoteId) = await SeedAsync();

        var line = await AssessAsync(tenantId, quoteId, new KnowsOnly("Nobody", "Nothing"), new FailingAliases());

        Assert.Equal(MarketAssessmentStatus.InsufficientBenchmarkData, line.Status);
    }

    [Fact]
    public async Task Another_tenants_sku_mapping_is_never_used()
    {
        var otherTenant = TenantId.New();
        var theirs = new SkuProductMapping
        {
            TenantId = otherTenant,
            NormalizedSku = "SC-ENT",
            CanonicalSku = "SALES-CLOUD-ENT",
            CanonicalProductName = "Sales Cloud Enterprise",
            CreatedAt = Now,
        };
        var (tenantId, quoteId) = await SeedAsync(mappingForOtherTenant: theirs);
        var benchmark = new KnowsOnly("Salesforce, Inc.", "Sales Cloud Enterprise");

        var line = await AssessAsync(tenantId, quoteId, benchmark, aliases: null);

        Assert.Equal(MarketAssessmentStatus.InsufficientBenchmarkData, line.Status);
        Assert.Single(benchmark.Queries);
    }
}
