using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Proves the Definition of Done for task E19/F03/US01/T01 (us-01-step-ticks-api, AC-1/AC-2/AC-3/
/// AC-6): <see cref="NegotiationStepService"/>'s whole-set semantics -- idempotent <see cref="
/// NegotiationStepService.SetAsync"/>, an absent key deleting its row, an unknown name rejected
/// with nothing written -- against a real Postgres+RLS database, mirroring <see cref="
/// ContractCorrectionServiceTests"/>'s own unprivileged-role rationale so a passing assertion is a
/// real proof, not a tautology from a superuser connection that unconditionally bypasses row
/// security.
/// </summary>
public sealed class ContractNegotiationStepTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_negotiation_step_app";
    private const string AppRolePassword = "raffa_negotiation_step_app_test_password";
    private const string Actor = "oid-test-actor";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new DocumentsContractsDbContext(adminOptions.Options))
        {
            // Applies every migration up to and including AddContractNegotiationStep -- table and
            // RLS policy both, in that one migration (ADR-009 w16 clause 2).
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

    private DocumentsContractsDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    /// <summary>Fake <see cref="IAuditWriter"/> that records every entry written -- same shape as
    /// <see cref="ContractCorrectionServiceTests"/>'s own <c>RecordingAuditWriter</c>.</summary>
    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Written { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Written.Add(entry);
            return Task.CompletedTask;
        }
    }

    /// <summary>Seeds a <see cref="Contract"/> row directly -- this module has no "create contract"
    /// writer (extraction is the only path); a directly-seeded row stands in for "whatever the AI
    /// extraction originally wrote", same rationale <see cref="ContractCorrectionServiceTests
    /// .SeedContractAsync"/> already documents.</summary>
    private static async Task<EntityId> SeedContractAsync(DocumentsContractsDbContext db, TenantId tenantId)
    {
        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "needs_review",
            Currency = "USD",
            AnnualSpend = 100_000m,
            AutoRenewal = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
        return contract.Id;
    }

    [Fact]
    public async Task GetAsync_returns_empty_for_an_untouched_contract_and_null_for_an_unknown_one()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var auditWriter = new RecordingAuditWriter();

        EntityId contractId;
        using (tenantContext.BeginScope(tenantId))
        {
            await using var seedDb = CreateAppContext(tenantContext);
            contractId = await SeedContractAsync(seedDb, tenantId);
        }

        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationStepService(db, tenantContext, clock, auditWriter);

        // AC-1: an untouched contract that exists returns an empty set, not 404-shaped null.
        var untouched = await service.GetAsync(tenantId, contractId);
        Assert.NotNull(untouched);
        Assert.Empty(untouched);

        // No such contract for this tenant -> null (the endpoint's own 404 signal).
        var unknown = await service.GetAsync(tenantId, EntityId.New());
        Assert.Null(unknown);
    }

    [Fact]
    public async Task SetAsync_then_GetAsync_round_trip_the_ticked_steps_and_write_one_audit_entry()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero));
        var auditWriter = new RecordingAuditWriter();

        EntityId contractId;
        using (tenantContext.BeginScope(tenantId))
        {
            await using var seedDb = CreateAppContext(tenantContext);
            contractId = await SeedContractAsync(seedDb, tenantId);
        }

        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationStepService(db, tenantContext, clock, auditWriter);

        var setResult = await service.SetAsync(tenantId, contractId, ["Notify", "RequestRevisedPricing"], Actor);

        Assert.True(setResult.IsSuccess);
        Assert.Equal(["Notify", "RequestRevisedPricing"], setResult.Value);

        var getResult = await service.GetAsync(tenantId, contractId);
        Assert.Equal(["Notify", "RequestRevisedPricing"], getResult);

        var entry = Assert.Single(auditWriter.Written);
        Assert.Equal(Actor, entry.Actor);
        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal(contractId.Value.ToString(), entry.ResourceId);
    }

    [Fact]
    public async Task SetAsync_is_idempotent_and_preserves_the_original_ticked_at_for_a_step_left_ticked()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var firstClock = new FixedClock(new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero));
        var secondClock = new FixedClock(new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero));

        EntityId contractId;
        using (tenantContext.BeginScope(tenantId))
        {
            await using var seedDb = CreateAppContext(tenantContext);
            contractId = await SeedContractAsync(seedDb, tenantId);
        }

        await using (var firstDb = CreateAppContext(tenantContext))
        {
            var firstService = new NegotiationStepService(firstDb, tenantContext, firstClock, auditWriter);
            var firstSet = await firstService.SetAsync(tenantId, contractId, ["Notify"], Actor);
            Assert.True(firstSet.IsSuccess);
        }

        // Re-PUTting the identical set at a later instant must be a true no-op on the still-ticked
        // row: its TickedAt must not move (NegotiationStepService.SetAsync's own doc comment).
        await using (var secondDb = CreateAppContext(tenantContext))
        {
            var secondService = new NegotiationStepService(secondDb, tenantContext, secondClock, auditWriter);
            var secondSet = await secondService.SetAsync(tenantId, contractId, ["Notify"], Actor);
            Assert.True(secondSet.IsSuccess);
            Assert.Equal(["Notify"], secondSet.Value);
        }

        using (tenantContext.BeginScope(tenantId))
        {
            await using var verifyDb = CreateAppContext(tenantContext);
            var row = await verifyDb.ContractNegotiationSteps
                .AsNoTracking()
                .SingleAsync(s => s.TenantId == tenantId && s.ContractId == contractId);
            Assert.Equal(NegotiationStep.Notify, row.Step);
            Assert.Equal(firstClock.UtcNow, row.TickedAt);
        }
    }

    [Fact]
    public async Task SetAsync_deletes_the_row_for_a_step_absent_from_the_new_set()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var auditWriter = new RecordingAuditWriter();

        EntityId contractId;
        using (tenantContext.BeginScope(tenantId))
        {
            await using var seedDb = CreateAppContext(tenantContext);
            contractId = await SeedContractAsync(seedDb, tenantId);
        }

        await using (var setupDb = CreateAppContext(tenantContext))
        {
            var setupService = new NegotiationStepService(setupDb, tenantContext, clock, auditWriter);
            var initialSet = await setupService.SetAsync(
                tenantId, contractId, ["Notify", "RequestRevisedPricing"], Actor);
            Assert.True(initialSet.IsSuccess);
        }

        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationStepService(db, tenantContext, clock, auditWriter);

        // Dropping "RequestRevisedPricing" from the whole set unticks it -- its row is deleted, not
        // flagged (there is no `ticked` boolean to flip).
        var narrowedSet = await service.SetAsync(tenantId, contractId, ["Notify"], Actor);
        Assert.True(narrowedSet.IsSuccess);
        Assert.Equal(["Notify"], narrowedSet.Value);

        var afterNarrow = await service.GetAsync(tenantId, contractId);
        Assert.Equal(["Notify"], afterNarrow);

        // The empty set unticks everything -- an idempotent PUT("[]") leaves nothing ticked.
        var emptySet = await service.SetAsync(tenantId, contractId, [], Actor);
        Assert.True(emptySet.IsSuccess);
        Assert.Empty(emptySet.Value);

        var afterEmpty = await service.GetAsync(tenantId, contractId);
        Assert.Empty(afterEmpty!);
    }

    [Fact]
    public async Task SetAsync_rejects_an_unknown_step_name_and_writes_nothing()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var auditWriter = new RecordingAuditWriter();

        EntityId contractId;
        using (tenantContext.BeginScope(tenantId))
        {
            await using var seedDb = CreateAppContext(tenantContext);
            contractId = await SeedContractAsync(seedDb, tenantId);
        }

        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationStepService(db, tenantContext, clock, auditWriter);

        var result = await service.SetAsync(tenantId, contractId, ["Notify", "Bogus"], Actor);

        Assert.True(result.IsFailure);
        Assert.Contains("Notify", result.Error, StringComparison.Ordinal);

        // AC-3: nothing written -- not even the valid "Notify" half of the rejected request -- and
        // no audit entry for a call that never mutated anything.
        var stepsAfter = await service.GetAsync(tenantId, contractId);
        Assert.Empty(stepsAfter!);
        Assert.Empty(auditWriter.Written);
    }

    [Fact]
    public async Task SetAsync_fails_for_an_unknown_contract_and_writes_nothing()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var auditWriter = new RecordingAuditWriter();

        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationStepService(db, tenantContext, clock, auditWriter);

        var result = await service.SetAsync(tenantId, EntityId.New(), ["Notify"], Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(NegotiationStepService.ContractNotFoundError, result.Error);
        Assert.Empty(auditWriter.Written);
    }

    [Fact]
    public async Task A_second_tenants_ticks_never_appear_for_the_same_contract_id()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var auditWriter = new RecordingAuditWriter();

        EntityId contractId;
        using (tenantContext.BeginScope(tenantA))
        {
            await using var seedDb = CreateAppContext(tenantContext);
            contractId = await SeedContractAsync(seedDb, tenantA);
        }

        await using (var writeDb = CreateAppContext(tenantContext))
        {
            var writeService = new NegotiationStepService(writeDb, tenantContext, clock, auditWriter);
            var setForA = await writeService.SetAsync(tenantA, contractId, ["Notify"], Actor);
            Assert.True(setForA.IsSuccess);
        }

        // Tenant B naming tenant A's contract id: the explicit tenant_id predicate plus Postgres
        // RLS both deny it -- null (404-shaped), never tenant A's ticks, never a 500 (ADR-009 w16
        // clause 2c).
        await using var readDb = CreateAppContext(tenantContext);
        var readService = new NegotiationStepService(readDb, tenantContext, clock, auditWriter);
        var asB = await readService.GetAsync(tenantB, contractId);
        Assert.Null(asB);

        var setAsB = await readService.SetAsync(tenantB, contractId, ["Notify"], Actor);
        Assert.True(setAsB.IsFailure);
        Assert.Equal(NegotiationStepService.ContractNotFoundError, setAsB.Error);

        // Sanity: tenant A's own tick is unaffected by tenant B's attempt.
        var asA = await readService.GetAsync(tenantA, contractId);
        Assert.Equal(["Notify"], asA);
    }
}
