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
/// Proves the Definition of Done for task E02/F05/US01/T02 (us-01-correction-history, AC-2
/// "correction history is queryable") and the parent story's own DoD ("... history is
/// retrievable"): <see cref="ContractCorrectionHistoryQueryService.GetHistoryAsync"/> reads back
/// the <see cref="CorrectionHistory"/> rows task E02/F05/US01/T01's
/// <see cref="ContractCorrectionService"/> already writes, scoped to the caller's tenant — against
/// a real Postgres+RLS database, mirroring <see cref="ContractCorrectionServiceTests"/>'s own
/// unprivileged-role rationale so a passing cross-tenant assertion is a real RLS proof, not a
/// tautology from a superuser connection that unconditionally bypasses row security.
/// </summary>
public sealed class ContractCorrectionHistoryQueryServiceTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_correction_history_app";
    private const string AppRolePassword = "raffa_correction_history_app_test_password";

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
            // Applies Initial + AddTenantRowLevelSecurity (covers contract/contract_version/
            // correction_history — see that migration's TenantScopedTables list).
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

    /// <summary>This test class only proves the read side
    /// (<see cref="ContractCorrectionHistoryQueryService"/>); the audit write task
    /// E02/F05/US01/T02 also added to <see cref="ContractCorrectionService"/> is proven by
    /// <see cref="ContractCorrectionServiceTests"/>'s own <c>RecordingAuditWriter</c> instead —
    /// this one just needs the seed corrections to succeed.</summary>
    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>Seeds a <see cref="Contract"/> row directly — same rationale as
    /// <see cref="ContractCorrectionServiceTests"/>'s own seed helper: this module has no "create
    /// contract" writer yet (the extraction pipeline's job), so a directly-seeded row stands in
    /// for "whatever the AI extraction originally wrote".</summary>
    private static async Task<EntityId> SeedContractAsync(
        DocumentsContractsDbContext db, TenantId tenantId, DateTimeOffset createdAt)
    {
        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "needs_review",
            Currency = "USD",
            AnnualSpend = 100000m,
            AutoRenewal = false,
            CreatedAt = createdAt,
        };
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
        return contract.Id;
    }

    /// <summary>Corrects a contract via the real write path (task E02/F05/US01/T01's
    /// <see cref="ContractCorrectionService"/>), so this test class proves a read back of
    /// genuinely persisted history rows, not hand-inserted fixture rows.</summary>
    private async Task CorrectAsync(
        ITenantContext tenantContext, TenantId tenantId, EntityId contractId, string field, string value,
        string reason, DateTimeOffset now)
    {
        await using var db = CreateAppContext(tenantContext);
        var service = new ContractCorrectionService(db, tenantContext, new FixedClock(now), new NoOpAuditWriter());
        var result = await service.CorrectAsync(
            tenantId, contractId, new Dictionary<string, string?> { [field] = value }, reason);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Returns_corrections_newest_first_for_the_owning_tenant()
    {
        var tenantId = TenantId.New();
        var now = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
        var tenantContext = new TenantContext();

        EntityId contractId;
        using (tenantContext.BeginScope(tenantId))
        {
            await using var seedDb = CreateAppContext(tenantContext);
            contractId = await SeedContractAsync(seedDb, tenantId, now.AddDays(-1));
        }

        await CorrectAsync(tenantContext, tenantId, contractId, "status", "active", "First fix", now);
        await CorrectAsync(tenantContext, tenantId, contractId, "status", "expired", "Second fix", now.AddHours(1));

        await using var db = CreateAppContext(tenantContext);
        var queryService = new ContractCorrectionHistoryQueryService(db, tenantContext);

        // AC-2: correction history is queryable, newest first.
        var history = await queryService.GetHistoryAsync(tenantId, contractId);

        Assert.NotNull(history);
        Assert.Equal(2, history!.Count);
        Assert.Equal("status", history[0].FieldName);
        Assert.Equal("active", history[0].PreviousValue);
        Assert.Equal("expired", history[0].NewValue);
        Assert.Equal("Second fix", history[0].Reason);
        Assert.Equal(now.AddHours(1), history[0].CorrectedAt);
        Assert.Equal("needs_review", history[1].PreviousValue);
        Assert.Equal("active", history[1].NewValue);
        Assert.Equal("First fix", history[1].Reason);
        Assert.Equal(now, history[1].CorrectedAt);
    }

    [Fact]
    public async Task Returns_an_empty_list_for_a_contract_that_has_never_been_corrected()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        EntityId contractId;
        using (tenantContext.BeginScope(tenantId))
        {
            await using var seedDb = CreateAppContext(tenantContext);
            contractId = await SeedContractAsync(seedDb, tenantId, DateTimeOffset.UtcNow);
        }

        await using var db = CreateAppContext(tenantContext);
        var queryService = new ContractCorrectionHistoryQueryService(db, tenantContext);

        // The contract genuinely exists (200), it just has no history yet (empty array, not 404).
        var history = await queryService.GetHistoryAsync(tenantId, contractId);

        Assert.NotNull(history);
        Assert.Empty(history!);
    }

    [Fact]
    public async Task Returns_null_for_a_contract_that_does_not_exist()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var queryService = new ContractCorrectionHistoryQueryService(db, tenantContext);

        var history = await queryService.GetHistoryAsync(tenantId, EntityId.New());

        Assert.Null(history);
    }

    [Fact]
    public async Task A_different_tenant_cannot_read_another_tenants_correction_history()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var tenantContext = new TenantContext();
        var now = DateTimeOffset.UtcNow;

        EntityId contractId;
        using (tenantContext.BeginScope(tenantA))
        {
            await using var seedDb = CreateAppContext(tenantContext);
            contractId = await SeedContractAsync(seedDb, tenantA, now);
        }

        await CorrectAsync(tenantContext, tenantA, contractId, "status", "active", "Tenant A's own fix", now);

        await using var db = CreateAppContext(tenantContext);
        var queryService = new ContractCorrectionHistoryQueryService(db, tenantContext);

        // ADR-009: tenant A's contract and its correction history genuinely exist (seeded/
        // corrected above) but must be invisible to tenant B — both the app-level tenant
        // predicate and Postgres RLS independently deny it, so a null (not empty) result is a
        // real cross-tenant proof, not a tautology from a superuser connection.
        var history = await queryService.GetHistoryAsync(tenantB, contractId);

        Assert.Null(history);
    }
}
