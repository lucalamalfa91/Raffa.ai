using Raffa.SharedKernel;
using Raffa.Suppliers.Products.Application;
using Raffa.Suppliers.Products.Domain;
using Raffa.Suppliers.Products.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Suppliers.Products.Tests;

/// <summary>
/// Proves the Definition of Done for task E19/F04/US01/T01 (outcome-resolves-the-opportunity;
/// ADR-028 §D5 clause 2): <see cref="SupplierNameLookup.FindByNormalizedNameAsync"/> is a strict,
/// read-only match against the <c>(tenant_id, normalized_name)</c> unique index
/// (<see cref="Infrastructure.Configurations.SupplierConfiguration"/>) — never
/// <see cref="Supplier.Aliases"/>, and never a row on a miss, unlike <see cref="SupplierResolver"/>
/// (proved by <see cref="SupplierResolverTests"/>), which resolves *or creates*. Same
/// Testcontainers-per-test-run shape as that sibling test class — needs nothing but a running
/// Docker daemon, no shared/external database to stand up by hand. Tenant isolation under RLS
/// (rather than this port's own application-level <c>WHERE tenant_id</c> filter, proved here) is
/// proved separately, at the module level, by
/// <c>Raffa.IntegrationTests.SupplierCrossTenantIsolationTests</c> — same split
/// <see cref="SupplierResolverTests"/> already establishes for <see cref="SupplierResolver"/>.
/// </summary>
public sealed class SupplierNameLookupTests : IAsyncLifetime
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

    private async Task<EntityId> SeedSupplierAsync(
        TenantId tenantId, string name, string normalizedName, params string[] aliases)
    {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var supplier = new Supplier
        {
            TenantId = tenantId,
            Name = name,
            NormalizedName = normalizedName,
            Aliases = aliases,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    }

    [Fact]
    public async Task FindByNormalizedNameAsync_returns_the_id_of_the_matching_supplier()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var supplierId = await SeedSupplierAsync(tenantId, "Salesforce, Inc.", "salesforce");

        await using var db = CreateContext();
        var lookup = new SupplierNameLookup(db);

        var found = await lookup.FindByNormalizedNameAsync(
            tenantId, "salesforce", CancellationToken.None);

        Assert.Equal(supplierId, found);
    }

    /// <summary>The Definition of Done's own "a name with no supplier returns nothing and creates
    /// no row" — the property that makes this port safe to call on a path (task
    /// E19/F04/US01/T01's negotiation-outcome resolution) that must never mint a supplier as a side
    /// effect of recording an outcome.</summary>
    [Fact]
    public async Task FindByNormalizedNameAsync_returns_null_and_creates_no_row_when_no_supplier_matches()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();

        await using var db = CreateContext();
        var lookup = new SupplierNameLookup(db);

        var found = await lookup.FindByNormalizedNameAsync(
            tenantId, "salesforce", CancellationToken.None);

        Assert.Null(found);

        await using var readDb = CreateContext();
        Assert.Equal(0, await readDb.Suppliers.CountAsync(s => s.TenantId == tenantId));
    }

    [Fact]
    public async Task FindByNormalizedNameAsync_never_matches_on_an_alias()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        // "sfdc" is only an alias, never this row's own NormalizedName --
        // SupplierResolverTests.ResolveAsync_matches_an_existing_alias_instead_of_creating_a_duplicate
        // proves SupplierResolver *does* match an alias; this lookup deliberately does not (see
        // SupplierNameLookup.FindByNormalizedNameAsync's own doc comment: the
        // (tenant_id, normalized_name) unique index covers NormalizedName only).
        await SeedSupplierAsync(tenantId, "Salesforce", "salesforce", "sfdc");

        await using var db = CreateContext();
        var lookup = new SupplierNameLookup(db);

        var found = await lookup.FindByNormalizedNameAsync(tenantId, "sfdc", CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindByNormalizedNameAsync_scopes_matching_to_the_calling_tenant()
    {
        await MigrateAsync();

        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        await SeedSupplierAsync(tenantA, "Allianz", "allianz");

        await using var db = CreateContext();
        var lookup = new SupplierNameLookup(db);

        // Tenant B has never done business with "Allianz" -- must not resolve tenant A's row even
        // though the normalized name is identical.
        var found = await lookup.FindByNormalizedNameAsync(tenantB, "allianz", CancellationToken.None);

        Assert.Null(found);
    }
}
