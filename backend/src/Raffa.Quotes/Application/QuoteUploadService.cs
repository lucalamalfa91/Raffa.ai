using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
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
        string actor,
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

        // Task F6-T04 (dedup by checksum): the same bytes, with the same caller-supplied header
        // fields, that this tenant already extracted successfully are answered with the existing
        // quote instead of storing a second blob, queuing a second job and paying for a second
        // model call. Deliberately narrow so it can never make a flow worse:
        //   * only a quote that reached Completed/NeedsReview is reused -- a Failed, Uploaded or
        //     Processing one (including a row orphaned by an older crash) never blocks a retry;
        //   * the header fields must match, because they decide whether the benchmark can match
        //     the lines at all: re-uploading the same file *with* the supplier filled in is a
        //     different request, not a duplicate;
        //   * purchaseDate is compared only when the caller sent one (otherwise it defaults to the
        //     upload day, which is never a reason to treat two uploads as different).
        var existing = await FindReusableQuoteAsync(
            tenantId, checksum, supplier, currency, geography, purchaseDate, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return await BuildDuplicateResultAsync(tenantId, existing, actor, now, cancellationToken)
                .ConfigureAwait(false);
        }

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
                actor,
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

    private async Task<Quote?> FindReusableQuoteAsync(
        TenantId tenantId,
        string checksum,
        string? supplier,
        string? currency,
        string? geography,
        DateOnly? purchaseDate,
        CancellationToken cancellationToken)
    {
        // AsNoTracking: the caller only reads this row. Filtering on TenantId explicitly is
        // belt-and-braces beside RLS (ADR-009), the convention every query in this module follows.
        var candidates = dbContext.Quotes.AsNoTracking()
            .Where(q => q.TenantId == tenantId
                && q.Checksum == checksum
                && (q.ProcessingStatus == QuoteProcessingStatus.Completed
                    || q.ProcessingStatus == QuoteProcessingStatus.NeedsReview)
                && q.Supplier == supplier
                && q.Currency == currency
                && q.Geography == geography);

        if (purchaseDate is not null)
        {
            candidates = candidates.Where(q => q.PurchaseDate == purchaseDate);
        }

        return await candidates
            .OrderByDescending(q => q.CreatedAt)
            .ThenBy(q => q.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<QuoteUploadResult>> BuildDuplicateResultAsync(
        TenantId tenantId,
        Quote existing,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var lines = await dbContext.QuoteLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.QuoteId == existing.Id)
            .Select(l => new { l.NormalizedAnnualUnitPrice, l.MatchStatus })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Trace the dedup without content: same resource, a distinct action, so "why did this
        // upload not create a quote" is answerable from the audit trail.
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                "quote.upload.deduplicated",
                "quote",
                existing.Id.Value.ToString(),
                now),
            cancellationToken).ConfigureAwait(false);

        return Result<QuoteUploadResult>.Success(new QuoteUploadResult(
            existing.Id,
            existing.FileName,
            existing.MimeType,
            existing.ProcessingStatus,
            existing.CreatedAt,
            existing.Supplier,
            existing.Currency,
            existing.Geography,
            existing.PurchaseDate,
            IsDuplicate: true,
            LineItemCount: lines.Count,
            NormalizedLineItemCount: lines.Count(l => l.NormalizedAnnualUnitPrice is not null),
            UnmatchedSkuCount: lines.Count(l => l.MatchStatus == SkuMatchStatus.Unmatched)));
    }
}
