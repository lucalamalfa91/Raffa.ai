using Raffa.Documents.Contracts.Domain;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Shape of the staged extraction pipeline that code outside it needs to know (the document list
/// and the validator decide "partial" from the jobs of these stages).
/// </summary>
public static class ExtractionPipeline
{
    /// <summary>AC-1's seven stages, in pipeline order. <see cref="ExtractionStage.Classification"/>
    /// is deliberately excluded — it is queued and (eventually) consumed elsewhere, before the
    /// pipeline ever runs (see <see cref="StagedExtractionService"/>'s doc comment).</summary>
    public static IReadOnlyList<ExtractionStage> Stages { get; } =
    [
        ExtractionStage.Metadata,
        ExtractionStage.CommercialTerms,
        ExtractionStage.DatesAndRenewalTerms,
        ExtractionStage.LineItems,
        ExtractionStage.LegalClauses,
        ExtractionStage.Obligations,
        ExtractionStage.Risk,
    ];
}
