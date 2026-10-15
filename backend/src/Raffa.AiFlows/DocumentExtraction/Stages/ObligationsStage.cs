using Raffa.AiFlows.DocumentExtraction.Schemas;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>Obligations.</summary>
internal sealed class ObligationsStage()
    : ExtractionStageBase(ExtractionStage.Obligations, StagedExtractionJsonSchemas.Obligations())
{
    public override StageFacts Parse(string payloadJson) =>
        new StageFacts.Obligations(StagePayload.Deserialize<ExtractedObligationsPayload>(payloadJson)?.Items ?? []);
}
