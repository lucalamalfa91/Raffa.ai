using Raffa.Renewals.Application;
using Raffa.Renewals.Domain;
using Raffa.Renewals.Infrastructure;
using Raffa.Renewals.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Renewals.Tests;

/// <summary>
/// Proves the Definition of Done for task E29/F01/US01/T01 (todo-entity-api; parent story
/// us-01-todo-entity-api AC-1/AC-3): <see cref="RenewalNegotiationTodoService"/> genuinely persists
/// through the same Npgsql/EF Core/RLS-interceptor pipeline every other module's write path uses
/// (ADR-003/ADR-009), validates every point before writing anything, and honours the idempotent
/// upsert rule verbatim from the task text ("same point_key updates current/target/rationale/rank;
/// never un-ticks Done; vanished → Superseded"). Mirrors <see cref="RenewalActionServiceTests"/>'s
/// own Testcontainers shape; tenant-isolation-under-RLS is proved separately by
/// <see cref="RenewalNegotiationTodoRlsCrossTenantIsolationTests"/> (same split
/// <c>RenewalActionServiceTests</c>/<c>RenewalActionRlsCrossTenantIsolationTests</c> already use).
/// </summary>
public sealed class RenewalNegotiationTodoServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private RenewalsDbContext CreateContext(ITenantContext? tenantContext = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RenewalsDbContext>();
        RenewalsDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString(), tenantContext);
        return new RenewalsDbContext(optionsBuilder.Options);
    }

    private async Task MigrateAsync()
    {
        await using var migrateDb = CreateContext();
        await migrateDb.Database.MigrateAsync();
    }

    private static RenewalNegotiationTodoPoint Point(
        string pointKey, int rank = 1, string current = "current", string target = "target",
        string rationale = "rationale", params string[] citationKeys) =>
        new(pointKey, $"Topic for {pointKey}", rank, current, target, rationale, citationKeys);

    [Fact]
    public async Task UpsertAsync_persists_new_rows_readable_through_GetAsync_ordered_by_rank()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));
        var auditWriter = new RecordingAuditWriter();

        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalNegotiationTodoService(db, tenantContext, clock, auditWriter);

            var result = await service.UpsertAsync(
                tenantId,
                contractId,
                [
                    Point("above_band_price", rank: 1, citationKeys: ["fact:1:unitPrice", "market:42"]),
                    Point("payment_terms", rank: 2),
                ],
                "actor@acme.example");

            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Value.Count);
        }

        await using var readDb = CreateContext(tenantContext);
        var readService = new RenewalNegotiationTodoService(readDb, tenantContext, clock, auditWriter);
        var stored = await readService.GetAsync(tenantId, contractId);

        Assert.Equal(2, stored.Count);
        Assert.Equal("above_band_price", stored[0].PointKey); // rank 1 first
        Assert.Equal(["fact:1:unitPrice", "market:42"], stored[0].CitationKeys);
        Assert.Equal(RenewalNegotiationTodoStatus.Open, stored[0].Status);
        Assert.Equal("ask", stored[0].Source);
        Assert.Equal("payment_terms", stored[1].PointKey);

        // Spec §14.1 / Appendix C rule 9: one audit entry per call, actor = resolved token subject.
        var entry = Assert.Single(auditWriter.Written);
        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal("renewal.negotiation_todos_written", entry.Action);
        Assert.Equal("renewal", entry.ResourceType);
        Assert.Equal(contractId.Value.ToString(), entry.ResourceId);
        Assert.Equal("actor@acme.example", entry.Actor);
    }

    [Fact]
    public async Task UpsertAsync_updates_current_target_rationale_rank_for_the_same_point_key()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();

        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalNegotiationTodoService(
                db, tenantContext, new FixedClock(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)), auditWriter);
            await service.UpsertAsync(
                tenantId, contractId,
                [Point("above_band_price", rank: 3, current: "old current", target: "old target", rationale: "old rationale")],
                "actor@acme.example");
        }

        var secondClock = new FixedClock(new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero));
        IReadOnlyList<RenewalNegotiationTodoResult> updated;
        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalNegotiationTodoService(db, tenantContext, secondClock, auditWriter);
            var result = await service.UpsertAsync(
                tenantId, contractId,
                [Point("above_band_price", rank: 1, current: "new current", target: "new target", rationale: "new rationale",
                    citationKeys: "fact:2:x")],
                "actor@acme.example");
            Assert.True(result.IsSuccess);
            updated = result.Value;
        }

        await using var readDb = CreateContext(tenantContext);
        var rows = await readDb.RenewalNegotiationTodos.Where(t => t.TenantId == tenantId).ToListAsync();

        var row = Assert.Single(rows); // upsert, not a second row for the same (tenant, contract, pointKey).
        Assert.Equal(1, row.Rank);
        Assert.Equal("new current", row.Current);
        Assert.Equal("new target", row.Target);
        Assert.Equal("new rationale", row.Rationale);
        Assert.Equal(["fact:2:x"], row.CitationKeys);
        Assert.Equal(secondClock.UtcNow, row.UpdatedAt);
        Assert.Single(updated);
    }

    [Fact]
    public async Task UpsertAsync_never_rewrites_topic_on_an_existing_row()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalNegotiationTodoService(db, tenantContext, clock, auditWriter);
            await service.UpsertAsync(
                tenantId, contractId,
                [new RenewalNegotiationTodoPoint("above_band_price", "Original topic", 1, "c", "t", "r", [])],
                "actor@acme.example");
        }

        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalNegotiationTodoService(db, tenantContext, clock, auditWriter);
            await service.UpsertAsync(
                tenantId, contractId,
                [new RenewalNegotiationTodoPoint("above_band_price", "Rewritten topic", 1, "c2", "t2", "r2", [])],
                "actor@acme.example");
        }

        await using var readDb = CreateContext(tenantContext);
        var row = await readDb.RenewalNegotiationTodos.SingleAsync(t => t.TenantId == tenantId);
        Assert.Equal("Original topic", row.Topic);
    }

    [Fact]
    public async Task UpsertAsync_never_un_ticks_a_done_row_and_freezes_its_content()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalNegotiationTodoService(db, tenantContext, clock, auditWriter);
            await service.UpsertAsync(
                tenantId, contractId,
                [Point("above_band_price", rank: 1, current: "before done")],
                "actor@acme.example");
            var tick = await service.SetDoneAsync(tenantId, contractId, "above_band_price", "actor@acme.example");
            Assert.True(tick.IsSuccess);
            Assert.Equal(RenewalNegotiationTodoStatus.Done, tick.Value!.Status);
        }

        // A later ask re-ranks the same point with new content -- must not un-tick or rewrite it.
        var secondClock = new FixedClock(new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));
        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalNegotiationTodoService(db, tenantContext, secondClock, auditWriter);
            await service.UpsertAsync(
                tenantId, contractId,
                [Point("above_band_price", rank: 5, current: "after done -- must not apply")],
                "actor@acme.example");
        }

        await using var readDb = CreateContext(tenantContext);
        var row = await readDb.RenewalNegotiationTodos.SingleAsync(t => t.TenantId == tenantId);
        Assert.Equal(RenewalNegotiationTodoStatus.Done, row.Status);
        Assert.Equal("before done", row.Current); // frozen, not rewritten to "after done".
        Assert.Equal(1, row.Rank); // frozen.
    }

    [Fact]
    public async Task UpsertAsync_supersedes_a_row_whose_point_key_is_absent_from_the_new_set()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();

        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalNegotiationTodoService(
                db, tenantContext, new FixedClock(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)), auditWriter);
            await service.UpsertAsync(
                tenantId, contractId,
                [Point("above_band_price", rank: 1), Point("payment_terms", rank: 2)],
                "actor@acme.example");
        }

        // Next ask no longer grounds "payment_terms" for this contract.
        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalNegotiationTodoService(
                db, tenantContext, new FixedClock(new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero)), auditWriter);
            var result = await service.UpsertAsync(
                tenantId, contractId, [Point("above_band_price", rank: 1)], "actor@acme.example");
            Assert.True(result.IsSuccess);
        }

        await using var readDb = CreateContext(tenantContext);
        var rows = await readDb.RenewalNegotiationTodos.Where(t => t.TenantId == tenantId).ToListAsync();

        Assert.Equal(2, rows.Count); // superseded, not deleted.
        var vanished = rows.Single(r => r.PointKey == "payment_terms");
        Assert.Equal(RenewalNegotiationTodoStatus.Superseded, vanished.Status);
        var stillTracked = rows.Single(r => r.PointKey == "above_band_price");
        Assert.Equal(RenewalNegotiationTodoStatus.Open, stillTracked.Status);
    }

    [Fact]
    public async Task UpsertAsync_reopens_a_superseded_row_that_reappears_in_a_later_ranked_set()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();

        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalNegotiationTodoService(
                db, tenantContext, new FixedClock(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)), auditWriter);
            await service.UpsertAsync(
                tenantId, contractId,
                [Point("above_band_price", rank: 1), Point("payment_terms", rank: 2)],
                "actor@acme.example");
            // Vanishes.
            await service.UpsertAsync(tenantId, contractId, [Point("above_band_price", rank: 1)], "actor@acme.example");
        }

        // Reappears with fresh content.
        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalNegotiationTodoService(
                db, tenantContext, new FixedClock(new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero)), auditWriter);
            await service.UpsertAsync(
                tenantId, contractId,
                [Point("above_band_price", rank: 1), Point("payment_terms", rank: 2, current: "reappeared")],
                "actor@acme.example");
        }

        await using var readDb = CreateContext(tenantContext);
        var reappeared = await readDb.RenewalNegotiationTodos
            .SingleAsync(t => t.TenantId == tenantId && t.PointKey == "payment_terms");

        Assert.Equal(RenewalNegotiationTodoStatus.Open, reappeared.Status);
        Assert.Equal("reappeared", reappeared.Current);
    }

    [Fact]
    public async Task SetDoneAsync_ticks_an_existing_row_and_writes_the_audit_entry()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.Zero));

        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalNegotiationTodoService(db, tenantContext, clock, auditWriter);
            await service.UpsertAsync(tenantId, contractId, [Point("above_band_price")], "ask-actor@acme.example");
        }

        await using var db2 = CreateContext(tenantContext);
        var tickService = new RenewalNegotiationTodoService(db2, tenantContext, clock, auditWriter);
        var result = await tickService.SetDoneAsync(tenantId, contractId, "above_band_price", "human@acme.example");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(RenewalNegotiationTodoStatus.Done, result.Value!.Status);

        var auditEntry = Assert.Single(auditWriter.Written, e => e.Actor == "human@acme.example");
        Assert.Equal("renewal.negotiation_todos_written", auditEntry.Action);
        Assert.Equal(contractId.Value.ToString(), auditEntry.ResourceId);
    }

    [Fact]
    public async Task SetDoneAsync_never_invents_a_point_and_returns_a_successful_null_instead()
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new RenewalNegotiationTodoService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.SetDoneAsync(TenantId.New(), EntityId.New(), "no_such_point", "actor@acme.example");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(0, await db.RenewalNegotiationTodos.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetDoneAsync_rejects_a_blank_point_key(string? pointKey)
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new RenewalNegotiationTodoService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.SetDoneAsync(TenantId.New(), EntityId.New(), pointKey, "actor@acme.example");

        Assert.True(result.IsFailure);
        Assert.Equal(RenewalNegotiationTodoService.PointKeyRequiredError, result.Error);
    }

    [Fact]
    public async Task UpsertAsync_rejects_an_empty_point_set_without_writing_anything()
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateContext(tenantContext);
        var service = new RenewalNegotiationTodoService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);

        var result = await service.UpsertAsync(
            TenantId.New(), EntityId.New(), [], "actor@acme.example");

        Assert.True(result.IsFailure);
        Assert.Equal(RenewalNegotiationTodoService.PointsRequiredError, result.Error);
        Assert.Empty(auditWriter.Written);
        Assert.Equal(0, await db.RenewalNegotiationTodos.CountAsync());
    }

    [Fact]
    public async Task UpsertAsync_rejects_duplicate_point_keys_in_the_same_call_without_writing_anything()
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateContext(tenantContext);
        var service = new RenewalNegotiationTodoService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);

        var result = await service.UpsertAsync(
            TenantId.New(), EntityId.New(),
            [Point("above_band_price", rank: 1), Point("above_band_price", rank: 2)],
            "actor@acme.example");

        Assert.True(result.IsFailure);
        Assert.Equal(RenewalNegotiationTodoService.DuplicatePointKeyError, result.Error);
        Assert.Empty(auditWriter.Written);
        Assert.Equal(0, await db.RenewalNegotiationTodos.CountAsync());
    }

    [Fact]
    public async Task UpsertAsync_rejects_a_blank_field_without_writing_anything()
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateContext(tenantContext);
        var service = new RenewalNegotiationTodoService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);

        var result = await service.UpsertAsync(
            TenantId.New(), EntityId.New(),
            [new RenewalNegotiationTodoPoint("above_band_price", "Topic", 1, "  ", "target", "rationale", [])],
            "actor@acme.example");

        Assert.True(result.IsFailure);
        Assert.Equal(RenewalNegotiationTodoService.CurrentRequiredError, result.Error);
        Assert.Empty(auditWriter.Written);
        Assert.Equal(0, await db.RenewalNegotiationTodos.CountAsync());
    }

    [Fact]
    public async Task GetAsync_returns_an_empty_list_when_nothing_was_ever_upserted()
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new RenewalNegotiationTodoService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.GetAsync(TenantId.New(), EntityId.New());

        Assert.Empty(result);
    }
}
