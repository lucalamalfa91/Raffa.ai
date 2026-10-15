using Raffa.AiFlows.DocumentExtraction.Schemas;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>Obligations.</summary>
internal sealed class ObligationsStage()
    : ExtractionStageBase(ExtractionStage.Obligations, StagedExtractionJsonSchemas.Obligations())
{
    /// <summary>F5-T05: the three scalar stages (used only when context dependencies are enabled).</summary>
    public override IReadOnlyList<ExtractionStage> DependsOn => ScalarStages;

    public override StageFacts Parse(string payloadJson) =>
        new StageFacts.Obligations(StagePayload.Deserialize<ExtractedObligationsPayload>(payloadJson)?.Items ?? []);
}
