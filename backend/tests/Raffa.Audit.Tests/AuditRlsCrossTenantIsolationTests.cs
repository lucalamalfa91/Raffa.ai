using Raffa.Audit.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Audit.Tests;

/// <summary>
/// Proves the Definition of Done for task E01/F06/US02/T01 (story us-02-audit-baseline, AC-1):
/// with the RLS policy from migration `AddTenantRowLevelSecurity` applied and
/// <see cref="TenantRlsConnectionInterceptor"/> setting the per-connection `app.tenant_id` claim,
/// one tenant's connection genuinely cannot read (or write) another tenant's audit trail — the
/// isolation is enforced by Postgres itself, not by an application-level `WHERE` clause. Mirrors
/// task E01/F05/US01/T01's own proof for Identity/Workspace
/// (`Raffa.Identity.Workspace.Tests.WorkspaceRlsCrossTenantIsolationTests`).
///
/// Runs all assertions through a dedicated, deliberately unprivileged Postgres role
/// (<see cref="AppRoleName"/>: `NOSUPERUSER NOBYPASSRLS`, not the table owner). The Testcontainers
/// bootstrap role is always a Postgres superuser, and superusers unconditionally bypass row
/// security regardless of policy or `FORCE` — asserting isolation over that connection would pass
/// vacuously. This role stands in for "the application's own database role", so a passing test
/// here is a real proof, not a tautology.
///
/// Also covers task E01/F06/US02/T02 (story us-02-audit-baseline AC-2, the `audit-query`
/// artifact): <see cref="Tenant_cannot_read_another_tenants_audit_events_through_AuditQueryService"/>
/// proves the same guarantee holds through <see cref="AuditQueryService"/> — the exact type
/// `GET /api/audit` calls — not only through <see cref="AuditDbContext"/> directly. And
/// <see cref="GetEventsAsync_returns_the_callers_tenant_even_when_the_caller_never_opened_a_scope"/>
/// proves the actual production shape of that call — nothing upstream of
/// <see cref="AuditQueryService"/> ever opens a tenant scope, see `Raffa.Api.Program` — still
/// sees the caller's own events, i.e. that <see cref="AuditQueryService"/> opens that scope itself
/// rather than relying on one already being active.
/// </summary>
public sealed class AuditRlsCrossTenantIsolationTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_audit_app";
    private const string AppRolePassword = "raffa_audit_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<AuditDbContext>();
        AuditDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new AuditDbContext(adminOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity + AddAppendOnlyEnforcement.
            await adminDb.Database.MigrateAsync();

            // A non-owner, non-superuser, NOBYPASSRLS role: see the type doc comment for why
            // this is required for the test to mean anything.
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

    private AuditDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AuditDbContext>();
        AuditDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new AuditDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Tenant_cannot_read_another_tenants_audit_events()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        await WriteEventAsync(tenantA, resourceId: "owned-by-tenant-a");
        await WriteEventAsync(tenantB, resourceId: "owned-by-tenant-b");

        var tenantContext = new TenantContext();
        using (tenantContext.BeginScope(tenantA))
        {
            await using var db = CreateAppContext(tenantContext);

            // AC-1: tenant B's row exists (seeded above, over the same table) but RLS makes it
            // invisible on a connection scoped to tenant A.
            var visible = await db.AuditEvents.ToListAsync();

            var visibleRow = Assert.Single(visible);
            Assert.Equal("owned-by-tenant-a", visibleRow.ResourceId);
            Assert.Equal(tenantA, visibleRow.TenantId);
        }
    }

    [Fact]
    public async Task Tenant_cannot_read_another_tenants_audit_events_through_AuditQueryService()
    {
        // Same guarantee as Tenant_cannot_read_another_tenants_audit_events, but through
        // AuditQueryService (task E01/F06/US02/T02's `audit-query` artifact) — the exact type
        // `GET /api/audit` calls — under the same unprivileged, NOBYPASSRLS application role, so
        // the proof covers the real query path end to end, not only AuditDbContext directly.
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        await WriteEventAsync(tenantA, resourceId: "owned-by-tenant-a");
        await WriteEventAsync(tenantB, resourceId: "owned-by-tenant-b");

        var tenantContext = new TenantContext();
        using (tenantContext.BeginScope(tenantA))
        {
            await using var db = CreateAppContext(tenantContext);
            IAuditQueryService queryService = new AuditQueryService(db, tenantContext);

            var visible = await queryService.GetEventsAsync(tenantA);

            var visibleRow = Assert.Single(visible);
            Assert.Equal("owned-by-tenant-a", visibleRow.ResourceId);
        }
    }

    [Fact]
    public async Task GetEventsAsync_returns_the_callers_tenant_even_when_the_caller_never_opened_a_scope()
    {
        // Regression test: unlike Tenant_cannot_read_another_tenants_audit_events_through_
        // AuditQueryService above, this test itself never calls tenantContext.BeginScope — that
        // is the actual shape of the real GET /api/audit call. AuditEndpointExtensions.
        // GetAuditEventsAsync resolves a tenantId from the caller's claims and passes it straight
        // to IAuditQueryService.GetEventsAsync; nothing upstream (no middleware — see
        // Raffa.Api.Program, which wires none) ever opens a tenant scope first. If
        // AuditQueryService.GetEventsAsync did not open its own scope, this would see zero rows
        // under the restricted role — same fail-closed RLS behaviour as
        // No_active_tenant_scope_sees_zero_audit_events below — even for the tenant's own,
        // legitimate query. This is the test that would have caught that gap.
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        await WriteEventAsync(tenantA, resourceId: "owned-by-tenant-a");
        await WriteEventAsync(tenantB, resourceId: "owned-by-tenant-b");

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        IAuditQueryService queryService = new AuditQueryService(db, tenantContext);

        // No BeginScope call anywhere in this test: GetEventsAsync must establish its own.
        var visible = await queryService.GetEventsAsync(tenantA);

        var visibleRow = Assert.Single(visible);
        Assert.Equal("owned-by-tenant-a", visibleRow.ResourceId);
    }

    [Fact]
    public async Task No_active_tenant_scope_sees_zero_audit_events()
    {
        await WriteEventAsync(TenantId.New(), resourceId: "belongs-to-someone-else");

        // No BeginScope entered: ITenantContext.Current is null, so the interceptor leaves
        // app.tenant_id unset. current_setting(..., true) then returns NULL, and
        // `tenant_id = NULL` is never true — fail closed, zero rows visible to anyone.
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);

        var visible = await db.AuditEvents.ToListAsync();

        Assert.Empty(visible);
    }

    [Fact]
    public async Task Cannot_write_an_audit_event_claiming_a_different_tenant_than_the_active_scope()
    {
        var activeScope = TenantId.New();
        var claimedOnEntry = TenantId.New(); // deliberately different from the active scope.

        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(activeScope);
        await using var db = CreateAppContext(tenantContext);

        var entry = new AuditEntry(
            claimedOnEntry, "cross-tenant-writer", "document.upload", "Document",
            "forged-resource", DateTimeOffset.UtcNow);

        IAuditWriter writer = new AuditWriter(db);

        // AC-1/ADR-009: the policy's WITH CHECK, not just USING, must reject a write for a
        // tenant other than the one the connection is scoped to — the backstop covers writes,
        // not only reads, even when it is IAuditWriter itself issuing the write.
        await Assert.ThrowsAsync<DbUpdateException>(() => writer.WriteAsync(entry));
    }

    private async Task WriteEventAsync(TenantId tenantId, string resourceId)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        IAuditWriter writer = new AuditWriter(db);
        await writer.WriteAsync(new AuditEntry(
            tenantId, "seed-actor", "seed.action", "SeedResource", resourceId, DateTimeOffset.UtcNow));
    }
}
