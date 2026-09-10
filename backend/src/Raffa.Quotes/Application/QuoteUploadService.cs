using System.Security.Cryptography;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Quotes.Application;

/// <summary>
/// Implements task E05/F01/US01/T01 (quote-extraction; parent story
/// us-01-quote-line-extraction AC-1): stores the uploaded bytes in tenant-scoped object storage
/// (no cross-tenant path) and persists the <see cref="Quote"/> + a queued
/// <see cref="QuoteExtractionJob"/> as one unit of work — mirrors
/// <c>Raffa.Documents.Contracts.Application.DocumentUploadService</c>'s own shape exactly (see
/// <see cref="Quote"/>'s own doc comment for why this module has its own entity rather than
/// reusing <c>Document</c>).
///
/// Owns its own tenant scope (<see cref="ITenantContext.BeginScope"/>) for the duration of the
/// call instead of relying on the caller to have entered one — every caller (the
/// `POST /api/quotes` endpoint today) gets the ADR-009 RLS backstop automatically.
/// </summary>
public sealed class QuoteUploadService(
    QuotesDbContext dbContext,
    IDocumentStorage storage,
    ITenantContext tenantContext,
    IClock clock,
    IAuditWriter auditWriter)
{
    private const int InitialVersionNumber = 1;

    /// <summary>Placeholder actor recorded on the audit entry until the API validates a caller
    /// identity token — same interim gap as
    /// <c>Raffa.Documents.Contracts.Application.DocumentUploadService.UnattributedActor</c>
    /// (ADR-010 is not listed in this task's "Architecture decisions in force"), for the identical
    /// reason: there is no validated caller principal yet, so there is nothing truthful to record
    /// here beyond this explicit placeholder.</summary>
    private const string UnattributedActor = "unattributed";

    /// <summary>
    /// Task E05/F02/US01/T01 (market-assessment) added the four trailing optional parameters —
    /// <paramref name="supplier"/>/<paramref name="currency"/>/<paramref name="geography"/>/
    /// <paramref name="purchaseDate"/> — the caller-supplied source of <see cref="Quote"/>'s own
    /// Quote-level benchmark-matching fields (see that entity's own doc comment for why they are
    /// explicit upload input rather than inferred). All default to <see langword="null"/> so every
    /// existing call site (every test written before this task) keeps compiling unchanged; a quote
    /// uploaded without them is simply not matchable yet — an honest, expected state, not a
    /// validation error, since spec §11.1's own "Identify supplier" workflow step has no dedicated
    /// task/UI yet for a caller to necessarily have this in hand at upload time.
    /// </summary>
    public async Task<Result<QuoteUploadResult>> UploadAsync(
        TenantId tenantId,
        string fileName,
        string? mimeType,
        Stream content,
        CancellationToken cancellationToken = default,
        string? supplier = null,
        string? currency = null,
        string? geography = null,
        DateOnly? purchaseDate = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Result<QuoteUploadResult>.Failure("A file name is required.");
        }

        // Buffered once: the checksum needs the full byte range, and the storage write needs a
        // seekable, replayable stream regardless of what kind of stream the caller handed in (an
        // ASP.NET Core request body stream is not guaranteed seekable) — same reasoning as
        // DocumentUploadService.UploadAsync.
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (buffer.Length == 0)
        {
            return Result<QuoteUploadResult>.Failure("The uploaded file is empty.");
        }

        buffer.Position = 0;
        var checksum = Convert.ToHexString(SHA256.HashData(buffer.ToArray()));

        var quoteId = EntityId.New();
        var now = clock.UtcNow;
        var effectiveMimeType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType;

        using var tenantScope = tenantContext.BeginScope(tenantId);

        buffer.Position = 0;
        // DocumentStoragePath is deliberately generic over "an uploaded document's id" (its own
        // doc comment: "no implementation constructs a path by hand"), not specific to
        // Raffa.Documents.Contracts.Domain.Document — reused here rather than duplicated.
        var storagePath = await storage
            .SaveAsync(tenantId, quoteId, InitialVersionNumber, fileName, buffer, cancellationToken)
            .ConfigureAwait(false);

        var quote = new Quote
        {
            Id = quoteId,
            TenantId = tenantId,
            FileName = fileName,
            MimeType = effectiveMimeType,
            StoragePath = storagePath,
            Checksum = checksum,
            ProcessingStatus = QuoteProcessingStatus.Uploaded,
            // Task E05/F02/US01/T01 (market-assessment): see Quote's own doc comment for why these
            // are explicit, optional caller input rather than inferred, and why PurchaseDate falls
            // back to this same upload's own "now" rather than staying null.
            Supplier = supplier,
            Currency = currency,
            Geography = geography,
            PurchaseDate = purchaseDate ?? DateOnly.FromDateTime(now.UtcDateTime),
            CreatedAt = now,
        };

        // AC-1 "...and creates an extraction job" — the queued row
        // Raffa.Api.QuoteExtractionPipeline advances to completion synchronously after this
        // upload returns (same "queue the row, a later step in the same request drives it"
        // shape DocumentUploadService/DocumentProcessingPipeline already established).
        var extractionJob = new QuoteExtractionJob
        {
            TenantId = tenantId,
            QuoteId = quoteId,
            Status = QuoteExtractionJobStatus.Queued,
            QueuedAt = now,
        };

        dbContext.Quotes.Add(quote);
        dbContext.QuoteExtractionJobs.Add(extractionJob);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                UnattributedActor,
                "quote.uploaded",
                "quote",
                quoteId.Value.ToString(),
                now),
            cancellationToken).ConfigureAwait(false);

        return Result<QuoteUploadResult>.Success(new QuoteUploadResult(
            quote.Id,
            quote.FileName,
            quote.MimeType,
            quote.ProcessingStatus,
            quote.CreatedAt,
            quote.Supplier,
            quote.Currency,
            quote.Geography,
            quote.PurchaseDate));
    }
}
