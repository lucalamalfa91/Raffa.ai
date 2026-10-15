using Raffa.AiFlows.DocumentExtraction.Schemas;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>Legal clauses.</summary>
internal sealed class LegalClausesStage()
    : ExtractionStageBase(ExtractionStage.LegalClauses, StagedExtractionJsonSchemas.Clauses())
{
    /// <summary>F5-T05: the three scalar stages (used only when context dependencies are enabled).</summary>
    public override IReadOnlyList<ExtractionStage> DependsOn => ScalarStages;

    public override StageFacts Parse(string payloadJson) =>
        new StageFacts.Clauses(StagePayload.Deserialize<ExtractedClausesPayload>(payloadJson)?.Items ?? []);
}
