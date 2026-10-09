using Raffa.Documents.Contracts.Application.Admission;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// What the module's extraction handler needs from the document processing flow: apply an admission
/// verdict (the pages the gate already parsed and its classification) to the document, run staged
/// extraction and index the result. The handler depends on this port rather than on the
/// implementation, so the implementation can live outside the module.
/// <see cref="DocumentProcessingPipeline"/> is the implementation.
/// </summary>
public interface IDocumentProcessingFlow
{
    /// <summary>Pages + classification in. See the matching overload of
    /// <see cref="DocumentProcessingPipeline.ProcessAsync(TenantId, EntityId, IReadOnlyList{DocumentPageText}, DocumentClassification, ReadOnlyMemory{byte}, string?, string?, CancellationToken)"/>
    /// for the full contract.</summary>
    Task<Result<DocumentProcessingSummary>> ProcessAsync(
        TenantId tenantId,
        EntityId documentId,
        IReadOnlyList<DocumentPageText> pages,
        DocumentClassification classification,
        ReadOnlyMemory<byte> content = default,
        string? fileName = null,
        string? mimeType = null,
        CancellationToken cancellationToken = default);
}
