using System.Globalization;
using Raffa.Api.Infrastructure;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Application.Preview;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Http.Features;

namespace Raffa.Api;

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
/// <item><see cref="DocumentUploadService.UploadAsync"/> — blob, rows and the queued
/// <c>ExtractionJob</c>, with the pointer published before the commit — and then <b>201</b>.
/// That is the end of the request.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What no longer happens in the request</b> (task E16/F02/US03/T01, wave w15, ADR-027 §D1,
/// closing NW-27). <see cref="DocumentAdmissionGate"/>'s content half (parse/OCR, readable-text
/// floor, <c>classify</c> role and threshold) and the whole of
/// <see cref="DocumentProcessingPipeline"/> moved behind the Worker. Two visible consequences:
/// <list type="bullet">
/// <item>a content refusal is <b>no longer a 422</b>. The document is persisted and the Worker
/// drives it to <see cref="DocumentProcessingStatus.Rejected"/> carrying its reason
/// <see cref="AdmissionRejectionReason">code</see>; the list endpoint returns that code and the
/// screen writes the sentence (ADR-020 w15 §6). The <c>document.rejected</c> audit row still
/// happens, on the Worker's side of the line;</item>
/// <item>the 201 carries <c>contractId: null</c> and <c>processingStatus: "Uploaded"</c> —
/// classification has not run, so there is nothing yet to link or to claim. Callers poll
/// <c>GET /api/documents</c>, whose <c>counts</c> and per-item stage are the readiness contract
/// (ADR-027 §C9).</item>
/// </list>
/// The request keeps only what it can decide for itself in milliseconds: size, format, tenancy.
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
/// <b>Identity posture</b> (ADR-010; ADR-011 w16 clauses 15–17; ADR-022 w15 footer): every
/// tenant-scoped route here resolves the caller through <c>ICallerContext</c> first — an absent or
/// invalid token is 401 before any handler body runs — then <c>X-Tenant-Id</c> as a
/// membership-verified selector (400/404). The resolved token subject is threaded into every write
/// this file makes (<see cref="DocumentUploadService.UploadAsync"/>'s own <c>actor</c> parameter,
/// same shape <see cref="ValidateDocumentAsync"/>/<see cref="ReprocessDocumentAsync"/>/
/// <see cref="DeleteDocumentAsync"/> already used): no write here can record a placeholder actor.
/// </para>
/// </summary>
public static class DocumentsEndpointExtensions
{
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
        endpoints.MapPost("/api/documents/{id}/prioritise", PrioritiseDocumentAsync);
        endpoints.MapPost("/api/documents/{id}/validate", ValidateDocumentAsync);
        endpoints.MapDelete("/api/documents", DeleteAllDocumentsAsync);
        endpoints.MapDelete("/api/documents/{id}", DeleteDocumentAsync);
        return endpoints;
    }

    /// <summary>`POST /api/documents/{id}/validate` request body: the field names the reviewer
    /// accepted as extracted (corrections are already durable via `PATCH /api/contracts/{id}`).
    /// Optional — `{}` is a valid body when every flagged field was corrected instead.</summary>
    public sealed record DocumentValidationRequest(IReadOnlyList<string>? AcceptedFields);

    /// <summary>
    /// `POST /api/documents/{id}/validate`: the review sign-off (product spec §7.1 "needs review →
    /// completed"; ADR-020 screen 6 "Mark as validated"). Not Admin-only — reviewing is the
    /// Procurement role's own job, and the field corrections this closes out (`PATCH
    /// /api/contracts/{id}`) carry no role gate either. 404 for an unknown or cross-tenant document,
    /// 409 with a named reason when the document is not in a reviewable state (still processing, or
    /// failed), 200 with the resulting status otherwise — idempotent for an already-validated one.
    /// The `document.validated` audit row is written by <see cref="DocumentValidationService"/>
    /// inside the tenant scope the RLS-protected audit table requires.
    /// </summary>
    private static async Task<IResult> ValidateDocumentAsync(
        string id,
        DocumentValidationRequest? body,
        HttpContext httpContext,
        DocumentValidationService validationService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var request = httpContext.Request;
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;

        if (!Guid.TryParse(id, out var documentGuid))
        {
            return Results.BadRequest("The document id in the route must be a GUID.");
        }

        var result = await validationService.ValidateAsync(
            tenantId,
            new EntityId(documentGuid),
            body?.AcceptedFields ?? [],
            caller.Identity!,
            cancellationToken);

        if (result is null)
        {
            return Results.NotFound();
        }

        if (result.IsFailure)
        {
            return Results.Conflict(result.Error);
        }

        var validation = result.Value;
        return Results.Ok(new
        {
            documentId = validation.DocumentId.Value,
            contractId = validation.ContractId?.Value,
            processingStatus = validation.ProcessingStatus.ToString(),
            validatedAt = validation.ValidatedAt,
            acceptedFields = validation.AcceptedFields,
            alreadyValidated = validation.AlreadyValidated,
        });
    }

    private static async Task<IResult> UploadDocumentAsync(
        HttpRequest request,
        DocumentAdmissionOptions admissionOptions,
        DocumentUploadService uploadService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;

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

        // ADR-027 §D1 (task E16/F02/US03/T01, closes NW-27): everything above this line is a
        // property of the REQUEST -- the multipart body, its declared size, the sniffed format --
        // and stays in-request because refusing it needs no model call and costs milliseconds.
        // Everything that needs to READ the document (parse/OCR, then the Foundry classify that
        // decides "is this a contract at all") now belongs to the Worker: it is minutes of model
        // latency against a Container Apps request timeout of about four, which is why fifteen
        // dropped PDFs sat on "Uploading..." until the whole batch timed out.
        //
        // Consequences the callers must know, both deliberate:
        //   * A content refusal is no longer a 422. The row is persisted and reaches
        //     DocumentProcessingStatus.Rejected with its reason CODE (ADR-027 §D6); the list
        //     endpoint carries that code and the screen -- never this API -- writes the sentence
        //     (ADR-020 w15 §6).
        //   * The 201 carries no contractId. Classification has not run, so there is nothing to
        //     link yet; the client polls GET /api/documents for the progression. Answering with a
        //     fabricated or null-but-meaningful id here is the guessing this task removes.
        using var storageContent = new MemoryStream(fileBytes);
        var result = await uploadService.UploadAsync(
            tenantId, file.FileName, format.MimeType, storageContent, caller.Identity!, cancellationToken);
        if (result.IsFailure)
        {
            return Results.BadRequest(result.Error);
        }

        var uploaded = result.Value;

        return Results.Created($"/api/documents/{uploaded.DocumentId}", new
        {
            id = uploaded.DocumentId.Value,
            contractId = (Guid?)null,
            fileName = uploaded.FileName,
            mimeType = uploaded.MimeType,
            processingStatus = uploaded.ProcessingStatus.ToString(),
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
        HungProcessingRecoveryService hungRecovery,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;

        if (!Guid.TryParse(id, out var documentGuid))
        {
            return Results.BadRequest("The document id in the route must be a GUID.");
        }

        var documentId = new EntityId(documentGuid);
        await hungRecovery
            .RecoverDocumentAsync(tenantId, documentId, force: false, cancellationToken)
            .ConfigureAwait(false);

        var metadata = await queryService
            .GetByIdAsync(tenantId, documentId, cancellationToken)
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
            // Task E22/F02/US01/T01 (ADR-029 clauses 6-7): exposed so the viewer can page
            // through the document and surface the budget cap honestly (AC-4/AC-7).
            pageCount = metadata.PageCount,
            isPageCountLimited = metadata.IsPageCountLimited,
        });
    }

    /// <summary>
    /// Task E13/F04/US01/T02 (R-DOC-06): one page of the tenant's documents, newest first, with
    /// the real processing stage (R-DOC-09), the parsed page count, the resolved supplier name and
    /// the weak-fact count the row's "Review N fields" action shows. Optional <c>status</c>,
    /// <c>page</c> and <c>pageSize</c> query parameters, same conventions as
    /// <c>GET /api/contracts</c>. Hung recovery also requeues Failed rows left terminal by the
    /// old 3-minute hang cap, so opening the list is enough to restart them.
    /// </summary>
    private static async Task<IResult> ListDocumentsAsync(
        HttpRequest request,
        DocumentQueryService queryService,
        HungProcessingRecoveryService hungRecovery,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;

        await hungRecovery
            .RecoverHungInTenantAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

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
        var counts = result.Counts ?? DocumentCounts.Empty;

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
                // ADR-027 §D6 (task E16/F02/US03/T01): the content gate's refusal as a CODE
                // (`not_a_contract` | `no_readable_text`), null unless Rejected. The screen writes
                // the sentence (ADR-020 w15 §6); this API never authors user-facing prose.
                rejectionReason = item.RejectionReason?.ToApiValue(),
                errorDetail = item.ErrorDetail,
            }),
            page = result.Page,
            pageSize = result.PageSize,
            totalCount = result.TotalCount,
            // ADR-027 §D7: tenant-wide, unfiltered by `status`, page-independent. Overlapping
            // projections, not a partition -- the client must not add or subtract them.
            counts = new
            {
                all = counts.All,
                needsAttention = counts.NeedsAttention,
                needsReview = counts.NeedsReview,
                processing = counts.Processing,
                rejected = counts.Rejected,
            },
        });
    }

    /// <summary>
    /// Task E13/F04/US01/T02 (R-DOC-08): streams the stored first-page PNG. Tenant-scoped through
    /// the document row itself and re-checked against the tenant's object-storage prefix on load
    /// (ADR-009) — the client never sees, and never supplies, a blob path. 404 covers all three of
    /// "no such document", "not your tenant" and "no preview stored": none of them is a distinction
    /// a caller is entitled to.
    ///
    /// <para>
    /// Task E22/F02/US01/T01 (ADR-029 clause 5): accepts an optional <c>?page=n</c> query
    /// parameter (1-based). Out of range is 404, never a silent page 1 — silently serving the wrong
    /// page to a citation deep-link is how a viewer lies about where a clause came from.
    /// </para>
    /// </summary>
    private static async Task<IResult> GetDocumentPreviewAsync(
        string id,
        HttpRequest request,
        DocumentPreviewService previewService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;

        if (!Guid.TryParse(id, out var documentGuid))
        {
            return Results.BadRequest("The document id in the route must be a GUID.");
        }

        // ADR-029 clause 5: optional ?page=n (1-based). Absent → page 1 (today's behaviour).
        // Out of range is 404 (handled by DocumentPreviewService.LoadAsync), never a clamp.
        var page = 1;
        if (request.Query.TryGetValue("page", out var pageValues) && !string.IsNullOrWhiteSpace(pageValues))
        {
            if (!int.TryParse(pageValues.ToString(), out var parsedPage) || parsedPage < 1)
            {
                return Results.NotFound();
            }

            page = parsedPage;
        }

        var png = await previewService.LoadAsync(tenantId, new EntityId(documentGuid), page, cancellationToken);
        return png is null
            ? Results.NotFound()
            : Results.File(png, PreviewMediaType(png));
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
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var request = httpContext.Request;
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;

        if (!Guid.TryParse(id, out var documentGuid))
        {
            return Results.BadRequest("The document id in the route must be a GUID.");
        }

        if (!await roleResolver.IsAdminAsync(httpContext, tenantId, cancellationToken))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await reprocessService.ReprocessAsync(
            tenantId, new EntityId(documentGuid), caller.Identity!, cancellationToken);
        if (result is null)
        {
            return Results.NotFound();
        }

        if (result.IsFailure)
        {
            return Results.BadRequest(result.Error);
        }

        // 202, not 200 (task E16/F02/US03/T01, ADR-027 §D1): the re-run is queued, not done. The
        // parse/classify/embed summary the 200 used to carry (pagesParsed, chunksIndexed, the
        // re-derived documentType) belongs to the Worker now and is read back through
        // GET /api/documents like any first upload. The audit row (document.reprocessed, "queued")
        // is written by the service itself, inside the tenant scope the RLS-protected audit table
        // requires — see DocumentReprocessService.
        var queued = result.Value;
        return Results.Accepted($"/api/documents/{queued.DocumentId.Value}", new
        {
            documentId = queued.DocumentId.Value,
            extractionJobId = queued.ExtractionJobId.Value,
            processingStatus = DocumentProcessingStatus.Uploaded.ToString(),
        });
    }

    /// <summary>
    /// Task E13/F04/US01/T02 (R-DOC-10): Admin-only deletion of the blobs, the preview, the
    /// retrieval chunks and the rows; the contract survives with its document link gone. Writes one
    /// <c>document.deleted</c> audit row before returning 204.
    /// </summary>
    /// <summary>
    /// <c>POST /api/documents/{id}/prioritise</c> (task E16/F03/US02/T01, ADR-027 w15 footer C12,
    /// ADR-020 w15 footer 11; built by hand 2026-09-14). The web calls it once when a user opens a
    /// document that is still queued: the document's queued, unclaimed classification job is stamped
    /// <c>prioritised_at</c> and the Worker takes it at its next free slot in this tenant, ahead of the
    /// FIFO. Any live member may ask -- whoever opened the document is the one waiting for it, and
    /// it grants nothing but an order. <b>204</b> always when the document exists: already claimed,
    /// already prioritised or already terminal are all no-ops (the client calls this blindly on
    /// open); <b>404</b> when the document does not exist for this tenant (never 403, Rule B1);
    /// <b>400</b> for a non-GUID id. One <c>document.prioritised</c> audit row when it changed
    /// something, none otherwise.
    /// </summary>
    private static async Task<IResult> PrioritiseDocumentAsync(
        string id,
        HttpRequest request,
        DocumentPriorityService priorityService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;

        if (!Guid.TryParse(id, out var documentGuid))
        {
            return Results.BadRequest("The document id in the route must be a GUID.");
        }

        var outcome = await priorityService
            .PrioritiseAsync(tenantId, new EntityId(documentGuid), caller.Identity!, cancellationToken)
            .ConfigureAwait(false);

        return outcome is null ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> DeleteDocumentAsync(
        string id,
        HttpContext httpContext,
        DocumentDeleteService deleteService,
        WorkspaceRoleResolver roleResolver,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var request = httpContext.Request;
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;

        if (!Guid.TryParse(id, out var documentGuid))
        {
            return Results.BadRequest("The document id in the route must be a GUID.");
        }

        if (!await roleResolver.IsAdminAsync(httpContext, tenantId, cancellationToken))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await deleteService.DeleteAsync(
            tenantId, new EntityId(documentGuid), caller.Identity!, cancellationToken);
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

    /// <summary>
    /// Admin-only bulk wipe: every document in the tenant, then portfolio contracts those files
    /// built, renewal rows for those contracts, and Ask chats scoped to them. 204 even when the
    /// tenant already had nothing — idempotent. Procurement is 403, same gate as single delete.
    /// </summary>
    private static async Task<IResult> DeleteAllDocumentsAsync(
        HttpContext httpContext,
        DocumentPurgeAllService purgeService,
        WorkspaceRoleResolver roleResolver,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var request = httpContext.Request;
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantId = caller.TenantId;

        if (!await roleResolver.IsAdminAsync(httpContext, tenantId, cancellationToken))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        await purgeService.PurgeAsync(tenantId, caller.Identity!, cancellationToken).ConfigureAwait(false);
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
            $"Raffa accepts files up to {options.MaxFileBytes / (1024 * 1024)} MB. This file is larger.",
            statusCode: StatusCodes.Status413PayloadTooLarge);

    /// <summary>JPEG scans are stored as the original bytes; sniff so the browser decodes them.</summary>
    private static string PreviewMediaType(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF
            ? DocumentFormatSniffer.JpegMimeType
            : DocumentPreviewService.PreviewContentType;
}
