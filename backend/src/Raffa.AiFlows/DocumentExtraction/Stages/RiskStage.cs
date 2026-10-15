using Raffa.AiFlows.DocumentExtraction.Schemas;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>Risks.</summary>
internal sealed class RiskStage()
    : ExtractionStageBase(ExtractionStage.Risk, StagedExtractionJsonSchemas.Risks())
{
    /// <summary>F5-T05: the three scalar stages (used only when context dependencies are enabled).</summary>
    public override IReadOnlyList<ExtractionStage> DependsOn => ScalarStages;

    public override StageFacts Parse(string payloadJson) =>
        new StageFacts.Risks(StagePayload.Deserialize<ExtractedRisksPayload>(payloadJson)?.Items ?? []);
}
