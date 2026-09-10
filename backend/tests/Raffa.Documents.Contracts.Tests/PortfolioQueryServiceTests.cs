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
/// Proves the Definition of Done for task E02/F03/US01/T01 (us-01-portfolio-list-filters,
/// AC-1/AC-2/AC-3): <see cref="PortfolioQueryService.GetPortfolioAsync"/> returns the spec §8.1
/// portfolio columns (including the derived "Renewal" column), applies the AC-2 filters, and
/// enforces tenant scoping — against a real Postgres+RLS database, mirroring
/// <see cref="DocumentQueryServiceTests"/>'s own unprivileged-role rationale so a passing
/// "a different tenant gets nothing back" assertion is a real RLS proof, not a tautology from a
/// superuser connection that unconditionally bypasses row security. Task E02/F03/US01/T02 adds
/// pagination coverage: <see cref="Pagination_slices_results_and_reports_total_count"/> and
/// <see cref="Pagination_total_count_reflects_risk_filter_not_unfiltered_set"/>.
///
/// There is no "ContractUploadService" yet (contract rows are populated by the extraction
/// pipeline, a different, not-yet-built module) — unlike
/// <see cref="DocumentQueryServiceTests"/>, which seeds through the real upload path, this class
/// seeds <see cref="Contract"/>/<see cref="Risk"/> rows directly via the app-role
/// <see cref="DocumentsContractsDbContext"/>, inside the same tenant scope
/// <see cref="DocumentUploadService"/> would use, so the RLS `WITH CHECK` on insert is satisfied.
/// </summary>
public sealed class PortfolioQueryServiceTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_portfolio_app";
    private const string AppRolePassword = "raffa_portfolio_app_test_password";

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
            // Applies Initial + AddTenantRowLevelSecurity (covers contract/risk among others —
            // see that migration's TenantScopedTables list).
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

    private static Contract NewContract(
        TenantId tenantId,
        EntityId? supplierId = null,
        ContractDocumentType type = ContractDocumentType.Msa,
        string status = "Active",
        decimal? annualSpend = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        DateOnly? cancellationDeadline = null,
        bool autoRenewal = false) => new()
    {
        TenantId = tenantId,
        SupplierId = supplierId,
        Type = type,
        Status = status,
        Currency = "USD",
        AnnualSpend = annualSpend,
        StartDate = startDate,
        EndDate = endDate,
        CancellationDeadline = cancellationDeadline,
        AutoRenewal = autoRenewal,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Seeds a contract (and optionally one risk for it) via the real app role, inside
    /// its own tenant scope, so RLS's `WITH CHECK` is satisfied on insert — same rationale as
    /// <see cref="DocumentQueryServiceTests.SeedDocumentAsync"/>.</summary>
    private async Task SeedContractAsync(ITenantContext tenantContext, Contract contract, RiskSeverity? risk = null)
    {
        await using var db = CreateAppContext(tenantContext);
        using var scope = tenantContext.BeginScope(contract.TenantId);

        db.Contracts.Add(contract);

        if (risk is { } severity)
        {
            db.Risks.Add(new Risk
            {
                TenantId = contract.TenantId,
                ContractId = contract.Id,
                RiskType = "test-risk",
                Severity = severity,
                Description = "seeded for test",
                IdentifiedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    private static Document NewDocument(
        TenantId tenantId, EntityId? contractId, DocumentProcessingStatus processingStatus) => new()
    {
        TenantId = tenantId,
        ContractId = contractId,
        FileName = "test-contract.pdf",
        MimeType = "application/pdf",
        StoragePath = $"{tenantId.Value}/test-contract.pdf",
        Checksum = "test-checksum",
        ProcessingStatus = processingStatus,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Seeds a document via the real app role, inside its own tenant scope, so RLS's
    /// `WITH CHECK` is satisfied on insert — same rationale as <see cref="SeedContractAsync"/>.
    /// Used by <see cref="GetAnalysisSummaryAsync"/>'s own tests (task E04/F03/US01/T01,
    /// savings-kpis) to control <see cref="Document.ProcessingStatus"/> independently of the
    /// linked <see cref="Contract"/>.</summary>
    private async Task SeedDocumentAsync(ITenantContext tenantContext, Document document)
    {
        await using var db = CreateAppContext(tenantContext);
        using var scope = tenantContext.BeginScope(document.TenantId);

        db.Documents.Add(document);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Returns_spec_columns_and_derives_renewal_date_from_auto_renewal()
    {
        var tenantId = TenantId.New();
        var supplierId = EntityId.New();
        var tenantContext = new TenantContext();

        var autoRenewing = NewContract(
            tenantId,
            supplierId: supplierId,
            type: ContractDocumentType.Msa,
            status: "Active",
            annualSpend: 120_000m,
            startDate: new DateOnly(2026, 1, 1),
            endDate: new DateOnly(2026, 12, 31),
            cancellationDeadline: new DateOnly(2026, 10, 1),
            autoRenewal: true);
        await SeedContractAsync(tenantContext, autoRenewing, RiskSeverity.High);

        var expiring = NewContract(tenantId, endDate: new DateOnly(2026, 12, 31), autoRenewal: false);
        await SeedContractAsync(tenantContext, expiring); // no risk seeded

        await using var db = CreateAppContext(tenantContext);
        var service = new PortfolioQueryService(db, tenantContext, new PortfolioAnalysisCalculator());

        var result = await service.GetPortfolioAsync(tenantId, PortfolioFilter.None);
        var items = result.Items;

        Assert.Equal(2, items.Count);
        Assert.Equal(2, result.TotalCount);

        var autoRenewingRow = Assert.Single(items, i => i.ContractId == autoRenewing.Id.Value);
        Assert.Equal(supplierId.Value, autoRenewingRow.SupplierId);
        Assert.Equal(ContractDocumentType.Msa, autoRenewingRow.Type);
        Assert.Equal(120_000m, autoRenewingRow.AnnualSpend);
        Assert.Equal(new DateOnly(2026, 1, 1), autoRenewingRow.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 31), autoRenewingRow.EndDate);
        Assert.Equal(new DateOnly(2026, 12, 31), autoRenewingRow.RenewalDate); // auto-renewal -> == EndDate
        Assert.Equal(new DateOnly(2026, 10, 1), autoRenewingRow.CancellationDeadline);
        Assert.True(autoRenewingRow.AutoRenewal);
        Assert.Equal("Active", autoRenewingRow.Status);
        Assert.Equal(RiskSeverity.High, autoRenewingRow.Risk);

        var expiringRow = Assert.Single(items, i => i.ContractId == expiring.Id.Value);
        Assert.Null(expiringRow.RenewalDate); // does not auto-renew -> no next renewal date
        Assert.Null(expiringRow.Risk); // no risk rows seeded for this contract
    }

    [Fact]
    public async Task Different_tenant_sees_no_rows()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var tenantContext = new TenantContext();

        await SeedContractAsync(tenantContext, NewContract(tenantA));

        await using var db = CreateAppContext(tenantContext);
        var service = new PortfolioQueryService(db, tenantContext, new PortfolioAnalysisCalculator());

        // AC-3: RLS and the app-level tenant predicate both independently deny a cross-tenant
        // read, even though the row genuinely exists (seeded above) for tenant A.
        var result = await service.GetPortfolioAsync(tenantB, PortfolioFilter.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Filters_apply_independently_and_can_combine_with_auto_renewal()
    {
        var tenantId = TenantId.New();
        var supplierA = EntityId.New();
        var supplierB = EntityId.New();
        var tenantContext = new TenantContext();

        // AC-2 fixture: one seed, then every filter dimension is exercised against it below.
        var c1 = NewContract( // supplier A, cheap, active, does not auto-renew, low risk
            tenantId, supplierId: supplierA, status: "Active", annualSpend: 10_000m,
            endDate: new DateOnly(2026, 12, 31), autoRenewal: false);
        var c2 = NewContract( // supplier B, expensive, expired, auto-renews in range, critical risk
            tenantId, supplierId: supplierB, status: "Expired", annualSpend: 500_000m,
            endDate: new DateOnly(2026, 12, 31), autoRenewal: true);
        var c3 = NewContract( // supplier A, mid-spend, active, auto-renews out of range, no risk
            tenantId, supplierId: supplierA, status: "Active", annualSpend: 250_000m,
            endDate: new DateOnly(2027, 6, 30), autoRenewal: true);

        await SeedContractAsync(tenantContext, c1, RiskSeverity.Low);
        await SeedContractAsync(tenantContext, c2, RiskSeverity.Critical);
        await SeedContractAsync(tenantContext, c3);

        await using var db = CreateAppContext(tenantContext);
        var service = new PortfolioQueryService(db, tenantContext, new PortfolioAnalysisCalculator());

        // Assert.Equivalent (not Assert.Equal) throughout: it compares collections as unordered
        // bags, and GetPortfolioAsync makes no ordering guarantee to rely on here. The default
        // page size (25) comfortably covers this 3-row fixture, so every match always lands on
        // page 1 — pagination itself is covered separately below.
        async Task<List<Guid>> IdsFor(PortfolioFilter filter) =>
            (await service.GetPortfolioAsync(tenantId, filter)).Items.Select(i => i.ContractId).ToList();

        Assert.Equivalent(new[] { c1.Id.Value, c3.Id.Value }, await IdsFor(new PortfolioFilter(SupplierId: supplierA)));
        Assert.Equivalent(new[] { c2.Id.Value }, await IdsFor(new PortfolioFilter(Status: "Expired")));
        Assert.Equivalent(new[] { c2.Id.Value, c3.Id.Value }, await IdsFor(new PortfolioFilter(MinAnnualSpend: 100_000m)));
        Assert.Equivalent(new[] { c1.Id.Value, c3.Id.Value }, await IdsFor(new PortfolioFilter(MaxAnnualSpend: 300_000m)));
        Assert.Equivalent(new[] { c2.Id.Value, c3.Id.Value }, await IdsFor(new PortfolioFilter(AutoRenewal: true)));
        Assert.Equivalent(new[] { c2.Id.Value }, await IdsFor(new PortfolioFilter(Risk: RiskSeverity.Critical)));

        // Renewal period: c1 shares c2's EndDate but does not auto-renew, so it must never match
        // a renewal-period filter; c3's EndDate falls outside this window.
        Assert.Equivalent(
            new[] { c2.Id.Value },
            await IdsFor(new PortfolioFilter(
                RenewalFrom: new DateOnly(2026, 12, 1), RenewalTo: new DateOnly(2026, 12, 31))));

        Assert.Equivalent(
            new[] { c3.Id.Value },
            await IdsFor(new PortfolioFilter(RenewalFrom: new DateOnly(2027, 1, 1))));

        // Unfiltered still returns all three (sanity check that none of the above filters leak).
        Assert.Equivalent(
            new[] { c1.Id.Value, c2.Id.Value, c3.Id.Value }, await IdsFor(PortfolioFilter.None));
    }

    [Fact]
    public async Task Pagination_slices_results_and_reports_total_count()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        var contracts = Enumerable.Range(0, 5)
            .Select(_ => NewContract(tenantId, endDate: new DateOnly(2026, 12, 31)))
            .ToList();
        foreach (var contract in contracts)
        {
            await SeedContractAsync(tenantContext, contract);
        }

        await using var db = CreateAppContext(tenantContext);
        var service = new PortfolioQueryService(db, tenantContext, new PortfolioAnalysisCalculator());

        var firstPage = await service.GetPortfolioAsync(
            tenantId, PortfolioFilter.None, new PortfolioPageRequest(Page: 1, PageSize: 2));
        var secondPage = await service.GetPortfolioAsync(
            tenantId, PortfolioFilter.None, new PortfolioPageRequest(Page: 2, PageSize: 2));
        var thirdPage = await service.GetPortfolioAsync(
            tenantId, PortfolioFilter.None, new PortfolioPageRequest(Page: 3, PageSize: 2));
        var pastLastPage = await service.GetPortfolioAsync(
            tenantId, PortfolioFilter.None, new PortfolioPageRequest(Page: 4, PageSize: 2));

        // 5 rows at page size 2 -> pages of 2, 2, 1, then an empty page past the end.
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Single(thirdPage.Items);
        Assert.Empty(pastLastPage.Items);

        // TotalCount is the same for every page — it counts the whole filtered set, not the slice.
        foreach (var page in new[] { firstPage, secondPage, thirdPage, pastLastPage })
        {
            Assert.Equal(5, page.TotalCount);
        }

        // Page/PageSize on the result echo back exactly what was requested.
        Assert.Equal((1, 2), (firstPage.Page, firstPage.PageSize));
        Assert.Equal((4, 2), (pastLastPage.Page, pastLastPage.PageSize));

        // Every contract appears on exactly one page — pagination neither drops nor duplicates rows.
        var allIds = firstPage.Items.Concat(secondPage.Items).Concat(thirdPage.Items)
            .Select(i => i.ContractId)
            .ToList();
        Assert.Equal(5, allIds.Distinct().Count());
        Assert.Equivalent(contracts.Select(c => c.Id.Value).ToList(), allIds);
    }

    [Fact]
    public async Task Pagination_total_count_reflects_risk_filter_not_unfiltered_set()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        var highRisk1 = NewContract(tenantId);
        var highRisk2 = NewContract(tenantId);
        var noRisk = NewContract(tenantId);

        await SeedContractAsync(tenantContext, highRisk1, RiskSeverity.High);
        await SeedContractAsync(tenantContext, highRisk2, RiskSeverity.High);
        await SeedContractAsync(tenantContext, noRisk); // excluded by the Risk filter below

        await using var db = CreateAppContext(tenantContext);
        var service = new PortfolioQueryService(db, tenantContext, new PortfolioAnalysisCalculator());

        // Risk is computed and filtered in memory, after the SQL-pushable filters run (see
        // PortfolioQueryService's own doc comment on why) — this pins that paging is computed
        // from *that* filtered set (2 rows), not the 3 rows the tenant/no-filter query itself
        // would have matched before Risk was applied.
        var page = await service.GetPortfolioAsync(
            tenantId, new PortfolioFilter(Risk: RiskSeverity.High), new PortfolioPageRequest(Page: 1, PageSize: 1));

        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal(RiskSeverity.High, page.Items[0].Risk);
    }

    // ----- GetAnalysisSummaryAsync (task E04/F03/US01/T01, savings-kpis) -----

    [Fact]
    public async Task GetAnalysisSummaryAsync_counts_only_contracts_with_a_completed_processing_document()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        // Analyzed: its document reached Completed.
        var analyzed = NewContract(tenantId, annualSpend: 100_000m);
        await SeedContractAsync(tenantContext, analyzed);
        await SeedDocumentAsync(tenantContext, NewDocument(tenantId, analyzed.Id, DocumentProcessingStatus.Completed));

        // Not analyzed: extraction has started (StagedExtractionService.EnsureContractAsync's own
        // bootstrap-shell Contract row already exists) but its document has not reached Completed
        // yet — must not count toward either KPI, even though a Contract row genuinely exists.
        var stillProcessing = NewContract(tenantId, annualSpend: 999_999m);
        await SeedContractAsync(tenantContext, stillProcessing);
        await SeedDocumentAsync(
            tenantContext, NewDocument(tenantId, stillProcessing.Id, DocumentProcessingStatus.Processing));

        await using var db = CreateAppContext(tenantContext);
        var service = new PortfolioQueryService(db, tenantContext, new PortfolioAnalysisCalculator());

        var summary = await service.GetAnalysisSummaryAsync(tenantId);

        Assert.Equal(1, summary.ContractsAnalyzedCount);
        var usd = Assert.Single(summary.AnnualSpendAnalyzed);
        Assert.Equal("USD", usd.Currency);
        Assert.Equal(100_000m, usd.Amount);
        Assert.Equal(1, usd.ContractCount);
    }

    [Fact]
    public async Task GetAnalysisSummaryAsync_is_tenant_scoped()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var tenantContext = new TenantContext();

        var contractA = NewContract(tenantA, annualSpend: 50_000m);
        await SeedContractAsync(tenantContext, contractA);
        await SeedDocumentAsync(tenantContext, NewDocument(tenantA, contractA.Id, DocumentProcessingStatus.Completed));

        await using var db = CreateAppContext(tenantContext);
        var service = new PortfolioQueryService(db, tenantContext, new PortfolioAnalysisCalculator());

        // AC-3 (parent story us-01-savings-kpis): a different tenant sees none of tenant A's rows,
        // even though they genuinely exist — same RLS-plus-application-filter guarantee
        // GetPortfolioAsync's own Different_tenant_sees_no_rows test already pins above.
        var summary = await service.GetAnalysisSummaryAsync(tenantB);

        Assert.Equal(0, summary.ContractsAnalyzedCount);
        Assert.Empty(summary.AnnualSpendAnalyzed);
    }
}
