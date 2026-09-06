using Contigo.Renewals.Domain;
using Contigo.Renewals.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Contigo.Renewals.Tests;

/// <summary>
/// Proves the "tenant isolation" half of task E03/F02/US01/T02 (renewal-alerts, ADR-009): with the
/// RLS policy from this task's own <c>AddRenewalAlert</c> migration applied and
/// <see cref="TenantRlsConnectionInterceptor"/> setting the per-connection <c>app.tenant_id</c>
/// claim, tenant A's connection genuinely cannot read (or write) tenant B's <see cref="RenewalAlert"/>
/// row — enforced by Postgres itself, not by <see cref="RenewalAlertService"/>'s own
/// application-level <c>WHERE tenant_id = ...</c> filter alone. Mirrors
/// <see cref="RenewalActionRlsCrossTenantIsolationTests"/> exactly, scoped to this task's own table
/// — see that type's own doc comment for why every assertion below runs through a dedicated,
/// deliberately unprivileged Postgres role rather than the Testcontainers bootstrap superuser.
/// </summary>
public sealed class RenewalAlertRlsCrossTenantIsolationTests : IAsyncLifetime
{
    private const string AppRoleName = "contigo_alert_app";
    private const string AppRolePassword = "contigo_alert_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<RenewalsDbContext>();
        RenewalsDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new RenewalsDbContext(adminOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity + this task's own AddRenewalAlert.
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

    private RenewalsDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RenewalsDbContext>();
        RenewalsDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new RenewalsDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Tenant_cannot_read_another_tenants_renewal_alert_row()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        await SeedAlertAsync(tenantA);
        await SeedAlertAsync(tenantB);

        var tenantContext = new TenantContext();
        using (tenantContext.BeginScope(tenantA))
        {
            await using var db = CreateAppContext(tenantContext);

            var visible = await db.RenewalAlerts.ToListAsync();

            var visibleRow = Assert.Single(visible);
            Assert.Equal(tenantA, visibleRow.TenantId);
        }
    }

    [Fact]
    public async Task No_active_tenant_scope_sees_zero_rows()
    {
        await SeedAlertAsync(TenantId.New());

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);

        var visible = await db.RenewalAlerts.ToListAsync();

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

        db.RenewalAlerts.Add(new RenewalAlert
        {
            TenantId = claimedOnRow,
            ContractId = EntityId.New(),
            Milestone = RenewalMilestoneKind.RenewalDate,
            ThresholdDays = 90,
            MilestoneDate = new DateOnly(2026, 4, 1),
            Status = RenewalAlertStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private async Task SeedAlertAsync(TenantId tenantId)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        db.RenewalAlerts.Add(new RenewalAlert
        {
            TenantId = tenantId,
            ContractId = EntityId.New(),
            Milestone = RenewalMilestoneKind.RenewalDate,
            ThresholdDays = 90,
            MilestoneDate = new DateOnly(2026, 4, 1),
            Status = RenewalAlertStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
