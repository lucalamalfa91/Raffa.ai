using Raffa.AiFlows.DocumentExtraction.Schemas;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>Price / SKU line items.</summary>
internal sealed class LineItemsStage()
    : ExtractionStageBase(ExtractionStage.LineItems, StagedExtractionJsonSchemas.LineItems())
{
    /// <summary>F5-T05: the three scalar stages (used only when context dependencies are enabled).</summary>
    public override IReadOnlyList<ExtractionStage> DependsOn => ScalarStages;

    public override StageFacts Parse(string payloadJson) =>
        new StageFacts.LineItems(StagePayload.Deserialize<ExtractedLineItemsPayload>(payloadJson)?.Items ?? []);
}
