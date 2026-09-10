using Raffa.Savings.Application;
using Raffa.Savings.Domain;
using Raffa.Savings.Infrastructure;
using Raffa.Savings.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Savings.Tests;

/// <summary>
/// Proves the Definition of Done for task E04/F02/US02/T01 (savings-opportunity; parent story
/// us-02-savings-opportunity AC-1/AC-2, and the story's own DoD "opportunity lifecycle (identify
/// -&gt; approve -&gt; realized)" up to the point this task owns — see
/// <see cref="SavingsOpportunityStatus.Realized"/>'s own doc comment for the audit-tracked
/// realized-value gap task E04/F02/US02/T02 closes): <see cref="SavingsOpportunityService"/>
/// genuinely persists an identified opportunity as a readable row through the exact same
/// Npgsql/EF Core/RLS-interceptor pipeline every other module's write path uses (ADR-003/ADR-009),
/// validates its fields before writing anything, supports a partial owner/status update by id, and
/// writes one <see cref="IAuditWriter"/> entry per successful mutation (spec §14.1). Mirrors
/// <c>Raffa.Renewals.Tests.RenewalActionServiceTests</c>'s own Testcontainers shape;
/// tenant-isolation-under-RLS is proved separately by
/// <c>SavingsOpportunityRlsCrossTenantIsolationTests</c>.
///
/// Spins up its own disposable Postgres container per test run (Testcontainers), so this test
/// needs nothing but a running Docker daemon; no shared/external database to stand up by hand.
/// </summary>
public sealed class SavingsOpportunityServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private SavingsDbContext CreateContext(ITenantContext? tenantContext = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SavingsDbContext>();
        SavingsDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString(), tenantContext);
        return new SavingsDbContext(optionsBuilder.Options);
    }

    private async Task MigrateAsync()
    {
        await using var migrateDb = CreateContext();
        await migrateDb.Database.MigrateAsync();
    }

    private static CreateSavingsOpportunityRequest ValidRequest(
        EntityId? supplierId = null, EntityId? contractId = null) =>
        new(
            SupplierId: supplierId,
            ContractId: contractId,
            Type: "price-renegotiation",
            CurrentSpend: 120_000m,
            Currency: "USD",
            EstimatedSavingsLow: 8_000m,
            EstimatedSavingsHigh: 15_000m,
            Confidence: 0.72);

    [Fact]
    public async Task CreateAsync_persists_an_identified_opportunity_readable_through_ListAsync()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var supplierId = EntityId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
        var auditWriter = new RecordingAuditWriter();

        EntityId opportunityId;
        await using (var db = CreateContext(tenantContext))
        {
            var service = new SavingsOpportunityService(db, tenantContext, clock, auditWriter);

            var result = await service.CreateAsync(tenantId, ValidRequest(supplierId, contractId));

            Assert.True(result.IsSuccess);
            Assert.Equal(supplierId, result.Value.SupplierId);
            Assert.Equal(contractId, result.Value.ContractId);
            Assert.Equal("price-renegotiation", result.Value.Type);
            Assert.Equal(120_000m, result.Value.CurrentSpend);
            Assert.Equal("USD", result.Value.Currency);
            Assert.Equal(8_000m, result.Value.EstimatedSavingsLow);
            Assert.Equal(15_000m, result.Value.EstimatedSavingsHigh);
            Assert.Equal(0.72, result.Value.Confidence);
            // Task E04/F03/US01/T02 (savings-list): the confidence tier flows through the real
            // service, not just the record's own unit tests -- 0.72 is above
            // SavingsProvenanceClassifier.HighConfidenceThreshold (0.7).
            Assert.Equal(SavingsConfidenceLevel.High, result.Value.ConfidenceLevel);
            Assert.Equal(SavingsOpportunityStatus.Identified, result.Value.Status);
            Assert.Null(result.Value.Owner);
            Assert.Equal(clock.UtcNow, result.Value.CreatedAt);
            Assert.Equal(clock.UtcNow, result.Value.UpdatedAt);
            opportunityId = result.Value.Id;
        }

        // Fresh context/connection, same tenant scope: this reads back from Postgres, not the
        // change tracker, and only succeeds if RLS actually lets this tenant see its own row.
        await using var readDb = CreateContext(tenantContext);
        var readService = new SavingsOpportunityService(readDb, tenantContext, clock, auditWriter);
        var listed = await readService.ListAsync(tenantId);

        var row = Assert.Single(listed);
        Assert.Equal(opportunityId, row.Id);

        // Spec §14.1 / Appendix C rule 9: every successful write is audited.
        var entry = Assert.Single(auditWriter.Written);
        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal("savings_opportunity.identified", entry.Action);
        Assert.Equal("savings_opportunity", entry.ResourceType);
        Assert.Equal(opportunityId.Value.ToString(), entry.ResourceId);
    }

    [Fact]
    public async Task ListAsync_returns_only_this_tenants_opportunities_newest_first()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var otherTenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();

        await using (var db = CreateContext(tenantContext))
        {
            var earlyClock = new FixedClock(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
            var lateClock = new FixedClock(new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero));

            var earlyService = new SavingsOpportunityService(db, tenantContext, earlyClock, auditWriter);
            var lateService = new SavingsOpportunityService(db, tenantContext, lateClock, auditWriter);

            var first = await earlyService.CreateAsync(tenantId, ValidRequest());
            var second = await lateService.CreateAsync(tenantId, ValidRequest());
            await lateService.CreateAsync(otherTenantId, ValidRequest());

            await using var readDb = CreateContext(tenantContext);
            var readService = new SavingsOpportunityService(readDb, tenantContext, lateClock, auditWriter);
            var listed = await readService.ListAsync(tenantId);

            Assert.Equal(2, listed.Count);
            // Newest identified first.
            Assert.Equal(second.Value.Id, listed[0].Id);
            Assert.Equal(first.Value.Id, listed[1].Id);
        }
    }

    [Fact]
    public async Task UpdateAsync_changes_only_the_supplied_fields()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var createClock = new FixedClock(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        var updateClock = new FixedClock(new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero));

        EntityId opportunityId;
        await using (var db = CreateContext(tenantContext))
        {
            var createService = new SavingsOpportunityService(db, tenantContext, createClock, auditWriter);
            var created = await createService.CreateAsync(tenantId, ValidRequest());
            opportunityId = created.Value.Id;
        }

        await using (var db = CreateContext(tenantContext))
        {
            var updateService = new SavingsOpportunityService(db, tenantContext, updateClock, auditWriter);

            // Owner only — status must remain Identified.
            var ownerUpdate = await updateService.UpdateAsync(
                tenantId, opportunityId, owner: "alice@acme.example", status: null, realizedAmount: null);

            Assert.True(ownerUpdate.IsSuccess);
            Assert.Equal("alice@acme.example", ownerUpdate.Value.Owner);
            Assert.Equal(SavingsOpportunityStatus.Identified, ownerUpdate.Value.Status);
            Assert.Equal(updateClock.UtcNow, ownerUpdate.Value.UpdatedAt);

            // Status only — owner set above must survive untouched.
            var statusUpdate = await updateService.UpdateAsync(
                tenantId, opportunityId, owner: null, status: "InProgress", realizedAmount: null);

            Assert.True(statusUpdate.IsSuccess);
            Assert.Equal("alice@acme.example", statusUpdate.Value.Owner);
            Assert.Equal(SavingsOpportunityStatus.InProgress, statusUpdate.Value.Status);
        }

        Assert.Equal(3, auditWriter.Written.Count); // 1 identify + 2 updates.
        Assert.All(
            auditWriter.Written.Skip(1),
            entry => Assert.Equal("savings_opportunity.updated", entry.Action));
    }

    [Fact]
    public async Task UpdateAsync_can_move_a_status_all_the_way_to_Realized()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(db, tenantContext, clock, auditWriter);
        var created = await service.CreateAsync(tenantId, ValidRequest());

        await service.UpdateAsync(
            tenantId, created.Value.Id, owner: "bob@acme.example", status: "InProgress", realizedAmount: null);
        var realized = await service.UpdateAsync(
            tenantId, created.Value.Id, owner: null, status: "Realized", realizedAmount: null);

        Assert.True(realized.IsSuccess);
        Assert.Equal(SavingsOpportunityStatus.Realized, realized.Value.Status);
        // No realizedAmount supplied on this call -- setting status alone still does not record a
        // RealizedSavings row (see SavingsOpportunityStatus.Realized's own doc comment).
        Assert.Null(realized.Value.RealizedAmount);
        Assert.Equal(0, await db.RealizedSavingsRecords.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_returns_NotFoundError_for_an_unknown_id()
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.UpdateAsync(
            TenantId.New(), EntityId.New(), owner: "alice", status: null, realizedAmount: null);

        Assert.True(result.IsFailure);
        Assert.Equal(SavingsOpportunityService.NotFoundError, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_request_with_no_fields_without_writing_anything()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);

        var created = await service.CreateAsync(tenantId, ValidRequest());
        var writesBeforeUpdate = auditWriter.Written.Count;

        var result = await service.UpdateAsync(
            tenantId, created.Value.Id, owner: null, status: null, realizedAmount: null);

        Assert.True(result.IsFailure);
        Assert.Equal(SavingsOpportunityService.NoFieldsToUpdateError, result.Error);
        Assert.Equal(writesBeforeUpdate, auditWriter.Written.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateAsync_rejects_a_blank_owner(string blankOwner)
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var created = await service.CreateAsync(tenantId, ValidRequest());

        var result = await service.UpdateAsync(
            tenantId, created.Value.Id, owner: blankOwner, status: null, realizedAmount: null);

        Assert.True(result.IsFailure);
        Assert.Equal(SavingsOpportunityService.OwnerCannotBeBlankError, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_unrecognized_status()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var created = await service.CreateAsync(tenantId, ValidRequest());

        var result = await service.UpdateAsync(
            tenantId, created.Value.Id, owner: null, status: "Cancelled", realizedAmount: null);

        Assert.True(result.IsFailure);
        Assert.Equal(SavingsOpportunityService.StatusInvalidError, result.Error);
    }

    // ----- UpdateAsync realized-value capture (task E04/F02/US02/T02, realized-savings) -----

    [Fact]
    public async Task UpdateAsync_records_a_realized_amount_and_writes_a_distinct_audit_event()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero));

        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(db, tenantContext, clock, auditWriter);
        var created = await service.CreateAsync(tenantId, ValidRequest());
        var writesBeforeUpdate = auditWriter.Written.Count;

        var result = await service.UpdateAsync(
            tenantId, created.Value.Id, owner: null, status: "Realized", realizedAmount: 9_500m);

        Assert.True(result.IsSuccess);
        Assert.Equal(SavingsOpportunityStatus.Realized, result.Value.Status);
        Assert.Equal(9_500m, result.Value.RealizedAmount);

        // Exactly one new RealizedSavings row, in the opportunity's own currency.
        var recorded = Assert.Single(await db.RealizedSavingsRecords.ToListAsync());
        Assert.Equal(tenantId, recorded.TenantId);
        Assert.Equal(created.Value.Id, recorded.SavingsOpportunityId);
        Assert.Equal(9_500m, recorded.Amount);
        Assert.Equal("USD", recorded.Currency);
        Assert.Equal(clock.UtcNow, recorded.RealizedAt);

        // Exactly one audit entry for this call (never two), and it is the realized-specific action.
        Assert.Equal(writesBeforeUpdate + 1, auditWriter.Written.Count);
        var entry = auditWriter.Written[^1];
        Assert.Equal("savings_opportunity.realized", entry.Action);
        Assert.Equal("savings_opportunity", entry.ResourceType);
        Assert.Equal(created.Value.Id.Value.ToString(), entry.ResourceId);
        Assert.Contains("realizedAmount=9500", entry.Detail);
    }

    [Fact]
    public async Task UpdateAsync_defaults_status_to_Realized_when_only_a_realized_amount_is_supplied()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);
        var created = await service.CreateAsync(tenantId, ValidRequest());

        // No 'status' at all in this call -- only a realized amount.
        var result = await service.UpdateAsync(
            tenantId, created.Value.Id, owner: null, status: null, realizedAmount: 1_200m);

        Assert.True(result.IsSuccess);
        Assert.Equal(SavingsOpportunityStatus.Realized, result.Value.Status);
        Assert.Equal(1_200m, result.Value.RealizedAmount);
    }

    [Fact]
    public async Task UpdateAsync_can_record_a_zero_realized_amount()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());
        var created = await service.CreateAsync(tenantId, ValidRequest());

        var result = await service.UpdateAsync(
            tenantId, created.Value.Id, owner: null, status: null, realizedAmount: 0m);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value.RealizedAmount);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_negative_realized_amount_without_writing_anything()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);
        var created = await service.CreateAsync(tenantId, ValidRequest());
        var writesBeforeUpdate = auditWriter.Written.Count;

        var result = await service.UpdateAsync(
            tenantId, created.Value.Id, owner: null, status: null, realizedAmount: -0.01m);

        Assert.True(result.IsFailure);
        Assert.Equal(SavingsOpportunityService.RealizedAmountMustBeNonNegativeError, result.Error);
        Assert.Equal(writesBeforeUpdate, auditWriter.Written.Count);
        Assert.Equal(0, await db.RealizedSavingsRecords.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_realized_amount_combined_with_a_conflicting_explicit_status()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);
        var created = await service.CreateAsync(tenantId, ValidRequest());
        var writesBeforeUpdate = auditWriter.Written.Count;

        var result = await service.UpdateAsync(
            tenantId, created.Value.Id, owner: null, status: "InProgress", realizedAmount: 500m);

        Assert.True(result.IsFailure);
        Assert.Equal(SavingsOpportunityService.RealizedAmountConflictsWithStatusError, result.Error);
        Assert.Equal(writesBeforeUpdate, auditWriter.Written.Count);
        Assert.Equal(0, await db.RealizedSavingsRecords.CountAsync());

        // Untouched: still Identified, the status CreateAsync gave it.
        var stillIdentified = await db.SavingsOpportunities.SingleAsync(o => o.Id == created.Value.Id);
        Assert.Equal(SavingsOpportunityStatus.Identified, stillIdentified.Status);
    }

    [Fact]
    public async Task UpdateAsync_allows_a_realized_amount_with_an_explicit_Realized_status()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());
        var created = await service.CreateAsync(tenantId, ValidRequest());

        // Explicit status "Realized" together with a realizedAmount is not a conflict -- it is the
        // one status value compatible with recording a realized value.
        var result = await service.UpdateAsync(
            tenantId, created.Value.Id, owner: null, status: "Realized", realizedAmount: 42m);

        Assert.True(result.IsSuccess);
        Assert.Equal(SavingsOpportunityStatus.Realized, result.Value.Status);
        Assert.Equal(42m, result.Value.RealizedAmount);
    }

    [Fact]
    public async Task UpdateAsync_appends_a_new_realized_savings_row_per_call_never_overwriting_the_previous_one()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());
        var created = await service.CreateAsync(tenantId, ValidRequest());

        await service.UpdateAsync(
            tenantId, created.Value.Id, owner: null, status: "Realized", realizedAmount: 100m);
        await service.UpdateAsync(
            tenantId, created.Value.Id, owner: null, status: "Realized", realizedAmount: 150m);

        // Append-only (see RealizedSavings's own doc comment): a second capture is a second row,
        // never a silent overwrite of the first.
        var rows = await db.RealizedSavingsRecords
            .Where(r => r.SavingsOpportunityId == created.Value.Id)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Amount == 100m);
        Assert.Contains(rows, r => r.Amount == 150m);
    }

    // ----- CreateAsync validation -----

    [Fact]
    public async Task CreateAsync_rejects_a_blank_type_without_writing_anything()
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);

        var request = ValidRequest() with { Type = "  " };
        var result = await service.CreateAsync(TenantId.New(), request);

        Assert.True(result.IsFailure);
        Assert.Equal(SavingsOpportunityService.TypeRequiredError, result.Error);
        Assert.Empty(auditWriter.Written);
        Assert.Equal(0, await db.SavingsOpportunities.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task CreateAsync_rejects_a_non_positive_current_spend(decimal currentSpend)
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var request = ValidRequest() with { CurrentSpend = currentSpend };
        var result = await service.CreateAsync(TenantId.New(), request);

        Assert.True(result.IsFailure);
        Assert.Equal(SavingsOpportunityService.CurrentSpendMustBePositiveError, result.Error);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_estimated_savings_range_where_high_is_below_low()
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var request = ValidRequest() with { EstimatedSavingsLow = 10_000m, EstimatedSavingsHigh = 5_000m };
        var result = await service.CreateAsync(TenantId.New(), request);

        Assert.True(result.IsFailure);
        Assert.Equal(SavingsOpportunityService.EstimatedSavingsRangeInvalidError, result.Error);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public async Task CreateAsync_rejects_a_confidence_outside_zero_to_one(double confidence)
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var request = ValidRequest() with { Confidence = confidence };
        var result = await service.CreateAsync(TenantId.New(), request);

        Assert.True(result.IsFailure);
        Assert.Equal(SavingsOpportunityService.ConfidenceOutOfRangeError, result.Error);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_null_request_argument()
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CreateAsync(TenantId.New(), null!));
    }
}
