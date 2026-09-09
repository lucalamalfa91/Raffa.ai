using System.Text.RegularExpressions;
using Contigo.Documents.Contracts.Domain;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Contigo.Documents.Contracts.Application;

/// <summary>
/// One contract field's latest extraction evidence, shaped for the review screen's evidence pane
/// (<c>GET /api/contracts/{id}/evidence</c>; product spec §7.3 "every extracted fact carries
/// source span + confidence"; ADR-020 screen 6 "evidence page with highlighted passage").
/// </summary>
/// <param name="FieldName">The <see cref="ExtractionEvidence.FieldName"/> — the same key
/// <c>ContractCorrectionService</c> accepts (<c>currency</c>, <c>annualSpend</c>, <c>supplier</c>,
/// <c>type</c>, ...).</param>
/// <param name="Value">What the model proposed, verbatim — not the contract's current (possibly
/// corrected) value.</param>
/// <param name="Passage">The sentence(s) of the source page around <paramref name="SourceSpan"/>,
/// when the page's indexed text still contains it; <see langword="null"/> when there is no page
/// text to quote (a classification verdict, a document whose chunks were removed) — the span
/// alone is then the only quote the pane can show, never an invented context.</param>
/// <param name="HighlightStart">Zero-based offset of the span inside <paramref name="Passage"/>.</param>
/// <param name="HighlightLength">Length of the span inside <paramref name="Passage"/>.</param>
/// <param name="ModelId">The extraction job's model id (brief §8 traceability), when recorded.</param>
public sealed record ContractFieldEvidence(
    string FieldName,
    string? Value,
    double? Confidence,
    int? SourcePage,
    string? SourceSpan,
    EntityId? SourceDocumentId,
    string? SourceFileName,
    string? Passage,
    int? HighlightStart,
    int? HighlightLength,
    string? ModelId,
    DateTimeOffset ExtractedAt);

/// <summary>
/// Tenant-scoped read of a contract's per-field extraction evidence — the trail
/// <see cref="ExtractionEvidence"/> has carried since task E02/F01/US02/T01 but that no endpoint
/// exposed until now (the review screen's own header comment named it as "a genuine, pre-existing
/// backend gap"). Latest row per field wins, matching <see cref="DocumentQueryService"/>'s own
/// "Review N fields" count: a field re-extracted by a reprocess is reported once, with its newest
/// proposal.
///
/// <para>
/// <b>The highlighted passage comes from the indexed page text.</b> Every page the pipeline parses
/// is stored verbatim as an <see cref="Embedding"/> chunk (<c>SourceType = "Document"</c>, one chunk
/// per page — <c>DocumentProcessingPipeline.IndexForRetrievalAsync</c>), so the sentence around a
/// fact's span can be quoted without re-reading the blob or re-running the parse. The span is
/// searched whitespace-insensitively (the model may quote a line break as a space); when it cannot
/// be located the passage is omitted rather than approximated.
/// </para>
///
/// <para>
/// Same belt-and-suspenders tenant scoping as every other read in this module (ADR-009): the
/// explicit <c>tenant_id</c> predicate plus the ambient RLS scope are two independent reasons a
/// cross-tenant contract id reads back as "not found".
/// </para>
/// </summary>
public sealed class ContractEvidenceQueryService(DocumentsContractsDbContext dbContext, ITenantContext tenantContext)
{
    /// <summary>Same discriminator <c>DocumentProcessingPipeline</c> indexes page chunks under.</summary>
    private const string DocumentSourceType = "Document";

    /// <summary>How far, in characters, the quoted passage may extend on either side of the span
    /// before it is cut at the nearest sentence boundary — enough for the clause the span sits in,
    /// short enough to read as a citation rather than a page dump.</summary>
    private const int PassageRadius = 220;

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Latest evidence per field for <paramref name="contractId"/>, alphabetically by field name;
    /// an empty list for a contract that exists but has no evidence yet; <see langword="null"/>
    /// when no such contract exists for this tenant (the endpoint turns that into a 404).
    /// </summary>
    public async Task<IReadOnlyList<ContractFieldEvidence>?> GetLatestAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var contractExists = await dbContext.Contracts
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId && c.Id == contractId, cancellationToken)
            .ConfigureAwait(false);
        if (!contractExists)
        {
            return null;
        }

        var rows = await dbContext.ExtractionEvidences
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.ContractId == contractId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var latest = rows
            .GroupBy(e => e.FieldName, StringComparer.OrdinalIgnoreCase)
            .Select(byField => byField.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id.Value).First())
            .OrderBy(e => e.FieldName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (latest.Count == 0)
        {
            return [];
        }

        var documentIds = latest
            .Where(e => e.SourceDocumentId is not null)
            .Select(e => e.SourceDocumentId!.Value)
            .Distinct()
            .ToList();

        var fileNamesByDocument = documentIds.Count == 0
            ? []
            : await dbContext.Documents
                .AsNoTracking()
                .Where(d => d.TenantId == tenantId && documentIds.Contains(d.Id))
                .Select(d => new { d.Id, d.FileName })
                .ToDictionaryAsync(d => d.Id, d => d.FileName, cancellationToken)
                .ConfigureAwait(false);

        var pageTexts = documentIds.Count == 0
            ? []
            : (await dbContext.Embeddings
                .AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.SourceType == DocumentSourceType && documentIds.Contains(e.SourceId))
                .Select(e => new { e.SourceId, e.ChunkIndex, e.Page, e.ChunkText })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
                .GroupBy(e => (e.SourceId, Page: e.Page ?? e.ChunkIndex + 1))
                .ToDictionary(g => g.Key, g => g.First().ChunkText);

        var jobIds = latest
            .Where(e => e.ExtractionJobId is not null)
            .Select(e => e.ExtractionJobId!.Value)
            .Distinct()
            .ToList();

        var modelsByJob = jobIds.Count == 0
            ? []
            : await dbContext.ExtractionJobs
                .AsNoTracking()
                .Where(j => j.TenantId == tenantId && jobIds.Contains(j.Id))
                .Select(j => new { j.Id, j.ModelId })
                .ToDictionaryAsync(j => j.Id, j => j.ModelId, cancellationToken)
                .ConfigureAwait(false);

        return latest
            .Select(evidence =>
            {
                var passage = LocatePassage(evidence, pageTexts);
                return new ContractFieldEvidence(
                    evidence.FieldName,
                    evidence.Value,
                    evidence.Confidence,
                    evidence.SourcePage,
                    evidence.SourceSpan,
                    evidence.SourceDocumentId,
                    evidence.SourceDocumentId is { } documentId ? fileNamesByDocument.GetValueOrDefault(documentId) : null,
                    passage?.Text,
                    passage?.HighlightStart,
                    passage?.HighlightLength,
                    evidence.ExtractionJobId is { } jobId ? modelsByJob.GetValueOrDefault(jobId) : null,
                    evidence.CreatedAt);
            })
            .ToList();
    }

    private static (string Text, int HighlightStart, int HighlightLength)? LocatePassage(
        ExtractionEvidence evidence, IReadOnlyDictionary<(EntityId, int), string> pageTexts)
    {
        if (evidence.SourceDocumentId is not { } documentId
            || evidence.SourcePage is not { } page
            || string.IsNullOrWhiteSpace(evidence.SourceSpan)
            || !pageTexts.TryGetValue((documentId, page), out var pageText))
        {
            return null;
        }

        var text = WhitespaceRegex.Replace(pageText, " ").Trim();
        var span = WhitespaceRegex.Replace(evidence.SourceSpan, " ").Trim();
        var index = text.IndexOf(span, StringComparison.OrdinalIgnoreCase);
        if (index < 0 || span.Length == 0)
        {
            return null;
        }

        var windowStart = Math.Max(0, index - PassageRadius);
        var sentenceStart = index > 0 ? text.LastIndexOf(". ", index - 1, index - windowStart, StringComparison.Ordinal) : -1;
        var start = sentenceStart >= windowStart ? sentenceStart + 2 : windowStart;

        var spanEnd = index + span.Length;
        var windowEnd = Math.Min(text.Length, spanEnd + PassageRadius);
        var sentenceEnd = text.IndexOf('.', spanEnd);
        var end = sentenceEnd >= 0 && sentenceEnd < windowEnd ? sentenceEnd + 1 : windowEnd;

        return (text[start..end], index - start, span.Length);
    }
}
