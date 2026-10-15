using Raffa.AiFlows.DocumentExtraction.Schemas;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>Legal clauses.</summary>
internal sealed class LegalClausesStage()
    : ExtractionStageBase(ExtractionStage.LegalClauses, StagedExtractionJsonSchemas.Clauses())
{
    public override StageFacts Parse(string payloadJson) =>
        new StageFacts.Clauses(StagePayload.Deserialize<ExtractedClausesPayload>(payloadJson)?.Items ?? []);
}
