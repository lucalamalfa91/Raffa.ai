using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Contigo.Suppliers.Products.Domain;
using Contigo.Suppliers.Products.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Contigo.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E13/F03/US01/T01 (parent story
/// us-01-supplier-identity AC-1): "another tenant cannot read it even with a guessed id" — with
/// this task's own <c>AddTenantRowLevelSecurity</c> migration applied and
/// <see cref="TenantRlsConnectionInterceptor"/> setting the per-connection <c>app.tenant_id</c>
/// claim, tenant A's connection genuinely cannot read (or write) tenant B's
/// <see cref="Supplier"/> row — enforced by Postgres itself, not by application code's own
/// <c>WHERE tenant_id = ...</c> filter alone. Mirrors
/// <c>Contigo.Renewals.Tests.RenewalAlertRlsCrossTenantIsolationTests</c> exactly, scoped to this
/// module's own table — see that type's own doc comment for why every assertion below runs
/// through a dedicated, deliberately unprivileged Postgres role rather than the Testcontainers
/// bootstrap superuser (a superuser always bypasses row security regardless of `FORCE ROW LEVEL
/// SECURITY`, which would make this proof vacuous).
///
/// Lives in <c>Contigo.IntegrationTests</c> rather than <c>Contigo.Suppliers.Products.Tests</c>
/// per this task's own file assignment; unlike the <c>R0</c>-<c>R4</c> suites elsewhere in this
/// project it does not go through <c>Contigo.Api</c>'s <c>WebApplicationFactory&lt;Program&gt;</c>
/// — <c>Contigo.Api</c> does not reference <c>Contigo.Suppliers.Products</c> yet (task
/// E13/F06/US01/T01 wires that in), so this test drives <see cref="SuppliersDbContext"/> directly,
/// the same shape <c>Contigo.Renewals.Tests</c>' own per-module Rls tests already use.
///
/// Spins up its own disposable Postgres container per test run (Testcontainers), so this test
/// needs nothing but a running Docker daemon; no shared/external database to stand up by hand.
/// </summary>
public sealed class SupplierCrossTenantIsolationTests : IAsyncLifetime
{
    private const string AppRoleName = "contigo_supplier_app";
    private const string AppRolePassword = "contigo_supplier_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<SuppliersDbContext>();
        SuppliersDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new SuppliersDbContext(adminOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity.
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

    private SuppliersDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SuppliersDbContext>();
        SuppliersDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new SuppliersDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Tenant_cannot_read_another_tenants_supplier_row_even_with_its_id()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        var tenantASupplierId = await SeedSupplierAsync(tenantA, "Allianz");
        await SeedSupplierAsync(tenantB, "Zurich");

        var tenantContext = new TenantContext();
        using (tenantContext.BeginScope(tenantB))
        {
            await using var db = CreateAppContext(tenantContext);

            // A guessed/leaked id from tenant A's own row -- RLS must deny this regardless of
            // whether the caller happens to know the exact primary key.
            var found = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == tenantASupplierId);
            Assert.Null(found);

            var visible = await db.Suppliers.ToListAsync();
            var visibleRow = Assert.Single(visible);
            Assert.Equal(tenantB, visibleRow.TenantId);
            Assert.Equal("Zurich", visibleRow.Name);
        }
    }

    [Fact]
    public async Task No_active_tenant_scope_sees_zero_rows()
    {
        await SeedSupplierAsync(TenantId.New(), "Allianz");

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);

        var visible = await db.Suppliers.ToListAsync();

        Assert.Empty(visible);
    }

    [Fact]
    public async Task Cannot_write_a_row_claiming_a_different_tenant_than_the_active_scope()
    {
        var activeScope = TenantId.New();
        var claimedOnRow = TenantId.New();

        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(activeScope);
        await using var db = CreateAppContext(tenantContext);

        var now = DateTimeOffset.UtcNow;
        db.Suppliers.Add(new Supplier
        {
            TenantId = claimedOnRow,
            Name = "Allianz",
            NormalizedName = "allianz",
            CreatedAt = now,
            UpdatedAt = now,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private async Task<EntityId> SeedSupplierAsync(TenantId tenantId, string name)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        var now = DateTimeOffset.UtcNow;
        var supplier = new Supplier
        {
            TenantId = tenantId,
            Name = name,
            NormalizedName = name.ToLowerInvariant(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    }
}
