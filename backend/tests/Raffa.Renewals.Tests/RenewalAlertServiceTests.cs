using Raffa.Renewals.Application;
using Raffa.Renewals.Configuration;
using Raffa.Renewals.Domain;
using Raffa.Renewals.Infrastructure;
using Raffa.Renewals.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Renewals.Tests;

/// <summary>
/// Proves the Definition of Done for task E03/F02/US01/T02 (renewal-alerts; parent story
/// us-01-threshold-scheduler AC-2/AC-3): <see cref="RenewalAlertService"/> genuinely persists a
/// de-duplicated <see cref="RenewalAlert"/> row per raised <see cref="RenewalApproachingEvent"/>
/// through the same Npgsql/EF Core/RLS-interceptor pipeline every other module write path uses
/// (ADR-003/ADR-009), never creates a second row for a threshold already alerted on, and correctly
/// resolves/re-raises alerts when a contract's terms are recomputed after a correction. Mirrors
/// <see cref="RenewalActionServiceTests"/>'s own Testcontainers shape; tenant-isolation-under-RLS is
/// proved separately by <see cref="RenewalAlertRlsCrossTenantIsolationTests"/> (same split
/// <c>RenewalActionServiceTests</c>/<c>RenewalActionRlsCrossTenantIsolationTests</c> already
/// established for the sibling entity).
/// </summary>
public sealed class RenewalAlertServiceTests : IAsyncLifetime
{
    private static readonly DateOnly AsOf = new(2026, 1, 1);

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

    private static RenewalAlertService CreateService(
        RenewalsDbContext db, ITenantContext tenantContext, IClock clock, RecordingAuditWriter auditWriter) =>
        new(
            db, tenantContext, clock, auditWriter,
            new RenewalEngine(clock),
            new RenewalThresholdScheduler(clock, new RenewalEngine(clock), auditWriter, new ThresholdWindowOptions(), tenantContext));

    private static RenewalApproachingEvent Event(
        TenantId tenantId, EntityId contractId, RenewalMilestoneKind milestone, int thresholdDays, DateOnly milestoneDate,
        DateTimeOffset occurredAt) => new()
        {
            TenantId = tenantId,
            OccurredAt = occurredAt,
            ContractId = contractId,
            Milestone = milestone,
            MilestoneDate = milestoneDate,
            ThresholdDays = thresholdDays,
            DaysRemaining = thresholdDays,
        };

    [Fact]
    public async Task CreateFromEventsAsync_persists_a_new_alert_readable_back_and_writes_one_audit_entry()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var auditWriter = new RecordingAuditWriter();
        var raised = Event(tenantId, contractId, RenewalMilestoneKind.RenewalDate, 90, AsOf.AddDays(90), clock.UtcNow);

        await using var db = CreateContext(tenantContext);
        var service = CreateService(db, tenantContext, clock, auditWriter);

        var results = await service.CreateFromEventsAsync(tenantId, [raised]);

        var result = Assert.Single(results);
        Assert.Equal(contractId, result.ContractId);
        Assert.Equal(RenewalMilestoneKind.RenewalDate, result.Milestone);
        Assert.Equal(90, result.ThresholdDays);
        Assert.Equal(AsOf.AddDays(90), result.MilestoneDate);
        Assert.Equal(RenewalAlertStatus.Active, result.Status);

        var stored = Assert.Single(await db.RenewalAlerts.Where(a => a.TenantId == tenantId).ToListAsync());
        Assert.Equal(contractId, stored.ContractId);
        Assert.Equal(RenewalAlertStatus.Active, stored.Status);
        Assert.Equal(clock.UtcNow, stored.CreatedAt);
        Assert.Equal(clock.UtcNow, stored.UpdatedAt);

        var entry = Assert.Single(auditWriter.Written);
        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal(RenewalAlertService.AuditCreatedAction, entry.Action);
        Assert.Equal("renewal.alert_created", entry.Action);
        Assert.Equal("renewal_alert", entry.ResourceType);
        Assert.Equal(contractId.Value.ToString(), entry.ResourceId);
        Assert.Equal(RenewalAlertService.AlertServiceActor, entry.Actor);
    }

    [Fact]
    public async Task CreateFromEventsAsync_does_not_create_a_second_row_for_a_threshold_already_alerted()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var auditWriter = new RecordingAuditWriter();
        var raised = Event(tenantId, contractId, RenewalMilestoneKind.RenewalDate, 90, AsOf.AddDays(90), clock.UtcNow);

        await using var db = CreateContext(tenantContext);
        var service = CreateService(db, tenantContext, clock, auditWriter);

        await service.CreateFromEventsAsync(tenantId, [raised]);
        var secondCallResults = await service.CreateFromEventsAsync(tenantId, [raised]);

        Assert.Empty(secondCallResults);
        Assert.Single(await db.RenewalAlerts.Where(a => a.TenantId == tenantId).ToListAsync());
        Assert.Single(auditWriter.Written); // still just the first call's own entry.
    }

    [Fact]
    public async Task CreateFromEventsAsync_persists_independent_alerts_for_different_milestones_on_the_same_contract()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var auditWriter = new RecordingAuditWriter();
        var renewalEvent = Event(tenantId, contractId, RenewalMilestoneKind.RenewalDate, 60, AsOf.AddDays(60), clock.UtcNow);
        var cancellationEvent = Event(tenantId, contractId, RenewalMilestoneKind.CancellationDeadline, 60, AsOf.AddDays(60), clock.UtcNow);

        await using var db = CreateContext(tenantContext);
        var service = CreateService(db, tenantContext, clock, auditWriter);

        var results = await service.CreateFromEventsAsync(tenantId, [renewalEvent, cancellationEvent]);

        Assert.Equal(2, results.Count);
        Assert.Equal(2, await db.RenewalAlerts.Where(a => a.TenantId == tenantId).CountAsync());
    }

    [Fact]
    public async Task CreateFromEventsAsync_is_a_no_op_for_an_empty_events_list()
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        await using var db = CreateContext(tenantContext);
        var service = CreateService(db, tenantContext, clock, auditWriter);

        var results = await service.CreateFromEventsAsync(TenantId.New(), []);

        Assert.Empty(results);
        Assert.Empty(auditWriter.Written);
        Assert.Equal(0, await db.RenewalAlerts.CountAsync());
    }

    [Fact]
    public async Task CreateFromEventsAsync_rejects_a_null_events_argument()
    {
        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = CreateService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CreateFromEventsAsync(TenantId.New(), null!));
    }

    [Fact]
    public async Task RecomputeForContractAsync_resolves_a_stale_active_alert_when_the_correction_moves_the_date_off_every_threshold()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var firstClock = new FixedClock(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var auditWriter = new RecordingAuditWriter();

        // Seed one pre-existing Active alert directly, as if an earlier scheduler tick raised it
        // (mirrors RenewalActionRlsCrossTenantIsolationTests.SeedActionAsync's own direct-seed shape).
        await using (var seedDb = CreateContext(tenantContext))
        {
            using var _ = tenantContext.BeginScope(tenantId);
            seedDb.RenewalAlerts.Add(new RenewalAlert
            {
                TenantId = tenantId,
                ContractId = contractId,
                Milestone = RenewalMilestoneKind.RenewalDate,
                ThresholdDays = 90,
                MilestoneDate = AsOf.AddDays(90),
                Status = RenewalAlertStatus.Active,
                CreatedAt = firstClock.UtcNow,
                UpdatedAt = firstClock.UtcNow,
            });
            await seedDb.SaveChangesAsync();
        }

        // A later correction pushes EndDate out to 200 days -- RenewalEngine now computes
        // RenewalDate = AsOf+200, which no longer matches the seeded alert's own MilestoneDate
        // (AsOf+90), and 200 is not one of the default 365/270/180/120/90/60/30 windows either, so
        // this recompute should resolve the stale alert and create nothing new.
        var secondClock = new FixedClock(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(5));
        var terms = new ContractRenewalTerms(contractId, AsOf.AddDays(200), AutoRenewal: true, CancellationNoticeDays: null);

        await using var db = CreateContext(tenantContext);
        var service = CreateService(db, tenantContext, secondClock, auditWriter);

        var outcome = await service.RecomputeForContractAsync(tenantId, terms);

        Assert.Equal(contractId, outcome.ContractId);
        Assert.Equal(1, outcome.ResolvedCount);
        Assert.Equal(0, outcome.CreatedCount);

        var stored = Assert.Single(await db.RenewalAlerts.Where(a => a.TenantId == tenantId).ToListAsync());
        Assert.Equal(RenewalAlertStatus.Resolved, stored.Status);
        Assert.Equal(AsOf.AddDays(90), stored.MilestoneDate); // never rewritten -- honest history.
        Assert.Equal(secondClock.UtcNow, stored.UpdatedAt);

        var entry = Assert.Single(auditWriter.Written);
        Assert.Equal(RenewalAlertService.AuditResolvedAction, entry.Action);
        Assert.Equal("renewal.alert_resolved", entry.Action);
        Assert.Equal(tenantId, entry.TenantId);
    }

    /// <summary>
    /// Terms that recompute to exactly the same date an existing Active alert already carries are
    /// unreachable through the real correction path (<c>ContractCorrectionService.CorrectAsync</c>
    /// never records a no-op field change, and <see cref="RenewalEngine.Calculate"/>'s
    /// <c>RenewalDate = EndDate</c> rule means a real <c>endDate</c>/<c>autoRenewal</c> correction
    /// always moves the computed date) — this test instead proves the narrower, still-valuable
    /// invariant that calling <see cref="RenewalAlertService.RecomputeForContractAsync"/> twice with
    /// unchanged terms is idempotent at the <see cref="RenewalAlert"/> level: no second row, no
    /// resolve, no new <see cref="RenewalAlertService.AuditCreatedAction"/>/<see cref="RenewalAlertService.AuditResolvedAction"/>
    /// entry. <see cref="RenewalThresholdScheduler"/> itself is deliberately *not* deduped (that
    /// type's own doc comment: "no persisted 'already alerted' state is needed here"), so it still
    /// honestly re-writes its own <c>renewal.approaching</c> entry every time "today" matches a
    /// threshold — that is this method re-running the same real check, not a bug.
    /// </summary>
    [Fact]
    public async Task RecomputeForContractAsync_leaves_a_still_matching_active_alert_untouched_and_creates_nothing_new()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var auditWriter = new RecordingAuditWriter();

        await using (var seedDb = CreateContext(tenantContext))
        {
            using var _ = tenantContext.BeginScope(tenantId);
            seedDb.RenewalAlerts.Add(new RenewalAlert
            {
                TenantId = tenantId,
                ContractId = contractId,
                Milestone = RenewalMilestoneKind.RenewalDate,
                ThresholdDays = 90,
                MilestoneDate = AsOf.AddDays(90),
                Status = RenewalAlertStatus.Active,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow,
            });
            await seedDb.SaveChangesAsync();
        }

        // Corrected terms restate exactly the same EndDate -- RenewalEngine recomputes the exact
        // same RenewalDate the seeded alert already carries.
        var terms = new ContractRenewalTerms(contractId, AsOf.AddDays(90), AutoRenewal: true, CancellationNoticeDays: null);

        await using var db = CreateContext(tenantContext);
        var service = CreateService(db, tenantContext, clock, auditWriter);

        var outcome = await service.RecomputeForContractAsync(tenantId, terms);

        Assert.Equal(0, outcome.ResolvedCount);
        Assert.Equal(0, outcome.CreatedCount); // still-matching key -> de-duped, not a second row.

        var stored = Assert.Single(await db.RenewalAlerts.Where(a => a.TenantId == tenantId).ToListAsync());
        Assert.Equal(RenewalAlertStatus.Active, stored.Status);
        Assert.Equal(clock.UtcNow, stored.UpdatedAt); // untouched.

        // The scheduler re-affirms the still-true threshold match (its own doc comment: it is
        // deliberately not deduped) -- but the alert itself did not change, so no
        // AuditCreatedAction/AuditResolvedAction entry accompanies it.
        var entry = Assert.Single(auditWriter.Written);
        Assert.Equal(RenewalApproachingEvent.EventName, entry.Action);
    }

    [Fact]
    public async Task RecomputeForContractAsync_creates_a_fresh_alert_and_a_renewal_approaching_entry_when_a_new_threshold_is_crossed()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var auditWriter = new RecordingAuditWriter();

        // No pre-existing alert; the corrected terms land the contract exactly on the 60-day
        // window -- AC-3 "scheduler recomputes when a contract/term is corrected" means this fires
        // the same renewal.approaching event (and durable audit entry) a scheduled tick would.
        var terms = new ContractRenewalTerms(contractId, AsOf.AddDays(60), AutoRenewal: true, CancellationNoticeDays: null);

        await using var db = CreateContext(tenantContext);
        var service = CreateService(db, tenantContext, clock, auditWriter);

        var outcome = await service.RecomputeForContractAsync(tenantId, terms);

        Assert.Equal(0, outcome.ResolvedCount);
        Assert.Equal(1, outcome.CreatedCount);

        var stored = Assert.Single(await db.RenewalAlerts.Where(a => a.TenantId == tenantId).ToListAsync());
        Assert.Equal(RenewalAlertStatus.Active, stored.Status);
        Assert.Equal(60, stored.ThresholdDays);

        Assert.Equal(2, auditWriter.Written.Count);
        Assert.Contains(auditWriter.Written, e => e.Action == RenewalApproachingEvent.EventName);
        Assert.Contains(auditWriter.Written, e => e.Action == RenewalAlertService.AuditCreatedAction);
    }

    [Fact]
    public async Task RecomputeForContractAsync_rejects_a_null_terms_argument()
    {
        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = CreateService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RecomputeForContractAsync(TenantId.New(), null!));
    }
}
