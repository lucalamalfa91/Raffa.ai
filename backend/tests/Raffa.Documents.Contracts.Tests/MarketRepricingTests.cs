using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Jobs;
using Raffa.SharedKernel.Market;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// The read wait budget, the shared background re-pricing, the due-contract finder and the pre-warm job
/// (<see cref="LineItemMarketPriceService"/>, <see cref="MarketRepriceJobHandler"/>), against an in-memory
/// store so they need no Docker. Each re-pricing runs in its own DI scope against the same store, which is
/// how it runs in the API.
/// </summary>
public sealed class MarketRepricingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    // ---- read wait budget ------------------------------------------------------------------------

    [Fact]
    public async Task A_read_whose_refresh_finishes_inside_the_budget_returns_the_fresh_comparison()
    {
        await using var h = new Harness(gateOpen: true, budgetMs: 5_000);
        var (tenant, contract, line) = await h.SeedAsync();

        var result = await h.ReadAsync(tenant, contract);

        Assert.True(result[line].Matched);
        Assert.Equal(350m, result[line].UnitPriceP50);
        Assert.Equal(1, h.Matcher.MatchCalls);
    }

    [Fact]
    public async Task A_read_whose_refresh_misses_the_budget_returns_the_stored_comparison_and_the_next_read_sees_the_result()
    {
        await using var h = new Harness(gateOpen: false, budgetMs: 100);
        var (tenant, contract, line) = await h.SeedAsync();

        var first = await h.ReadAsync(tenant, contract).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(first); // nothing was on file yet
        await h.WaitUntilAsync(() => Task.FromResult(h.Matcher.MatchCalls == 1));

        h.Matcher.Open();
        await h.WaitUntilAsync(async () => (await h.ReadAsync(tenant, contract)).TryGetValue(line, out var p) && p.Matched);

        Assert.Equal(1, h.Matcher.MatchCalls); // the polling reads joined the one refresh, none started another
    }

    [Fact]
    public async Task Reads_of_one_contract_share_one_background_refresh()
    {
        await using var h = new Harness(gateOpen: false, budgetMs: 10_000);
        var (tenant, contract, line) = await h.SeedAsync();

        var a = h.ReadAsync(tenant, contract);
        var b = h.ReadAsync(tenant, contract);
        await h.WaitUntilAsync(() => Task.FromResult(h.Matcher.MatchCalls >= 1));
        await Task.Delay(300);
        h.Matcher.Open();

        var results = await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.All(results, r => Assert.True(r[line].Matched));
        Assert.Equal(1, h.Matcher.MatchCalls);
    }

    [Fact]
    public async Task A_zero_budget_never_waits_for_the_refresh()
    {
        await using var h = new Harness(gateOpen: false, budgetMs: 0);
        var (tenant, contract, line) = await h.SeedAsync();

        var result = await h.ReadAsync(tenant, contract).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(result);

        h.Matcher.Open();
        await h.WaitUntilAsync(async () => (await h.ReadAsync(tenant, contract)).TryGetValue(line, out var p) && p.Matched);
    }

    [Fact]
    public async Task An_unbounded_budget_restores_the_original_wait_for_the_whole_refresh()
    {
        await using var h = new Harness(gateOpen: false, budgetMs: MarketPricingOptions.UnboundedReadWait);
        var (tenant, contract, line) = await h.SeedAsync();

        var read = h.ReadAsync(tenant, contract);
        await Task.Delay(300);
        Assert.False(read.IsCompleted);

        h.Matcher.Open();
        var result = await read.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result[line].Matched);
        Assert.Equal(1, h.Matcher.MatchCalls);
    }

    [Fact]
    public async Task Pricing_a_contract_at_extraction_always_waits_whatever_the_budget()
    {
        await using var h = new Harness(gateOpen: false, budgetMs: 0);
        var (tenant, contract, line) = await h.SeedAsync();

        using var scope = h.Provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<LineItemMarketPriceService>();
        var priced = service.PriceContractAsync(tenant, contract, CancellationToken.None);
        await Task.Delay(300);
        Assert.False(priced.IsCompleted);

        h.Matcher.Open();
        Assert.True((await priced.WaitAsync(TimeSpan.FromSeconds(10)))[line].Matched);
    }

    // ---- the finder mirrors what a read would re-price -------------------------------------------

    public static TheoryData<string> Scenarios =>
        ["none", "fresh", "unmatched-fresh", "stale", "older-corpus", "no-corpus-recorded", "match-without-kind"];

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task The_finder_flags_exactly_the_contracts_a_read_would_reprice(string scenario)
    {
        await using var h = new Harness(gateOpen: true, budgetMs: MarketPricingOptions.UnboundedReadWait);
        var (tenant, contract, line) = await h.SeedAsync();
        await h.StoreAsync(tenant, contract, line, scenario);

        var due = await h.FindAsync(tenant, 10);
        var callsBefore = h.Matcher.MatchCalls;
        await h.RefreshStaleAsync(tenant, contract);
        var repriced = h.Matcher.MatchCalls > callsBefore;

        Assert.Equal(repriced, due.Contains(contract));
        Assert.Equal(scenario is "none" or "stale" or "older-corpus" or "no-corpus-recorded" or "match-without-kind", repriced);
    }

    [Fact]
    public async Task The_finder_ignores_other_tenants_orphan_lines_and_honours_the_limit()
    {
        await using var h = new Harness(gateOpen: true, budgetMs: 5_000);
        var (tenant, contractA, _) = await h.SeedAsync();
        var (_, _, _) = await h.SeedAsync(tenant);
        var (_, _, _) = await h.SeedAsync(tenant);
        var (otherTenant, otherContract, _) = await h.SeedAsync();
        await h.SeedOrphanLineAsync(tenant);

        var all = await h.FindAsync(tenant, 10);
        var limited = await h.FindAsync(tenant, 2);
        var other = await h.FindAsync(otherTenant, 10);

        Assert.Equal(3, all.Count);
        Assert.Contains(contractA, all);
        Assert.DoesNotContain(otherContract, all);
        Assert.Equal(2, limited.Count);
        Assert.Equal([otherContract], other);
    }

    [Fact]
    public async Task Without_a_matcher_nothing_is_ever_due()
    {
        await using var h = new Harness(gateOpen: true, budgetMs: 5_000, withMatcher: false);
        var (tenant, _, _) = await h.SeedAsync();

        Assert.Empty(await h.FindAsync(tenant, 10));
    }

    // ---- the pre-warm job -------------------------------------------------------------------------

    [Fact]
    public async Task The_job_prices_only_the_due_contracts_of_its_tenant_and_a_second_delivery_finds_nothing_left()
    {
        await using var h = new Harness(gateOpen: true, budgetMs: 5_000);
        var (tenantA, dueContract, dueLine) = await h.SeedAsync();
        var (_, freshContract, freshLine) = await h.SeedAsync(tenantA);
        await h.StoreAsync(tenantA, freshContract, freshLine, "fresh");
        var (tenantB, otherContract, otherLine) = await h.SeedAsync();

        var message = new JobRequested(JobKinds.MarketReprice, tenantA.Value, tenantA.Value, Guid.NewGuid(), JobRequested.CurrentSchemaVersion);
        Assert.Equal(JobHandleOutcome.Handled, await h.HandleAsync(message));

        Assert.Equal(1, h.Matcher.MatchCalls); // the due contract only
        Assert.True((await h.StoredAsync(tenantA, dueContract)).ContainsKey(dueLine));
        Assert.Empty(await h.StoredAsync(tenantB, otherContract)); // another workspace is not touched
        Assert.False((await h.StoredAsync(tenantB, otherContract)).ContainsKey(otherLine));

        Assert.Equal(JobHandleOutcome.Handled, await h.HandleAsync(message));
        Assert.Equal(1, h.Matcher.MatchCalls);
    }

    [Fact]
    public void The_job_kind_is_stable_on_the_wire()
    {
        Assert.Equal("market-reprice", JobKinds.MarketReprice);
        Assert.Equal(JobKinds.MarketReprice, new MarketRepriceJobHandler(null!, null!, new MarketPricingOptions()).Kind);
    }

    // ---- harness ---------------------------------------------------------------------------------

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _database = $"reprice-{Guid.NewGuid():N}";

        public Harness(bool gateOpen, int budgetMs, bool withMatcher = true)
        {
            Matcher = new GatedMatcher(gateOpen);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ITenantContext, TenantContext>();
            services.AddSingleton<IClock>(new FixedClock(T0));
            services.AddSingleton(new MarketPricingOptions { ReadWaitBudgetMs = budgetMs, DetachedRefreshTimeoutSeconds = 60 });
            if (withMatcher)
            {
                services.AddSingleton<IMarketPriceMatcher>(Matcher);
            }

            services.AddScoped(_ => InMemoryDocumentsDb.Create(_database));
            services.AddScoped<LineItemMarketPriceService>();
            services.AddScoped<MarketRepriceJobHandler>();
            Provider = services.BuildServiceProvider();
        }

        public GatedMatcher Matcher { get; }

        public ServiceProvider Provider { get; }

        public async Task<(TenantId Tenant, EntityId Contract, EntityId Line)> SeedAsync(TenantId? tenant = null)
        {
            var tenantId = tenant ?? TenantId.New();
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            var contract = new Contract
            {
                TenantId = tenantId,
                Type = ContractDocumentType.Msa,
                Status = "needs_review",
                Currency = "GBP",
                CreatedAt = T0,
            };
            var line = new ContractLineItem
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                Description = "Sales Cloud Unlimited named users",
                Sku = "unl",
                Quantity = 10m,
                UnitPrice = 100m,
                CreatedAt = T0,
            };
            db.Contracts.Add(contract);
            db.ContractLineItems.Add(line);
            await db.SaveChangesAsync();
            return (tenantId, contract.Id, line.Id);
        }

        public async Task SeedOrphanLineAsync(TenantId tenant)
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            db.ContractLineItems.Add(new ContractLineItem
            {
                TenantId = tenant,
                ContractId = EntityId.New(),
                Description = "Line of a contract that no longer exists",
                CreatedAt = T0,
            });
            await db.SaveChangesAsync();
        }

        public async Task StoreAsync(TenantId tenant, EntityId contract, EntityId line, string scenario)
        {
            if (scenario == "none")
            {
                return;
            }

            var matched = scenario is not "unmatched-fresh";
            var row = new ContractLineItemMarketPrice
            {
                TenantId = tenant,
                ContractId = contract,
                LineItemId = line,
                CheckedAt = scenario == "stale" ? T0 - LineItemMarketPriceService.RefreshAfter - TimeSpan.FromHours(1) : T0,
                CorpusVersion = scenario switch
                {
                    "older-corpus" => "feed:0",
                    "no-corpus-recorded" => null,
                    _ => Matcher.CorpusVersion,
                },
                RecordId = matched ? "MKT-1" : null,
                UnitPriceP50 = matched ? 350m : null,
                MatchKind = matched && scenario != "match-without-kind" ? MarketMatchKind.Exact : null,
            };
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            db.ContractLineItemMarketPrices.Add(row);
            await db.SaveChangesAsync();
        }

        public async Task<IReadOnlyDictionary<EntityId, LineItemMarketPrice>> ReadAsync(TenantId tenant, EntityId contract)
        {
            using var scope = Provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<LineItemMarketPriceService>();
            return await service.GetCurrentAsync(tenant, contract, CancellationToken.None);
        }

        public async Task RefreshStaleAsync(TenantId tenant, EntityId contract)
        {
            using var scope = Provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<LineItemMarketPriceService>();
            await service.RefreshIfStaleAsync(tenant, contract, CancellationToken.None);
        }

        public async Task<IReadOnlyList<EntityId>> FindAsync(TenantId tenant, int max)
        {
            using var scope = Provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<LineItemMarketPriceService>();
            return await service.FindContractsNeedingRefreshAsync(tenant, max, CancellationToken.None);
        }

        public async Task<JobHandleOutcome> HandleAsync(JobRequested message)
        {
            using var scope = Provider.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<MarketRepriceJobHandler>();
            return await handler.HandleAsync(message, deliveryCount: 1);
        }

        public async Task<Dictionary<EntityId, ContractLineItemMarketPrice>> StoredAsync(TenantId tenant, EntityId contract)
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            return await db.ContractLineItemMarketPrices
                .AsNoTracking()
                .Where(p => p.TenantId == tenant && p.ContractId == contract)
                .ToDictionaryAsync(p => p.LineItemId);
        }

        public async Task WaitUntilAsync(Func<Task<bool>> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!await condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "Condition was not met in time.");
                await Task.Delay(25);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Matcher.Open(); // let any re-pricing still waiting on the gate end
            await Task.Delay(50);
            await Provider.DisposeAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class GatedMatcher(bool open) : IMarketPriceMatcher
    {
        private readonly TaskCompletionSource _gate = CreateGate(open);

        private int _matchCalls;

        public string? CorpusVersion => "feed:1";

        public int MatchCalls => Volatile.Read(ref _matchCalls);

        public void Open() => _gate.TrySetResult();

        public Task<string?> GetCorpusVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>(CorpusVersion);

        public async Task<IReadOnlyList<MarketPriceMatch?>> MatchAsync(
            MarketPriceContext context, IReadOnlyList<MarketPriceLine> lines, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _matchCalls);
            await _gate.Task.WaitAsync(cancellationToken);
            return lines
                .Select(_ => (MarketPriceMatch?)new MarketPriceMatch(
                    "MKT-1", "Sales Cloud Unlimited", "UK", "GBP", 12, 300m, 350m, 410m, 64, "mock feed", T0))
                .ToList();
        }

        private static TaskCompletionSource CreateGate(bool open)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (open)
            {
                gate.SetResult();
            }

            return gate;
        }
    }
}
