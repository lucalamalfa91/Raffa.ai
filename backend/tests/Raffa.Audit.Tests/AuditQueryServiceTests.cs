using Raffa.Audit.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Audit.Tests;

/// <summary>
/// Proves the Definition of Done for task E01/F06/US02/T02 (story us-02-audit-baseline, AC-2):
/// <see cref="AuditQueryService"/> — this module's <see cref="IAuditQueryService"/> implementation,
/// the produced `audit-query` artifact — returns only the requested tenant's events, newest first,
/// correctly mapped to <see cref="AuditEventRecord"/>.
///
/// Uses the same default (Testcontainers superuser) connection <c>AuditWriterTests</c> uses rather
/// than the dedicated unprivileged role <c>AuditRlsCrossTenantIsolationTests</c> stands up: this
/// class proves <see cref="AuditQueryService"/>'s own filtering/ordering/mapping logic, not RLS
/// cross-tenant enforcement — task E01/F06/US02/T01's own tests already cover RLS exhaustively, and
/// <c>AuditRlsCrossTenantIsolationTests</c> now also covers this exact query path end-to-end under
/// the restricted application role.
/// </summary>
public sealed class AuditQueryServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly TenantContext _tenantContext = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AuditDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<AuditDbContext>();
        AuditDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString(), _tenantContext);
        return new AuditDbContext(optionsBuilder.Options);
    }

    private async Task WriteEventAsync(
        TenantId tenantId,
        string actor,
        string action,
        string resourceType,
        string resourceId,
        DateTimeOffset occurredAt,
        string? detail = null)
    {
        using var _ = _tenantContext.BeginScope(tenantId);
        await using var db = CreateContext();

        IAuditWriter writer = new AuditWriter(db);
        await writer.WriteAsync(
            new AuditEntry(tenantId, actor, action, resourceType, resourceId, occurredAt, detail));
    }

    [Fact]
    public async Task GetEventsAsync_returns_only_events_for_the_requested_tenant()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        await WriteEventAsync(tenantA, "user-a", "document.upload", "Document", "doc-a", DateTimeOffset.UtcNow);
        await WriteEventAsync(tenantB, "user-b", "document.upload", "Document", "doc-b", DateTimeOffset.UtcNow);

        IAuditQueryService queryService = new AuditQueryService(CreateContext(), _tenantContext);
        var events = await queryService.GetEventsAsync(tenantA);

        var onlyEvent = Assert.Single(events);
        Assert.Equal("doc-a", onlyEvent.ResourceId);
    }

    [Fact]
    public async Task GetEventsAsync_maps_every_field()
    {
        var tenantId = TenantId.New();
        var occurredAt = new DateTimeOffset(2026, 9, 3, 9, 30, 0, TimeSpan.Zero);

        await WriteEventAsync(
            tenantId, "user-123@acme.example", "document.upload", "Document", "doc-1", occurredAt,
            "uploaded via web");

        IAuditQueryService queryService = new AuditQueryService(CreateContext(), _tenantContext);
        var events = await queryService.GetEventsAsync(tenantId);

        var stored = Assert.Single(events);
        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal("user-123@acme.example", stored.Actor);
        Assert.Equal("document.upload", stored.Action);
        Assert.Equal("Document", stored.ResourceType);
        Assert.Equal("doc-1", stored.ResourceId);
        Assert.Equal(occurredAt, stored.OccurredAt);
        Assert.Equal("uploaded via web", stored.Detail);
    }

    [Fact]
    public async Task GetEventsAsync_orders_newest_first()
    {
        var tenantId = TenantId.New();
        var now = DateTimeOffset.UtcNow;

        await WriteEventAsync(tenantId, "user-1", "document.upload", "Document", "oldest", now.AddMinutes(-10));
        await WriteEventAsync(tenantId, "user-1", "document.upload", "Document", "newest", now);
        await WriteEventAsync(tenantId, "user-1", "document.upload", "Document", "middle", now.AddMinutes(-5));

        IAuditQueryService queryService = new AuditQueryService(CreateContext(), _tenantContext);
        var events = await queryService.GetEventsAsync(tenantId);

        Assert.Equal("newest,middle,oldest", string.Join(",", events.Select(e => e.ResourceId)));
    }

    [Fact]
    public async Task GetEventsAsync_returns_empty_for_a_tenant_with_no_events()
    {
        await WriteEventAsync(
            TenantId.New(), "user-1", "document.upload", "Document", "belongs-to-someone-else",
            DateTimeOffset.UtcNow);

        IAuditQueryService queryService = new AuditQueryService(CreateContext(), _tenantContext);
        var events = await queryService.GetEventsAsync(TenantId.New());

        Assert.Empty(events);
    }
}
