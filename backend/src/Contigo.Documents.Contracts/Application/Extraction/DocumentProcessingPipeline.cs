using Contigo.AiGateway;
using Contigo.AiGateway.Contracts;
using Contigo.Documents.Contracts.Application.Admission;
using Contigo.Documents.Contracts.Application.Preview;
using Contigo.Documents.Contracts.Domain;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Suppliers;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Contigo.Documents.Contracts.Application.Extraction;

/// <summary>
/// Task E02/F06/US01/T01 (r1-integration): the caller <see cref="HybridDocumentParsingService"/>'s
/// and <see cref="StagedExtractionService"/>'s own doc comments both name as missing —
/// <c>HybridDocumentParsingService</c>: "Wiring 'load the Document row, read its bytes from
/// storage, call this, then call StagedExtractionService' into an HTTP endpoint or the Worker's
/// queue dispatch is later-task scope"; <c>StagedExtractionService.EnsureContractAsync</c>:
/// "nothing else in this wave consumes the <see cref="ExtractionStage.Classification"/> job
/// <c>DocumentUploadService</c> queues at upload". This is that later task: one orchestrator that
/// runs the rest of product spec §7.1's pipeline — classify, then hybrid parse's page-mapped text
/// into staged extraction, then indexes the result for Ask Contigo retrieval — so a document
/// uploaded on `dev`/`demo` is actually searchable and extracted afterward, not stuck at
/// "Uploaded" forever (parent story us-01-final-integration AC-1: "Upload -&gt; parse/OCR -&gt;
/// classify -&gt; extract -&gt; portfolio -&gt; 360 -&gt; Ask Contigo ... works end-to-end").
///
/// <b>Two entry points, one continuation</b> (task E13/F04/US01/T01, documents-admission):
/// <list type="bullet">
/// <item><see cref="ProcessAsync(TenantId, EntityId, string, string, ReadOnlyMemory{byte}, CancellationToken)"/>
/// — bytes in: parse, classify, then extract + index. The original R1 shape, still the right one
/// for a re-run over stored bytes (reprocess) and for any caller that has not classified yet.</item>
/// <item><see cref="ProcessAsync(TenantId, EntityId, IReadOnlyList{DocumentPageText}, DocumentClassification, CancellationToken)"/>
/// — pages + classification in: the admission gate (<see cref="DocumentAdmissionGate"/>) has
/// already parsed and classified the upload before anything was persisted (ADR-024 "gate before
/// persistence"), so <c>POST /api/documents</c> continues from that verdict and the classify role
/// is called exactly once per upload. The queued <see cref="Domain.ExtractionJob"/> row is
/// advanced with the gate's own verdict/metadata, exactly as the in-pipeline classify would have
/// done.</item>
/// </list>
///
/// <b>Ordering</b>: classify runs <em>after</em> the hybrid parse, not before, even though
/// <see cref="ExtractionStage.Classification"/> is declared as the "zeroth" pipeline stage
/// (<see cref="ExtractionStage"/>'s own doc comment). <see cref="AiClassificationRequest.DocumentText"/>
/// is "native or OCR'd text of the document" — classification has nothing to read until the hybrid
/// parse (native or `ocr` gateway role) has already produced it. <see cref="Document.DocumentType"/>
/// is set, and its own <see cref="Domain.ExtractionJob"/> row is resolved, before
/// <see cref="StagedExtractionService.RunAsync"/> is called: that service's own
/// <c>EnsureContractAsync</c> seeds a freshly-created <see cref="Contract"/>'s
/// <see cref="Contract.Type"/> from <see cref="Document.DocumentType"/>, so classification must be
/// durable first (both entry points flush it via <c>SaveChangesAsync</c> before calling
/// <see cref="StagedExtractionService.RunAsync"/> — sharing the caller's own scoped
/// <see cref="DocumentsContractsDbContext"/>, so <c>RunAsync</c>'s own re-query for the
/// <see cref="Document"/> row returns the same tracked, already-updated entity rather than racing a
/// second connection).
///
/// <b>Bytes in, not a storage re-read</b>: the bytes overload takes <c>content</c> directly rather
/// than loading it back through <c>Contigo.SharedKernel.Storage.IDocumentStorage</c> — that
/// interface exposes no read/load method today (only <c>SaveAsync</c>; see its own doc comment),
/// and adding one is a larger, separate change to a shared abstraction every module and both hosts
/// depend on. The callers already hold the uploaded bytes in memory for
/// <c>DocumentUploadService.UploadAsync</c>'s own storage write, so passing the same buffer here
/// avoids the extra round trip entirely rather than working around a missing read API.
///
/// <b>Synchronous, in-request, not a queue dispatch</b>: <c>Contigo.Worker.Queue
/// .QueueConsumerHostedService</c> deliberately does not dispatch a received message to a domain
/// handler yet (its own doc comment: "is a later task once that handler exists"), and nothing in
/// this codebase enqueues a durable message for <c>InMemoryQueueConsumer</c> to receive either —
/// <see cref="Domain.ExtractionJob"/> rows are written directly by <c>DocumentUploadService</c>,
/// never posted to <c>Contigo.Worker.Queue.IQueueConsumer</c>. Building that real async dispatch
/// (a durable queue producer/consumer pair) is a Worker feature in its own right, not this
/// integration task's scope. Running the rest of the pipeline synchronously, inline with the
/// upload request, is the smallest honest way to make R1's "upload -&gt; ... -&gt; Ask Contigo"
/// promise actually true on `dev`/`demo` today without redesigning the queue architecture — a
/// documented interim choice, not a silently absorbed shortcut (OQ-askv2-007 keeps it in force for
/// V2). A later task can move this call behind a real durable queue without changing either
/// signature or behaviour.
///
/// <b>Never fails an already-durable upload</b>: every failure this type can report (parse
/// failure, one extraction stage failing, one page failing to embed) is recorded on the
/// <see cref="Document"/>/<see cref="Domain.ExtractionJob"/> rows and returned to the caller, but
/// none of it unwinds the upload itself — the bytes are already safely stored and the document row
/// already exists by the time this runs (mirrors <see cref="StagedExtractionService"/>'s own
/// per-stage "one failure does not abort the others" posture, generalized one layer up).
///
/// <b>Supplier linking</b> (task E13/F03/US01/T02, requirements R-SUP-01/R-SUP-02): once staged
/// extraction reports an accepted <c>supplier</c> fact
/// (<see cref="StagedExtractionSummary.AcceptedSupplierName"/>), this pipeline turns that legal name
/// into <see cref="Contract.SupplierId"/> through <see cref="ISupplierResolver"/>. It happens here,
/// not in <see cref="StagedExtractionService"/>, because ADR-002 forbids Documents/Contracts from
/// referencing <c>Contigo.Suppliers.Products</c> — the port lives in SharedKernel and this is the
/// module's own orchestration layer. <paramref name="supplierResolver"/> is optional (defaulted to
/// <see langword="null"/>, so the built-in container supplies it only where the Suppliers module is
/// composed in, and unit tests that construct this type directly need not know about it at all): a
/// host without that module keeps extracting exactly as before, contracts simply carry no supplier
/// link.
/// </summary>
public sealed class DocumentProcessingPipeline(
    DocumentsContractsDbContext dbContext,
    IAiGateway aiGateway,
    HybridDocumentParsingService parsingService,
    StagedExtractionService extractionService,
    EmbeddingRetrievalService embeddingRetrievalService,
    ITenantContext tenantContext,
    IClock clock,
    DocumentPreviewService? previewService = null,
    ISupplierResolver? supplierResolver = null)
{
    /// <summary>Discriminator this pipeline indexes every chunk under (<see cref="Domain.Embedding.SourceType"/>),
    /// matching <c>Contigo.Api.ChatEndpointExtensions.ToEvidenceSnippet</c>'s own
    /// <c>"Document"</c> literal so a resulting Ask Contigo citation's composite id
    /// (<c>{SourceType}:{SourceId}</c>) resolves back to this <see cref="Document"/>.</summary>
    private const string DocumentSourceType = "Document";

    /// <summary>Same threshold and same reasoning as <see cref="StagedExtractionService.LowConfidenceThreshold"/>
    /// (documented separately, not shared, because the two run against independent
    /// <see cref="Domain.ExtractionJob"/> rows on different <see cref="ExtractionStage"/> values —
    /// there is no single shared constant to reference without one service reaching into the
    /// other's private state): below this, classification is a proposal a human should confirm,
    /// not a trusted fact (product principle: "Human-in-the-loop for consequential decisions").
    /// </summary>
    private const double LowConfidenceThreshold = 0.6;

    /// <summary>
    /// Bytes in: hybrid parse → classify (gateway call) → staged extraction → Ask Contigo indexing.
    /// A parse failure marks the document <see cref="DocumentProcessingStatus.Failed"/> (an honest
    /// terminal state — not "still processing") and is returned; see the type doc comment.
    /// </summary>
    public async Task<Result<DocumentProcessingSummary>> ProcessAsync(
        TenantId tenantId,
        EntityId documentId,
        string fileName,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var document = await LoadDocumentAsync(tenantId, documentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return NotFound(documentId);
        }

        var parseResult = await parsingService
            .ParseAsync(fileName, mimeType, content, cancellationToken)
            .ConfigureAwait(false);
        if (parseResult.IsFailure)
        {
            // Honest terminal state: a document whose bytes could not be read at all (neither
            // natively nor via OCR) is not "still processing" — Failed, not silently left at
            // Uploaded, so the portfolio/360 surfaces do not imply extraction is still in flight.
            document.ProcessingStatus = DocumentProcessingStatus.Failed;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<DocumentProcessingSummary>.Failure(parseResult.Error);
        }

        var pages = parseResult.Value;
        var (documentType, classificationConfidence) = await ClassifyAsync(tenantId, document, pages, cancellationToken)
            .ConfigureAwait(false);

        return await ExtractAndIndexAsync(
            tenantId, document, pages, documentType, classificationConfidence, content, mimeType, fileName, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Pages + classification in (task E13/F04/US01/T01): the admission gate already parsed and
    /// classified this upload, so this overload only applies that verdict to the
    /// <see cref="Document"/> and its queued <see cref="ExtractionStage.Classification"/> job —
    /// no second parse, no second classify call — then runs staged extraction and indexing exactly
    /// as the bytes overload does.
    /// </summary>
    public async Task<Result<DocumentProcessingSummary>> ProcessAsync(
        TenantId tenantId,
        EntityId documentId,
        IReadOnlyList<DocumentPageText> pages,
        DocumentClassification classification,
        ReadOnlyMemory<byte> content = default,
        string? fileName = null,
        string? mimeType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(classification);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var document = await LoadDocumentAsync(tenantId, documentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return NotFound(documentId);
        }

        var classificationJob = await FindQueuedClassificationJobAsync(tenantId, document, cancellationToken)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
        document.DocumentType = classification.DocumentType;
        if (classificationJob is not null)
        {
            classificationJob.StartedAt = now;
            classificationJob.ModelId = classification.Metadata.ModelId;
            classificationJob.Status = classification.Confidence < LowConfidenceThreshold
                ? ExtractionJobStatus.NeedsReview
                : ExtractionJobStatus.Completed;
            classificationJob.CompletedAt = now;
        }

        return await ExtractAndIndexAsync(
            tenantId, document, pages, classification.DocumentType, classification.Confidence,
            content, mimeType ?? document.MimeType, fileName ?? document.FileName, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Result<DocumentProcessingSummary> NotFound(EntityId documentId) =>
        Result<DocumentProcessingSummary>.Failure($"Document {documentId} was not found for this tenant.");

    private Task<Document?> LoadDocumentAsync(TenantId tenantId, EntityId documentId, CancellationToken cancellationToken) =>
        dbContext.Documents
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, cancellationToken);

    /// <summary>
    /// The continuation both entry points share once <paramref name="document"/>'s type is decided:
    /// flush classification, run <see cref="StagedExtractionService.RunAsync"/>, index every page
    /// for retrieval, report the summary.
    /// </summary>
    private async Task<Result<DocumentProcessingSummary>> ExtractAndIndexAsync(
        TenantId tenantId,
        Document document,
        IReadOnlyList<DocumentPageText> pages,
        ContractDocumentType documentType,
        double? classificationConfidence,
        ReadOnlyMemory<byte> content,
        string mimeType,
        string fileName,
        CancellationToken cancellationToken)
    {
        // R-DOC-06's list column: what the parse really produced, recorded before extraction so a
        // later stage failing still leaves an honest page count behind.
        document.PageCount = pages.Count;

        // R-DOC-08: render and store the first-page preview from the bytes we already hold. Never
        // fatal - DocumentPreviewService returns null instead of throwing, and a document with no
        // preview simply answers 404 on that endpoint (see that type's own doc comment).
        if (previewService is not null && !content.IsEmpty)
        {
            var previewPath = await previewService
                .RenderAndStoreAsync(tenantId, document.Id, fileName, mimeType, content, cancellationToken)
                .ConfigureAwait(false);
            if (previewPath is not null)
            {
                document.PreviewPath = previewPath;
            }
        }

        // Flush classification before StagedExtractionService.RunAsync runs — see the type doc
        // comment's "Ordering" remarks: EnsureContractAsync reads document.DocumentType to seed a
        // freshly-created Contract.Type, and both services share this same scoped DbContext
        // instance, so this SaveChangesAsync only needs to make the *classification job* durable;
        // the in-memory `document` entity RunAsync re-queries is already the identical, already-
        // updated tracked instance regardless.
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var extractionResult = await extractionService
            .RunAsync(tenantId, document.Id, pages, cancellationToken)
            .ConfigureAwait(false);
        if (extractionResult.IsFailure)
        {
            return Result<DocumentProcessingSummary>.Failure(extractionResult.Error);
        }

        await LinkSupplierAsync(tenantId, extractionResult.Value, cancellationToken).ConfigureAwait(false);

        var chunksIndexed = await IndexForRetrievalAsync(tenantId, document.Id, pages, cancellationToken)
            .ConfigureAwait(false);

        return Result<DocumentProcessingSummary>.Success(new DocumentProcessingSummary(
            document.Id,
            extractionResult.Value.ContractId,
            documentType,
            classificationConfidence,
            extractionResult.Value.DocumentProcessingStatus,
            pages.Count,
            chunksIndexed));
    }

    /// <summary>
    /// Task E13/F03/US01/T02: turns an accepted <c>supplier</c> fact into
    /// <see cref="Contract.SupplierId"/> (requirements R-SUP-01/R-SUP-02, parent story AC-3). Runs
    /// on every processing pass, including a re-run over an already-extracted document, so
    /// re-processing a contract stored before this feature existed back-fills its supplier link
    /// (R-SUP-03) without a bespoke migration job.
    ///
    /// <para>
    /// Four no-ops, each deliberate: no resolver composed in (a host without the Suppliers module —
    /// see the type doc comment), no accepted supplier fact (absent, or below the critical-field
    /// bar — that document is already in <c>needs_review</c> with the fact's evidence, and a human
    /// correction re-resolves it), a resolver failure (this pipeline never fails an already-durable
    /// upload — see the type doc comment's own "Never fails an already-durable upload" remark), and
    /// an unchanged link (no pointless <c>UPDATE</c> against
    /// <see cref="Contract.Version"/>'s concurrency token on every re-processing pass).
    /// </para>
    /// </summary>
    private async Task LinkSupplierAsync(
        TenantId tenantId, StagedExtractionSummary summary, CancellationToken cancellationToken)
    {
        if (supplierResolver is null || summary.AcceptedSupplierName is not { } supplierName)
        {
            return;
        }

        var resolved = await supplierResolver
            .ResolveAsync(tenantId, supplierName, cancellationToken)
            .ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return;
        }

        var contract = await dbContext.Contracts
            .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == summary.ContractId, cancellationToken)
            .ConfigureAwait(false);
        if (contract is null || contract.SupplierId == resolved.Value.Id)
        {
            return;
        }

        contract.SupplierId = resolved.Value.Id;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the `classify` gateway role against the just-parsed text and applies the result onto
    /// <paramref name="document"/> in memory (not yet saved — see the type doc comment's "Ordering"
    /// remarks). Resolves and updates the <see cref="Domain.ExtractionJob"/> row
    /// <c>DocumentUploadService</c> already queued at upload time (<see cref="ExtractionStage.Classification"/>,
    /// <see cref="ExtractionJobStatus.Queued"/>) — the same "advance the queued row to completion"
    /// shape a real Worker dispatch would use, rather than inserting a second, redundant job row.
    /// A gateway failure (for example empty parsed text) leaves <paramref name="document"/>'s
    /// <see cref="Document.DocumentType"/> at its current value (the pre-classification default,
    /// <see cref="ContractDocumentType.Other"/>, for a first run) and marks the job
    /// <see cref="ExtractionJobStatus.Failed"/> — classification is one stage among several; its
    /// failure must not abort staged extraction (mirrors <see cref="StagedExtractionService"/>'s
    /// own per-stage failure posture).
    /// </summary>
    private async Task<(ContractDocumentType DocumentType, double? Confidence)> ClassifyAsync(
        TenantId tenantId, Document document, IReadOnlyList<DocumentPageText> pages, CancellationToken cancellationToken)
    {
        var classificationJob = await FindQueuedClassificationJobAsync(tenantId, document, cancellationToken)
            .ConfigureAwait(false);

        var startedAt = clock.UtcNow;
        if (classificationJob is not null)
        {
            classificationJob.Status = ExtractionJobStatus.Running;
            classificationJob.StartedAt = startedAt;
        }

        var classificationText = BuildClassificationText(pages);
        var classifyResult = await aiGateway
            .ClassifyAsync(new AiClassificationRequest(classificationText), cancellationToken)
            .ConfigureAwait(false);
        var completedAt = clock.UtcNow;

        if (classifyResult.IsFailure)
        {
            if (classificationJob is not null)
            {
                classificationJob.Status = ExtractionJobStatus.Failed;
                classificationJob.ErrorDetail = Truncate(classifyResult.Error);
                classificationJob.CompletedAt = completedAt;
            }

            return (document.DocumentType, null);
        }

        var mappedType = ContractDocumentTypeMap.FromAi(classifyResult.Value.DocumentType);
        document.DocumentType = mappedType;

        if (classificationJob is not null)
        {
            classificationJob.ModelId = classifyResult.Value.Metadata.ModelId;
            classificationJob.Status = classifyResult.Value.Confidence < LowConfidenceThreshold
                ? ExtractionJobStatus.NeedsReview
                : ExtractionJobStatus.Completed;
            classificationJob.CompletedAt = completedAt;
        }

        return (mappedType, classifyResult.Value.Confidence);
    }

    private Task<ExtractionJob?> FindQueuedClassificationJobAsync(
        TenantId tenantId, Document document, CancellationToken cancellationToken) =>
        dbContext.ExtractionJobs
            .Where(j => j.TenantId == tenantId
                && j.DocumentId == document.Id
                && j.Stage == ExtractionStage.Classification
                && j.Status == ExtractionJobStatus.Queued)
            .OrderBy(j => j.QueuedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Representative text for the classify role (<see cref="AiClassificationRequest.DocumentText"/>:
    /// "the full text of the document (or a representative prefix)") — every page, in order, so a
    /// multi-page contract's type is judged on all of it, not on a cover page alone.</summary>
    private static string BuildClassificationText(IReadOnlyList<DocumentPageText> pages) =>
        string.Join("\n\n", pages.Select(p => p.Text));

    /// <summary>
    /// Indexes every non-blank page as one retrieval chunk (<c>chunkIndex</c> = zero-based page
    /// number) under <see cref="DocumentSourceType"/>, so Ask Contigo citations resolve back to
    /// this document. One page failing to embed is counted out, never fatal — the document is
    /// still partially askable, which is more honest than "not askable at all".
    /// </summary>
    private async Task<int> IndexForRetrievalAsync(
        TenantId tenantId, EntityId documentId, IReadOnlyList<DocumentPageText> pages, CancellationToken cancellationToken)
    {
        var sectionsByPage = await SectionLabelsByPageAsync(tenantId, documentId, cancellationToken)
            .ConfigureAwait(false);
        var indexed = 0;
        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Text))
            {
                continue;
            }

            // R-EVD-01 / R-DOC-07 AC-2: the chunk carries its own 1-based page (and, when the
            // document has been sectioned, the section label). chunkIndex stays zero-based - it is
            // a position within the source, not a page number, and Ask's own citation ids already
            // depend on it.
            var indexResult = await embeddingRetrievalService
                .IndexChunkAsync(
                    tenantId,
                    DocumentSourceType,
                    documentId,
                    page.PageNumber - 1,
                    page.Text,
                    page.PageNumber,
                    sectionsByPage.GetValueOrDefault(page.PageNumber),
                    cancellationToken)
                .ConfigureAwait(false);
            if (indexResult.IsSuccess)
            {
                indexed++;
            }
        }

        return indexed;
    }

    /// <summary>
    /// Section label per page for the chunks of this document (R-EVD-01 "section label"): the
    /// clause types staged extraction has already attributed to a page of this same document. A
    /// page with several clauses is labelled with the first, in insertion order - one short label
    /// is what a citation subtitle can show; a page with none stays unlabelled rather than being
    /// given a made-up section name.
    /// </summary>
    private async Task<Dictionary<int, string>> SectionLabelsByPageAsync(
        TenantId tenantId, EntityId documentId, CancellationToken cancellationToken)
    {
        var clauses = await dbContext.Clauses
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.SourceDocumentId == documentId && c.SourcePage != null)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new { Page = c.SourcePage!.Value, c.ClauseType, c.SourceSpan })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var labels = new Dictionary<int, string>();
        foreach (var clause in clauses)
        {
            var label = string.IsNullOrWhiteSpace(clause.SourceSpan) ? clause.ClauseType : clause.SourceSpan;
            if (!string.IsNullOrWhiteSpace(label))
            {
                labels.TryAdd(clause.Page, label);
            }
        }

        return labels;
    }

    private static string Truncate(string value, int maxLength = 1000) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
