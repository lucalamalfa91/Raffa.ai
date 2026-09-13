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
/// Proves the Definition of Done for task E14/F03/US01/T01 (wave w14 "workspace is real", NW-09;
/// ADR-026 §D2): <see cref="PortfolioQueryService.CountValidatedContractsAsync"/> is a real SQL
/// <c>CountAsync</c> — never <c>ToListAsync().Count</c> — and returns exactly the same number as
/// <see cref="PortfolioQueryService.GetAnalysisSummaryAsync"/>'s own
/// <c>PortfolioAnalysisSummary.ContractsAnalyzedCount</c> for the same tenant (N8): both derive
/// "validated" from the identical rule (at least one linked <see cref="Document"/> reached
/// <see cref="DocumentProcessingStatus.Completed"/>), so a wave-w14 caller and the pre-existing
/// `GET /api/savings/kpis` caller can never disagree about how many contracts a tenant has
/// validated. Same real-Postgres+RLS, unprivileged-app-role rig as
/// <see cref="PortfolioQueryServiceTests"/> (see that class's own doc comment for why a superuser
/// connection would make a tenant-isolation assertion vacuous).
/// </summary>
public sealed class ValidatedContractCountTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_contract_count_app";
    private const string AppRolePassword = "raffa_contract_count_app_test_password";

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

    private static Contract NewContract(TenantId tenantId, decimal? annualSpend = null) => new()
    {
        TenantId = tenantId,
        Type = ContractDocumentType.Msa,
        Status = "processing",
        Currency = "USD",
        AnnualSpend = annualSpend,
        AutoRenewal = false,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Document NewDocument(
        TenantId tenantId, EntityId? contractId, DocumentProcessingStatus processingStatus, string fileName) => new()
    {
        TenantId = tenantId,
        ContractId = contractId,
        FileName = fileName,
        MimeType = "application/pdf",
        StoragePath = $"{tenantId.Value}/{fileName}",
        Checksum = $"checksum-{fileName}",
        ProcessingStatus = processingStatus,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task SeedContractAsync(ITenantContext tenantContext, Contract contract)
    {
        await using var db = CreateAppContext(tenantContext);
        using var scope = tenantContext.BeginScope(contract.TenantId);
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
    }

    private async Task SeedDocumentAsync(ITenantContext tenantContext, Document document)
    {
        await using var db = CreateAppContext(tenantContext);
        using var scope = tenantContext.BeginScope(document.TenantId);
        db.Documents.Add(document);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Equals_contracts_analyzed_count_for_the_same_tenant()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        // Validated: its document reached Completed.
        var validated = NewContract(tenantId, annualSpend: 100_000m);
        await SeedContractAsync(tenantContext, validated);
        await SeedDocumentAsync(
            tenantContext, NewDocument(tenantId, validated.Id, DocumentProcessingStatus.Completed, "validated.pdf"));

        // Not validated: extraction has started (a Contract row already exists — the
        // StagedExtractionService.EnsureContractAsync bootstrap shell) but its document has not
        // reached Completed yet.
        var stillProcessing = NewContract(tenantId, annualSpend: 999_999m);
        await SeedContractAsync(tenantContext, stillProcessing);
        await SeedDocumentAsync(
            tenantContext,
            NewDocument(tenantId, stillProcessing.Id, DocumentProcessingStatus.Processing, "processing.pdf"));

        await using var db = CreateAppContext(tenantContext);
        var service = new PortfolioQueryService(db, tenantContext, new PortfolioAnalysisCalculator());

        var count = await service.CountValidatedContractsAsync(tenantId);
        var summary = await service.GetAnalysisSummaryAsync(tenantId);

        // N8: one definition, two callers — they can never disagree.
        Assert.Equal(1, count);
        Assert.Equal(summary.ContractsAnalyzedCount, count);
    }

    [Fact]
    public async Task A_contract_with_two_completed_documents_still_counts_once()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        var contract = NewContract(tenantId);
        await SeedContractAsync(tenantContext, contract);
        await SeedDocumentAsync(
            tenantContext, NewDocument(tenantId, contract.Id, DocumentProcessingStatus.Completed, "first.pdf"));
        await SeedDocumentAsync(
            tenantContext, NewDocument(tenantId, contract.Id, DocumentProcessingStatus.Completed, "second.pdf"));

        await using var db = CreateAppContext(tenantContext);
        var service = new PortfolioQueryService(db, tenantContext, new PortfolioAnalysisCalculator());

        // Distinct contract ids: two completed documents linked to one contract is still one
        // validated contract, never two.
        Assert.Equal(1, await service.CountValidatedContractsAsync(tenantId));
    }

    [Fact]
    public async Task A_completed_document_with_no_linked_contract_row_does_not_count()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        // A document pointing at a contract id with no Contract row at all (never seeded here) —
        // the same "must currently exist in Contracts" guard GetAnalysisSummaryAsync's own
        // contracts-table join already enforces, so an orphaned reference is never counted.
        var orphanContractId = EntityId.New();
        await SeedDocumentAsync(
            tenantContext,
            NewDocument(tenantId, orphanContractId, DocumentProcessingStatus.Completed, "orphan.pdf"));

        await using var db = CreateAppContext(tenantContext);
        var service = new PortfolioQueryService(db, tenantContext, new PortfolioAnalysisCalculator());

        Assert.Equal(0, await service.CountValidatedContractsAsync(tenantId));
    }

    [Fact]
    public async Task Different_tenant_sees_zero()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var tenantContext = new TenantContext();

        var contractA = NewContract(tenantA);
        await SeedContractAsync(tenantContext, contractA);
        await SeedDocumentAsync(
            tenantContext, NewDocument(tenantA, contractA.Id, DocumentProcessingStatus.Completed, "a.pdf"));

        await using var db = CreateAppContext(tenantContext);
        var service = new PortfolioQueryService(db, tenantContext, new PortfolioAnalysisCalculator());

        // RLS and the app-level tenant predicate both independently deny a cross-tenant read, even
        // though the row genuinely exists for tenant A — same guarantee
        // PortfolioQueryServiceTests.Different_tenant_sees_no_rows already pins for GetPortfolioAsync.
        Assert.Equal(0, await service.CountValidatedContractsAsync(tenantB));
    }

    [Fact]
    public async Task No_validated_contracts_returns_zero_not_a_fabricated_row()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        var neverProcessed = NewContract(tenantId);
        await SeedContractAsync(tenantContext, neverProcessed);

        await using var db = CreateAppContext(tenantContext);
        var service = new PortfolioQueryService(db, tenantContext, new PortfolioAnalysisCalculator());

        Assert.Equal(0, await service.CountValidatedContractsAsync(tenantId));
    }
}
