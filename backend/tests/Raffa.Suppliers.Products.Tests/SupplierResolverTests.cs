using Raffa.SharedKernel;
using Raffa.Suppliers.Products.Application;
using Raffa.Suppliers.Products.Domain;
using Raffa.Suppliers.Products.Infrastructure;
using Raffa.Suppliers.Products.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Suppliers.Products.Tests;

/// <summary>
/// Proves the Definition of Done for task E13/F03/US01/T01 (parent story
/// us-01-supplier-identity AC-2): <see cref="SupplierResolver"/> matches an existing row by
/// normalized name or alias before ever creating a new one, so "Salesforce, Inc." and "salesforce"
/// resolve to a single id — through the exact same Npgsql/EF Core pipeline every other module's
/// write path uses (ADR-003), not an in-memory fake. Tenant isolation under RLS is proved
/// separately, in <c>Raffa.IntegrationTests.SupplierCrossTenantIsolationTests</c> (same split
/// <c>Raffa.Renewals.Tests</c> already uses between its own service tests and its own
/// <c>*RlsCrossTenantIsolationTests</c>).
///
/// Spins up its own disposable Postgres container per test run (Testcontainers), so this test
/// needs nothing but a running Docker daemon; no shared/external database to stand up by hand.
/// </summary>
public sealed class SupplierResolverTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private SuppliersDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<SuppliersDbContext>();
        SuppliersDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString());
        return new SuppliersDbContext(optionsBuilder.Options);
    }

    private async Task MigrateAsync()
    {
        await using var migrateDb = CreateContext();
        await migrateDb.Database.MigrateAsync();
    }

    [Fact]
    public async Task ResolveAsync_creates_a_new_row_on_the_first_call()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero));

        await using var db = CreateContext();
        var resolver = new SupplierResolver(db, clock);

        var result = await resolver.ResolveAsync(tenantId, "Salesforce, Inc.", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Salesforce, Inc.", result.Value.Name);

        var rows = await db.Suppliers.Where(s => s.TenantId == tenantId).ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal("salesforce", row.NormalizedName);
        Assert.Equal(clock.UtcNow, row.CreatedAt);
        Assert.Equal(clock.UtcNow, row.UpdatedAt);
        Assert.Equal(result.Value.Id, row.Id);
    }

    [Fact]
    public async Task ResolveAsync_reuses_the_row_on_the_second_call_for_an_equivalent_name()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero));

        Result<Raffa.SharedKernel.Suppliers.SupplierRef> first;
        await using (var db = CreateContext())
        {
            var resolver = new SupplierResolver(db, clock);
            first = await resolver.ResolveAsync(tenantId, "Salesforce, Inc.", CancellationToken.None);
        }

        // Fresh context/connection: the second call must read back from Postgres, not reuse the
        // first call's change tracker.
        await using var secondDb = CreateContext();
        var secondResolver = new SupplierResolver(secondDb, clock);
        var second = await secondResolver.ResolveAsync(tenantId, "SALESFORCE INC", CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Id, second.Value.Id);

        await using var readDb = CreateContext();
        var count = await readDb.Suppliers.CountAsync(s => s.TenantId == tenantId);
        Assert.Equal(1, count); // one row, not a duplicate for the same tenant + normalized name.
    }

    [Fact]
    public async Task ResolveAsync_matches_an_existing_alias_instead_of_creating_a_duplicate()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero));

        Guid seededId;
        await using (var seedDb = CreateContext())
        {
            var seeded = new Supplier
            {
                TenantId = tenantId,
                Name = "Salesforce",
                NormalizedName = "salesforce",
                Aliases = ["sfdc"],
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow,
            };
            seedDb.Suppliers.Add(seeded);
            await seedDb.SaveChangesAsync();
            seededId = seeded.Id.Value;
        }

        await using var db = CreateContext();
        var resolver = new SupplierResolver(db, clock);

        // "SFDC" normalizes to "sfdc" -- not the row's own NormalizedName ("salesforce"), only one
        // of its Aliases -- so this only succeeds if the alias branch of the match is real.
        var result = await resolver.ResolveAsync(tenantId, "SFDC", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seededId, result.Value.Id.Value);

        var count = await db.Suppliers.CountAsync(s => s.TenantId == tenantId);
        Assert.Equal(1, count); // matched the alias, did not create a second row.
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_rejects_a_blank_name_without_writing_anything(string? rawName)
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        await using var db = CreateContext();
        var resolver = new SupplierResolver(db, new FixedClock(DateTimeOffset.UtcNow));

        var result = await resolver.ResolveAsync(tenantId, rawName!, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, await db.Suppliers.CountAsync(s => s.TenantId == tenantId));
    }

    [Fact]
    public async Task ResolveAsync_scopes_matching_to_the_calling_tenant()
    {
        await MigrateAsync();

        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero));

        await using (var db = CreateContext())
        {
            var resolver = new SupplierResolver(db, clock);
            await resolver.ResolveAsync(tenantA, "Allianz", CancellationToken.None);
        }

        // Tenant B has never done business with "Allianz" -- this must create tenant B's own row,
        // not silently return tenant A's, even though the normalized name is identical.
        await using var db2 = CreateContext();
        var resolverForB = new SupplierResolver(db2, clock);
        var resultForB = await resolverForB.ResolveAsync(tenantB, "Allianz", CancellationToken.None);

        Assert.True(resultForB.IsSuccess);

        await using var readDb = CreateContext();
        var allRows = await readDb.Suppliers
            .Where(s => s.NormalizedName == "allianz")
            .ToListAsync();
        Assert.Equal(2, allRows.Count);
        Assert.Equal(2, allRows.Select(s => s.TenantId).Distinct().Count());
    }
}
