using Microsoft.EntityFrameworkCore;
using Raffa.Documents.Contracts.Application.V4;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Fixtures;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Infrastructure.Fixtures;

/// <summary>
/// Fixture mode only: the scripted sample document. <c>POST /api/documents/sample</c> creates the
/// prototype's "Salesforce_MSA_2024.pdf" (fixture contract 1) as a document being read, whose
/// contract facts already carry the two fields to verify; the reading then advances on the
/// prototype's timer (<see cref="FixtureSampleDocumentReading"/>). Uploading it again returns the
/// same document (same hash, AskRaffa dedup rule).
/// </summary>
public sealed class FixtureSampleDocuments(DocumentsContractsDbContext dbContext, ITenantContext tenantContext, IClock clock)
{
    public const int SampleFixtureId = 1;

    public async Task<Guid> UploadAsync(FixtureSeedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var _ = tenantContext.BeginScope(context.TenantId);

        var documentId = context.Id("document", SampleFixtureId);
        var existing = await dbContext.Documents
            .AnyAsync(d => d.TenantId == context.TenantId && d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);
        if (existing)
        {
            return documentId.Value;
        }

        var facts = DocumentsContractsFixtureSeeder.BuildFacts(context, SampleFixtureId);
        DocumentsContractsFixtureSeeder.AddContract(dbContext, context, facts, DocumentProcessingStatus.Processing, clock.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return documentId.Value;
    }
}

/// <summary>The prototype's reading timer (600 ms per stage) for the fixture documents, recognised
/// by their fixture checksum.</summary>
public sealed class FixtureSampleDocumentReading : ISampleDocumentReading
{
    public TimeSpan StageDuration { get; } = TimeSpan.FromMilliseconds(600);

    public bool IsSample(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return string.Equals(
            document.Checksum, DocumentsContractsFixtureSeeder.SampleChecksum(document.FileName), StringComparison.Ordinal);
    }
}
