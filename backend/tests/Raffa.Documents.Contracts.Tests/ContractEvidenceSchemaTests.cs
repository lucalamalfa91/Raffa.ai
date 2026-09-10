using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Proves the Definition of Done for task E02/F02/US01/T02 (task-02-schema-evidence, AC-2):
/// every consequential fact this story owns — <see cref="Contract"/>,
/// <see cref="ContractLineItem"/>, <see cref="Clause"/>, <see cref="Obligation"/>,
/// <see cref="Risk"/> — carries an evidence pointer (source document/span/page), a confidence
/// score, and an optimistic-concurrency <c>version</c> column, and those columns genuinely
/// round-trip through EF Core against a real Postgres instance (Appendix C rule 2: never show a
/// consequential extracted fact without source evidence and confidence metadata; rule 5: never
/// destructively overwrite contract history or corrections).
///
/// <see cref="CorrectionHistory"/> and <see cref="ContractVersion"/> are deliberately excluded
/// from this proof: they are the audit-trail/snapshot *mechanism* itself, not AI-extracted
/// consequential facts, so AC-2 does not apply to them. <see cref="Contract"/>'s own scalar
/// fields (dates, spend, auto-renewal, ...) are populated from evidenced <see cref="Clause"/>
/// rows rather than carrying parallel per-field evidence columns of their own — product spec §6
/// lists confidence/source only for "ContractClause", not "Contract" — but <see cref="Contract"/>
/// still gets the <c>version</c> concurrency guard because it is itself directly correctable.
///
/// Spins up its own disposable Postgres+pgvector container (Testcontainers), matching
/// <see cref="ContractLineItemSchemaTests"/>/<see cref="DocumentsContractsMigrationTests"/>.
/// </summary>
public sealed class ContractEvidenceSchemaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private DocumentsContractsDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString());
        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }

    /// <summary>(table, column, expected information_schema.columns.data_type, is_nullable) for
    /// every evidence/confidence/version column AC-2 requires — pre-existing (task-01) and new
    /// (this task) alike, so this is a complete proof, not just a diff.</summary>
    private static readonly (string Table, string Column, string DataType, bool IsNullable)[] EvidenceColumns =
    [
        ("contract", "version", "integer", false),

        ("contract_line_item", "source_document_id", "uuid", true),
        ("contract_line_item", "source_span", "character varying", true),
        ("contract_line_item", "source_page", "integer", true),
        ("contract_line_item", "confidence", "double precision", true),
        ("contract_line_item", "version", "integer", false),

        ("clause", "source_document_id", "uuid", true),
        ("clause", "source_span", "character varying", true),
        ("clause", "source_page", "integer", true),
        ("clause", "confidence", "double precision", true),
        ("clause", "version", "integer", false),

        ("obligation", "source_document_id", "uuid", true),
        ("obligation", "source_span", "character varying", true),
        ("obligation", "source_page", "integer", true),
        ("obligation", "confidence", "double precision", true),
        ("obligation", "version", "integer", false),

        ("risk", "source_document_id", "uuid", true),
        ("risk", "source_span", "character varying", true),
        ("risk", "source_page", "integer", true),
        ("risk", "confidence", "double precision", true),
        ("risk", "version", "integer", false),
    ];

    [Fact]
    public async Task Migration_adds_evidence_confidence_and_version_columns_to_every_us01_consequential_fact_table()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        await db.Database.OpenConnectionAsync();
        try
        {
            foreach (var expected in EvidenceColumns)
            {
                var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText =
                    "SELECT data_type, is_nullable FROM information_schema.columns " +
                    "WHERE table_schema = 'public' AND table_name = @table AND column_name = @column";
                var tableParameter = command.CreateParameter();
                tableParameter.ParameterName = "table";
                tableParameter.Value = expected.Table;
                command.Parameters.Add(tableParameter);
                var columnParameter = command.CreateParameter();
                columnParameter.ParameterName = "column";
                columnParameter.Value = expected.Column;
                command.Parameters.Add(columnParameter);

                await using var reader = await command.ExecuteReaderAsync();
                var hasRow = await reader.ReadAsync();

                Assert.True(
                    hasRow,
                    $"[AC-2] Table \"{expected.Table}\" has no \"{expected.Column}\" column after migrating.");
                Assert.Equal(expected.DataType, reader.GetString(0));
                Assert.Equal(expected.IsNullable ? "YES" : "NO", reader.GetString(1));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task Evidence_confidence_and_version_round_trip_through_ef_core_for_every_us01_fact()
    {
        await using (var migrateDb = CreateContext())
        {
            await migrateDb.Database.MigrateAsync();
        }

        var tenantId = TenantId.New();
        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "Active",
            Currency = "USD",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var document = new Document
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            FileName = "msa-2026.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value}/msa-2026.pdf",
            Checksum = "sha256:test-checksum",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Spec §7.2 "Price / SKU / line-item extraction" — its own evidenced extraction stage;
        // ContractLineItem previously had no evidence/confidence at all (task-01 gap this task closes).
        var lineItem = new ContractLineItem
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            Description = "Enterprise plan — 250 named users",
            SourceDocumentId = document.Id,
            SourceSpan = "Schedule A, p.4",
            SourcePage = 4,
            Confidence = 0.92,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Spec §6 "ContractClause": contract_id, ..., source_document, page/section, confidence.
        var clause = new Clause
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            SourceDocumentId = document.Id,
            ClauseType = "AutoRenewal",
            RawText = "This Agreement renews automatically for successive one-year terms.",
            SourceSpan = "§8.4",
            SourcePage = 12,
            Confidence = 0.97,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Spec §6 "Obligation" minimum fields end in "... source"; §7.3's structured-output
        // example expresses that source as page + section, mirroring Clause — Obligation
        // previously had SourceDocumentId/Confidence but no page/section (task-01 gap).
        var obligation = new Obligation
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            SourceDocumentId = document.Id,
            Party = "Customer",
            ObligationType = "RenewalNotice",
            Description = "Provide written notice 90 days before the renewal date.",
            SourceSpan = "§8.4",
            SourcePage = 12,
            Confidence = 0.9,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Risk identified directly from the document text, with no pre-extracted Clause behind
        // it (ClauseId left null) — must still carry its own direct evidence (Appendix C rule 2),
        // not only the indirect trace through an optional Clause.
        var risk = new Risk
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            SourceDocumentId = document.Id,
            RiskType = "AutoRenewal",
            Severity = RiskSeverity.Medium,
            Description = "Auto-renewal with a narrow 90-day cancellation window.",
            SourceSpan = "§8.4",
            SourcePage = 12,
            Confidence = 0.85,
            IdentifiedAt = DateTimeOffset.UtcNow,
        };

        await using (var writeDb = CreateContext())
        {
            writeDb.Contracts.Add(contract);
            writeDb.Documents.Add(document);
            writeDb.ContractLineItems.Add(lineItem);
            writeDb.Clauses.Add(clause);
            writeDb.Obligations.Add(obligation);
            writeDb.Risks.Add(risk);
            await writeDb.SaveChangesAsync();
        }

        // Fresh context/connection: reads back from Postgres, not the change tracker.
        await using var readDb = CreateContext();

        var storedContract = await readDb.Contracts.SingleAsync(c => c.Id == contract.Id);
        Assert.Equal(1, storedContract.Version);

        var storedLineItem = await readDb.ContractLineItems.SingleAsync(li => li.Id == lineItem.Id);
        Assert.Equal(document.Id, storedLineItem.SourceDocumentId);
        Assert.Equal("Schedule A, p.4", storedLineItem.SourceSpan);
        Assert.Equal(4, storedLineItem.SourcePage);
        Assert.Equal(0.92, storedLineItem.Confidence);
        Assert.Equal(1, storedLineItem.Version);

        var storedClause = await readDb.Clauses.SingleAsync(c => c.Id == clause.Id);
        Assert.Equal(document.Id, storedClause.SourceDocumentId);
        Assert.Equal("§8.4", storedClause.SourceSpan);
        Assert.Equal(12, storedClause.SourcePage);
        Assert.Equal(0.97, storedClause.Confidence);
        Assert.Equal(1, storedClause.Version);

        var storedObligation = await readDb.Obligations.SingleAsync(o => o.Id == obligation.Id);
        Assert.Equal(document.Id, storedObligation.SourceDocumentId);
        Assert.Equal("§8.4", storedObligation.SourceSpan);
        Assert.Equal(12, storedObligation.SourcePage);
        Assert.Equal(0.9, storedObligation.Confidence);
        Assert.Equal(1, storedObligation.Version);

        var storedRisk = await readDb.Risks.SingleAsync(r => r.Id == risk.Id);
        Assert.Null(storedRisk.ClauseId);
        Assert.Equal(document.Id, storedRisk.SourceDocumentId);
        Assert.Equal("§8.4", storedRisk.SourceSpan);
        Assert.Equal(12, storedRisk.SourcePage);
        Assert.Equal(0.85, storedRisk.Confidence);
        Assert.Equal(1, storedRisk.Version);
    }

    [Fact]
    public async Task Version_concurrency_token_rejects_a_write_against_a_stale_read()
    {
        await using (var migrateDb = CreateContext())
        {
            await migrateDb.Database.MigrateAsync();
        }

        var tenantId = TenantId.New();
        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "Active",
            Currency = "USD",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using (var writeDb = CreateContext())
        {
            writeDb.Contracts.Add(contract);
            await writeDb.SaveChangesAsync();
        }

        // Two independent readers load the same row — simulating a background re-extraction job
        // racing a human correction over the same Contract (Appendix C rule 5: never
        // destructively overwrite contract history or human corrections).
        await using var firstReader = CreateContext();
        var first = await firstReader.Contracts.SingleAsync(c => c.Id == contract.Id);

        await using var secondReader = CreateContext();
        var second = await secondReader.Contracts.SingleAsync(c => c.Id == contract.Id);

        // The first writer's change is accepted and moves the row to version 2.
        first.Status = "Renewed";
        first.Version++;
        await firstReader.SaveChangesAsync();

        // The second writer still holds the pre-update version (1), so its write must be
        // rejected rather than silently clobber the first writer's change.
        second.Status = "Terminated";
        second.Version++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondReader.SaveChangesAsync());
    }
}

/// <summary>
/// Proves the <c>version</c> column task E02/F02/US01/T02 adds is wired as a genuine EF Core
/// optimistic-concurrency token — not just a plain integer column — on every entity this
/// story's AC-2 covers. Building <see cref="DbContext.Model"/> walks <c>OnModelCreating</c> but
/// opens no database connection, so unlike <see cref="ContractEvidenceSchemaTests"/> this class
/// needs no Postgres container.
/// </summary>
public sealed class ContractEvidenceVersionConcurrencyTokenTests
{
    private static DocumentsContractsDbContext CreateModelOnlyContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(
            optionsBuilder,
            "Host=localhost;Database=schema-model-only;Username=test;Password=test");
        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }

    [Theory]
    [InlineData(typeof(Contract))]
    [InlineData(typeof(ContractLineItem))]
    [InlineData(typeof(Clause))]
    [InlineData(typeof(Obligation))]
    [InlineData(typeof(Risk))]
    public void Version_is_configured_as_an_ef_core_concurrency_token(Type entityType)
    {
        using var db = CreateModelOnlyContext();

        var property = db.Model.FindEntityType(entityType)?.FindProperty("Version");

        Assert.NotNull(property);
        Assert.True(
            property!.IsConcurrencyToken,
            $"[AC-2] {entityType.Name}.Version must be an EF Core concurrency token (Appendix C rule 5).");
    }
}
