using Contigo.Documents.Contracts.Application;
using Contigo.Documents.Contracts.Domain;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Contigo.Documents.Contracts.Tests;

/// <summary>
/// <see cref="DocumentValidationService"/> — the review sign-off behind
/// <c>POST /api/documents/{id}/validate</c>: a reviewed document becomes <c>completed</c> with one
/// audit row naming the accepted fields, a document that is not yet reviewable is refused with a
/// named reason, a second sign-off is an audited no-op, and another tenant's document is simply
/// not found. Real Postgres (same Testcontainer pattern as <see cref="StagedExtractionServiceTests"/>)
/// because the status write goes through the RLS-scoped <see cref="DocumentsContractsDbContext"/>.
/// </summary>
public sealed class DocumentValidationServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 15, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = CreateContext(new TenantContext());
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private DocumentsContractsDbContext CreateContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString(), tenantContext);
        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Written { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Written.Add(entry);
            return Task.CompletedTask;
        }
    }

    private static async Task<Document> SeedDocumentAsync(
        DocumentsContractsDbContext db, TenantId tenantId, DocumentProcessingStatus status)
    {
        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "active",
            Currency = "EUR",
            CreatedAt = Now,
        };
        var document = new Document
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            FileName = "msa.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value:D}/documents/msa.pdf",
            Checksum = "checksum",
            ProcessingStatus = status,
            CreatedAt = Now,
        };

        db.Contracts.Add(contract);
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return document;
    }

    [Fact]
    public async Task A_document_in_needs_review_becomes_completed_and_the_sign_off_is_audited()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var document = await SeedDocumentAsync(seedDb, tenantId, DocumentProcessingStatus.NeedsReview);

        var audit = new RecordingAuditWriter();
        await using var runDb = CreateContext(tenantContext);
        var service = new DocumentValidationService(runDb, tenantContext, audit, new FixedClock(Now));

        var result = await service.ValidateAsync(
            tenantId, document.Id, ["currency", " autoRenewal ", "currency", ""], "buyer@acme.example");

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentProcessingStatus.Completed, result.Value.ProcessingStatus);
        Assert.Equal(document.ContractId, result.Value.ContractId);
        Assert.False(result.Value.AlreadyValidated);
        Assert.Equal(Now, result.Value.ValidatedAt);
        // Trimmed, de-duplicated, blanks dropped — what the audit row records.
        Assert.Equal(["currency", "autoRenewal"], result.Value.AcceptedFields);

        await using var readDb = CreateContext(tenantContext);
        using (tenantContext.BeginScope(tenantId))
        {
            var stored = await readDb.Documents.SingleAsync(d => d.Id == document.Id);
            Assert.Equal(DocumentProcessingStatus.Completed, stored.ProcessingStatus);
        }

        var entry = Assert.Single(audit.Written);
        Assert.Equal(DocumentValidationService.ValidatedAuditAction, entry.Action);
        Assert.Equal("buyer@acme.example", entry.Actor);
        Assert.Equal("document", entry.ResourceType);
        Assert.Equal(document.Id.Value.ToString(), entry.ResourceId);
        Assert.Contains("acceptedFields=currency,autoRenewal", entry.Detail);
        Assert.Contains($"contractId={document.ContractId!.Value.Value}", entry.Detail);
    }

    [Fact]
    public async Task Validating_an_already_completed_document_is_an_audited_no_op()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var document = await SeedDocumentAsync(seedDb, tenantId, DocumentProcessingStatus.Completed);

        var audit = new RecordingAuditWriter();
        await using var runDb = CreateContext(tenantContext);
        var service = new DocumentValidationService(runDb, tenantContext, audit, new FixedClock(Now));

        var result = await service.ValidateAsync(tenantId, document.Id, [], "buyer@acme.example");

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AlreadyValidated);
        Assert.Equal(DocumentProcessingStatus.Completed, result.Value.ProcessingStatus);
        Assert.Contains("alreadyValidated=True", Assert.Single(audit.Written).Detail);
    }

    [Theory]
    [InlineData(DocumentProcessingStatus.Uploaded, DocumentValidationService.StillProcessingError)]
    [InlineData(DocumentProcessingStatus.Processing, DocumentValidationService.StillProcessingError)]
    [InlineData(DocumentProcessingStatus.Failed, DocumentValidationService.FailedProcessingError)]
    public async Task A_document_that_is_not_reviewable_is_refused_with_a_named_reason_and_left_untouched(
        DocumentProcessingStatus status, string expectedError)
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var document = await SeedDocumentAsync(seedDb, tenantId, status);

        var audit = new RecordingAuditWriter();
        await using var runDb = CreateContext(tenantContext);
        var service = new DocumentValidationService(runDb, tenantContext, audit, new FixedClock(Now));

        var result = await service.ValidateAsync(tenantId, document.Id, ["currency"], "buyer@acme.example");

        Assert.NotNull(result);
        Assert.True(result.IsFailure);
        Assert.Equal(expectedError, result.Error);
        Assert.Empty(audit.Written);

        await using var readDb = CreateContext(tenantContext);
        using var tenantScope = tenantContext.BeginScope(tenantId);
        Assert.Equal(status, (await readDb.Documents.SingleAsync(d => d.Id == document.Id)).ProcessingStatus);
    }

    [Fact]
    public async Task Another_tenants_document_and_an_unknown_id_both_read_as_not_found()
    {
        var owner = TenantId.New();
        var other = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var document = await SeedDocumentAsync(seedDb, owner, DocumentProcessingStatus.NeedsReview);

        var audit = new RecordingAuditWriter();
        await using var runDb = CreateContext(tenantContext);
        var service = new DocumentValidationService(runDb, tenantContext, audit, new FixedClock(Now));

        Assert.Null(await service.ValidateAsync(other, document.Id, [], "intruder@else.example"));
        Assert.Null(await service.ValidateAsync(owner, EntityId.New(), [], "buyer@acme.example"));
        Assert.Empty(audit.Written);

        await using var readDb = CreateContext(tenantContext);
        using var tenantScope = tenantContext.BeginScope(owner);
        Assert.Equal(
            DocumentProcessingStatus.NeedsReview,
            (await readDb.Documents.SingleAsync(d => d.Id == document.Id)).ProcessingStatus);
    }
}
