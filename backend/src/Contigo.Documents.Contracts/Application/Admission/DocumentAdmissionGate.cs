using System.Security.Cryptography;
using System.Text.Json;
using Contigo.AiGateway;
using Contigo.AiGateway.Contracts;
using Contigo.Documents.Contracts.Application.Extraction;
using Contigo.Documents.Contracts.Domain;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;

namespace Contigo.Documents.Contracts.Application.Admission;

/// <summary>
/// Task E13/F04/US01/T01 (documents-admission): "only contracts get in" (ADR-024 "gate before
/// persistence"; <c>inputs/requirements.md</c> R-DOC-03; HITL decisions D3/D7). Runs <em>after</em>
/// the format check (<see cref="DocumentFormatSniffer"/>) and <em>before</em> any blob write,
/// <c>document</c>/<c>embedding</c> row or extraction job: parse in memory
/// (<see cref="HybridDocumentParsingService"/> — native text for PDF/DOCX/XLSX, the <c>ocr</c>
/// gateway role for images and scanned PDFs, ADR-017), require enough readable text, then ask
/// the <c>classify</c> role (ADR-004) and admit only a contract-related type reported at or above
/// <see cref="DocumentAdmissionOptions.AdmissionThreshold"/>.
///
/// <para>
/// <b>One tenant scope, opened here.</b> <see cref="EvaluateAsync"/> opens the tenant's own
/// <see cref="ITenantContext"/> scope before the first gateway call: <c>LoggingAiGateway</c> fails
/// closed when an AI call has no ambient tenant to attribute it to (ADR-011), and the rejection
/// audit write needs the same scope for RLS (ADR-009).
/// </para>
///
/// <para>
/// <b>Rejections leave one trace and nothing else.</b> A rejected upload writes exactly one audit
/// row, <see cref="RejectedAuditAction"/>, whose <c>ResourceId</c> is the SHA-256 of the bytes and
/// whose <c>Detail</c> names the detected type, confidence, reason, byte length and MIME type —
/// never the file name, never any text (ADR-011 "audit without content"; R-DOC-03 "file-name
/// hash, detected type, confidence, reason — never content"). It is written inside the tenant's
/// own RLS scope (ADR-009), like <c>DocumentUploadService</c>'s <c>document.uploaded</c>.
/// </para>
///
/// <para>
/// <b>"Could not read" is not "not a contract".</b> When the parse/OCR or the classify call itself
/// fails (page budget exceeded, gateway error), the decision is <see cref="AdmissionOutcome.Failed"/>
/// with the underlying error: the caller reports it as a processing failure, nothing is persisted,
/// and no <c>document.rejected</c> row is written — the document was never judged.
/// </para>
///
/// <para>
/// <b>Classified once.</b> An admitted decision carries the parsed pages and the
/// <see cref="DocumentClassification"/> so <see cref="DocumentProcessingPipeline"/> continues from
/// them instead of re-parsing and re-classifying (ADR-017 page budget, ADR-004 cost posture).
/// </para>
/// </summary>
public sealed class DocumentAdmissionGate(
    HybridDocumentParsingService parsingService,
    IAiGateway aiGateway,
    DocumentAdmissionOptions options,
    ITenantContext tenantContext,
    IAuditWriter auditWriter,
    IClock clock)
{
    /// <summary>Audit action for a rejected upload (R-DOC-03).</summary>
    public const string RejectedAuditAction = "document.rejected";

    /// <summary>Audit <c>ResourceType</c> for a rejected upload — the resource never became a
    /// <c>document</c> row, so it is named for what it was: an upload attempt.</summary>
    public const string RejectedAuditResourceType = "document-upload";

    /// <summary>The backend-owned fragment of the 422 body (OpenAPI <c>uploadDocument</c> 422 <c>hint</c>);
    /// the web's own R-DOC-04 copy quotes it.</summary>
    public const string Hint = "Contigo only keeps contracts, order forms, quotes and the documents around them.";

    private static readonly JsonSerializerOptions AuditDetailJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Decides whether the upload may be persisted. <paramref name="mimeType"/> must be the sniffed,
    /// canonical MIME type (<see cref="DocumentFormatDetection.MimeType"/>) — it selects the native
    /// vs. OCR parse path. <paramref name="actor"/> is recorded on the audit row of a rejection.
    /// </summary>
    public async Task<AdmissionDecision> EvaluateAsync(
        TenantId tenantId,
        string actor,
        string fileName,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        // One tenant scope around the whole evaluation, opened before the first gateway call:
        // LoggingAiGateway refuses to log — and therefore to run — an `ocr` or `classify` call
        // with no ambient tenant ("every AI call must be attributable to a tenant", ADR-011), and
        // the rejection audit write below needs the same scope for RLS (ADR-009). Same entry-point
        // posture DocumentProcessingPipeline and AuditQueryService take: open this call's own
        // scope rather than trusting one is already active.
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var parseResult = await parsingService
            .ParseAsync(fileName, mimeType, content, cancellationToken)
            .ConfigureAwait(false);
        if (parseResult.IsFailure)
        {
            return AdmissionDecision.Fail(parseResult.Error);
        }

        var pages = parseResult.Value;
        var readableChars = CountReadableChars(pages);
        if (readableChars < options.MinReadableChars)
        {
            var noText = AdmissionDecision.RejectNoReadableText(pages, readableChars);
            await AuditRejectionAsync(tenantId, actor, mimeType, content, noText, cancellationToken).ConfigureAwait(false);
            return noText;
        }

        var classifyResult = await aiGateway
            .ClassifyAsync(new AiClassificationRequest(BuildClassificationText(pages)), cancellationToken)
            .ConfigureAwait(false);
        if (classifyResult.IsFailure)
        {
            return AdmissionDecision.Fail(classifyResult.Error);
        }

        var detectedType = ContractDocumentTypeMap.FromAi(classifyResult.Value.DocumentType);
        var confidence = classifyResult.Value.Confidence;
        if (detectedType == ContractDocumentType.Other || confidence < options.AdmissionThreshold)
        {
            var notAContract = AdmissionDecision.RejectNotAContract(pages, readableChars, detectedType, confidence);
            await AuditRejectionAsync(tenantId, actor, mimeType, content, notAContract, cancellationToken).ConfigureAwait(false);
            return notAContract;
        }

        return AdmissionDecision.Admit(
            pages,
            readableChars,
            new DocumentClassification(detectedType, confidence, classifyResult.Value.Metadata));
    }

    /// <summary>Non-whitespace characters across every page — a scanned blank page or an OCR
    /// placeholder line does not count as "readable contract text" (R-DOC-03 AC-2).</summary>
    public static int CountReadableChars(IReadOnlyList<DocumentPageText> pages)
    {
        var count = 0;
        foreach (var page in pages)
        {
            if (string.IsNullOrEmpty(page.Text))
            {
                continue;
            }

            foreach (var character in page.Text)
            {
                if (!char.IsWhiteSpace(character))
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Same representative text <see cref="DocumentProcessingPipeline"/> hands the classify
    /// role: every page, in order, separated by a blank line.</summary>
    private static string BuildClassificationText(IReadOnlyList<DocumentPageText> pages) =>
        string.Join("\n\n", pages.Select(p => p.Text));

    private async Task AuditRejectionAsync(
        TenantId tenantId,
        string actor,
        string mimeType,
        ReadOnlyMemory<byte> content,
        AdmissionDecision decision,
        CancellationToken cancellationToken)
    {
        var contentHash = Convert.ToHexString(SHA256.HashData(content.Span));
        var detail = JsonSerializer.Serialize(
            new RejectionAuditDetail(
            decision.DetectedType.ToString(),
            Math.Round(decision.Confidence, 4),
            decision.Reason!.Value.ToApiValue(),
            decision.ReadableChars,
            content.Length,
            mimeType),
            AuditDetailJson);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                RejectedAuditAction,
                RejectedAuditResourceType,
                contentHash,
                clock.UtcNow,
                detail),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The audit <c>Detail</c> payload: verdict metadata only, never the file name or any
    /// text (ADR-011). Serialized with the JSON property names the audit trail exposes.</summary>
    private sealed record RejectionAuditDetail(
        string DetectedType,
        double Confidence,
        string Reason,
        int ReadableChars,
        long Bytes,
        string MimeType);
}
