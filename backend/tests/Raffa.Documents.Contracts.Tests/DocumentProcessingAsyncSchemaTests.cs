using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Proves AC-1, AC-3 and AC-5 of task E16/F02/US01/T01 (async-processing-schema, ADR-027 §D6):
/// the new <c>extraction_job</c>/<c>document</c> columns are genuinely nullable-or-defaulted (not
/// just declared that way in C#), and <see cref="DocumentProcessingStatus.Rejected"/> round-trips
/// through <c>processing_status</c> with no schema change to that column.
/// </summary>
public sealed class DocumentProcessingAsyncSchemaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private DocumentsContractsDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString());
        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Extraction_job_claim_columns_are_nullable_or_defaulted()
    {
        var tenantId = TenantId.New();
        var document = new Document
        {
            TenantId = tenantId,
            FileName = "contract.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value}/contract.pdf",
            Checksum = "checksum",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        // Deliberately does not set AttemptCount/ClaimedAt/ClaimedBy -- AC-1 requires these to be
        // safe to omit, exactly as every pre-existing row (written by the previous API image,
        // AC-2) will be after this migration applies.
        var job = new ExtractionJob
        {
            TenantId = tenantId,
            DocumentId = document.Id,
            Stage = ExtractionStage.Classification,
            QueuedAt = DateTimeOffset.UtcNow,
        };

        await using (var writeDb = CreateContext())
        {
            writeDb.Documents.Add(document);
            writeDb.ExtractionJobs.Add(job);
            await writeDb.SaveChangesAsync();
        }

        await using var readDb = CreateContext();
        var reloaded = await readDb.ExtractionJobs.SingleAsync(j => j.Id == job.Id);

        Assert.Equal(0, reloaded.AttemptCount);
        Assert.Null(reloaded.ClaimedAt);
        Assert.Null(reloaded.ClaimedBy);
    }

    [Fact]
    public async Task Document_rejection_columns_are_nullable_and_absent_on_an_admitted_document()
    {
        var tenantId = TenantId.New();
        var document = new Document
        {
            TenantId = tenantId,
            FileName = "contract.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value}/contract.pdf",
            Checksum = "checksum",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        // ProcessingStatus stays at its default (Uploaded) -- an admitted document never touches
        // the rejection columns.

        await using (var writeDb = CreateContext())
        {
            writeDb.Documents.Add(document);
            await writeDb.SaveChangesAsync();
        }

        await using var readDb = CreateContext();
        var reloaded = await readDb.Documents.SingleAsync(d => d.Id == document.Id);

        Assert.Null(reloaded.RejectionReason);
        Assert.Null(reloaded.RejectionDetectedType);
        Assert.Null(reloaded.RejectionConfidence);
    }

    [Fact]
    public async Task Rejected_status_and_rejection_fields_round_trip_with_no_schema_change_to_processing_status()
    {
        var tenantId = TenantId.New();
        var document = new Document
        {
            TenantId = tenantId,
            FileName = "recipe.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value}/recipe.pdf",
            Checksum = "checksum",
            CreatedAt = DateTimeOffset.UtcNow,
            ProcessingStatus = DocumentProcessingStatus.Rejected,
            RejectionReason = AdmissionRejectionReason.NotAContract,
            RejectionDetectedType = ContractDocumentType.Other,
            RejectionConfidence = 0.12,
        };

        await using (var writeDb = CreateContext())
        {
            writeDb.Documents.Add(document);
            await writeDb.SaveChangesAsync();
        }

        await using var readDb = CreateContext();
        var reloaded = await readDb.Documents.SingleAsync(d => d.Id == document.Id);

        Assert.Equal(DocumentProcessingStatus.Rejected, reloaded.ProcessingStatus);
        Assert.Equal(AdmissionRejectionReason.NotAContract, reloaded.RejectionReason);
        Assert.Equal(ContractDocumentType.Other, reloaded.RejectionDetectedType);
        Assert.Equal(0.12, reloaded.RejectionConfidence);

        // AC-3, verified at the physical column, not just through the EF model: `Rejected` added
        // no CHECK constraint, no enum type and no default to `processing_status` -- the wire
        // value is a plain string this column already accepted before this task existed.
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT character_maximum_length, is_nullable, column_default
              FROM information_schema.columns
             WHERE table_name = 'document' AND column_name = 'processing_status'
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(30, reader.GetInt32(0));
        Assert.Equal("NO", reader.GetString(1));
        Assert.True(await reader.IsDBNullAsync(2), "processing_status must keep no column default.");

        await using var checkConstraints = new NpgsqlCommand(
            """
            SELECT count(*) FROM information_schema.constraint_column_usage
             WHERE table_name = 'document' AND column_name = 'processing_status'
            """,
            connection);
        var constraintCount = (long)(await checkConstraints.ExecuteScalarAsync())!;
        Assert.Equal(0L, constraintCount);
    }
}
