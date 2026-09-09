using System.Globalization;
using Contigo.Api.Infrastructure;
using Contigo.Documents.Contracts.Application;
using Contigo.Documents.Contracts.Application.Admission;
using Contigo.Documents.Contracts.Application.Extraction;
using Contigo.Documents.Contracts.Application.Preview;
using Contigo.Documents.Contracts.Domain;
using Contigo.SharedKernel;
using Microsoft.AspNetCore.Http.Features;

namespace Contigo.Api;

/// <summary>
/// Task E13/F04/US01/T01 (documents-admission): the two document endpoints, moved out of
/// <c>Program.cs</c> (ADR-002 "host is a thin composition root") and — for the upload — reordered
/// so that nothing is persisted before the document has been judged (ADR-024 "gate before
/// persistence"; <c>inputs/requirements.md</c> R-DOC-01/02/03).
///
/// <para>
/// <b><c>POST /api/documents</c>, in order:</b>
/// <list type="number">
/// <item>tenant header, multipart shape, non-empty <c>file</c> field → 400 (unchanged from task
/// E01/F06/US01/T01);</item>
/// <item>size ≤ <c>Documents:MaxFileBytes</c> → otherwise 413, before any parse or model call
/// (R-DOC-01). Kestrel's own per-request body cap is lifted to the same limit for this endpoint
/// so a 40 MB contract is ours to accept and a 60 MB one is ours to refuse, with our message —
/// never Kestrel's opaque 413;</item>
/// <item>format by extension <b>and</b> magic bytes (<see cref="DocumentFormatSniffer"/>) →
/// otherwise 415 with R-DOC-02's own sentence, still before any AI-gateway call (AC-1: a
/// <c>.zip</c> renamed <c>.pdf</c> never reaches the gateway). The sniffed canonical MIME type,
/// not the browser's <c>Content-Type</c>, is what gets stored and what selects the native-vs-OCR
/// parse path;</item>
/// <item><see cref="DocumentAdmissionGate"/>: parse/OCR in memory, readable-text floor,
/// <c>classify</c> role, threshold → 422 <c>{ rejected, detectedType, confidence, reason, hint }</c>
/// with nothing persisted and one <c>document.rejected</c> audit row (R-DOC-03); a gate that
/// could not reach a verdict at all (parse or classify failure) is a 400 carrying the error,
/// not a rejection;</item>
/// <item>only when admitted: <see cref="DocumentUploadService.UploadAsync"/> (blob + rows, as
/// before) then <see cref="DocumentProcessingPipeline"/>'s pages-and-classification overload —
/// the parse and the classify verdict the gate already produced are reused, so the model is
/// called once per upload. As before, a pipeline failure after the upload is durable is reported
/// in the 201 body (<c>processingStatus</c>/<c>contractId</c> fall back to the pre-processing
/// values), never turned into an HTTP error.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>The V2 surface</b> (task E13/F04/US01/T02, documents-v2-api): the same file also maps the
/// four endpoints the Documents V2 screen and Ask's citation cards need —
/// <c>GET /api/documents</c> (the server-side list that replaced the V1 screen's
/// <c>sessionStorage</c>, R-DOC-06/09), <c>GET /api/documents/{id}/preview</c> (the stored
/// first-page PNG, streamed under the caller's tenant scope, never a blob URL, R-DOC-08),
/// <c>POST /api/documents/{id}/reprocess</c> and <c>DELETE /api/documents/{id}</c> (both Admin,
/// R-DOC-07/R-DOC-10; every other role gets 403 — see <see cref="WorkspaceRoleResolver"/> for how
/// the role is established while ADR-010 is not yet wired).
/// </para>
///
/// <para>
/// <b>Interim identity posture</b> (ADR-022, OQ-askv2-005): the tenant comes from
/// <c>X-Tenant-Id</c> and the audit actor of a rejection from <c>X-User-Id</c> when the web sent
/// one (it does, for every call — see <c>web/src/api/client.ts</c>), else the same
/// <c>"unattributed"</c> literal <c>DocumentUploadService</c> records on <c>document.uploaded</c>.
/// Neither header is validated against an identity provider; both are replaced by token claims
/// the task that lands the API JWT on this host (ADR-010).
/// </para>
/// </summary>
public static class DocumentsEndpointExtensions
{
    private const string TenantHeaderName = "X-Tenant-Id";
    private const string UserIdHeaderName = "X-User-Id";
    private const string UnattributedActor = "unattributed";
    private const string FileFieldName = "file";

    /// <summary>Allowance for multipart framing (boundaries, part headers) on top of
    /// <see cref="DocumentAdmissionOptions.MaxFileBytes"/> when sizing the request-body cap; the
    /// file itself is still measured exactly.</summary>
    private const long MultipartFramingAllowanceBytes = 1024 * 1024;

    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/documents", UploadDocumentAsync);
        endpoints.MapGet("/api/documents", ListDocumentsAsync);
        endpoints.MapGet("/api/documents/{id}", GetDocumentAsync);
        endpoints.MapGet("/api/documents/{id}/preview", GetDocumentPreviewAsync);
        endpoints.MapPost("/api/documents/{id}/reprocess", ReprocessDocumentAsync);
        endpoints.MapDelete("/api/documents/{id}", DeleteDocumentAsync);
        return endpoints;
    }

    private static async Task<IResult> UploadDocumentAsync(
        HttpRequest request,
        DocumentAdmissionOptions admissionOptions,
        DocumentAdmissionGate admissionGate,
        DocumentUploadService uploadService,
        DocumentProcessingPipeline processingPipeline,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenant(request, out var tenantId))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        if (!request.HasFormContentType)
        {
            return Results.BadRequest("Expected multipart/form-data with a 'file' field.");
        }

        var bodyLimit = admissionOptions.MaxFileBytes + MultipartFramingAllowanceBytes;
        if (request.ContentLength is { } declaredLength && declaredLength > bodyLimit)
        {
            // The declared body is already over the limit: refuse before reading a single byte.
            return TooLarge(admissionOptions);
        }

        var bodySizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = bodyLimit;
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge(admissionOptions);
        }
        catch (InvalidDataException exception)
        {
            return Results.BadRequest($"The multipart body could not be read: {exception.Message}");
        }

        var file = form.Files[FileFieldName];
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest("A non-empty 'file' form field is required.");
        }

        if (file.Length > admissionOptions.MaxFileBytes)
        {
            return TooLarge(admissionOptions);
        }

        byte[] fileBytes;
        await using (var uploadStream = file.OpenReadStream())
        await using (var buffer = new MemoryStream())
        {
            await uploadStream.CopyToAsync(buffer, cancellationToken);
            fileBytes = buffer.ToArray();
        }

        if (fileBytes.LongLength > admissionOptions.MaxFileBytes)
        {
            return TooLarge(admissionOptions);
        }

        if (!DocumentFormatSniffer.TryDetect(file.FileName, fileBytes, out var format))
        {
            return Results.Json(
                DocumentFormatSniffer.RejectionMessage,
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        var decision = await admissionGate.EvaluateAsync(
            tenantId, ResolveActor(request), file.FileName, format.MimeType, fileBytes, cancellationToken);

        switch (decision.Outcome)
        {
            case AdmissionOutcome.Failed:
                // Two different failures wear the same outcome: a document this pipeline genuinely
                // cannot read (the caller's problem -> 400) and an AI provider that could not be
                // reached at all (ours -> 503, and worth retrying). Neither persists anything.
                return decision.Error?.StartsWith(
                    DocumentAdmissionGate.GatewayUnavailablePrefix, StringComparison.Ordinal) == true
                    ? Results.Json(decision.Error, statusCode: StatusCodes.Status503ServiceUnavailable)
                    : Results.BadRequest(decision.Error);
            case AdmissionOutcome.Rejected:
                return Results.Json(
                    new
                    {
                        rejected = true,
                        detectedType = decision.DetectedType.ToString(),
                        confidence = decision.Confidence,
                        reason = decision.Reason!.Value.ToApiValue(),
                        hint = DocumentAdmissionGate.Hint,
                    },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        using var storageContent = new MemoryStream(fileBytes);
        var result = await uploadService.UploadAsync(
            tenantId, file.FileName, format.MimeType, storageContent, cancellationToken);
        if (result.IsFailure)
        {
            return Results.BadRequest(result.Error);
        }

        var uploaded = result.Value;

        var processingResult = await processingPipeline.ProcessAsync(
            tenantId,
            uploaded.DocumentId,
            decision.Pages,
            decision.Classification!,
            fileBytes,
            uploaded.FileName,
            format.MimeType,
            cancellationToken);

        var processingStatus = processingResult.IsSuccess
            ? processingResult.Value.ProcessingStatus
            : uploaded.ProcessingStatus;
        var contractId = processingResult.IsSuccess ? processingResult.Value.ContractId.Value : (Guid?)null;

        return Results.Created($"/api/documents/{uploaded.DocumentId}", new
        {
            id = uploaded.DocumentId.Value,
            contractId,
            fileName = uploaded.FileName,
            mimeType = uploaded.MimeType,
            processingStatus = processingStatus.ToString(),
            createdAt = uploaded.CreatedAt,
        });
    }

    /// <summary>
    /// Task E01/F06/US01/T02 (us-01-document-upload, AC-3): reads back the metadata/status the
    /// upload persists, scoped to the caller's tenant. <c>documentType</c> is the widened
    /// <c>ContractDocumentType</c> (task E13/F04/US01/T01: Quote / Invoice / PriceList / Nda / Dpa
    /// are now their own values; existing names unchanged).
    /// </summary>
    private static async Task<IResult> GetDocumentAsync(
        string id,
        HttpRequest request,
        DocumentQueryService queryService,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenant(request, out var tenantId))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        if (!Guid.TryParse(id, out var documentGuid))
        {
            return Results.BadRequest("The document id in the route must be a GUID.");
        }

        var metadata = await queryService
            .GetByIdAsync(tenantId, new EntityId(documentGuid), cancellationToken)
            .ConfigureAwait(false);

        if (metadata is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            id = metadata.DocumentId.Value,
            contractId = metadata.ContractId?.Value,
            fileName = metadata.FileName,
            mimeType = metadata.MimeType,
            documentType = metadata.DocumentType.ToString(),
            processingStatus = metadata.ProcessingStatus.ToString(),
            createdAt = metadata.CreatedAt,
        });
    }

    /// <summary>
    /// Task E13/F04/US01/T02 (R-DOC-06): one page of the tenant's documents, newest first, with
    /// the real processing stage (R-DOC-09), the parsed page count, the resolved supplier name and
    /// the weak-fact count the row's "Review N fields" action shows. Optional <c>status</c>,
    /// <c>page</c> and <c>pageSize</c> query parameters, same conventions as
    /// <c>GET /api/contracts</c>.
    /// </summary>
    private static async Task<IResult> ListDocumentsAsync(
        HttpRequest request,
        DocumentQueryService queryService,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenant(request, out var tenantId))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        DocumentProcessingStatus? status = null;
        if (request.Query.TryGetValue("status", out var statusValues) && !string.IsNullOrWhiteSpace(statusValues))
        {
            if (!Enum.TryParse<DocumentProcessingStatus>(statusValues.ToString(), ignoreCase: true, out var parsed))
            {
                return Results.BadRequest(
                    $"'status' must be one of {string.Join(", ", Enum.GetNames<DocumentProcessingStatus>())}.");
            }

            status = parsed;
        }

        if (!TryReadPositiveInt(request, "page", defaultValue: 1, out var page, out var pageError))
        {
            return Results.BadRequest(pageError);
        }

        if (!TryReadPositiveInt(request, "pageSize", PortfolioPageRequest.DefaultPageSize, out var pageSize, out var sizeError))
        {
            return Results.BadRequest(sizeError);
        }

        if (pageSize > PortfolioPageRequest.MaxPageSize)
        {
            return Results.BadRequest(
                $"'pageSize' must be an integer between 1 and {PortfolioPageRequest.MaxPageSize}.");
        }

        var result = await queryService.ListAsync(tenantId, status, page, pageSize, cancellationToken);

        return Results.Ok(new
        {
            items = result.Items.Select(item => new
            {
                id = item.DocumentId.Value,
                contractId = item.ContractId?.Value,
                supplierName = item.SupplierName,
                fileName = item.FileName,
                documentType = item.DocumentType.ToString(),
                processingStatus = item.ProcessingStatus.ToString(),
                stage = item.Stage,
                pageCount = item.PageCount,
                createdAt = item.CreatedAt,
                weakFactCount = item.WeakFactCount,
            }),
            page = result.Page,
            pageSize = result.PageSize,
            totalCount = result.TotalCount,
        });
    }

    /// <summary>
    /// Task E13/F04/US01/T02 (R-DOC-08): streams the stored first-page PNG. Tenant-scoped through
    /// the document row itself and re-checked against the tenant's object-storage prefix on load
    /// (ADR-009) — the client never sees, and never supplies, a blob path. 404 covers all three of
    /// "no such document", "not your tenant" and "no preview stored": none of them is a distinction
    /// a caller is entitled to.
    /// </summary>
    private static async Task<IResult> GetDocumentPreviewAsync(
        string id,
        HttpRequest request,
        DocumentPreviewService previewService,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenant(request, out var tenantId))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        if (!Guid.TryParse(id, out var documentGuid))
        {
            return Results.BadRequest("The document id in the route must be a GUID.");
        }

        var png = await previewService.LoadAsync(tenantId, new EntityId(documentGuid), cancellationToken);
        return png is null
            ? Results.NotFound()
            : Results.File(png, DocumentPreviewService.PreviewContentType);
    }

    /// <summary>
    /// Task E13/F04/US01/T02 (R-DOC-07): Admin-only re-run of parse + embedding + staged extraction
    /// over the stored bytes, so documents indexed before the V2 pipeline get readable, page-aware
    /// chunks and back-filled facts. Writes one <c>document.reprocessed</c> audit row.
    /// </summary>
    private static async Task<IResult> ReprocessDocumentAsync(
        string id,
        HttpContext httpContext,
        DocumentReprocessService reprocessService,
        WorkspaceRoleResolver roleResolver,
        CancellationToken cancellationToken)
    {
        var request = httpContext.Request;
        if (!TryResolveTenant(request, out var tenantId))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        if (!Guid.TryParse(id, out var documentGuid))
        {
            return Results.BadRequest("The document id in the route must be a GUID.");
        }

        if (!await roleResolver.IsAdminAsync(httpContext, tenantId, cancellationToken))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await reprocessService.ReprocessAsync(
            tenantId, new EntityId(documentGuid), ResolveActor(request), cancellationToken);
        if (result is null)
        {
            return Results.NotFound();
        }

        if (result.IsFailure)
        {
            return Results.BadRequest(result.Error);
        }

        // The audit row (document.reprocessed) is written by the service itself, inside the tenant
        // scope the RLS-protected audit table requires — see DocumentReprocessService.
        var summary = result.Value;
        return Results.Ok(new
        {
            documentId = summary.DocumentId.Value,
            contractId = summary.ContractId.Value,
            documentType = summary.DocumentType.ToString(),
            processingStatus = summary.ProcessingStatus.ToString(),
            pagesParsed = summary.PagesParsed,
            chunksIndexed = summary.ChunksIndexed,
        });
    }

    /// <summary>
    /// Task E13/F04/US01/T02 (R-DOC-10): Admin-only deletion of the blobs, the preview, the
    /// retrieval chunks and the rows; the contract survives with its document link gone. Writes one
    /// <c>document.deleted</c> audit row before returning 204.
    /// </summary>
    private static async Task<IResult> DeleteDocumentAsync(
        string id,
        HttpContext httpContext,
        DocumentDeleteService deleteService,
        WorkspaceRoleResolver roleResolver,
        CancellationToken cancellationToken)
    {
        var request = httpContext.Request;
        if (!TryResolveTenant(request, out var tenantId))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        if (!Guid.TryParse(id, out var documentGuid))
        {
            return Results.BadRequest("The document id in the route must be a GUID.");
        }

        if (!await roleResolver.IsAdminAsync(httpContext, tenantId, cancellationToken))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await deleteService.DeleteAsync(
            tenantId, new EntityId(documentGuid), ResolveActor(request), cancellationToken);
        if (result is null)
        {
            return Results.NotFound();
        }

        if (result.IsFailure)
        {
            return Results.BadRequest(result.Error);
        }

        // The audit row (document.deleted) is written by the service itself, inside the tenant
        // scope the RLS-protected audit table requires — see DocumentDeleteService.
        return Results.NoContent();
    }

    /// <summary>Reads an optional positive-integer query parameter, defaulting when absent.</summary>
    private static bool TryReadPositiveInt(
        HttpRequest request, string name, int defaultValue, out int value, out string? error)
    {
        value = defaultValue;
        error = null;

        if (!request.Query.TryGetValue(name, out var values) || string.IsNullOrWhiteSpace(values))
        {
            return true;
        }

        if (!int.TryParse(values.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 1)
        {
            error = $"'{name}' must be a positive integer.";
            return false;
        }

        return true;
    }

    private static IResult TooLarge(DocumentAdmissionOptions options) =>
        Results.Json(
            $"Contigo accepts files up to {options.MaxFileBytes / (1024 * 1024)} MB. This file is larger.",
            statusCode: StatusCodes.Status413PayloadTooLarge);

    private static bool TryResolveTenant(HttpRequest request, out TenantId tenantId)
    {
        if (request.Headers.TryGetValue(TenantHeaderName, out var values)
            && Guid.TryParse(values.ToString(), out var tenantGuid))
        {
            tenantId = new TenantId(tenantGuid);
            return true;
        }

        tenantId = default;
        return false;
    }

    private static string ResolveActor(HttpRequest request) =>
        request.Headers.TryGetValue(UserIdHeaderName, out var values)
        && !string.IsNullOrWhiteSpace(values.ToString())
            ? values.ToString().Trim()
            : UnattributedActor;
}
