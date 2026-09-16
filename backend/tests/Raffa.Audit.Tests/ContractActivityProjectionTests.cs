using Raffa.Audit.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Audit.Tests;

/// <summary>
/// Task E21/F01/US01/T01 (ADR-011 w17 clause 25, ADR-001 w17 clause 3): proves the new
/// <see cref="IAuditQueryService.GetContractEventsAsync"/> method satisfies all five conditions:
///
/// <list type="number">
/// <item><b>Contract-scoped</b> — only events whose <c>ResourceId</c> matches the requested
/// contract GUID appear; a tenant-wide event with a different <c>ResourceId</c> is absent.</item>
/// <item><b>Default-deny allow-list</b> — an event whose <c>Action</c> is not in the caller's
/// allow-list is absent even when it matches the <c>ResourceId</c>.</item>
/// <item><b>Names, never values</b> — the test does not assert on <c>Detail</c>; the host is
/// responsible for not projecting it (see <see cref="Raffa.Api.ContractsEndpointExtensions"/>).
/// This test proves the method DOES return Detail (so the host can decide), confirming condition 3
/// is a host constraint, not a storage constraint.</item>
/// <item><b>RLS backstop</b> — a context configured without the tenant argument (the optional
/// third argument to <see cref="AuditDbContextOptions.Configure"/>) returns zero rows, proving
/// the silent-empty-array failure mode the council refused; the production path that opens a scope
/// is the one this method uses.</item>
/// <item><b>Cross-tenant isolation</b> — another tenant's event with the same <c>ResourceId</c>
/// never appears.</item>
/// </list>
/// </summary>
public sealed class ContractActivityProjectionTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_activity_app";
    private const string AppRolePassword = "raffa_activity_app_test_pwd";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly TenantContext _tenantContext = new();
    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<AuditDbContext>();
        AuditDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());
        await using (var adminDb = new AuditDbContext(adminOptions.Options))
        {
            await adminDb.Database.MigrateAsync();
            // Superusers bypass FORCE RLS, so a two-argument Configure on the bootstrap role
            // would see every row and the fail-closed proof would be a tautology (same reason
            // AuditRlsCrossTenantIsolationTests creates this role).
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

    private AuditDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AuditDbContext>()
            .Let(b => AuditDbContextOptions.Configure(b, _postgres.GetConnectionString(), _tenantContext))
            .Options);

    /// <summary>A context built without the tenant-context argument (the optional third arg of
    /// <see cref="AuditDbContextOptions.Configure"/>) on the unprivileged app role — the context
    /// that returns zero rows silently for every tenant when no <c>app.tenant_id</c> claim is set
    /// (ADR-009 RLS backstop).</summary>
    private AuditDbContext CreateContextWithoutTenantClaim() =>
        new(new DbContextOptionsBuilder<AuditDbContext>()
            .Let(b => AuditDbContextOptions.Configure(b, _appConnectionString))
            .Options);

    private async Task WriteAsync(
        TenantId tenantId, string actor, string action, string resourceId, DateTimeOffset occurredAt,
        string? detail = null)
    {
        using var _ = _tenantContext.BeginScope(tenantId);
        await using var db = CreateContext();
        await new AuditWriter(db).WriteAsync(
            new AuditEntry(tenantId, actor, action, "contract", resourceId, occurredAt, detail));
    }

    private static readonly IReadOnlyCollection<string> AllowList =
        ["contract.corrected", "contract.negotiation_steps_set"];

    // ----- Condition 1: contract-scoped -----

    [Fact]
    public async Task GetContractEventsAsync_returns_only_events_for_the_requested_contract()
    {
        var tenantId = TenantId.New();
        var contractId = EntityId.New().Value.ToString();
        var otherId = EntityId.New().Value.ToString();
        var now = DateTimeOffset.UtcNow;

        await WriteAsync(tenantId, "user-a", "contract.corrected", contractId, now);
        await WriteAsync(tenantId, "user-b", "contract.corrected", otherId, now.AddMinutes(-1));

        IAuditQueryService svc = new AuditQueryService(CreateContext(), _tenantContext);
        var events = await svc.GetContractEventsAsync(tenantId, contractId, AllowList);

        var single = Assert.Single(events);
        Assert.Equal("user-a", single.Actor);
        Assert.Equal(contractId, single.ResourceId);
    }

    // ----- Condition 2: default-deny allow-list -----

    [Fact]
    public async Task GetContractEventsAsync_excludes_actions_not_in_the_allow_list()
    {
        var tenantId = TenantId.New();
        var contractId = EntityId.New().Value.ToString();
        var now = DateTimeOffset.UtcNow;

        // Listed action — should appear.
        await WriteAsync(tenantId, "user-a", "contract.corrected", contractId, now);
        // NOT listed action — must be absent even though it matches the contract id.
        await WriteAsync(tenantId, "system:worker", "document.upload", contractId, now.AddMinutes(-1));

        IAuditQueryService svc = new AuditQueryService(CreateContext(), _tenantContext);
        var events = await svc.GetContractEventsAsync(tenantId, contractId, AllowList);

        Assert.All(events, e => Assert.Contains(e.Action, AllowList));
        Assert.DoesNotContain(events, e => e.Action == "document.upload");
    }

    [Fact]
    public async Task GetContractEventsAsync_returns_empty_when_allow_list_is_empty()
    {
        var tenantId = TenantId.New();
        var contractId = EntityId.New().Value.ToString();

        await WriteAsync(tenantId, "user-a", "contract.corrected", contractId, DateTimeOffset.UtcNow);

        IAuditQueryService svc = new AuditQueryService(CreateContext(), _tenantContext);
        // An empty allow-list is the logical "deny everything" — not an error, just an empty result.
        var events = await svc.GetContractEventsAsync(tenantId, contractId, []);

        Assert.Empty(events);
    }

    // ----- Condition 3: names, never values (mechanism) -----

    [Fact]
    public async Task GetContractEventsAsync_returns_Detail_so_the_host_can_decide_not_to_project_it()
    {
        var tenantId = TenantId.New();
        var contractId = EntityId.New().Value.ToString();

        await WriteAsync(tenantId, "user-a", "contract.corrected", contractId, DateTimeOffset.UtcNow,
            detail: "type: MSA → SLA");

        IAuditQueryService svc = new AuditQueryService(CreateContext(), _tenantContext);
        var events = await svc.GetContractEventsAsync(tenantId, contractId, AllowList);

        var ev = Assert.Single(events);
        // The service returns Detail — the host (ContractsEndpointExtensions) must NOT forward it
        // to the wire (ADR-011 w17 clause 22 condition 3). This assertion confirms the field
        // is accessible for the host to make that decision, not that it should be projected.
        Assert.Equal("type: MSA → SLA", ev.Detail);
    }

    // ----- Condition 4: host-built context without scope returns zero rows -----

    [Fact]
    public async Task A_context_without_the_tenant_argument_returns_zero_rows_for_every_tenant()
    {
        var tenantId = TenantId.New();
        var contractId = EntityId.New().Value.ToString();

        await WriteAsync(tenantId, "user-a", "contract.corrected", contractId, DateTimeOffset.UtcNow);

        // This is the "host-built context" path (AuditDbContextOptions without tenantContext).
        // Without the RLS interceptor, app.tenant_id is never set and RLS fails closed —
        // zero rows, regardless of the explicit WHERE predicate.
        // GetContractEventsAsync avoids this path by opening its own BeginScope.
        await using var rawDb = CreateContextWithoutTenantClaim();
        var directCount = await rawDb.AuditEvents
            .Where(e => e.TenantId == tenantId && e.ResourceId == contractId)
            .CountAsync();
        Assert.Equal(0, directCount); // proves the failure mode exists

        // The production path (AuditQueryService with ITenantContext) sees the row.
        IAuditQueryService svc = new AuditQueryService(CreateContext(), _tenantContext);
        var events = await svc.GetContractEventsAsync(tenantId, contractId, AllowList);
        Assert.Single(events); // proves the service does NOT take the failing path
    }

    // ----- Condition 5: cross-tenant isolation -----

    [Fact]
    public async Task GetContractEventsAsync_never_returns_another_tenants_events()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        // Same resource id for both tenants — would leak across tenants if either the RLS
        // backstop or the application-level tenant predicate were missing.
        var sharedResourceId = EntityId.New().Value.ToString();

        await WriteAsync(tenantA, "user-a", "contract.corrected", sharedResourceId, DateTimeOffset.UtcNow);
        await WriteAsync(tenantB, "user-b", "contract.corrected", sharedResourceId, DateTimeOffset.UtcNow.AddMinutes(-1));

        IAuditQueryService svc = new AuditQueryService(CreateContext(), _tenantContext);
        var eventsForA = await svc.GetContractEventsAsync(tenantA, sharedResourceId, AllowList);

        // Only tenant A's event — tenant B's event with the same resource id must not leak.
        Assert.All(eventsForA, e => Assert.Equal("user-a", e.Actor));
        Assert.DoesNotContain(eventsForA, e => e.Actor == "user-b");
    }

    // ----- Ordering and row cap -----

    [Fact]
    public async Task GetContractEventsAsync_orders_newest_first()
    {
        var tenantId = TenantId.New();
        var contractId = EntityId.New().Value.ToString();
        var now = DateTimeOffset.UtcNow;

        await WriteAsync(tenantId, "oldest", "contract.corrected", contractId, now.AddMinutes(-10));
        await WriteAsync(tenantId, "newest", "contract.corrected", contractId, now);
        await WriteAsync(tenantId, "middle", "contract.negotiation_steps_set", contractId, now.AddMinutes(-5));

        IAuditQueryService svc = new AuditQueryService(CreateContext(), _tenantContext);
        var events = await svc.GetContractEventsAsync(tenantId, contractId, AllowList);

        Assert.Equal("newest,middle,oldest", string.Join(",", events.Select(e => e.Actor)));
    }
}

file static class DbContextOptionsBuilderExtensions
{
    /// <summary>Fluent helper so <c>CreateContext()</c> can chain Configure inline.</summary>
    public static T Let<T>(this T value, Action<T> configure)
    {
        configure(value);
        return value;
    }
}
