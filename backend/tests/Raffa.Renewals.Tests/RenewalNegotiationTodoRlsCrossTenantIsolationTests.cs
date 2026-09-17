using Raffa.Renewals.Domain;
using Raffa.Renewals.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Renewals.Tests;

/// <summary>
/// Proves AC-1's RLS half for task E29/F01/US01/T01 (todo-entity-api; wave w19 NW-85; ADR-009 w19):
/// with the RLS policy from this module's own `AddRenewalNegotiationTodo` migration applied and
/// <see cref="TenantRlsConnectionInterceptor"/> setting the per-connection `app.tenant_id` claim,
/// tenant A's connection genuinely cannot read (or write) tenant B's <see cref="RenewalNegotiationTodo"/>
/// row — the isolation is enforced by Postgres itself, not by
/// <see cref="Raffa.Renewals.Application.RenewalNegotiationTodoService"/>'s own application-level
/// `WHERE tenant_id = ...` filter alone (ADR-009's "belt-and-suspenders"). Mirrors
/// <see cref="RenewalActionRlsCrossTenantIsolationTests"/> exactly, scoped to this module's third
/// table.
///
/// Runs all assertions through a dedicated, deliberately unprivileged Postgres role
/// (<see cref="AppRoleName"/>: `NOSUPERUSER NOBYPASSRLS`, not the table owner) — see that sibling
/// type's own doc comment for why a superuser connection would make this pass vacuously.
/// </summary>
public sealed class RenewalNegotiationTodoRlsCrossTenantIsolationTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_app";
    private const string AppRolePassword = "raffa_app_test_password";

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
            // Applies Initial + AddTenantRowLevelSecurity + AddRenewalAlert + this task's
            // AddRenewalNegotiationTodo migration.
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
    public async Task Tenant_cannot_read_another_tenants_negotiation_todo_row()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        await SeedTodoAsync(tenantA, pointKey: "owned-by-tenant-a");
        await SeedTodoAsync(tenantB, pointKey: "owned-by-tenant-b");

        var tenantContext = new TenantContext();
        using (tenantContext.BeginScope(tenantA))
        {
            await using var db = CreateAppContext(tenantContext);

            // ADR-009: tenant B's row exists (seeded above, over the same table) but RLS makes it
            // invisible on a connection scoped to tenant A.
            var visible = await db.RenewalNegotiationTodos.ToListAsync();

            var visibleRow = Assert.Single(visible);
            Assert.Equal("owned-by-tenant-a", visibleRow.PointKey);
            Assert.Equal(tenantA, visibleRow.TenantId);
        }
    }

    [Fact]
    public async Task No_active_tenant_scope_sees_zero_rows()
    {
        await SeedTodoAsync(TenantId.New(), pointKey: "belongs-to-someone-else");

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);

        var visible = await db.RenewalNegotiationTodos.ToListAsync();

        Assert.Empty(visible);
    }

    [Fact]
    public async Task Cannot_write_a_row_claiming_a_different_tenant_than_the_active_scope()
    {
        var activeScope = TenantId.New();
        var claimedOnRow = TenantId.New(); // deliberately different from the active scope.

        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(activeScope);
        await using var db = CreateAppContext(tenantContext);

        db.RenewalNegotiationTodos.Add(NewTodo(claimedOnRow, "point-key"));

        // ADR-009: the policy's WITH CHECK, not just USING, must reject a write for a tenant other
        // than the one the connection is scoped to.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private async Task SeedTodoAsync(TenantId tenantId, string pointKey)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        db.RenewalNegotiationTodos.Add(NewTodo(tenantId, pointKey));
        await db.SaveChangesAsync();
    }

    private static RenewalNegotiationTodo NewTodo(TenantId tenantId, string pointKey) => new()
    {
        TenantId = tenantId,
        ContractId = EntityId.New(),
        PointKey = pointKey,
        Topic = "Topic",
        Rank = 1,
        Current = "current",
        Target = "target",
        Rationale = "rationale",
        CitationKeys = [],
        Source = "ask",
        Status = RenewalNegotiationTodoStatus.Open,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
