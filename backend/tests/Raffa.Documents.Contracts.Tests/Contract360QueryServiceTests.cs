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
/// Proves the Definition of Done for task E02/F03/US02/T01 (us-02-contract-360-aggregate,
/// AC-1/AC-2/AC-3): <see cref="Contract360QueryService.GetByIdAsync"/> assembles the spec §8.2
/// header + tab aggregate from <see cref="Contract"/>/<see cref="ContractLineItem"/>/
/// <see cref="Clause"/>/<see cref="Obligation"/>/<see cref="Risk"/>/<see cref="Document"/> and
/// enforces tenant scoping — against a real Postgres+RLS database, mirroring
/// <see cref="PortfolioQueryServiceTests"/>'s own unprivileged-role rationale so a passing
/// "a different tenant gets nothing back" assertion is a real RLS proof, not a tautology from a
/// superuser connection that unconditionally bypasses row security.
///
/// There is no "ContractUploadService" yet (contract rows are populated by the extraction
/// pipeline, a different, not-yet-built caller) — like <see cref="PortfolioQueryServiceTests"/>,
/// this class seeds <see cref="Contract"/> and its child rows directly via the app-role
/// <see cref="DocumentsContractsDbContext"/>, inside the same tenant scope a real writer would
/// use, so the RLS `WITH CHECK` on insert is satisfied.
/// </summary>
public sealed class Contract360QueryServiceTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_contract360_app";
    private const string AppRolePassword = "raffa_contract360_app_test_password";

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
            // Applies every migration up to and including AddEvidenceConfidenceVersionColumns —
            // covers contract/contract_line_item/clause/obligation/risk/document, all RLS-enabled.
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

    /// <summary>Seeds one contract with one of each child fact (two line items — one with a null
    /// per-line cost, to prove <see cref="Contract360Commercials"/>'s rollup rule — two risks, of
    /// different severity, to prove the header's "highest severity" rule), all via the real
    /// app role inside its own tenant scope so RLS's `WITH CHECK` is satisfied on insert.</summary>
    private async Task<Contract> SeedFullContractAsync(ITenantContext tenantContext, TenantId tenantId, EntityId supplierId)
    {
        await using var db = CreateAppContext(tenantContext);
        using var scope = tenantContext.BeginScope(tenantId);

        var contract = new Contract
        {
            TenantId = tenantId,
            SupplierId = supplierId,
            Type = ContractDocumentType.Msa,
            Status = "Active",
            Currency = "USD",
            AnnualSpend = 120_000m,
            TotalContractValue = 360_000m,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            EffectiveDate = new DateOnly(2026, 1, 1),
            CancellationDeadline = new DateOnly(2026, 10, 1),
            AutoRenewal = true,
            RenewalTermMonths = 12,
            PaymentTerms = "Net 30",
            GoverningLaw = "Swiss law",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Contracts.Add(contract);

        db.ContractLineItems.Add(new ContractLineItem
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            Sku = "SKU-1",
            Description = "Enterprise seats",
            Quantity = 100m,
            Unit = "seat",
            UnitPrice = 100m,
            ListPrice = 120m,
            Discount = 10m,
            BillingPeriod = "Annual",
            AnnualCost = 10_000m,
            TotalCost = 30_000m,
            SourceSpan = "p.2 table 1",
            SourcePage = 2,
            Confidence = 0.92,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.ContractLineItems.Add(new ContractLineItem
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            Description = "Support plan (cost not yet extracted)",
            AnnualCost = null, // deliberately missing -> rollup must treat this as 0, not null-out the total
            TotalCost = 5_000m,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Clauses.Add(new Clause
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            ClauseType = "LimitationOfLiability",
            RawText = "Liability shall not exceed fees paid in the preceding 12 months.",
            NormalizedValue = "12-month fees cap",
            RiskLevel = RiskSeverity.Medium,
            SourceSpan = "p.5 s.9.2",
            SourcePage = 5,
            Confidence = 0.88,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Obligations.Add(new Obligation
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            Party = "Customer",
            ObligationType = "Notice",
            Description = "Provide 90 days written notice to cancel.",
            DueDate = new DateOnly(2026, 10, 1),
            Criticality = "High",
            Status = "Open",
            Confidence = 0.95,
            SourceSpan = "p.3 s.4.1",
            SourcePage = 3,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Risks.Add(new Risk
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            RiskType = "AutoRenewal",
            Severity = RiskSeverity.High,
            Description = "Auto-renews without an active opt-out.",
            Confidence = 0.8,
            Status = "Open",
            SourceSpan = "p.5 s.9.2",
            SourcePage = 5,
            IdentifiedAt = DateTimeOffset.UtcNow,
        });
        db.Risks.Add(new Risk
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            RiskType = "UnlimitedLiability",
            Severity = RiskSeverity.Critical,
            Description = "Uncapped liability for data breach.",
            IdentifiedAt = DateTimeOffset.UtcNow,
        });

        db.Documents.Add(new Document
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            FileName = "msa.pdf",
            MimeType = "application/pdf",
            DocumentType = ContractDocumentType.Msa,
            StoragePath = $"{tenantId}/{contract.Id}/1/msa.pdf",
            Checksum = "deadbeef",
            ProcessingStatus = DocumentProcessingStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
        return contract;
    }

    private async Task<Contract> SeedBareContractAsync(ITenantContext tenantContext, TenantId tenantId)
    {
        await using var db = CreateAppContext(tenantContext);
        using var scope = tenantContext.BeginScope(tenantId);

        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.OrderForm,
            Status = "Active",
            Currency = "USD",
            AutoRenewal = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task Returns_the_full_360_aggregate_for_the_owning_tenant()
    {
        var tenantId = TenantId.New();
        var supplierId = EntityId.New();
        var tenantContext = new TenantContext();

        var contract = await SeedFullContractAsync(tenantContext, tenantId, supplierId);

        await using var db = CreateAppContext(tenantContext);
        var service = new Contract360QueryService(db, tenantContext);

        var result = await service.GetByIdAsync(tenantId, contract.Id);

        Assert.NotNull(result);
        Assert.Equal(contract.Id, result!.ContractId);

        // AC-1/spec §8.2 header: "supplier, contract name/type, annual spend, TCV, start/end,
        // renewal date, cancellation deadline" + the derived Renewal/Risk this task also carries.
        Assert.Equal(contract.Id, result.Header.ContractId);
        Assert.Equal(supplierId, result.Header.SupplierId);
        Assert.Equal(ContractDocumentType.Msa, result.Header.Type);
        Assert.Equal("Active", result.Header.Status);
        Assert.Equal(120_000m, result.Header.AnnualSpend);
        Assert.Equal(360_000m, result.Header.TotalContractValue);
        Assert.Equal(new DateOnly(2026, 1, 1), result.Header.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 31), result.Header.EndDate);
        Assert.Equal(new DateOnly(2026, 12, 31), result.Header.RenewalDate); // auto-renewal -> == EndDate
        Assert.Equal(new DateOnly(2026, 10, 1), result.Header.CancellationDeadline);
        Assert.True(result.Header.AutoRenewal);
        Assert.Equal(RiskSeverity.Critical, result.Header.Risk); // highest of High/Critical seeded

        // Overview: the descriptive fields not already on the header.
        Assert.Equal("USD", result.Overview.Currency);
        Assert.Equal(new DateOnly(2026, 1, 1), result.Overview.EffectiveDate);
        Assert.Equal(12, result.Overview.RenewalTermMonths);
        Assert.Equal("Net 30", result.Overview.PaymentTerms);
        Assert.Equal("Swiss law", result.Overview.GoverningLaw);
        Assert.Null(result.Overview.ParentContractId);
        Assert.Equal(1, result.Overview.Version);

        // AC-2 "Commercials ... read from StructuredContracts + line items": contract-level terms
        // plus the line-item rollup, where a missing per-line AnnualCost contributes 0 (not null).
        Assert.Equal(120_000m, result.Commercials.AnnualSpend);
        Assert.Equal(360_000m, result.Commercials.TotalContractValue);
        Assert.Equal(2, result.Commercials.LineItemCount);
        Assert.Equal(10_000m, result.Commercials.LineItemAnnualCostTotal); // 10_000 + (null -> 0)
        Assert.Equal(35_000m, result.Commercials.LineItemTotalCostTotal); // 30_000 + 5_000

        // AC-2 "products read from ... line items".
        Assert.Equal(2, result.Products.Count);
        var primaryLine = Assert.Single(result.Products, p => p.Sku == "SKU-1");
        Assert.Equal("Enterprise seats", primaryLine.Description);
        Assert.Equal(100m, primaryLine.Quantity);
        Assert.Equal(10_000m, primaryLine.AnnualCost);
        Assert.Equal("p.2 table 1", primaryLine.SourceSpan);
        Assert.Equal(2, primaryLine.SourcePage);
        Assert.Equal(0.92, primaryLine.Confidence);

        // AC-2 "clauses/obligations/risks from extracted facts".
        var clause = Assert.Single(result.Clauses);
        Assert.Equal("LimitationOfLiability", clause.ClauseType);
        Assert.Equal(RiskSeverity.Medium, clause.RiskLevel);
        Assert.Equal(0.88, clause.Confidence);

        var obligation = Assert.Single(result.Obligations);
        Assert.Equal("Customer", obligation.Party);
        Assert.Equal(new DateOnly(2026, 10, 1), obligation.DueDate);
        Assert.Equal("High", obligation.Criticality);

        Assert.Equal(2, result.Risks.Count);
        Assert.Contains(result.Risks, r => r.RiskType == "AutoRenewal" && r.Severity == RiskSeverity.High);
        Assert.Contains(result.Risks, r => r.RiskType == "UnlimitedLiability" && r.Severity == RiskSeverity.Critical);

        var document = Assert.Single(result.Documents);
        Assert.Equal("msa.pdf", document.FileName);
        Assert.Equal(DocumentProcessingStatus.Completed, document.ProcessingStatus);

        // Renewal tab mirrors the header's derived renewal fields (see that record's doc comment).
        Assert.Equal(contract.EndDate, result.Renewal.EndDate);
        Assert.Equal(contract.EndDate, result.Renewal.RenewalDate);
        Assert.Equal(contract.CancellationDeadline, result.Renewal.CancellationDeadline);
        Assert.True(result.Renewal.AutoRenewal);
        Assert.Equal(12, result.Renewal.RenewalTermMonths);

        // us-02 "Task-count note": benchmark/activity are R3/R4 placeholders, always empty.
        Assert.Empty(result.Benchmark);
        Assert.Empty(result.Activity);
    }

    [Fact]
    public async Task Line_item_and_risk_rollups_are_empty_for_a_contract_with_no_child_rows()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        var contract = await SeedBareContractAsync(tenantContext, tenantId);

        await using var db = CreateAppContext(tenantContext);
        var service = new Contract360QueryService(db, tenantContext);

        var result = await service.GetByIdAsync(tenantId, contract.Id);

        Assert.NotNull(result);
        Assert.Null(result!.Header.Risk);
        Assert.Null(result.Header.RenewalDate); // does not auto-renew -> no next renewal date

        Assert.Equal(0, result.Commercials.LineItemCount);
        Assert.Null(result.Commercials.LineItemAnnualCostTotal); // zero line items -> null, not 0
        Assert.Null(result.Commercials.LineItemTotalCostTotal);

        Assert.Empty(result.Products);
        Assert.Empty(result.Clauses);
        Assert.Empty(result.Obligations);
        Assert.Empty(result.Risks);
        Assert.Empty(result.Documents);
        Assert.Empty(result.Benchmark);
        Assert.Empty(result.Activity);
    }

    [Fact]
    public async Task Returns_null_for_a_contract_that_belongs_to_a_different_tenant()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var tenantContext = new TenantContext();

        var contract = await SeedBareContractAsync(tenantContext, tenantA);

        await using var db = CreateAppContext(tenantContext);
        var service = new Contract360QueryService(db, tenantContext);

        // AC-3: RLS and the app-level tenant predicate both independently deny a cross-tenant
        // read, even though the row genuinely exists (seeded above) for tenant A.
        var result = await service.GetByIdAsync(tenantB, contract.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_null_for_an_id_that_does_not_exist()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var db = CreateAppContext(tenantContext);
        var service = new Contract360QueryService(db, tenantContext);

        var result = await service.GetByIdAsync(tenantId, EntityId.New());

        Assert.Null(result);
    }
}
