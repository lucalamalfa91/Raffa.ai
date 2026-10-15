using Raffa.AiFlows.DocumentExtraction.Schemas;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>Base of the three scalar-field stages (their facts land on the Contract through evidence rows).</summary>
internal abstract class ScalarFactsStage(ExtractionStage stage, IReadOnlyList<string> allowedFields)
    : ExtractionStageBase(stage, StagedExtractionJsonSchemas.Facts(allowedFields))
{
    public override StageFacts Parse(string payloadJson) =>
        new StageFacts.Scalars(Stage, StagePayload.Deserialize<ExtractedFactsPayload>(payloadJson)?.Facts ?? []);
}
