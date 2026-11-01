using Microsoft.EntityFrameworkCore;
using Npgsql;
using Raffa.Savings.Application.Results;
using Raffa.Savings.Domain.Results;
using Raffa.Savings.Infrastructure;
using Raffa.Savings.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Savings;
using Raffa.SharedKernel.Tenancy;
using Testcontainers.PostgreSql;

namespace Raffa.Savings.Tests.Results;

/// <summary>
/// The verifiable-savings tables against Postgres as the app role (RLS enforced): acceptance test 9
/// (a signed contract yields a <c>saving_result</c> with realized 0 until invoices exist) and 13
/// (every new table rejects cross-tenant reads/writes).
/// </summary>
public sealed class VerifiableSavingsDatabaseTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_verifiable_savings_app";
    private const string AppRolePassword = "raffa_verifiable_savings_app_test_password";
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var adminOptions = new DbContextOptionsBuilder<SavingsDbContext>();
        SavingsDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());
        await using (var adminDb = new SavingsDbContext(adminOptions.Options))
        {
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

    private SavingsDbContext Db(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<SavingsDbContext>();
        SavingsDbContextOptions.Configure(options, _appConnectionString, tenantContext);
        return new SavingsDbContext(options.Options);
    }

    private static SavingResultRecord Signed(string supplier, string kind, decimal amount) => new(
        Guid.NewGuid(), Guid.NewGuid(), supplier, supplier + " MSA", "CRM", kind, amount, "CHF",
        "CHF 156 → 148 per user / month, same 340 users", "previous contract price, same quantities",
        156m, 148m, 4080m, "(old − new unit price) × contracted quantity",
        new DateOnly(2026, 10, 18), new DateOnly(2028, 10, 17), "new term from 18 Oct 2026",
        null, supplier + "_signed.pdf", "Luca Lamalfa", new DateOnly(2026, 9, 8), "luca");

    [Fact]
    public async Task A_signed_contract_yields_a_price_result_realized_only_by_invoices()
    {
        var tenant = TenantId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(Now);
        await using var db = Db(tenantContext);
        var recorder = new SavingResultRecorder(db, tenantContext, clock);

        var id = await recorder.RecordAsync(tenant, Signed("Salesforce", "price", SavingResultRecord.PriceSaving(156m, 148m, 4080m)));
        await recorder.RecordAsync(tenant, Signed("Workday", "volume", 28000m));

        var query = new ResultsQueryService(db, tenantContext, new NoContracts(), new NoCases(), clock);
        var report = await query.GetReportAsync(tenant, "fy");
        Assert.Equal(32640m, report.Headline.Contractual);
        Assert.Equal(0m, report.Headline.Realized);
        Assert.Equal(28000m, report.Headline.Volume);
        Assert.Equal(1, report.Headline.Count);
        var row = Assert.Single(report.Contractual);
        Assert.Equal("price", row.Kind);
        Assert.False(row.Realized);

        using (tenantContext.BeginScope(tenant))
        {
            var invoice = new Invoice { TenantId = tenant, SupplierName = "Salesforce", Period = new DateOnly(2026, 11, 1), Amount = 50320m, Currency = "CHF", CreatedAt = Now };
            db.Invoices.Add(invoice);
            db.SavingRealizations.Add(new SavingRealization { TenantId = tenant, SavingResultId = new EntityId(id), InvoiceId = invoice.Id, RealizedAmount = 2720m, ComputedAt = Now });
            await db.SaveChangesAsync();
        }

        var after = await query.GetReportAsync(tenant, "fy");
        Assert.Equal(2720m, after.Headline.Realized);
        var proven = Assert.Single(after.Contractual);
        Assert.True(proven.Realized);
        Assert.Equal(1, proven.Proof!.InvoiceCount);
        Assert.Equal(24, proven.Proof.PeriodMonths);
    }

    [Fact]
    public async Task Every_new_table_hides_another_tenants_rows()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var tenantContext = new TenantContext();
        await using (var db = Db(tenantContext))
        {
            foreach (var tenant in new[] { tenantA, tenantB })
            {
                await new SavingResultRecorder(db, tenantContext, new FixedClock(Now)).RecordAsync(tenant, Signed("Salesforce", "price", 1000m));
                await new CaseObjectiveTableStore(db, tenantContext, new FixedClock(Now))
                    .UpsertAsync(tenant, new CaseObjectiveInfo(Guid.NewGuid(), Guid.NewGuid(), null, "p5", null, null, null, null));
                using var _ = tenantContext.BeginScope(tenant);
                var invoice = new Invoice { TenantId = tenant, SupplierName = "S", Period = new DateOnly(2026, 1, 1), Amount = 1m, Currency = "CHF", CreatedAt = Now };
                db.Invoices.Add(invoice);
                db.SavingsGoals.Add(new SavingsGoal { TenantId = tenant, UserId = "u", SourceText = "10%", ScopeJson = "{}", Period = "FY 2026", Active = true, CreatedAt = Now });
                await db.SaveChangesAsync();
                var result = await db.SavingResults.FirstAsync();
                db.SavingRealizations.Add(new SavingRealization { TenantId = tenant, SavingResultId = result.Id, InvoiceId = invoice.Id, RealizedAmount = 1m, ComputedAt = Now });
                await db.SaveChangesAsync();
            }
        }

        using (tenantContext.BeginScope(tenantA))
        {
            await using var db = Db(tenantContext);
            Assert.Equal(tenantA, Assert.Single(await db.SavingResults.ToListAsync()).TenantId);
            Assert.Equal(tenantA, Assert.Single(await db.Invoices.ToListAsync()).TenantId);
            Assert.Equal(tenantA, Assert.Single(await db.SavingRealizations.ToListAsync()).TenantId);
            Assert.Equal(tenantA, Assert.Single(await db.SavingsGoals.ToListAsync()).TenantId);
            Assert.Equal(tenantA, Assert.Single(await db.CaseObjectives.ToListAsync()).TenantId);

            db.SavingsGoals.Add(new SavingsGoal { TenantId = tenantB, UserId = "x", SourceText = "x", ScopeJson = "{}", Period = "FY 2026", CreatedAt = Now });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    private sealed class NoContracts : IContractFactsReader
    {
        public Task<IReadOnlyList<ContractFacts>> ListValidatedAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ContractFacts>>([]);

        public Task<ContractFacts?> GetAsync(TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ContractFacts?>(null);
    }

    private sealed class NoCases : ICaseObjectiveSource
    {
        public Task<IReadOnlyList<CaseObjectiveInfo>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CaseObjectiveInfo>>([]);
    }
}
