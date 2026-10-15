using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>What a stage reads: the page-marked text of the whole document. <paramref name="Context"/> is
/// empty unless context dependencies are enabled (not the case today).</summary>
public sealed record ExtractionStageInput(
    string DocumentText,
    int PageCount,
    IReadOnlyDictionary<ExtractionStage, StageFacts> Context);
