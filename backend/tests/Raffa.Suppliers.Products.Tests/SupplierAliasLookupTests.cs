using Microsoft.EntityFrameworkCore;
using Raffa.SharedKernel;
using Raffa.Suppliers.Products.Application;
using Raffa.Suppliers.Products.Domain;
using Raffa.Suppliers.Products.Infrastructure;
using Testcontainers.PostgreSql;

namespace Raffa.Suppliers.Products.Tests;

/// <summary>
/// Task F6-T09: <see cref="SupplierAliasLookup"/> returns the names a tenant's supplier is recorded
/// under, finding it by its normalised name or an alias like <see cref="SupplierResolver"/> does, but
/// read-only: an unknown name returns nothing and creates nothing.
/// </summary>
public sealed class SupplierAliasLookupTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var migrateDb = CreateContext();
        await migrateDb.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private SuppliersDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<SuppliersDbContext>();
        SuppliersDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString());
        return new SuppliersDbContext(optionsBuilder.Options);
    }

    private async Task SeedAsync(TenantId tenantId, string name, string normalizedName, params string[] aliases)
    {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        db.Suppliers.Add(new Supplier
        {
            TenantId = tenantId,
            Name = name,
            NormalizedName = normalizedName,
            Aliases = aliases,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<string>> LookupAsync(TenantId tenantId, string rawName)
    {
        await using var db = CreateContext();
        return await new SupplierAliasLookup(db).GetAlternateNamesAsync(tenantId, rawName, CancellationToken.None);
    }

    [Fact]
    public async Task The_supplier_is_found_by_its_normalised_name_and_its_own_name_and_aliases_come_back()
    {
        var tenantId = TenantId.New();
        await SeedAsync(tenantId, "Salesforce", "salesforce", "sfdc", "sales force");

        var names = await LookupAsync(tenantId, "Salesforce, Inc.");

        Assert.Equal(["Salesforce", "sfdc", "sales force"], names);
    }

    [Fact]
    public async Task The_supplier_is_found_by_an_alias_too()
    {
        var tenantId = TenantId.New();
        await SeedAsync(tenantId, "Salesforce", "salesforce", "sfdc");

        var names = await LookupAsync(tenantId, "SFDC");

        Assert.Equal(["Salesforce"], names);
    }

    [Fact]
    public async Task The_name_that_was_asked_about_is_never_returned_back()
    {
        var tenantId = TenantId.New();
        await SeedAsync(tenantId, "Salesforce", "salesforce", "sfdc");

        Assert.DoesNotContain("Salesforce", await LookupAsync(tenantId, "salesforce"), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unknown_or_blank_name_returns_nothing_and_creates_no_row()
    {
        var tenantId = TenantId.New();

        Assert.Empty(await LookupAsync(tenantId, "Nobody Ltd"));
        Assert.Empty(await LookupAsync(tenantId, "  "));

        await using var db = CreateContext();
        Assert.Equal(0, await db.Suppliers.CountAsync(s => s.TenantId == tenantId));
    }

    [Fact]
    public async Task Another_tenants_supplier_is_never_returned()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        await SeedAsync(tenantA, "Salesforce", "salesforce", "sfdc");

        Assert.Empty(await LookupAsync(tenantB, "Salesforce"));
    }
}
