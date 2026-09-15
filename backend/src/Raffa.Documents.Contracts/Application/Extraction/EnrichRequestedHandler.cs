using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Handles an <see cref="EnrichRequested"/> message: runs the full 7-stage
/// <see cref="StagedExtractionService"/> pipeline, links the supplier, and promotes the contract
/// to <see cref="ContractIdentityState.Official"/>.
///
/// <para>
/// This is the second half of the two-queue split (instant-identity-ingest). The intake handler
/// (<see cref="ExtractionRequestedHandler"/>) does the fast headline pass (seconds) and publishes
/// <see cref="EnrichRequested"/>; this handler does the slow 7-stage extract (minutes). The
/// Service Bus lock on the intake message therefore does not cover the 7-stage run.
/// </para>
///
/// <para>
/// <b>No-clobber rule</b>: if <see cref="Contract.Version"/> is greater than 1 (a human has
/// made corrections), this handler skips writing <see cref="Contract.ProvisionalSupplierName"/>
/// and <see cref="Contract.DisplayName"/>. StagedExtractionService's own concurrency token
/// (<see cref="Contract.Version"/>) handles concurrent writes correctly.
/// </para>
/// </summary>
public sealed class EnrichRequestedHandler(
    DocumentsContractsDbContext dbContext,
    IDocumentStorage storage,
    HybridDocumentParsingService parsingService,
    StagedExtractionService extractionService,
    EmbeddingRetrievalService embeddingRetrievalService,
    ITenantContext tenantContext,
    ILogger<EnrichRequestedHandler> logger,
    ISupplierResolver? supplierResolver = null)
{
    private const string SystemActor = "system:enrich";
    private const string DocumentSourceType = "Document";

    public async Task HandleAsync(EnrichRequested message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var tenantId = new TenantId(message.TenantId);
        var documentId = new EntityId(message.DocumentId);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var document = await dbContext.Documents
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            logger.LogInformation(
                "Enrich: document {DocumentId} not found for tenant; nothing to do", documentId.Value);
            return;
        }

        var bytes = await storage.LoadAsync(tenantId, document.StoragePath, cancellationToken)
            .ConfigureAwait(false);

        if (bytes is null || bytes.Length == 0)
        {
            logger.LogWarning(
                "Enrich: bytes for document {DocumentId} could not be loaded; routing to manual review",
                documentId.Value);
            await MarkNeedsReviewAsync(document, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Parse the document. For born-digital PDFs PdfPig already ran in the intake pass and
        // set the right type; we re-parse here because the pages are not stored anywhere.
        // OCR cost is incurred once (during intake) for scanned PDFs; native parse (PdfPig /
        // OpenXML) is cheap and re-running it here is fine.
        var parseResult = await parsingService
            .ParseAsync(document.FileName, document.MimeType, bytes, cancellationToken)
            .ConfigureAwait(false);

        if (parseResult.IsFailure)
        {
            logger.LogWarning(
                "Enrich: parse failed for document {DocumentId}: {Error}; routing to manual review",
                documentId.Value, parseResult.Error);
            await MarkNeedsReviewAsync(document, cancellationToken).ConfigureAwait(false);
            return;
        }

        var pages = parseResult.Value;

        document.ProcessingStatus = DocumentProcessingStatus.Processing;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Bug fix: if a human already corrected this contract (Version > 1), skip the 7-stage
        // extraction so we do not clobber their edits. Promote to Official and route to manual
        // review so the user can validate their own corrections through the review flow.
        // This is checked after re-parse (pages are needed for retrieval indexing) but before
        // RunAsync, which is the only call that would overwrite human-edited field values.
        if (document.ContractId is { } humanEditedContractId)
        {
            var humanEditedContract = await dbContext.Contracts
                .SingleOrDefaultAsync(
                    c => c.TenantId == tenantId && c.Id == humanEditedContractId, cancellationToken)
                .ConfigureAwait(false);

            if (humanEditedContract is not null && humanEditedContract.Version > 1)
            {
                logger.LogInformation(
                    "Enrich: contract {ContractId} for document {DocumentId} was already human-edited " +
                    "(Version {Version}); skipping extraction clobber and promoting to Official",
                    humanEditedContractId.Value, documentId.Value, humanEditedContract.Version);

                humanEditedContract.IdentityState = ContractIdentityState.Official;
                document.ProcessingStatus = DocumentProcessingStatus.NeedsReview;
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                // Index whatever pages we have so Ask Raffa can still search the document text.
                await IndexForRetrievalAsync(tenantId, document.Id, pages, cancellationToken)
                    .ConfigureAwait(false);

                logger.LogInformation(
                    "Enrich: document {DocumentId} preserved human edits → NeedsReview / Official",
                    documentId.Value);
                return;
            }
        }

        // Run the 7-stage pipeline. Pass null for classificationConfidence so the pipeline uses
        // the document's already-set DocumentType (from the intake classify step) without a second
        // classify LLM call.
        var extractionResult = await extractionService
            .RunAsync(tenantId, document.Id, pages, cancellationToken)
            .ConfigureAwait(false);

        if (extractionResult.IsFailure)
        {
            // RunAsync returns IsFailure only for structural failures (e.g. document deleted
            // mid-flight). Stage-level failures are surfaced as NeedsReview/Failed status inside
            // an IsSuccess result, so this branch is rare. Either way: do not leave the document
            // stuck in Processing — route to manual review.
            logger.LogWarning(
                "Enrich: staged extraction failed for document {DocumentId}: {Error}; routing to manual review",
                documentId.Value, extractionResult.Error);
            await MarkNeedsReviewAsync(document, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Supplier linking — same logic as DocumentProcessingPipeline.LinkSupplierAsync.
        await LinkSupplierAsync(tenantId, extractionResult.Value, cancellationToken).ConfigureAwait(false);

        // Promote to official — only if no human corrections have been made (Version == 1).
        await PromoteToOfficialAsync(tenantId, extractionResult.Value.ContractId, cancellationToken)
            .ConfigureAwait(false);

        // Index pages for Ask Raffa retrieval.
        await IndexForRetrievalAsync(tenantId, document.Id, pages, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Enrich: document {DocumentId} → official (contractId {ContractId})",
            documentId.Value, extractionResult.Value.ContractId.Value);
    }

    /// <summary>
    /// Sets <see cref="ContractIdentityState.Official"/> on the contract, respecting human
    /// corrections (Version > 1 means a human has edited the contract; do not overwrite).
    /// </summary>
    private async Task PromoteToOfficialAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken)
    {
        var contract = await dbContext.Contracts
            .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == contractId, cancellationToken)
            .ConfigureAwait(false);

        if (contract is null)
        {
            return;
        }

        // Never clobber human corrections: Version > 1 means someone edited this contract.
        // (Human edits that arrived before RunAsync are handled earlier in HandleAsync; this
        // guard is a safety net for corrections that race with the extraction run itself.)
        if (contract.Version > 1)
        {
            return; // human-edited — leave the identity state and fields as the user left them
        }

        contract.IdentityState = ContractIdentityState.Official;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Terminates the enrich run as <see cref="DocumentProcessingStatus.NeedsReview"/> so the
    /// document is surfaced to the user for manual data entry, rather than being left stuck in
    /// <see cref="DocumentProcessingStatus.Processing"/> indefinitely. The provisional identity
    /// set by the intake headline pass (filename / provisional supplier name) remains intact so
    /// the row is never a blank "unknown document" in the Documents list.
    /// </summary>
    private async Task MarkNeedsReviewAsync(Document document, CancellationToken cancellationToken)
    {
        document.ProcessingStatus = DocumentProcessingStatus.NeedsReview;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Enrich: document {DocumentId} marked NeedsReview for manual data entry",
            document.Id.Value);
    }

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

    private async Task<int> IndexForRetrievalAsync(
        TenantId tenantId, EntityId documentId, IReadOnlyList<DocumentPageText> pages, CancellationToken cancellationToken)
    {
        var indexed = 0;

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Text))
            {
                continue;
            }

            var indexResult = await embeddingRetrievalService
                .IndexChunkAsync(
                    tenantId,
                    DocumentSourceType,
                    documentId,
                    page.PageNumber - 1,
                    page.Text,
                    page.PageNumber,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);

            if (indexResult.IsSuccess)
            {
                indexed++;
            }
        }

        return indexed;
    }
}
